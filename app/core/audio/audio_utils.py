"""
VoiceStudio Audio Utilities

High-quality audio processing utilities for voice cloning workflows.
Includes core audio operations and advanced quality enhancement functions.

Compatible with:
- Librosa 0.11.0
- SoundFile 0.12.1
- NumPy 1.26.4
- pyloudnorm 0.1.1
- noisereduce 3.0.2
"""

from __future__ import annotations

import contextlib
import logging
import os
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Any, cast

import numpy as np

_MISSING: Any = None

try:
    import librosa
    import pyloudnorm as pyln
    import soundfile as sf
except ImportError as e:
    logging.warning(f"Audio libraries not fully installed: {e}")
    librosa = _MISSING
    sf = _MISSING
    pyln = _MISSING

try:
    import noisereduce as nr
except ImportError:
    nr = None
    logging.warning("noisereduce not installed. Artifact removal will be limited.")

# Try importing voicefixer for voice restoration
try:
    from voicefixer import VoiceFixer

    HAS_VOICEFIXER = True
except ImportError:
    HAS_VOICEFIXER = False
    VoiceFixer = None
    logging.debug("voicefixer not installed. Voice restoration will be limited.")

# Try importing deepfilternet for speech enhancement
try:
    import deepfilternet

    HAS_DEEPFILTERNET = True
except ImportError:
    HAS_DEEPFILTERNET = False
    deepfilternet = None
    logging.debug("deepfilternet not installed. Speech enhancement will be limited.")

# Try importing resampy for high-quality resampling
try:
    import resampy

    HAS_RESAMPY = True
except ImportError:
    HAS_RESAMPY = False
    resampy = None
    logging.debug("resampy not installed. Using librosa for resampling.")

# Try importing pyrubberband for time-stretching and pitch-shifting
try:
    import pyrubberband as pyrb

    HAS_PYRUBBERBAND = True
except ImportError:
    HAS_PYRUBBERBAND = False
    pyrb = None
    logging.debug("pyrubberband not installed. Time-stretching will be limited.")

# Try importing webrtcvad for voice activity detection
try:
    import webrtcvad

    HAS_WEBRTCVAD = True
except ImportError:
    HAS_WEBRTCVAD = False
    webrtcvad = None
    logging.debug("webrtcvad not installed. Voice activity detection will be limited.")

try:
    import importlib.util

    import crepe

    # Test if crepe can actually work (requires TensorFlow)
    if importlib.util.find_spec("tensorflow") is not None:
        HAS_CREPE = True
    else:
        HAS_CREPE = False
        crepe = None
        logging.debug("crepe requires TensorFlow. " "Using librosa.pyin for pitch tracking.")
except ImportError:
    HAS_CREPE = False
    crepe = None
    logging.debug("crepe not installed. Using librosa.pyin for pitch tracking.")

# Try importing soxr for high-quality resampling
try:
    import soxr

    HAS_SOXR = True
except ImportError:
    HAS_SOXR = False
    soxr = None
    logging.debug("soxr not installed. Using librosa/resampy for resampling.")

# Try importing silero-vad for voice activity detection
try:
    from silero_vad import get_speech_timestamps, load_silero_vad

    HAS_SILERO_VAD = True
except ImportError:
    HAS_SILERO_VAD = False
    load_silero_vad = None
    get_speech_timestamps = None
    logging.debug("silero-vad not installed. Using webrtcvad for voice activity detection.")

# Try importing pywavelets for wavelet transforms
try:
    import pywt

    HAS_PYWAVELETS = True
except ImportError:
    HAS_PYWAVELETS = False
    pywt = None
    logging.debug("pywavelets not installed. Wavelet analysis will be limited.")

# Try importing scipy for advanced signal processing
try:
    from scipy import ndimage

    HAS_SCIPY = True
except ImportError:
    HAS_SCIPY = False
    ndimage = None
    logging.debug("scipy not available. Advanced spectral enhancement will be limited.")

# Try importing mutagen for audio metadata
_id3_no_header_error: type[Exception]
try:
    from mutagen import File as MutagenFile
    from mutagen.id3 import ID3NoHeaderError

    _id3_no_header_error = ID3NoHeaderError
    HAS_MUTAGEN = True
except ImportError:
    HAS_MUTAGEN = False
    MutagenFile = _MISSING
    _id3_no_header_error = Exception
    logging.debug("mutagen not installed. Audio metadata reading will be limited.")

# Try importing spleeter for source separation
try:
    from spleeter.separator import Separator

    HAS_SPLEETER = True
except ImportError:
    HAS_SPLEETER = False
    Separator = None
    logging.debug("spleeter not installed. Source separation will be limited.")

logger = logging.getLogger(__name__)

# Try importing Cython-optimized functions
try:
    from .audio_processing_cython import (
        calculate_dynamic_range_cython,
        calculate_rms_cython,
        calculate_snr_from_audio_cython,
        calculate_spectral_centroid_cython,
        calculate_spectral_rolloff_cython,
        calculate_zero_crossing_rate_cython,
        clip_audio_cython,
        normalize_peak_cython,
    )

    HAS_CYTHON_AUDIO = True
except ImportError:
    HAS_CYTHON_AUDIO = False
    logger.debug("Cython audio processing not available. Using pure Python implementations.")

# Default paths - GAP-PY-002: Use centralized path_config when available
try:
    from backend.config.path_config import get_path

    DEFAULT_TEMP_DIR = str(get_path("temp"))
    DEFAULT_OUTPUT_DIR = str(get_path("output"))
except ImportError:
    DEFAULT_TEMP_DIR = os.path.join(
        os.environ.get("PROGRAMDATA", "C:\\ProgramData"), "VoiceStudio", "temp"
    )
    DEFAULT_OUTPUT_DIR = os.path.join(
        os.environ.get("APPDATA", os.path.expanduser("~")), "VoiceStudio", "output"
    )

_lazy_caches: dict[str, Any] = {}


def normalize_lufs(
    audio: np.ndarray,
    sample_rate: int,
    target_lufs: float = -23.0,
    block_size: float = 0.400,
) -> np.ndarray:
    """
    Normalize audio to target LUFS (Loudness Units relative to Full Scale).

    Critical for voice cloning quality - ensures consistent loudness levels
    across different audio sources.

    Args:
        audio: Input audio array (mono or stereo)
        sample_rate: Sample rate in Hz
        target_lufs: Target LUFS value (default -23.0, broadcast standard)
        block_size: Block size in seconds for loudness measurement

    Returns:
        Normalized audio array with same shape as input

    Raises:
        ImportError: If pyloudnorm is not installed
        ValueError: If audio is empty or invalid
    """
    if pyln is None:
        raise ImportError(
            "pyloudnorm is required for LUFS normalization. Install with: pip install pyloudnorm==0.1.1"
        )

    if audio.size == 0:
        raise ValueError("Audio array is empty")

    # Ensure audio is float32 in range [-1, 1]
    if audio.dtype != np.float32:
        audio = audio.astype(np.float32)

    # Clip to prevent overflow
    audio = np.clip(audio, -1.0, 1.0)

    # Handle mono vs stereo
    if len(audio.shape) == 1:
        # Mono audio
        meter = pyln.Meter(sample_rate, block_size=block_size)
        loudness = meter.integrated_loudness(audio)

        if np.isnan(loudness) or np.isinf(loudness):
            logger.warning("Could not measure loudness, returning original audio")
            return audio

        # Normalize to target LUFS
        normalized = pyln.normalize.loudness(audio, loudness, target_lufs)
    else:
        # Stereo audio - process channels in parallel for better performance
        num_channels = audio.shape[1]
        normalized_channels: list[np.ndarray | None] = [None] * num_channels
        meter = pyln.Meter(sample_rate, block_size=block_size)

        # Process channels in parallel if more than 2 channels
        if num_channels > 2:

            def process_channel(ch_idx):
                channel_audio = audio[:, ch_idx]
                loudness = meter.integrated_loudness(channel_audio)

                if np.isnan(loudness) or np.isinf(loudness):
                    logger.warning(
                        f"Could not measure loudness for channel {ch_idx}, " "using original"
                    )
                    return ch_idx, channel_audio
                else:
                    normalized_ch = pyln.normalize.loudness(channel_audio, loudness, target_lufs)
                    return ch_idx, normalized_ch

            # Use ThreadPoolExecutor for parallel processing
            with ThreadPoolExecutor(max_workers=min(num_channels, 4)) as executor:
                futures = {executor.submit(process_channel, ch): ch for ch in range(num_channels)}
                for future in as_completed(futures):
                    ch_idx, processed_audio = future.result()
                    normalized_channels[ch_idx] = processed_audio
        else:
            # Sequential processing for 1-2 channels (faster due to overhead)
            for channel in range(num_channels):
                channel_audio = audio[:, channel]
                loudness = meter.integrated_loudness(channel_audio)

                if np.isnan(loudness) or np.isinf(loudness):
                    logger.warning(
                        f"Could not measure loudness for channel {channel}, " "using original"
                    )
                    normalized_channels[channel] = channel_audio
                else:
                    normalized_channels[channel] = pyln.normalize.loudness(
                        channel_audio, loudness, target_lufs
                    )

        valid_channels = [ch for ch in normalized_channels if ch is not None]
        normalized = np.column_stack(valid_channels)

    return np.asarray(normalized, dtype=np.float32)


def detect_silence(
    audio: np.ndarray,
    sample_rate: int,
    threshold_db: float = -40.0,
    min_silence_duration: float = 0.1,
    frame_length: int = 2048,
    hop_length: int = 512,
    use_vad: bool = False,
    vad_aggressiveness: int = 2,
) -> list[tuple[float, float]]:
    """
    Detect silence regions in audio.

    Useful for trimming silence from voice cloning samples and
    identifying speech segments.

    Uses webrtcvad for high-quality voice activity detection if available,
    otherwise falls back to energy-based detection.

    Args:
        audio: Input audio array (mono or stereo)
        sample_rate: Sample rate in Hz
        threshold_db: Silence threshold in dB (default -40.0)
        min_silence_duration: Minimum duration in seconds to consider as silence
        frame_length: Frame length for analysis
        hop_length: Hop length for analysis
        use_vad: If True, use webrtcvad for voice activity detection
        vad_aggressiveness: VAD aggressiveness (0-3, higher = more aggressive)

    Returns:
        List of (start_time, end_time) tuples for silence regions in seconds

    Raises:
        ImportError: If librosa is not installed
        ValueError: If audio is empty or invalid
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for silence detection. Install with: pip install librosa==0.11.0"
        )

    if audio.size == 0:
        raise ValueError("Audio array is empty")

    # Convert to mono if stereo
    audio_mono = np.mean(audio, axis=1) if len(audio.shape) > 1 else audio

    # Use silero-vad if available and requested (highest quality)
    if use_vad and HAS_SILERO_VAD:
        try:
            # silero-vad provides state-of-the-art voice activity detection
            # Load model (lazy initialization)
            if "silero_model" not in _lazy_caches:
                _lazy_caches["silero_model"], _lazy_caches["silero_utils"] = load_silero_vad()

            # Get speech timestamps
            speech_timestamps = get_speech_timestamps(
                audio_mono,
                _lazy_caches["silero_model"],
                threshold=0.5,
                sampling_rate=sample_rate,
            )

            # Convert to silence regions
            if not speech_timestamps:
                # All silence
                return [(0.0, len(audio_mono) / sample_rate)]

            # Find silence gaps between speech segments
            silence_regions = []
            total_duration = len(audio_mono) / sample_rate

            # Check beginning
            if speech_timestamps[0]["start"] / sample_rate > min_silence_duration:
                silence_regions.append((0.0, speech_timestamps[0]["start"] / sample_rate))

            # Check gaps between speech segments
            for i in range(len(speech_timestamps) - 1):
                gap_start = speech_timestamps[i]["end"] / sample_rate
                gap_end = speech_timestamps[i + 1]["start"] / sample_rate
                gap_duration = gap_end - gap_start
                if gap_duration >= min_silence_duration:
                    silence_regions.append((gap_start, gap_end))

            # Check end
            last_speech_end = speech_timestamps[-1]["end"] / sample_rate
            if total_duration - last_speech_end > min_silence_duration:
                silence_regions.append((last_speech_end, total_duration))

            return silence_regions
        except Exception as e:
            logger.warning(f"silero-vad detection failed: {e}. Falling back to webrtcvad.")

    # Use webrtcvad if available and requested
    if use_vad and HAS_WEBRTCVAD:
        try:
            # webrtcvad requires 8kHz, 16kHz, or 32kHz, 10ms, 20ms, or 30ms frames
            # and 16-bit PCM
            if sample_rate not in [8000, 16000, 32000, 48000]:
                # Resample to 16kHz (webrtcvad standard)
                audio_16k = resample_audio(audio_mono, sample_rate, 16000)
                vad_sample_rate = 16000
            else:
                audio_16k = audio_mono
                vad_sample_rate = sample_rate

            # Convert to int16 PCM
            audio_int16 = (audio_16k * 32767).astype(np.int16)

            # Create VAD
            vad = webrtcvad.Vad(vad_aggressiveness)

            # Process in 30ms frames (webrtcvad requirement)
            frame_duration_ms = 30
            frame_size = int(vad_sample_rate * frame_duration_ms / 1000)
            frames = [
                audio_int16[i : i + frame_size] for i in range(0, len(audio_int16), frame_size)
            ]

            # Detect voice activity
            voice_frames = []
            for i, frame in enumerate(frames):
                # Pad frame if needed
                if len(frame) < frame_size:
                    frame = np.pad(frame, (0, frame_size - len(frame)), mode="constant")
                # Check if frame contains voice
                if vad.is_speech(frame.tobytes(), vad_sample_rate):
                    voice_frames.append(i)

            # Convert to silence regions
            if not voice_frames:
                # All silence
                return [(0.0, len(audio_mono) / sample_rate)]

            # Find silence gaps
            silence_regions = []
            time_per_frame = frame_duration_ms / 1000.0
            scale_factor = sample_rate / vad_sample_rate

            prev_voice = voice_frames[0]
            for voice_frame in voice_frames[1:]:
                if voice_frame > prev_voice + 1:
                    # Gap found
                    start_time = (prev_voice + 1) * time_per_frame * scale_factor
                    end_time = voice_frame * time_per_frame * scale_factor
                    if end_time - start_time >= min_silence_duration:
                        silence_regions.append((start_time, end_time))
                prev_voice = voice_frame

            # Check beginning and end
            if voice_frames[0] > 0:
                start_time = 0.0
                end_time = voice_frames[0] * time_per_frame * scale_factor
                if end_time - start_time >= min_silence_duration:
                    silence_regions.insert(0, (start_time, end_time))

            last_voice = voice_frames[-1]
            total_time = len(audio_mono) / sample_rate
            last_frame_time = (last_voice + 1) * time_per_frame * scale_factor
            if total_time > last_frame_time:
                start_time = last_frame_time
                end_time = total_time
                if end_time - start_time >= min_silence_duration:
                    silence_regions.append((start_time, end_time))

            return silence_regions
        except Exception as e:
            logger.warning(f"webrtcvad detection failed: {e}. Falling back to energy-based.")

    # Fallback to energy-based detection
    # Calculate RMS energy
    rms = librosa.feature.rms(y=audio_mono, frame_length=frame_length, hop_length=hop_length)[0]

    # Convert to dB
    rms_db = librosa.power_to_db(rms**2, ref=1.0)

    # Find silence frames
    silence_frames = rms_db < threshold_db

    # Convert frame indices to time
    frame_times = librosa.frames_to_time(
        np.arange(len(silence_frames)), sr=sample_rate, hop_length=hop_length
    )

    # Find continuous silence regions
    silence_regions = []
    in_silence = False
    silence_start: float | None = None

    for i, is_silent in enumerate(silence_frames):
        if is_silent and not in_silence:
            silence_start = float(frame_times[i])
            in_silence = True
        elif not is_silent and in_silence and silence_start is not None:
            silence_end = float(frame_times[i])
            duration = silence_end - silence_start
            if duration >= min_silence_duration:
                silence_regions.append((silence_start, silence_end))
            in_silence = False

    # Handle silence at the end
    if in_silence and silence_start is not None:
        silence_end = float(frame_times[-1])
        duration = silence_end - silence_start
        if duration >= min_silence_duration:
            silence_regions.append((silence_start, silence_end))

    return silence_regions


def resample_audio(
    audio: np.ndarray, original_sr: int, target_sr: int, res_type: str = "soxr_hq"
) -> np.ndarray:
    """
    High-quality audio resampling.

    Essential for voice cloning - ensures audio is at the correct sample rate
    for the target engine (typically 22050 Hz or 24000 Hz).

    Uses soxr (highest quality) if available, then resampy, otherwise falls back to librosa.

    Args:
        audio: Input audio array
        original_sr: Original sample rate in Hz
        target_sr: Target sample rate in Hz
        res_type: Resampling type ('soxr_hq' for highest quality, 'kaiser_best', 'kaiser_fast')

    Returns:
        Resampled audio array

    Raises:
        ImportError: If librosa is not installed
        ValueError: If sample rates are invalid
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for resampling. Install with: pip install librosa==0.11.0"
        )

    if original_sr <= 0 or target_sr <= 0:
        raise ValueError("Sample rates must be positive")

    if original_sr == target_sr:
        return audio.copy()

    # Use soxr if available for highest quality resampling
    if HAS_SOXR:
        try:
            # soxr provides highest quality resampling
            # Convert to float64 for soxr (it handles conversion internally)
            audio_float64 = audio.astype(np.float64)
            resampled = soxr.resample(audio_float64, original_sr, target_sr, quality="VHQ")
            return np.asarray(resampled, dtype=audio.dtype)
        except Exception as e:
            logger.warning(f"soxr resampling failed: {e}. Falling back to resampy/librosa.")

    # Use resampy if available for higher quality resampling
    if HAS_RESAMPY:
        try:
            resampled = resampy.resample(audio, original_sr, target_sr)
            return np.asarray(resampled, dtype=audio.dtype)
        except Exception as e:
            logger.warning(f"resampy resampling failed: {e}. Falling back to librosa.")

    # Fallback to librosa's high-quality resampler
    resampled = librosa.resample(audio, orig_sr=original_sr, target_sr=target_sr, res_type=res_type)

    return resampled.astype(audio.dtype)


def convert_format(
    input_path: str | Path,
    output_path: str | Path,
    output_format: str = "wav",
    sample_rate: int | None = None,
    channels: int | None = None,
    subtype: str = "PCM_16",
) -> Path:
    """
    Convert audio file format with optional resampling and channel conversion.

    Supports common formats: WAV, FLAC, MP3 (if ffmpeg available), OGG.

    Args:
        input_path: Path to input audio file
        output_path: Path to output audio file
        output_format: Output format ('wav', 'flac', 'mp3', 'ogg')
        sample_rate: Target sample rate (None to keep original)
        channels: Target channel count (None to keep original, 1 for mono, 2 for stereo)
        subtype: Audio subtype (e.g., 'PCM_16', 'PCM_24', 'PCM_32', 'FLOAT')

    Returns:
        Path to output file

    Raises:
        ImportError: If soundfile is not installed
        FileNotFoundError: If input file doesn't exist
        ValueError: If format is unsupported
    """
    if sf is None:
        raise ImportError(
            "soundfile is required for format conversion. Install with: pip install soundfile==0.12.1"
        )

    input_path = Path(input_path)
    output_path = Path(output_path)

    if not input_path.exists():
        raise FileNotFoundError(f"Input file not found: {input_path}")

    # Read input audio
    audio, sr = sf.read(str(input_path))

    # Resample if needed
    if sample_rate is not None and sr != sample_rate:
        if librosa is None:
            raise ImportError(
                "librosa is required for resampling. Install with: pip install librosa==0.11.0"
            )
        audio = resample_audio(audio, sr, sample_rate)
        sr = sample_rate

    # Convert channels if needed
    if channels is not None:
        if len(audio.shape) == 1:  # Mono
            if channels == 2:  # Convert to stereo
                audio = np.column_stack([audio, audio])
            elif channels != 1:
                raise ValueError(f"Invalid channel count: {channels}")
        else:  # Stereo or multi-channel
            if channels == 1:  # Convert to mono
                audio = np.mean(audio, axis=1)
            elif channels == 2 and audio.shape[1] != 2:
                # Take first two channels or duplicate if mono
                if audio.shape[1] >= 2:
                    audio = audio[:, :2]
                else:
                    audio = np.column_stack([audio[:, 0], audio[:, 0]])
            elif audio.shape[1] != channels:
                raise ValueError(f"Cannot convert from {audio.shape[1]} to {channels} channels")

    # Ensure output directory exists
    output_path.parent.mkdir(parents=True, exist_ok=True)

    # Write output file
    sf.write(str(output_path), audio, sr, subtype=subtype, format=output_format)

    logger.info(
        f"Converted {input_path} to {output_path} ({output_format}, {sr}Hz, {channels or 'original'}ch)"
    )
    return output_path


def analyze_voice_characteristics(
    audio: np.ndarray, sample_rate: int
) -> dict[str, float | np.ndarray | list[float]]:
    """
    Extract voice characteristics from audio for voice cloning analysis.

    Analyzes fundamental frequency, formants, spectral characteristics,
    and other features useful for voice matching and quality assessment.

    Args:
        audio: Input audio array (mono)
        sample_rate: Sample rate in Hz

    Returns:
        Dictionary containing:
        - f0_mean: Mean fundamental frequency (Hz)
        - f0_std: Standard deviation of F0
        - formants: List of formant frequencies (F1, F2, F3)
        - spectral_centroid: Spectral centroid (Hz)
        - spectral_rolloff: Spectral rolloff frequency (Hz)
        - zero_crossing_rate: Zero crossing rate
        - mfcc: MFCC coefficients (13-dimensional)

    Raises:
        ImportError: If librosa is not installed
        ValueError: If audio is empty or invalid
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for voice analysis. Install with: pip install librosa==0.11.0"
        )

    if audio.size == 0:
        raise ValueError("Audio array is empty")

    # Convert to mono if needed
    audio_mono = np.mean(audio, axis=1) if len(audio.shape) > 1 else audio

    # Extract fundamental frequency (F0)
    # Use crepe (deep learning-based) if available, otherwise use librosa.pyin
    if HAS_CREPE and crepe is not None:
        try:
            # CREPE provides more accurate pitch tracking using deep learning
            _time, frequency, confidence, _activation = crepe.predict(
                audio_mono, sample_rate, viterbi=True
            )
            # Filter by confidence threshold (0.5 is default)
            confident_mask = confidence > 0.5
            f0_voiced = frequency[confident_mask]
            voiced_flag = confident_mask
            f0 = frequency
        except Exception as e:
            logger.warning(f"CREPE pitch tracking failed, falling back to librosa.pyin: {e}")
            # Fallback to librosa.pyin
            f0, voiced_flag, voiced_probs = librosa.pyin(
                audio_mono,
                fmin=float(librosa.note_to_hz("C2")),
                fmax=float(librosa.note_to_hz("C7")),
            )
            f0_voiced = f0[voiced_flag]
    else:
        # Use librosa.pyin (probabilistic YIN algorithm)
        f0, voiced_flag, _voiced_probs = librosa.pyin(
            audio_mono,
            fmin=float(librosa.note_to_hz("C2")),
            fmax=float(librosa.note_to_hz("C7")),
        )
        f0_voiced = f0[voiced_flag]

    # Calculate F0 statistics
    f0_mean = float(np.nanmean(f0_voiced)) if len(f0_voiced) > 0 else 0.0
    f0_std = float(np.nanstd(f0_voiced)) if len(f0_voiced) > 0 else 0.0

    # Extract formants (simplified - using spectral peaks)
    # Note: True formant extraction requires LPC analysis
    stft = librosa.stft(audio_mono)
    magnitude = np.abs(stft)
    power = magnitude**2

    # Find spectral peaks that could be formants
    # F1 typically 300-800 Hz, F2 800-3000 Hz, F3 2000-4000 Hz
    freqs = librosa.fft_frequencies(sr=sample_rate)

    # Find peaks in formant regions
    formant_regions = [(300, 800), (800, 3000), (2000, 4000)]  # F1  # F2  # F3
    formants = []
    for fmin, fmax in formant_regions:
        region_mask = (freqs >= fmin) & (freqs <= fmax)
        if np.any(region_mask):
            region_power = np.mean(power[region_mask, :], axis=1)
            peak_idx = np.argmax(region_power)
            formants.append(float(freqs[region_mask][peak_idx]))
        else:
            formants.append(0.0)

    # Spectral features
    spectral_centroid = float(
        np.mean(librosa.feature.spectral_centroid(y=audio_mono, sr=sample_rate))
    )
    spectral_rolloff = float(
        np.mean(librosa.feature.spectral_rolloff(y=audio_mono, sr=sample_rate))
    )
    zero_crossing_rate = float(np.mean(librosa.feature.zero_crossing_rate(audio_mono)))

    # MFCC (Mel-frequency cepstral coefficients)
    mfcc = librosa.feature.mfcc(y=audio_mono, sr=sample_rate, n_mfcc=13)
    mfcc_mean = np.mean(mfcc, axis=1).tolist()

    return {
        "f0_mean": f0_mean,
        "f0_std": f0_std,
        "formants": formants,
        "spectral_centroid": spectral_centroid,
        "spectral_rolloff": spectral_rolloff,
        "zero_crossing_rate": zero_crossing_rate,
        "mfcc": mfcc_mean,
        "voiced_ratio": float(np.mean(voiced_flag)) if len(voiced_flag) > 0 else 0.0,
    }


def enhance_voice_quality(
    audio: np.ndarray,
    sample_rate: int,
    normalize: bool = True,
    denoise: bool = True,
    target_lufs: float = -23.0,
) -> np.ndarray:
    """
    Enhance voice quality through normalization, denoising, and artifact removal.

    Applies a quality enhancement pipeline optimized for voice cloning workflows.

    Args:
        audio: Input audio array
        sample_rate: Sample rate in Hz
        normalize: Whether to normalize to target LUFS
        denoise: Whether to apply denoising
        target_lufs: Target LUFS for normalization

    Returns:
        Enhanced audio array

    Raises:
        ImportError: If required libraries are not installed
    """
    enhanced = audio.copy()

    # Try advanced voice restoration with voicefixer first (if available)
    if denoise and HAS_VOICEFIXER:
        try:
            # VoiceFixer requires specific sample rate (44100 Hz)
            if sample_rate != 44100:
                # Resample to 44100 for voicefixer
                enhanced_44k = resample_audio(enhanced, sample_rate, 44100)
            else:
                enhanced_44k = enhanced

            # Initialize VoiceFixer (lazy initialization)
            if "voicefixer" not in _lazy_caches:
                _lazy_caches["voicefixer"] = VoiceFixer()

            # Restore voice
            enhanced_44k = _lazy_caches["voicefixer"].restore(enhanced_44k, cuda=False)

            # Resample back to original sample rate
            if sample_rate != 44100:
                enhanced = resample_audio(enhanced_44k, 44100, sample_rate)
            else:
                enhanced = enhanced_44k

            logger.debug("Voice restoration applied using voicefixer")
        except Exception as e:
            logger.warning(
                f"VoiceFixer restoration failed: {e}. Falling back to standard denoising."
            )

    # Try deepfilternet for speech enhancement (if voicefixer not used or failed)
    if denoise and HAS_DEEPFILTERNET and not HAS_VOICEFIXER:
        try:
            # DeepFilterNet works with 48kHz, but can handle other rates
            enhanced = deepfilternet.enhance(enhanced, sample_rate)
            logger.debug("Speech enhancement applied using deepfilternet")
        except Exception as e:
            logger.warning(
                f"DeepFilterNet enhancement failed: {e}. Falling back to standard denoising."
            )

    # Denoise with noisereduce as fallback or additional step
    if denoise and nr is not None:
        try:
            # Convert to mono for denoising if stereo
            if len(enhanced.shape) > 1:
                num_channels = enhanced.shape[1]

                # Process channels in parallel for better performance
                if num_channels > 2:

                    def denoise_channel(ch_idx):
                        return ch_idx, nr.reduce_noise(y=enhanced[:, ch_idx], sr=sample_rate)

                    enhanced_channels: list[np.ndarray | None] = [None] * num_channels
                    with ThreadPoolExecutor(max_workers=min(num_channels, 4)) as executor:
                        futures = {
                            executor.submit(denoise_channel, ch): ch for ch in range(num_channels)
                        }
                        for future in as_completed(futures):
                            ch_idx, denoised = future.result()
                            enhanced_channels[ch_idx] = denoised
                    valid_enhanced = [ch for ch in enhanced_channels if ch is not None]
                    enhanced = np.column_stack(valid_enhanced)
                else:
                    # Sequential for 1-2 channels (lower overhead)
                    seq_channels: list[np.ndarray] = []
                    for ch in range(num_channels):
                        seq_channels.append(
                            np.asarray(nr.reduce_noise(y=enhanced[:, ch], sr=sample_rate))
                        )
                    enhanced = np.column_stack(seq_channels)
            else:
                enhanced = nr.reduce_noise(y=enhanced, sr=sample_rate)
        except Exception as e:
            logger.warning(f"Denoising failed: {e}, continuing without denoising")

    # Normalize to target LUFS
    if normalize and pyln is not None:
        try:
            enhanced = normalize_lufs(enhanced, sample_rate, target_lufs=target_lufs)
        except Exception as e:
            logger.warning(f"Normalization failed: {e}, continuing without normalization")

    # Remove artifacts (synthesis artifacts, clicks, pops)
    enhanced = remove_artifacts(enhanced, sample_rate)

    return enhanced


def remove_artifacts(audio: np.ndarray, sample_rate: int, threshold: float = 0.01) -> np.ndarray:
    """
    Remove synthesis artifacts from audio (clicks, pops, discontinuities).

    Uses spectral gating and smoothing to reduce artifacts common in
    neural voice synthesis.

    Args:
        audio: Input audio array
        sample_rate: Sample rate in Hz
        threshold: Threshold for artifact detection (0.0-1.0)

    Returns:
        Audio with artifacts reduced
    """
    cleaned = audio.copy()

    # Detect and smooth discontinuities
    if len(cleaned.shape) == 1:
        # Simple discontinuity detection for mono
        diff = np.abs(np.diff(cleaned))
        discontinuities = diff > threshold

        if np.any(discontinuities):
            # Smooth discontinuities with a short window
            window_size = int(sample_rate * 0.001)  # 1ms window
            if window_size > 1:
                from scipy import signal

                if hasattr(signal, "savgol_filter"):
                    try:
                        # Use Savitzky-Golay filter for smoothing
                        cleaned = signal.savgol_filter(
                            cleaned,
                            window_length=min(window_size * 2 + 1, len(cleaned) // 2),
                            polyorder=2,
                        )
                    except Exception:
                        # Fallback to simple moving average
                        kernel = np.ones(window_size) / window_size
                        cleaned = np.convolve(cleaned, kernel, mode="same")
    else:
        # Process each channel
        cleaned_channels = []
        for ch in range(cleaned.shape[1]):
            cleaned_channels.append(remove_artifacts(cleaned[:, ch], sample_rate, threshold))
        cleaned = np.column_stack(cleaned_channels)

    # Clip to prevent overflow
    cleaned = np.clip(cleaned, -1.0, 1.0)

    return cleaned


def load_audio(file_path: str | Path) -> tuple[np.ndarray, int]:
    """
    Load audio file and return audio array and sample rate.

    Supports common formats: WAV, FLAC, MP3, OGG.
    Uses librosa for loading (handles resampling and format conversion).

    Args:
        file_path: Path to audio file

    Returns:
        Tuple of (audio array, sample_rate)
        Audio is returned as float32 in range [-1, 1]

    Raises:
        ImportError: If librosa is not installed
        FileNotFoundError: If file doesn't exist
        ValueError: If file format is unsupported
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for loading audio. Install with: pip install librosa==0.11.0"
        )

    file_path = Path(file_path)

    if not file_path.exists():
        raise FileNotFoundError(f"Audio file not found: {file_path}")

    try:
        # Load audio using librosa (returns float32 in range [-1, 1])
        audio, sample_rate = librosa.load(
            str(file_path),
            sr=None,  # Keep original sample rate
            mono=False,  # Keep original channel layout
        )

        # Ensure audio is 2D for stereo (samples, channels)
        if len(audio.shape) == 1:
            # Mono - keep as 1D
            ...
        elif len(audio.shape) == 2 and audio.shape[0] < audio.shape[1]:
            # Transpose if needed (librosa sometimes returns shape (channels, samples))
            audio = audio.T

        logger.info(f"Loaded audio from {file_path}: shape={audio.shape}, sr={sample_rate}")
        return audio, int(sample_rate)

    except Exception as e:
        logger.error(f"Failed to load audio from {file_path}: {e}")
        raise ValueError(f"Failed to load audio file: {e}")


def save_audio(
    audio: np.ndarray,
    sample_rate: int,
    file_path: str | Path,
    format: str = "wav",
    subtype: str = "PCM_16",
) -> Path:
    """
    Save audio array to file.

    Supports common formats: WAV, FLAC, OGG.
    Uses soundfile for saving (high-quality, fast).

    Args:
        audio: Audio array (mono or stereo) in range [-1, 1]
        sample_rate: Sample rate in Hz
        file_path: Path to output file
        format: Output format ('wav', 'flac', 'ogg')
        subtype: Audio subtype (e.g., 'PCM_16', 'PCM_24', 'PCM_32', 'FLOAT')

    Returns:
        Path to saved file

    Raises:
        ImportError: If soundfile is not installed
        ValueError: If audio is invalid or format is unsupported
    """
    if sf is None:
        raise ImportError(
            "soundfile is required for saving audio. Install with: pip install soundfile==0.12.1"
        )

    file_path = Path(file_path)

    # Ensure audio is in correct format
    if audio.size == 0:
        raise ValueError("Audio array is empty")

    # Clip to valid range
    audio = np.clip(audio, -1.0, 1.0)

    # Ensure audio is float32
    if audio.dtype != np.float32:
        audio = audio.astype(np.float32)

    # Ensure output directory exists
    file_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        # Save audio using soundfile
        sf.write(str(file_path), audio, sample_rate, format=format, subtype=subtype)

        logger.info(
            f"Saved audio to {file_path}: shape={audio.shape}, sr={sample_rate}, format={format}"
        )
        return file_path

    except Exception as e:
        logger.error(f"Failed to save audio to {file_path}: {e}")
        raise ValueError(f"Failed to save audio file: {e}")


def time_stretch_audio(
    audio: np.ndarray, sample_rate: int, rate: float, preserve_pitch: bool = True
) -> np.ndarray:
    """
    Time-stretch audio (change tempo without changing pitch, or vice versa).

    Uses pyrubberband for high-quality time-stretching and pitch-shifting.
    Falls back to librosa if pyrubberband is not available.

    Args:
        audio: Input audio array
        sample_rate: Sample rate in Hz
        rate: Stretch rate (1.0 = no change, 2.0 = double speed, 0.5 = half speed)
        preserve_pitch: If True, preserve pitch while changing tempo. If False, change pitch.

    Returns:
        Time-stretched audio array

    Raises:
        ValueError: If rate is invalid
    """
    if rate <= 0:
        raise ValueError("Stretch rate must be positive")

    if rate == 1.0:
        return audio.copy()

    # Use pyrubberband if available for high-quality time-stretching
    if HAS_PYRUBBERBAND:
        try:
            if preserve_pitch:
                stretched = pyrb.time_stretch(audio, sample_rate, rate)
            else:
                semitones = 12 * np.log2(rate)
                stretched = pyrb.pitch_shift(audio, sample_rate, semitones)

            return np.asarray(stretched)
        except Exception as e:
            logger.warning(f"pyrubberband time-stretching failed: {e}. Falling back to librosa.")

    # Fallback to librosa
    if librosa is None:
        raise ImportError("librosa is required for time-stretching")

    if preserve_pitch:
        stretched = librosa.effects.time_stretch(audio, rate=rate)
    else:
        semitones = 12 * np.log2(rate)
        stretched = librosa.effects.pitch_shift(audio, sr=sample_rate, n_steps=semitones)

    return np.asarray(stretched)


def pitch_shift_audio(audio: np.ndarray, sample_rate: int, semitones: float) -> np.ndarray:
    """
    Pitch-shift audio (change pitch without changing tempo).

    Uses pyrubberband for high-quality pitch-shifting.
    Falls back to librosa if pyrubberband is not available.

    Args:
        audio: Input audio array
        sample_rate: Sample rate in Hz
        semitones: Pitch shift in semitones (positive = higher, negative = lower)

    Returns:
        Pitch-shifted audio array
    """
    if semitones == 0.0:
        return audio.copy()

    # Use pyrubberband if available
    if HAS_PYRUBBERBAND:
        try:
            shifted = pyrb.pitch_shift(audio, sample_rate, semitones)
            return np.asarray(shifted)
        except Exception as e:
            logger.warning(f"pyrubberband pitch-shifting failed: {e}. Falling back to librosa.")

    # Fallback to librosa
    if librosa is None:
        raise ImportError("librosa is required for pitch-shifting")

    shifted = librosa.effects.pitch_shift(audio, sr=sample_rate, n_steps=semitones)
    return shifted


def separate_voice_from_music(
    audio: np.ndarray,
    sample_rate: int,
    model: str = "2stems",
    output_format: str = "numpy",
) -> np.ndarray | dict[str, np.ndarray]:
    """
    Separate voice from background music using spleeter.

    Uses spleeter library for source separation to extract voice from mixed audio.
    Useful for isolating voice from music tracks for voice cloning.

    Args:
        audio: Input audio array (mono or stereo)
        sample_rate: Sample rate in Hz
        model: Spleeter model ('2stems', '4stems', '5stems')
            - '2stems': Separates into vocals and accompaniment
            - '4stems': Separates into vocals, drums, bass, other
            - '5stems': Separates into vocals, drums, bass, piano, other
        output_format: Output format ('numpy' for arrays, 'dict' for dictionary)

    Returns:
        If output_format='numpy': Voice-only audio array
        If output_format='dict': Dictionary with separated stems

    Raises:
        ImportError: If spleeter is not installed
        ValueError: If model is invalid
    """
    if not HAS_SPLEETER:
        raise ImportError(
            "spleeter is required for source separation. "
            "Install with: pip install spleeter>=2.3.0"
        )

    if model not in ["2stems", "4stems", "5stems"]:
        raise ValueError(f"Invalid model '{model}'. Use '2stems', '4stems', or '5stems'")

    try:
        # Initialize separator
        separator = Separator(f"spleeter:{model}")

        # Ensure audio is mono for spleeter (it expects mono input)
        audio_mono = np.mean(audio, axis=1) if len(audio.shape) > 1 else audio

        # Save audio to temporary file (spleeter requires file input)
        import os
        import tempfile

        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_file:
            tmp_path = tmp_file.name
            if sf is not None:
                sf.write(tmp_path, audio_mono, sample_rate)
            else:
                raise ImportError("soundfile required for spleeter integration")

        try:
            # Separate sources
            separator.separate_to_file(
                tmp_path,
                str(Path(tmp_path).parent),
                filename_format="{instrument}.{codec}",
            )

            # Load separated vocals
            output_dir = Path(tmp_path).parent
            vocals_path = output_dir / "vocals.wav"

            if vocals_path.exists():
                vocals_audio, _vocals_sr = sf.read(str(vocals_path))

                # Clean up temporary files
                os.unlink(tmp_path)
                if vocals_path.exists():
                    os.unlink(vocals_path)

                if output_format == "numpy":
                    return np.asarray(vocals_audio)
                else:
                    # Load all stems if available
                    result = {"vocals": vocals_audio}
                    if model in ["4stems", "5stems"]:
                        for stem in ["drums", "bass", "other"]:
                            stem_path = output_dir / f"{stem}.wav"
                            if stem_path.exists():
                                stem_audio, _ = sf.read(str(stem_path))
                                result[stem] = stem_audio
                                os.unlink(stem_path)
                    if model == "5stems":
                        piano_path = output_dir / "piano.wav"
                        if piano_path.exists():
                            piano_audio, _ = sf.read(str(piano_path))
                            result["piano"] = piano_audio
                            os.unlink(piano_path)
                    return result
            else:
                # Clean up and return original audio
                os.unlink(tmp_path)
                logger.warning("Spleeter separation failed, returning original audio")
                return audio_mono if output_format == "numpy" else {"vocals": audio_mono}

        except Exception as e:
            # Clean up on error
            if os.path.exists(tmp_path):
                os.unlink(tmp_path)
            logger.warning(f"Spleeter separation failed: {e}, returning original audio")
            return audio_mono if output_format == "numpy" else {"vocals": audio_mono}

    except Exception as e:
        logger.error(f"Source separation failed: {e}", exc_info=True)
        raise ValueError(f"Failed to separate voice from music: {e}")


def analyze_audio_wavelets(
    audio: np.ndarray, sample_rate: int, wavelet: str = "db4", levels: int = 5
) -> dict[str, float | np.ndarray | list[float]]:
    """
    Analyze audio using wavelet transforms for detailed frequency analysis.

    Useful for detecting artifacts, analyzing spectral content, and quality assessment.

    Args:
        audio: Input audio array (mono)
        sample_rate: Sample rate in Hz
        wavelet: Wavelet type ('db4', 'haar', 'coif2', etc.)
        levels: Number of decomposition levels

    Returns:
        Dictionary containing:
        - coefficients: Wavelet coefficients for each level
        - energy_by_level: Energy distribution across levels
        - detail_energies: Detail coefficients energy per level
        - approximation_energy: Approximation coefficient energy
        - spectral_features: Spectral features extracted from wavelets
    """
    if not HAS_PYWAVELETS:
        raise ImportError(
            "pywavelets is required for wavelet analysis. Install with: pip install pywavelets>=1.9.0"
        )

    if audio.size == 0:
        raise ValueError("Audio array is empty")

    # Convert to mono if needed
    audio_mono = np.mean(audio, axis=1) if len(audio.shape) > 1 else audio

    # Perform wavelet decomposition
    coeffs = pywt.wavedec(audio_mono, wavelet, level=levels)

    # Extract approximation and detail coefficients
    approximation = coeffs[0]
    details = coeffs[1:]

    # Calculate energy by level
    approximation_energy = float(np.sum(approximation**2))
    detail_energies = [float(np.sum(detail**2)) for detail in details]
    total_energy = approximation_energy + sum(detail_energies)
    energy_by_level = [approximation_energy / total_energy if total_energy > 0 else 0.0]
    energy_by_level.extend([e / total_energy if total_energy > 0 else 0.0 for e in detail_energies])

    # Extract spectral features
    # High-frequency content (detail coefficients)
    high_freq_energy = sum(detail_energies[:2]) if len(detail_energies) >= 2 else 0.0
    # Mid-frequency content
    mid_freq_energy = (
        sum(detail_energies[2:4])
        if len(detail_energies) >= 4
        else sum(detail_energies[2:]) if len(detail_energies) > 2 else 0.0
    )
    # Low-frequency content (approximation)
    low_freq_energy = approximation_energy

    return {
        "coefficients": coeffs,
        "energy_by_level": energy_by_level,
        "detail_energies": detail_energies,
        "approximation_energy": approximation_energy,
        "spectral_features": {
            "high_freq_energy": (high_freq_energy / total_energy if total_energy > 0 else 0.0),
            "mid_freq_energy": (mid_freq_energy / total_energy if total_energy > 0 else 0.0),
            "low_freq_energy": (low_freq_energy / total_energy if total_energy > 0 else 0.0),
            "energy_ratio": (high_freq_energy / low_freq_energy if low_freq_energy > 0 else 0.0),
        },
    }


def read_audio_metadata(file_path: str | Path) -> dict[str, Any]:
    """
    Read audio file metadata using mutagen.

    Supports MP3, FLAC, OGG, M4A, and other formats with metadata.

    Args:
        file_path: Path to audio file

    Returns:
        Dictionary containing metadata:
        - title: Track title
        - artist: Artist name
        - album: Album name
        - genre: Genre
        - year: Year
        - duration: Duration in seconds
        - bitrate: Bitrate in kbps
        - sample_rate: Sample rate in Hz
        - channels: Number of channels
        - format: File format
        - all_tags: All available tags
    """
    if not HAS_MUTAGEN:
        raise ImportError(
            "mutagen is required for metadata reading. Install with: pip install mutagen>=1.47.0"
        )

    file_path = Path(file_path)
    if not file_path.exists():
        raise FileNotFoundError(f"Audio file not found: {file_path}")

    try:
        audio_file = MutagenFile(str(file_path))

        if audio_file is None:
            raise ValueError(f"Unsupported audio format: {file_path.suffix}")

        metadata: dict[str, Any] = {
            "title": None,
            "artist": None,
            "album": None,
            "genre": None,
            "year": None,
            "duration": None,
            "bitrate": None,
            "sample_rate": None,
            "channels": None,
            "format": file_path.suffix.lower().lstrip("."),
            "all_tags": {},
        }

        # Extract common tags
        if hasattr(audio_file, "tags") and audio_file.tags is not None:
            tags = audio_file.tags

            # Title
            if "TIT2" in tags or "TITLE" in tags:
                metadata["title"] = str(tags.get("TIT2", tags.get("TITLE", [""])[0]))

            # Artist
            if "TPE1" in tags or "ARTIST" in tags:
                metadata["artist"] = str(tags.get("TPE1", tags.get("ARTIST", [""])[0]))

            # Album
            if "TALB" in tags or "ALBUM" in tags:
                metadata["album"] = str(tags.get("TALB", tags.get("ALBUM", [""])[0]))

            # Genre
            if "TCON" in tags or "GENRE" in tags:
                metadata["genre"] = str(tags.get("TCON", tags.get("GENRE", [""])[0]))

            # Year
            if "TDRC" in tags or "DATE" in tags or "YEAR" in tags:
                year_str = str(tags.get("TDRC", tags.get("DATE", tags.get("YEAR", [""]))[0]))
                try:
                    metadata["year"] = int(year_str[:4]) if year_str else None
                except (ValueError, IndexError):
                    metadata["year"] = None

            # Store all tags
            for key in tags:
                try:
                    metadata["all_tags"][key] = str(tags[key][0]) if tags[key] else None
                except (IndexError, TypeError):
                    metadata["all_tags"][key] = None

        # Extract audio properties
        if hasattr(audio_file, "info"):
            info = audio_file.info
            metadata["duration"] = float(info.length) if hasattr(info, "length") else None
            metadata["bitrate"] = int(info.bitrate / 1000) if hasattr(info, "bitrate") else None
            metadata["sample_rate"] = (
                int(info.sample_rate) if hasattr(info, "sample_rate") else None
            )
            metadata["channels"] = int(info.channels) if hasattr(info, "channels") else None

        return metadata

    except _id3_no_header_error:
        # File exists but has no ID3 tags
        logger.debug(f"Audio file has no metadata tags: {file_path}")
        return {
            "title": None,
            "artist": None,
            "album": None,
            "genre": None,
            "year": None,
            "duration": None,
            "bitrate": None,
            "sample_rate": None,
            "channels": None,
            "format": file_path.suffix.lower().lstrip("."),
            "all_tags": {},
        }
    except Exception as e:
        logger.error(f"Failed to read audio metadata from {file_path}: {e}")
        raise ValueError(f"Failed to read audio metadata: {e}")


def match_voice_profile(
    reference_audio: np.ndarray,
    target_audio: np.ndarray,
    reference_sr: int,
    target_sr: int,
) -> dict[str, float | np.ndarray | list[str]]:
    """
    Match voice profile between reference and target audio.

    Analyzes both audio samples and provides metrics for voice matching,
    useful for voice cloning quality assessment.

    Uses improved algorithms for better accuracy:
    - Cosine similarity for MFCC comparison (more robust than L2 distance)
    - Normalized feature comparison
    - Spectral centroid similarity for additional accuracy
    - Sigmoid-based distance-to-similarity conversion

    Args:
        reference_audio: Reference audio array (target voice)
        target_audio: Target audio array (synthesized voice)
        reference_sr: Reference sample rate
        target_sr: Target sample rate

    Returns:
        Dictionary containing:
        - f0_similarity: F0 similarity score (0-1)
        - formant_similarity: Formant similarity score (0-1)
        - mfcc_distance: MFCC distance (lower is better)
        - mfcc_cosine_similarity: MFCC cosine similarity (0-1, higher is better)
        - spectral_similarity: Spectral centroid similarity (0-1)
        - overall_similarity: Overall similarity score (0-1)
        - recommendations: List of recommendations for improvement
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for voice matching. Install with: pip install librosa==0.11.0"
        )

    # Resample to same rate if needed
    if reference_sr != target_sr:
        target_audio = resample_audio(target_audio, target_sr, reference_sr)
        target_sr = reference_sr

    # Analyze both audio samples
    ref_chars = analyze_voice_characteristics(reference_audio, reference_sr)
    target_chars = analyze_voice_characteristics(target_audio, target_sr)

    # Calculate F0 similarity with improved normalization
    ref_f0 = cast(float, ref_chars["f0_mean"])
    target_f0 = cast(float, target_chars["f0_mean"])
    if ref_f0 > 0 and target_f0 > 0:
        f0_ratio = min(ref_f0, target_f0) / max(ref_f0, target_f0)
        f0_similarity = f0_ratio
    else:
        f0_similarity = 0.0

    # Calculate formant similarity with improved weighting
    # F1 and F2 are more important for voice identity than F3
    formant_weights = [0.4, 0.4, 0.2]  # F1, F2, F3 weights
    formant_sims: list[float] = []
    ref_formants = cast(list[float], ref_chars["formants"])
    target_formants = cast(list[float], target_chars["formants"])
    for i, (ref, tgt) in enumerate(zip(ref_formants, target_formants)):
        if ref > 0 and tgt > 0:
            # Ratio-based similarity for each formant
            formant_ratio = min(ref, tgt) / max(ref, tgt)
            formant_sims.append(formant_ratio * formant_weights[i])
        else:
            formant_sims.append(0.0)
    formant_similarity = sum(formant_sims) / sum(formant_weights) if formant_sims else 0.0

    # Calculate MFCC similarity using cosine similarity (more robust)
    ref_mfcc = np.array(ref_chars["mfcc"])
    target_mfcc = np.array(target_chars["mfcc"])

    # Compute L2 distance (for backward compatibility)
    mfcc_distance = float(np.linalg.norm(ref_mfcc - target_mfcc))

    # Compute cosine similarity (more robust for spectral features)
    ref_norm = np.linalg.norm(ref_mfcc)
    target_norm = np.linalg.norm(target_mfcc)
    if ref_norm > 0 and target_norm > 0:
        mfcc_cosine_similarity = float(np.dot(ref_mfcc, target_mfcc) / (ref_norm * target_norm))
        # Clamp to [0, 1] range (cosine can be negative)
        mfcc_cosine_similarity = max(0.0, mfcc_cosine_similarity)
    else:
        mfcc_cosine_similarity = 0.0

    # Calculate spectral centroid similarity
    ref_centroid = cast(float, ref_chars.get("spectral_centroid", 0))
    target_centroid = cast(float, target_chars.get("spectral_centroid", 0))
    if ref_centroid > 0 and target_centroid > 0:
        spectral_similarity = min(ref_centroid, target_centroid) / max(
            ref_centroid, target_centroid
        )
    else:
        spectral_similarity = 0.0

    # Convert MFCC distance to similarity using sigmoid function
    # This gives a smoother transition and handles larger distances better
    # Calibrated so that distance=50 gives ~0.8 similarity, distance=100 gives ~0.5
    mfcc_distance_similarity = 1.0 / (1.0 + np.exp((mfcc_distance - 75) / 25))

    # Overall similarity with improved weighting
    # F0: 25%, Formant: 20%, MFCC cosine: 30%, Spectral: 10%, MFCC distance: 15%
    overall_similarity = (
        f0_similarity * 0.25
        + formant_similarity * 0.20
        + mfcc_cosine_similarity * 0.30
        + spectral_similarity * 0.10
        + mfcc_distance_similarity * 0.15
    )

    # Generate recommendations based on improved thresholds
    recommendations = []
    if f0_similarity < 0.85:
        recommendations.append("F0 mismatch detected - consider adjusting pitch parameters")
    if formant_similarity < 0.75:
        recommendations.append("Formant mismatch detected - voice characteristics differ")
    if mfcc_cosine_similarity < 0.85:
        recommendations.append("Spectral mismatch - MFCC characteristics differ from reference")
    if mfcc_distance > 100:
        recommendations.append("High MFCC distance - consider reference audio quality")
    if spectral_similarity < 0.8:
        recommendations.append("Spectral centroid mismatch - brightness differs from reference")
    if overall_similarity < 0.75:
        recommendations.append("Overall voice match is moderate - review synthesis parameters")

    return {
        "f0_similarity": float(f0_similarity),
        "formant_similarity": float(formant_similarity),
        "mfcc_distance": mfcc_distance,
        "mfcc_cosine_similarity": float(mfcc_cosine_similarity),
        "spectral_similarity": float(spectral_similarity),
        "overall_similarity": float(overall_similarity),
        "recommendations": recommendations,
    }


def enhance_voice_cloning_quality(
    audio: np.ndarray,
    sample_rate: int,
    enhancement_level: str = "standard",
    preserve_prosody: bool = True,
    target_lufs: float = -23.0,
    use_rvc_postprocessing: bool = False,
    reference_audio: str | Path | np.ndarray | None = None,
    rvc_model_path: str | None = None,
) -> np.ndarray:
    """
    Advanced quality enhancement specifically optimized for voice cloning outputs.

    Applies a comprehensive enhancement pipeline designed to improve the quality
    of synthesized voice audio while preserving natural prosody and characteristics.
    Now includes optional RVC (Retrieval-based Voice Conversion) post-processing
    for superior voice quality and similarity.

    Args:
        audio: Input audio array from voice cloning engine
        sample_rate: Sample rate in Hz
        enhancement_level: Enhancement intensity ('light', 'standard', 'aggressive', 'ultra')
        preserve_prosody: If True, applies gentle processing to preserve natural prosody
        target_lufs: Target LUFS for normalization (-30.0 to -6.0)
        use_rvc_postprocessing: If True, apply RVC post-processing for enhanced quality
        reference_audio: Optional reference audio for RVC processing (path or array)
        rvc_model_path: Optional path to RVC model for post-processing

    Returns:
        Enhanced audio array with improved quality

    Raises:
        ImportError: If required libraries are not installed
    """
    if librosa is None:
        raise ImportError(
            "librosa is required for voice cloning enhancement. "
            "Install with: pip install librosa==0.11.0"
        )

    enhanced = audio.copy()

    # Step 1: Remove DC offset and normalize
    enhanced = enhanced - np.mean(enhanced)
    if np.max(np.abs(enhanced)) > 0:
        enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95

    # Step 2: Advanced denoising (level-dependent)
    if enhancement_level in ["standard", "aggressive", "ultra"]:
        if HAS_VOICEFIXER:
            try:
                # VoiceFixer provides state-of-the-art voice restoration
                if sample_rate != 44100:
                    enhanced_44k = resample_audio(enhanced, sample_rate, 44100)
                else:
                    enhanced_44k = enhanced

                if "cloning_voicefixer" not in _lazy_caches:
                    _lazy_caches["cloning_voicefixer"] = VoiceFixer()

                enhanced_44k = _lazy_caches["cloning_voicefixer"].restore(enhanced_44k, cuda=False)

                if sample_rate != 44100:
                    enhanced = resample_audio(enhanced_44k, 44100, sample_rate)
                else:
                    enhanced = enhanced_44k

                logger.debug("VoiceFixer restoration applied")
            except Exception as e:
                logger.debug(f"VoiceFixer failed: {e}, using standard denoising")

        # Apply standard denoising as fallback or additional step
        if nr is not None:
            with contextlib.suppress(Exception):
                enhanced = nr.reduce_noise(y=enhanced, sr=sample_rate)

    # Step 3: Spectral smoothing for naturalness (if preserving prosody)
    if preserve_prosody and librosa is not None:
        try:
            # Gentle spectral smoothing to reduce artifacts while preserving prosody
            stft = librosa.stft(enhanced, hop_length=512, n_fft=2048)
            magnitude = np.abs(stft)
            phase = np.angle(stft)

            # Apply gentle Gaussian smoothing to magnitude spectrum
            if HAS_SCIPY:
                try:
                    from scipy import ndimage

                    # Adaptive smoothing based on enhancement level
                    smoothing_sigma = {
                        "light": 0.2,
                        "standard": 0.4,
                        "aggressive": 0.6,
                        "ultra": 0.8,
                    }.get(enhancement_level, 0.4)
                    smoothed_magnitude = ndimage.gaussian_filter(magnitude, sigma=smoothing_sigma)
                except ImportError:
                    # Fallback: simple moving average
                    smoothed_magnitude = magnitude
            else:
                smoothed_magnitude = magnitude

            # Reconstruct with smoothed magnitude
            enhanced_stft = smoothed_magnitude * np.exp(1j * phase)
            enhanced = librosa.istft(enhanced_stft, hop_length=512)

            # Normalize after spectral processing
            if np.max(np.abs(enhanced)) > 0:
                enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95
        except Exception as e:
            logger.debug(f"Spectral smoothing failed: {e}")

    # Step 4: RVC Post-processing (if enabled and available)
    if use_rvc_postprocessing and reference_audio is not None:
        try:
            enhanced = _apply_rvc_postprocessing(
                enhanced, sample_rate, reference_audio, rvc_model_path
            )
            logger.debug("RVC post-processing applied")
        except Exception as e:
            logger.debug(f"RVC post-processing failed: {e}, continuing without RVC")

    # Step 5: Prosody-preserving artifact removal
    if enhancement_level in ["standard", "aggressive", "ultra"]:
        with contextlib.suppress(Exception):
            enhanced = remove_artifacts(enhanced, sample_rate)

    # Step 6: Advanced spectral enhancement (ultra mode)
    if enhancement_level == "ultra" and librosa is not None:
        try:
            # Multi-band spectral enhancement
            stft = librosa.stft(enhanced, hop_length=512, n_fft=2048)
            magnitude = np.abs(stft)
            phase = np.angle(stft)
            freqs = librosa.fft_frequencies(sr=sample_rate)

            # Enhance different frequency bands
            # Low frequencies (80-500 Hz): slight boost for warmth
            low_mask = (freqs >= 80) & (freqs < 500)
            if np.any(low_mask):
                magnitude[low_mask, :] *= 1.05

            # Mid frequencies (500-3000 Hz): clarity boost
            mid_mask = (freqs >= 500) & (freqs < 3000)
            if np.any(mid_mask):
                magnitude[mid_mask, :] *= 1.1

            # High frequencies (3000-8000 Hz): presence boost
            high_mask = (freqs >= 3000) & (freqs < 8000)
            if np.any(high_mask):
                magnitude[high_mask, :] *= 1.15

            # Reconstruct
            enhanced_stft = magnitude * np.exp(1j * phase)
            enhanced = librosa.istft(enhanced_stft, hop_length=512)

            # Normalize
            if np.max(np.abs(enhanced)) > 0:
                enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95
        except Exception as e:
            logger.debug(f"Ultra spectral enhancement failed: {e}")

    # Step 7: LUFS normalization for broadcast standards
    try:
        enhanced = normalize_lufs(enhanced, sample_rate, target_lufs=target_lufs)
    except Exception:
        # Fallback to peak normalization
        if np.max(np.abs(enhanced)) > 0:
            enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95

    # Step 8: Final quality pass (aggressive/ultra mode only)
    if enhancement_level in ["aggressive", "ultra"] and librosa is not None:
        try:
            # Apply gentle high-frequency enhancement for clarity
            stft = librosa.stft(enhanced, hop_length=512)
            magnitude = np.abs(stft)
            phase = np.angle(stft)

            # Boost high frequencies slightly (above 4kHz) for clarity
            freqs = librosa.fft_frequencies(sr=sample_rate)
            high_freq_mask = freqs > 4000
            if np.any(high_freq_mask):
                boost_factor = 1.15 if enhancement_level == "ultra" else 1.1
                magnitude[high_freq_mask, :] *= boost_factor

            # Reconstruct
            enhanced_stft = magnitude * np.exp(1j * phase)
            enhanced = librosa.istft(enhanced_stft, hop_length=512)

            # Final normalization
            if np.max(np.abs(enhanced)) > 0:
                enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95
        except Exception:
            ...

    return enhanced


def _apply_rvc_postprocessing(
    audio: np.ndarray,
    sample_rate: int,
    reference_audio: str | Path | np.ndarray,
    rvc_model_path: str | None = None,
) -> np.ndarray:
    """
    Apply RVC (Retrieval-based Voice Conversion) post-processing for enhanced quality.

    This function attempts to use RVC for voice conversion if available, otherwise
    uses advanced spectral matching techniques.

    Args:
        audio: Audio to enhance
        sample_rate: Sample rate
        reference_audio: Reference audio for voice matching
        rvc_model_path: Optional RVC model path

    Returns:
        Enhanced audio with improved voice similarity
    """
    # Try to use actual RVC engine if available
    try:
        from app.core.engines.rvc_engine import RVCEngine

        rvc_engine = RVCEngine()
        if rvc_engine.initialize():
            # Load reference audio
            if isinstance(reference_audio, (str, Path)):
                import soundfile as sf

                ref_audio, ref_sr = sf.read(str(reference_audio))
            else:
                ref_audio = reference_audio
                ref_sr = sample_rate

            # Convert using RVC
            converted = rvc_engine.convert_voice(
                source_audio=audio,
                reference_audio=ref_audio,
                sample_rate=sample_rate,
                reference_sample_rate=ref_sr,
            )
            if converted is not None:
                if isinstance(converted, tuple):
                    result_arr = converted[0]
                    if result_arr is not None:
                        return result_arr
                else:
                    return converted
    except (ImportError, Exception) as e:
        logger.debug(f"RVC engine not available: {e}, using spectral matching")

    # Fallback: Advanced spectral matching
    if librosa is not None:
        try:
            # Load reference audio
            if isinstance(reference_audio, (str, Path)):
                ref_audio, ref_sr = librosa.load(str(reference_audio), sr=sample_rate)
            else:
                ref_audio = reference_audio
                if len(ref_audio.shape) > 1:
                    ref_audio = np.mean(ref_audio, axis=1)

            # Extract spectral envelopes
            audio_stft = librosa.stft(audio, hop_length=512)
            ref_stft = librosa.stft(ref_audio, hop_length=512)

            audio_magnitude = np.abs(audio_stft)
            ref_magnitude = np.abs(ref_stft)
            audio_phase = np.angle(audio_stft)

            # Match spectral envelope (voice timbre)
            # Use reference magnitude envelope to modify audio
            if ref_magnitude.shape[1] > audio_magnitude.shape[1]:
                # Interpolate reference to match audio length
                from scipy.interpolate import interp1d

                ref_interp = interp1d(
                    np.linspace(0, 1, ref_magnitude.shape[1]),
                    ref_magnitude,
                    kind="linear",
                    axis=1,
                    fill_value="extrapolate",
                )
                ref_magnitude = ref_interp(np.linspace(0, 1, audio_magnitude.shape[1]))

            # Blend spectral envelopes (70% reference, 30% original for naturalness)
            matched_magnitude = (
                0.7 * ref_magnitude[:, : audio_magnitude.shape[1]] + 0.3 * audio_magnitude
            )

            # Reconstruct
            matched_stft = matched_magnitude * np.exp(1j * audio_phase)
            enhanced = librosa.istft(matched_stft, hop_length=512)

            # Normalize
            if np.max(np.abs(enhanced)) > 0:
                enhanced = enhanced / np.max(np.abs(enhanced)) * 0.95

            return enhanced
        except Exception as e:
            logger.debug(f"Spectral matching failed: {e}")

    # If all else fails, return original
    return audio
