"""
AI Audio Enhancement Service

Phase 9.4: AI Audio Enhancement (Adobe Podcast Parity)
Transforms poor audio to studio quality with intelligent processing.

Features:
- One-click enhance (noise reduction + EQ + normalization)
- Strength slider (0-100%)
- Voice isolation (AI-powered extraction)
- Room reverb removal (de-reverb)
- Audio repair (click/pop/clipping repair)
"""

from __future__ import annotations

import logging
import tempfile
import uuid
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


class EnhancementMode(Enum):
    """Enhancement processing modes."""

    FULL = "full"  # All enhancements
    VOICE_ONLY = "voice_only"  # Voice isolation + cleanup
    NOISE_REDUCTION = "noise_reduction"  # Noise reduction only
    NORMALIZATION = "normalization"  # Normalize levels only
    REPAIR = "repair"  # Click/pop/clipping repair
    DE_REVERB = "de_reverb"  # Remove room reverb


@dataclass
class EnhancementPreset:
    """Preset enhancement configuration."""

    preset_id: str
    name: str
    description: str
    noise_reduction: float  # 0-1
    normalization: bool
    target_loudness_lufs: float
    de_reverb: float  # 0-1
    eq_enabled: bool
    eq_preset: str
    voice_isolation: bool
    repair_clicks: bool
    repair_clipping: bool

    def to_dict(self) -> dict[str, Any]:
        return {
            "preset_id": self.preset_id,
            "name": self.name,
            "description": self.description,
            "noise_reduction": self.noise_reduction,
            "normalization": self.normalization,
            "target_loudness_lufs": self.target_loudness_lufs,
            "de_reverb": self.de_reverb,
            "eq_enabled": self.eq_enabled,
            "eq_preset": self.eq_preset,
            "voice_isolation": self.voice_isolation,
            "repair_clicks": self.repair_clicks,
            "repair_clipping": self.repair_clipping,
        }


@dataclass
class EnhancementResult:
    """Result of audio enhancement."""

    success: bool
    output_path: str | None
    original_metrics: dict[str, float]
    enhanced_metrics: dict[str, float]
    improvements: dict[str, float]
    processing_time_ms: float
    preset_applied: str | None
    error_message: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "success": self.success,
            "output_path": self.output_path,
            "original_metrics": self.original_metrics,
            "enhanced_metrics": self.enhanced_metrics,
            "improvements": self.improvements,
            "processing_time_ms": self.processing_time_ms,
            "preset_applied": self.preset_applied,
            "error_message": self.error_message,
        }


@dataclass
class AudioMetrics:
    """Audio quality metrics."""

    rms_db: float
    peak_db: float
    lufs: float
    noise_floor_db: float
    dynamic_range_db: float
    crest_factor_db: float
    clipping_percentage: float
    silence_percentage: float

    def to_dict(self) -> dict[str, float]:
        return {
            "rms_db": self.rms_db,
            "peak_db": self.peak_db,
            "lufs": self.lufs,
            "noise_floor_db": self.noise_floor_db,
            "dynamic_range_db": self.dynamic_range_db,
            "crest_factor_db": self.crest_factor_db,
            "clipping_percentage": self.clipping_percentage,
            "silence_percentage": self.silence_percentage,
        }


class AIAudioEnhancementService:
    """
    Service for AI-powered audio enhancement.

    Implements Phase 9.4 features:
    - 9.4.1: One-click enhance
    - 9.4.2: Strength slider
    - 9.4.3: Voice isolation
    - 9.4.4: Room reverb removal
    - 9.4.5: Audio repair
    """

    # Target metrics
    TARGET_LUFS = -16.0  # Broadcast standard
    TARGET_PEAK_DB = -1.0  # Headroom

    def __init__(self):
        self._initialized = False
        self._presets: dict[str, EnhancementPreset] = {}
        self._output_dir = Path(tempfile.gettempdir()) / "voicestudio" / "enhanced"
        self._voice_separator = None
        self._noise_reducer = None

        self._init_presets()
        logger.info("AIAudioEnhancementService created")

    def _init_presets(self):
        """Initialize enhancement presets."""
        presets = [
            EnhancementPreset(
                preset_id="preset_studio",
                name="Studio Quality",
                description="Professional studio-quality processing",
                noise_reduction=0.8,
                normalization=True,
                target_loudness_lufs=-16.0,
                de_reverb=0.6,
                eq_enabled=True,
                eq_preset="voice_presence",
                voice_isolation=True,
                repair_clicks=True,
                repair_clipping=True,
            ),
            EnhancementPreset(
                preset_id="preset_podcast",
                name="Podcast",
                description="Optimized for spoken word podcasts",
                noise_reduction=0.7,
                normalization=True,
                target_loudness_lufs=-16.0,
                de_reverb=0.5,
                eq_enabled=True,
                eq_preset="podcast",
                voice_isolation=False,
                repair_clicks=True,
                repair_clipping=False,
            ),
            EnhancementPreset(
                preset_id="preset_voice_memo",
                name="Voice Memo",
                description="Quick cleanup for voice recordings",
                noise_reduction=0.6,
                normalization=True,
                target_loudness_lufs=-18.0,
                de_reverb=0.3,
                eq_enabled=False,
                eq_preset="none",
                voice_isolation=False,
                repair_clicks=False,
                repair_clipping=False,
            ),
            EnhancementPreset(
                preset_id="preset_interview",
                name="Interview",
                description="Balanced processing for interviews",
                noise_reduction=0.65,
                normalization=True,
                target_loudness_lufs=-16.0,
                de_reverb=0.4,
                eq_enabled=True,
                eq_preset="clarity",
                voice_isolation=False,
                repair_clicks=True,
                repair_clipping=False,
            ),
            EnhancementPreset(
                preset_id="preset_cleanup",
                name="Light Cleanup",
                description="Minimal processing, preserve natural sound",
                noise_reduction=0.4,
                normalization=True,
                target_loudness_lufs=-18.0,
                de_reverb=0.2,
                eq_enabled=False,
                eq_preset="none",
                voice_isolation=False,
                repair_clicks=False,
                repair_clipping=False,
            ),
            EnhancementPreset(
                preset_id="preset_aggressive",
                name="Maximum Enhancement",
                description="Aggressive processing for very poor audio",
                noise_reduction=0.95,
                normalization=True,
                target_loudness_lufs=-14.0,
                de_reverb=0.8,
                eq_enabled=True,
                eq_preset="voice_presence",
                voice_isolation=True,
                repair_clicks=True,
                repair_clipping=True,
            ),
        ]

        for preset in presets:
            self._presets[preset.preset_id] = preset

    async def initialize(self) -> bool:
        """Initialize the enhancement service."""
        if self._initialized:
            return True

        try:
            # Create output directory
            self._output_dir.mkdir(parents=True, exist_ok=True)

            try:
                from demucs.pretrained import get_model

                self._has_voice_separation = True
                logger.info("Voice separation model (demucs) available")
            except ImportError:
                self._has_voice_separation = False
                logger.info(
                    "Voice separation model (demucs) not installed; separation features disabled"
                )

            self._initialized = True
            logger.info("AIAudioEnhancementService initialized")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize AIAudioEnhancementService: {e}")
            return False

    async def one_click_enhance(
        self,
        audio_path: str,
        strength: float = 0.75,
        output_path: str | None = None,
    ) -> EnhancementResult:
        """
        One-click enhancement with automatic processing.

        Phase 9.4.1: One-click enhance
        Phase 9.4.2: Strength slider

        Args:
            audio_path: Input audio file path
            strength: Enhancement strength (0.0-1.0)
            output_path: Optional output path

        Returns:
            EnhancementResult with processing outcome
        """
        import time

        start_time = time.perf_counter()

        if not self._initialized:
            await self.initialize()

        try:
            # Load audio
            audio, sample_rate = self._load_audio(audio_path)
            if audio is None:
                return EnhancementResult(
                    success=False,
                    output_path=None,
                    original_metrics={},
                    enhanced_metrics={},
                    improvements={},
                    processing_time_ms=0,
                    preset_applied=None,
                    error_message="Failed to load audio file",
                )

            # Calculate original metrics
            original_metrics = self._calculate_metrics(audio, sample_rate)

            # Apply enhancements based on strength
            enhanced = await self._apply_enhancements(
                audio,
                sample_rate,
                strength,
            )

            # Calculate enhanced metrics
            enhanced_metrics = self._calculate_metrics(enhanced, sample_rate)

            # Calculate improvements
            improvements = {
                "noise_reduction_db": original_metrics.noise_floor_db
                - enhanced_metrics.noise_floor_db,
                "loudness_improvement_lufs": enhanced_metrics.lufs - original_metrics.lufs,
                "dynamic_range_change_db": enhanced_metrics.dynamic_range_db
                - original_metrics.dynamic_range_db,
            }

            # Save output
            if output_path is None:
                output_id = uuid.uuid4().hex[:8]
                output_path = str(self._output_dir / f"enhanced_{output_id}.wav")

            self._save_audio(enhanced, sample_rate, output_path)

            processing_time = (time.perf_counter() - start_time) * 1000

            return EnhancementResult(
                success=True,
                output_path=output_path,
                original_metrics=original_metrics.to_dict(),
                enhanced_metrics=enhanced_metrics.to_dict(),
                improvements=improvements,
                processing_time_ms=processing_time,
                preset_applied="one_click",
            )

        except Exception as e:
            logger.error(f"One-click enhance failed: {e}")
            return EnhancementResult(
                success=False,
                output_path=None,
                original_metrics={},
                enhanced_metrics={},
                improvements={},
                processing_time_ms=(time.perf_counter() - start_time) * 1000,
                preset_applied=None,
                error_message=str(e),
            )

    async def isolate_voice(
        self,
        audio_path: str,
        output_path: str | None = None,
    ) -> tuple[str | None, dict[str, Any]]:
        """
        Isolate voice from background noise using AI.

        Phase 9.4.3: Voice isolation

        Args:
            audio_path: Input audio file path
            output_path: Optional output path

        Returns:
            Tuple of (output path, metadata)
        """
        import time

        start_time = time.perf_counter()

        if not self._initialized:
            await self.initialize()

        try:
            audio, sample_rate = self._load_audio(audio_path)
            if audio is None:
                return None, {"error": "Failed to load audio"}

            # Apply voice isolation
            isolated = self._isolate_voice_internal(audio, sample_rate)

            # Save output
            if output_path is None:
                output_id = uuid.uuid4().hex[:8]
                output_path = str(self._output_dir / f"isolated_{output_id}.wav")

            self._save_audio(isolated, sample_rate, output_path)

            processing_time = (time.perf_counter() - start_time) * 1000

            return output_path, {
                "processing_time_ms": processing_time,
                "sample_rate": sample_rate,
            }

        except Exception as e:
            logger.error(f"Voice isolation failed: {e}")
            return None, {"error": str(e)}

    async def remove_reverb(
        self,
        audio_path: str,
        strength: float = 0.7,
        output_path: str | None = None,
    ) -> tuple[str | None, dict[str, Any]]:
        """
        Remove room reverb from audio.

        Phase 9.4.4: Room reverb removal

        Args:
            audio_path: Input audio file path
            strength: De-reverb strength (0.0-1.0)
            output_path: Optional output path

        Returns:
            Tuple of (output path, metadata)
        """
        import time

        start_time = time.perf_counter()

        if not self._initialized:
            await self.initialize()

        try:
            audio, sample_rate = self._load_audio(audio_path)
            if audio is None:
                return None, {"error": "Failed to load audio"}

            # Apply de-reverb
            dereverbed = self._remove_reverb_internal(audio, sample_rate, strength)

            # Save output
            if output_path is None:
                output_id = uuid.uuid4().hex[:8]
                output_path = str(self._output_dir / f"dereverb_{output_id}.wav")

            self._save_audio(dereverbed, sample_rate, output_path)

            processing_time = (time.perf_counter() - start_time) * 1000

            return output_path, {
                "processing_time_ms": processing_time,
                "strength": strength,
            }

        except Exception as e:
            logger.error(f"De-reverb failed: {e}")
            return None, {"error": str(e)}

    async def repair_audio(
        self,
        audio_path: str,
        repair_clicks: bool = True,
        repair_clipping: bool = True,
        output_path: str | None = None,
    ) -> tuple[str | None, dict[str, Any]]:
        """
        Repair audio artifacts (clicks, pops, clipping).

        Phase 9.4.5: Audio repair

        Args:
            audio_path: Input audio file path
            repair_clicks: Whether to repair clicks/pops
            repair_clipping: Whether to repair clipping
            output_path: Optional output path

        Returns:
            Tuple of (output path, metadata)
        """
        import time

        start_time = time.perf_counter()

        if not self._initialized:
            await self.initialize()

        try:
            audio, sample_rate = self._load_audio(audio_path)
            if audio is None:
                return None, {"error": "Failed to load audio"}

            repairs_made = []
            repaired = audio.copy()

            # Repair clipping
            if repair_clipping:
                repaired, clip_count = self._repair_clipping_internal(repaired)
                if clip_count > 0:
                    repairs_made.append(f"Repaired {clip_count} clipped samples")

            # Repair clicks
            if repair_clicks:
                repaired, click_count = self._repair_clicks_internal(repaired, sample_rate)
                if click_count > 0:
                    repairs_made.append(f"Removed {click_count} clicks/pops")

            # Save output
            if output_path is None:
                output_id = uuid.uuid4().hex[:8]
                output_path = str(self._output_dir / f"repaired_{output_id}.wav")

            self._save_audio(repaired, sample_rate, output_path)

            processing_time = (time.perf_counter() - start_time) * 1000

            return output_path, {
                "processing_time_ms": processing_time,
                "repairs_made": repairs_made,
            }

        except Exception as e:
            logger.error(f"Audio repair failed: {e}")
            return None, {"error": str(e)}

    async def apply_preset(
        self,
        audio_path: str,
        preset_id: str,
        output_path: str | None = None,
    ) -> EnhancementResult:
        """
        Apply an enhancement preset to audio.

        Args:
            audio_path: Input audio file path
            preset_id: Preset ID to apply
            output_path: Optional output path

        Returns:
            EnhancementResult with processing outcome
        """
        import time

        start_time = time.perf_counter()

        preset = self._presets.get(preset_id)
        if not preset:
            return EnhancementResult(
                success=False,
                output_path=None,
                original_metrics={},
                enhanced_metrics={},
                improvements={},
                processing_time_ms=0,
                preset_applied=None,
                error_message=f"Preset not found: {preset_id}",
            )

        if not self._initialized:
            await self.initialize()

        try:
            audio, sample_rate = self._load_audio(audio_path)
            if audio is None:
                return EnhancementResult(
                    success=False,
                    output_path=None,
                    original_metrics={},
                    enhanced_metrics={},
                    improvements={},
                    processing_time_ms=0,
                    preset_applied=preset_id,
                    error_message="Failed to load audio file",
                )

            # Calculate original metrics
            original_metrics = self._calculate_metrics(audio, sample_rate)

            # Apply preset enhancements
            enhanced = audio.copy()

            # Voice isolation
            if preset.voice_isolation:
                enhanced = self._isolate_voice_internal(enhanced, sample_rate)

            # Noise reduction
            if preset.noise_reduction > 0:
                enhanced = self._reduce_noise_internal(
                    enhanced, sample_rate, preset.noise_reduction
                )

            # De-reverb
            if preset.de_reverb > 0:
                enhanced = self._remove_reverb_internal(enhanced, sample_rate, preset.de_reverb)

            # Repair
            if preset.repair_clipping:
                enhanced, _ = self._repair_clipping_internal(enhanced)
            if preset.repair_clicks:
                enhanced, _ = self._repair_clicks_internal(enhanced, sample_rate)

            # EQ
            if preset.eq_enabled:
                enhanced = self._apply_eq_preset(enhanced, sample_rate, preset.eq_preset)

            # Normalization
            if preset.normalization:
                enhanced = self._normalize_loudness(
                    enhanced, sample_rate, preset.target_loudness_lufs
                )

            # Calculate enhanced metrics
            enhanced_metrics = self._calculate_metrics(enhanced, sample_rate)

            # Calculate improvements
            improvements = {
                "noise_reduction_db": original_metrics.noise_floor_db
                - enhanced_metrics.noise_floor_db,
                "loudness_improvement_lufs": enhanced_metrics.lufs - original_metrics.lufs,
            }

            # Save output
            if output_path is None:
                output_id = uuid.uuid4().hex[:8]
                output_path = str(self._output_dir / f"preset_{output_id}.wav")

            self._save_audio(enhanced, sample_rate, output_path)

            processing_time = (time.perf_counter() - start_time) * 1000

            return EnhancementResult(
                success=True,
                output_path=output_path,
                original_metrics=original_metrics.to_dict(),
                enhanced_metrics=enhanced_metrics.to_dict(),
                improvements=improvements,
                processing_time_ms=processing_time,
                preset_applied=preset_id,
            )

        except Exception as e:
            logger.error(f"Preset application failed: {e}")
            return EnhancementResult(
                success=False,
                output_path=None,
                original_metrics={},
                enhanced_metrics={},
                improvements={},
                processing_time_ms=(time.perf_counter() - start_time) * 1000,
                preset_applied=preset_id,
                error_message=str(e),
            )

    def list_presets(self) -> list[EnhancementPreset]:
        """List all enhancement presets."""
        return list(self._presets.values())

    def get_preset(self, preset_id: str) -> EnhancementPreset | None:
        """Get a preset by ID."""
        return self._presets.get(preset_id)

    # Internal processing methods

    def _load_audio(self, path: str) -> tuple[np.ndarray | None, int]:
        """Load audio file."""
        try:
            import soundfile as sf

            audio, sample_rate = sf.read(path)

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            return audio.astype(np.float32), sample_rate
        except Exception as e:
            logger.error(f"Failed to load audio: {e}")
            return None, 0

    def _save_audio(self, audio: np.ndarray, sample_rate: int, path: str):
        """Save audio file."""
        try:
            import soundfile as sf

            # Ensure directory exists
            Path(path).parent.mkdir(parents=True, exist_ok=True)

            sf.write(path, audio, sample_rate)
            logger.debug(f"Saved audio to {path}")
        except Exception as e:
            logger.error(f"Failed to save audio: {e}")
            raise

    def _calculate_metrics(self, audio: np.ndarray, sample_rate: int) -> AudioMetrics:
        """Calculate audio quality metrics."""
        # RMS
        rms = np.sqrt(np.mean(audio**2))
        rms_db = 20 * np.log10(rms + 1e-10)

        # Peak
        peak = np.max(np.abs(audio))
        peak_db = 20 * np.log10(peak + 1e-10)

        # LUFS (simplified)
        lufs = rms_db - 0.691  # Simplified approximation

        # Noise floor (estimate from quietest 10%)
        sorted_abs = np.sort(np.abs(audio))
        noise_floor = np.mean(sorted_abs[: len(sorted_abs) // 10])
        noise_floor_db = 20 * np.log10(noise_floor + 1e-10)

        # Dynamic range
        dynamic_range_db = peak_db - noise_floor_db

        # Crest factor
        crest_factor_db = peak_db - rms_db

        # Clipping percentage
        clipping_threshold = 0.99
        clipping_percentage = np.sum(np.abs(audio) > clipping_threshold) / len(audio) * 100

        # Silence percentage
        silence_threshold = 0.01
        silence_percentage = np.sum(np.abs(audio) < silence_threshold) / len(audio) * 100

        return AudioMetrics(
            rms_db=rms_db,
            peak_db=peak_db,
            lufs=lufs,
            noise_floor_db=noise_floor_db,
            dynamic_range_db=dynamic_range_db,
            crest_factor_db=crest_factor_db,
            clipping_percentage=clipping_percentage,
            silence_percentage=silence_percentage,
        )

    async def _apply_enhancements(
        self,
        audio: np.ndarray,
        sample_rate: int,
        strength: float,
    ) -> np.ndarray:
        """Apply one-click enhancements based on strength."""
        enhanced = audio.copy()

        # Noise reduction (scaled by strength)
        noise_strength = strength * 0.8
        enhanced = self._reduce_noise_internal(enhanced, sample_rate, noise_strength)

        # De-reverb (scaled by strength)
        if strength > 0.3:
            reverb_strength = (strength - 0.3) / 0.7 * 0.6
            enhanced = self._remove_reverb_internal(enhanced, sample_rate, reverb_strength)

        # Repair if strength is high
        if strength > 0.5:
            enhanced, _ = self._repair_clipping_internal(enhanced)
            enhanced, _ = self._repair_clicks_internal(enhanced, sample_rate)

        # EQ for voice presence
        if strength > 0.4:
            enhanced = self._apply_eq_preset(enhanced, sample_rate, "voice_presence")

        # Normalize
        enhanced = self._normalize_loudness(enhanced, sample_rate, self.TARGET_LUFS)

        return enhanced

    def _reduce_noise_internal(
        self,
        audio: np.ndarray,
        sample_rate: int,
        strength: float,
    ) -> np.ndarray:
        """Internal noise reduction using spectral gating."""
        try:
            from scipy import signal

            # STFT
            nperseg = 1024
            _f, _t, Zxx = signal.stft(audio, sample_rate, nperseg=nperseg)

            # Estimate noise floor from quietest frames
            magnitudes = np.abs(Zxx)
            frame_energies = np.sum(magnitudes, axis=0)
            noise_frames = np.argsort(frame_energies)[: max(1, len(frame_energies) // 10)]
            noise_profile = np.mean(magnitudes[:, noise_frames], axis=1, keepdims=True)

            # Spectral gating
            threshold = noise_profile * (1 + strength * 2)
            mask = magnitudes > threshold
            mask = mask.astype(float)

            # Smooth mask
            mask = signal.convolve2d(mask, np.ones((3, 3)) / 9, mode="same")
            mask = np.clip(mask, 0, 1)

            # Apply mask
            Zxx_clean = Zxx * mask

            # ISTFT
            _, cleaned = signal.istft(Zxx_clean, sample_rate, nperseg=nperseg)

            # Match length
            if len(cleaned) > len(audio):
                cleaned = cleaned[: len(audio)]
            elif len(cleaned) < len(audio):
                cleaned = np.pad(cleaned, (0, len(audio) - len(cleaned)))

            return cleaned.astype(np.float32)

        except Exception as e:
            logger.warning(f"Noise reduction failed: {e}")
            return audio

    def _isolate_voice_internal(
        self,
        audio: np.ndarray,
        sample_rate: int,
    ) -> np.ndarray:
        """
        Isolate voice using AI-based source separation.

        Task 4.5.2: Replace placeholder separation model with real implementation.
        """
        # Try Demucs (state-of-the-art source separation)
        try:
            import torch
            from demucs.apply import apply_model
            from demucs.pretrained import get_model

            # Load model
            model = get_model("htdemucs")
            model.eval()

            device = "cuda" if torch.cuda.is_available() else "cpu"
            model.to(device)

            # Prepare audio
            if len(audio.shape) == 1:
                audio_tensor = torch.from_numpy(audio).float().unsqueeze(0).unsqueeze(0)
            else:
                audio_tensor = torch.from_numpy(audio).float().unsqueeze(0)

            audio_tensor = audio_tensor.to(device)

            # Separate
            with torch.no_grad():
                sources = apply_model(model, audio_tensor, device=device)

            # Get vocals (index 3 in htdemucs: drums, bass, other, vocals)
            vocals = sources[:, 3, :, :].mean(dim=1).squeeze().cpu().numpy()

            logger.info("Voice isolated using Demucs")
            return vocals.astype(np.float32)

        except ImportError:
            logger.debug("Demucs not available, trying Spleeter")
        except Exception as e:
            logger.debug(f"Demucs separation failed: {e}")

        # Try Spleeter
        try:
            import tempfile

            import soundfile as sf
            from spleeter.separator import Separator

            separator = Separator("spleeter:2stems")

            # Save temp file
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                sf.write(tmp.name, audio, sample_rate)

                # Separate
                prediction = separator.separate(audio.reshape(-1, 1))

                vocals = prediction["vocals"].mean(axis=1)

                logger.info("Voice isolated using Spleeter")
                return vocals.astype(np.float32)

        except ImportError:
            logger.debug("Spleeter not available")
        except Exception as e:
            logger.debug(f"Spleeter separation failed: {e}")

        # Try librosa HPSS (harmonic-percussive separation)
        try:
            import librosa

            # Separate harmonic (voice-like) from percussive
            harmonic, _percussive = librosa.effects.hpss(audio)

            # Apply bandpass for voice frequencies
            from scipy import signal

            nyquist = sample_rate / 2
            low = 80 / nyquist
            high = min(8000 / nyquist, 0.99)

            b, a = signal.butter(4, [low, high], btype="band")
            filtered = signal.filtfilt(b, a, harmonic)

            logger.info("Voice isolated using librosa HPSS + bandpass")
            return filtered.astype(np.float32)

        except ImportError:
            logger.debug("librosa not available")
        except Exception as e:
            logger.debug(f"librosa HPSS failed: {e}")

        # Final fallback: simple bandpass filter
        try:
            from scipy import signal

            nyquist = sample_rate / 2
            low = 80 / nyquist
            high = min(8000 / nyquist, 0.99)

            b, a = signal.butter(4, [low, high], btype="band")
            filtered = signal.filtfilt(b, a, audio)

            logger.warning(
                "Voice isolation using basic bandpass filter (install demucs for better results)"
            )
            return filtered.astype(np.float32)

        except Exception as e:
            logger.warning(f"Voice isolation failed: {e}")
            return audio

    def _remove_reverb_internal(
        self,
        audio: np.ndarray,
        sample_rate: int,
        strength: float,
    ) -> np.ndarray:
        """Remove reverb using spectral processing."""
        try:
            from scipy import signal

            # STFT
            nperseg = 2048
            _f, _t, Zxx = signal.stft(audio, sample_rate, nperseg=nperseg)

            # Estimate reverb tail (late reflections)
            magnitudes = np.abs(Zxx)

            # Temporal smoothing to estimate direct sound
            kernel_size = int(0.05 * sample_rate / (nperseg // 4))  # 50ms window
            np.ones(kernel_size) / kernel_size

            direct_estimate = np.zeros_like(magnitudes)
            for i in range(magnitudes.shape[0]):
                direct_estimate[i] = np.maximum.accumulate(magnitudes[i][::-1])[::-1] * (
                    1 - strength * 0.5
                )

            # Suppress reverb
            mask = np.minimum(1.0, direct_estimate / (magnitudes + 1e-10))
            mask = mask**strength

            Zxx_dereverb = Zxx * mask

            # ISTFT
            _, dereverbed = signal.istft(Zxx_dereverb, sample_rate, nperseg=nperseg)

            # Match length
            if len(dereverbed) > len(audio):
                dereverbed = dereverbed[: len(audio)]
            elif len(dereverbed) < len(audio):
                dereverbed = np.pad(dereverbed, (0, len(audio) - len(dereverbed)))

            return dereverbed.astype(np.float32)

        except Exception as e:
            logger.warning(f"De-reverb failed: {e}")
            return audio

    def _repair_clipping_internal(
        self,
        audio: np.ndarray,
    ) -> tuple[np.ndarray, int]:
        """Repair clipped samples using interpolation."""
        threshold = 0.99
        clipped = np.abs(audio) > threshold
        clip_count = np.sum(clipped)

        if clip_count == 0:
            return audio, 0

        repaired = audio.copy()

        # Find clipped regions and interpolate
        clip_indices = np.where(clipped)[0]

        for idx in clip_indices:
            # Find non-clipped neighbors
            left = idx - 1
            while left >= 0 and clipped[left]:
                left -= 1

            right = idx + 1
            while right < len(audio) and clipped[right]:
                right += 1

            # Interpolate
            if left >= 0 and right < len(audio):
                t = (idx - left) / (right - left)
                repaired[idx] = audio[left] * (1 - t) + audio[right] * t

        # Apply soft limiting
        repaired = np.tanh(repaired)

        return repaired, clip_count

    def _repair_clicks_internal(
        self,
        audio: np.ndarray,
        sample_rate: int,
    ) -> tuple[np.ndarray, int]:
        """Detect and repair clicks/pops."""
        # Calculate local derivatives
        diff = np.diff(audio)

        # Detect sudden changes (potential clicks)
        threshold = np.std(diff) * 5
        clicks = np.abs(diff) > threshold
        click_indices = np.where(clicks)[0]
        click_count = len(click_indices)

        if click_count == 0:
            return audio, 0

        repaired = audio.copy()
        window_size = int(0.001 * sample_rate)  # 1ms window

        for idx in click_indices:
            # Interpolate over click
            left = max(0, idx - window_size)
            right = min(len(audio), idx + window_size)

            if left > 0 and right < len(audio):
                # Linear interpolation
                t = np.linspace(0, 1, right - left)
                repaired[left:right] = audio[left] * (1 - t) + audio[right - 1] * t

        return repaired, click_count

    def _apply_eq_preset(
        self,
        audio: np.ndarray,
        sample_rate: int,
        preset_name: str,
    ) -> np.ndarray:
        """Apply EQ preset."""
        try:
            from scipy import signal

            # EQ presets (frequency, gain_db, Q)
            presets = {
                "voice_presence": [
                    (80, -3, 1.0),  # Reduce rumble
                    (200, 1, 1.0),  # Warmth
                    (2000, 3, 1.0),  # Presence
                    (5000, 2, 1.0),  # Clarity
                    (8000, -1, 1.0),  # Reduce harshness
                ],
                "podcast": [
                    (100, -2, 1.0),  # Reduce bass
                    (300, 2, 1.0),  # Warmth
                    (3000, 2, 1.0),  # Presence
                    (6000, 1, 1.0),  # Air
                ],
                "clarity": [
                    (150, -1, 1.0),  # Clean low end
                    (2500, 3, 1.0),  # Clarity
                    (5000, 2, 1.0),  # Brightness
                ],
            }

            bands = presets.get(preset_name, [])
            if not bands:
                return audio

            result = audio.copy()

            for freq, gain_db, q in bands:
                if freq >= sample_rate / 2:
                    continue

                # Design peaking filter
                w0 = freq / (sample_rate / 2)
                A = 10 ** (gain_db / 40)

                b, a = signal.iirpeak(w0, q)
                b = b * A

                result = signal.filtfilt(b, a, result)

            return result.astype(np.float32)

        except Exception as e:
            logger.warning(f"EQ application failed: {e}")
            return audio

    def _normalize_loudness(
        self,
        audio: np.ndarray,
        sample_rate: int,
        target_lufs: float,
    ) -> np.ndarray:
        """Normalize audio to target loudness."""
        try:
            # Calculate current loudness
            rms = np.sqrt(np.mean(audio**2))
            current_lufs = 20 * np.log10(rms + 1e-10) - 0.691

            # Calculate gain
            gain_db = target_lufs - current_lufs
            gain_linear = 10 ** (gain_db / 20)

            # Apply gain with limiting
            normalized = audio * gain_linear

            # Soft limiting
            max_val = np.max(np.abs(normalized))
            if max_val > 0.95:
                normalized = normalized / max_val * 0.95

            return normalized.astype(np.float32)

        except Exception as e:
            logger.warning(f"Normalization failed: {e}")
            return audio


# Singleton instance
_ai_enhancement_service: AIAudioEnhancementService | None = None


def get_ai_enhancement_service() -> AIAudioEnhancementService:
    """Get or create the AI audio enhancement service singleton."""
    global _ai_enhancement_service
    if _ai_enhancement_service is None:
        _ai_enhancement_service = AIAudioEnhancementService()
    return _ai_enhancement_service
