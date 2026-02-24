"""
Prosody Transfer Engine.

Task 4.3.2: Copy prosody from reference audio to target.
Enables style/emotion transfer while preserving content.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass

import numpy as np

logger = logging.getLogger(__name__)


@dataclass
class ProsodyFeatures:
    """Extracted prosody features."""

    # Pitch contour
    f0_contour: np.ndarray  # Pitch values per frame
    f0_mean: float
    f0_std: float
    f0_range: tuple[float, float]

    # Energy/intensity contour
    energy_contour: np.ndarray
    energy_mean: float
    energy_std: float

    # Timing
    duration: float  # Total duration in seconds
    speaking_rate: float  # Syllables per second (estimated)
    pause_locations: list[tuple[float, float]]  # (start, end) in seconds

    # Voice quality
    jitter: float  # Pitch perturbation
    shimmer: float  # Amplitude perturbation
    hnr: float  # Harmonics-to-noise ratio


class ProsodyExtractor:
    """
    Extract prosody features from audio.
    """

    def __init__(self, sample_rate: int = 16000):
        self._sample_rate = sample_rate
        self._frame_size = 512
        self._hop_size = 160

    def extract(self, audio: np.ndarray) -> ProsodyFeatures:
        """
        Extract prosody features from audio.

        Args:
            audio: Audio samples

        Returns:
            ProsodyFeatures containing all prosodic information
        """
        duration = len(audio) / self._sample_rate

        # Extract F0 (pitch)
        f0_contour = self._extract_f0(audio)
        f0_voiced = f0_contour[f0_contour > 0]
        f0_mean = float(np.mean(f0_voiced)) if len(f0_voiced) > 0 else 0.0
        f0_std = float(np.std(f0_voiced)) if len(f0_voiced) > 0 else 0.0
        f0_range = (
            (float(np.min(f0_voiced)), float(np.max(f0_voiced)))
            if len(f0_voiced) > 0
            else (0.0, 0.0)
        )

        # Extract energy
        energy_contour = self._extract_energy(audio)
        energy_mean = float(np.mean(energy_contour))
        energy_std = float(np.std(energy_contour))

        # Detect pauses
        pause_locations = self._detect_pauses(energy_contour)

        # Estimate speaking rate
        speaking_rate = self._estimate_speaking_rate(audio, duration)

        # Voice quality measures
        jitter = self._calculate_jitter(f0_contour)
        shimmer = self._calculate_shimmer(energy_contour)
        hnr = self._calculate_hnr(audio)

        return ProsodyFeatures(
            f0_contour=f0_contour,
            f0_mean=f0_mean,
            f0_std=f0_std,
            f0_range=f0_range,
            energy_contour=energy_contour,
            energy_mean=energy_mean,
            energy_std=energy_std,
            duration=duration,
            speaking_rate=speaking_rate,
            pause_locations=pause_locations,
            jitter=jitter,
            shimmer=shimmer,
            hnr=hnr,
        )

    def _extract_f0(self, audio: np.ndarray) -> np.ndarray:
        """Extract fundamental frequency contour."""
        try:
            import librosa

            pitches, magnitudes = librosa.piptrack(
                y=audio,
                sr=self._sample_rate,
                n_fft=self._frame_size,
                hop_length=self._hop_size,
            )

            # Get pitch at maximum magnitude per frame
            f0 = np.zeros(pitches.shape[1])
            for i in range(pitches.shape[1]):
                idx = magnitudes[:, i].argmax()
                f0[i] = pitches[idx, i]

            return f0

        except ImportError:
            logger.debug("librosa not available for F0 extraction, using fallback autocorrelation")

        # Fallback: autocorrelation
        n_frames = (len(audio) - self._frame_size) // self._hop_size + 1
        f0 = np.zeros(n_frames)

        for i in range(n_frames):
            start = i * self._hop_size
            frame = audio[start : start + self._frame_size]

            if len(frame) < self._frame_size // 2:
                continue

            corr = np.correlate(frame, frame, mode="full")
            corr = corr[len(corr) // 2 :]

            min_lag = int(self._sample_rate / 500)
            max_lag = int(self._sample_rate / 50)

            if max_lag < len(corr):
                search = corr[min_lag:max_lag]
                if len(search) > 0 and np.max(search) > 0:
                    peak = np.argmax(search) + min_lag
                    if peak > 0:
                        f0[i] = self._sample_rate / peak

        return f0

    def _extract_energy(self, audio: np.ndarray) -> np.ndarray:
        """Extract energy/RMS contour."""
        try:
            import librosa

            return np.asarray(
                librosa.feature.rms(
                    y=audio,
                    frame_length=self._frame_size,
                    hop_length=self._hop_size,
                )[0]
            )
        except ImportError:
            logger.debug(
                "librosa not available for RMS extraction, using fallback manual calculation"
            )

        # Manual RMS calculation
        n_frames = (len(audio) - self._frame_size) // self._hop_size + 1
        energy = np.zeros(n_frames)

        for i in range(n_frames):
            start = i * self._hop_size
            frame = audio[start : start + self._frame_size]
            energy[i] = np.sqrt(np.mean(frame**2))

        return energy

    def _detect_pauses(
        self,
        energy: np.ndarray,
        threshold: float = 0.1,
        min_pause_frames: int = 5,
    ) -> list[tuple[float, float]]:
        """Detect pauses in speech."""
        pauses = []
        in_pause = False
        pause_start = 0

        energy_threshold = np.mean(energy) * threshold

        for i, e in enumerate(energy):
            if e < energy_threshold:
                if not in_pause:
                    in_pause = True
                    pause_start = i
            else:
                if in_pause:
                    pause_len = i - pause_start
                    if pause_len >= min_pause_frames:
                        start_time = pause_start * self._hop_size / self._sample_rate
                        end_time = i * self._hop_size / self._sample_rate
                        pauses.append((start_time, end_time))
                    in_pause = False

        return pauses

    def _estimate_speaking_rate(self, audio: np.ndarray, duration: float) -> float:
        """Estimate speaking rate in syllables/second."""
        try:
            import librosa

            onset_env = librosa.onset.onset_strength(y=audio, sr=self._sample_rate)
            onsets = librosa.onset.onset_detect(onset_envelope=onset_env, sr=self._sample_rate)
            return len(onsets) / duration if duration > 0 else 0.0
        except ImportError:
            return 4.5  # Average speaking rate

    def _calculate_jitter(self, f0: np.ndarray) -> float:
        """Calculate jitter (pitch perturbation)."""
        f0_voiced = f0[f0 > 0]
        if len(f0_voiced) < 2:
            return 0.0

        diffs = np.abs(np.diff(f0_voiced))
        return float(np.mean(diffs) / np.mean(f0_voiced))

    def _calculate_shimmer(self, energy: np.ndarray) -> float:
        """Calculate shimmer (amplitude perturbation)."""
        if len(energy) < 2:
            return 0.0

        diffs = np.abs(np.diff(energy))
        return float(np.mean(diffs) / np.mean(energy)) if np.mean(energy) > 0 else 0.0

    def _calculate_hnr(self, audio: np.ndarray) -> float:
        """Calculate harmonics-to-noise ratio."""
        try:
            import librosa

            harmonic, percussive = librosa.effects.hpss(audio)
            h_energy = np.sum(harmonic**2)
            p_energy = np.sum(percussive**2)
            if p_energy > 0:
                return float(10 * np.log10(h_energy / p_energy))
            return 20.0
        except ImportError:
            return 15.0  # Default


class ProsodyTransferEngine:
    """
    Transfer prosody from reference to target audio.

    Features:
    - F0 contour transfer
    - Energy contour transfer
    - Duration/timing transfer
    - Partial prosody transfer
    """

    def __init__(self, sample_rate: int = 16000):
        self._sample_rate = sample_rate
        self._extractor = ProsodyExtractor(sample_rate)

    def transfer(
        self,
        target_audio: np.ndarray,
        reference_audio: np.ndarray,
        pitch_weight: float = 1.0,
        energy_weight: float = 1.0,
        duration_weight: float = 0.0,
    ) -> np.ndarray:
        """
        Transfer prosody from reference to target.

        Args:
            target_audio: Audio to modify
            reference_audio: Audio to copy prosody from
            pitch_weight: How much pitch to transfer (0-1)
            energy_weight: How much energy to transfer (0-1)
            duration_weight: How much duration to transfer (0-1)

        Returns:
            Target audio with reference prosody
        """
        # Extract prosody from both
        target_prosody = self._extractor.extract(target_audio)
        ref_prosody = self._extractor.extract(reference_audio)

        output = target_audio.copy().astype(np.float32)

        # Transfer pitch
        if pitch_weight > 0:
            output = self._transfer_pitch(output, target_prosody, ref_prosody, pitch_weight)

        # Transfer energy
        if energy_weight > 0:
            output = self._transfer_energy(output, target_prosody, ref_prosody, energy_weight)

        # Transfer duration (time-stretch)
        if duration_weight > 0:
            output = self._transfer_duration(output, target_prosody, ref_prosody, duration_weight)

        return output

    def _transfer_pitch(
        self,
        audio: np.ndarray,
        target_prosody: ProsodyFeatures,
        ref_prosody: ProsodyFeatures,
        weight: float,
    ) -> np.ndarray:
        """Transfer pitch characteristics."""
        # Calculate pitch shift to match reference mean
        ref_prosody.f0_mean - target_prosody.f0_mean

        if target_prosody.f0_mean > 0:
            # Convert Hz difference to semitones
            semitones = 12 * np.log2(ref_prosody.f0_mean / target_prosody.f0_mean)
            semitones = semitones * weight

            # Apply pitch shift
            try:
                import librosa

                return librosa.effects.pitch_shift(audio, sr=self._sample_rate, n_steps=semitones)
            except ImportError:
                # Simple resampling fallback
                ratio = 2 ** (semitones / 12)
                new_len = int(len(audio) / ratio)
                if new_len > 0:
                    indices = np.linspace(0, len(audio) - 1, new_len)
                    shifted = np.interp(indices, np.arange(len(audio)), audio)
                    # Resample back
                    indices = np.linspace(0, len(shifted) - 1, len(audio))
                    return np.interp(indices, np.arange(len(shifted)), shifted)

        return audio

    def _transfer_energy(
        self,
        audio: np.ndarray,
        target_prosody: ProsodyFeatures,
        ref_prosody: ProsodyFeatures,
        weight: float,
    ) -> np.ndarray:
        """Transfer energy contour."""
        if target_prosody.energy_mean <= 0:
            return audio

        # Scale to match reference energy
        energy_ratio = ref_prosody.energy_mean / target_prosody.energy_mean

        # Blend with original
        final_ratio = 1.0 + (energy_ratio - 1.0) * weight

        return audio * final_ratio

    def _transfer_duration(
        self,
        audio: np.ndarray,
        target_prosody: ProsodyFeatures,
        ref_prosody: ProsodyFeatures,
        weight: float,
    ) -> np.ndarray:
        """Transfer duration (time-stretch)."""
        if target_prosody.duration <= 0:
            return audio

        duration_ratio = ref_prosody.duration / target_prosody.duration

        # Blend with original
        final_ratio = 1.0 + (duration_ratio - 1.0) * weight

        # Time stretch
        try:
            import librosa

            return librosa.effects.time_stretch(audio, rate=1 / final_ratio)
        except ImportError:
            # Simple resampling (changes pitch too)
            new_len = int(len(audio) * final_ratio)
            if new_len > 0:
                indices = np.linspace(0, len(audio) - 1, new_len)
                return np.interp(indices, np.arange(len(audio)), audio)

        return audio

    def extract_prosody(self, audio: np.ndarray) -> ProsodyFeatures:
        """Extract prosody features from audio."""
        return self._extractor.extract(audio)

    def apply_prosody(
        self,
        audio: np.ndarray,
        prosody: ProsodyFeatures,
    ) -> np.ndarray:
        """Apply extracted prosody to audio."""
        target_prosody = self._extractor.extract(audio)

        # Create dummy reference with the given prosody
        output = self._transfer_pitch(audio, target_prosody, prosody, 1.0)
        output = self._transfer_energy(output, target_prosody, prosody, 1.0)

        return output
