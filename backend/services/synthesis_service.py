"""
Synthesis service: canonical entry point for text-to-speech synthesis.

M4: Full synthesis logic lives here. Routes call SynthesisService.synthesize()
instead of importing synthesize_core from voice route module.
Policy, provenance, and usage are enforced in the synthesis flow.
"""

from __future__ import annotations

import logging
import os
import tempfile
import uuid
from pathlib import Path
from typing import Any

import numpy as np

from backend.core.exceptions import ServiceError
from backend.services.audio_artifacts.use_cases import (
    create_audio_artifact_from_file,
    create_audio_artifact_from_wav_array,
)
from backend.services.audio_download_service import download_audio_to_temp
from backend.services.path_service import PathService
from backend.services.voice_helpers import (
    check_consent_required,
    ensure_tts_assets,
    normalize_engine_id,
)

logger = logging.getLogger(__name__)


def _is_voice_studio_stub_test_mode() -> bool:
    """True when CI/stub runs must not load real TTS engines (golden-loop smoke, integration)."""
    v = os.environ.get("VOICESTUDIO_TEST_MODE", "").strip().lower()
    return v in ("1", "true", "yes", "stub")


# Quality optimization
HAS_QUALITY_OPTIMIZATION = False
try:
    from backend.ml.models.engine_service import get_engine_service

    _svc = get_engine_service()
    if _svc:
        presets = _svc.get_quality_presets()
        HAS_QUALITY_OPTIMIZATION = len(presets) >= 0
except Exception as e:
    logger.warning("Quality optimization not available: %s", e)


def _get_log_context(**kwargs: Any) -> dict[str, Any]:
    """Build structured logging context with correlation ID."""
    try:
        from backend.api.middleware.correlation_id import (
            get_correlation_id,
            get_span_id,
            get_trace_id,
        )

        context = {
            "correlation_id": get_correlation_id() or "no-correlation-id",
            "trace_id": get_trace_id() or "N/A",
            "span_id": get_span_id() or "N/A",
        }
    except ImportError:
        context = {}
    context.update(kwargs)
    return context


async def _resolve_profile_audio(
    profile_id: str,
    profile: Any,
    profile_dir: str,
) -> str:
    """Resolve the reference audio path for a voice profile."""
    profile_audio_path = None
    reference_audio_url = getattr(profile, "reference_audio_url", None)

    authoritative_path = os.path.join(profile_dir, "reference_audio.wav")
    if os.path.exists(authoritative_path):
        profile_audio_path = authoritative_path
        logger.debug("Using authoritative reference audio: %s", authoritative_path)
    else:
        fallback_names = ["reference.wav", "audio.wav"]
        for name in fallback_names:
            candidate = os.path.join(profile_dir, name)
            if os.path.exists(candidate):
                profile_audio_path = candidate
                logger.info(
                    "Reference audio found at fallback path '%s' for profile %s.",
                    name,
                    profile_id,
                )
                break

    if not profile_audio_path and reference_audio_url:
        if reference_audio_url.startswith("http"):
            logger.info("Downloading reference audio from URL: %s", reference_audio_url)
            path = await download_audio_to_temp(reference_audio_url)
            downloaded_path = str(path) if path else None
            if downloaded_path and os.path.exists(downloaded_path):
                profile_audio_path = downloaded_path
            else:
                logger.warning(
                    "Failed to download reference audio from URL: %s",
                    reference_audio_url,
                )
        elif os.path.exists(reference_audio_url):
            profile_audio_path = reference_audio_url
            logger.info("Using reference_audio_url path: %s", reference_audio_url)
        else:
            logger.warning(
                "reference_audio_url does not exist on disk: %s",
                reference_audio_url,
            )

    if not profile_audio_path or not os.path.exists(profile_audio_path):
        logger.error(
            "Reference audio not found for profile %s. Checked: %s, reference_audio_url=%s",
            profile_id,
            authoritative_path,
            reference_audio_url or "(not set)",
        )
        raise ServiceError(
            400,
            (
                f"Reference audio not found for profile '{profile_id}'. "
                f"Expected at: {authoritative_path}. "
                "Please upload reference audio or re-run the cloning wizard."
            ),
        )

    return profile_audio_path


async def _try_utility_tts_fallback(
    text: str,
    language: str,
    original_error: Exception,
) -> dict[str, Any] | None:
    """Try gTTS and pyttsx3 as fallback TTS when main engine fails."""
    try:
        from backend.tts.tts_utils import synthesize_with_utility

        logger.warning("Main engine failed, trying utility TTS fallback: %s", original_error)

        with tempfile.NamedTemporaryFile(delete=False, suffix=".mp3") as tmp_mp3:
            fallback_mp3 = tmp_mp3.name
        try:
            synthesize_with_utility(
                text,
                utility="gtts",
                language=language or "en",
                output_path=fallback_mp3,
            )
            try:
                import soundfile as sf

                audio, sr = sf.read(fallback_mp3)
                duration = len(audio) / float(sr) if sr else 0.0
                aid, cached_path, _ = create_audio_artifact_from_wav_array(
                    audio, sr, created_by="gtts_fallback"
                )
                logger.info("Fallback to gTTS successful")
                return {"audio_id": aid, "cached_path": cached_path, "duration": duration}
            except ImportError:
                aid, cached_path, _ = create_audio_artifact_from_file(
                    fallback_mp3, created_by="gtts_fallback", delete_source=False
                )
                duration = 0.0
                logger.info("Fallback to gTTS successful (MP3 format)")
                return {"audio_id": aid, "cached_path": cached_path, "duration": duration}
        except Exception as gtts_error:
            logger.warning("gTTS fallback failed: %s", gtts_error)
        finally:
            try:
                os.unlink(fallback_mp3)
            # ALLOWED: bare except - best effort, failure acceptable
            except OSError:
                pass

        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_wav:
            fallback_wav = tmp_wav.name
        try:
            synthesize_with_utility(
                text,
                utility="pyttsx3",
                output_path=fallback_wav,
            )
            aid, cached_path, _ = create_audio_artifact_from_file(
                fallback_wav, created_by="pyttsx3_fallback", delete_source=True
            )
            duration = 0.0
            logger.info("Fallback to pyttsx3 successful")
            return {"audio_id": aid, "cached_path": cached_path, "duration": duration}
        except Exception as pyttsx3_error:
            logger.warning("pyttsx3 fallback also failed: %s", pyttsx3_error)
            return None
        finally:
            try:
                if os.path.exists(fallback_wav):
                    os.unlink(fallback_wav)
            # ALLOWED: bare except - best effort, failure acceptable
            except OSError:
                pass
    except ImportError:
        logger.debug("TTS utilities not available for fallback")
        return None


def _extract_quality_metrics(
    result: Any,
    engine: Any,
    output_path: str,
) -> tuple[float, float, Any]:
    """Extract quality metrics from synthesis result and calculate duration."""
    from backend.api.models_additional import QualityMetrics

    if isinstance(result, tuple):
        audio, engine_quality_metrics = result
    else:
        audio = result
        engine_quality_metrics = {}

    if isinstance(audio, np.ndarray):
        sample_rate = getattr(engine, "sample_rate", 22050)
        duration = len(audio) / sample_rate
    else:
        import wave

        try:
            with wave.open(output_path, "rb") as wav_file:
                frames = wav_file.getnframes()
                sample_rate = wav_file.getframerate()
                duration = frames / float(sample_rate)
        except (wave.Error, OSError) as wav_err:
            logger.debug("Could not read duration from %s: %s", output_path, wav_err)
            duration = 2.5

    detailed_metrics = None
    quality_score = 0.85

    if engine_quality_metrics:
        artifacts_info = engine_quality_metrics.get("artifacts", {})
        if isinstance(artifacts_info, dict):
            artifact_score = artifacts_info.get("artifact_score", 0.0)
            has_clicks = artifacts_info.get("has_clicks", False)
            has_distortion = artifacts_info.get("has_distortion", False)
        else:
            artifact_score = 0.0
            has_clicks = False
            has_distortion = False

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

        if engine_quality_metrics.get("mos_score"):
            quality_score = engine_quality_metrics["mos_score"] / 5.0
        elif engine_quality_metrics.get("similarity"):
            quality_score = engine_quality_metrics["similarity"]
        else:
            metric_values = [
                v
                for k, v in engine_quality_metrics.items()
                if k not in ["artifacts", "voice_profile_match"]
                and isinstance(v, (int, float))
            ]
            if metric_values:
                quality_score = sum(metric_values) / len(metric_values)
                if quality_score > 1.0:
                    quality_score = quality_score / 5.0

    return duration, quality_score, detailed_metrics


class SynthesisService:
    """Canonical synthesis service. Contains full synthesis logic."""

    @staticmethod
    async def synthesize(
        req: Any,
        request: Any,
        config_service: Any = None,
    ) -> Any:
        """
        Synthesize audio from text. Enforces policy, consent, provenance.
        """
        from backend.api.exceptions import (
            EngineProcessingException,
            EngineUnavailableException,
            InvalidEngineException,
            ProfileNotFoundException,
        )
        from backend.api.models_additional import VoiceSynthesizeRequest, VoiceSynthesizeResponse
        from backend.api.utils.instrumentation import EventType, instrument_flow
        from backend.core.circuit_breaker import get_engine_breaker
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )

        _ensure_engine_router()

        _policy = getattr(request.state, "voice_policy", None)
        if _policy and _policy.demo_mode:
            raise ServiceError(403, "Voice synthesis is disabled in demo mode.")

        _consent_id = getattr(req, "consent_id", None)
        if check_consent_required(req.profile_id, request):
            if not _consent_id or not _consent_id.strip():
                raise ServiceError(403, "consent_id is required for third-party voice profiles.")
            try:
                from backend.services.security_service import (
                    ConsentStatus,
                    get_security_service,
                )

                _svc = get_security_service()
                _record = _svc.consent.get_consent_by_id(_consent_id.strip())
                if not _record:
                    raise ServiceError(403, "Consent record not found.")
                if _record.status != ConsentStatus.GRANTED:
                    raise ServiceError(403, f"Consent not granted (status={_record.status.value}).")
                if not _record.is_valid:
                    raise ServiceError(403, "Consent expired or revoked.")
            except ServiceError:
                raise
            except Exception as _ce:
                logger.warning("Consent check error: %s", _ce)

        request_id = getattr(request.state, "request_id", None)

        if not req.engine or not req.engine.strip():
            try:
                if config_service:
                    default_engine = config_service.get_default_engine("tts")
                    if default_engine:
                        requested_engine = default_engine
                    else:
                        requested_engine = "xtts_v2"
                else:
                    requested_engine = "xtts_v2"
            except Exception:
                requested_engine = "xtts_v2"
        else:
            requested_engine = req.engine.strip()

        engine_id = normalize_engine_id(requested_engine)

        with instrument_flow(
            EventType.SYNTHESIS_START,
            EventType.SYNTHESIS_COMPLETE,
            EventType.SYNTHESIS_ERROR,
            request_id=request_id,
            profile_id=req.profile_id,
            engine=engine_id,
            text_length=len(req.text) if req.text else 0,
        ):
            try:
                if _is_voice_studio_stub_test_mode():
                    # Avoid LogRecord key collisions (e.g. correlation_id) from _get_log_context.
                    logger.info(
                        "VOICESTUDIO_TEST_MODE stub: deterministic synthesis artifact (no engine load).",
                        extra={
                            "vs_operation": "synthesis_stub",
                            "vs_profile_id": req.profile_id,
                            "vs_engine": engine_id,
                        },
                    )
                    audio_id = f"synth_{req.profile_id}_{uuid.uuid4().hex[:8]}"
                    sample_rate = 22050
                    n_samples = int(sample_rate * 0.25)
                    silence = np.zeros(n_samples, dtype=np.float32)
                    create_audio_artifact_from_wav_array(
                        silence,
                        sample_rate,
                        created_by="stub",
                        audio_id=audio_id,
                        source="ci_golden_loop_stub",
                    )
                    duration = float(n_samples) / float(sample_rate)
                    return VoiceSynthesizeResponse(
                        audio_id=audio_id,
                        audio_url=f"/api/voice/audio/{audio_id}",
                        duration=duration,
                        quality_score=0.0,
                        quality_metrics=None,
                    )

                ensure_tts_assets(engine_id)

                valid_engines: list[str] = []
                if ENGINE_AVAILABLE and engine_router:
                    valid_engines = engine_router.list_engines()
                if not valid_engines and engine_router is not None:
                    try:
                        engine_router.load_all_engines("engines")
                        valid_engines = engine_router.list_engines()
                    except Exception as e:
                        logger.warning("Failed to auto-load engines: %s", e)
                        valid_engines = []

                if valid_engines and engine_id not in valid_engines:
                    from backend.services.engine_priority import resolve_engine_priority

                    fallback_chain, _fb_source = resolve_engine_priority("tts")

                    original_engine_id = engine_id
                    for fallback_engine in fallback_chain:
                        if fallback_engine in valid_engines:
                            engine_id = fallback_engine
                            logger.info(
                                "Engine '%s' not available, falling back to '%s'",
                                original_engine_id,
                                fallback_engine,
                                extra=_get_log_context(
                                    operation="synthesis",
                                    original_engine=original_engine_id,
                                    fallback_engine=fallback_engine,
                                    profile_id=req.profile_id,
                                ),
                            )
                            break
                    else:
                        engines_str = (
                            ", ".join(valid_engines)
                            if valid_engines
                            else "none (engines not loaded)"
                        )
                        raise InvalidEngineException(
                            engine=requested_engine,
                            available_engines=(
                                engines_str.split(", ")
                                if engines_str != "none (engines not loaded)"
                                else []
                            ),
                        )
                elif not valid_engines:
                    logger.warning(
                        "No engines available - engine router not initialized"
                    )

                if ENGINE_AVAILABLE and engine_router:
                    try:
                        engine = engine_router.get_engine(engine_id)
                        if engine is None:
                            raise EngineUnavailableException(
                                engine=requested_engine,
                                reason="Engine failed to initialize",
                            )

                        from backend.services.profile_search_service import get_profiles_proxy

                        _profiles = get_profiles_proxy()
                        if req.profile_id not in _profiles:
                            raise ProfileNotFoundException(profile_id=req.profile_id)

                        profile = _profiles[req.profile_id]
                        profile_dir = str(PathService.get_profiles_dir() / req.profile_id)
                        profile_audio_path = await _resolve_profile_audio(
                            req.profile_id, profile, profile_dir
                        )

                        text_to_synthesize = req.text
                        try:
                            from backend.nlp.text_processing import get_text_preprocessor

                            preprocessor = get_text_preprocessor()
                            preprocessed = preprocessor.preprocess_for_tts(
                                req.text,
                                language=req.language or "en",
                                normalize=True,
                                segment_sentences=True,
                            )
                            text_to_synthesize = preprocessed["normalized"]
                            logger.debug(
                                "Text preprocessed: %s sentences, %s words",
                                len(preprocessed["sentences"]),
                                preprocessed["word_count"],
                            )
                        # ALLOWED: bare except - optional dependency, import failure acceptable
                        except ImportError:
                            pass
                        except Exception as e:
                            logger.warning("NLP preprocessing failed, using raw text: %s", e)

                        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
                            output_path = tmp.name
                        calculate_quality = True

                        quality_preset = None
                        enhance_quality = False
                        if (
                            HAS_QUALITY_OPTIMIZATION
                            and hasattr(req, "quality_mode")
                            and req.quality_mode
                        ):
                            try:
                                from backend.engines.quality_facade import (
                                    get_synthesis_params_from_preset,
                                )

                                preset_params = get_synthesis_params_from_preset(
                                    req.quality_mode, engine_name=engine_id
                                )
                                enhance_quality = preset_params.get("enhance_quality", False)
                                quality_preset = preset_params.get("quality_preset")
                            except Exception as e:
                                logger.warning("Failed to get quality preset: %s", e)

                        if not quality_preset and engine_id == "tortoise":
                            quality_mode_map = {
                                "fast": "ultra_fast",
                                "standard": "fast",
                                "high": "high_quality",
                                "ultra": "ultra_quality",
                            }
                            quality_preset = quality_mode_map.get(
                                getattr(req, "quality_mode", "standard"), "high_quality"
                            )
                            enhance_quality = quality_preset in ["high_quality", "ultra_quality"]
                        elif not enhance_quality and engine_id == "chatterbox":
                            enhance_quality = getattr(req, "quality_mode", "standard") in [
                                "high",
                                "ultra",
                            ]

                        if hasattr(engine, "synthesize"):
                            synthesis_kwargs = {
                                "text": text_to_synthesize,
                                "speaker_wav": (
                                    profile_audio_path
                                    if os.path.exists(profile_audio_path)
                                    else None
                                ),
                                "language": req.language or "en",
                                "output_path": output_path,
                                "calculate_quality": calculate_quality,
                                "enhance_quality": enhance_quality,
                            }
                            if req.emotion:
                                synthesis_kwargs["emotion"] = req.emotion
                            if quality_preset:
                                synthesis_kwargs["quality_preset"] = quality_preset

                            result = None
                            synthesis_error: Exception | None = None
                            max_retries = 2
                            engine_breaker = get_engine_breaker(engine_id)

                            if not engine_breaker.allow_request():
                                logger.warning(
                                    "Circuit breaker OPEN for engine '%s'",
                                    engine_id,
                                    extra=_get_log_context(
                                        operation="synthesis",
                                        engine=engine_id,
                                        profile_id=req.profile_id,
                                        circuit_state="open",
                                        retry_after_seconds=engine_breaker.time_until_retry(),
                                    ),
                                )
                                raise ServiceError(
                                    503,
                                    (
                                        f"Engine '{engine_id}' is temporarily unavailable. "
                                        f"Retry in {int(engine_breaker.time_until_retry())} seconds."
                                    ),
                                )

                            for attempt in range(max_retries + 1):
                                try:
                                    result = engine.synthesize(**synthesis_kwargs)
                                    engine_breaker.record_success()
                                    break
                                except RuntimeError as e:
                                    engine_breaker.record_failure()
                                    error_msg = str(e).lower()
                                    if attempt == max_retries:
                                        result = await _try_utility_tts_fallback(
                                            text_to_synthesize,
                                            req.language or "en",
                                            e,
                                        )
                                        if isinstance(result, dict) and "audio_id" in result:
                                            break
                                    if "cuda" in error_msg or "gpu" in error_msg or "device" in error_msg:
                                        if attempt < max_retries:
                                            logger.warning(
                                                "Synthesis attempt %s failed with device error: %s. Retrying...",
                                                attempt + 1,
                                                e,
                                            )
                                            try:
                                                engine.cleanup()
                                                engine.initialize()
                                            except Exception as cleanup_error:
                                                logger.warning(
                                                    "Engine reinitialization failed: %s",
                                                    cleanup_error,
                                                )
                                            synthesis_error = e
                                            continue
                                    synthesis_error = e
                                    break
                                except MemoryError as e:
                                    logger.error("Memory error during synthesis: %s", e)
                                    synthesis_error = e
                                    break
                                except Exception as e:
                                    logger.error(
                                        "Synthesis error (attempt %s): %s",
                                        attempt + 1,
                                        e,
                                        exc_info=True,
                                    )
                                    synthesis_error = e
                                    if attempt < max_retries and "timeout" in str(e).lower():
                                        continue
                                    break

                            if result is None and synthesis_error is not None:
                                result = await _try_utility_tts_fallback(
                                    text_to_synthesize,
                                    req.language or "en",
                                    synthesis_error,
                                )
                                if isinstance(result, dict) and "audio_id" in result:
                                    return VoiceSynthesizeResponse(
                                        audio_id=result["audio_id"],
                                        audio_url=f"/api/voice/audio/{result['audio_id']}",
                                        duration=result.get("duration", 0.0),
                                        quality_score=0.0,
                                        quality_metrics=None,
                                    )

                            if isinstance(result, dict) and "audio_id" in result:
                                return VoiceSynthesizeResponse(
                                    audio_id=result["audio_id"],
                                    audio_url=f"/api/voice/audio/{result['audio_id']}",
                                    duration=result.get("duration", 0.0),
                                    quality_score=0.0,
                                    quality_metrics=None,
                                )

                            file_written_early = output_path and os.path.exists(output_path)
                            if result is None and not file_written_early:
                                if synthesis_error:
                                    error_msg = str(synthesis_error)
                                    if isinstance(synthesis_error, RuntimeError):
                                        if "cuda" in error_msg.lower() or "gpu" in error_msg.lower():
                                            detail = (
                                                f"GPU/device error during synthesis: {error_msg}. "
                                                "Try: 1) Check GPU drivers, 2) Use CPU mode, 3) Free GPU memory"
                                            )
                                        else:
                                            detail = f"Engine runtime error: {error_msg}"
                                    elif isinstance(synthesis_error, MemoryError):
                                        detail = (
                                            f"Insufficient memory for synthesis: {error_msg}. "
                                            "Try: 1) Close other applications, 2) Use lower quality mode, "
                                            "3) Reduce text length"
                                        )
                                    elif "timeout" in error_msg.lower():
                                        detail = (
                                            f"Synthesis timed out: {error_msg}. "
                                            "Try: 1) Use faster quality mode, 2) Reduce text length, "
                                            "3) Check system resources"
                                        )
                                    else:
                                        detail = f"Synthesis failed: {error_msg}"
                                    raise ServiceError(500, detail)
                                raise ServiceError(
                                    500,
                                    "Synthesis failed - engine returned None. "
                                    "Check engine logs for details.",
                                )

                            if isinstance(result, tuple):
                                audio, _ = result
                            else:
                                audio = result

                            file_written = os.path.exists(output_path)
                            if audio is None and not file_written:
                                raise ServiceError(
                                    500,
                                    "Synthesis failed - engine returned None and did not write output.",
                                )

                            duration, quality_score, detailed_metrics = _extract_quality_metrics(
                                result, engine, output_path
                            )

                            audio_id = f"synth_{req.profile_id}_{uuid.uuid4().hex[:8]}"

                            if os.path.exists(output_path):
                                create_audio_artifact_from_file(
                                    output_path,
                                    created_by=engine_id,
                                    audio_id=audio_id,
                                    delete_source=True,
                                )
                                return VoiceSynthesizeResponse(
                                    audio_id=audio_id,
                                    audio_url=f"/api/voice/audio/{audio_id}",
                                    duration=duration,
                                    quality_score=quality_score,
                                    quality_metrics=detailed_metrics,
                                )
                            raise ServiceError(
                                500,
                                f"Engine '{requested_engine}' does not support synthesis",
                            )
                    except ServiceError:
                        raise
                    except Exception as e:
                        logger.error("Engine synthesis error: %s", e, exc_info=True)
                        raise EngineProcessingException(
                            engine=engine_id,
                            operation="synthesis",
                            error_message=str(e),
                        ) from e

                raise ServiceError(
                    503,
                    (
                        "Voice synthesis engines are not available. "
                        "Please ensure engines are properly installed and configured."
                    ),
                )
            except ServiceError:
                raise
            except Exception as e:
                logger.error(
                    "Synthesis error: %s",
                    e,
                    exc_info=True,
                    extra=_get_log_context(
                        operation="synthesis",
                        engine=engine_id,
                        profile_id=req.profile_id,
                        text_length=len(req.text) if req.text else 0,
                        error_type=type(e).__name__,
                    ),
                )
                raise ServiceError(500, f"Synthesis failed: {e!s}")
