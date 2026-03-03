"""Voice analysis routes - quality metrics, characteristics, and pronunciation testing."""

from __future__ import annotations

import logging
import os
import tempfile
from typing import Any

import numpy as np
from fastapi import File, HTTPException, Request, UploadFile

from ...models_additional import (
    VoiceAnalyzeResponse,
    VoiceCharacteristicAnalysisRequest,
    VoiceCharacteristicAnalysisResponse,
    VoiceCharacteristicData,
)
from ...utils.quality_batch import calculate_batch_quality_score
from . import _shared
from ._helpers import (
    _coerce_optional_float,
    _normalize_metrics_payload,
)
from ._shared import (
    HAS_PITCH_TRACKER,
    router,
)

logger = logging.getLogger(__name__)


@router.post("/analyze", response_model=VoiceAnalyzeResponse)
async def analyze(
    audio_file: UploadFile = File(...),
    reference_audio: UploadFile | None = File(None),
    metrics: str | None = None,
) -> VoiceAnalyzeResponse:
    """
    Analyze audio quality and voice characteristics.

    Metrics:
    - mos: Mean Opinion Score (1-5)
    - similarity: Voice similarity to reference (0-1)
    - naturalness: Naturalness score (0-1)
    """
    try:
        from backend.core.security.file_validation import (
            FileValidationError,
            validate_audio_file,
        )

        # Read and validate uploaded file
        content = await audio_file.read()
        try:
            validate_audio_file(content, filename=audio_file.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid audio file: {e.message}",
            ) from e

        # Save uploaded file temporarily
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
            tmp_file.write(content)
            tmp_path = tmp_file.name

        try:
            # Parse metrics
            metric_list = []
            if metrics:
                metric_list = [m.strip() for m in metrics.split(",")]
            else:
                metric_list = ["mos", "similarity", "naturalness"]

            # Save reference audio if provided
            ref_path = None
            if reference_audio:
                ref_content = await reference_audio.read()
                try:
                    validate_audio_file(ref_content, filename=reference_audio.filename)
                except FileValidationError as e:
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid reference audio file: {e.message}",
                    ) from e
                with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as ref_file:
                    ref_file.write(ref_content)
                    ref_path = ref_file.name

            # Perform analysis using quality metrics if available
            results: dict[str, float] = {}
            missing_deps: list[str] = []
            include_all = "all" in metric_list or len(metric_list) == 0
            metrics_all: dict[str, Any] = {}

            if _shared.quality_metrics and _shared.ENGINE_AVAILABLE:
                try:
                    metrics_all = _shared.quality_metrics["calculate_all"](
                        tmp_path, reference_audio=ref_path if ref_path else None
                    )
                    missing_deps = _normalize_metrics_payload(
                        metrics_all.get("missing_dependencies") or []
                    )
                    if not isinstance(missing_deps, list):
                        missing_deps = [str(missing_deps)]

                    if include_all:
                        for key, value in metrics_all.items():
                            if key in (
                                "missing_dependencies",
                                "artifacts",
                                "voice_profile_match",
                            ):
                                continue
                            if isinstance(value, bool):
                                continue
                            metric_value = _coerce_optional_float(value)
                            if metric_value is not None:
                                results[key] = metric_value

                        artifacts_info = metrics_all.get("artifacts")
                        if isinstance(artifacts_info, dict):
                            artifact_score = artifacts_info.get("artifact_score")
                            if artifact_score is not None and not isinstance(artifact_score, bool):
                                metric_value = _coerce_optional_float(artifact_score)
                                if metric_value is not None:
                                    results["artifact_score"] = metric_value
                    else:
                        metric_map = {
                            "mos": "mos_score",
                            "similarity": "similarity",
                            "naturalness": "naturalness",
                            "snr": "snr_db",
                        }
                        for requested, source_key in metric_map.items():
                            if requested not in metric_list:
                                continue
                            metric_value = metrics_all.get(source_key)
                            if metric_value is None or isinstance(metric_value, bool):
                                continue
                            coerced = _coerce_optional_float(metric_value)
                            if coerced is not None:
                                results[requested] = coerced

                        if "snr" not in results:
                            metric_value = metrics_all.get("snr_db")
                            if metric_value is not None and not isinstance(metric_value, bool):
                                coerced = _coerce_optional_float(metric_value)
                                if coerced is not None:
                                    results["snr"] = coerced

                    if (
                        ref_path is None
                        and ("similarity" in metric_list or include_all)
                        and "similarity" not in results
                    ):
                        try:
                            similarity_value = _shared.quality_metrics["similarity"](tmp_path, tmp_path)
                            if similarity_value is not None and not isinstance(
                                similarity_value, bool
                            ):
                                coerced = _coerce_optional_float(similarity_value)
                                if coerced is not None:
                                    results["similarity"] = coerced
                        except Exception as e:
                            logger.debug(f"Self-similarity calculation failed: {e}")

                except ImportError as e:
                    raise HTTPException(
                        status_code=503,
                        detail=("Quality metrics dependencies are missing. " f"{e!s}"),
                    ) from e
                except Exception as e:
                    logger.warning(f"Quality metrics calculation failed: {e}")
                    raise HTTPException(
                        status_code=500,
                        detail=f"Quality metrics calculation failed: {e!s}",
                    ) from e
            else:
                # Quality metrics not available - return error for requested metrics
                unavailable_metrics = []
                if "mos" in metric_list:
                    unavailable_metrics.append("MOS")
                if "similarity" in metric_list:
                    unavailable_metrics.append("similarity")
                if "naturalness" in metric_list:
                    unavailable_metrics.append("naturalness")
                if "snr" in metric_list:
                    unavailable_metrics.append("SNR")

                raise HTTPException(
                    status_code=503,
                    detail=(
                        f"Quality metrics calculation is not available for: "
                        f"{', '.join(unavailable_metrics)}. "
                        "Please ensure quality metrics libraries are installed. "
                        "Install with: pip install librosa resemblyzer"
                    ),
                )

            # Optional metrics (when dependencies are available)
            analysis_audio = None
            analysis_sr = None
            try:
                import soundfile as sf

                analysis_audio, analysis_sr = sf.read(tmp_path)
                if len(analysis_audio.shape) > 1:
                    analysis_audio = analysis_audio[:, 0]  # Use first channel
            except ImportError:
                missing_deps.append("soundfile (pip install soundfile)")
            except Exception as e:
                logger.debug(f"Audio load failed for analysis metrics: {e}")

            if analysis_audio is not None and analysis_sr is not None:
                # LUFS via pyloudnorm (if available)
                try:
                    import pyloudnorm as pyln

                    meter = pyln.Meter(analysis_sr)
                    lufs_value = float(meter.integrated_loudness(analysis_audio))
                    if np.isfinite(lufs_value):
                        results["lufs"] = lufs_value
                except ImportError:
                    missing_deps.append("pyloudnorm (pip install pyloudnorm)")
                except Exception as e:
                    logger.debug(f"LUFS calculation failed: {e}")

                # Calculate pitch stability using pitch tracking
                if HAS_PITCH_TRACKER:
                    try:
                        from ...audio_processing import PitchTracker

                        pitch_tracker = PitchTracker()
                        if pitch_tracker.crepe_available or pitch_tracker.pyin_available:
                            # Use crepe for higher accuracy, fallback to pyin
                            method = "crepe" if pitch_tracker.crepe_available else "pyin"
                            pitch_data = pitch_tracker.track_pitch(
                                audio_array=analysis_audio,
                                sample_rate=analysis_sr,
                                method=method,
                            )
                            if pitch_data and "f0" in pitch_data:
                                f0_values = pitch_data["f0"]
                                # Remove unvoiced frames (NaN/zero)
                                valid_f0 = f0_values[(f0_values > 0) & ~np.isnan(f0_values)]
                                if len(valid_f0) > 10:
                                    # Calculate coefficient of variation (CV)
                                    # Lower CV = more stable pitch
                                    f0_mean = np.mean(valid_f0)
                                    f0_std = np.std(valid_f0)
                                    if f0_mean > 0:
                                        cv = f0_std / f0_mean
                                        # Convert CV to stability (0-1, higher = stable)
                                        # Typical CV for stable voice: 0.1-0.2
                                        pitch_stability = max(0.0, min(1.0, 1.0 - cv * 2.0))
                                        results["pitch_stability"] = pitch_stability
                    except Exception as e:
                        logger.debug(f"Pitch stability calculation failed: {e}")

            # Compute overall quality score from available metrics
            quality_score = calculate_batch_quality_score(metrics_all)
            if quality_score is None:
                quality_score = calculate_batch_quality_score(
                    {
                        "mos_score": results.get("mos"),
                        "similarity": results.get("similarity"),
                        "naturalness": results.get("naturalness"),
                    }
                )

            if not results:
                if missing_deps:
                    raise HTTPException(
                        status_code=503,
                        detail=(
                            "Quality metrics are unavailable due to missing dependencies: "
                            f"{', '.join(missing_deps)}"
                        ),
                    )
                raise HTTPException(
                    status_code=500,
                    detail="Quality metrics calculation did not produce any results.",
                )

            return VoiceAnalyzeResponse(
                metrics=results,
                quality_score=quality_score,
                missing_dependencies=missing_deps,
            )

        finally:
            # Clean up reference audio temp file
            if ref_path and os.path.exists(ref_path):
                os.unlink(ref_path)
            # Clean up temp file
            if os.path.exists(tmp_path):
                os.unlink(tmp_path)

    except Exception as e:
        logger.error(f"Analysis error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Analysis failed: {e!s}")


@router.post("/analyze-characteristics", response_model=VoiceCharacteristicAnalysisResponse)
async def analyze_voice_characteristics_endpoint(
    req: VoiceCharacteristicAnalysisRequest,
) -> VoiceCharacteristicAnalysisResponse:
    """
    Analyze voice characteristics for preservation and enhancement (IDEA 64).

    Analyzes pitch, formants, timbre, and prosody to preserve voice identity
    during cloning and provide recommendations for enhancement.
    """
    import numpy as np

    try:
        # Get audio file path
        from backend.services.audio_path_resolver import resolve_audio_path

        audio_path = resolve_audio_path(req.audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio file not found: {req.audio_id}")

        # Try to load audio processing libraries
        try:
            import librosa
            import soundfile as sf

            HAS_AUDIO_LIBS = True
        except ImportError:
            HAS_AUDIO_LIBS = False

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio processing libraries not available. Install librosa and soundfile.",
            )

        # Load audio
        audio, sample_rate = sf.read(audio_path)
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)  # Convert to mono

        # Import voice characteristic analysis
        try:
            from backend.audio.audio_utils import (
                analyze_voice_characteristics,
                match_voice_profile,
            )

            # Analyze characteristics
            characteristics_dict = analyze_voice_characteristics(audio, sample_rate)

            # Build characteristic data
            characteristics = VoiceCharacteristicData(
                pitch_mean=characteristics_dict.get("f0_mean"),
                pitch_std=characteristics_dict.get("f0_std"),
                formants=characteristics_dict.get("formants"),
                spectral_centroid=characteristics_dict.get("spectral_centroid"),
                spectral_rolloff=characteristics_dict.get("spectral_rolloff"),
                mfcc=characteristics_dict.get("mfcc"),
                prosody_patterns=(
                    {
                        "pitch_contour": "analyzed",
                        "rhythm": "analyzed",
                        "stress": "analyzed",
                    }
                    if req.include_prosody
                    else None
                ),
            )

            # Analyze reference if provided
            reference_characteristics = None
            similarity_score = None
            preservation_score = None
            recommendations = []

            if req.reference_audio_id:
                ref_path = resolve_audio_path(req.reference_audio_id)
                if ref_path and os.path.exists(ref_path):
                    ref_audio, ref_sr = sf.read(ref_path)
                    if len(ref_audio.shape) > 1:
                        ref_audio = np.mean(ref_audio, axis=1)

                    ref_characteristics_dict = analyze_voice_characteristics(ref_audio, ref_sr)
                    reference_characteristics = VoiceCharacteristicData(
                        pitch_mean=ref_characteristics_dict.get("f0_mean"),
                        pitch_std=ref_characteristics_dict.get("f0_std"),
                        formants=ref_characteristics_dict.get("formants"),
                        spectral_centroid=ref_characteristics_dict.get("spectral_centroid"),
                        spectral_rolloff=ref_characteristics_dict.get("spectral_rolloff"),
                        mfcc=ref_characteristics_dict.get("mfcc"),
                    )

                    # Calculate similarity
                    profile_match = match_voice_profile(ref_audio, audio, ref_sr, sample_rate)
                    similarity_score = profile_match.get("overall_similarity", 0.0)
                    preservation_score = similarity_score  # Use similarity as preservation score

                    # Generate recommendations
                    if similarity_score < 0.7:
                        recommendations.append(
                            "Voice characteristics differ significantly from reference"
                        )
                    if characteristics.pitch_mean and reference_characteristics.pitch_mean:
                        pitch_diff = abs(
                            characteristics.pitch_mean - reference_characteristics.pitch_mean
                        )
                        if pitch_diff > 50:
                            recommendations.append(
                                f"Pitch differs by {pitch_diff:.1f}Hz - consider adjustment"
                            )

            # Additional recommendations
            if characteristics.pitch_std and characteristics.pitch_std > 100:
                recommendations.append("High pitch variation detected - consider prosody control")
            if characteristics.formants:
                if any(f < 100 or f > 5000 for f in characteristics.formants if f):
                    recommendations.append(
                        "Unusual formant frequencies detected - check audio quality"
                    )

            return VoiceCharacteristicAnalysisResponse(
                audio_id=req.audio_id,
                characteristics=characteristics,
                reference_characteristics=reference_characteristics,
                similarity_score=similarity_score,
                preservation_score=preservation_score,
                recommendations=recommendations,
            )

        except ImportError as e:
            logger.error(f"Failed to import voice characteristic functions: {e}")
            raise HTTPException(
                status_code=503,
                detail="Voice characteristic analysis functions not available.",
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Voice characteristic analysis error: {e}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Voice characteristic analysis failed: {e!s}"
        ) from e


# --- Test pronunciation (called by PronunciationLexiconViewModel) ---


@router.post("/test-pronunciation")
async def test_pronunciation(request: Request):
    """Test pronunciation of a word."""
    body = await request.json()
    word = body.get("word", "")
    phonemes = body.get("phonemes") or word
    language = body.get("language", "en")
    return {
        "word": word,
        "phonemes": phonemes,
        "language": language,
        "audio_url": None,
        "status": "ok",
    }
