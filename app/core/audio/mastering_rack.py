"""
Mastering Rack Module for VoiceStudio
Professional mastering processing chain for final audio output

Compatible with:
- Python 3.10+
- librosa>=0.11.0
- scipy>=1.9.0
- soundfile>=0.12.1
- pyloudnorm>=0.1.1
"""

from __future__ import annotations

import logging

import numpy as np

logger = logging.getLogger(__name__)

try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    logger.warning("librosa not installed. Install with: pip install librosa")

try:
    from scipy import signal

    HAS_SCIPY = True
except ImportError:
    HAS_SCIPY = False
    logger.warning("scipy not installed. Install with: pip install scipy")

try:
    import pyloudnorm as pyln

    HAS_PYLOUDNORM = True
except ImportError:
    HAS_PYLOUDNORM = False
    logger.warning("pyloudnorm not installed. Install with: pip install pyloudnorm")

# Import audio utilities
try:
    from .audio_utils import normalize_lufs

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    logger.warning("audio_utils not available")

# Import Post-FX for effect processing
try:
    from .post_fx import PostFXProcessor

    HAS_POST_FX = True
except ImportError:
    HAS_POST_FX = False
    logger.warning("post_fx not available")


class MasteringRack:
    """
    Mastering Rack for professional final audio mastering.

    Supports:
    - Multiband compression
    - Limiter
    - Stereo enhancement
    - Final EQ
    - Loudness normalization (LUFS)
    - Dithering
    - Mastering presets
    """

    def __init__(self, sample_rate: int = 24000):
        """
        Initialize Mastering Rack.

        Args:
            sample_rate: Default sample rate for processing
        """
        self.sample_rate = sample_rate

    def master(
        self,
        audio: np.ndarray,
        sample_rate: int | None = None,
        preset: str | None = None,
        **kwargs,
    ) -> np.ndarray:
        """
        Apply mastering chain to audio.

        Args:
            audio: Input audio array
            sample_rate: Sample rate (uses instance default if None)
            preset: Mastering preset name (e.g., 'broadcast', 'podcast', 'music')
            **kwargs: Custom mastering parameters:
                - multiband_compressor: Enable multiband compression
                - limiter: Enable limiter
                - stereo_enhance: Enable stereo enhancement
                - final_eq: Enable final EQ
                - normalize_lufs: Target LUFS for normalization
                - dither: Enable dithering

        Returns:
            Mastered audio array
        """
        if sample_rate is None:
            sample_rate = self.sample_rate

        processed_audio = audio.copy()

        # Apply preset if specified
        if preset:
            preset_params = self.get_preset(preset)
            kwargs.update(preset_params)

        # Mastering chain (order matters)
        # 1. Multiband compression
        if kwargs.get("multiband_compressor", True):
            processed_audio = self.apply_multiband_compressor(processed_audio, sample_rate, kwargs)

        # 2. Final EQ
        if kwargs.get("final_eq", True):
            processed_audio = self.apply_final_eq(processed_audio, sample_rate, kwargs)

        # 3. Stereo enhancement
        if kwargs.get("stereo_enhance", False):
            processed_audio = self.apply_stereo_enhancement(processed_audio, sample_rate, kwargs)

        # 4. Limiter
        if kwargs.get("limiter", True):
            processed_audio = self.apply_limiter(processed_audio, sample_rate, kwargs)

        # 5. Loudness normalization
        if kwargs.get("normalize_lufs") is not None:
            target_lufs = kwargs.get("normalize_lufs", -23.0)
            processed_audio = self.apply_loudness_normalization(
                processed_audio, sample_rate, target_lufs
            )

        # 6. Dithering (if needed for bit depth reduction)
        if kwargs.get("dither", False):
            processed_audio = self.apply_dithering(processed_audio, kwargs)

        return processed_audio

    def apply_multiband_compressor(
        self,
        audio: np.ndarray,
        sample_rate: int,
        params: dict,
    ) -> np.ndarray:
        """Apply multiband compression."""
        if not HAS_SCIPY:
            logger.warning("scipy required for multiband compression")
            return audio

        # Multiband parameters
        low_threshold = params.get("low_threshold", -18.0)
        mid_threshold = params.get("mid_threshold", -15.0)
        high_threshold = params.get("high_threshold", -12.0)

        low_ratio = params.get("low_ratio", 3.0)
        mid_ratio = params.get("mid_ratio", 4.0)
        high_ratio = params.get("high_ratio", 2.0)

        # Process each channel
        if len(audio.shape) == 1:
            audio = audio.reshape(1, -1)
            was_mono = True
        else:
            was_mono = False

        processed_channels = []

        for channel in audio:
            nyquist = sample_rate / 2.0

            # Split into bands
            # Low band (below 200 Hz)
            low_cutoff = 200.0 / nyquist
            b_low, a_low = signal.iirfilter(4, low_cutoff, btype="lowpass", ftype="butter")
            low_band = signal.filtfilt(b_low, a_low, channel)

            # Mid band (200-5000 Hz)
            mid_low = 200.0 / nyquist
            mid_high = 5000.0 / nyquist
            b_mid, a_mid = signal.iirfilter(
                4, [mid_low, mid_high], btype="bandpass", ftype="butter"
            )
            mid_band = signal.filtfilt(b_mid, a_mid, channel)

            # High band (above 5000 Hz)
            high_cutoff = 5000.0 / nyquist
            b_high, a_high = signal.iirfilter(4, high_cutoff, btype="highpass", ftype="butter")
            high_band = signal.filtfilt(b_high, a_high, channel)

            # Compress each band
            low_compressed = self._compress_band(low_band, sample_rate, low_threshold, low_ratio)
            mid_compressed = self._compress_band(mid_band, sample_rate, mid_threshold, mid_ratio)
            high_compressed = self._compress_band(
                high_band, sample_rate, high_threshold, high_ratio
            )

            # Recombine bands
            processed = low_compressed + mid_compressed + high_compressed
            processed_channels.append(processed)

        result = np.array(processed_channels)
        if was_mono:
            result = result[0]

        # Normalize
        max_val = np.max(np.abs(result))
        if max_val > 0:
            result = result / max_val * 0.95

        return result

    def _compress_band(
        self,
        audio: np.ndarray,
        sample_rate: int,
        threshold_db: float,
        ratio: float,
    ) -> np.ndarray:
        """Compress a single frequency band."""
        threshold = 10.0 ** (threshold_db / 20.0)

        # Calculate RMS envelope
        window_size = int(sample_rate * 0.01)  # 10ms
        rms = np.sqrt(np.convolve(audio**2, np.ones(window_size) / window_size, mode="same"))

        # Calculate gain reduction
        gain_reduction = np.ones_like(rms)
        above_threshold = rms > threshold

        if np.any(above_threshold):
            excess = rms[above_threshold] - threshold
            compressed = threshold + excess / ratio
            gain_reduction[above_threshold] = compressed / rms[above_threshold]

        # Apply compression
        compressed_audio: np.ndarray = audio * gain_reduction
        return compressed_audio

    def apply_final_eq(self, audio: np.ndarray, sample_rate: int, params: dict) -> np.ndarray:
        """Apply final EQ for mastering."""
        if not HAS_SCIPY:
            logger.warning("scipy required for final EQ")
            return audio

        # EQ parameters (subtle mastering EQ)
        low_gain_db = params.get("final_low_gain", 0.0)
        mid_gain_db = params.get("final_mid_gain", 0.0)
        high_gain_db = params.get("final_high_gain", 0.0)

        # Convert dB to linear
        low_gain = 10.0 ** (low_gain_db / 20.0)
        mid_gain = 10.0 ** (mid_gain_db / 20.0)
        high_gain = 10.0 ** (high_gain_db / 20.0)

        # Process each channel
        if len(audio.shape) == 1:
            audio = audio.reshape(1, -1)
            was_mono = True
        else:
            was_mono = False

        processed_channels = []

        for channel in audio:
            processed = channel.copy()
            nyquist = sample_rate / 2.0

            # Low shelf (below 200 Hz)
            if low_gain != 1.0:
                low_cutoff = 200.0 / nyquist
                b, a = signal.iirfilter(4, low_cutoff, btype="lowpass", ftype="butter")
                low_signal = signal.filtfilt(b, a, processed)
                processed = processed + (low_signal * (low_gain - 1.0))

            # Mid band (200-8000 Hz)
            if mid_gain != 1.0:
                mid_low = 200.0 / nyquist
                mid_high = 8000.0 / nyquist
                b, a = signal.iirfilter(4, [mid_low, mid_high], btype="bandpass", ftype="butter")
                mid_signal = signal.filtfilt(b, a, processed)
                processed = processed + (mid_signal * (mid_gain - 1.0))

            # High shelf (above 8000 Hz)
            if high_gain != 1.0:
                high_cutoff = 8000.0 / nyquist
                b, a = signal.iirfilter(4, high_cutoff, btype="highpass", ftype="butter")
                high_signal = signal.filtfilt(b, a, processed)
                processed = processed + (high_signal * (high_gain - 1.0))

            processed_channels.append(processed)

        result = np.array(processed_channels)
        if was_mono:
            result = result[0]

        # Normalize
        max_val = np.max(np.abs(result))
        if max_val > 0.95:
            result = result / max_val * 0.95

        return result

    def apply_stereo_enhancement(
        self, audio: np.ndarray, sample_rate: int, params: dict
    ) -> np.ndarray:
        """Apply stereo enhancement."""
        width = params.get("stereo_width", 1.0)

        # Only process stereo audio
        if len(audio.shape) == 1:
            return audio

        if audio.shape[0] != 2:
            return audio

        left = audio[0]
        right = audio[1]

        # Mid/side processing
        mid = (left + right) / 2.0
        side = (left - right) / 2.0

        # Enhance side signal
        enhanced_side = side * width

        # Reconstruct stereo
        enhanced_left = mid + enhanced_side
        enhanced_right = mid - enhanced_side

        result = np.array([enhanced_left, enhanced_right])

        # Normalize
        max_val = np.max(np.abs(result))
        if max_val > 0:
            result = result / max_val * 0.95

        return result

    def apply_limiter(self, audio: np.ndarray, sample_rate: int, params: dict) -> np.ndarray:
        """Apply limiter (brickwall)."""
        ceiling_db = params.get("ceiling", -0.3)
        release_ms = params.get("limiter_release", 10.0)

        ceiling = 10.0 ** (ceiling_db / 20.0)

        # Process each channel
        if len(audio.shape) == 1:
            audio = audio.reshape(1, -1)
            was_mono = True
        else:
            was_mono = False

        processed_channels = []

        for channel in audio:
            # Simple peak limiter
            # Find peaks above ceiling
            peaks = np.abs(channel) > ceiling

            if np.any(peaks):
                # Calculate gain reduction
                gain_reduction = np.ones_like(channel)
                gain_reduction[peaks] = ceiling / np.abs(channel[peaks])

                # Smooth gain reduction (release envelope)
                release_samples = int(sample_rate * release_ms / 1000.0)
                envelope = np.ones_like(gain_reduction)

                for i in range(1, len(envelope)):
                    if gain_reduction[i] < envelope[i - 1]:
                        # Attack (instant)
                        envelope[i] = gain_reduction[i]
                    else:
                        # Release
                        alpha = 1.0 / release_samples
                        envelope[i] = envelope[i - 1] * (1 - alpha) + gain_reduction[i] * alpha

                processed = channel * envelope
            else:
                processed = channel

            processed_channels.append(processed)

        result = np.array(processed_channels)
        if was_mono:
            result = result[0]

        return result

    def apply_loudness_normalization(
        self, audio: np.ndarray, sample_rate: int, target_lufs: float
    ) -> np.ndarray:
        """Apply loudness normalization to target LUFS."""
        if HAS_AUDIO_UTILS and HAS_PYLOUDNORM:
            try:
                return normalize_lufs(audio, sample_rate, target_lufs)
            except Exception as e:
                logger.warning(f"LUFS normalization failed: {e}")
                # Fallback to peak normalization
                max_val = np.max(np.abs(audio))
                if max_val > 0:
                    peak_normalized: np.ndarray = audio / max_val * 0.95
                    return peak_normalized
                return audio

        # Fallback to peak normalization
        max_val = np.max(np.abs(audio))
        if max_val > 0:
            fallback_normalized: np.ndarray = audio / max_val * 0.95
            return fallback_normalized
        return audio

    def apply_dithering(self, audio: np.ndarray, params: dict) -> np.ndarray:
        """Apply dithering for bit depth reduction."""
        bit_depth = params.get("bit_depth", 16)

        # Generate triangular dither
        dither_level = 1.0 / (2.0 ** (bit_depth - 1))
        dither = (
            np.random.triangular(-dither_level, 0, dither_level, size=audio.shape)
            if len(audio.shape) > 1
            else np.random.triangular(-dither_level, 0, dither_level, size=len(audio))
        )

        # Add dither
        dithered = audio + dither

        # Quantize
        quantized: np.ndarray = np.round(dithered * (2.0 ** (bit_depth - 1))) / (
            2.0 ** (bit_depth - 1)
        )

        return quantized

    def get_preset(self, preset_name: str) -> dict:
        """Get mastering preset parameters."""
        presets = {
            "broadcast": {
                "multiband_compressor": True,
                "limiter": True,
                "stereo_enhance": False,
                "final_eq": True,
                "normalize_lufs": -23.0,
                "dither": False,
                "final_low_gain": 0.0,
                "final_mid_gain": 0.0,
                "final_high_gain": 0.0,
                "ceiling": -0.3,
            },
            "podcast": {
                "multiband_compressor": True,
                "limiter": True,
                "stereo_enhance": False,
                "final_eq": True,
                "normalize_lufs": -19.0,
                "dither": False,
                "final_low_gain": -1.0,
                "final_mid_gain": 0.5,
                "final_high_gain": 1.0,
                "ceiling": -0.5,
            },
            "music": {
                "multiband_compressor": True,
                "limiter": True,
                "stereo_enhance": True,
                "final_eq": True,
                "normalize_lufs": -16.0,
                "dither": False,
                "final_low_gain": 0.5,
                "final_mid_gain": 0.0,
                "final_high_gain": 0.5,
                "ceiling": -0.1,
                "stereo_width": 1.2,
            },
            "voice": {
                "multiband_compressor": True,
                "limiter": True,
                "stereo_enhance": False,
                "final_eq": True,
                "normalize_lufs": -23.0,
                "dither": False,
                "final_low_gain": -0.5,
                "final_mid_gain": 1.0,
                "final_high_gain": 0.5,
                "ceiling": -0.3,
            },
        }

        return presets.get(preset_name.lower(), {})


def create_mastering_rack(sample_rate: int = 24000) -> MasteringRack:
    """
    Factory function to create a Mastering Rack instance.

    Args:
        sample_rate: Default sample rate for processing

    Returns:
        Initialized MasteringRack instance
    """
    return MasteringRack(sample_rate=sample_rate)


def master_audio(
    audio: np.ndarray,
    sample_rate: int,
    preset: str | None = None,
    **kwargs,
) -> np.ndarray:
    """
    Convenience function to master audio.

    Args:
        audio: Input audio array
        sample_rate: Sample rate
        preset: Mastering preset name
        **kwargs: Custom mastering parameters

    Returns:
        Mastered audio array
    """
    rack = MasteringRack(sample_rate=sample_rate)
    return rack.master(audio, sample_rate, preset, **kwargs)
