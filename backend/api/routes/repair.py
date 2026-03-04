"""
Audio Repair Routes

Endpoints for repairing audio issues like clipping, distortion, and artifacts.
"""

import logging
import os
import uuid
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models_additional import RepairClippingRequest

logger = logging.getLogger(__name__)


class RepairClippingResponse(BaseModel):
    """Response model for clipping repair."""

    audio_id: str
    repaired_audio_id: str
    clipped_samples: int
    clipped_percentage: float
    method: str


router = APIRouter(prefix="/api/repair", tags=["repair"])


@router.post("/clipping", response_model=RepairClippingResponse)
async def clipping(req: RepairClippingRequest) -> RepairClippingResponse:
    """
    Repair clipped audio by reconstructing clipped samples.

    Clipping occurs when audio exceeds the maximum amplitude,
    causing distortion. This endpoint attempts to reconstruct
    clipped regions using interpolation and spectral reconstruction.

    Args:
        req: Request with audio_id to repair

    Returns:
        Dictionary with repaired audio_id
    """
    try:
        audio_id = req.audio_id
        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        # Get audio file path
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path:
            raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Try to load audio processing libraries
        try:
            import librosa
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Audio repair requires librosa and soundfile. "
                    "Install with: pip install librosa soundfile"
                ),
            )

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Normalize audio
        if np.max(np.abs(audio)) > 1.0:
            audio = audio / np.max(np.abs(audio))

        # Detect clipped samples (samples at or near maximum amplitude)
        clipping_threshold = 0.95  # Consider samples above 95% as clipped
        clipped_mask = np.abs(audio) >= clipping_threshold

        if not np.any(clipped_mask):
            # No clipping detected, return original
            logger.info(f"No clipping detected in {audio_id}")
            return RepairClippingResponse(
                audio_id=audio_id,
                repaired_audio_id=audio_id,
                clipped_samples=0,
                clipped_percentage=0.0,
                method="none_needed",
            )

        # Count clipped samples
        num_clipped = np.sum(clipped_mask)
        clipped_percentage = (num_clipped / len(audio)) * 100

        logger.info(
            f"Detected clipping in {audio_id}: "
            f"{num_clipped} samples ({clipped_percentage:.2f}%)"
        )

        # Repair clipped samples using interpolation
        repaired_audio = audio.copy()

        # Method 1: Linear interpolation for clipped regions
        # Find consecutive clipped regions
        clipped_indices = np.where(clipped_mask)[0]

        if len(clipped_indices) > 0:
            # Group consecutive indices
            regions = []
            start = clipped_indices[0]
            for i in range(1, len(clipped_indices)):
                if clipped_indices[i] - clipped_indices[i - 1] > 1:
                    # Gap found, end current region
                    regions.append((start, clipped_indices[i - 1]))
                    start = clipped_indices[i]
            regions.append((start, clipped_indices[-1]))

            # Repair each region
            for start_idx, end_idx in regions:
                # Get surrounding samples for interpolation
                before_start = max(0, start_idx - 10)
                after_end = min(len(audio), end_idx + 11)

                # Get values before and after clipped region
                before_value = audio[before_start:start_idx]
                after_value = audio[end_idx + 1 : after_end]

                # Interpolate using cubic spline if scipy available, else linear
                try:
                    from scipy.interpolate import interp1d

                    # Create interpolation function
                    x_before = np.arange(before_start, start_idx)
                    x_after = np.arange(end_idx + 1, after_end)
                    x_all = np.concatenate([x_before, x_after])
                    y_all = np.concatenate([before_value, after_value])

                    if len(x_all) > 1:
                        interp_func = interp1d(
                            x_all,
                            y_all,
                            kind="cubic" if len(x_all) > 3 else "linear",
                            fill_value="extrapolate",
                        )

                        # Interpolate clipped region
                        x_clipped = np.arange(start_idx, end_idx + 1)
                        repaired_audio[start_idx : end_idx + 1] = interp_func(x_clipped)
                except ImportError:
                    # Fallback: Simple linear interpolation
                    if len(before_value) > 0 and len(after_value) > 0:
                        before_avg = np.mean(before_value)
                        after_avg = np.mean(after_value)

                        # Linear interpolation
                        region_length = end_idx - start_idx + 1
                        for i, idx in enumerate(range(start_idx, end_idx + 1)):
                            t = (i + 1) / (region_length + 1)
                            repaired_audio[idx] = before_avg * (1 - t) + after_avg * t
                    else:
                        # No surrounding samples, use zero
                        repaired_audio[start_idx : end_idx + 1] = 0.0

        # Method 2: Spectral reconstruction for severe clipping
        # Use spectral subtraction to reduce artifacts
        if clipped_percentage > 5.0:  # More than 5% clipped
            try:
                # Calculate STFT
                n_fft = 2048
                hop_length = 512
                stft = librosa.stft(repaired_audio, n_fft=n_fft, hop_length=hop_length)
                magnitude = np.abs(stft)
                phase = np.angle(stft)

                # Apply gentle spectral smoothing to reduce artifacts
                from scipy import ndimage

                smoothed_magnitude = ndimage.gaussian_filter(magnitude, sigma=1.0)

                # Reconstruct audio
                repaired_stft = smoothed_magnitude * np.exp(1j * phase)
                repaired_audio = librosa.istft(repaired_stft, hop_length=hop_length)
            except ImportError:
                logger.debug("scipy not available, skipping spectral reconstruction")

        # Normalize to prevent new clipping
        max_val = np.max(np.abs(repaired_audio))
        if max_val > 0.95:
            repaired_audio = repaired_audio * (0.95 / max_val)

        # Save and register via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        repaired_audio_id, _, _ = create_audio_artifact_from_wav_array(
            repaired_audio,
            sample_rate,
            created_by="repair_clipping",
            audio_id=f"repaired_{uuid.uuid4().hex[:8]}",
        )

        logger.info(
            f"Audio clipping repair completed: {audio_id} -> {repaired_audio_id} "
            f"({num_clipped} samples repaired, {clipped_percentage:.2f}%)"
        )

        return RepairClippingResponse(
            audio_id=audio_id,
            repaired_audio_id=repaired_audio_id,
            clipped_samples=int(num_clipped),
            clipped_percentage=float(clipped_percentage),
            method="interpolation" if clipped_percentage <= 5.0 else "spectral_reconstruction",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Audio clipping repair failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Audio clipping repair failed: {e!s}") from e
