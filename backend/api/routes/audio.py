"""
Audio Analysis Routes - Basic Single-File Operations

Endpoints for quick audio visualization and analysis of single files.
Optimized for real-time UI rendering with minimal latency.

Route Purposes (GAP-B06):
- /api/audio/waveform: Quick waveform generation for audio files < 1 minute
- /api/audio/spectrogram: Quick spectrogram generation for single files
- /api/audio/meters: Real-time level meters (peak, RMS, LUFS)
- /api/audio/loudness: Loudness measurement for single files

For advanced operations (batch processing, streaming, comparison), use:
- /api/waveform for advanced waveform features (caching, config, analysis)
- /api/spectrogram for advanced spectrogram features (custom FFT, overlays)

See also: docs/api/ROUTE_MAPPING.md for complete route documentation.
"""

from __future__ import annotations

import logging
import os
import shutil
import uuid
from pathlib import Path

import numpy as np
from fastapi import APIRouter, File, HTTPException, Query, UploadFile
from pydantic import BaseModel

from backend.config.path_config import get_path

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/audio", tags=["audio"])

# Import audio utilities
try:
    import librosa
    import soundfile as sf

    HAS_AUDIO_LIBS = True
except ImportError:
    HAS_AUDIO_LIBS = False
    logger.warning("librosa/soundfile not available. Audio analysis endpoints will be limited.")


class WaveformData(BaseModel):
    """Waveform data for rendering."""

    samples: list[float]
    sample_rate: int
    duration: float
    channels: int
    width: int
    mode: str  # "peak" or "rms"


class SpectrogramFrame(BaseModel):
    """Single frame of spectrogram data."""

    time: float
    frequencies: list[float]


class SpectrogramData(BaseModel):
    """Spectrogram data for rendering."""

    frames: list[SpectrogramFrame]
    sample_rate: int
    fft_size: int
    hop_length: int
    width: int
    height: int


class AudioMeters(BaseModel):
    """Audio level meters data."""

    peak: float
    rms: float
    lufs: float | None = None
    channels: list[dict[str, float]] = []


class LoudnessData(BaseModel):
    """Loudness (LUFS) data for visualization."""

    times: list[float]
    lufs_values: list[float]
    integrated_lufs: float | None = None
    peak_lufs: float | None = None
    sample_rate: int
    duration: float


class RadarData(BaseModel):
    """Radar chart data for frequency domain visualization."""

    band_names: list[str]
    frequencies: list[float]
    magnitudes: list[float]
    phases: list[float] | None = None
    sample_rate: int


class PhaseData(BaseModel):
    """Phase analysis data for visualization."""

    times: list[float]
    correlation: list[float]
    phase_difference: list[float] | None = None
    stereo_width: list[float] | None = None
    average_correlation: float | None = None
    sample_rate: int
    duration: float


class RadarAxis(BaseModel):
    """Radar chart axis definition."""

    name: str
    max_value: float = 1.0
    min_value: float = 0.0


class RadarDataPoint(BaseModel):
    """Radar chart data point."""

    axis_name: str
    value: float  # Normalized 0.0-1.0


class RadarChartData(BaseModel):
    """Radar chart data for rendering."""

    axes: list[RadarAxis]
    points: list[RadarDataPoint]
    label: str = ""


def _get_audio_path(audio_id: str) -> str | None:
    """Get audio file path from audio_id. Thin wrapper over resolve_audio_path."""
    from backend.services.audio_path_resolver import resolve_audio_path

    return resolve_audio_path(audio_id)


@router.get("/file/{audio_id}")
async def get_audio_file(audio_id: str):
    """Stream an audio file by ID (uploaded, synthesized, or project audio)."""
    from starlette.responses import FileResponse

    file_path = _get_audio_path(audio_id)
    if not file_path or not os.path.isfile(file_path):
        raise HTTPException(status_code=404, detail=f"Audio not found: {audio_id}")
    ext = os.path.splitext(file_path)[1].lower()
    media_type = {
        ".wav": "audio/wav",
        ".mp3": "audio/mpeg",
        ".flac": "audio/flac",
        ".ogg": "audio/ogg",
    }.get(ext, "audio/wav")
    return FileResponse(file_path, media_type=media_type, filename=f"{audio_id}{ext}")


def _downsample_waveform(
    audio: np.ndarray, sample_rate: int, target_width: int, mode: str = "peak"
) -> np.ndarray:
    """Downsample audio to target width for waveform rendering."""
    if not HAS_AUDIO_LIBS:
        # Fallback: simple downsampling
        if len(audio) <= target_width:
            return np.asarray(audio.tolist())

        step = len(audio) // target_width
        if mode == "peak":
            # Peak mode: take max absolute value in each bin
            downsampled = []
            for i in range(0, len(audio), step):
                chunk = audio[i : i + step]
                if len(chunk) > 0:
                    downsampled.append(float(np.max(np.abs(chunk))))
        else:
            # RMS mode: calculate RMS in each bin
            downsampled = []
            for i in range(0, len(audio), step):
                chunk = audio[i : i + step]
                if len(chunk) > 0:
                    rms = np.sqrt(np.mean(chunk**2))
                    downsampled.append(float(rms))

        return np.array(downsampled[:target_width])

    # Use librosa for better downsampling
    if mode == "peak":
        # Peak mode: take max absolute value
        hop_length = max(1, len(audio) // target_width)
        downsampled = []
        for i in range(0, len(audio), hop_length):
            chunk = audio[i : i + hop_length]
            if len(chunk) > 0:
                downsampled.append(float(np.max(np.abs(chunk))))
        return np.array(downsampled[:target_width])
    else:
        # RMS mode: use librosa's frame function
        hop_length = max(1, len(audio) // target_width)
        frames = librosa.util.frame(audio, frame_length=hop_length, hop_length=hop_length, axis=0)
        rms = np.sqrt(np.mean(frames**2, axis=0))
        return np.asarray(rms[:target_width])


@router.get("/waveform", response_model=WaveformData)
@cache_response(ttl=300)  # Cache for 5 minutes (waveform data is static for a given audio file)
def get_waveform_data(
    audio_id: str = Query(..., description="Audio file identifier"),
    width: int = Query(1024, description="Target pixel width for downsampling"),
    mode: str = Query("peak", description="Waveform mode: 'peak' or 'rms'"),
) -> WaveformData:
    """
    Get downsampled waveform data for rendering.

    Returns waveform samples optimized for the specified width.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")
        if width <= 0:
            raise HTTPException(status_code=400, detail="Width must be greater than 0")
        if width > 10000:
            raise HTTPException(status_code=400, detail="Width cannot exceed 10000")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for waveform: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)

            # Handle stereo by converting to mono
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Calculate duration
            duration = len(audio) / sample_rate

            # Downsample waveform
            if mode not in ["peak", "rms"]:
                mode = "peak"

            downsampled = _downsample_waveform(audio, sample_rate, width, mode)

            return WaveformData(
                samples=downsampled.tolist(),
                sample_rate=sample_rate,
                duration=duration,
                channels=1,
                width=len(downsampled),
                mode=mode,
            )
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for waveform {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error generating waveform data for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate waveform data: {e!s}")


@router.get("/spectrogram", response_model=SpectrogramData)
@cache_response(ttl=300)  # Cache for 5 minutes (spectrogram data is static for a given audio file)
def get_spectrogram_data(
    audio_id: str = Query(..., description="Audio file identifier"),
    width: int = Query(512, description="Target pixel width"),
    height: int = Query(256, description="Target pixel height (frequency bins)"),
) -> SpectrogramData:
    """
    Get FFT-based spectrogram data for rendering.

    Returns spectrogram frames optimized for the specified dimensions.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")
        if width <= 0:
            raise HTTPException(status_code=400, detail="Width must be greater than 0")
        if width > 5000:
            raise HTTPException(status_code=400, detail="Width cannot exceed 5000")
        if height <= 0:
            raise HTTPException(status_code=400, detail="Height must be greater than 0")
        if height > 2048:
            raise HTTPException(status_code=400, detail="Height cannot exceed 2048")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for spectrogram: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)

            # Handle stereo by converting to mono
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Calculate STFT parameters
            n_fft = 2048
            hop_length = max(1, len(audio) // width)

            # Compute STFT
            stft = librosa.stft(audio, n_fft=n_fft, hop_length=hop_length)
            magnitude = np.abs(stft)

            # Convert to dB
            magnitude_db = librosa.amplitude_to_db(magnitude, ref=np.max)

            # Normalize to 0-1 range
            magnitude_normalized = (magnitude_db + 80) / 80.0  # Assuming -80dB floor
            magnitude_normalized = np.clip(magnitude_normalized, 0, 1)

            # Downsample frequency bins to target height
            if magnitude_normalized.shape[0] > height:
                # Average adjacent bins
                step = magnitude_normalized.shape[0] // height
                downsampled = []
                for i in range(0, magnitude_normalized.shape[0], step):
                    chunk = magnitude_normalized[i : i + step, :]
                    if len(chunk) > 0:
                        downsampled.append(np.mean(chunk, axis=0))
                magnitude_normalized = np.array(downsampled)

            # Create frames
            frames = []
            time_per_frame = hop_length / sample_rate

            for i in range(magnitude_normalized.shape[1]):
                frames.append(
                    SpectrogramFrame(
                        time=i * time_per_frame,
                        frequencies=magnitude_normalized[:, i].tolist(),
                    )
                )

            return SpectrogramData(
                frames=frames,
                sample_rate=sample_rate,
                fft_size=n_fft,
                hop_length=hop_length,
                width=len(frames),
                height=magnitude_normalized.shape[0],
            )
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for spectrogram {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error generating spectrogram data for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate spectrogram data: {e!s}")


@router.get("/loudness", response_model=LoudnessData)
@cache_response(ttl=300)  # Cache for 5 minutes (loudness data is static for a given audio file)
def get_loudness_data(
    audio_id: str = Query(..., description="Audio file identifier"),
    width: int = Query(1024, description="Target pixel width for downsampling"),
    block_size: float = Query(0.400, description="Block size in seconds for LUFS measurement"),
) -> LoudnessData:
    """
    Get loudness (LUFS) data over time for visualization.

    Returns LUFS values calculated at regular intervals, optimized for the specified width.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")
        if width <= 0:
            raise HTTPException(status_code=400, detail="Width must be greater than 0")
        if width > 10000:
            raise HTTPException(status_code=400, detail="Width cannot exceed 10000")
        if block_size <= 0:
            raise HTTPException(status_code=400, detail="Block size must be greater than 0")
        if block_size > 10.0:
            raise HTTPException(status_code=400, detail="Block size cannot exceed 10 seconds")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        # Try to import pyloudnorm
        try:
            import pyloudnorm as pyln

            HAS_PYLOUDNORM = True
        except ImportError:
            HAS_PYLOUDNORM = False
            logger.warning("pyloudnorm not available. LUFS calculation will be approximated.")

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for loudness analysis: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)

            # Handle stereo by converting to mono for loudness measurement
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Calculate duration
            duration = len(audio) / sample_rate

            # Calculate number of blocks
            num_blocks = max(1, width)
            block_samples = max(1, int(sample_rate * block_size))
            hop_samples = max(1, len(audio) // num_blocks)

            times = []
            lufs_values = []

            if HAS_PYLOUDNORM:
                # Use pyloudnorm for accurate LUFS measurement
                meter = pyln.Meter(sample_rate, block_size=block_size)

                # Calculate LUFS for each block
                for i in range(0, len(audio), hop_samples):
                    block_end = min(i + block_samples, len(audio))
                    block_audio = audio[i:block_end]

                    if len(block_audio) < block_samples * 0.5:  # Skip incomplete blocks
                        break

                    try:
                        loudness = meter.integrated_loudness(block_audio)
                        if np.isnan(loudness) or np.isinf(loudness):
                            loudness = -70.0  # Default to very quiet
                    except Exception:
                        loudness = -70.0  # Default to very quiet

                    time_pos = i / sample_rate
                    times.append(time_pos)
                    lufs_values.append(float(loudness))
            else:
                # Fallback: approximate LUFS from RMS
                for i in range(0, len(audio), hop_samples):
                    block_end = min(i + hop_samples, len(audio))
                    block_audio = audio[i:block_end]

                    if len(block_audio) == 0:
                        break

                    # Calculate RMS
                    rms = np.sqrt(np.mean(block_audio**2))

                    # Approximate LUFS from RMS (rough conversion)
                    # LUFS ≈ 20 * log10(RMS) - 0.691 (approximate offset)
                    if rms > 1e-10:
                        lufs_approx = 20 * np.log10(rms) - 0.691
                        lufs_approx = max(-70.0, min(0.0, lufs_approx))  # Clamp to reasonable range
                    else:
                        lufs_approx = -70.0

                    time_pos = i / sample_rate
                    times.append(time_pos)
                    lufs_values.append(float(lufs_approx))

            # Calculate integrated and peak LUFS
            integrated_lufs = None
            peak_lufs = None

            if HAS_PYLOUDNORM and len(audio) > 0:
                try:
                    meter = pyln.Meter(sample_rate)
                    integrated_lufs = float(meter.integrated_loudness(audio))
                    if np.isnan(integrated_lufs) or np.isinf(integrated_lufs):
                        integrated_lufs = None
                except Exception:
                    ...

            if len(lufs_values) > 0:
                peak_lufs = float(max(lufs_values))

            return LoudnessData(
                times=times,
                lufs_values=lufs_values,
                integrated_lufs=integrated_lufs,
                peak_lufs=peak_lufs,
                sample_rate=sample_rate,
                duration=duration,
            )
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for loudness analysis {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error generating loudness data for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate loudness data: {e!s}")


@router.get("/meters", response_model=AudioMeters)
@cache_response(ttl=1)  # Cache for 1 second (meters update very frequently)
def get_audio_meters(
    audio_id: str = Query(..., description="Audio file identifier")
) -> AudioMeters:
    """
    Get audio level meters data (peak, RMS, LUFS).

    Returns current audio levels for real-time meter updates.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for meters: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)

            # Handle multi-channel
            if len(audio.shape) == 1:
                audio = audio.reshape(-1, 1)

            num_channels = audio.shape[1]

            # Calculate peak and RMS per channel
            channels = []
            peak_values = []
            rms_values = []

            for ch in range(num_channels):
                channel_audio = audio[:, ch]
                peak = float(np.max(np.abs(channel_audio)))
                rms = float(np.sqrt(np.mean(channel_audio**2)))

                channels.append({"peak": peak, "rms": rms})
                peak_values.append(peak)
                rms_values.append(rms)

            # Overall peak and RMS
            overall_peak = float(np.max(peak_values))
            overall_rms = float(np.sqrt(np.mean([r**2 for r in rms_values])))

            # Calculate LUFS (simplified - use pyloudnorm if available)
            lufs = None
            try:
                import pyloudnorm as pyln

                meter = pyln.Meter(sample_rate)
                lufs = float(meter.integrated_loudness(audio))
            except (ImportError, Exception):
                # Fallback: estimate from RMS
                lufs = float(20 * np.log10(max(overall_rms, 1e-10)))

            return AudioMeters(peak=overall_peak, rms=overall_rms, lufs=lufs, channels=channels)
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for meters {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error calculating audio meters for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to calculate audio meters: {e!s}")


@router.get("/radar", response_model=RadarData)
@cache_response(ttl=300)  # Cache for 5 minutes (radar data is static for a given audio file)
def get_radar_data(audio_id: str = Query(..., description="Audio file identifier")) -> RadarData:
    """
    Get radar chart data for frequency domain visualization.

    Analyzes audio and returns frequency band magnitudes
    in a format suitable for radar chart rendering.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for radar analysis: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Compute FFT to get frequency domain representation
            n_fft = 2048
            hop_length = 512
            D = np.abs(librosa.stft(audio, n_fft=n_fft, hop_length=hop_length))

            # Average across time to get overall frequency spectrum
            avg_spectrum = np.mean(D, axis=1)

            # Define frequency bands (octave bands: 20Hz-20kHz)
            # Low: 20-250Hz, Low-Mid: 250-500Hz, Mid: 500-2kHz, High-Mid: 2-4kHz, High: 4-20kHz
            freq_bins = librosa.fft_frequencies(sr=sample_rate, n_fft=n_fft)

            # Define frequency bands
            band_ranges = [
                (20, 250),  # Low
                (250, 500),  # Low-Mid
                (500, 2000),  # Mid
                (2000, 4000),  # High-Mid
                (4000, 20000),  # High
            ]
            band_names = ["Low", "Low-Mid", "Mid", "High-Mid", "High"]

            magnitudes = []
            frequencies = []
            phases = []

            for (low_freq, high_freq), _band_name in zip(band_ranges, band_names):
                # Find frequency bins in this range
                mask = (freq_bins >= low_freq) & (freq_bins <= high_freq)
                if np.any(mask):
                    # Average magnitude in this band
                    band_magnitude = np.mean(avg_spectrum[mask])
                    # Center frequency
                    center_freq = np.mean(freq_bins[mask])

                    # Normalize magnitude (0-1 range)
                    max_magnitude = np.max(avg_spectrum)
                    normalized_magnitude = (
                        float(band_magnitude / max_magnitude) if max_magnitude > 0 else 0.0
                    )

                    magnitudes.append(normalized_magnitude)
                    frequencies.append(float(center_freq))
                    phases.append(0.0)  # Phase not computed for average spectrum

            # Normalize magnitudes to 0-1 range
            if magnitudes and max(magnitudes) > 0:
                max_mag = max(magnitudes)
                magnitudes = [m / max_mag for m in magnitudes]

            return RadarData(
                band_names=band_names,
                frequencies=frequencies,
                magnitudes=magnitudes,
                phases=phases if phases else None,
                sample_rate=sample_rate,
            )
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for radar analysis {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error generating radar data for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate radar data: {e!s}")


@router.get("/phase", response_model=PhaseData)
@cache_response(ttl=300)  # Cache for 5 minutes (phase data is static for a given audio file)
def get_phase_data(
    audio_id: str = Query(..., description="Audio file identifier"),
    window_size: float = Query(0.1, description="Window size in seconds for phase analysis"),
) -> PhaseData:
    """
    Get phase analysis data for visualization.

    Returns phase correlation, phase difference, and stereo width over time.
    """
    try:
        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")
        if window_size <= 0:
            raise HTTPException(status_code=400, detail="Window size must be greater than 0")
        if window_size > 5.0:
            raise HTTPException(status_code=400, detail="Window size cannot exceed 5 seconds")

        if not HAS_AUDIO_LIBS:
            raise HTTPException(
                status_code=503,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
            )

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            logger.warning(f"Audio file not found for phase analysis: {audio_id}")
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        try:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)
            duration = len(audio) / sample_rate

            # Handle mono vs stereo
            is_stereo = len(audio.shape) > 1 and audio.shape[1] >= 2
            if not is_stereo:
                # Convert mono to pseudo-stereo for analysis
                audio_stereo = np.column_stack([audio, audio])
            else:
                audio_stereo = audio[:, :2]  # Take first 2 channels

            left_channel = audio_stereo[:, 0]
            right_channel = audio_stereo[:, 1]

            # Calculate window samples
            window_samples = int(window_size * sample_rate)
            hop_samples = window_samples // 2  # 50% overlap
            num_windows = max(1, (len(left_channel) - window_samples) // hop_samples + 1)

            times = []
            correlations = []
            phase_differences = []
            stereo_widths = []

            for i in range(num_windows):
                start_idx = i * hop_samples
                end_idx = min(start_idx + window_samples, len(left_channel))

                if end_idx <= start_idx:
                    break

                left_window = left_channel[start_idx:end_idx]
                right_window = right_channel[start_idx:end_idx]

                # Calculate time for this window
                time_center = (start_idx + end_idx) / 2.0 / sample_rate
                times.append(float(time_center))

                # Calculate correlation
                # Normalize windows
                left_norm = left_window - np.mean(left_window)
                right_norm = right_window - np.mean(right_window)

                std_left = np.std(left_norm)
                std_right = np.std(right_norm)

                if std_left > 0 and std_right > 0:
                    correlation = np.corrcoef(left_norm, right_norm)[0, 1]
                    correlation = float(np.clip(correlation, -1.0, 1.0))
                else:
                    correlation = 0.0
                correlations.append(correlation)

                # Calculate phase difference using FFT
                left_fft = np.fft.rfft(left_window)
                right_fft = np.fft.rfft(right_window)

                # Get phase angles
                left_phase = np.angle(left_fft)
                right_phase = np.angle(right_fft)

                # Average phase difference (weighted by magnitude)
                magnitudes = np.abs(left_fft) + np.abs(right_fft)
                if np.sum(magnitudes) > 0:
                    phase_diff = np.sum((left_phase - right_phase) * magnitudes) / np.sum(
                        magnitudes
                    )
                    # Convert to degrees
                    phase_diff_degrees = float(np.degrees(phase_diff))
                    phase_diff_degrees = np.clip(phase_diff_degrees, -180, 180)
                else:
                    phase_diff_degrees = 0.0
                phase_differences.append(phase_diff_degrees)

                # Calculate stereo width (simplified: based on correlation)
                # Width of 1.0 = fully stereo (correlation = 0), 0.0 = mono (correlation = 1)
                stereo_width = float(1.0 - abs(correlation))
                stereo_widths.append(stereo_width)

            # Calculate average correlation
            avg_correlation = float(np.mean(correlations)) if correlations else None

            return PhaseData(
                times=times,
                correlation=correlations,
                phase_difference=phase_differences if phase_differences else None,
                stereo_width=stereo_widths if stereo_widths else None,
                average_correlation=avg_correlation,
                sample_rate=sample_rate,
                duration=float(duration),
            )
        except Exception as e:
            logger.error(
                f"Failed to load or process audio for phase analysis {audio_id}: {e!s}",
                exc_info=True,
            )
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error generating phase data for {audio_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate phase data: {e!s}")


# --- Audio Upload ---

# Base upload directory (outside repo to avoid Cursor brick risk)
_UPLOAD_BASE = str(get_path("audio_uploads"))
UPLOAD_DIR = _UPLOAD_BASE  # Legacy compatibility
UPLOAD_ORIGINALS_DIR = os.path.join(_UPLOAD_BASE, "originals")
UPLOAD_WAV_DIR = os.path.join(_UPLOAD_BASE, "wav")


def _migrate_audio_uploads_from_repo() -> None:
    """One-time migration: move existing data/audio_uploads to new location."""
    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(__file__))))
    old_base = os.path.join(project_root, "data", "audio_uploads")
    new_base = _UPLOAD_BASE
    if os.path.normpath(old_base) == os.path.normpath(new_base):
        return
    if not os.path.isdir(old_base):
        return
    try:
        items = list(Path(old_base).rglob("*"))
        files = [p for p in items if p.is_file()]
        if not files:
            return
        logger.info("Migrating %d files from data/audio_uploads to %s", len(files), new_base)
        for src in files:
            rel = src.relative_to(old_base)
            dst = Path(new_base) / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            if not dst.exists() or dst.stat().st_size != src.stat().st_size:
                shutil.copy2(src, dst)
        logger.info("Migration complete. Old files remain in %s (remove manually if desired).", old_base)
    except Exception as e:
        logger.warning("Audio upload migration skipped: %s", e)


_migrate_audio_uploads_from_repo()


class AudioUploadResponse(BaseModel):
    """Response from audio upload."""

    id: str
    filename: str
    path: str  # Path to canonical WAV file
    original_path: str | None = None  # Path to original uploaded file
    canonical_path: str | None = None  # Alias for path (WAV)
    size: int
    original_size: int | None = None
    content_type: str | None = None
    detected_format: str | None = None
    converted: bool = False  # True if conversion to WAV was performed


@router.post("/upload", response_model=AudioUploadResponse, status_code=201)
async def upload_audio(file: UploadFile = File(...)):
    """
    Upload an audio file for processing.

    Validates the file is a genuine audio file (format, size, content headers)
    before persisting to disk. The original file is preserved and a canonical
    WAV copy is created for internal processing.

    **Supported audio formats (Standard Set):**
    - WAV (.wav) - Recommended, uncompressed
    - MP3 (.mp3) - Compressed, widely supported
    - FLAC (.flac) - Lossless compressed
    - OGG/Vorbis (.ogg) - Open format compressed
    - Opus (.opus) - Modern compressed format
    - M4A (.m4a) - MPEG-4 Audio (AAC in container)
    - AAC (.aac) - Advanced Audio Coding (raw)
    - WMA (.wma) - Windows Media Audio
    - AIFF (.aiff, .aif) - Apple lossless format

    **Behavior:**
    - Original file is saved to originals/ directory
    - File is converted to canonical WAV format (44100 Hz, 16-bit, stereo)
    - WAV file is saved to wav/ directory
    - Both paths are returned in the response

    **Limits:**
    - Maximum file size: 500MB
    - Sample rates: 8000-96000 Hz supported (22050+ recommended for cloning)
    """
    # Ensure directories exist
    os.makedirs(UPLOAD_ORIGINALS_DIR, exist_ok=True)
    os.makedirs(UPLOAD_WAV_DIR, exist_ok=True)

    # Read file content
    content = await file.read()
    original_size = len(content)

    # Validate media file before saving (accepts audio + video for audio extraction)
    detected_format = None
    is_video_source = False
    try:
        from backend.core.security.file_validation import (
            FileCategory,
            validate_media_for_audio_extraction,
        )

        file_info = validate_media_for_audio_extraction(content, filename=file.filename)
        detected_format = file_info.extension
        is_video_source = file_info.category == FileCategory.VIDEO
        if is_video_source:
            logger.info(
                "Video file '%s' accepted for audio extraction (will convert to WAV)",
                file.filename,
            )
    except ImportError:
        logger.warning("file_validation module not available; skipping audio validation")
    except Exception as validation_error:
        logger.warning(
            "Audio upload validation failed for '%s': %s",
            file.filename,
            validation_error,
        )
        raise HTTPException(
            status_code=400, detail=f"Invalid audio file: {validation_error!s}"
        ) from validation_error

    file_id = str(uuid.uuid4())
    original_ext = os.path.splitext(file.filename or "audio.wav")[1] or ".wav"
    original_filename = f"{file_id}{original_ext}"
    original_path = os.path.join(UPLOAD_ORIGINALS_DIR, original_filename)

    wav_filename = f"{file_id}.wav"
    wav_path = os.path.join(UPLOAD_WAV_DIR, wav_filename)

    try:
        # Save original file
        from pathlib import Path

        Path(original_path).write_bytes(content)

        # Check if conversion is needed
        is_wav = original_ext.lower() in (".wav", ".wave")
        converted = False

        if is_wav:
            # Already WAV - just copy to wav directory
            shutil.copy2(original_path, wav_path)
        else:
            # Convert to WAV using AudioConversionService
            try:
                from backend.core.audio.conversion import get_conversion_service

                conversion_service = get_conversion_service()
                result = await conversion_service.convert_to_wav(
                    input_path=Path(original_path),
                    output_path=Path(wav_path),
                    sample_rate=44100,
                    channels=2,
                    bit_depth=16,
                )

                if not result.success:
                    logger.error(f"Audio conversion failed for {file.filename}: {result.error}")
                    # Fall back to keeping original only
                    shutil.copy2(original_path, wav_path)
                else:
                    converted = True
                    logger.info(f"Converted {file.filename} ({detected_format}) to WAV")
            except ImportError:
                logger.warning("AudioConversionService not available; copying original")
                shutil.copy2(original_path, wav_path)
            except Exception as conv_error:
                logger.warning(f"Conversion failed, copying original: {conv_error}")
                shutil.copy2(original_path, wav_path)

        wav_size = os.path.getsize(wav_path) if os.path.exists(wav_path) else original_size

        return AudioUploadResponse(
            id=file_id,
            filename=file.filename or original_filename,
            path=wav_path,  # Primary path is the canonical WAV
            original_path=original_path,
            canonical_path=wav_path,
            size=wav_size,
            original_size=original_size,
            content_type=file.content_type,
            detected_format=detected_format,
            converted=converted,
        )
    except Exception as e:
        # Clean up on failure
        for path in [original_path, wav_path]:
            if os.path.exists(path):
                try:
                    os.remove(path)
                # ALLOWED: bare except - Best effort cleanup, failure is acceptable
                except Exception:
                    pass
        logger.error(f"Audio upload failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Upload failed: {e!s}")


# --- Audio Export ---


class AudioExportRequest(BaseModel):
    """Request for audio export with format conversion."""

    source: str  # audio_id or filename
    format: str  # Target format (wav, mp3, flac, ogg, opus, m4a, aac, wma, aiff)
    sample_rate: int | None = None  # Output sample rate (Hz)
    channels: int | None = None  # Output channels (1=mono, 2=stereo)
    bitrate_kbps: int | None = None  # Bitrate for lossy formats
    normalize: bool = False  # Apply loudness normalization


class AudioExportResponse(BaseModel):
    """Response from audio export."""

    success: bool
    filename: str
    format: str
    size: int
    content_type: str
    error: str | None = None


# Supported export formats with MIME types
EXPORT_FORMAT_MIME_TYPES = {
    "wav": "audio/wav",
    "mp3": "audio/mpeg",
    "flac": "audio/flac",
    "ogg": "audio/ogg",
    "opus": "audio/opus",
    "m4a": "audio/mp4",
    "aac": "audio/aac",
    "wma": "audio/x-ms-wma",
    "aiff": "audio/aiff",
}


@router.post("/export")
async def export_audio(request: AudioExportRequest):
    """
    Export an audio file to a different format.

    Converts the source audio to the requested format and returns the file
    as a streaming response.

    **Supported export formats:**
    - wav - Uncompressed PCM (highest quality)
    - mp3 - MPEG Layer 3 (widely compatible)
    - flac - Free Lossless Audio Codec
    - ogg - OGG Vorbis
    - opus - Opus codec (modern, efficient)
    - m4a - MPEG-4 Audio (AAC)
    - aac - Advanced Audio Coding (raw)
    - wma - Windows Media Audio
    - aiff - Audio Interchange File Format

    **Parameters:**
    - source: Audio ID or filename to export
    - format: Target format (required)
    - sample_rate: Output sample rate (optional, uses source rate)
    - channels: Output channels (optional, uses source channels)
    - bitrate_kbps: Bitrate for lossy formats (optional, uses format default)
    - normalize: Apply loudness normalization (optional, default false)
    """
    import tempfile

    from fastapi.responses import StreamingResponse

    target_format = request.format.lower().lstrip(".")

    if target_format not in EXPORT_FORMAT_MIME_TYPES:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported export format: {request.format}. "
            f"Supported formats: {', '.join(EXPORT_FORMAT_MIME_TYPES.keys())}",
        )

    # Find source audio file
    audio_path = _get_audio_path(request.source)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {request.source}")

    # Import conversion service
    try:
        from backend.core.audio.conversion import get_conversion_service
        from backend.core.audio.formats import AudioFormat, get_format_by_extension
    except ImportError as e:
        logger.error(f"Audio conversion module not available: {e}")
        raise HTTPException(status_code=503, detail="Audio conversion service not available")

    # Map format string to AudioFormat enum
    format_map = {
        "wav": AudioFormat.WAV,
        "mp3": AudioFormat.MP3,
        "flac": AudioFormat.FLAC,
        "ogg": AudioFormat.OGG,
        "opus": AudioFormat.OPUS,
        "m4a": AudioFormat.M4A,
        "aac": AudioFormat.AAC,
        "wma": AudioFormat.WMA,
        "aiff": AudioFormat.AIFF,
    }

    audio_format = format_map.get(target_format)
    if audio_format is None:
        raise HTTPException(status_code=400, detail=f"Unknown audio format: {target_format}")

    # Create temporary output file
    with tempfile.NamedTemporaryFile(suffix=f".{target_format}", delete=False) as tmp:
        output_path = Path(tmp.name)

    try:
        # Perform conversion
        service = get_conversion_service()
        result = await service.convert_to_format(
            input_path=Path(audio_path),
            output_path=output_path,
            target_format=audio_format,
            bitrate_kbps=request.bitrate_kbps,
            sample_rate=request.sample_rate,
            channels=request.channels,
            normalize=request.normalize,
        )

        if not result.success:
            raise HTTPException(status_code=500, detail=f"Conversion failed: {result.error}")

        # Generate output filename
        source_basename = os.path.splitext(os.path.basename(audio_path))[0]
        output_filename = f"{source_basename}.{target_format}"

        # Stream the file back
        def iterfile():
            with open(output_path, "rb") as f:
                while chunk := f.read(65536):  # 64KB chunks
                    yield chunk
            # Clean up temp file after streaming
            try:
                os.unlink(output_path)
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except Exception:
                pass

        return StreamingResponse(
            iterfile(),
            media_type=EXPORT_FORMAT_MIME_TYPES[target_format],
            headers={
                "Content-Disposition": f'attachment; filename="{output_filename}"',
                "Content-Length": str(result.file_size_bytes),
            },
        )

    except HTTPException:
        # Clean up and re-raise
        if output_path.exists():
            try:
                os.unlink(output_path)
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except Exception:
                pass
        raise
    except Exception as e:
        # Clean up on error
        if output_path.exists():
            try:
                os.unlink(output_path)
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except Exception:
                pass
        logger.error(f"Audio export failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Export failed: {e!s}")


@router.get("/formats")
async def get_supported_formats():
    """
    Get list of supported audio formats for import and export.

    Returns format information including extensions, MIME types,
    and whether the format is lossy or lossless.
    """
    try:
        from backend.core.audio.formats import STANDARD_AUDIO_FORMATS

        formats = []
        for fmt_info in STANDARD_AUDIO_FORMATS.values():
            formats.append(
                {
                    "id": fmt_info.format.value,
                    "name": fmt_info.name,
                    "description": fmt_info.description,
                    "extensions": list(fmt_info.extensions),
                    "mime_types": list(fmt_info.mime_types),
                    "is_lossy": fmt_info.is_lossy,
                    "supports_metadata": fmt_info.supports_metadata,
                    "default_bitrate_kbps": fmt_info.default_bitrate_kbps,
                }
            )

        return {
            "formats": formats,
            "import_extensions": list(EXPORT_FORMAT_MIME_TYPES.keys()),
            "export_extensions": list(EXPORT_FORMAT_MIME_TYPES.keys()),
        }
    except ImportError:
        # Fallback if format catalog not available
        return {
            "formats": [],
            "import_extensions": list(EXPORT_FORMAT_MIME_TYPES.keys()),
            "export_extensions": list(EXPORT_FORMAT_MIME_TYPES.keys()),
        }
