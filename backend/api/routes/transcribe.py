"""
Transcription Routes

Endpoints for audio transcription using Whisper or other ASR engines.
Supports multiple languages, word timestamps, and diarization.
"""

from __future__ import annotations

import logging
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

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
