"""
Voice Cloning and Synthesis Routes - Private Helper Functions

Extracted helper functions for the voice route module.
"""

from __future__ import annotations

import asyncio
import base64
import os
import time
from pathlib import Path
from typing import Any

import numpy as np
from fastapi import HTTPException, WebSocket
from numpy.typing import NDArray

from backend.ml.models.engine_service import get_engine_service
from backend.ml.models.model_preflight import (
    PreflightError,
    ensure_piper,
    ensure_sovits,
    ensure_xtts,
)
from backend.services.audio_artifacts.use_cases import (
    create_audio_artifact_from_file,
    create_audio_artifact_from_wav_array,
)
from backend.services.audio_download_service import download_audio_to_temp
from backend.services.circuit_breaker_facade import get_engine_breaker

from ...exceptions import InvalidEngineException
from ...middleware.correlation_id import get_correlation_id, get_span_id, get_trace_id
from ...models_additional import QualityMetrics, VoiceCloneResponse
from . import _shared
from ._shared import (
    _ENGINE_ID_ALIASES,
    AUDIO_STORAGE_MAX_AGE_SECONDS,
    AUDIO_STORAGE_MAX_SIZE,
    HAS_HTTPX,
    logger,
)


def _log_context(**kwargs: Any) -> dict[str, Any]:
    """
    Build structured logging context with correlation ID.

    Args:
        **kwargs: Additional context fields to include

    Returns:
        Dict with correlation_id, trace_id, span_id, and any additional fields

    GAP-I08: Enhanced with full tracing context.
    """
    context = {
        "correlation_id": get_correlation_id() or "no-correlation-id",
        "trace_id": get_trace_id() or "N/A",
        "span_id": get_span_id() or "N/A",
    }
    context.update(kwargs)
    return context


async def _download_url_to_file(url: str, timeout: float = 30.0) -> str | None:
    """
    Download a file from URL and cache it locally via audio_download_service.

    Returns:
        Path to the downloaded file as str, or None if download failed
    """
    path = await download_audio_to_temp(url, timeout)
    return str(path) if path else None


def _normalize_engine_id(engine_id: str) -> str:
    engine_norm = (engine_id or "").strip().lower()
    return str(_ENGINE_ID_ALIASES.get(engine_norm, engine_norm))


def _normalize_candidate_metrics(candidate_metrics: Any) -> list[dict[str, Any]]:
    """
    Normalize candidate metrics payload for multi-reference runs.

    Ensures a consistent list-of-dicts shape even when the engine stores metrics
    on the instance in various formats.
    """
    try:
        if isinstance(candidate_metrics, dict):
            payload = candidate_metrics.get("candidates", candidate_metrics)
            return _normalize_metrics_payload(payload) or []
        if isinstance(candidate_metrics, (list, tuple)):
            return _normalize_metrics_payload(candidate_metrics) or []
    except (ValueError, TypeError, KeyError) as e:
        logger.debug(f"Failed to extract candidate metrics: {e}")
    return []


def _build_clone_response(
    *,
    profile_id: str,
    audio_id: str | None,
    duration: float | None,
    quality_score: float,
    quality_metrics: QualityMetrics | None,
    device: str | None,
    candidate_metrics: Any,
) -> VoiceCloneResponse:
    """
    Build a consistent VoiceCloneResponse ensuring all key fields are present.
    """
    candidates_payload = _normalize_candidate_metrics(candidate_metrics)
    audio_url = f"/api/voice/audio/{audio_id}" if audio_id else None
    device_used = device or "unknown"

    return VoiceCloneResponse(
        profile_id=profile_id,
        audio_id=audio_id,
        audio_url=audio_url,
        duration=duration,
        quality_score=quality_score,
        quality_metrics=quality_metrics,
        device=device_used,
        candidate_metrics=candidates_payload,
    )


def _ensure_tts_assets(engine_id: str) -> None:
    """
    Ensure required TTS assets exist (auto-download when allowed).

    Catches PreflightError from service layer and converts to HTTPException.
    """
    try:
        if engine_id in ("xtts", "xtts_v2"):
            ensure_xtts(auto_download=True)
        elif engine_id == "piper":
            ensure_piper(auto_download=True)
    except PreflightError as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)


def _ensure_vc_assets(engine_id: str) -> None:
    """
    Ensure VC assets (So-VITS) exist.

    Catches PreflightError from service layer and converts to HTTPException.
    """
    try:
        if engine_id in ("gpt_sovits", "sovits", "sovits_v4"):
            ensure_sovits(auto_download=False)
    except PreflightError as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)


def _dedupe_and_get_path(output_path: str) -> str:
    """
    Place synthesized audio into content-addressed cache; return cached path.

    Falls back to the original path on any failure.
    """
    try:
        from backend.services.audio_registry_service import ensure_cached

        cached_path = ensure_cached(Path(output_path))
        if cached_path and os.path.exists(cached_path):
            return str(cached_path)
    except Exception as e:
        logger.warning("Audio cache deduplication failed for %s: %s", output_path, e)
    return output_path


def _get_wav_duration_seconds(path: str) -> float | None:
    try:
        import wave

        with wave.open(path, "rb") as wav_file:
            frames = wav_file.getnframes()
            sample_rate = wav_file.getframerate()
            if sample_rate:
                return frames / float(sample_rate)
    except Exception as e:
        logger.debug(f"Duration check failed for {path}: {e}")
    return None


def _normalize_metrics_payload(value: Any) -> Any:
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, dict):
        return {k: _normalize_metrics_payload(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_normalize_metrics_payload(v) for v in value]
    if isinstance(value, tuple):
        return [_normalize_metrics_payload(v) for v in value]
    return value


def _coerce_optional_bool(value: Any) -> bool | None:
    if value is None:
        return None
    return bool(_normalize_metrics_payload(value))


def _coerce_optional_float(value: Any) -> float | None:
    if value is None:
        return None
    normalized = _normalize_metrics_payload(value)
    try:
        return float(normalized)
    except (TypeError, ValueError):
        return None


def _cleanup_old_audio_files() -> None:
    """
    Purge old registry entries (mapping only; does not delete files).

    Removes entries older than AUDIO_STORAGE_MAX_AGE_SECONDS or beyond
    AUDIO_STORAGE_MAX_SIZE (oldest first).
    """
    from backend.services.audio_registry_service import purge_old_entries

    purge_old_entries(AUDIO_STORAGE_MAX_AGE_SECONDS, AUDIO_STORAGE_MAX_SIZE)


def _save_audio_to_project(project_id: str, audio_id: str, source_path: str) -> str:
    from ....services.ProjectStoreService import get_project_store_service

    store = get_project_store_service()
    dest_path = store.save_audio_file(
        project_id,
        source_path,
        audio_id=audio_id,
    )
    return str(dest_path)


def _ensure_engine_router() -> None:
    """Lazy initialization of engine router - called at request time, not import time."""
    if _shared.engine_router is not None:
        return  # Already initialized

    try:
        if _shared._voice_engine_service is None:
            _shared._voice_engine_service = get_engine_service()

        # Get the actual engine router from the service
        _shared.engine_router = _shared._voice_engine_service.get_engine_router()

        if _shared.engine_router is not None:
            # Try to load engines if not already loaded
            engines = _shared.engine_router.list_engines()
            if not engines:
                _shared.engine_router.load_all_engines("engines")
                engines = _shared.engine_router.list_engines()

            _shared.ENGINE_AVAILABLE = len(engines) > 0
            if _shared.ENGINE_AVAILABLE:
                logger.info(f"Voice engine router initialized with {len(engines)} engines")
                _shared.quality_metrics = _get_quality_metrics()
        else:
            _shared.ENGINE_AVAILABLE = False
            _shared.quality_metrics = None
            logger.warning("Engine router not available from service")
    except Exception as e:
        logger.warning(f"Failed to initialize engine router: {e}")
        _shared.ENGINE_AVAILABLE = False
        _shared.quality_metrics = None


def _get_quality_metrics() -> dict[str, Any]:
    """Get quality metrics functions via EngineService."""
    if _shared._voice_engine_service is None:
        return {}
    return {
        "calculate_all": _shared._voice_engine_service.calculate_all_metrics,
        "mos": _shared._voice_engine_service.calculate_mos_score,
        "similarity": _shared._voice_engine_service.calculate_similarity,
        "naturalness": _shared._voice_engine_service.calculate_naturalness,
        "snr": _shared._voice_engine_service.calculate_snr,
    }


async def _resolve_profile_audio(
    profile_id: str,
    profile: Any,
    profile_dir: str | None = None,
) -> str:
    """
    Resolve the reference audio path for a voice profile.

    Priority order:
    1. Canonical path: get_path("profiles")/{id}/reference_audio.wav (or fallbacks)
    2. profile.reference_audio_url (file path or HTTP URL)

    Args:
        profile_id: The profile identifier
        profile: The profile object with reference_audio_url attribute
        profile_dir: Optional directory; if None, uses PathService.get_profiles_dir() / profile_id

    Returns:
        Path to the reference audio file

    Raises:
        HTTPException: If no valid reference audio is found
    """
    from backend.services.path_service import PathService

    if profile_dir is None:
        profile_dir = str(PathService.get_profiles_dir() / profile_id)

    profile_audio_path = None

    # Try canonical path first (reference_audio.wav, reference.wav, audio.wav)
    authoritative_path = os.path.join(profile_dir, "reference_audio.wav")
    if os.path.exists(authoritative_path):
        profile_audio_path = authoritative_path
        logger.debug("Using authoritative reference audio: %s", authoritative_path)
    else:
        # Fallback: other common filenames in profile directory
        fallback_names = ["reference.wav", "audio.wav"]
        for name in fallback_names:
            candidate = os.path.join(profile_dir, name)
            if os.path.exists(candidate):
                profile_audio_path = candidate
                logger.info(
                    "Reference audio found at fallback path '%s' for profile %s. "
                    "Consider renaming to 'reference_audio.wav' for consistency.",
                    name,
                    profile_id,
                )
                break

    # Fallback: profile.reference_audio_url (file path or HTTP URL)
    if not profile_audio_path and profile.reference_audio_url:
        if profile.reference_audio_url.startswith("http"):
            logger.info("Downloading reference audio from URL: %s", profile.reference_audio_url)
            downloaded_path = await _download_url_to_file(profile.reference_audio_url)
            if downloaded_path and os.path.exists(downloaded_path):
                profile_audio_path = downloaded_path
            else:
                logger.warning(
                    "Failed to download reference audio from URL: %s",
                    profile.reference_audio_url,
                )
        elif os.path.exists(profile.reference_audio_url):
            profile_audio_path = profile.reference_audio_url
            logger.info(
                "Using reference_audio_url path: %s",
                profile.reference_audio_url,
            )
        else:
            logger.warning(
                "reference_audio_url does not exist on disk: %s",
                profile.reference_audio_url,
            )

    # If still not found, raise clear error
    if not profile_audio_path or not os.path.exists(profile_audio_path):
        logger.error(
            "Reference audio not found for profile %s. "
            "Checked: %s, fallbacks in %s, reference_audio_url=%s",
            profile_id,
            authoritative_path,
            profile_dir,
            profile.reference_audio_url or "(not set)",
        )
        raise HTTPException(
            status_code=400,
            detail=(
                f"Reference audio not found for profile '{profile_id}'. "
                f"Expected at: {authoritative_path}. "
                "Please upload reference audio or re-run the cloning wizard."
            ),
        )

    return profile_audio_path


async def _perform_synthesis_with_retry(
    engine: Any,
    synthesis_kwargs: dict[str, Any],
    engine_id: str,
    text_to_synthesize: str,
    language: str,
    output_path: str,
    max_retries: int = 2,
) -> tuple[Any, Exception | None]:
    """
    Perform synthesis with circuit breaker and retries (no automatic utility TTS substitution).

    Args:
        engine: The engine instance to use
        synthesis_kwargs: Keyword arguments for synthesis
        engine_id: Engine identifier for circuit breaker
        text_to_synthesize: Text to synthesize
        language: Language code
        output_path: Output file path
        max_retries: Maximum retry attempts

    Returns:
        Tuple of (result, error) - result is synthesis output, error is None on success
    """
    result = None
    synthesis_error: Exception | None = None

    # Get circuit breaker for this engine (TD-014)
    engine_breaker = get_engine_breaker(engine_id)

    # Check if circuit is open before attempting
    if not engine_breaker.allow_request():
        logger.warning(
            f"Circuit breaker OPEN for engine '{engine_id}', "
            f"retry in {engine_breaker.time_until_retry():.1f}s"
        )
        raise HTTPException(
            status_code=503,
            detail=f"Engine '{engine_id}' is temporarily unavailable. "
            f"Retry in {int(engine_breaker.time_until_retry())} seconds.",
        )

    for attempt in range(max_retries + 1):
        try:
            result = engine.synthesize(**synthesis_kwargs)
            # Record success with circuit breaker
            engine_breaker.record_success()
            return result, None  # Success
        except RuntimeError as e:
            # Record failure with circuit breaker
            engine_breaker.record_failure()
            error_msg = str(e).lower()

            # GPU/device errors - may be recoverable
            if "cuda" in error_msg or "gpu" in error_msg or "device" in error_msg:
                if attempt < max_retries:
                    logger.warning(
                        f"Synthesis attempt {attempt + 1} failed with device error: {e}. "
                        "Retrying..."
                    )
                    # Try to reinitialize engine on device error
                    try:
                        engine.cleanup()
                        engine.initialize()
                    except Exception as cleanup_error:
                        logger.warning(f"Engine reinitialization failed: {cleanup_error}")
                    synthesis_error = e
                    continue
            synthesis_error = e
            break
        except MemoryError as e:
            # Memory errors - not recoverable without cleanup
            logger.error(f"Memory error during synthesis: {e}")
            synthesis_error = e
            break
        except Exception as e:
            # Other errors - log and retry if timeout
            logger.error(
                f"Synthesis error (attempt {attempt + 1}): {e}",
                exc_info=True,
                extra=_log_context(
                    operation="synthesis_retry",
                    engine=engine_id,
                    attempt=attempt + 1,
                    max_retries=max_retries,
                    error_type=type(e).__name__,
                    text_length=len(text_to_synthesize) if text_to_synthesize else 0,
                ),
            )
            synthesis_error = e
            if attempt < max_retries and "timeout" in str(e).lower():
                continue
            break

    return result, synthesis_error


def _extract_quality_metrics(
    result: Any,
    engine: Any,
    output_path: str,
) -> tuple[float, float, QualityMetrics | None]:
    """
    Extract quality metrics from synthesis result and calculate duration.

    Args:
        result: The synthesis result (audio array or tuple)
        engine: The engine instance (for sample rate)
        output_path: Path to the output audio file

    Returns:
        Tuple of (duration, quality_score, detailed_metrics)
    """
    # Handle both single return and tuple (audio, metrics)
    if isinstance(result, tuple):
        audio, engine_quality_metrics = result
    else:
        audio = result
        engine_quality_metrics = {}

    # Calculate duration from audio array
    if isinstance(audio, np.ndarray):
        sample_rate = getattr(engine, "sample_rate", 22050)
        duration = len(audio) / sample_rate
    else:
        # If audio was saved to file, estimate duration
        import wave

        try:
            with wave.open(output_path, "rb") as wav_file:
                frames = wav_file.getnframes()
                sample_rate = wav_file.getframerate()
                duration = frames / float(sample_rate)
        except (wave.Error, OSError) as wav_err:
            logger.debug(f"Could not read duration from {output_path}: {wav_err}")
            duration = 2.5  # Fallback

    # Extract quality metrics
    detailed_metrics = None
    quality_score = 0.85  # Default

    if engine_quality_metrics:
        # Extract detailed metrics
        artifacts_info = engine_quality_metrics.get("artifacts", {})
        if isinstance(artifacts_info, dict):
            artifact_score = artifacts_info.get("artifact_score", 0.0)
            has_clicks = artifacts_info.get("has_clicks", False)
            has_distortion = artifacts_info.get("has_distortion", False)
        else:
            artifact_score = 0.0
            has_clicks = False
            has_distortion = False

        # Build detailed metrics object
        detailed_metrics = QualityMetrics(
            mos_score=engine_quality_metrics.get("mos_score"),
            similarity=engine_quality_metrics.get("similarity"),
            naturalness=engine_quality_metrics.get("naturalness"),
            snr_db=engine_quality_metrics.get("snr_db"),
            artifact_score=artifact_score,
            has_clicks=has_clicks,
            has_distortion=has_distortion,
            voice_profile_match=engine_quality_metrics.get("voice_profile_match"),
        )

        # Calculate overall quality score from metrics
        if engine_quality_metrics.get("mos_score"):
            quality_score = engine_quality_metrics["mos_score"] / 5.0  # Normalize MOS to 0-1
        elif engine_quality_metrics.get("similarity"):
            quality_score = engine_quality_metrics["similarity"]  # Use similarity as quality score
        else:
            # Average available metrics
            metric_values = [
                v
                for k, v in engine_quality_metrics.items()
                if k not in ["artifacts", "voice_profile_match"] and isinstance(v, (int, float))
            ]
            if metric_values:
                quality_score = sum(metric_values) / len(metric_values)
                # Normalize if needed
                if quality_score > 1.0:
                    quality_score = quality_score / 5.0

    return duration, quality_score, detailed_metrics


def _engine_supports_streaming(engine_instance: Any) -> bool:
    """Check if an engine supports streaming synthesis."""
    return hasattr(engine_instance, "synthesize_stream") and callable(
        getattr(engine_instance, "synthesize_stream", None)
    )


def _get_engine_sample_rate(engine_instance: Any, engine_id: str) -> int:
    """Get the sample rate for an engine."""
    # Engine-specific sample rates
    SAMPLE_RATES = {
        "openvoice": 24000,
        "xtts": 24000,
        "xtts_v2": 24000,
        "tacotron2": 22050,
        "piper": 22050,
        "bark": 24000,
        "tortoise": 24000,
    }
    return getattr(
        engine_instance,
        "DEFAULT_SAMPLE_RATE",
        SAMPLE_RATES.get(engine_id, 24000),
    )


async def _stream_synthesis_chunks(
    websocket: WebSocket,
    engine_instance: Any,
    engine_id: str,
    text: str,
    profile_audio_path: str | None,
    language: str,
    chunk_size: int,
    overlap: int,
    **kwargs: Any,
) -> None:
    """
    Stream audio chunks from an engine's synthesize_stream method.

    Handles both generator and async generator streaming modes.
    """
    sample_rate = _get_engine_sample_rate(engine_instance, engine_id)
    chunk_index = 0
    total_samples = 0

    # Build streaming kwargs
    stream_kwargs = {
        "text": text,
        "language": language,
        "chunk_size": chunk_size,
        "overlap": overlap,
    }

    # Add speaker_wav for voice cloning engines
    if profile_audio_path:
        stream_kwargs["speaker_wav"] = profile_audio_path

    # Merge additional kwargs
    stream_kwargs.update(kwargs)

    try:
        # Get the streaming generator
        stream_gen = engine_instance.synthesize_stream(**stream_kwargs)

        # Handle async generators
        if hasattr(stream_gen, "__anext__"):
            async for audio_chunk in stream_gen:
                await _send_audio_chunk(websocket, audio_chunk, chunk_index, sample_rate)
                chunk_index += 1
                total_samples += len(audio_chunk)
        else:
            # Handle sync generators
            for audio_chunk in stream_gen:
                await _send_audio_chunk(websocket, audio_chunk, chunk_index, sample_rate)
                chunk_index += 1
                total_samples += len(audio_chunk)
                # Yield control to allow other async tasks
                await asyncio.sleep(0)

        # Send completion message
        duration = total_samples / sample_rate
        await websocket.send_json(
            {
                "type": "complete",
                "total_chunks": chunk_index,
                "total_samples": total_samples,
                "duration": duration,
                "sample_rate": sample_rate,
                "engine": engine_id,
            }
        )

    except Exception as e:
        logger.error(f"Streaming error for {engine_id}: {e}", exc_info=True)
        await websocket.send_json({"type": "error", "message": f"Streaming failed: {e!s}"})


async def _send_audio_chunk(
    websocket: WebSocket,
    audio_chunk: NDArray[Any],
    chunk_index: int,
    sample_rate: int,
) -> None:
    """Send a single audio chunk over WebSocket."""
    # Ensure numpy array
    if not isinstance(audio_chunk, np.ndarray):
        audio_chunk = np.array(audio_chunk, dtype=np.float32)

    # Convert to float32 if needed
    if audio_chunk.dtype != np.float32:
        audio_chunk = audio_chunk.astype(np.float32)

    # Encode as base64
    audio_bytes = audio_chunk.tobytes()
    audio_b64 = base64.b64encode(audio_bytes).decode("utf-8")

    await websocket.send_json(
        {
            "type": "audio_chunk",
            "chunk_index": chunk_index,
            "data": audio_b64,
            "sample_rate": sample_rate,
            "format": "float32",
            "samples": len(audio_chunk),
        }
    )
