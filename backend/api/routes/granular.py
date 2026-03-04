"""
Granular Synthesis Routes

Endpoints for granular synthesis and audio manipulation using
granular synthesis techniques (time-stretching, pitch-shifting, etc.).
"""

import logging
import os
import tempfile
import uuid

import numpy as np
from fastapi import APIRouter, HTTPException

from ..models_additional import GranularRenderRequest

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/granular", tags=["granular"])


@router.post("/render")
async def render(req: GranularRenderRequest) -> dict:
    """
    Render audio using granular synthesis.

    Granular synthesis breaks audio into small grains and
    manipulates them to create time-stretching, pitch-shifting,
    and other effects.

    Args:
        req: Request with audio_id and granular synthesis parameters

    Returns:
        Dictionary with rendered audio_id
    """
    try:
        audio_id = req.audio_id
        params = req.params

        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        if not params:
            params = {}

        # Get granular synthesis parameters
        grain_size_ms = params.get("grain_size_ms", 50.0)  # Grain size in milliseconds
        overlap = params.get("overlap", 0.5)  # Overlap ratio (0.0-1.0)
        pitch_shift = params.get("pitch_shift", 0.0)  # Pitch shift in semitones
        time_stretch = params.get("time_stretch", 1.0)  # Time stretch ratio
        window_type = params.get("window_type", "hann")  # Window type

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
                    "Granular synthesis requires librosa and soundfile. "
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

        # Convert grain size from milliseconds to samples
        grain_size_samples = int((grain_size_ms / 1000.0) * sample_rate)
        grain_size_samples = max(64, min(grain_size_samples, 4096))  # Clamp to reasonable range

        # Calculate hop size based on overlap
        hop_size = int(grain_size_samples * (1.0 - overlap))
        hop_size = max(1, hop_size)  # Ensure at least 1 sample

        # Apply time stretching if needed
        if time_stretch != 1.0:
            # Use librosa's phase vocoder for time stretching
            stft = librosa.stft(audio, n_fft=2048, hop_length=512)
            stft_stretched = librosa.phase_vocoder(stft, rate=1.0 / time_stretch)
            audio = librosa.istft(stft_stretched, hop_length=512)

        # Apply pitch shifting if needed
        if pitch_shift != 0.0:
            audio = librosa.effects.pitch_shift(
                audio,
                sr=sample_rate,
                n_steps=pitch_shift,
            )

        # Perform granular synthesis
        # Create window function
        if window_type == "hann":
            window = np.hanning(grain_size_samples)
        elif window_type == "hamming":
            window = np.hamming(grain_size_samples)
        elif window_type == "blackman":
            window = np.blackman(grain_size_samples)
        else:
            window = np.ones(grain_size_samples)  # Rectangular window

        # Normalize window
        window = window / np.max(window)

        # Granular synthesis: break audio into grains and overlap-add
        output_length = int(len(audio) * time_stretch)
        output_audio = np.zeros(output_length)

        # Process audio in grains
        position = 0
        grain_index = 0

        while position < len(audio) - grain_size_samples:
            # Extract grain
            grain = audio[position : position + grain_size_samples]

            # Apply window
            windowed_grain = grain * window

            # Calculate output position (with time stretching)
            output_position = int(position * time_stretch)

            # Overlap-add grain to output
            end_pos = min(output_position + grain_size_samples, len(output_audio))
            grain_length = end_pos - output_position

            if grain_length > 0:
                output_audio[output_position:end_pos] += windowed_grain[:grain_length]

            # Move to next grain
            position += hop_size
            grain_index += 1

        # Normalize output to prevent clipping
        max_val = np.max(np.abs(output_audio))
        if max_val > 0.95:
            output_audio = output_audio * (0.95 / max_val)

        # Save rendered audio via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        rendered_audio_id, _, _ = create_audio_artifact_from_wav_array(
            output_audio,
            sample_rate,
            created_by="granular",
            audio_id=f"granular_{uuid.uuid4().hex[:8]}",
        )

        logger.info(
            f"Granular synthesis completed: {audio_id} -> {rendered_audio_id} "
            f"(grain_size={grain_size_ms}ms, overlap={overlap}, "
            f"pitch_shift={pitch_shift}st, time_stretch={time_stretch}x, "
            f"grains={grain_index})"
        )

        return {
            "audio_id": audio_id,
            "rendered_audio_id": rendered_audio_id,
            "grain_size_ms": grain_size_ms,
            "overlap": overlap,
            "pitch_shift": pitch_shift,
            "time_stretch": time_stretch,
            "window_type": window_type,
            "grains_processed": grain_index,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Granular synthesis failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Granular synthesis failed: {e!s}") from e
