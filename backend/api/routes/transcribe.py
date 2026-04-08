"""
Transcription Routes

Endpoints for audio transcription using Whisper or other ASR engines.
Supports multiple languages, word timestamps, and diarization.
"""

from __future__ import annotations

import asyncio
import logging
import uuid
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, Field

from backend.api.deps import TrackStoreDep
from backend.data.repositories.job_repository import JobType
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.ml.models.model_preflight import PreflightError
from backend.services.transcription_service import (
    TranscriptionRequest,
    transcribe_audio,
)
from backend.services.transcription_service import (
    get_supported_languages as get_supported_languages_svc,
)
from backend.services.transcription_service import (
    list_transcription_engines as list_transcription_engines_svc,
)

from ..models import ApiOk
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/transcribe", tags=["transcribe"])


class WordTimestamp(BaseModel):
    """Word with timestamp information."""

    word: str
    start: float
    end: float
    confidence: float | None = None


class TranscriptionSegment(BaseModel):
    """Segment of transcription with timestamps and optional speaker (diarization)."""

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    text: str
    start: float
    end: float
    words: list[WordTimestamp] | None = None
    speaker: str | None = None


class TranscriptionResponse(BaseModel):
    """Response from transcription."""

    id: str
    audio_id: str
    text: str
    language: str
    duration: float
    segments: list[TranscriptionSegment]
    word_timestamps: list[WordTimestamp]
    created: datetime
    engine: str


class SupportedLanguage(BaseModel):
    """Supported language for transcription."""

    code: str
    name: str


class TranscriptionEngine(BaseModel):
    """Information about an available transcription engine."""

    id: str
    name: str
    description: str = ""
    supports_word_timestamps: bool = True
    supports_diarization: bool = False
    supports_vad: bool = False


@router.get("/languages", response_model=list[SupportedLanguage])
@cache_response(ttl=600)
async def get_supported_languages():
    """Get list of supported languages for transcription."""
    languages = get_supported_languages_svc()
    return [SupportedLanguage(code=l["code"], name=l["name"]) for l in languages]


@router.get("/engines", response_model=list[TranscriptionEngine])
@cache_response(ttl=300)
async def list_transcription_engines():
    """List available transcription (STT) engines."""
    engines = list_transcription_engines_svc()
    return [
        TranscriptionEngine(
            id=e["id"],
            name=e["name"],
            description=e.get("description", ""),
            supports_word_timestamps=e.get("supports_word_timestamps", True),
            supports_diarization=e.get("supports_diarization", False),
            supports_vad=e.get("supports_vad", False),
        )
        for e in engines
    ]


def _result_to_response(result) -> TranscriptionResponse:
    """Convert TranscriptionResult to TranscriptionResponse."""
    segments = [
        TranscriptionSegment(
            id=s.id,
            text=s.text,
            start=s.start,
            end=s.end,
            words=(
                [
                    WordTimestamp(
                        word=w.get("word", ""),
                        start=w.get("start", 0),
                        end=w.get("end", 0),
                        confidence=w.get("confidence") or w.get("probability"),
                    )
                    for w in (s.words or [])
                ]
                if s.words
                else None
            ),
            speaker=s.speaker,
        )
        for s in result.segments
    ]
    word_timestamps = [
        WordTimestamp(
            word=w.get("word", ""),
            start=w.get("start", 0),
            end=w.get("end", 0),
            confidence=w.get("probability"),
        )
        for w in result.word_timestamps
    ]
    return TranscriptionResponse(
        id=result.id,
        audio_id=result.audio_id,
        text=result.text,
        language=result.language,
        duration=result.duration,
        segments=segments,
        word_timestamps=word_timestamps,
        created=result.created,
        engine=result.engine,
    )


@router.post("/", response_model=TranscriptionResponse)
async def transcribe_audio_route(
    request: TranscriptionRequest,
    project_id: str | None = Query(None, description="Project ID to associate transcription with"),
):
    """
    Transcribe audio file using Whisper or other STT engines.

    Steps:
    1. Load audio file from audio_id (via audio API)
    2. Use Whisper/WhisperX/other engine to transcribe
    3. Return transcription with timestamps
    """
    try:
        result = await transcribe_audio(request, project_id=project_id)
        return _result_to_response(result)
    except HTTPException:
        raise
    except PreflightError as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)
    except Exception as e:
        logger.error("Transcription error: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Transcription failed: {e!s}")


@router.get("/{transcription_id}", response_model=TranscriptionResponse)
@cache_response(ttl=300)
async def get_transcription(transcription_id: str):
    """Get transcription by ID."""
    repo = get_transcription_repository()
    data = await repo.get_transcription(transcription_id)
    if data is None:
        raise HTTPException(status_code=404, detail="Transcription not found")

    return TranscriptionResponse(**data)


@router.get("/", response_model=list[TranscriptionResponse])
@cache_response(ttl=30)
async def list_transcriptions(
    audio_id: str | None = Query(None, description="Filter by audio ID"),
    project_id: str | None = Query(None, description="Filter by project ID"),
):
    """List transcriptions, optionally filtered by audio ID or project ID."""
    repo = get_transcription_repository()
    transcriptions = await repo.list_transcriptions(
        audio_id=audio_id,
        project_id=project_id,
    )

    return [TranscriptionResponse(**t) for t in transcriptions]


class RegenerateSegmentRequest(BaseModel):
    """GAP-046: start async regeneration for one linked segment (canonical job + synthesis)."""

    project_id: str = Field(..., min_length=1)
    track_id: str = Field(..., min_length=1)
    clip_id: str = Field(..., min_length=1)
    transcription_id: str = Field(..., min_length=1)
    segment_id: str = Field(..., min_length=1)
    replacement_text: str | None = Field(
        default=None,
        description="If set, synthesize this text; otherwise use current segment text from storage.",
    )
    profile_id: str | None = Field(
        default=None,
        description="Override clip profile; defaults to clip.profile_id from track store.",
    )
    engine: str | None = Field(default=None, description="TTS engine id override.")


class RegenerateSegmentAcceptedResponse(BaseModel):
    job_id: str
    status: str = "pending"


async def _validate_regenerate_segment_request(
    body: RegenerateSegmentRequest,
    track_store: object,
) -> str:
    """Return resolved profile_id or raise HTTPException."""
    repo = get_transcription_repository()
    tdata = await repo.get_transcription(body.transcription_id)
    if not tdata:
        raise HTTPException(
            status_code=404,
            detail={"code": "TRANSCRIPTION_NOT_FOUND", "message": "Transcription not found."},
        )
    segs = tdata.get("segments") or []
    seg = next((s for s in segs if str(s.get("id", "")) == body.segment_id), None)
    if seg is None:
        raise HTTPException(
            status_code=400,
            detail={"code": "SEGMENT_NOT_FOUND", "message": "Segment not found on transcription."},
        )

    track_data = track_store.get_track(body.project_id, body.track_id)
    if track_data is None:
        raise HTTPException(
            status_code=404,
            detail={"code": "TRACK_NOT_FOUND", "message": "Track not found."},
        )
    clips = track_data.get("clips") or []
    clip = next((c for c in clips if str(c.get("id", "")) == body.clip_id), None)
    if clip is None:
        raise HTTPException(
            status_code=404,
            detail={"code": "CLIP_NOT_FOUND", "message": "Clip not found on track."},
        )

    prof = (body.profile_id or clip.get("profile_id") or "").strip()
    if not prof:
        raise HTTPException(
            status_code=400,
            detail={"code": "PROFILE_REQUIRED", "message": "Clip has no profile_id; provide profile_id."},
        )

    text = (body.replacement_text if body.replacement_text is not None else seg.get("text") or "").strip()
    if not text:
        raise HTTPException(
            status_code=400,
            detail={"code": "EMPTY_TEXT", "message": "Nothing to synthesize (empty segment text)."},
        )
    return prof


@router.post(
    "/regenerate-segment",
    response_model=RegenerateSegmentAcceptedResponse,
    status_code=202,
)
async def start_regenerate_segment(
    body: RegenerateSegmentRequest,
    track_store: TrackStoreDep,
):
    """GAP-046: queue single-segment regeneration (canonical job + synthesis pipeline)."""
    from backend.services.canonical_job_lifecycle import create_job
    from backend.services.transcript_segment_regeneration import (
        run_transcript_segment_regeneration_job,
    )

    profile_id = await _validate_regenerate_segment_request(body, track_store)
    job_id = str(uuid.uuid4())
    await create_job(
        job_id,
        JobType.SYNTHESIS.value,
        "Transcript segment regenerate",
        metadata={
            "domain": "transcript_regenerate_segment",
            "project_id": body.project_id,
            "track_id": body.track_id,
            "clip_id": body.clip_id,
            "transcription_id": body.transcription_id,
            "segment_id": body.segment_id,
        },
    )

    asyncio.create_task(
        run_transcript_segment_regeneration_job(
            job_id,
            project_id=body.project_id,
            track_id=body.track_id,
            clip_id=body.clip_id,
            transcription_id=body.transcription_id,
            segment_id=body.segment_id,
            replacement_text=body.replacement_text,
            profile_id=profile_id,
            engine=body.engine,
            track_store=track_store,
        )
    )
    return RegenerateSegmentAcceptedResponse(job_id=job_id)


class TranscriptionUpdateRequest(BaseModel):
    """Request to update a transcription's text or segments."""

    text: str | None = None
    segments: list[dict] | None = None
    word_timestamps: list[dict] | None = None


@router.put("/{transcription_id}", response_model=TranscriptionResponse)
async def update_transcription(transcription_id: str, request: TranscriptionUpdateRequest):
    """
    Update a transcription's text and/or segments.

    Allows editing transcript text after initial transcription,
    modifying segment boundaries, or correcting word timestamps.
    """
    repo = get_transcription_repository()
    updated = await repo.update_transcription(
        transcription_id=transcription_id,
        text=request.text,
        segments=request.segments,
        word_timestamps=request.word_timestamps,
    )
    if updated is None:
        raise HTTPException(status_code=404, detail="Transcription not found")

    return TranscriptionResponse(**updated)


@router.delete("/{transcription_id}", response_model=ApiOk)
async def delete_transcription(transcription_id: str):
    """Delete transcription."""
    repo = get_transcription_repository()
    deleted = await repo.delete_transcription(transcription_id)
    if not deleted:
        raise HTTPException(status_code=404, detail="Transcription not found")
    return ApiOk()
