# mypy: disable-error-code="untyped-decorator"
# SAFETY: FastAPI router decorators lack complete type stubs; route handlers are correctly typed.
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
from fastapi.responses import FileResponse

from backend.api.dependencies import require_synthesis_clearance
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
    HAS_QUALITY_OPTIMIZATION,
    EngineConfigServiceDep,
    EngineProcessingException,
    EngineUnavailableException,
    EventType,
    InvalidEngineException,
    ProfileNotFoundException,
    get_config,
    get_engine_breaker,
    instrument_flow,
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

    Thin route: delegates to SynthesisService (canonical synthesis logic).
    Engines are dynamically discovered from engine manifests.
    """
    from backend.voice.services.synthesis_service import SynthesisService

    return await SynthesisService.synthesize(req, request, config_service)


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
        if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
            raise HTTPException(
                status_code=503,
                detail="Engine router not available for multi-pass synthesis",
            )

        # Validate engine
        valid_engines = _shared.engine_router.list_engines()
        requested_engine = req.engine
        engine_id = _normalize_engine_id(requested_engine)
        if engine_id not in valid_engines:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid engine '{requested_engine}'. Available: {', '.join(valid_engines)}",
            )

        # Get engine instance
        engine = _shared.engine_router.get_engine(engine_id)
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
) -> VoiceSynthesizeResponse:
    """
    Synthesize with granular style control (OpenVoice).

    Supports emotion, accent, rhythm, pauses, and intonation control.
    """
    # Demo mode gate (GPT Research Phase 2)
    if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in ("true", "1", "yes"):
        raise HTTPException(status_code=403, detail="Style synthesis disabled in demo mode.")
    if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    if engine != "openvoice":
        raise HTTPException(
            status_code=400,
            detail="Style control is currently only supported for OpenVoice engine",
        )

    try:
        engine_instance = _shared.engine_router.get_engine(engine)
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

        # Calculate quality if requested (use _shared.quality_metrics set by _ensure_engine_router)
        quality_metrics = None
        if calculate_quality and _shared.quality_metrics:
            try:
                import soundfile as sf

                audio_array, sr = sf.read(output_path)
                metrics = _shared.quality_metrics["calculate_all"](audio_array, sr)
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
) -> VoiceSynthesizeResponse:
    """
    Zero-shot cross-lingual voice cloning (OpenVoice).

    Clones voice from source language to target language.
    """
    # Demo mode gate (GPT Research Phase 2)
    if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in ("true", "1", "yes"):
        raise HTTPException(status_code=403, detail="Cross-lingual synthesis disabled in demo mode.")
    if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    if engine != "openvoice":
        raise HTTPException(
            status_code=400,
            detail="Cross-lingual cloning is currently only supported for OpenVoice engine",
        )

    try:
        engine_instance = _shared.engine_router.get_engine(engine)
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
            if calculate_quality and _shared.quality_metrics:
                try:
                    import soundfile as sf

                    audio_array, sr = sf.read(output_path)
                    metrics = _shared.quality_metrics["calculate_all"](audio_array, sr)
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
            # ALLOWED: bare except - best effort, failure acceptable
            except OSError:
                pass

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Cross-lingual synthesis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Synthesis failed: {e!s}")


# Synthesis route delegates to SynthesisService. No handler registration.
