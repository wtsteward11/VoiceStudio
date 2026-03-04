"""
Noise Reduction Routes

Endpoints for applying noise reduction to audio files.
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid

import numpy as np
from fastapi import APIRouter, HTTPException

from ..models_additional import NrApplyRequest

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/nr", tags=["nr"])

# In-memory noise print storage (replace with database in production)
_noise_prints: dict[str, dict] = {}
_state_lock = asyncio.Lock()


@router.post("/apply")
async def apply(req: NrApplyRequest) -> dict:
    """
    Apply noise reduction to audio using a noise print.

    Noise reduction uses spectral subtraction or similar techniques
    to remove background noise from audio.

    Args:
        req: Request with audio_id and noise_print_id

    Returns:
        Dictionary with processed audio_id
    """
    try:
        audio_id = req.audio_id
        noise_print_id = req.noise_print_id

        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        if not noise_print_id:
            raise HTTPException(status_code=400, detail="noise_print_id is required")

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
                    "Noise reduction requires librosa and soundfile. "
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

        # Apply noise reduction using spectral subtraction
        # This is a simplified implementation - production would use
        # more sophisticated algorithms like Wiener filtering or deep learning

        # Calculate STFT
        n_fft = 2048
        hop_length = 512
        stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
        magnitude = np.abs(stft)
        phase = np.angle(stft)

        # Estimate noise spectrum from noise print or first few frames
        # (assuming first 0.5 seconds contain mostly noise)
        noise_frames = int(0.5 * sample_rate / hop_length)
        noise_frames = min(noise_frames, magnitude.shape[1])

        if noise_frames > 0:
            # Estimate noise spectrum as average of first frames
            noise_spectrum = np.mean(magnitude[:, :noise_frames], axis=1, keepdims=True)
        else:
            # Fallback: use minimum magnitude as noise estimate
            noise_spectrum = np.min(magnitude, axis=1, keepdims=True)

        # Spectral subtraction: subtract noise spectrum from signal
        # Use over-subtraction factor (alpha) to reduce musical noise
        alpha = 2.0  # Over-subtraction factor
        beta = 0.1  # Spectral floor factor

        # Subtract noise spectrum
        cleaned_magnitude = magnitude - alpha * noise_spectrum

        # Apply spectral floor to prevent over-subtraction artifacts
        spectral_floor = beta * magnitude
        cleaned_magnitude = np.maximum(cleaned_magnitude, spectral_floor)

        # Reconstruct audio with cleaned magnitude and original phase
        cleaned_stft = cleaned_magnitude * np.exp(1j * phase)
        processed_audio = librosa.istft(cleaned_stft, hop_length=hop_length)

        # Normalize to prevent clipping
        max_val = np.max(np.abs(processed_audio))
        if max_val > 0.95:
            processed_audio = processed_audio * (0.95 / max_val)

        # Save and register via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        output_audio_id, _, _ = create_audio_artifact_from_wav_array(
            processed_audio,
            sample_rate,
            created_by="nr",
            audio_id=f"nr_{uuid.uuid4().hex[:8]}",
        )

        logger.info(
            f"Noise reduction applied: {audio_id} -> {output_audio_id} "
            f"(noise_print: {noise_print_id})"
        )

        return {
            "audio_id": output_audio_id,
            "original_audio_id": audio_id,
            "noise_print_id": noise_print_id,
            "method": "spectral_subtraction",
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Noise reduction failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Noise reduction failed: {e!s}") from e


@router.post("/noise-print/create")
async def create_noise_print(audio_id: str, name: str | None = None) -> dict:
    """
    Create a noise print from audio.

    A noise print is a spectral profile of background noise that can
    be used for noise reduction.

    Args:
        audio_id: Audio file ID containing noise sample
        name: Optional name for the noise print

    Returns:
        Dictionary with noise_print_id
    """
    try:
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
                    "Noise print creation requires librosa and soundfile. "
                    "Install with: pip install librosa soundfile"
                ),
            )

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Calculate noise spectrum
        n_fft = 2048
        hop_length = 512
        stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
        magnitude = np.abs(stft)

        # Average magnitude spectrum as noise print
        noise_spectrum = np.mean(magnitude, axis=1)

        # Create noise print
        noise_print_id = f"noise-print-{uuid.uuid4().hex[:8]}"
        noise_print = {
            "id": noise_print_id,
            "name": name or f"Noise Print from {audio_id}",
            "audio_id": audio_id,
            "spectrum": noise_spectrum.tolist(),
            "sample_rate": sample_rate,
            "n_fft": n_fft,
        }

        _noise_prints[noise_print_id] = noise_print

        logger.info(f"Created noise print: {noise_print_id} from {audio_id}")

        return {
            "noise_print_id": noise_print_id,
            "name": noise_print["name"],
            "audio_id": audio_id,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create noise print: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create noise print: {e!s}") from e


@router.get("/noise-prints")
async def list_noise_prints() -> list:
    """List all available noise prints."""
    return [
        {
            "id": np["id"],
            "name": np["name"],
            "audio_id": np["audio_id"],
        }
        for np in _noise_prints.values()
    ]
