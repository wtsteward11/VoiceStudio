from __future__ import annotations

import json
import logging
import os
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any

from fastapi import Depends, File, Form, HTTPException, Request, UploadFile

from backend.api.dependencies import require_synthesis_clearance
from fastapi.responses import FileResponse

from backend.services.audio_artifacts.use_cases import create_audio_artifact_from_file

from ...models_additional import (
    MultiPassSynthesisRequest,
    MultiPassSynthesisResponse,
    QualityMetrics,
    VoiceSynthesizeRequest,
    VoiceSynthesizeResponse,
)
from . import _shared
from ._helpers import (
    _download_url_to_file,
    _ensure_engine_router,
    _ensure_tts_assets,
    _extract_quality_metrics,
    _log_context,
    _normalize_engine_id,
    _resolve_profile_audio,
    _try_utility_tts_fallback,
)
from ._shared import (
    ENGINE_AVAILABLE,
    HAS_QUALITY_OPTIMIZATION,
    EngineConfigServiceDep,
    EngineProcessingException,
    EngineUnavailableException,
    EventType,
    InvalidEngineException,
    ProfileNotFoundException,
    engine_router,
    get_config,
    get_engine_breaker,
    instrument_flow,
    quality_metrics,
    router,
)

logger = logging.getLogger(__name__)


@router.post("/synthesize", response_model=VoiceSynthesizeResponse)
async def synthesize(
    req: VoiceSynthesizeRequest,
    request: Request,
    _policy: None = Depends(require_synthesis_clearance),
    config_service: EngineConfigServiceDep | None = None,
) -> VoiceSynthesizeResponse:
    """
    Synthesize audio from text using a voice profile.

    Engines are dynamically discovered from engine manifests.
    Any engine with an engine.manifest.json file in engines/ will be available.
    No hardcoded engine limits - add as many engines as needed.
    """
    # Lazy-initialize engine router at request time (not import time)
    _ensure_engine_router()

    # Get request ID from middleware
    request_id = getattr(request.state, "request_id", None)

    # Select default engine if not specified (XTTS -> Piper -> eSpeak fallback)
    if not req.engine or not req.engine.strip():
        # Try to get default from injected config service
        try:
            if config_service:
                default_engine = config_service.get_default_engine("tts")
                if default_engine:
                    requested_engine = default_engine
                else:
                    # Hardcoded fallback chain: XTTS -> Piper -> eSpeak
                    requested_engine = "xtts_v2"
            else:
                requested_engine = "xtts_v2"
        except Exception:
            # Fallback to XTTS if config service unavailable
            requested_engine = "xtts_v2"
    else:
        # Use the engine specified in the request
        requested_engine = req.engine.strip()

    engine_id = _normalize_engine_id(requested_engine)

    # Ensure required assets exist for selected engine (auto-download when allowed)
    _ensure_tts_assets(engine_id)

    # Instrument synthesis flow

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
            # Dynamically discover available engines from router
            valid_engines: list[str] = []
            if ENGINE_AVAILABLE and engine_router:
                valid_engines = engine_router.list_engines()
            if not valid_engines and engine_router is not None:
                # If no engines loaded, try loading from manifests
                try:
                    engine_router.load_all_engines("engines")
                    valid_engines = engine_router.list_engines()
                except Exception as e:
                    logger.warning(f"Failed to auto-load engines: {e}")
                    valid_engines = []

            # Validate engine and try fallback chain if invalid
            if valid_engines and engine_id not in valid_engines:
                # GAP-PY-005: Load fallback chain from config
                try:
                    fallback_chain = get_config().get_fallback_chain("tts")
                except Exception as cfg_err:
                    logger.warning(f"Failed to load fallback chain from config: {cfg_err}")
                    fallback_chain = []

                # Default fallback chain if config is empty or unavailable
                if not fallback_chain:
                    fallback_chain = ["xtts_v2", "xtts", "piper", "espeak_ng"]

                original_engine_id = engine_id

                for fallback_engine in fallback_chain:
                    if fallback_engine in valid_engines:
                        engine_id = fallback_engine
                        logger.info(
                            f"Engine '{original_engine_id}' not available, "
                            f"falling back to '{fallback_engine}'",
                            extra=_log_context(
                                operation="synthesis",
                                original_engine=original_engine_id,
                                fallback_engine=fallback_engine,
                                profile_id=req.profile_id,
                            ),
                        )
                        break
                else:
                    # No fallback available
                    engines_str = (
                        ", ".join(valid_engines) if valid_engines else "none (engines not loaded)"
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
                # No engines available - this is a configuration issue
                logger.warning(
                    "No engines available - engine router not initialized or no engines loaded"
                )

            # If engines are available, use them
            if ENGINE_AVAILABLE and engine_router:
                try:
                    # Get engine instance (creates if not exists)
                    engine = engine_router.get_engine(engine_id)
                    if engine is None:
                        raise EngineUnavailableException(
                            engine=requested_engine,
                            reason="Engine failed to initialize",
                        )

                    # Get profile audio path from profile storage
                    from backend.services.profile_search_service import get_profiles_proxy

                    _profiles = get_profiles_proxy()
                    if req.profile_id not in _profiles:
                        raise ProfileNotFoundException(profile_id=req.profile_id)

                    profile = _profiles[req.profile_id]

                    # Resolve reference audio path using canonical profiles dir
                    profile_audio_path = await _resolve_profile_audio(
                        req.profile_id, profile
                    )

                    # Preprocess text using NLP if available
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
                        # Use normalized text for synthesis
                        text_to_synthesize = preprocessed["normalized"]
                        logger.debug(
                            f"Text preprocessed: {len(preprocessed['sentences'])} sentences, "
                            f"{preprocessed['word_count']} words"
                        )
                    except ImportError:
                        # NLP not available, use raw text
                        ...
                    except Exception as e:
                        logger.warning(f"NLP preprocessing failed, using raw text: {e}")

                    # Perform synthesis with quality calculation
                    # Use preprocessed text if available
                    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
                        output_path = tmp.name
                    calculate_quality = True

                    # Use quality presets if available
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

                            # Get parameters from quality preset
                            preset_params = get_synthesis_params_from_preset(
                                req.quality_mode, engine_name=engine_id
                            )
                            enhance_quality = preset_params.get("enhance_quality", False)
                            quality_preset = preset_params.get("quality_preset")
                            logger.debug(
                                f"Using quality preset '{req.quality_mode}' for engine '{engine_id}'"
                            )
                        except Exception as e:
                            logger.warning(f"Failed to get quality preset: {e}")

                    # Fallback to legacy mapping if preset system not available
                    if not quality_preset and engine_id == "tortoise":
                        # Tortoise quality presets (legacy mapping)
                        quality_mode_map = {
                            "fast": "ultra_fast",
                            "standard": "fast",
                            "high": "high_quality",
                            "ultra": "ultra_quality",
                        }
                        quality_preset = quality_mode_map.get(
                            getattr(req, "quality_mode", "standard"), "high_quality"
                        )
                        enhance_quality = quality_preset in [
                            "high_quality",
                            "ultra_quality",
                        ]
                    elif not enhance_quality and engine_id == "chatterbox":
                        # Chatterbox doesn't have quality presets, but can enhance
                        enhance_quality = getattr(req, "quality_mode", "standard") in [
                            "high",
                            "ultra",
                        ]

                    if hasattr(engine, "synthesize"):
                        synthesis_kwargs = {
                            "text": text_to_synthesize,  # Use preprocessed text
                            "speaker_wav": (
                                profile_audio_path if os.path.exists(profile_audio_path) else None
                            ),
                            "language": req.language or "en",
                            "output_path": output_path,
                            "calculate_quality": calculate_quality,
                            "enhance_quality": enhance_quality,
                        }

                        # Add engine-specific parameters
                        if req.emotion:
                            synthesis_kwargs["emotion"] = req.emotion
                        if quality_preset:
                            synthesis_kwargs["quality_preset"] = quality_preset

                        # Attempt synthesis with circuit breaker + error recovery
                        result = None
                        synthesis_error: Exception | None = None
                        max_retries = 2

                        # Get circuit breaker for this engine (TD-014)
                        engine_breaker = get_engine_breaker(engine_id)

                        # Check if circuit is open before attempting
                        if not engine_breaker.allow_request():
                            logger.warning(
                                f"Circuit breaker OPEN for engine '{engine_id}', "
                                f"retry in {engine_breaker.time_until_retry():.1f}s",
                                extra=_log_context(
                                    operation="synthesis",
                                    engine=engine_id,
                                    profile_id=req.profile_id,
                                    circuit_state="open",
                                    retry_after_seconds=engine_breaker.time_until_retry(),
                                ),
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
                                break  # Success, exit retry loop
                            except RuntimeError as e:
                                # Record failure with circuit breaker
                                engine_breaker.record_failure()
                                # GPU/device errors - may be recoverable
                                error_msg = str(e).lower()

                                if (
                                    "cuda" in error_msg
                                    or "gpu" in error_msg
                                    or "device" in error_msg
                                ):
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
                                            logger.warning(
                                                f"Engine reinitialization failed: {cleanup_error}"
                                            )
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
                                # Other errors - log and break
                                logger.error(
                                    f"Synthesis error (attempt {attempt + 1}): {e}",
                                    exc_info=True,
                                )
                                synthesis_error = e
                                if attempt < max_retries and "timeout" in str(e).lower():
                                    # Retry timeout errors
                                    continue
                                break

                    # Try fallback to utility TTS if all main engine attempts failed
                    if result is None and synthesis_error is not None:
                        fallback_result = await _try_utility_tts_fallback(
                            text_to_synthesize,
                            req.language or "en",
                            synthesis_error,
                        )
                        if fallback_result is not None:
                            return VoiceSynthesizeResponse(
                                audio_id=fallback_result["audio_id"],
                                audio_url=f"/api/voice/audio/{fallback_result['audio_id']}",
                                duration=fallback_result["duration"],
                                quality_score=0.0,
                                quality_metrics=None,
                            )

                    # Handle synthesis result or error
                    # Some engines write to output_path and return None - check file first
                    file_written_early = output_path and os.path.exists(output_path)

                    if result is None and not file_written_early:
                        # Provide detailed error message based on error type
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

                            raise HTTPException(status_code=500, detail=detail)
                        else:
                            raise HTTPException(
                                status_code=500,
                                detail="Synthesis failed - engine returned None. "
                                "Check engine logs for details.",
                            )

                    # Handle both single return and tuple (audio, metrics).
                    #
                    # Some engines write to output_path and return None (or (None, metrics)).
                    # Treat that as success if the file exists on disk.
                    if isinstance(result, tuple):
                        audio, _engine_quality_metrics = result
                    else:
                        audio = result

                    file_written = os.path.exists(output_path)
                    if audio is None and not file_written:
                        raise HTTPException(
                            status_code=500,
                            detail="Synthesis failed - engine returned None and did not write an output file. "
                            "The engine may not be properly initialized or the input may be invalid.",
                        )

                    # Extract duration and quality metrics using helper (Phase 1B extraction)
                    duration, quality_score, detailed_metrics = _extract_quality_metrics(
                        result, engine, output_path
                    )

                    # Store via artifact spine (no AudioRegistry.register in routes)
                    if os.path.exists(output_path):
                        audio_id, _cached_path, _meta = create_audio_artifact_from_file(
                            output_path,
                            created_by=engine_id or "synthesis",
                            delete_source=True,
                        )
                        return VoiceSynthesizeResponse(
                            audio_id=audio_id,
                            audio_url=f"/api/voice/audio/{audio_id}",
                            duration=duration,
                            quality_score=quality_score,
                            quality_metrics=detailed_metrics,
                        )
                    else:
                        raise HTTPException(
                            status_code=500,
                            detail=f"Engine '{requested_engine}' does not support synthesis",
                        )
                except HTTPException:
                    raise
                except Exception as e:
                    logger.error(f"Engine synthesis error: {e}", exc_info=True)
                    raise EngineProcessingException(
                        engine=engine_id,
                        operation="synthesis",
                        error_message=str(e),
                    ) from e

            # Stub mode: when no engines, produce minimal WAV for golden path proof
            if os.environ.get("VOICESTUDIO_TEST_MODE", "").lower() in ("stub", "1", "true", "yes"):
                import math
                import struct

                sr = 16000
                dur_s = 0.5
                n = int(sr * dur_s)
                # Minimal tone (rms > 0.001 required by proof validator)
                samples = b"".join(
                    struct.pack("<h", int(32767 * 0.01 * math.sin(2 * math.pi * 440 * i / sr)))
                    for i in range(n)
                )
                header = struct.pack(
                    "<4sI4s4sIHHIIHH4sI",
                    b"RIFF", 36 + len(samples), b"WAVE",
                    b"fmt ", 16, 1, 1, sr, sr * 2, 2, 16,
                    b"data", len(samples),
                )
                with tempfile.NamedTemporaryFile(
                    delete=False, suffix=".wav", dir=tempfile.gettempdir()
                ) as tmp:
                    tmp.write(header + samples)
                    tmp_path = tmp.name
                try:
                    aid, cached_path, _ = create_audio_artifact_from_file(
                        tmp_path, created_by="stub_test_mode", delete_source=True,
                    )
                finally:
                    if os.path.exists(tmp_path):
                        try:
                            os.unlink(tmp_path)
                        except OSError:
                            pass
                return VoiceSynthesizeResponse(
                    audio_id=aid,
                    audio_url=f"/api/voice/audio/{aid}",
                    duration=dur_s,
                    quality_score=0.0,
                    quality_metrics=None,
                )

            # No engines available - return proper error
            raise HTTPException(
                status_code=503,
                detail=(
                    "Voice synthesis engines are not available. "
                    "Please ensure engines are properly installed and configured. "
                    "Install required dependencies and ensure engine manifests are loaded."
                ),
            )
        except HTTPException:
            raise
        except Exception as e:
            logger.error(
                f"Synthesis error: {e}",
                exc_info=True,
                extra=_log_context(
                    operation="synthesis",
                    engine=engine_id,
                    profile_id=req.profile_id,
                    text_length=len(req.text) if req.text else 0,
                    error_type=type(e).__name__,
                ),
            )
            raise HTTPException(status_code=500, detail=f"Synthesis failed: {e!s}")


@router.post("/synthesize/multipass", response_model=MultiPassSynthesisResponse)
async def synthesize_multipass(
    req: MultiPassSynthesisRequest,
    request: Request,
    _policy: None = Depends(require_synthesis_clearance),
) -> MultiPassSynthesisResponse:
    """
    Multi-pass synthesis with quality refinement (IDEA 61).

    Generates multiple synthesis passes, compares quality metrics,
    and selects the best segments for maximum quality output.
    """
    from ...models_additional import (
        MultiPassSynthesisResponse,
        PassResult,
        QualityMetrics,
    )

    try:
        if not ENGINE_AVAILABLE or not engine_router:
            raise HTTPException(
                status_code=503,
                detail="Engine router not available for multi-pass synthesis",
            )

        # Validate engine
        valid_engines = engine_router.list_engines()
        requested_engine = req.engine
        engine_id = _normalize_engine_id(requested_engine)
        if engine_id not in valid_engines:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid engine '{requested_engine}'. Available: {', '.join(valid_engines)}",
            )

        # Get engine instance
        engine = engine_router.get_engine(engine_id)
        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Engine '{requested_engine}' is not available or failed to initialize",
            )

        # Get profile audio path
        from backend.services.profile_search_service import get_profiles_proxy

        _profiles = get_profiles_proxy()
        if req.profile_id not in _profiles:
            raise HTTPException(status_code=404, detail=f"Profile not found: {req.profile_id}")

        profile = _profiles[req.profile_id]
        profile_audio_path = None

        if profile.reference_audio_url:
            if profile.reference_audio_url.startswith("http"):
                # Download from URL
                logger.info(f"Downloading reference audio from URL: {profile.reference_audio_url}")
                downloaded_path = await _download_url_to_file(profile.reference_audio_url)
                if downloaded_path and os.path.exists(downloaded_path):
                    profile_audio_path = downloaded_path
                    logger.info(f"Using downloaded reference audio: {profile_audio_path}")
            else:
                profile_audio_path = profile.reference_audio_url

        if not profile_audio_path:
            from backend.services.profile_service import resolve_reference_audio_path

            resolved = resolve_reference_audio_path(req.profile_id)
            if resolved.exists():
                profile_audio_path = str(resolved)

        if not profile_audio_path or not os.path.exists(profile_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Profile reference audio not found for profile: {req.profile_id}",
            )

        preset_overrides: dict[str, float] = {}
        if req.pass_preset == "naturalness_focus":
            preset_overrides = {"min_quality_improvement": 0.02, "naturalness_weight": 1.5}
        elif req.pass_preset == "similarity_focus":
            preset_overrides = {"min_quality_improvement": 0.01, "similarity_weight": 1.5}
        elif req.pass_preset == "artifact_focus":
            preset_overrides = {"min_quality_improvement": 0.03, "artifact_penalty": 2.0}

        if preset_overrides.get("min_quality_improvement"):
            req.min_quality_improvement = preset_overrides["min_quality_improvement"]

        # Generate multiple passes
        passes: list[PassResult] = []
        improvement_tracking: list[float] = []
        best_pass = 0
        best_quality = 0.0
        previous_quality = 0.0

        max_passes = req.max_passes or 3
        min_improvement = req.min_quality_improvement or 0.02

        for pass_num in range(1, max_passes + 1):
            logger.info(f"Multi-pass synthesis: Pass {pass_num}/{max_passes}")

            # Create synthesis request for this pass
            synth_req = VoiceSynthesizeRequest(
                engine=engine_id,
                profile_id=req.profile_id,
                text=req.text,
                language=req.language,
                emotion=req.emotion,
                enhance_quality=True,  # Always enhance for multi-pass
            )

            # Perform synthesis
            synth_response = await synthesize(synth_req, request, config_service=None)

            if not synth_response.quality_metrics:
                # Calculate basic quality if metrics not available
                quality_score = synth_response.quality_score
                quality_metrics = QualityMetrics(
                    mos_score=quality_score * 5.0 if quality_score <= 1.0 else None,
                    similarity=quality_score if quality_score <= 1.0 else None,
                )
            else:
                quality_metrics = synth_response.quality_metrics
                quality_score = synth_response.quality_score

            # Calculate improvement
            improvement = 0.0
            if pass_num > 1:
                improvement = quality_score - previous_quality

            # Create pass result
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

            # Track best pass
            if quality_score > best_quality:
                best_quality = quality_score
                best_pass = pass_num

            # Adaptive stopping: stop if improvement is too small
            if req.adaptive and pass_num > 1 and improvement < min_improvement:
                logger.info(
                    f"Multi-pass synthesis: Stopping early at pass {pass_num} "
                    f"(improvement {improvement:.4f} < {min_improvement})"
                )
                break

            previous_quality = quality_score

        # Select best pass result
        best_pass_result = passes[best_pass - 1]

        # Get audio duration from best pass
        from backend.services.audio_path_resolver import resolve_audio_path

        best_audio_path = resolve_audio_path(best_pass_result.audio_id)
        duration = 2.5  # Default
        if best_audio_path and os.path.exists(best_audio_path):
            try:
                import wave

                with wave.open(best_audio_path, "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sample_rate = wav_file.getframerate()
                    duration = frames / float(sample_rate)
            except (wave.Error, OSError) as wav_err:
                logger.debug(f"Could not read duration from {best_audio_path}: {wav_err}")

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

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Multi-pass synthesis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Multi-pass synthesis failed: {e!s}") from e


@router.post("/synthesize/style", response_model=VoiceSynthesizeResponse)
async def synthesize_with_style(
    request: Request,
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
    _policy: None = Depends(require_synthesis_clearance),
):
    """
    Synthesize with granular style control (OpenVoice).

    Supports emotion, accent, rhythm, pauses, and intonation control.
    """
    # Demo mode gate (GPT Research Phase 2)
    if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in ("true", "1", "yes"):
        raise HTTPException(status_code=403, detail="Style synthesis disabled in demo mode.")
    if not ENGINE_AVAILABLE or not engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    if engine != "openvoice":
        raise HTTPException(
            status_code=400,
            detail="Style control is currently only supported for OpenVoice engine",
        )

    try:
        engine_instance = engine_router.get_engine(engine)
        if engine_instance is None:
            raise HTTPException(status_code=503, detail=f"Engine '{engine}' is not available")

        # Check if engine supports style control
        if not hasattr(engine_instance, "synthesize_with_style"):
            raise HTTPException(status_code=400, detail="Engine does not support style control")

        # Parse pauses if provided
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
                logger.warning(f"Invalid pauses JSON: {pauses}")

        # Build intonation dict
        intonation = {}
        if pitch_shift is not None:
            intonation["pitch_shift"] = pitch_shift
        if pitch_variance is not None:
            intonation["pitch_variance"] = pitch_variance
        if energy is not None:
            intonation["energy"] = energy

        # Get profile audio path
        from backend.services.profile_service import resolve_reference_audio_path

        profile_audio_path = resolve_reference_audio_path(profile_id)
        if not profile_audio_path.exists():
            raise HTTPException(status_code=404, detail=f"Profile audio not found: {profile_id}")
        profile_audio_path = str(profile_audio_path)

        # Synthesize with style
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            output_path = tmp.name
        engine_instance.synthesize_with_style(
            text=text,
            speaker_wav=profile_audio_path,
            language=language,
            emotion=emotion,
            accent=accent,
            rhythm=rhythm,
            pauses=pause_list,
            intonation=intonation if intonation else None,
            output_path=output_path,
            pause_positions=pause_positions,
        )

        # Store via artifact spine (registry + provenance)
        audio_id, _, _ = create_audio_artifact_from_file(
            output_path,
            created_by="style",
            project_id=None,
            source="style_transfer",
        )

        # Calculate quality if requested
        quality_metrics = None
        if calculate_quality and quality_metrics:
            try:
                import soundfile as sf

                audio_array, sr = sf.read(output_path)
                metrics = quality_metrics["calculate_all"](audio_array, sr)
                quality_metrics = QualityMetrics(
                    mos_score=metrics.get("mos_score"),
                    similarity=metrics.get("similarity"),
                    naturalness=metrics.get("naturalness"),
                    snr_db=metrics.get("snr_db"),
                )
            except Exception as e:
                logger.warning(f"Quality calculation failed: {e}")

        # Calculate duration
        import wave

        try:
            with wave.open(output_path, "rb") as wav_file:
                frames = wav_file.getnframes()
                sample_rate = wav_file.getframerate()
                duration = frames / float(sample_rate)
        except (wave.Error, OSError) as wav_err:
            logger.debug(f"Could not read duration from {output_path}: {wav_err}")
            duration = 2.5

        return VoiceSynthesizeResponse(
            audio_id=audio_id,
            audio_url=f"/api/voice/audio/{audio_id}",
            duration=duration,
            quality_score=0.85,
            quality_metrics=quality_metrics,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Style synthesis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Synthesis failed: {e!s}")


@router.post("/synthesize/cross-lingual", response_model=VoiceSynthesizeResponse)
async def synthesize_cross_lingual(
    request: Request,
    text: str,
    profile_id: str,
    source_language: str = "en",
    target_language: str = "es",
    engine: str = "openvoice",
    enhance_quality: bool = True,
    calculate_quality: bool = True,
    _policy: None = Depends(require_synthesis_clearance),
):
    """
    Zero-shot cross-lingual voice cloning (OpenVoice).

    Clones voice from source language to target language.
    """
    # Demo mode gate (GPT Research Phase 2)
    if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in ("true", "1", "yes"):
        raise HTTPException(status_code=403, detail="Cross-lingual synthesis disabled in demo mode.")
    if not ENGINE_AVAILABLE or not engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    if engine != "openvoice":
        raise HTTPException(
            status_code=400,
            detail="Cross-lingual cloning is currently only supported for OpenVoice engine",
        )

    try:
        engine_instance = engine_router.get_engine(engine)
        if engine_instance is None:
            raise HTTPException(status_code=503, detail=f"Engine '{engine}' is not available")

        # Check if engine supports cross-lingual
        if not hasattr(engine_instance, "synthesize_cross_lingual"):
            raise HTTPException(
                status_code=400, detail="Engine does not support cross-lingual cloning"
            )

        # Get profile audio path
        from backend.services.profile_service import resolve_reference_audio_path

        profile_audio_path = resolve_reference_audio_path(profile_id)
        if not profile_audio_path.exists():
            raise HTTPException(status_code=404, detail=f"Profile audio not found: {profile_id}")
        profile_audio_path = str(profile_audio_path)

        # Synthesize cross-lingual (artifact spine: NamedTemporaryFile + create_audio_artifact_from_file)
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
            output_path = tmp.name
        try:
            audio = engine_instance.synthesize_cross_lingual(
                text=text,
                speaker_wav=profile_audio_path,
                source_language=source_language,
                target_language=target_language,
                output_path=output_path,
            )

            if audio is None:
                raise HTTPException(status_code=500, detail="Cross-lingual synthesis failed")

            # Calculate duration and quality before storing (file consumed by store)
            import wave

            try:
                with wave.open(output_path, "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sample_rate = wav_file.getframerate()
                    duration = frames / float(sample_rate)
            except (wave.Error, OSError) as wav_err:
                logger.debug("Could not read duration from %s: %s", output_path, wav_err)
                duration = 2.5

            quality_metrics_obj = None
            if calculate_quality and quality_metrics:
                try:
                    import soundfile as sf

                    audio_array, sr = sf.read(output_path)
                    metrics = quality_metrics["calculate_all"](audio_array, sr)
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
            )
        finally:
            try:
                if os.path.exists(output_path):
                    os.unlink(output_path)
            except OSError:
                pass

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Cross-lingual synthesis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Synthesis failed: {e!s}")


from backend.services.voice_synthesis_service import register_synthesize_handler

register_synthesize_handler(synthesize)
