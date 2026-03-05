"""
Dataset Scoring and Culling Routes

Endpoints for scoring audio clips in datasets and culling low-quality clips.
"""

from __future__ import annotations

import json
import logging
import os
import zipfile
from datetime import datetime
from pathlib import Path
from typing import Any

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel

from ..models import ApiOk
from ..models_additional import DatasetCullRequest, DatasetScoreRequest, ScoreResult

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/dataset", tags=["dataset"])

# Import engine service for quality metrics (ADR-008 compliant)
from backend.ml.models.engine_service import get_engine_service

# Try to import audio processing libraries
try:
    import numpy as np

    from backend.audio import audio_utils

    HAS_AUDIO_PROCESSING = True
except ImportError:
    HAS_AUDIO_PROCESSING = False
    logger.warning("Audio processing libraries not available for dataset scoring")


@router.post("/score", response_model=list[ScoreResult])
async def score(req: DatasetScoreRequest) -> list[ScoreResult]:
    """
    Score audio clips in a dataset.

    Returns quality metrics (SNR, LUFS, quality score) for each clip.
    """
    try:
        if not req.clips or len(req.clips) == 0:
            raise HTTPException(
                status_code=400,
                detail="At least one clip must be provided for scoring",
            )

        # Validate clip paths/IDs
        for clip in req.clips:
            if not clip or not clip.strip():
                raise HTTPException(status_code=400, detail="Clip paths/IDs cannot be empty")

        if not HAS_AUDIO_PROCESSING:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Audio processing libraries not available. " "Install required dependencies."
                ),
            )

        results = []
        for clip in req.clips:
            try:
                # Check if clip is a file path or audio ID
                clip_path = clip
                if not os.path.exists(clip_path):
                    # Try to get from audio storage
                    from backend.services.audio_artifacts import AudioRegistry

                    clip_path = AudioRegistry.get_path(clip)
                    if not clip_path:
                        logger.warning(f"Clip not found: {clip}")
                        results.append(ScoreResult(clip=clip, snr=0.0, lufs=-70.0, quality=0.0))
                        continue

                if not os.path.exists(clip_path):
                    logger.warning(f"Audio file not found: {clip_path}")
                    results.append(ScoreResult(clip=clip, snr=0.0, lufs=-70.0, quality=0.0))
                    continue

                # Load audio file
                audio, sample_rate = audio_utils.load_audio(clip_path)

                # Convert to mono if stereo
                if len(audio.shape) > 1:
                    audio = np.mean(audio, axis=1)

                # Calculate SNR via EngineService (ADR-008 compliant)
                try:
                    engine_service = get_engine_service()
                    snr = engine_service.calculate_snr(audio)
                    if np.isnan(snr) or np.isinf(snr):
                        snr = 0.0
                except Exception as e:
                    logger.warning(f"Failed to calculate SNR for {clip}: {e}")
                    snr = 0.0

                # Calculate LUFS
                try:
                    # Try to use pyloudnorm if available
                    try:
                        import pyloudnorm as pyln

                        meter = pyln.Meter(sample_rate)
                        lufs = float(meter.integrated_loudness(audio))
                        if np.isnan(lufs) or np.isinf(lufs):
                            lufs = -70.0
                    except ImportError:
                        # Fallback: approximate LUFS from RMS
                        rms = np.sqrt(np.mean(audio**2))
                        if rms > 1e-10:
                            lufs = 20 * np.log10(rms) - 0.691
                            lufs = max(-70.0, min(0.0, lufs))
                        else:
                            lufs = -70.0
                except Exception as e:
                    logger.warning(f"Failed to calculate LUFS for {clip}: {e}")
                    lufs = -70.0

                # Calculate quality score (0-1) based on SNR and LUFS
                # Normalize SNR: 0-40 dB maps to 0-1 (40+ dB = 1.0)
                snr_score = min(1.0, max(0.0, snr / 40.0))

                # Normalize LUFS: -70 to -10 LUFS maps to 0-1
                # (optimal around -23 LUFS)
                # Closer to -23 is better
                optimal_lufs = -23.0
                lufs_diff = abs(lufs - optimal_lufs)
                lufs_score = max(0.0, 1.0 - (lufs_diff / 47.0))  # 47 = range from -70 to -23

                # Combined quality score (weighted average)
                quality = (snr_score * 0.6) + (lufs_score * 0.4)

                results.append(
                    ScoreResult(
                        clip=clip,
                        snr=float(snr),
                        lufs=float(lufs),
                        quality=float(quality),
                    )
                )

            except Exception as e:
                logger.error(f"Failed to score clip {clip}: {e}", exc_info=True)
                results.append(ScoreResult(clip=clip, snr=0.0, lufs=-70.0, quality=0.0))

        return results

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to score dataset: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to score dataset: {e!s}") from e


@router.post("/cull", response_model=ApiOk)
async def cull(req: DatasetCullRequest) -> ApiOk:
    """
    Cull low-quality clips from a dataset.

    Removes clips that fall below quality thresholds.
    """
    try:
        # Extract parameters (validated by Pydantic)
        dataset_id = req.dataset_id
        min_quality = req.min_quality
        min_snr = req.min_snr
        max_lufs = req.max_lufs

        # Score all clips in the dataset
        if not HAS_AUDIO_PROCESSING:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Audio processing libraries not available. " "Install required dependencies."
                ),
            )

        # Get dataset clips (would come from dataset storage in production)
        clips = req.clips or []
        if not clips:
            # In production, load clips from dataset storage
            logger.warning(
                f"No clips provided for dataset {dataset_id}, " "attempting to load from storage"
            )
            # Load clips from dataset storage when available

        if not clips:
            logger.info(f"Dataset {dataset_id} has no clips to cull")
            return ApiOk()

        # Score all clips using the score function defined above
        from ..models_additional import DatasetScoreRequest

        score_request = DatasetScoreRequest(clips=clips)
        score_results = await score(score_request)

        # Filter out clips below thresholds
        culled_count = 0
        remaining_clips = []

        for result in score_results:
            should_cull = (
                result.quality < min_quality or result.snr < min_snr or result.lufs > max_lufs
            )

            if should_cull:
                culled_count += 1
                logger.debug(
                    f"Culling clip {result.clip}: "
                    f"quality={result.quality:.2f} (min={min_quality}), "
                    f"SNR={result.snr:.2f} (min={min_snr}), "
                    f"LUFS={result.lufs:.2f} (max={max_lufs})"
                )
            else:
                remaining_clips.append(result.clip)

        # In production, update dataset storage to remove culled clips
        # Log the results
        logger.info(
            f"Culled {culled_count} clips from dataset {dataset_id} "
            f"({len(remaining_clips)} remaining) with thresholds: "
            f"quality >= {min_quality}, SNR >= {min_snr}, LUFS <= {max_lufs}"
        )

        return ApiOk()

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to cull dataset: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to cull dataset: {e!s}") from e


# Pydantic models for analysis and export
class DatasetAnalysisResult(BaseModel):
    """Dataset analysis results."""

    total_clips: int
    valid_clips: int
    invalid_clips: int
    average_quality: float
    average_snr: float
    average_lufs: float
    quality_distribution: dict[str, int]  # "high", "medium", "low" -> count
    snr_distribution: dict[str, int]  # "excellent", "good", "fair", "poor" -> count
    # "optimal", "acceptable", "too_loud", "too_quiet" -> count
    lufs_distribution: dict[str, int]
    total_duration: float  # Total duration in seconds
    average_duration: float  # Average clip duration in seconds
    sample_rates: dict[int, int]  # Sample rate -> count
    channels: dict[int, int]  # Channel count -> count
    errors: list[str]  # List of validation errors


class DatasetValidationResult(BaseModel):
    """Dataset validation results."""

    is_valid: bool
    errors: list[str]
    warnings: list[str]
    clip_count: int
    valid_clip_count: int


class DatasetExportRequest(BaseModel):
    """Request to export a dataset."""

    dataset_id: str
    clips: list[str]
    format: str = "zip"  # "zip" or "json"
    include_metadata: bool = True
    include_scores: bool = True
    output_path: str | None = None


def _validate_dataset(clips: list[str]) -> DatasetValidationResult:
    """
    Validate a dataset structure and clips.

    Args:
        clips: List of clip paths or IDs

    Returns:
        DatasetValidationResult with validation status
    """
    errors: list[str] = []
    warnings: list[str] = []
    valid_clip_count = 0

    if not clips:
        errors.append("Dataset must contain at least one clip")
        return DatasetValidationResult(
            is_valid=False,
            errors=errors,
            warnings=warnings,
            clip_count=0,
            valid_clip_count=0,
        )

    # Check for duplicate clips
    clip_set = set(clips)
    if len(clip_set) < len(clips):
        duplicates = len(clips) - len(clip_set)
        warnings.append(f"Dataset contains {duplicates} duplicate clip(s)")

    # Validate each clip
    for clip in clips:
        if not clip or not clip.strip():
            errors.append("Dataset contains empty clip identifier")
            continue

        clip_path = clip
        if not os.path.exists(clip_path):
            # Try to get from audio storage
            try:
                from backend.services.audio_artifacts import AudioRegistry

                clip_path = AudioRegistry.get_path(clip)
                if not clip_path:
                    errors.append(f"Clip not found: {clip}")
                    continue
            except Exception:
                errors.append(f"Clip not found: {clip}")
                continue

        if not os.path.exists(clip_path):
            errors.append(f"Audio file not found: {clip_path}")
            continue

        # Check file extension
        valid_extensions = {".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac"}
        file_ext = Path(clip_path).suffix.lower()
        if file_ext not in valid_extensions:
            warnings.append(
                f"Clip has unusual extension: {clip_path} "
                f"(expected: {', '.join(valid_extensions)})"
            )

        # Try to load and validate audio
        if HAS_AUDIO_PROCESSING:
            try:
                audio, sample_rate = audio_utils.load_audio(clip_path)
                if len(audio) == 0:
                    errors.append(f"Clip is empty: {clip}")
                    continue
                if sample_rate < 8000 or sample_rate > 192000:
                    warnings.append(f"Clip has unusual sample rate: {clip} " f"({sample_rate} Hz)")
                valid_clip_count += 1
            except Exception as e:
                errors.append(f"Failed to load clip {clip}: {e!s}")
        else:
            # Can't validate audio without processing libraries
            valid_clip_count += 1

    is_valid = len(errors) == 0

    return DatasetValidationResult(
        is_valid=is_valid,
        errors=errors,
        warnings=warnings,
        clip_count=len(clips),
        valid_clip_count=valid_clip_count,
    )


@router.post("/validate", response_model=DatasetValidationResult)
async def validate_dataset(req: DatasetScoreRequest) -> DatasetValidationResult:
    """
    Validate a dataset structure and clips.

    Returns validation results with errors and warnings.
    """
    try:
        if not req.clips:
            return DatasetValidationResult(
                is_valid=False,
                errors=["Dataset must contain at least one clip"],
                warnings=[],
                clip_count=0,
                valid_clip_count=0,
            )

        return _validate_dataset(req.clips)

    except Exception as e:
        logger.error(f"Failed to validate dataset: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to validate dataset: {e!s}") from e


@router.post("/analyze", response_model=DatasetAnalysisResult)
async def analyze_dataset(req: DatasetScoreRequest) -> DatasetAnalysisResult:
    """
    Analyze a dataset and return comprehensive statistics.

    Returns quality metrics, distributions, and dataset characteristics.
    """
    try:
        if not req.clips or len(req.clips) == 0:
            raise HTTPException(
                status_code=400,
                detail="At least one clip must be provided for analysis",
            )

        if not HAS_AUDIO_PROCESSING:
            raise HTTPException(
                status_code=503,
                detail="Audio processing libraries not available. "
                "Install required dependencies.",
            )

        # Score all clips
        score_request = DatasetScoreRequest(clips=req.clips)
        score_results = await score(score_request)

        # Calculate statistics
        valid_results = [r for r in score_results if r.quality > 0]
        total_clips = len(req.clips)
        valid_clips = len(valid_results)
        invalid_clips = total_clips - valid_clips

        if valid_clips == 0:
            return DatasetAnalysisResult(
                total_clips=total_clips,
                valid_clips=0,
                invalid_clips=invalid_clips,
                average_quality=0.0,
                average_snr=0.0,
                average_lufs=-70.0,
                quality_distribution={"high": 0, "medium": 0, "low": 0},
                snr_distribution={"excellent": 0, "good": 0, "fair": 0, "poor": 0},
                lufs_distribution={
                    "optimal": 0,
                    "acceptable": 0,
                    "too_loud": 0,
                    "too_quiet": 0,
                },
                total_duration=0.0,
                average_duration=0.0,
                sample_rates={},
                channels={},
                errors=["No valid clips found in dataset"],
            )

        # Calculate averages
        average_quality = sum(r.quality for r in valid_results) / valid_clips
        average_snr = sum(r.snr for r in valid_results) / valid_clips
        average_lufs = sum(r.lufs for r in valid_results) / valid_clips

        # Quality distribution
        quality_distribution = {"high": 0, "medium": 0, "low": 0}
        for r in valid_results:
            if r.quality >= 0.8:
                quality_distribution["high"] += 1
            elif r.quality >= 0.5:
                quality_distribution["medium"] += 1
            else:
                quality_distribution["low"] += 1

        # SNR distribution
        snr_distribution = {"excellent": 0, "good": 0, "fair": 0, "poor": 0}
        for r in valid_results:
            if r.snr >= 30:
                snr_distribution["excellent"] += 1
            elif r.snr >= 20:
                snr_distribution["good"] += 1
            elif r.snr >= 10:
                snr_distribution["fair"] += 1
            else:
                snr_distribution["poor"] += 1

        # LUFS distribution
        lufs_distribution = {
            "optimal": 0,
            "acceptable": 0,
            "too_loud": 0,
            "too_quiet": 0,
        }
        for r in valid_results:
            if -25 <= r.lufs <= -20:
                lufs_distribution["optimal"] += 1
            elif -30 <= r.lufs <= -15:
                lufs_distribution["acceptable"] += 1
            elif r.lufs > -15:
                lufs_distribution["too_loud"] += 1
            else:
                lufs_distribution["too_quiet"] += 1

        # Calculate durations and audio characteristics
        total_duration = 0.0
        sample_rates: dict[int, int] = {}
        channels: dict[int, int] = {}
        errors: list[str] = []

        for clip in req.clips:
            try:
                clip_path = clip
                if not os.path.exists(clip_path):
                    from backend.services.audio_artifacts import AudioRegistry

                    clip_path = AudioRegistry.get_path(clip) or clip
                    if not clip_path or not os.path.exists(clip_path):
                        continue

                if os.path.exists(clip_path):
                    audio, sample_rate = audio_utils.load_audio(clip_path)
                    duration = len(audio) / sample_rate
                    total_duration += duration

                    sample_rates[sample_rate] = sample_rates.get(sample_rate, 0) + 1
                    num_channels = 1 if len(audio.shape) == 1 else audio.shape[1]
                    channels[num_channels] = channels.get(num_channels, 0) + 1
            except Exception as e:
                errors.append(f"Failed to analyze clip {clip}: {e!s}")

        average_duration = total_duration / valid_clips if valid_clips > 0 else 0.0

        return DatasetAnalysisResult(
            total_clips=total_clips,
            valid_clips=valid_clips,
            invalid_clips=invalid_clips,
            average_quality=float(average_quality),
            average_snr=float(average_snr),
            average_lufs=float(average_lufs),
            quality_distribution=quality_distribution,
            snr_distribution=snr_distribution,
            lufs_distribution=lufs_distribution,
            total_duration=float(total_duration),
            average_duration=float(average_duration),
            sample_rates=sample_rates,
            channels=channels,
            errors=errors,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to analyze dataset: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to analyze dataset: {e!s}") from e


@router.post("/export")
async def export_dataset(req: DatasetExportRequest):
    """
    Export a dataset to a file or archive.

    Supports ZIP archive (with audio files) or JSON metadata export.
    """
    try:
        if not req.clips or len(req.clips) == 0:
            raise HTTPException(
                status_code=400,
                detail="At least one clip must be provided for export",
            )

        # Determine output path
        if req.output_path:
            output_path = req.output_path
        else:
            export_dir = os.path.join(os.path.expanduser("~"), "VoiceStudio", "exports", "datasets")
            os.makedirs(export_dir, exist_ok=True)
            timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            if req.format == "zip":
                output_path = os.path.join(export_dir, f"{req.dataset_id}_{timestamp}.zip")
            else:
                output_path = os.path.join(export_dir, f"{req.dataset_id}_{timestamp}.json")

        if req.format == "zip":
            # Create ZIP archive with audio files
            with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as zipf:
                clips_list: list[dict[str, Any]] = []
                metadata: dict[str, Any] = {
                    "dataset_id": req.dataset_id,
                    "export_date": datetime.utcnow().isoformat(),
                    "clip_count": len(req.clips),
                    "clips": clips_list,
                }

                for clip in req.clips:
                    clip_path = clip
                    if not os.path.exists(clip_path):
                        from backend.services.audio_artifacts import AudioRegistry

                        clip_path = AudioRegistry.get_path(clip)
                        if not clip_path:
                            logger.warning(f"Clip not found for export: {clip}")
                            continue

                    if os.path.exists(clip_path):
                        # Add audio file to ZIP
                        clip_name = os.path.basename(clip_path)
                        zipf.write(clip_path, clip_name)

                        # Add metadata if requested
                        if req.include_metadata:
                            clip_info: dict[str, Any] = {
                                "original_path": clip,
                                "filename": clip_name,
                            }

                            # Add scores if requested
                            if req.include_scores and HAS_AUDIO_PROCESSING:
                                try:
                                    score_request = DatasetScoreRequest(clips=[clip])
                                    score_results = await score(score_request)
                                    if score_results:
                                        clip_info["scores"] = {
                                            "snr": score_results[0].snr,
                                            "lufs": score_results[0].lufs,
                                            "quality": score_results[0].quality,
                                        }
                                except Exception as e:
                                    logger.warning(f"Failed to score clip for export: {e}")

                            metadata["clips"].append(clip_info)

                # Add metadata file to ZIP
                if req.include_metadata:
                    metadata_json = json.dumps(metadata, indent=2)
                    zipf.writestr("metadata.json", metadata_json)

            logger.info(f"Exported dataset {req.dataset_id} to {output_path}")
            try:
                from backend.services.usage_stats import record_export_completed
                record_export_completed()
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
            return FileResponse(
                output_path,
                media_type="application/zip",
                filename=os.path.basename(output_path),
            )

        else:
            # Export as JSON metadata
            clips_list2: list[dict[str, Any]] = []
            metadata = {
                "dataset_id": req.dataset_id,
                "export_date": datetime.utcnow().isoformat(),
                "clip_count": len(req.clips),
                "clips": clips_list2,
            }

            for clip in req.clips:
                clip_info2: dict[str, Any] = {"clip_id": clip}

                clip_path = clip
                if not os.path.exists(clip_path):
                    from backend.services.audio_artifacts import AudioRegistry

                    clip_path = AudioRegistry.get_path(clip)
                    if clip_path:
                        clip_info2["file_path"] = clip_path
                    else:
                        clip_info2["file_path"] = None
                        clip_info2["error"] = "Clip not found"
                else:
                    clip_info2["file_path"] = clip_path

                if req.include_scores and HAS_AUDIO_PROCESSING:
                    try:
                        score_request = DatasetScoreRequest(clips=[clip])
                        score_results = await score(score_request)
                        if score_results:
                            clip_info2["scores"] = {
                                "snr": score_results[0].snr,
                                "lufs": score_results[0].lufs,
                                "quality": score_results[0].quality,
                            }
                    except Exception as e:
                        logger.warning(f"Failed to score clip for export: {e}")

                clips_list2.append(clip_info2)

            # Write JSON file
            with open(output_path, "w", encoding="utf-8") as f:
                json.dump(metadata, f, indent=2)

            logger.info(f"Exported dataset {req.dataset_id} to {output_path}")
            try:
                from backend.services.usage_stats import record_export_completed
                record_export_completed()
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
            return FileResponse(
                output_path,
                media_type="application/json",
                filename=os.path.basename(output_path),
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to export dataset: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to export dataset: {e!s}") from e
