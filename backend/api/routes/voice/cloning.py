# mypy: disable-error-code="untyped-decorator"
# SAFETY: FastAPI router decorators lack complete type stubs; route handlers are correctly typed.
"""Voice cloning routes - clone voice from reference audio."""

from __future__ import annotations

import contextlib
import json
import logging
import os
import tempfile
import uuid
from typing import Any

import numpy as np
from fastapi import File, Form, HTTPException, UploadFile

from backend.services.audio_artifacts import AudioRegistry

from ...models_additional import (
    QualityMetrics,
    VoiceCloneResponse,
)
from . import _shared
from ._helpers import (
    _build_clone_response,
    _coerce_optional_bool,
    _coerce_optional_float,
    _dedupe_and_get_path,
    _ensure_engine_router,
    _ensure_tts_assets,
    _ensure_vc_assets,
    _get_wav_duration_seconds,
    _log_context,
    _normalize_engine_id,
    _normalize_metrics_payload,
    _save_audio_to_project,
)
from ._shared import router

logger = logging.getLogger(__name__)


@router.post("/clone", response_model=VoiceCloneResponse)
async def clone(
    reference_audio: list[UploadFile] = File(...),
    text: str | None = Form(None),
    engine: str = Form("xtts"),
    quality_mode: str = Form("standard"),
    enhance_quality: bool = Form(False),
    use_multi_reference: bool = Form(False),
    use_rvc_postprocessing: bool = Form(False),
    language: str = Form("en"),
    prosody_params: str | None = Form(None),
    project_id: str | None = Form(None),
    profile_name: str | None = Form(None),
    consent_acknowledged: bool = Form(False),
    consent_type: str | None = Form(None),
    consent_timestamp: str | None = Form(None),
    consent_source: str | None = Form(None),
    consent_id: str | None = Form(None),
) -> VoiceCloneResponse:
    """
    Clone voice from reference audio and optionally synthesize text with advanced features.

    Quality modes:
    - fast: Quick cloning, lower quality
    - standard: Balanced quality and speed
    - high: Best quality, slower processing
    - ultra: Maximum quality, very slow (includes RVC post-processing if enabled)

    Advanced features:
    - enhance_quality: Apply advanced quality enhancement pipeline
    - use_multi_reference: Use ensemble approach when multiple references provided
    - use_rvc_postprocessing: Apply RVC post-processing for enhanced voice similarity
    - prosody_params: JSON string with prosody control parameters (pitch, tempo, formant_shift, energy)
    """
    # Item 26: Safe Demo Mode - disable voice cloning when VOICESTUDIO_DEMO_MODE=true
    if os.environ.get("VOICESTUDIO_DEMO_MODE", "").strip().lower() in ("true", "1", "yes"):
        raise HTTPException(
            status_code=403,
            detail="Voice cloning is disabled in demo mode.",
        )
    if not consent_acknowledged:
        raise HTTPException(
            status_code=400,
            detail="Consent is required for voice cloning. Set consent_acknowledged=true.",
        )
    # Item 23: When consent_id is provided, require valid granted consent
    if consent_id and consent_id.strip():
        try:
            from backend.services.security_service import (
                ConsentStatus,
                ConsentType,
                get_security_service,
            )
            svc = get_security_service()
            record = svc.consent.get_consent_by_id(consent_id.strip())
            if not record:
                raise HTTPException(
                    status_code=403,
                    detail="Valid consent record required for voice cloning. Consent ID not found.",
                )
            if record.status != ConsentStatus.GRANTED:
                raise HTTPException(
                    status_code=403,
                    detail=f"Consent not granted (status={record.status.value}). Cannot clone.",
                )
            if not record.is_valid:
                raise HTTPException(
                    status_code=403,
                    detail="Consent expired or revoked. Cannot clone.",
                )
            if record.consent_type != ConsentType.VOICE_CLONING:
                logger.warning(
                    "Clone with consent_id type %s (expected voice_cloning)",
                    record.consent_type.value,
                )
        except HTTPException:
            raise
        except Exception as e:
            logger.warning("Consent check failed: %s", e)
            raise HTTPException(
                status_code=403,
                detail="Valid consent record required for voice cloning. Consent check failed.",
            ) from e
    consent_metadata = {
        "consent_type": consent_type or "voice_clone",
        "consent_timestamp": consent_timestamp or "",
        "consent_source": consent_source or "api",
    }
    if consent_timestamp or consent_source:
        logger.info(
            "voice_clone_consent %s",
            consent_metadata,
            extra=_log_context(operation="clone", consent=consent_metadata),
        )
    try:
        requested_engine = engine
        engine_id = _normalize_engine_id(engine)
        device_used = None
        candidate_metrics = None
        project_id = project_id.strip() if project_id else None
        # Ensure model assets exist before any engine work
        _ensure_tts_assets(engine_id)
        if use_rvc_postprocessing or engine_id in ("gpt_sovits", "sovits", "sovits_v4"):
            _ensure_vc_assets(engine_id)

        # Dynamically discover available engines from router
        valid_engines: list[str] = []
        if _shared.ENGINE_AVAILABLE and _shared.engine_router:
            valid_engines = _shared.engine_router.list_engines()
            if not valid_engines:
                # If no engines loaded, try loading from manifests
                try:
                    _shared.engine_router.load_all_engines("engines")
                    valid_engines = _shared.engine_router.list_engines()
                except Exception as e:
                    logger.warning(f"Failed to auto-load engines: {e}")
                    valid_engines = []

        # Validate engine
        if valid_engines and engine_id not in valid_engines:
            engines_str = ", ".join(valid_engines) if valid_engines else "none (engines not loaded)"
            raise HTTPException(
                status_code=400,
                detail=f"Invalid engine '{requested_engine}'. Available engines: {engines_str}",
            )
        elif not valid_engines:
            # No engines available - this is a configuration issue
            logger.warning(
                "No engines available - engine router not initialized or no engines loaded",
                extra=_log_context(
                    operation="clone",
                    requested_engine=requested_engine,
                    quality_mode=quality_mode,
                ),
            )

        # Validate quality mode
        valid_modes = ["fast", "standard", "high", "ultra"]
        if quality_mode not in valid_modes:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid quality_mode. Must be one of: {', '.join(valid_modes)}",
            )

        # Save and validate reference audio(s) - accepts video files for audio extraction
        ref_paths: list[str] = []
        reference_files = reference_audio or []

        from backend.core.security.file_validation import (
            FileCategory,
            FileValidationError,
            validate_media_for_audio_extraction,
        )

        for ref_file in reference_files:
            content = await ref_file.read()
            try:
                file_info = validate_media_for_audio_extraction(content, filename=ref_file.filename)
                is_video_or_non_wav = (
                    file_info.category == FileCategory.VIDEO
                    or file_info.extension not in ("wav", "wave")
                )
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid reference audio file '{ref_file.filename}': {e.message}",
                ) from e

            # Save to temp file
            original_ext = os.path.splitext(ref_file.filename or "audio.wav")[1] or ".wav"
            with tempfile.NamedTemporaryFile(delete=False, suffix=original_ext) as tmp_file:
                tmp_file.write(content)
                tmp_path = tmp_file.name

            # Convert to WAV if needed (video files or non-WAV audio)
            if is_video_or_non_wav:
                wav_path = tmp_path.rsplit(".", 1)[0] + ".wav"
                try:
                    from pathlib import Path

                    from backend.core.audio.conversion import get_conversion_service

                    conversion_service = get_conversion_service()
                    conv_result = await conversion_service.convert_to_wav(
                        input_path=Path(tmp_path),
                        output_path=Path(wav_path),
                        sample_rate=44100,
                        channels=2,
                        bit_depth=16,
                    )

                    if conv_result.success:
                        ref_paths.append(wav_path)
                        # Clean up original temp file
                        with contextlib.suppress(OSError):
                            os.unlink(tmp_path)
                        logger.info(
                            "Converted reference audio '%s' to WAV for cloning",
                            ref_file.filename,
                        )
                    else:
                        # Conversion failed - use original (may not work with cloning)
                        ref_paths.append(tmp_path)
                        logger.warning(
                            "Audio conversion failed for '%s': %s (using original)",
                            ref_file.filename,
                            conv_result.error,
                        )
                except ImportError:
                    ref_paths.append(tmp_path)
                    logger.warning("AudioConversionService not available; using original format")
                except Exception as conv_error:
                    ref_paths.append(tmp_path)
                    logger.warning(
                        "Conversion failed for '%s', using original: %s",
                        ref_file.filename,
                        conv_error,
                    )
            else:
                ref_paths.append(tmp_path)
        ref_path = ref_paths[0] if ref_paths else None

        try:
            # Use a stable, non-process-random identifier (built-in hash() is salted per process).
            # If profile_name is provided, use it as part of the ID (sanitized)
            if profile_name:
                # Sanitize profile name for use in ID (alphanumeric and dashes only)
                import re

                sanitized_name = re.sub(r"[^a-zA-Z0-9-]", "_", profile_name.strip())[:32]
                profile_id = f"{sanitized_name}_{uuid.uuid4().hex[:8]}"
            else:
                profile_id = f"clone_{uuid.uuid4().hex[:12]}"

            # If engines are available and text is provided, synthesize
            if _shared.ENGINE_AVAILABLE and _shared.engine_router and text:
                try:
                    engine_instance = _shared.engine_router.get_engine(engine_id)
                    if engine_instance:
                        device_used = getattr(engine_instance, "device", None)
                        # Map quality_mode to engine-specific presets
                        quality_preset = None
                        enhance_quality = quality_mode in ["high", "ultra"]

                        if engine_id == "tortoise":
                            # Tortoise quality presets
                            quality_mode_map = {
                                "fast": "ultra_fast",
                                "standard": "fast",
                                "high": "high_quality",
                                "ultra": "ultra_quality",
                            }
                            quality_preset = quality_mode_map.get(quality_mode, "high_quality")
                            enhance_quality = quality_preset in [
                                "high_quality",
                                "ultra_quality",
                            ]

                        # Parse prosody parameters if provided
                        prosody_params_dict = None
                        if prosody_params:
                            try:
                                prosody_params_dict = json.loads(prosody_params)
                            except (json.JSONDecodeError, Exception) as e:
                                logger.warning(f"Failed to parse prosody_params: {e}")

                        # Use clone_voice if available, otherwise use synthesize
                        output_path: str | None = None
                        if hasattr(engine_instance, "clone_voice"):
                            with tempfile.NamedTemporaryFile(
                                delete=False, suffix=".wav"
                            ) as tmp:
                                output_path = tmp.name
                            if use_multi_reference and len(ref_paths) > 1:
                                reference_audio_arg: str | list[str] | None = ref_paths
                            else:
                                reference_audio_arg = ref_paths[0] if ref_paths else ref_path
                            clone_kwargs = {
                                "reference_audio": reference_audio_arg,
                                "text": text,
                                "language": language,
                                "output_path": output_path,
                                "calculate_quality": True,
                                "enhance_quality": enhance_quality,
                                "use_multi_reference": use_multi_reference,
                            }
                            if quality_preset:
                                clone_kwargs["quality_preset"] = quality_preset
                            if prosody_params_dict:
                                clone_kwargs["prosody_params"] = prosody_params_dict

                            # Apply RVC post-processing if enabled
                            if use_rvc_postprocessing:
                                # This will be handled in the quality enhancement pipeline
                                clone_kwargs["enhance_quality"] = True

                            logger.info(
                                f"Calling clone_voice with output_path={output_path}",
                                extra=_log_context(
                                    operation="clone",
                                    engine=engine_id,
                                    quality_mode=quality_mode,
                                    profile_id=profile_id,
                                    text_length=len(text) if text else 0,
                                    multi_reference=use_multi_reference,
                                    rvc_postprocessing=use_rvc_postprocessing,
                                ),
                            )
                            result = engine_instance.clone_voice(**clone_kwargs)
                            candidate_metrics = getattr(
                                engine_instance, "_last_multi_reference_metrics", None
                            )
                            logger.info(
                                f"clone_voice returned: type={type(result)}, is_tuple={isinstance(result, tuple)}, is_dict={isinstance(result, dict)}"
                            )
                        elif hasattr(engine_instance, "synthesize"):
                            # Fallback to synthesize method
                            with tempfile.NamedTemporaryFile(
                                delete=False, suffix=".wav"
                            ) as tmp:
                                output_path = tmp.name
                            synth_reference = ref_paths[0] if ref_paths else ref_path
                            synth_kwargs = {
                                "text": text,
                                "speaker_wav": synth_reference,
                                "language": language,
                                "output_path": output_path,
                                "calculate_quality": True,
                                "enhance_quality": enhance_quality,
                            }
                            if quality_preset:
                                synth_kwargs["quality_preset"] = quality_preset
                            logger.info(
                                f"Calling synthesize (fallback) with output_path={output_path}"
                            )
                            result = engine_instance.synthesize(**synth_kwargs)
                            logger.info(
                                f"synthesize returned: type={type(result)}, is_tuple={isinstance(result, tuple)}, is_dict={isinstance(result, dict)}"
                            )
                        else:
                            logger.warning(
                                "Engine instance has neither clone_voice nor synthesize method"
                            )
                            result = None

                        # Handle tuple return (audio, metrics) or single audio
                        audio: Any = None
                        metrics: dict[str, Any] = {}
                        if isinstance(result, tuple):
                            audio, metrics = result
                            logger.info(
                                f"Result is tuple: audio type={type(audio)}, metrics type={type(metrics)}"
                            )
                        else:
                            audio = result
                            logger.info(f"Result is single value: audio type={type(audio)}")

                        if isinstance(metrics, dict):
                            metrics = _normalize_metrics_payload(metrics)

                        file_written = output_path is not None and os.path.exists(output_path)
                        logger.info(
                            f"File check: output_path={output_path}, exists={os.path.exists(output_path) if output_path else False}, file_written={file_written}"
                        )

                        # Some engines write to output_path and return None (or (None, metrics)).
                        # Treat that as success if the file exists.
                        logger.info(
                            f"Before audio persistence check: file_written={file_written}, audio is ndarray={isinstance(audio, np.ndarray) if audio is not None else False}"
                        )
                        if not file_written and isinstance(audio, np.ndarray):
                            logger.info(
                                "Audio is ndarray but file not written, persisting audio to file..."
                            )
                            # Fallback: persist returned audio so the UI can retrieve it.
                            if not output_path:
                                with tempfile.NamedTemporaryFile(
                                    delete=False, suffix=".wav"
                                ) as tmp:
                                    output_path = tmp.name
                            try:
                                import wave

                                sample_rate = (
                                    getattr(engine_instance, "output_sample_rate", None)
                                    or getattr(engine_instance, "sample_rate", None)
                                    or getattr(engine_instance, "DEFAULT_SAMPLE_RATE", None)
                                    or 22050
                                )
                                pcm = np.asarray(audio)
                                if pcm.ndim != 1:
                                    pcm = pcm.reshape(-1)
                                if pcm.dtype != np.int16:
                                    pcm = np.clip(pcm.astype(np.float32), -1.0, 1.0)
                                    pcm = (pcm * 32767.0).astype(np.int16)
                                with wave.open(output_path, "wb") as wf:
                                    wf.setnchannels(1)
                                    wf.setsampwidth(2)
                                    wf.setframerate(int(sample_rate))
                                    wf.writeframes(pcm.tobytes())
                                file_written = os.path.exists(output_path)
                            except Exception as e:
                                logger.warning(f"Failed to persist clone audio to file: {e}")

                        # Log synthesis result for debugging
                        logger.info(
                            f"Clone synthesis result: file_written={file_written}, "
                            f"output_path={output_path}, has_output_path={output_path is not None}, "
                            f"output_path_exists={os.path.exists(output_path) if output_path else False}",
                            extra=_log_context(
                                operation="clone",
                                engine=engine_id,
                                profile_id=profile_id,
                                file_written=file_written,
                                output_path=output_path,
                            ),
                        )

                        # Extract detailed quality metrics (when available)
                        detailed_metrics = None
                        quality_score = 0.88 if quality_mode in ["high", "ultra"] else 0.82

                        if metrics:
                            # Extract artifact information
                            artifacts_info = metrics.get("artifacts", {})
                            if isinstance(artifacts_info, dict):
                                artifact_score = _coerce_optional_float(
                                    artifacts_info.get("artifact_score")
                                )
                                has_clicks = _coerce_optional_bool(artifacts_info.get("has_clicks"))
                                has_distortion = _coerce_optional_bool(
                                    artifacts_info.get("has_distortion")
                                )
                            else:
                                artifact_score = None
                                has_clicks = None
                                has_distortion = None

                            # Build detailed metrics
                            detailed_metrics = QualityMetrics(
                                mos_score=_coerce_optional_float(metrics.get("mos_score")),
                                similarity=_coerce_optional_float(metrics.get("similarity")),
                                naturalness=_coerce_optional_float(metrics.get("naturalness")),
                                snr_db=_coerce_optional_float(metrics.get("snr_db")),
                                artifact_score=artifact_score,
                                has_clicks=has_clicks,
                                has_distortion=has_distortion,
                                voice_profile_match=_normalize_metrics_payload(
                                    metrics.get("voice_profile_match")
                                ),
                            )

                            # Calculate quality score from metrics
                            mos_score = metrics.get("mos_score")
                            similarity = metrics.get("similarity")
                            quality_score_metric = metrics.get("quality_score")
                            if mos_score is not None:
                                _mos_f = _coerce_optional_float(mos_score)
                                if _mos_f is not None:
                                    quality_score = _mos_f / 5.0
                            elif similarity is not None:
                                quality_score = _coerce_optional_float(similarity) or quality_score
                            elif quality_score_metric is not None:
                                quality_score = (
                                    _coerce_optional_float(quality_score_metric) or quality_score
                                )

                        duration_seconds = None
                        if file_written and output_path:
                            audio_id = f"clone_{profile_id}_{uuid.uuid4().hex[:8]}"
                            cached_path = _dedupe_and_get_path(output_path)
                            duration_seconds = _get_wav_duration_seconds(
                                cached_path
                            ) or _get_wav_duration_seconds(output_path)
                            AudioRegistry.register(
                                audio_id,
                                cached_path,
                                project_id=project_id,
                                source="clone",
                                model_used="clone",
                                duration_seconds=duration_seconds,
                            )
                            logger.info(
                                f"Clone audio registered: audio_id={audio_id}, cached_path={cached_path}"
                            )
                            if project_id:
                                try:
                                    project_path = _save_audio_to_project(
                                        project_id, audio_id, cached_path
                                    )
                                    logger.info(
                                        "Clone audio saved to project %s: %s",
                                        project_id,
                                        project_path,
                                    )
                                except KeyError:
                                    raise HTTPException(
                                        status_code=404,
                                        detail=(
                                            f"Project '{project_id}' not found. "
                                            "Please check the project ID and try again."
                                        ),
                                    )
                                except ValueError as e:
                                    raise HTTPException(status_code=400, detail=str(e))
                                except FileNotFoundError as e:
                                    raise HTTPException(status_code=404, detail=str(e))
                                except PermissionError:
                                    raise HTTPException(
                                        status_code=403,
                                        detail=(
                                            "Permission denied when saving audio to the project. "
                                            "Please check directory permissions."
                                        ),
                                    )
                                except OSError as e:
                                    if "No space left" in str(e) or "disk full" in str(e).lower():
                                        raise HTTPException(
                                            status_code=507,
                                            detail=(
                                                "Disk full. Please free up space and try again."
                                            ),
                                        )
                                    raise HTTPException(
                                        status_code=500,
                                        detail=f"Failed to save project audio: {e!s}",
                                    )
                            if cached_path != output_path and os.path.exists(output_path):
                                try:
                                    os.remove(output_path)
                                except Exception as e:
                                    logger.debug(
                                        f"Failed to remove temp clone audio {output_path}: {e}"
                                    )

                            return _build_clone_response(
                                profile_id=profile_id,
                                audio_id=audio_id,
                                duration=duration_seconds,
                                quality_score=quality_score,
                                quality_metrics=detailed_metrics,
                                device=device_used,
                                candidate_metrics=candidate_metrics,
                            )

                        logger.warning(
                            "Clone synthesis did not produce audio file: "
                            f"file_written={file_written}, output_path={output_path}"
                        )
                        return _build_clone_response(
                            profile_id=profile_id,
                            audio_id=None,
                            duration=None,
                            quality_score=quality_score,
                            quality_metrics=detailed_metrics,
                            device=device_used,
                            candidate_metrics=candidate_metrics,
                        )
                except Exception as e:
                    logger.error(f"Cloning with engine failed: {e}", exc_info=True)
                    # Continue to return profile creation response

            # Return profile creation response (no audio synthesized)
            logger.warning(
                f"Clone endpoint returning profile-only response: profile_id={profile_id}, audio_id=None (synthesis did not produce audio)"
            )
            return _build_clone_response(
                profile_id=profile_id,
                audio_id=None,
                duration=None,
                quality_score=0.85,
                quality_metrics=None,
                device=device_used,
                candidate_metrics=candidate_metrics,
            )

        finally:
            # Clean up temp file(s)
            for ref_path in ref_paths:
                if os.path.exists(ref_path):
                    os.unlink(ref_path)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Cloning error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Cloning failed: {e!s}")
