"""
Sonography Visualization Routes

Endpoints for sonography (waterfall/3D spectrogram) visualization.
"""

from __future__ import annotations

import asyncio
import logging
import os

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/sonography", tags=["sonography"])

# In-memory sonography data (replace with database in production)
_sonography_data: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class SonographyConfig(BaseModel):
    """Sonography visualization configuration."""

    audio_id: str
    time_window: float = 1.0  # Time window in seconds
    overlap: float = 0.5  # Overlap ratio (0.0 to 1.0)
    frequency_resolution: int = 1024
    time_resolution: int = 100  # Number of time slices
    color_scheme: str = "waterfall"
    perspective: str = "3d"  # 3d, top, side, front
    rotation_x: float = 45.0
    rotation_y: float = 30.0
    zoom: float = 1.0


class SonographyFrame(BaseModel):
    """A single sonography frame."""

    timestamp: float
    frequencies: list[float]
    magnitudes: list[float]
    phase: list[float] | None = None


class SonographyData(BaseModel):
    """Sonography data for visualization."""

    audio_id: str
    config: SonographyConfig
    frames: list[SonographyFrame]
    total_duration: float
    sample_rate: int


class SonographyGenerateRequest(BaseModel):
    """Request to generate sonography data."""

    audio_id: str
    time_window: float = 1.0
    overlap: float = 0.5
    frequency_resolution: int = 1024
    time_resolution: int = 100
    color_scheme: str = "waterfall"
    perspective: str = "3d"


@router.post("/generate", response_model=SonographyData)
async def generate_sonography(request: SonographyGenerateRequest):
    """Generate sonography visualization data (waterfall/3D spectrogram)."""
    import numpy as np

    try:
        # Get audio file path
        from backend.services.audio_path_resolver import resolve_audio_path

        audio_path = resolve_audio_path(request.audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {request.audio_id}",
            )

        # Load audio file (app on path via main.py bootstrap)
        try:
            from backend.audio.audio_utils import load_audio

            audio, sample_rate = load_audio(audio_path)
        except ImportError as e:
            raise HTTPException(
                status_code=503,
                detail=f"Audio processing libraries not available: {e!s}. "
                "Install with: pip install librosa==0.11.0 soundfile==0.12.1",
            )
        except Exception as e:
            logger.error(f"Failed to load audio file {audio_path}: {e}")
            raise HTTPException(
                status_code=500,
                detail=f"Failed to load audio file: {e!s}",
            )

        # Convert to mono for analysis
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        total_duration = len(audio) / sample_rate

        # Validate time resolution
        max_frames = int(total_duration / (request.time_window * (1 - request.overlap))) + 1
        time_resolution = min(request.time_resolution, max_frames)

        # Calculate hop length (overlap)
        window_samples = int(request.time_window * sample_rate)
        hop_samples = int(window_samples * (1 - request.overlap))

        # Generate frames with overlapping windows
        frames = []
        for i in range(time_resolution):
            start_sample = i * hop_samples
            end_sample = start_sample + window_samples

            if start_sample >= len(audio):
                break

            # Extract window
            window = audio[start_sample : min(end_sample, len(audio))]

            # Pad if needed
            if len(window) < window_samples:
                window = np.pad(window, (0, window_samples - len(window)), mode="constant")

            # Compute FFT
            try:
                import librosa

                # Compute STFT for this window
                stft = librosa.stft(
                    window,
                    n_fft=request.frequency_resolution,
                    hop_length=window_samples // 4,
                    window="hann",
                )
                magnitude = np.abs(stft)

                # Get frequency bins
                freqs = librosa.fft_frequencies(sr=sample_rate, n_fft=request.frequency_resolution)

                # Take only positive frequencies
                freqs = freqs[: magnitude.shape[0]]
                magnitude = magnitude[: len(freqs), 0]  # Take first time frame

                frames.append(
                    SonographyFrame(
                        timestamp=start_sample / sample_rate,
                        frequencies=freqs.tolist(),
                        magnitudes=magnitude.tolist(),
                    )
                )
            except ImportError:
                # Fallback to numpy FFT if librosa not available
                fft = np.fft.rfft(window, n=request.frequency_resolution)
                magnitude = np.abs(fft)
                freqs = np.fft.rfftfreq(request.frequency_resolution, 1.0 / sample_rate)

                frames.append(
                    SonographyFrame(
                        timestamp=start_sample / sample_rate,
                        frequencies=freqs.tolist(),
                        magnitudes=magnitude.tolist(),
                    )
                )

        if not frames:
            raise HTTPException(
                status_code=500,
                detail="No frames generated. Audio may be too short.",
            )

        config = SonographyConfig(
            audio_id=request.audio_id,
            time_window=request.time_window,
            overlap=request.overlap,
            frequency_resolution=request.frequency_resolution,
            time_resolution=time_resolution,
            color_scheme=request.color_scheme,
            perspective=request.perspective,
        )

        data = SonographyData(
            audio_id=request.audio_id,
            config=config,
            frames=frames,
            total_duration=total_duration,
            sample_rate=sample_rate,
        )

        _sonography_data[request.audio_id] = data.model_dump()

        return data
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to generate sonography: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to generate sonography: {e!s}",
        ) from e


@router.get("/{audio_id}", response_model=SonographyData)
async def get_sonography_data(audio_id: str):
    """Get sonography data for an audio file."""
    if audio_id not in _sonography_data:
        raise HTTPException(status_code=404, detail="Sonography data not found")

    data = _sonography_data[audio_id]
    return SonographyData(**data)


@router.get("/perspectives")
async def get_available_perspectives():
    """Get available visualization perspectives."""
    return {
        "perspectives": [
            {"id": "3d", "name": "3D View"},
            {"id": "top", "name": "Top View"},
            {"id": "side", "name": "Side View"},
            {"id": "front", "name": "Front View"},
            {"id": "waterfall", "name": "Waterfall"},
        ]
    }


@router.get("/color-schemes")
async def get_available_color_schemes():
    """Get available color schemes."""
    return {
        "color_schemes": [
            {"id": "waterfall", "name": "Waterfall"},
            {"id": "heatmap", "name": "Heatmap"},
            {"id": "spectral", "name": "Spectral"},
            {"id": "rainbow", "name": "Rainbow"},
        ]
    }
