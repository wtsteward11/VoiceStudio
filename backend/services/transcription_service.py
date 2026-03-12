"""
Transcription service: canonical entry point for audio transcription.

M4: Full transcription logic lives here. Routes call TranscriptionService.transcribe_audio
instead of containing engine selection, inference, and storage logic.
Other routes (dubbing, assistant_run) call via this service without route-to-route imports.
"""

from __future__ import annotations

import logging
import os
import uuid
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

from pydantic import BaseModel

from backend.core.circuit_breaker import get_engine_breaker
from backend.core.exceptions import ServiceError
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.ml.models.engine_service import get_engine_service
from backend.ml.models.model_preflight import PreflightError, ensure_whisper_cpp
from backend.security.path_validator import PathValidationError, get_path_validator

logger = logging.getLogger(__name__)

# Engine availability (lazy, set on first use)
_STT_ENGINE_AVAILABLE: bool | None = None


def _is_stt_available() -> bool:
    """Check if STT engines are available via EngineService."""
    global _STT_ENGINE_AVAILABLE
    if _STT_ENGINE_AVAILABLE is not None:
        return _STT_ENGINE_AVAILABLE
    try:
        engine_service = get_engine_service()
        engines = engine_service.list_engines()
        _STT_ENGINE_AVAILABLE = len(engines) > 0
        if _STT_ENGINE_AVAILABLE:
            logger.info("EngineService available with %d engines for transcription", len(engines))
        else:
            logger.warning("No engines available for transcription")
    except Exception as e:
        logger.warning("EngineService not available for transcription: %s", e)
        _STT_ENGINE_AVAILABLE = False
    return _STT_ENGINE_AVAILABLE or False


class TranscriptionRequest(BaseModel):
    """Request for transcription."""

    audio_id: str
    engine: str = "whisper"
    language: str | None = None
    word_timestamps: bool = False
    diarization: bool = False
    use_vad: bool = False
    model_path: str | None = None


@dataclass
class TranscriptionSegmentResult:
    """Segment of transcription with timestamps."""

    text: str
    start: float
    end: float
    words: list[dict[str, Any]] | None = None
    speaker: str | None = None


@dataclass
class TranscriptionResult:
    """Result of transcription for programmatic use."""

    id: str
    audio_id: str
    text: str
    language: str
    duration: float
    segments: list[TranscriptionSegmentResult]
    word_timestamps: list[dict[str, Any]]
    created: datetime
    engine: str

    def to_dict(self) -> dict[str, Any]:
        """Convert to API response dict."""
        return {
            "id": self.id,
            "audio_id": self.audio_id,
            "text": self.text,
            "language": self.language,
            "duration": self.duration,
            "segments": [
                {
                    "text": s.text,
                    "start": s.start,
                    "end": s.end,
                    "words": s.words,
                    "speaker": s.speaker,
                }
                for s in self.segments
            ],
            "word_timestamps": self.word_timestamps,
            "created": self.created,
            "engine": self.engine,
        }


async def _resolve_audio_path(
    audio_id: str,
    project_id: str | None,
) -> str | None:
    """Resolve audio file path from audio_id, with library asset and project fallback."""
    from backend.services.audio_path_resolver import resolve_audio_path
    from backend.services.project_service import ensure_project_dir

    # 1. Standard resolution (AudioRegistry, upload dirs, project dirs)
    audio_path = resolve_audio_path(audio_id)
    if audio_path and os.path.exists(str(audio_path)):
        return str(audio_path)

    # 2. Library asset lookup (audio_id from POST /api/library/assets/upload)
    try:
        from backend.data.repositories.library_repository import get_library_asset_repository

        asset_repo = get_library_asset_repository()
        entity = await asset_repo.get_by_id(audio_id)
        if entity and entity.path and os.path.exists(entity.path):
            return entity.path
    except Exception as e:
        logger.debug("Library asset lookup failed for audio_id %s: %s", audio_id, e)

    # 3. Project audio directory fallback
    if not project_id:
        return None

    try:
        project_dir = ensure_project_dir(project_id)
        audio_dir = os.path.join(project_dir, "audio")
        if not os.path.exists(audio_dir):
            return None

        path_validator = get_path_validator()
        safe_audio_id = path_validator.sanitize(audio_id)
        potential_path = os.path.join(audio_dir, safe_audio_id)
        resolved = Path(potential_path).resolve()
        audio_dir_resolved = Path(audio_dir).resolve()
        if not str(resolved).startswith(str(audio_dir_resolved)):
            logger.warning("Path traversal attempt blocked: %s", audio_id)
            return None

        if os.path.exists(potential_path):
            return potential_path

        for filename in os.listdir(audio_dir):
            base_name = os.path.splitext(filename)[0]
            if base_name == safe_audio_id or filename == safe_audio_id:
                return os.path.join(audio_dir, filename)
    except (PathValidationError, Exception) as e:
        logger.debug("Could not load from project audio: %s", e)

    return None


def _normalize_segments(
    result: dict[str, Any],
    request: TranscriptionRequest,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Convert engine result to segments and word_timestamps."""
    segments = []
    for seg in result.get("segments", []):
        seg_dict: dict[str, Any] = {
            "text": seg["text"],
            "start": seg["start"],
            "end": seg["end"],
            "speaker": seg.get("speaker"),
        }
        if request.word_timestamps and "word_timestamps" in result:
            words = [
                {
                    "word": w["word"],
                    "start": w["start"],
                    "end": w["end"],
                    "confidence": w.get("probability"),
                }
                for w in result["word_timestamps"]
                if seg["start"] <= w["start"] < seg["end"]
            ]
            seg_dict["words"] = words
        segments.append(seg_dict)

    word_timestamps = []
    if request.word_timestamps and "word_timestamps" in result:
        word_timestamps = [
            {
                "word": w["word"],
                "start": w["start"],
                "end": w["end"],
                "confidence": w.get("probability"),
            }
            for w in result["word_timestamps"]
        ]

    return segments, word_timestamps


async def transcribe_audio(
    request: TranscriptionRequest,
    project_id: str | None = None,
) -> TranscriptionResult:
    """
    Transcribe audio file using Whisper or other STT engines.

    Returns TranscriptionResult for programmatic use (dubbing, assistant_run) and route response construction.
    Raises PreflightError, ServiceError, or generic Exception.
    """
    transcription_id = str(uuid.uuid4())

    if request.engine == "whisper_cpp":
        ensure_whisper_cpp(auto_download=True)

    engine_service = get_engine_service()
    stt_engine = None

    if _is_stt_available():
        try:
            valid_engines = engine_service.list_engines()
            engine_names = [e.get("id", e.get("name", "")) for e in valid_engines]
            if request.engine in engine_names:
                stt_engine = engine_service.get_engine(request.engine)
            elif valid_engines:
                logger.warning(
                    "Engine '%s' not in EngineService. Available: %s. Will try direct creation.",
                    request.engine,
                    engine_names,
                )
        except Exception as e:
            logger.debug("Could not get engine from EngineService: %s", e)

    if not stt_engine and request.engine in ("whisper_cpp", "whisper"):
        try:
            logger.info("Getting %s engine via EngineService", request.engine)
            stt_engine = engine_service.get_whisper_engine()
            if not stt_engine:
                raise ServiceError(503, f"Engine {request.engine} not available")
        except ServiceError:
            raise
        except Exception as e:
            logger.error("Whisper engine not available: %s", e)
            raise ServiceError(
                503,
                (
                    f"Transcription engine '{request.engine}' is not available. "
                    "Please ensure the engine is properly installed. "
                    "Install with: pip install faster-whisper==1.0.3"
                ),
            )
    elif not stt_engine:
        raise ServiceError(
            503,
            (
                f"Transcription engine '{request.engine}' is not available. "
                "Please ensure the engine is properly installed and configured."
            ),
        )

    audio_path = await _resolve_audio_path(request.audio_id, project_id)
    if not audio_path or not os.path.exists(audio_path):
        error_msg = f"Audio file not found for audio_id: {request.audio_id}. "
        if project_id:
            error_msg += f"Checked project '{project_id}' audio directory. "
        error_msg += "Please ensure the audio has been synthesized or uploaded first."
        raise ServiceError(404, error_msg)

    logger.info("Transcribing audio: %s with engine: %s", audio_path, request.engine)

    engine_breaker = get_engine_breaker(request.engine)
    if not engine_breaker.allow_request():
        logger.warning(
            "Circuit breaker OPEN for engine '%s', retry in %.1fs",
            request.engine,
            engine_breaker.time_until_retry(),
        )
        raise ServiceError(
            503,
            (
                f"Transcription engine '{request.engine}' is temporarily unavailable. "
                f"Retry in {int(engine_breaker.time_until_retry())} seconds."
            ),
        )

    if not stt_engine.is_initialized():
        logger.info("Initializing Whisper engine...")
        stt_engine.initialize()

    language = request.language
    if language == "auto" or language == "":
        language = None

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

    if result is None:
        engine_breaker.record_failure()
        raise ServiceError(500, f"Transcription failed for engine '{request.engine}'")
    if isinstance(result, str):
        result = {
            "text": result,
            "segments": [],
            "language": language or "unknown",
            "word_timestamps": [],
        }

    segments, word_timestamps = _normalize_segments(result, request)

    transcription_data = {
        "id": transcription_id,
        "audio_id": request.audio_id,
        "text": result["text"],
        "language": result.get("language", "unknown"),
        "duration": result.get("duration", 0.0),
        "segments": segments,
        "word_timestamps": word_timestamps,
        "created": datetime.utcnow(),
        "engine": request.engine,
    }
    if project_id:
        transcription_data["project_id"] = project_id

    repo = get_transcription_repository()
    await repo.store_transcription(transcription_data)

    logger.info(
        "Transcription complete: %s, language=%s, duration=%.2fs",
        transcription_id,
        transcription_data["language"],
        transcription_data["duration"],
    )

    # Build TranscriptionResult for programmatic callers (dubbing, assistant_run)
    seg_results = [
        TranscriptionSegmentResult(
            text=s["text"],
            start=s["start"],
            end=s["end"],
            words=s.get("words"),
            speaker=s.get("speaker"),
        )
        for s in segments
    ]
    return TranscriptionResult(
        id=transcription_id,
        audio_id=request.audio_id,
        text=result["text"],
        language=result.get("language", "unknown"),
        duration=result.get("duration", 0.0),
        segments=seg_results,
        word_timestamps=word_timestamps,
        created=transcription_data["created"],
        engine=request.engine,
    )


async def get_transcription_for_audio(
    audio_id: str,
    project_id: str | None = None,
) -> dict[str, Any] | None:
    """
    Get the most recent transcription for an audio_id, if any.

    Used by dubbing and other routes to avoid re-transcribing when a transcription exists.
    """
    repo = get_transcription_repository()
    items = await repo.list_transcriptions(audio_id=audio_id, project_id=project_id)
    return items[0] if items else None


def get_supported_languages() -> list[dict[str, str]]:
    """Get list of supported languages for transcription."""
    if _is_stt_available():
        try:
            engine_service = get_engine_service()
            whisper_engine = engine_service.get_whisper_engine()
            if whisper_engine and hasattr(whisper_engine, "get_supported_languages"):
                supported = whisper_engine.get_supported_languages()
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
                    {"code": code, "name": language_names.get(code, code.title())}
                    for code in supported
                ]
        except Exception as e:
            logger.debug("Failed to get languages from engine: %s", e)

    return [
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


def list_transcription_engines() -> list[dict[str, Any]]:
    """List available transcription (STT) engines."""
    engines: list[dict[str, Any]] = []

    if _is_stt_available():
        try:
            engine_service = get_engine_service()
            all_engines = engine_service.list_engines()
            stt_types = {"stt", "transcription", "speech_recognition", "whisper"}
            for eng in all_engines:
                eng_type = eng.get("type", "").lower()
                eng_id = eng.get("id", eng.get("name", ""))
                eng_name = eng.get("name", eng_id)
                is_stt = eng_type in stt_types or any(
                    s in eng_id.lower() for s in ["whisper", "vosk", "stt", "transcri"]
                )
                if is_stt:
                    engines.append({
                        "id": eng_id,
                        "name": eng_name,
                        "description": eng.get("description", ""),
                        "supports_word_timestamps": eng.get("supports_word_timestamps", True),
                        "supports_diarization": eng.get("supports_diarization", False),
                        "supports_vad": eng.get("supports_vad", False),
                    })
        except Exception as e:
            logger.warning("Failed to get engines from EngineService: %s", e)

    if not engines:
        engines = [
            {
                "id": "whisper_cpp",
                "name": "Whisper.cpp",
                "description": "OpenAI Whisper ported to C++ for CPU inference",
                "supports_word_timestamps": True,
                "supports_diarization": False,
                "supports_vad": False,
            },
            {
                "id": "whisper",
                "name": "Whisper",
                "description": "OpenAI Whisper for speech recognition",
                "supports_word_timestamps": True,
                "supports_diarization": False,
                "supports_vad": False,
            },
            {
                "id": "vosk",
                "name": "Vosk",
                "description": "Offline speech recognition toolkit",
                "supports_word_timestamps": True,
                "supports_diarization": False,
                "supports_vad": True,
            },
        ]
    return engines
