"""
Advanced Spectrogram Routes

Endpoints for advanced spectrogram visualization and analysis with custom
FFT configuration, overlay support, and advanced color mapping.

Route Purposes (GAP-B06):
- /api/spectrogram/config/{audio_id}: Spectrogram display configuration
- /api/spectrogram/data/{audio_id}: Generate spectrogram with custom settings
- /api/spectrogram/analyze/{audio_id}: Frequency-domain analysis
- /api/spectrogram/overlay: Overlay multiple spectrograms for comparison

Use Cases:
- Custom FFT window sizes (256-8192)
- Adjustable frequency and time ranges
- Multiple color schemes (viridis, plasma, inferno, etc.)
- Phase and magnitude display options
- Log-scale frequency axis

For quick single-file spectrograms, consider using /api/audio/spectrogram.

See also: docs/api/ROUTE_MAPPING.md for complete route documentation.
"""

from __future__ import annotations

import logging
import asyncio
import os
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, field_validator

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/spectrogram", tags=["spectrogram"])

# In-memory spectrogram settings (replace with database in production)
_spectrogram_settings: dict[str, dict] = {}
_state_lock = asyncio.Lock()
_MAX_SPECTROGRAM_SETTINGS = 1000  # Maximum number of settings to keep


def _cleanup_old_spectrogram_settings():
    """Clean up old spectrogram settings if limit exceeded."""
    if len(_spectrogram_settings) > _MAX_SPECTROGRAM_SETTINGS:
        # Remove oldest entries (simple FIFO)
        excess = len(_spectrogram_settings) - _MAX_SPECTROGRAM_SETTINGS
        keys_to_remove = list(_spectrogram_settings.keys())[:excess]
        for key in keys_to_remove:
            del _spectrogram_settings[key]
        logger.info(f"Cleaned up {len(keys_to_remove)} old spectrogram settings")


class SpectrogramConfig(BaseModel):
    """Spectrogram configuration."""

    audio_id: str
    window_size: int = 2048
    hop_length: int = 512
    n_fft: int = 2048
    frequency_range: dict[str, float] | None = None
    time_range: dict[str, float] | None = None
    color_scheme: str = "viridis"
    colormap_range: dict[str, float] | None = None
    show_phase: bool = False
    show_magnitude: bool = True
    log_scale: bool = True

    @field_validator("window_size", "n_fft")
    @classmethod
    def validate_window_size(cls, v: int) -> int:
        """Validate window size and n_fft."""
        if not 256 <= v <= 8192:
            raise ValueError("Window size/n_fft must be between 256 and 8192")
        if v & (v - 1) != 0:  # Check if power of 2
            raise ValueError("Window size/n_fft must be a power of 2")
        return v

    @field_validator("hop_length")
    @classmethod
    def validate_hop_length(cls, v: int) -> int:
        """Validate hop length."""
        if not 128 <= v <= 2048:
            raise ValueError("Hop length must be between 128 and 2048")
        return v

    @field_validator("color_scheme")
    @classmethod
    def validate_color_scheme(cls, v: str) -> str:
        """Validate color scheme."""
        valid_schemes = [
            "viridis",
            "plasma",
            "inferno",
            "magma",
            "hot",
            "cool",
            "grayscale",
        ]
        if v not in valid_schemes:
            raise ValueError(f"Invalid color scheme: {v}. " f"Must be one of {valid_schemes}")
        return v

    @field_validator("frequency_range", "time_range", "colormap_range")
    @classmethod
    def validate_range(cls, v: dict[str, float] | None) -> dict[str, float] | None:
        """Validate range dictionaries."""
        if v is None:
            return v
        if "min" in v and "max" in v and v["min"] >= v["max"]:
            raise ValueError("Range min must be less than max")
        return v


class SpectrogramFrame(BaseModel):
    """A single spectrogram frame."""

    time: float
    frequencies: list[float]
    magnitudes: list[float]
    phases: list[float] | None = None


class SpectrogramData(BaseModel):
    """Complete spectrogram data."""

    audio_id: str
    sample_rate: int
    duration: float
    frames: list[SpectrogramFrame]
    frequency_resolution: float
    time_resolution: float
    config: SpectrogramConfig


@router.get("/config/{audio_id}")
@cache_response(ttl=300)  # Cache for 5 minutes (config is relatively static)
async def get_spectrogram_config(audio_id: str):
    """Get spectrogram configuration for an audio file."""
    if audio_id not in _spectrogram_settings:
        # Return default config
        return SpectrogramConfig(audio_id=audio_id)

    return SpectrogramConfig(**_spectrogram_settings[audio_id])


@router.put("/config/{audio_id}")
async def update_spectrogram_config(audio_id: str, config: SpectrogramConfig):
    """Update spectrogram configuration."""
    try:
        # Validate audio_id matches config
        if config.audio_id != audio_id:
            raise HTTPException(
                status_code=400,
                detail="Audio ID in config must match URL parameter",
            )

        # Clean up old settings if needed
        _cleanup_old_spectrogram_settings()

        _spectrogram_settings[audio_id] = config.model_dump()
        logger.debug(f"Updated spectrogram config for audio: {audio_id}")
        return config
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update spectrogram config: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update config: {e!s}",
        ) from e


@router.get("/data/{audio_id}", response_model=SpectrogramData)
@cache_response(ttl=300)  # Cache for 5 minutes (spectrogram data is static for a given audio file)
async def get_spectrogram_data(
    audio_id: str,
    window_size: int = Query(2048, ge=256, le=8192),
    hop_length: int = Query(512, ge=128, le=2048),
    n_fft: int = Query(2048, ge=256, le=8192),
    frequency_min: float | None = Query(None, ge=0.0),
    frequency_max: float | None = Query(None, ge=0.0),
    time_start: float | None = Query(None, ge=0.0),
    time_end: float | None = Query(None, ge=0.0),
    log_scale: bool = Query(True),
):
    """Get spectrogram data for an audio file."""
    # Validate parameters
    if window_size & (window_size - 1) != 0:
        raise HTTPException(
            status_code=400,
            detail="window_size must be a power of 2",
        )
    if n_fft & (n_fft - 1) != 0:
        raise HTTPException(
            status_code=400,
            detail="n_fft must be a power of 2",
        )
    if frequency_min is not None and frequency_max is not None:
        if frequency_min >= frequency_max:
            raise HTTPException(
                status_code=400,
                detail="frequency_min must be less than frequency_max",
            )
    if time_start is not None and time_end is not None and time_start >= time_end:
        raise HTTPException(
            status_code=400,
            detail="time_start must be less than time_end",
        )

    try:
        # Get audio file path
        from .audio import _get_audio_path

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        # Try to load audio analysis libraries
        try:
            import librosa
            import numpy as np
            import soundfile as sf

            HAS_AUDIO_LIBS = True
        except ImportError:
            HAS_AUDIO_LIBS = False
            logger.error(
                "librosa/soundfile not available. Spectrogram generation requires these libraries."
            )

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Spectrogram generation requires librosa and soundfile libraries. "
                    "Please install with: pip install librosa soundfile"
                ),
            )

        if HAS_AUDIO_LIBS:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)
            duration = len(audio) / sample_rate

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Ensure audio is float32 and normalized
            if audio.dtype != np.float32:
                audio = audio.astype(np.float32)
            if np.max(np.abs(audio)) > 1.0:
                audio = audio / np.max(np.abs(audio))

            # Compute STFT with specified parameters
            stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
            magnitude = np.abs(stft)

            # Convert to log scale if requested
            if log_scale:
                magnitude_db = librosa.amplitude_to_db(magnitude, ref=np.max)
                magnitude = magnitude_db
            else:
                magnitude = magnitude

            # Get frequency bins
            frequencies = librosa.fft_frequencies(sr=sample_rate, n_fft=n_fft)

            # Apply frequency range filter if specified
            if frequency_min is not None or frequency_max is not None:
                freq_mask = np.ones(len(frequencies), dtype=bool)
                if frequency_min is not None:
                    freq_mask &= frequencies >= frequency_min
                if frequency_max is not None:
                    freq_mask &= frequencies <= frequency_max
                frequencies = frequencies[freq_mask]
                magnitude = magnitude[freq_mask, :]

            # Calculate time axis
            time_axis = librosa.frames_to_time(
                np.arange(magnitude.shape[1]), sr=sample_rate, hop_length=hop_length
            )

            # Apply time range filter if specified
            if time_start is not None or time_end is not None:
                time_mask = np.ones(len(time_axis), dtype=bool)
                if time_start is not None:
                    time_mask &= time_axis >= time_start
                if time_end is not None:
                    time_mask &= time_axis <= time_end
                time_axis = time_axis[time_mask]
                magnitude = magnitude[:, time_mask]

            # Limit number of frames to prevent memory issues
            MAX_FRAMES = 10000
            if len(time_axis) > MAX_FRAMES:
                logger.warning(
                    f"Limiting frames from {len(time_axis)} to {MAX_FRAMES} "
                    f"for audio {audio_id}"
                )
                step = len(time_axis) // MAX_FRAMES
                time_axis = time_axis[::step]
                magnitude = magnitude[:, ::step]

            # Create frames from real STFT data
            frames = []
            for i, time in enumerate(time_axis):
                frames.append(
                    SpectrogramFrame(
                        time=float(time),
                        frequencies=list(frequencies),
                        magnitudes=list(magnitude[:, i]),
                    )
                )

            frequency_resolution = sample_rate / n_fft
            time_resolution = hop_length / sample_rate

        config = SpectrogramConfig(
            audio_id=audio_id,
            window_size=window_size,
            hop_length=hop_length,
            n_fft=n_fft,
            frequency_range=(
                {
                    "min": frequency_min or 0.0,
                    "max": frequency_max or sample_rate / 2,
                }
                if frequency_min or frequency_max
                else None
            ),
            time_range=(
                {"start": time_start or 0.0, "end": time_end or duration}
                if time_start or time_end
                else None
            ),
            log_scale=log_scale,
        )

        return SpectrogramData(
            audio_id=audio_id,
            sample_rate=sample_rate,
            duration=duration,
            frames=frames,
            frequency_resolution=frequency_resolution,
            time_resolution=time_resolution,
            config=config,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Failed to generate spectrogram for {audio_id}: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"Failed to generate spectrogram: {e!s}",
        ) from e


@router.get("/compare")
@cache_response(
    ttl=300
)  # Cache for 5 minutes (comparison results are static for given audio files)
async def compare_spectrograms(
    audio_ids: str = Query(..., description="Comma-separated audio IDs"),
):
    """Compare multiple spectrograms side by side."""
    try:
        audio_id_list = [aid.strip() for aid in audio_ids.split(",")]
        # Remove empty strings
        audio_id_list = [aid for aid in audio_id_list if aid]

        if len(audio_id_list) < 2:
            raise HTTPException(
                status_code=400,
                detail="At least 2 audio IDs required",
            )

        # Limit number of comparisons to prevent memory issues
        MAX_COMPARISONS = 10
        if len(audio_id_list) > MAX_COMPARISONS:
            raise HTTPException(
                status_code=400,
                detail=(
                    f"Too many audio IDs ({len(audio_id_list)}). " f"Maximum: {MAX_COMPARISONS}"
                ),
            )

        # Load audio files and compute spectrograms
        from .audio import _get_audio_path

        try:
            import librosa
            import numpy as np
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="librosa/soundfile not available. Install with: pip install librosa soundfile",
            )

        spectrograms = []
        for audio_id in audio_id_list:
            audio_path = _get_audio_path(audio_id)
            if not audio_path or not os.path.exists(audio_path):
                logger.warning(f"Audio file not found: {audio_id}")
                continue

            # Load audio
            audio, sample_rate = sf.read(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Compute STFT
            stft = librosa.stft(audio, n_fft=2048, hop_length=512)
            magnitude = np.abs(stft)
            magnitude_db = librosa.amplitude_to_db(magnitude, ref=np.max)

            spectrograms.append(
                {
                    "audio_id": audio_id,
                    "spectrogram": magnitude_db,
                    "sample_rate": sample_rate,
                }
            )

        if len(spectrograms) < 2:
            raise HTTPException(
                status_code=400,
                detail="At least 2 valid audio files required for comparison",
            )

        # Normalize spectrograms to same dimensions
        min_freq_bins = min(s["spectrogram"].shape[0] for s in spectrograms)
        min_time_frames = min(s["spectrogram"].shape[1] for s in spectrograms)

        normalized = []
        for spec in spectrograms:
            normalized.append(spec["spectrogram"][:min_freq_bins, :min_time_frames])

        # Compute differences
        differences = []
        for i in range(len(normalized) - 1):
            diff = np.abs(normalized[i] - normalized[i + 1])
            differences.append(
                {
                    "audio_id_1": spectrograms[i]["audio_id"],
                    "audio_id_2": spectrograms[i + 1]["audio_id"],
                    "mean_difference": float(np.mean(diff)),
                    "max_difference": float(np.max(diff)),
                    "similarity": float(1.0 / (1.0 + np.mean(diff))),
                }
            )

        return {
            "compared_files": len(spectrograms),
            "differences": differences,
            "summary": {
                "mean_similarity": float(np.mean([d["similarity"] for d in differences])),
                "min_similarity": float(np.min([d["similarity"] for d in differences])),
                "max_similarity": float(np.max([d["similarity"] for d in differences])),
            },
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to compare spectrograms: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to compare spectrograms: {e!s}",
        ) from e


@router.get("/export/{audio_id}")
@cache_response(ttl=300)  # Cache for 5 minutes (exported spectrograms are static)
async def export_spectrogram(
    audio_id: str,
    format: str = Query("png", pattern="^(png|jpg|svg)$"),
    width: int = Query(1920, ge=100, le=4096),
    height: int = Query(1080, ge=100, le=4096),
):
    """Export spectrogram as an image."""
    # Validate dimensions to prevent memory issues
    MAX_PIXELS = 4096 * 4096  # ~16MP
    total_pixels = width * height
    if total_pixels > MAX_PIXELS:
        raise HTTPException(
            status_code=400,
            detail=(
                f"Image dimensions too large ({width}x{height} = "
                f"{total_pixels} pixels). Maximum: {MAX_PIXELS} pixels"
            ),
        )

    try:
        # Load audio and generate spectrogram
        import tempfile

        from fastapi.responses import FileResponse

        from .audio import _get_audio_path

        try:
            import librosa
            import numpy as np
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="librosa/soundfile not available. Install with: pip install librosa soundfile",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        # Load audio
        audio, _sample_rate = sf.read(audio_path)
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Compute STFT
        n_fft = 2048
        hop_length = 512
        stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
        magnitude = np.abs(stft)
        magnitude_db = librosa.amplitude_to_db(magnitude, ref=np.max)

        # Render to image
        try:
            import matplotlib

            matplotlib.use("Agg")  # Non-interactive backend
            import matplotlib.pyplot as plt

            # Create figure
            fig, ax = plt.subplots(figsize=(width / 100, height / 100), dpi=100)

            # Display spectrogram
            im = ax.imshow(
                magnitude_db,
                aspect="auto",
                origin="lower",
                cmap="viridis",
                interpolation="bilinear",
            )

            ax.set_xlabel("Time")
            ax.set_ylabel("Frequency (Hz)")
            ax.set_title(f"Spectrogram - {audio_id}")

            # Add colorbar
            plt.colorbar(im, ax=ax, label="Magnitude (dB)")

            # Save to temporary file
            output_path = tempfile.mktemp(suffix=f".{format}")
            plt.savefig(output_path, format=format, bbox_inches="tight", dpi=100)
            plt.close(fig)

            return FileResponse(
                output_path,
                media_type=f"image/{format}",
                filename=f"spectrogram_{audio_id}.{format}",
            )
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="matplotlib not available. Install with: pip install matplotlib",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Failed to export spectrogram for {audio_id}: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"Failed to export spectrogram: {e!s}",
        ) from e


@router.get("/color-schemes")
@cache_response(ttl=600)  # Cache for 10 minutes (color schemes are static)
async def get_color_schemes():
    """Get available color schemes."""
    return {
        "schemes": [
            {"id": "viridis", "name": "Viridis", "description": "Perceptually uniform"},
            {"id": "plasma", "name": "Plasma", "description": "High contrast"},
            {"id": "inferno", "name": "Inferno", "description": "Dark theme"},
            {"id": "magma", "name": "Magma", "description": "Warm colors"},
            {"id": "hot", "name": "Hot", "description": "Black-red-yellow-white"},
            {"id": "cool", "name": "Cool", "description": "Cyan-magenta"},
            {"id": "grayscale", "name": "Grayscale", "description": "Black and white"},
        ]
    }
