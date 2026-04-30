"""
Transcription Routes

Endpoints for audio transcription using Whisper or other ASR engines.
Supports multiple languages, word timestamps, and diarization.
"""

from __future__ import annotations

import asyncio
import json
import logging
import uuid
from collections.abc import Coroutine
from datetime import datetime
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, Field

from backend.api.deps import TrackStoreDep
from backend.core.exceptions import ServiceError
from backend.data.repositories.job_repository import (
    JobEntity,
    JobStatus,
    JobType,
    get_job_repository,
)
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.ml.models.model_preflight import PreflightError as MLPreflightError
from backend.services.canonical_job_lifecycle import (
    complete_job,
    create_job,
    fail_job,
    mark_job_running,
    update_job_progress,
)
from backend.services.model_preflight import PreflightError as ServicePreflightError
from backend.services.transcription_service import (
    TranscriptionRequest,
    build_simulation_transcript,
    transcribe_audio,
)
from backend.services.transcription_service import (
    get_supported_languages as get_supported_languages_svc,
)
from backend.services.transcription_service import (
    list_transcription_engines as list_transcription_engines_svc,
)

from ..models import ApiOk, VoiceStudioBaseModel
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/transcribe", tags=["transcribe"])

# Strong references so scheduled background coroutines are not GC'd mid-flight.
_BACKGROUND_TASKS: set[asyncio.Task[Any]] = set()


def _fire_and_track(coro: Coroutine[Any, Any, Any]) -> None:
    """Schedule a background coroutine and retain the task until it completes."""
    task = asyncio.create_task(coro)
    _BACKGROUND_TASKS.add(task)
    task.add_done_callback(_BACKGROUND_TASKS.discard)


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


def _job_blocker_str(detail: object) -> str:
    """Normalize preflight/service detail to a single string for job.blocker."""
    if isinstance(detail, str):
        return detail
    try:
        return json.dumps(detail, default=str)
    except (TypeError, ValueError):
        return str(detail)


class TranscriptionJobRequest(VoiceStudioBaseModel):
    """Explicit simulation or real transcription under a single job contract."""

    audio_id: str
    engine: str = "whisper"
    language: str | None = None
    word_timestamps: bool = False
    simulate: bool = False
    async_mode: bool = False


class TranscriptionJobResponse(VoiceStudioBaseModel):
    """Transcription job outcome with simulation / availability metadata."""

    job_id: str
    audio_id: str
    transcript_id: str | None = None
    status: str
    mode: str
    is_simulated: bool
    real_transcription_performed: bool
    blocker: str | None = None
    transcript: TranscriptionResponse | None = None
    progress: float | None = None


async def _merge_transcription_job_metadata(job_id: str, patch: dict[str, object]) -> None:
    """Merge keys into job_history.metadata for transcription_job domain jobs."""
    jrepo = get_job_repository()
    entity = await jrepo.get_by_id(job_id)
    if not entity:
        return
    meta = entity.get_metadata()
    meta.update(patch)
    entity.set_metadata(meta)
    await jrepo.update(job_id, {"metadata": entity.metadata})


def _transcription_job_metadata_base(
    request: TranscriptionJobRequest,
    project_id: str | None,
) -> dict[str, object]:
    return {
        "domain": "transcription_job",
        "audio_id": request.audio_id,
        "simulate": request.simulate,
        "engine": request.engine,
        "language": request.language or "",
        "project_id": project_id or "",
        "transcription_status": "pending",
        "mode": "pending",
        "is_simulated": False,
        "real_transcription_performed": False,
        "blocker": None,
    }


async def _run_transcription_job_bg(
    job_id: str,
    request: TranscriptionJobRequest,
    project_id: str | None,
) -> None:
    """Execute transcription job work after async POST accepted (canonical job_history)."""
    repo = get_transcription_repository()
    try:
        await mark_job_running(job_id)
        await update_job_progress(job_id, 0.1, current_step="transcribing")
        await _merge_transcription_job_metadata(
            job_id,
            {
                "transcription_status": "running",
                "mode": "simulation" if request.simulate else "real",
            },
        )

        if request.simulate:
            transcript_id = str(uuid.uuid4())
            payload = build_simulation_transcript(
                transcript_id=transcript_id,
                audio_id=request.audio_id,
                job_id=job_id,
                language=(request.language or "en"),
            )
            if project_id:
                payload["project_id"] = project_id
            if not payload.get("segments"):
                await _merge_transcription_job_metadata(
                    job_id,
                    {
                        "transcription_status": "failed",
                        "mode": "simulation",
                        "is_simulated": True,
                        "real_transcription_performed": False,
                        "blocker": "Simulation produced no segments.",
                    },
                )
                await fail_job(job_id, "EMPTY_TRANSCRIPT: Simulation produced no segments.")
                return
            await repo.store_transcription(payload)
            stored = await repo.get_transcription(transcript_id)
            if stored is None:
                logger.error("Async simulation stored but transcript %s not readable", transcript_id)
                await _merge_transcription_job_metadata(
                    job_id,
                    {
                        "transcription_status": "failed",
                        "mode": "simulation",
                        "is_simulated": True,
                        "real_transcription_performed": False,
                        "blocker": "Transcription persistence failed after simulation",
                    },
                )
                await fail_job(job_id, "Transcription persistence failed after simulation")
                return
            await _merge_transcription_job_metadata(
                job_id,
                {
                    "transcription_status": "completed",
                    "mode": "simulation",
                    "is_simulated": True,
                    "real_transcription_performed": False,
                    "blocker": None,
                },
            )
            await complete_job(job_id, result_id=transcript_id)
            return

        try:
            result = await transcribe_audio(
                TranscriptionRequest(
                    audio_id=request.audio_id,
                    engine=request.engine,
                    language=request.language,
                    word_timestamps=request.word_timestamps,
                    diarization=False,
                    use_vad=False,
                ),
                project_id=project_id,
            )
        except (MLPreflightError, ServicePreflightError) as e:
            blocker = _job_blocker_str(e.detail)
            await _merge_transcription_job_metadata(
                job_id,
                {
                    "transcription_status": "unavailable",
                    "mode": "unavailable",
                    "is_simulated": False,
                    "real_transcription_performed": False,
                    "blocker": blocker,
                },
            )
            await fail_job(job_id, blocker)
            return
        except ServiceError as e:
            blocker = _job_blocker_str(e.detail)
            await _merge_transcription_job_metadata(
                job_id,
                {
                    "transcription_status": "failed",
                    "mode": "real",
                    "is_simulated": False,
                    "real_transcription_performed": False,
                    "blocker": blocker,
                },
            )
            await fail_job(job_id, blocker)
            return

        await update_job_progress(job_id, 0.95, current_step="persisting")
        await _merge_transcription_job_metadata(
            job_id,
            {
                "transcription_status": "completed",
                "mode": "real",
                "is_simulated": False,
                "real_transcription_performed": True,
                "blocker": None,
            },
        )
        await complete_job(job_id, result_id=result.id)
    except Exception as e:
        logger.error("Async transcription job %s failed: %s", job_id, e, exc_info=True)
        await _merge_transcription_job_metadata(
            job_id,
            {
                "transcription_status": "failed",
                "mode": "real",
                "is_simulated": False,
                "real_transcription_performed": False,
                "blocker": str(e),
            },
        )
        await fail_job(job_id, str(e))


def _job_entity_to_transcription_job_response(entity: JobEntity) -> TranscriptionJobResponse:
    """Map canonical JobEntity (domain transcription_job) to TranscriptionJobResponse."""
    meta = entity.get_metadata()
    if meta.get("domain") != "transcription_job":
        raise ValueError("not a transcription_job entity")

    audio_id = str(meta.get("audio_id", ""))
    transcript_id = entity.result_id
    ts = meta.get("transcription_status")
    if isinstance(ts, str):
        api_status = ts
    elif entity.status == JobStatus.COMPLETED.value:
        api_status = "completed"
    elif entity.status == JobStatus.FAILED.value:
        api_status = "unavailable" if meta.get("mode") == "unavailable" else "failed"
    elif entity.status == JobStatus.RUNNING.value:
        api_status = "running"
    else:
        api_status = "pending"

    mode = str(meta.get("mode") or api_status)
    is_simulated = bool(meta.get("is_simulated", False))
    real_tp = bool(meta.get("real_transcription_performed", False))
    blocker_raw = meta.get("blocker")
    blocker_out: str | None = None
    if isinstance(blocker_raw, str):
        blocker_out = blocker_raw
    elif blocker_raw is not None:
        blocker_out = str(blocker_raw)
    if blocker_out is None and entity.error and api_status in ("failed", "unavailable"):
        blocker_out = entity.error

    progress_val: float | None = None
    if entity.progress is not None:
        progress_val = float(entity.progress)

    return TranscriptionJobResponse(
        job_id=entity.id,
        audio_id=audio_id,
        transcript_id=transcript_id,
        status=api_status,
        mode=mode,
        is_simulated=is_simulated,
        real_transcription_performed=real_tp,
        blocker=blocker_out,
        transcript=None,
        progress=progress_val,
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
    except (MLPreflightError, ServicePreflightError) as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)
    except Exception as e:
        logger.error("Transcription error: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Transcription failed: {e!s}")


@router.post("/jobs", response_model=TranscriptionJobResponse)
async def transcribe_job_route(
    request: TranscriptionJobRequest,
    project_id: str | None = Query(None, description="Project ID to associate transcription with"),
):
    """
    Run transcription as a job with explicit simulation and availability metadata.

    Always returns ``TranscriptionJobResponse`` for contract outcomes; HTTP 422
    is used only for invalid simulation payloads (e.g. empty segments).
    """
    job_id = str(uuid.uuid4())
    repo = get_transcription_repository()

    if request.async_mode:
        if request.simulate:
            probe = build_simulation_transcript(
                transcript_id="__probe__",
                audio_id=request.audio_id,
                job_id=job_id,
                language=(request.language or "en"),
            )
            if not probe.get("segments"):
                raise HTTPException(
                    status_code=422,
                    detail={
                        "code": "EMPTY_TRANSCRIPT",
                        "message": "Simulation produced no segments.",
                    },
                )
        meta = _transcription_job_metadata_base(request, project_id)
        await create_job(
            job_id,
            JobType.TRANSCRIPTION.value,
            "Transcription job",
            metadata=meta,
        )
        _fire_and_track(_run_transcription_job_bg(job_id, request, project_id))
        return TranscriptionJobResponse(
            job_id=job_id,
            audio_id=request.audio_id,
            transcript_id=None,
            status="pending",
            mode="pending",
            is_simulated=request.simulate,
            real_transcription_performed=False,
            blocker=None,
            transcript=None,
            progress=0.0,
        )

    if request.simulate:
        transcript_id = str(uuid.uuid4())
        payload = build_simulation_transcript(
            transcript_id=transcript_id,
            audio_id=request.audio_id,
            job_id=job_id,
            language=(request.language or "en"),
        )
        if project_id:
            payload["project_id"] = project_id
        if not payload.get("segments"):
            raise HTTPException(
                status_code=422,
                detail={
                    "code": "EMPTY_TRANSCRIPT",
                    "message": "Simulation produced no segments.",
                },
            )
        await repo.store_transcription(payload)
        stored = await repo.get_transcription(transcript_id)
        if stored is None:
            logger.error("Simulation stored but transcript %s not readable", transcript_id)
            raise HTTPException(status_code=500, detail="Transcription persistence failed after simulation")
        transcript = TranscriptionResponse(**stored)
        return TranscriptionJobResponse(
            job_id=job_id,
            audio_id=request.audio_id,
            transcript_id=transcript_id,
            status="completed",
            mode="simulation",
            is_simulated=True,
            real_transcription_performed=False,
            blocker=None,
            transcript=transcript,
        )

    try:
        result = await transcribe_audio(
            TranscriptionRequest(
                audio_id=request.audio_id,
                engine=request.engine,
                language=request.language,
                word_timestamps=request.word_timestamps,
                diarization=False,
                use_vad=False,
            ),
            project_id=project_id,
        )
    except (MLPreflightError, ServicePreflightError) as e:
        return TranscriptionJobResponse(
            job_id=job_id,
            audio_id=request.audio_id,
            transcript_id=None,
            status="unavailable",
            mode="unavailable",
            is_simulated=False,
            real_transcription_performed=False,
            blocker=_job_blocker_str(e.detail),
            transcript=None,
        )
    except ServiceError as e:
        return TranscriptionJobResponse(
            job_id=job_id,
            audio_id=request.audio_id,
            transcript_id=None,
            status="failed",
            mode="real",
            is_simulated=False,
            real_transcription_performed=False,
            blocker=_job_blocker_str(e.detail),
            transcript=None,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error("Transcription job error: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Transcription failed: {e!s}") from e

    transcript = _result_to_response(result)
    return TranscriptionJobResponse(
        job_id=job_id,
        audio_id=request.audio_id,
        transcript_id=result.id,
        status="completed",
        mode="real",
        is_simulated=False,
        real_transcription_performed=True,
        blocker=None,
        transcript=transcript,
    )


@router.get("/jobs/{job_id}", response_model=TranscriptionJobResponse)
async def get_transcription_job_status(job_id: str):
    """Poll durable transcription job status (canonical job_history, domain transcription_job)."""
    jrepo = get_job_repository()
    entity = await jrepo.get_by_id(job_id)
    if entity is None:
        raise HTTPException(status_code=404, detail="Transcription job not found")
    meta = entity.get_metadata()
    if meta.get("domain") != "transcription_job":
        raise HTTPException(status_code=404, detail="Transcription job not found")
    try:
        return _job_entity_to_transcription_job_response(entity)
    except ValueError:
        raise HTTPException(status_code=404, detail="Transcription job not found") from None


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

    _fire_and_track(
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
