"""
Advanced Waveform Visualization Routes

Endpoints for advanced waveform analysis and visualization with caching,
configuration management, and detailed analysis features.

Route Purposes (GAP-B06):
- /api/waveform/config/{audio_id}: Waveform display configuration (zoom, channels)
- /api/waveform/data/{audio_id}: Detailed waveform data with optional analysis
- /api/waveform/analysis/{audio_id}: Peak/RMS/crest factor analysis
- /api/waveform/compare: A/B waveform comparison (batch operation)

Use Cases:
- Multi-channel display configuration
- Persistent waveform settings per audio file
- Detailed signal analysis (dynamic range, crest factor, zero-crossing rate)
- A/B comparison between two audio files

For quick single-file operations (< 1 min audio), consider using /api/audio/waveform.

See also: docs/api/ROUTE_MAPPING.md for complete route documentation.
"""

from __future__ import annotations

import asyncio
import logging
import os

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/waveform", tags=["waveform"])

# In-memory waveform data cache (replace with database in production)
_waveform_cache: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class WaveformConfig(BaseModel):
    """Waveform visualization configuration."""

    audio_id: str
    zoom_level: float = 1.0  # 0.1 to 10.0
    show_channels: list[int] = [0]  # Which channels to display
    show_rms: bool = True
    show_peak: bool = True
    show_zero_crossings: bool = False
    color_scheme: str = "default"  # default, heatmap, spectral
    time_range: dict[str, float] | None = None  # {"start": 0.0, "end": 10.0}


class WaveformData(BaseModel):
    """Waveform data for visualization."""

    audio_id: str
    sample_rate: int
    channels: int
    duration: float
    samples: list[list[float]]  # Per-channel sample data
    rms_values: list[float] | None = None
    peak_values: list[float] | None = None
    zero_crossings: list[int] | None = None
    time_points: list[float]  # Time points for each sample


class WaveformAnalysis(BaseModel):
    """Waveform analysis results."""

    audio_id: str
    peak_amplitude: float
    rms_amplitude: float
    dynamic_range: float
    crest_factor: float
    zero_crossing_rate: float
    dc_offset: float


@router.get("/config/{audio_id}", response_model=WaveformConfig)
@cache_response(ttl=300)  # Cache for 5 minutes (config is relatively static)
async def get_waveform_config(audio_id: str):
    """Get waveform configuration for an audio file."""
    cache_key = f"config_{audio_id}"
    if cache_key in _waveform_cache:
        config = _waveform_cache[cache_key]
        return WaveformConfig(**config)

    # Default config
    return WaveformConfig(audio_id=audio_id)


@router.put("/config/{audio_id}", response_model=WaveformConfig)
async def update_waveform_config(audio_id: str, config: WaveformConfig):
    """Update waveform configuration."""
    cache_key = f"config_{audio_id}"
    _waveform_cache[cache_key] = config.model_dump()
    logger.debug(f"Updated waveform config for {audio_id}")
    return config


@router.get("/data/{audio_id}", response_model=WaveformData)
@cache_response(ttl=300)  # Cache for 5 minutes (waveform data is static for a given audio file)
async def get_waveform_data(
    audio_id: str,
    zoom_level: float | None = None,
    time_start: float | None = None,
    time_end: float | None = None,
):
    """Get waveform data for visualization."""
    import numpy as np

    try:
        # Get audio file path
        from backend.services.audio_path_resolver import resolve_audio_path

        audio_path = resolve_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {audio_id}",
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

        # Handle mono vs multi-channel
        if len(audio.shape) == 1:
            channels = 1
            audio = audio.reshape(-1, 1)
        else:
            channels = audio.shape[1]

        duration = len(audio) / sample_rate

        # Apply time range filter if specified
        if time_start is not None or time_end is not None:
            start_sample = int((time_start or 0.0) * sample_rate)
            end_sample = int((time_end or duration) * sample_rate)
            start_sample = max(0, min(start_sample, len(audio)))
            end_sample = max(start_sample, min(end_sample, len(audio)))
            audio = audio[start_sample:end_sample]
            duration = len(audio) / sample_rate

        # Calculate downsample factor based on zoom level
        downsample_factor = 1.0
        if zoom_level:
            downsample_factor = max(1.0, zoom_level)

        # Downsample if needed (for performance)
        target_samples = int(len(audio) / downsample_factor)
        if target_samples < len(audio):
            # Simple downsampling by taking every Nth sample
            step = len(audio) // target_samples
            audio = audio[::step]

        # Extract samples per channel
        samples = [audio[:, ch].tolist() for ch in range(channels)]

        # Calculate time points
        time_points = [
            (i / sample_rate) * (step if target_samples < len(audio) else 1)
            for i in range(len(audio))
        ]

        # Calculate RMS and peak values per channel
        rms_values = [float(np.sqrt(np.mean(audio[:, ch] ** 2))) for ch in range(channels)]
        peak_values = [float(np.max(np.abs(audio[:, ch]))) for ch in range(channels)]

        # Calculate zero crossings per channel
        zero_crossings = []
        for ch in range(channels):
            channel_audio = audio[:, ch]
            # Find sign changes
            sign_changes = np.diff(np.signbit(channel_audio))
            zero_crossings.append(int(np.sum(sign_changes)))

        return WaveformData(
            audio_id=audio_id,
            sample_rate=sample_rate,
            channels=channels,
            duration=duration,
            samples=samples,
            rms_values=rms_values,
            peak_values=peak_values,
            zero_crossings=zero_crossings,
            time_points=time_points,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get waveform data for {audio_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to process waveform data: {e!s}",
        )


@router.get("/analysis/{audio_id}", response_model=WaveformAnalysis)
@cache_response(ttl=300)  # Cache for 5 minutes (analysis results are static for a given audio file)
async def analyze_waveform(audio_id: str):
    """Analyze waveform and return metrics."""
    import numpy as np

    try:
        # Get audio file path
        from backend.services.audio_path_resolver import resolve_audio_path

        audio_path = resolve_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {audio_id}",
            )

        # Load audio file
        try:
            from backend.audio.audio_utils import load_audio

            audio, _sample_rate = load_audio(audio_path)
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

        # Convert to mono for analysis if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Calculate peak amplitude
        peak_amplitude = float(np.max(np.abs(audio)))

        # Calculate RMS amplitude
        rms_amplitude = float(np.sqrt(np.mean(audio**2)))

        # Calculate dynamic range (peak to RMS ratio in dB)
        dynamic_range = 20 * np.log10(peak_amplitude / rms_amplitude) if rms_amplitude > 0 else 0.0

        # Calculate crest factor (peak to RMS ratio)
        crest_factor = peak_amplitude / rms_amplitude if rms_amplitude > 0 else 0.0

        # Calculate zero crossing rate
        sign_changes = np.diff(np.signbit(audio))
        num_zero_crossings = np.sum(sign_changes)
        zero_crossing_rate = num_zero_crossings / len(audio) if len(audio) > 0 else 0.0

        # Calculate DC offset
        dc_offset = float(np.mean(audio))

        return WaveformAnalysis(
            audio_id=audio_id,
            peak_amplitude=peak_amplitude,
            rms_amplitude=rms_amplitude,
            dynamic_range=float(dynamic_range),
            crest_factor=float(crest_factor),
            zero_crossing_rate=float(zero_crossing_rate),
            dc_offset=dc_offset,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to analyze waveform for {audio_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to analyze waveform: {e!s}",
        )


@router.get("/compare")
@cache_response(
    ttl=300
)  # Cache for 5 minutes (comparison results are static for given audio files)
async def compare_waveforms(audio_id_1: str, audio_id_2: str):
    """Compare two waveforms."""
    import numpy as np

    try:
        # Get audio file paths
        from backend.services.audio_path_resolver import resolve_audio_path

        audio_path_1 = resolve_audio_path(audio_id_1)
        audio_path_2 = resolve_audio_path(audio_id_2)

        if not audio_path_1 or not os.path.exists(audio_path_1):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {audio_id_1}",
            )
        if not audio_path_2 or not os.path.exists(audio_path_2):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file not found for audio_id: {audio_id_2}",
            )

        # Load both audio files
        try:
            from backend.audio.audio_utils import load_audio

            audio_1, sr_1 = load_audio(audio_path_1)
            audio_2, sr_2 = load_audio(audio_path_2)
        except ImportError as e:
            raise HTTPException(
                status_code=503,
                detail=f"Audio processing libraries not available: {e!s}. "
                "Install with: pip install librosa==0.11.0 soundfile==0.12.1",
            )
        except Exception as e:
            logger.error(f"Failed to load audio files: {e}")
            raise HTTPException(
                status_code=500,
                detail=f"Failed to load audio files: {e!s}",
            )

        # Convert to mono for comparison
        if len(audio_1.shape) > 1:
            audio_1 = np.mean(audio_1, axis=1)
        if len(audio_2.shape) > 1:
            audio_2 = np.mean(audio_2, axis=1)

        # Resample to same rate if needed
        if sr_1 != sr_2:
            try:
                import librosa

                if sr_1 > sr_2:
                    audio_1 = librosa.resample(audio_1, orig_sr=sr_1, target_sr=sr_2)
                    sr_1 = sr_2
                else:
                    audio_2 = librosa.resample(audio_2, orig_sr=sr_2, target_sr=sr_1)
                    sr_2 = sr_1
            except ImportError:
                raise HTTPException(
                    status_code=503,
                    detail="librosa required for resampling. "
                    "Install with: pip install librosa==0.11.0",
                )

        # Pad or truncate to same length
        min_len = min(len(audio_1), len(audio_2))
        audio_1 = audio_1[:min_len]
        audio_2 = audio_2[:min_len]

        # Calculate similarity using cross-correlation
        # Normalize both signals
        audio_1_norm = audio_1 / (np.linalg.norm(audio_1) + 1e-10)
        audio_2_norm = audio_2 / (np.linalg.norm(audio_2) + 1e-10)

        # Cross-correlation for similarity
        correlation = np.correlate(audio_1_norm, audio_2_norm, mode="valid")
        similarity = float(np.max(correlation))

        # Calculate amplitude difference
        amp_1 = np.mean(np.abs(audio_1))
        amp_2 = np.mean(np.abs(audio_2))
        amplitude_difference = abs(amp_1 - amp_2) / max(amp_1, amp_2, 1e-10)

        # Calculate phase difference (using cross-correlation lag)
        if len(correlation) > 0:
            max_corr_idx = np.argmax(correlation)
            # Lag in seconds
            timing_difference = max_corr_idx / sr_1
        else:
            timing_difference = 0.0

        # Phase difference (simplified - difference in zero crossings)
        zc_1 = np.sum(np.diff(np.signbit(audio_1)))
        zc_2 = np.sum(np.diff(np.signbit(audio_2)))
        phase_difference = abs(zc_1 - zc_2) / max(zc_1, zc_2, 1) if max(zc_1, zc_2) > 0 else 0.0

        return {
            "similarity": float(similarity),
            "phase_difference": float(phase_difference),
            "amplitude_difference": float(amplitude_difference),
            "timing_difference": float(timing_difference),
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Failed to compare waveforms {audio_id_1} and {audio_id_2}: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"Failed to compare waveforms: {e!s}",
        )
