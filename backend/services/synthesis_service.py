"""
Synthesis service: canonical entry point for text-to-speech synthesis.

M4: Full synthesis logic lives here. Routes call SynthesisService.synthesize()
instead of importing synthesize_core from voice route module.
Policy, provenance, and usage are enforced in the synthesis flow.
"""

from __future__ import annotations

import json
import logging
import os
import tempfile
import uuid
import wave
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt

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


def _synthesis_engine_output_path() -> str:
    """Reserve a unique WAV path for engine output.

    Must not pre-create the file: ``tempfile.NamedTemporaryFile`` leaves a 0-byte
    file on disk, and ``os.path.exists`` would then claim output exists even when
    the engine never wrote audio — leading to empty artifacts and HTTP 200 with
    zero-length bodies on ``GET /api/audio/file/{id}``.
    """
    return os.path.join(tempfile.gettempdir(), f"vs_synth_{uuid.uuid4().hex}.wav")


def _synth_output_file_ready(path: str | None) -> bool:
    """True when *path* is a regular file with non-zero size (engine actually wrote bytes)."""
    if not path or not os.path.isfile(path):
        return False
    try:
        return os.path.getsize(path) > 0
    except OSError:
        return False


def _optional_text(value: Any) -> str | None:
    """Return a trimmed string or None for optional provenance fields."""
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def _generated_audio_metadata(req: Any, engine_id: str, audio_id: str) -> dict[str, Any]:
    """Build generated-audio provenance metadata for the artifact registry."""
    metadata: dict[str, Any] = {
        "generated_audio_id": audio_id,
        "audio_id": audio_id,
        "source": "voice_synthesis",
        "source_engine": _optional_text(getattr(req, "engine", None)) or engine_id,
        "routed_engine": engine_id,
        "profile_id": _optional_text(getattr(req, "profile_id", None)),
    }
    project_id = _optional_text(getattr(req, "project_id", None))
    session_id = _optional_text(getattr(req, "session_id", None))
    if project_id:
        metadata["project_id"] = project_id
    if session_id:
        metadata["session_id"] = session_id
    return metadata


def _record_generated_audio_metadata(req: Any, engine_id: str, audio_id: str) -> None:
    """Persist generated-audio provenance on an already registered artifact."""
    from backend.services.audio_registry_service import get_registry

    get_registry().update_metadata(audio_id, _generated_audio_metadata(req, engine_id, audio_id))


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
    """Build ``extra=`` payload for structured logging.

    Do **not** put ``correlation_id`` / ``trace_id`` / ``span_id`` here: the global
    ``LogRecord`` factory (``correlation_id.py``) already sets them on every record.
    Passing the same keys in ``extra`` causes ``KeyError: Attempt to overwrite
    'correlation_id' in LogRecord`` on synthesis (and any other path using this helper).
    """
    return dict(kwargs)


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
        except (wave.Error, OSError, EOFError) as wav_err:
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
        from backend.api.models_additional import (
            SsmlHandlingDiagnostics,
            VoiceSynthesizeRequest,
            VoiceSynthesizeResponse,
        )
        from backend.api.utils.instrumentation import EventType, instrument_flow
        from backend.core.circuit_breaker import get_engine_breaker
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )
        from backend.services.ssml_capability_resolver import (
            SsmlPolicyRejected,
            apply_ssml_synthesis_policy,
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
                    silence: npt.NDArray[np.float32] = np.zeros(
                        n_samples, dtype=np.float32
                    )
                    create_audio_artifact_from_wav_array(
                        silence,
                        sample_rate,
                        created_by="stub",
                        audio_id=audio_id,
                        project_id=_optional_text(getattr(req, "project_id", None)),
                        source="ci_golden_loop_stub",
                    )
                    _record_generated_audio_metadata(req, "stub", audio_id)
                    duration = float(n_samples) / float(sample_rate)
                    return VoiceSynthesizeResponse(
                        audio_id=audio_id,
                        audio_url=f"/api/voice/audio/{audio_id}",
                        generated_audio_id=audio_id,
                        profile_id=req.profile_id,
                        duration=duration,
                        quality_score=0.0,
                        quality_metrics=None,
                        ssml_handling=None,
                        routed_engine="stub",
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

                        try:
                            ssml_policy = apply_ssml_synthesis_policy(
                                engine_id, req.text or ""
                            )
                        except SsmlPolicyRejected as rej:
                            raise ServiceError(422, rej.message) from rej

                        ssml_handling: SsmlHandlingDiagnostics | None = None
                        if ssml_policy.diagnostics is not None:
                            ssml_handling = SsmlHandlingDiagnostics(
                                **ssml_policy.diagnostics
                            )

                        text_to_synthesize = ssml_policy.effective_text
                        if not ssml_policy.skip_text_preprocessor:
                            try:
                                from backend.nlp.text_processing import get_text_preprocessor

                                preprocessor = get_text_preprocessor()
                                preprocessed = preprocessor.preprocess_for_tts(
                                    text_to_synthesize,
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
                                logger.warning(
                                    "NLP preprocessing failed, using raw text: %s", e
                                )

                        if not (text_to_synthesize or "").strip():
                            raise ServiceError(
                                400,
                                "No speakable text remains after SSML processing.",
                            )

                        output_path = _synthesis_engine_output_path()
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
                            if ssml_policy.pass_ssml_to_engine:
                                synthesis_kwargs["ssml"] = True
                            if req.speed is not None:
                                synthesis_kwargs["speed"] = req.speed
                            if req.pitch is not None:
                                synthesis_kwargs["pitch"] = req.pitch
                            if req.stability is not None:
                                synthesis_kwargs["stability"] = req.stability
                            if req.clarity is not None:
                                synthesis_kwargs["clarity"] = req.clarity
                            if req.temperature is not None:
                                synthesis_kwargs["temperature"] = req.temperature

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
                                        synthesis_error = e
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

                            if isinstance(result, dict) and "audio_id" in result:
                                result_audio_id = str(result["audio_id"])
                                routed_engine = str(result.get("routed_engine") or engine_id)
                                _record_generated_audio_metadata(req, routed_engine, result_audio_id)
                                return VoiceSynthesizeResponse(
                                    audio_id=result_audio_id,
                                    audio_url=f"/api/voice/audio/{result_audio_id}",
                                    generated_audio_id=result_audio_id,
                                    profile_id=req.profile_id,
                                    duration=result.get("duration", 0.0),
                                    quality_score=0.0,
                                    quality_metrics=None,
                                    ssml_handling=ssml_handling,
                                    routed_engine=routed_engine,
                                )

                            file_written_early = _synth_output_file_ready(output_path)
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

                            file_written = _synth_output_file_ready(output_path)
                            if audio is None and not file_written:
                                raise ServiceError(
                                    500,
                                    "Synthesis failed - engine returned None and did not write output.",
                                )

                            duration, quality_score, detailed_metrics = _extract_quality_metrics(
                                result, engine, output_path
                            )

                            audio_id = f"synth_{req.profile_id}_{uuid.uuid4().hex[:8]}"

                            if _synth_output_file_ready(output_path):
                                create_audio_artifact_from_file(
                                    output_path,
                                    created_by=engine_id,
                                    audio_id=audio_id,
                                    project_id=_optional_text(getattr(req, "project_id", None)),
                                    source="voice_synthesis",
                                    delete_source=True,
                                )
                                _record_generated_audio_metadata(req, engine_id, audio_id)
                                return VoiceSynthesizeResponse(
                                    audio_id=audio_id,
                                    audio_url=f"/api/voice/audio/{audio_id}",
                                    generated_audio_id=audio_id,
                                    profile_id=req.profile_id,
                                    duration=duration,
                                    quality_score=quality_score,
                                    quality_metrics=detailed_metrics,
                                    ssml_handling=ssml_handling,
                                    routed_engine=engine_id,
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

    @staticmethod
    async def synthesize_multipass(
        req: Any,
        request: Any,
        config_service: Any = None,
    ) -> Any:
        """Multi-pass synthesis; each pass delegates to synthesize()."""
        from backend.api.models_additional import (
            MultiPassSynthesisResponse,
            PassResult,
            QualityMetrics,
            VoiceSynthesizeRequest,
        )
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )

        _ensure_engine_router()
        if not ENGINE_AVAILABLE or not engine_router:
            raise ServiceError(
                503,
                "Engine router not available for multi-pass synthesis",
            )

        valid_engines = engine_router.list_engines()
        requested_engine = req.engine
        engine_id = normalize_engine_id(requested_engine)
        if engine_id not in valid_engines:
            raise ServiceError(
                400,
                f"Invalid engine '{requested_engine}'. Available: {', '.join(valid_engines)}",
            )

        if engine_router.get_engine(engine_id) is None:
            raise ServiceError(
                503,
                f"Engine '{requested_engine}' is not available or failed to initialize",
            )

        min_improvement = (
            req.min_quality_improvement
            if req.min_quality_improvement is not None
            else 0.02
        )
        if req.pass_preset == "naturalness_focus":
            min_improvement = 0.02
        elif req.pass_preset == "similarity_focus":
            min_improvement = 0.01
        elif req.pass_preset == "artifact_focus":
            min_improvement = 0.03

        adaptive = True if req.adaptive is None else bool(req.adaptive)

        passes: list[Any] = []
        improvement_tracking: list[float] = []
        best_pass = 0
        best_quality = 0.0
        previous_quality = 0.0
        max_passes = req.max_passes or 3

        for pass_num in range(1, max_passes + 1):
            logger.info("Multi-pass synthesis: Pass %s/%s", pass_num, max_passes)
            synth_req = VoiceSynthesizeRequest(
                engine=engine_id,
                profile_id=req.profile_id,
                text=req.text,
                language=req.language,
                emotion=req.emotion,
                enhance_quality=True,
                consent_id=getattr(req, "consent_id", None),
            )
            synth_response = await SynthesisService.synthesize(
                synth_req, request, config_service
            )

            if not synth_response.quality_metrics:
                quality_score = synth_response.quality_score
                quality_metrics = QualityMetrics(
                    mos_score=quality_score * 5.0 if quality_score <= 1.0 else None,
                    similarity=quality_score if quality_score <= 1.0 else None,
                )
            else:
                quality_metrics = synth_response.quality_metrics
                quality_score = synth_response.quality_score

            improvement = 0.0
            if pass_num > 1:
                improvement = quality_score - previous_quality

            pass_result = PassResult(
                pass_number=pass_num,
                audio_id=synth_response.audio_id,
                audio_url=synth_response.audio_url,
                quality_metrics=quality_metrics,
                quality_score=quality_score,
                improvement=improvement if pass_num > 1 else None,
            )
            passes.append(pass_result)
            improvement_tracking.append(improvement if pass_num > 1 else 0.0)

            if quality_score > best_quality:
                best_quality = quality_score
                best_pass = pass_num

            if adaptive and pass_num > 1 and improvement < min_improvement:
                logger.info(
                    "Multi-pass synthesis: Stopping early at pass %s "
                    "(improvement %.4f < %s)",
                    pass_num,
                    improvement,
                    min_improvement,
                )
                break

            previous_quality = quality_score

        best_pass_result = passes[best_pass - 1]

        from backend.services.audio_path_resolver import resolve_audio_path

        best_audio_path = resolve_audio_path(best_pass_result.audio_id)
        duration = 2.5
        if best_audio_path and os.path.exists(best_audio_path):
            try:
                with wave.open(best_audio_path, "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sample_rate = wav_file.getframerate()
                    duration = frames / float(sample_rate)
            except (wave.Error, OSError) as wav_err:
                logger.debug(
                    "Could not read duration from %s: %s", best_audio_path, wav_err
                )

        return MultiPassSynthesisResponse(
            audio_id=best_pass_result.audio_id,
            audio_url=best_pass_result.audio_url,
            duration=duration,
            quality_score=best_pass_result.quality_score,
            quality_metrics=best_pass_result.quality_metrics,
            passes_completed=len(passes),
            passes=passes,
            best_pass=best_pass,
            improvement_tracking=improvement_tracking,
        )

    @staticmethod
    def _split_oversized_sentence(sentence: str, max_chunk_chars: int) -> list[str]:
        """Word-bounded segments when a single sentence exceeds max_chunk_chars."""
        words = sentence.split()
        out: list[str] = []
        cur: list[str] = []
        cur_len = 0
        for w in words:
            add = len(w) + (1 if cur else 0)
            if cur_len + add > max_chunk_chars and cur:
                out.append(" ".join(cur))
                cur = [w]
                cur_len = len(w)
            else:
                cur.append(w)
                cur_len += add
        if cur:
            out.append(" ".join(cur))
        return [x for x in out if x.strip()]

    @staticmethod
    def _chunk_text_for_long_form(text: str, max_chunk_chars: int, language: str) -> list[str]:
        """
        Deterministic sentence-boundary chunking for long-form synthesis (GAP-049).
        Uses TextPreprocessor.sentence_segmentation; falls back to one chunk on NLP failure.
        """
        stripped = text.strip()
        if not stripped:
            return []
        try:
            from backend.nlp.text_processing import get_text_preprocessor

            preprocessor = get_text_preprocessor()
            sentences = preprocessor.sentence_segmentation(
                stripped, language=language or "en"
            )
        except Exception as e:
            logger.warning("Long-form chunking: NLP unavailable, single chunk: %s", e)
            return [stripped]

        sentences = [s.strip() for s in sentences if s and str(s).strip()]
        if not sentences:
            return [stripped]

        chunks: list[str] = []
        current: list[str] = []
        current_len = 0

        for sent in sentences:
            s = sent.strip()
            if not s:
                continue
            if len(s) > max_chunk_chars:
                if current:
                    chunks.append(" ".join(current).strip())
                    current = []
                    current_len = 0
                chunks.extend(
                    SynthesisService._split_oversized_sentence(s, max_chunk_chars)
                )
                continue
            added = len(s) + (1 if current else 0)
            if current_len + added > max_chunk_chars and current:
                chunks.append(" ".join(current).strip())
                current = [s]
                current_len = len(s)
            else:
                current.append(s)
                current_len += added
        if current:
            chunks.append(" ".join(current).strip())
        return [c for c in chunks if c]

    @staticmethod
    async def synthesize_long_form(
        req: Any,
        request: Any,
        config_service: Any = None,
    ) -> Any:
        """
        Long-form synthesis: chunk text at sentence boundaries, synthesize each chunk with
        identical settings, concatenate audio, one merged artifact (GAP-049).
        """
        from backend.api.models_additional import (
            LongFormChunkResult,
            LongFormSynthesisResponse,
            VoiceSynthesizeRequest,
        )
        from backend.audio.audio_utils import load_audio, resample_audio
        from backend.services.audio_path_resolver import resolve_audio_path
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )

        _ensure_engine_router()
        if not ENGINE_AVAILABLE or not engine_router:
            raise ServiceError(
                503,
                "Engine router not available for long-form synthesis",
            )

        valid_engines = engine_router.list_engines()
        requested_engine = getattr(req, "engine", None)
        if not requested_engine or not str(requested_engine).strip():
            try:
                if config_service:
                    default_engine = config_service.get_default_engine("tts")
                    requested_engine = default_engine or "xtts_v2"
                else:
                    requested_engine = "xtts_v2"
            except Exception:
                requested_engine = "xtts_v2"
        engine_id = normalize_engine_id(str(requested_engine).strip())

        if valid_engines and engine_id not in valid_engines:
            raise ServiceError(
                400,
                f"Invalid engine '{requested_engine}'. Available: {', '.join(valid_engines)}",
            )
        if engine_router.get_engine(engine_id) is None:
            raise ServiceError(
                503,
                f"Engine '{requested_engine}' is not available or failed to initialize",
            )

        lang = req.language or "en"
        max_chars = int(req.chunk_size_chars)
        chunks = SynthesisService._chunk_text_for_long_form(req.text, max_chars, lang)
        if not chunks:
            raise ServiceError(400, "No synthesizable text after chunking.")

        failed: list[LongFormChunkResult] = []
        arrays: list[Any] = []
        sample_rate: int | None = None
        quality_num = 0.0
        quality_den = 0.0

        for idx, chunk_text in enumerate(chunks):
            synth_req = VoiceSynthesizeRequest(
                engine=engine_id,
                profile_id=req.profile_id,
                text=chunk_text,
                language=lang,
                emotion=req.emotion,
                enhance_quality=bool(req.enhance_quality)
                if req.enhance_quality is not None
                else False,
                consent_id=getattr(req, "consent_id", None),
                speed=getattr(req, "speed", None),
                pitch=getattr(req, "pitch", None),
                stability=getattr(req, "stability", None),
                clarity=getattr(req, "clarity", None),
                temperature=getattr(req, "temperature", None),
            )
            try:
                synth_response = await SynthesisService.synthesize(
                    synth_req, request, config_service
                )
            except Exception as e:
                logger.warning(
                    "Long-form chunk %s failed: %s",
                    idx,
                    e,
                    exc_info=True,
                )
                failed.append(LongFormChunkResult(chunk_index=idx, error=str(e)))
                continue

            aid = getattr(synth_response, "audio_id", None)
            if not aid:
                failed.append(
                    LongFormChunkResult(chunk_index=idx, error="Missing audio_id")
                )
                continue
            path = resolve_audio_path(aid)
            if not path or not os.path.exists(path):
                failed.append(
                    LongFormChunkResult(
                        chunk_index=idx,
                        error="Could not resolve synthesized audio path",
                    )
                )
                continue
            try:
                chunk_audio, sr = load_audio(path)
            except Exception as e:
                failed.append(
                    LongFormChunkResult(chunk_index=idx, error=f"Load audio: {e!s}")
                )
                continue

            chunk_audio = np.asarray(chunk_audio, dtype=np.float32)
            if chunk_audio.ndim > 1:
                chunk_audio = np.mean(chunk_audio, axis=1)

            if chunk_audio is None or len(chunk_audio) == 0:
                failed.append(
                    LongFormChunkResult(chunk_index=idx, error="Empty audio chunk")
                )
                continue

            q = float(getattr(synth_response, "quality_score", 0.0) or 0.0)
            w = float(len(chunk_audio))
            quality_num += q * w
            quality_den += w

            if sample_rate is None:
                sample_rate = int(sr)
                arrays.append(np.asarray(chunk_audio, dtype=np.float32))
            else:
                if int(sr) != int(sample_rate):
                    chunk_audio = resample_audio(
                        np.asarray(chunk_audio, dtype=np.float32),
                        int(sr),
                        int(sample_rate),
                    )
                arrays.append(np.asarray(chunk_audio, dtype=np.float32))

        if not arrays or sample_rate is None:
            raise ServiceError(
                500,
                "All long-form synthesis chunks failed; no audio to merge.",
            )

        merged = np.concatenate(arrays)
        out_id = f"longform_{uuid.uuid4().hex[:12]}"
        create_audio_artifact_from_wav_array(
            merged,
            int(sample_rate),
            created_by="long_form_synthesis",
            audio_id=out_id,
            source="gap049_long_form",
        )
        duration = float(len(merged)) / float(sample_rate)
        avg_quality = quality_num / quality_den if quality_den > 0 else 0.0

        return LongFormSynthesisResponse(
            audio_id=out_id,
            audio_url=f"/api/voice/audio/{out_id}",
            duration=duration,
            quality_score=float(avg_quality),
            chunks_total=len(chunks),
            chunks_succeeded=len(arrays),
            partial_failure=len(failed) > 0,
            failed_chunks=failed,
        )

    @staticmethod
    async def synthesize_with_style(
        *,
        _request: Any,
        text: str,
        profile_id: str,
        engine: str = "openvoice",
        language: str = "en",
        emotion: str | None = None,
        accent: str | None = None,
        rhythm: float | None = None,
        pauses: str | None = None,
        pitch_shift: float | None = None,
        pitch_variance: float | None = None,
        energy: float | None = None,
        enhance_quality: bool = True,
        calculate_quality: bool = True,
    ) -> Any:
        """OpenVoice style-controlled synthesis (canonical service path)."""
        from backend.api.models_additional import QualityMetrics, VoiceSynthesizeResponse
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )
        from backend.services.profile_service import resolve_reference_audio_path

        if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in (
            "true",
            "1",
            "yes",
        ):
            raise ServiceError(403, "Style synthesis disabled in demo mode.")

        _ensure_engine_router()
        if not ENGINE_AVAILABLE or not engine_router:
            raise ServiceError(503, "Engine router not available")

        if engine != "openvoice":
            raise ServiceError(
                400,
                "Style control is currently only supported for OpenVoice engine",
            )

        engine_instance = engine_router.get_engine(engine)
        if engine_instance is None:
            raise ServiceError(503, f"Engine '{engine}' is not available")

        if not hasattr(engine_instance, "synthesize_with_style"):
            raise ServiceError(400, "Engine does not support style control")

        pause_list = None
        pause_positions = None
        if pauses:
            try:
                pause_data = json.loads(pauses)
                if isinstance(pause_data, list):
                    pause_list = pause_data
                elif isinstance(pause_data, dict):
                    pause_list = pause_data.get("durations", [])
                    pause_positions = pause_data.get("positions", [0.3, 0.7])
            except json.JSONDecodeError:
                logger.warning("Invalid pauses JSON: %s", pauses)

        intonation: dict[str, float] = {}
        if pitch_shift is not None:
            intonation["pitch_shift"] = pitch_shift
        if pitch_variance is not None:
            intonation["pitch_variance"] = pitch_variance
        if energy is not None:
            intonation["energy"] = energy

        profile_audio_path = resolve_reference_audio_path(profile_id)
        if not profile_audio_path.exists():
            raise ServiceError(404, f"Profile audio not found: {profile_id}")
        profile_audio_str = str(profile_audio_path)

        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            output_path = tmp.name
        try:
            engine_instance.synthesize_with_style(
                text=text,
                speaker_wav=profile_audio_str,
                language=language,
                emotion=emotion,
                accent=accent,
                rhythm=rhythm,
                pauses=pause_list,
                intonation=intonation if intonation else None,
                output_path=output_path,
                pause_positions=pause_positions,
            )

            quality_metrics = None
            if calculate_quality:
                try:
                    from backend.ml.models.engine_service import get_engine_service

                    svc = get_engine_service()
                    import soundfile as sf

                    audio_array, sr = sf.read(output_path)
                    metrics = svc.calculate_all_metrics(audio_array, sr)
                    quality_metrics = QualityMetrics(
                        mos_score=metrics.get("mos_score"),
                        similarity=metrics.get("similarity"),
                        naturalness=metrics.get("naturalness"),
                        snr_db=metrics.get("snr_db"),
                    )
                except Exception as e:
                    logger.warning("Quality calculation failed: %s", e)

            try:
                with wave.open(output_path, "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sample_rate = wav_file.getframerate()
                    duration = frames / float(sample_rate)
            except (wave.Error, OSError) as wav_err:
                logger.debug("Could not read duration from %s: %s", output_path, wav_err)
                duration = 2.5

            audio_id, _, _ = create_audio_artifact_from_file(
                output_path,
                created_by="style",
                project_id=None,
                source="style_transfer",
            )

            return VoiceSynthesizeResponse(
                audio_id=audio_id,
                audio_url=f"/api/voice/audio/{audio_id}",
                duration=duration,
                quality_score=0.85,
                quality_metrics=quality_metrics,
                routed_engine=str(engine),
            )
        finally:
            if os.path.exists(output_path):
                try:
                    os.unlink(output_path)
                except OSError as unlink_err:
                    logger.debug(
                        "Could not remove temp style output %s: %s",
                        output_path,
                        unlink_err,
                    )

    @staticmethod
    async def synthesize_cross_lingual(
        *,
        _request: Any,
        text: str,
        profile_id: str,
        source_language: str = "en",
        target_language: str = "es",
        engine: str = "openvoice",
        enhance_quality: bool = True,
        calculate_quality: bool = True,
    ) -> Any:
        """OpenVoice cross-lingual synthesis (canonical service path)."""
        from backend.api.models_additional import QualityMetrics, VoiceSynthesizeResponse
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )
        from backend.services.profile_service import resolve_reference_audio_path

        _ = enhance_quality  # reserved for future engine kwargs parity

        if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in (
            "true",
            "1",
            "yes",
        ):
            raise ServiceError(
                403,
                "Cross-lingual synthesis disabled in demo mode.",
            )

        _ensure_engine_router()
        if not ENGINE_AVAILABLE or not engine_router:
            raise ServiceError(503, "Engine router not available")

        if engine != "openvoice":
            raise ServiceError(
                400,
                "Cross-lingual cloning is currently only supported for OpenVoice engine",
            )

        engine_instance = engine_router.get_engine(engine)
        if engine_instance is None:
            raise ServiceError(503, f"Engine '{engine}' is not available")

        if not hasattr(engine_instance, "synthesize_cross_lingual"):
            raise ServiceError(
                400,
                "Engine does not support cross-lingual cloning",
            )

        profile_audio_path = resolve_reference_audio_path(profile_id)
        if not profile_audio_path.exists():
            raise ServiceError(404, f"Profile audio not found: {profile_id}")
        profile_audio_str = str(profile_audio_path)

        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            output_path = tmp.name
        try:
            audio = engine_instance.synthesize_cross_lingual(
                text=text,
                speaker_wav=profile_audio_str,
                source_language=source_language,
                target_language=target_language,
                output_path=output_path,
            )

            if audio is None:
                raise ServiceError(500, "Cross-lingual synthesis failed")

            try:
                with wave.open(output_path, "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sample_rate = wav_file.getframerate()
                    duration = frames / float(sample_rate)
            except (wave.Error, OSError) as wav_err:
                logger.debug(
                    "Could not read duration from %s: %s", output_path, wav_err
                )
                duration = 2.5

            quality_metrics_obj = None
            if calculate_quality:
                try:
                    from backend.ml.models.engine_service import get_engine_service

                    svc = get_engine_service()
                    import soundfile as sf

                    audio_array, sr = sf.read(output_path)
                    metrics = svc.calculate_all_metrics(audio_array, sr)
                    quality_metrics_obj = QualityMetrics(
                        mos_score=metrics.get("mos_score"),
                        similarity=metrics.get("similarity"),
                        naturalness=metrics.get("naturalness"),
                        snr_db=metrics.get("snr_db"),
                    )
                except Exception as e:
                    logger.warning("Quality calculation failed: %s", e)

            audio_id, _cached_path, _meta = create_audio_artifact_from_file(
                output_path,
                created_by="cross_lingual",
                delete_source=True,
            )

            return VoiceSynthesizeResponse(
                audio_id=audio_id,
                audio_url=f"/api/voice/audio/{audio_id}",
                duration=duration,
                quality_score=0.85,
                quality_metrics=quality_metrics_obj,
                routed_engine="openvoice",
            )
        finally:
            if os.path.exists(output_path):
                try:
                    os.unlink(output_path)
                except OSError as unlink_err:
                    logger.debug(
                        "Could not remove temp cross-lingual output %s: %s",
                        output_path,
                        unlink_err,
                    )
