"""
Spectral Inpainting Routes

Endpoints for spectral inpainting - reconstructing missing or damaged
audio regions using spectral analysis and interpolation.
"""

import logging
import os
import tempfile
import uuid

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models_additional import SpectralInpaintRequest

logger = logging.getLogger(__name__)


class SpectralInpaintResponse(BaseModel):
    """Response model for spectral inpainting."""

    audio_id: str
    inpainted_audio_id: str
    mask_type: str
    mask: str


router = APIRouter(prefix="/api/spectral", tags=["spectral"])


@router.post("/inpaint", response_model=SpectralInpaintResponse)
async def inpaint(req: SpectralInpaintRequest) -> SpectralInpaintResponse:
    """
    Perform spectral inpainting to reconstruct missing or damaged
    audio regions.

    Spectral inpainting uses frequency-domain analysis to fill in gaps
    or repair damaged audio regions by interpolating spectral content.

    Args:
        req: Request with audio_id and mask specification

    Returns:
        Dictionary with inpainted audio_id
    """
    try:
        audio_id = req.audio_id
        mask = req.mask  # Mask specification (e.g., time ranges or frequency ranges)

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
                    "Spectral inpainting requires librosa and soundfile. "
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

        # Parse mask specification
        # Mask can be:
        # - Time range: "0.5-1.0" (seconds)
        # - Frequency range: "1000-2000" (Hz)
        # - JSON: {"time": [0.5, 1.0]} or {"freq": [1000, 2000]}

        mask_type = "time"  # Default to time mask
        mask_start = 0.0
        mask_end = 0.0

        try:
            import json

            if mask.startswith("{") or mask.startswith("["):
                # JSON format
                mask_data = json.loads(mask)
                if "time" in mask_data:
                    mask_type = "time"
                    mask_start = float(mask_data["time"][0])
                    mask_end = float(mask_data["time"][1])
                elif "freq" in mask_data:
                    mask_type = "freq"
                    mask_start = float(mask_data["freq"][0])
                    mask_end = float(mask_data["freq"][1])
            elif "-" in mask:
                # Range format (e.g., "0.5-1.0" or "1000-2000")
                parts = mask.split("-")
                if len(parts) == 2:
                    mask_start = float(parts[0])
                    mask_end = float(parts[1])
                    # Determine type based on values
                    if mask_end > 1000:  # Likely frequency
                        mask_type = "freq"
                    else:  # Likely time
                        mask_type = "time"
        except Exception as e:
            logger.warning(f"Could not parse mask '{mask}', using default: {e}")
            # Default: mask middle 10% of audio
            mask_type = "time"
            mask_start = len(audio) / sample_rate * 0.45
            mask_end = len(audio) / sample_rate * 0.55

        # Perform spectral inpainting
        if mask_type == "time":
            # Time-domain mask: inpaint time region
            start_sample = int(mask_start * sample_rate)
            end_sample = int(mask_end * sample_rate)
            start_sample = max(0, min(start_sample, len(audio)))
            end_sample = max(start_sample, min(end_sample, len(audio)))

            # Calculate STFT
            n_fft = 2048
            hop_length = 512
            stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
            magnitude = np.abs(stft)
            phase = np.angle(stft)

            # Find frames that overlap with masked region
            frame_times = librosa.frames_to_time(
                np.arange(magnitude.shape[1]), sr=sample_rate, hop_length=hop_length
            )

            masked_frames = np.where((frame_times >= mask_start) & (frame_times <= mask_end))[0]

            if len(masked_frames) > 0:
                # Inpaint masked frames using spectral interpolation
                for frame_idx in masked_frames:
                    # Get surrounding frames for interpolation
                    before_idx = max(0, frame_idx - 5)
                    after_idx = min(magnitude.shape[1] - 1, frame_idx + 5)

                    # Interpolate magnitude spectrum
                    if before_idx < frame_idx < after_idx:
                        # Linear interpolation of magnitude
                        before_mag = magnitude[:, before_idx]
                        after_mag = magnitude[:, after_idx]
                        t = (frame_idx - before_idx) / (after_idx - before_idx)
                        magnitude[:, frame_idx] = before_mag * (1 - t) + after_mag * t
                    elif frame_idx == before_idx:
                        magnitude[:, frame_idx] = magnitude[:, after_idx]
                    elif frame_idx == after_idx:
                        magnitude[:, frame_idx] = magnitude[:, before_idx]

                # Reconstruct audio with inpainted magnitude
                inpainted_stft = magnitude * np.exp(1j * phase)
                inpainted_audio = librosa.istft(inpainted_stft, hop_length=hop_length)
            else:
                # No frames to inpaint, use original
                inpainted_audio = audio.copy()

        elif mask_type == "freq":
            # Frequency-domain mask: inpaint frequency region
            # Calculate STFT
            n_fft = 2048
            hop_length = 512
            stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
            magnitude = np.abs(stft)
            phase = np.angle(stft)

            # Convert frequency range to bin indices
            freqs = librosa.fft_frequencies(sr=sample_rate, n_fft=n_fft)
            freq_start_bin = np.argmin(np.abs(freqs - mask_start))
            freq_end_bin = np.argmin(np.abs(freqs - mask_end))

            # Inpaint frequency bins using interpolation
            for bin_idx in range(freq_start_bin, freq_end_bin + 1):
                # Get surrounding bins for interpolation
                before_bin = max(0, bin_idx - 5)
                after_bin = min(magnitude.shape[0] - 1, bin_idx + 5)

                if before_bin < bin_idx < after_bin:
                    # Interpolate across all time frames
                    before_mag = magnitude[before_bin, :]
                    after_mag = magnitude[after_bin, :]
                    t = (bin_idx - before_bin) / (after_bin - before_bin)
                    magnitude[bin_idx, :] = before_mag * (1 - t) + after_mag * t
                elif bin_idx == before_bin:
                    magnitude[bin_idx, :] = magnitude[after_bin, :]
                elif bin_idx == after_bin:
                    magnitude[bin_idx, :] = magnitude[before_bin, :]

            # Reconstruct audio
            inpainted_stft = magnitude * np.exp(1j * phase)
            inpainted_audio = librosa.istft(inpainted_stft, hop_length=hop_length)
        else:
            # Unknown mask type, return original
            inpainted_audio = audio.copy()

        # Normalize to prevent clipping
        max_val = np.max(np.abs(inpainted_audio))
        if max_val > 0.95:
            inpainted_audio = inpainted_audio * (0.95 / max_val)

        # Save inpainted audio via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        inpainted_audio_id, _, _ = create_audio_artifact_from_wav_array(
            inpainted_audio,
            sample_rate,
            created_by="spectral",
            audio_id=f"inpainted_{uuid.uuid4().hex[:8]}",
        )

        logger.info(
            f"Spectral inpainting completed: {audio_id} -> {inpainted_audio_id} "
            f"(mask_type={mask_type}, mask={mask})"
        )

        return SpectralInpaintResponse(
            audio_id=audio_id,
            inpainted_audio_id=inpainted_audio_id,
            mask_type=mask_type,
            mask=mask,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Spectral inpainting failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Spectral inpainting failed: {e!s}") from e
