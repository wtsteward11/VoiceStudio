"""
Transcription Routes

Endpoints for audio transcription using Whisper or other ASR engines.
Supports multiple languages, word timestamps, and diarization.
"""

from __future__ import annotations

import logging
import asyncio
import os
import uuid
from datetime import datetime
from pathlib import Path
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from backend.core.circuit_breaker import get_engine_breaker
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.ml.models.engine_service import get_engine_service
from backend.ml.models.model_preflight import PreflightError, ensure_whisper_cpp
from backend.security.path_validator import PathValidationError, get_path_validator

from ..models import ApiOk
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/transcribe", tags=["transcribe"])

_transcriptions: dict[str, dict[str, Any]] = {}
_state_lock = asyncio.Lock()


# STT engine via EngineService (ADR-008 compliant)
STT_ENGINE_AVAILABLE = False

try:
    _engine_service = get_engine_service()
    engines = _engine_service.list_engines()
    STT_ENGINE_AVAILABLE = len(engines) > 0
    if STT_ENGINE_AVAILABLE:
        logger.info(f"EngineService available with {len(engines)} engines for transcription")
    else:
        logger.warning("No engines available for transcription")
except Exception as e:
    logger.warning(f"EngineService not available for transcription: {e}")
    STT_ENGINE_AVAILABLE = False


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
    speaker: str | None = None  # Set when engine supports diarization (e.g. WhisperX)


class TranscriptionRequest(BaseModel):
    """Request for transcription."""

    audio_id: str
    engine: str = "whisper"  # whisper, whisper_cpp, whisperx, vosk
    language: str | None = None  # Auto-detect if None
    word_timestamps: bool = False
    diarization: bool = False  # Speaker diarization (WhisperX only)
    use_vad: bool = False  # Use voice activity detection
    model_path: str | None = None  # override for whisper_cpp gguf


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


@router.get("/languages", response_model=list[SupportedLanguage])
@cache_response(ttl=600)  # Cache for 10 minutes (supported languages are static)
async def get_supported_languages():
    """Get list of supported languages for transcription."""
    # If engine router is available, try to get languages from registered engine
    if STT_ENGINE_AVAILABLE and engine_router:
        try:
            # Try to get whisper_cpp (preferred) or whisper engine (fallback)
            whisper_engine = None
            for engine_name in ("whisper_cpp", "whisper"):
                try:
                    whisper_engine = engine_router.get_engine(engine_name, gpu=True)
                    if whisper_engine:
                        break
                except Exception:
                    whisper_engine = None

            if whisper_engine and hasattr(whisper_engine, "get_supported_languages"):
                supported = whisper_engine.get_supported_languages()
                # Map language codes to names
                language_names = {
                    "auto": "Auto-detect",
                    "en": "English",
                    "es": "Spanish",
                    "fr": "French",
                    "de": "German",
                    "it": "Italian",
                    "pt": "Portuguese",
                    "ru": "Russian",
                    "ja": "Japanese",
                    "ko": "Korean",
                    "zh": "Chinese",
                    "ar": "Arabic",
                    "hi": "Hindi",
                    "nl": "Dutch",
                    "pl": "Polish",
                    "tr": "Turkish",
                    "sv": "Swedish",
                    "no": "Norwegian",
                    "fi": "Finnish",
                    "da": "Danish",
                }

                return [
                    SupportedLanguage(code=code, name=language_names.get(code, code.title()))
                    for code in supported
                ]
        except Exception as e:
            logger.debug(f"Failed to get languages from engine: {e}")

    # Fallback to common languages
    languages = [
        {"code": "auto", "name": "Auto-detect"},
        {"code": "en", "name": "English"},
        {"code": "es", "name": "Spanish"},
        {"code": "fr", "name": "French"},
        {"code": "de", "name": "German"},
        {"code": "it", "name": "Italian"},
        {"code": "pt", "name": "Portuguese"},
        {"code": "ru", "name": "Russian"},
        {"code": "ja", "name": "Japanese"},
        {"code": "ko", "name": "Korean"},
        {"code": "zh", "name": "Chinese"},
        {"code": "ar", "name": "Arabic"},
        {"code": "hi", "name": "Hindi"},
        {"code": "nl", "name": "Dutch"},
        {"code": "pl", "name": "Polish"},
        {"code": "tr", "name": "Turkish"},
        {"code": "sv", "name": "Swedish"},
        {"code": "no", "name": "Norwegian"},
        {"code": "fi", "name": "Finnish"},
        {"code": "da", "name": "Danish"},
    ]
    return languages


class TranscriptionEngine(BaseModel):
    """Information about an available transcription engine."""

    id: str
    name: str
    description: str = ""
    supports_word_timestamps: bool = True
    supports_diarization: bool = False
    supports_vad: bool = False


# GAP-CS-003: Add engines endpoint for dynamic discovery
@router.get("/engines", response_model=list[TranscriptionEngine])
@cache_response(ttl=300)  # Cache for 5 minutes
async def list_transcription_engines():
    """
    List available transcription (STT) engines.

    GAP-CS-003: Enables dynamic engine discovery for the frontend.
    """
    engines: list[TranscriptionEngine] = []

    # Get engines from EngineService
    if STT_ENGINE_AVAILABLE:
        try:
            engine_service = get_engine_service()
            all_engines = engine_service.list_engines()

            # Filter to STT-capable engines
            stt_types = {"stt", "transcription", "speech_recognition", "whisper"}
            for eng in all_engines:
                eng_type = eng.get("type", "").lower()
                eng_id = eng.get("id", eng.get("name", ""))
                eng_name = eng.get("name", eng_id)

                # Include if type matches or name suggests STT
                is_stt = eng_type in stt_types or any(
                    s in eng_id.lower() for s in ["whisper", "vosk", "stt", "transcri"]
                )

                if is_stt:
                    engines.append(
                        TranscriptionEngine(
                            id=eng_id,
                            name=eng_name,
                            description=eng.get("description", ""),
                            supports_word_timestamps=eng.get("supports_word_timestamps", True),
                            supports_diarization=eng.get("supports_diarization", False),
                            supports_vad=eng.get("supports_vad", False),
                        )
                    )
        except Exception as e:
            logger.warning(f"Failed to get engines from EngineService: {e}")

    # Fallback: return known engines if none discovered
    if not engines:
        engines = [
            TranscriptionEngine(
                id="whisper_cpp",
                name="Whisper.cpp",
                description="OpenAI Whisper ported to C++ for CPU inference",
                supports_word_timestamps=True,
                supports_diarization=False,
                supports_vad=False,
            ),
            TranscriptionEngine(
                id="whisper",
                name="Whisper",
                description="OpenAI Whisper for speech recognition",
                supports_word_timestamps=True,
                supports_diarization=False,
                supports_vad=False,
            ),
            TranscriptionEngine(
                id="vosk",
                name="Vosk",
                description="Offline speech recognition toolkit",
                supports_word_timestamps=True,
                supports_diarization=False,
                supports_vad=True,
            ),
        ]

    return engines


@router.post("/", response_model=TranscriptionResponse)
async def transcribe_audio(
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
        transcription_id = str(uuid.uuid4())
        # Ensure whisper.cpp model is present (auto-download if allowed)
        if request.engine == "whisper_cpp":
            ensure_whisper_cpp(auto_download=True)

        # Get engine instance via EngineService (ADR-008 compliant)
        stt_engine = None
        engine_service = get_engine_service()

        # Try to get from EngineService first
        if STT_ENGINE_AVAILABLE:
            try:
                valid_engines = engine_service.list_engines()
                engine_names = [e.get("id", e.get("name", "")) for e in valid_engines]

                if request.engine in engine_names:
                    stt_engine = engine_service.get_engine(request.engine)
                elif valid_engines:
                    logger.warning(
                        f"Engine '{request.engine}' not in EngineService. "
                        f"Available: {engine_names}. Will try direct creation."
                    )
            except Exception as e:
                logger.debug(f"Could not get engine from EngineService: {e}")

        # If not found, try to get via EngineService accessors (ADR-008 compliant)
        if not stt_engine and request.engine in ("whisper_cpp", "whisper"):
            try:
                logger.info(f"Getting {request.engine} engine via EngineService")
                stt_engine = engine_service.get_whisper_engine()
                if not stt_engine:
                    raise HTTPException(status_code=503, detail=f"Engine {request.engine} not available")
            except Exception as e:
                logger.error(f"Whisper engine not available: {e}.")
                raise HTTPException(
                    status_code=503,
                    detail=(
                        f"Transcription engine '{request.engine}' is not available. "
                        "Please ensure the engine is properly installed. "
                        "Install with: pip install faster-whisper==1.0.3"
                    ),
                )
        elif not stt_engine:
            # Engine not available - return proper error
            logger.error(f"Engine '{request.engine}' not available.")
            raise HTTPException(
                status_code=503,
                detail=(
                    f"Transcription engine '{request.engine}' is not available. "
                    "Please ensure the engine is properly installed and configured."
                ),
            )

        # Get audio file path from audio_id using helper function
        from .audio import _get_audio_path

        audio_path = _get_audio_path(request.audio_id)

        # If not found and project_id provided, try project audio specifically
        if not audio_path and project_id:
            try:
                from .projects import _ensure_project_dir

                project_dir = _ensure_project_dir(project_id)
                audio_dir = os.path.join(project_dir, "audio")

                # Check if audio_id matches a filename in project audio directory
                if os.path.exists(audio_dir):
                    # Validate audio_id to prevent path traversal
                    path_validator = get_path_validator()
                    try:
                        # Sanitize and validate the user-provided audio_id
                        safe_audio_id = path_validator.sanitize(request.audio_id)
                        potential_path = os.path.join(audio_dir, safe_audio_id)

                        # Verify the resolved path stays within audio_dir
                        resolved = Path(potential_path).resolve()
                        audio_dir_resolved = Path(audio_dir).resolve()
                        if not str(resolved).startswith(str(audio_dir_resolved)):
                            logger.warning(f"Path traversal attempt blocked: {request.audio_id}")
                            raise PathValidationError(
                                "Invalid audio path", request.audio_id, "traversal"
                            )

                        if os.path.exists(potential_path):
                            audio_path = potential_path
                        else:
                            # Try matching by filename (without extension)
                            for filename in os.listdir(audio_dir):
                                base_name = os.path.splitext(filename)[0]
                                if base_name == safe_audio_id or filename == safe_audio_id:
                                    audio_path = os.path.join(audio_dir, filename)
                                    break
                    except PathValidationError as e:
                        logger.warning(
                            f"Path validation failed for audio_id '{request.audio_id}': {e}"
                        )
                        raise HTTPException(status_code=400, detail="Invalid audio identifier")
            except HTTPException:
                raise
            except Exception as e:
                logger.debug(f"Could not load from project audio: {e}")

        # Final check - if still no audio path, raise error
        if not audio_path or not os.path.exists(audio_path):
            error_msg = f"Audio file not found for audio_id: {request.audio_id}. "
            if project_id:
                error_msg += f"Checked project '{project_id}' audio directory. "
            error_msg += "Please ensure the audio has been synthesized or uploaded first."
            raise HTTPException(status_code=404, detail=error_msg)

        # Transcribe using Whisper engine
        logger.info(f"Transcribing audio: {audio_path} with engine: {request.engine}")

        # Circuit breaker (TD-014): fail fast if engine is unavailable
        engine_breaker = get_engine_breaker(request.engine)
        if not engine_breaker.allow_request():
            logger.warning(
                "Circuit breaker OPEN for engine '%s', retry in %.1fs",
                request.engine,
                engine_breaker.time_until_retry(),
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    f"Transcription engine '{request.engine}' is temporarily unavailable. "
                    f"Retry in {int(engine_breaker.time_until_retry())} seconds."
                ),
            )

        # Ensure engine is initialized
        if not stt_engine.is_initialized():
            logger.info("Initializing Whisper engine...")
            stt_engine.initialize()

        # Prepare language (handle "auto" as None for auto-detection)
        language = request.language
        if language == "auto" or language == "":
            language = None

        # Transcribe audio (with circuit breaker success/failure recording)
        try:
            if request.engine == "whisper_cpp":
                result = stt_engine.transcribe(
                    audio=audio_path,
                    language=language,
                    word_timestamps=request.word_timestamps,
                    output_format="json",
                )
            elif request.engine == "whisperx" and hasattr(stt_engine, "transcribe"):
                result = stt_engine.transcribe(
                    audio=audio_path,
                    language=language,
                    word_timestamps=request.word_timestamps,
                    diarization=request.diarization,
                )
            else:
                result = stt_engine.transcribe(
                    audio=audio_path,
                    language=language,
                    word_timestamps=request.word_timestamps,
                )
        except Exception:
            engine_breaker.record_failure()
            raise
        engine_breaker.record_success()

        # Normalize legacy return shapes (some engines return plain text).
        if result is None:
            engine_breaker.record_failure()
            raise HTTPException(
                status_code=500,
                detail=f"Transcription failed for engine '{request.engine}'",
            )
        if isinstance(result, str):
            result = {
                "text": result,
                "segments": [],
                "language": language or "unknown",
                "word_timestamps": [],
            }

        # Convert result to response format (include speaker when present, e.g. WhisperX diarization)
        segments = [
            TranscriptionSegment(
                text=seg["text"],
                start=seg["start"],
                end=seg["end"],
                words=(
                    [
                        WordTimestamp(
                            word=w["word"],
                            start=w["start"],
                            end=w["end"],
                            confidence=w.get("probability"),
                        )
                        for w in result.get("word_timestamps", [])
                        if seg["start"] <= w["start"] < seg["end"]
                    ]
                    if request.word_timestamps
                    else None
                ),
                speaker=seg.get("speaker"),
            )
            for seg in result["segments"]
        ]

        # Flatten word timestamps if requested
        word_timestamps = []
        if request.word_timestamps and "word_timestamps" in result:
            word_timestamps = [
                WordTimestamp(
                    word=w["word"],
                    start=w["start"],
                    end=w["end"],
                    confidence=w.get("probability"),
                )
                for w in result["word_timestamps"]
            ]

        transcription = TranscriptionResponse(
            id=transcription_id,
            audio_id=request.audio_id,
            text=result["text"],
            language=result["language"],
            duration=result.get("duration", 0.0),
            segments=segments,
            word_timestamps=word_timestamps,
            created=datetime.utcnow(),
            engine=request.engine,
        )

        # Persist transcription to database
        transcription_data = transcription.model_dump()
        if project_id:
            transcription_data["project_id"] = project_id

        repo = get_transcription_repository()
        await repo.store_transcription(transcription_data)

        logger.info(
            f"Transcription complete: {transcription_id}, "
            f"language={transcription.language}, "
            f"duration={transcription.duration:.2f}s"
        )
        return transcription

    except HTTPException:
        raise
    except PreflightError as e:
        # Convert service-layer preflight error to HTTP exception
        raise HTTPException(status_code=e.status_code, detail=e.detail)
    except Exception as e:
        logger.error(f"Transcription error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Transcription failed: {e!s}")


@router.get("/{transcription_id}", response_model=TranscriptionResponse)
@cache_response(ttl=300)  # Cache for 5 minutes (transcription results are static)
async def get_transcription(transcription_id: str):
    """Get transcription by ID."""
    repo = get_transcription_repository()
    data = await repo.get_transcription(transcription_id)
    if data is None:
        raise HTTPException(status_code=404, detail="Transcription not found")

    return TranscriptionResponse(**data)


@router.get("/", response_model=list[TranscriptionResponse])
@cache_response(ttl=30)  # Cache for 30 seconds (transcription list may change frequently)
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
