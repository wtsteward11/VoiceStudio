"""
Instant Voice Cloning Service

Phase 9.1: Competitive Feature Parity - Instant Voice Cloning
Enables voice cloning from 6-10 second audio samples (Fish Audio/ElevenLabs parity).

Features:
- Zero-shot cloning with minimal audio (6-10s)
- Instant preview synthesis (2-3 second preview)
- Clone quality estimation before processing
- Speaker embedding extraction for rapid cloning
"""

from __future__ import annotations

import asyncio
import hashlib
import logging
import time
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Constants for instant cloning
MIN_AUDIO_DURATION_SECONDS = 6.0  # Minimum for instant cloning
OPTIMAL_AUDIO_DURATION_SECONDS = 10.0  # Optimal duration
MAX_PREVIEW_DURATION_SECONDS = 3.0  # Preview synthesis length
QUALITY_THRESHOLD_EXCELLENT = 0.85
QUALITY_THRESHOLD_GOOD = 0.70
QUALITY_THRESHOLD_ACCEPTABLE = 0.50


class CloneQualityEstimate:
    """Quality estimate for voice cloning potential."""

    def __init__(
        self,
        overall_score: float,
        audio_quality: float,
        voice_clarity: float,
        embedding_confidence: float,
        noise_level: float,
        duration_score: float,
        recommendations: list[str],
        estimated_clone_fidelity: str,
    ):
        self.overall_score = overall_score
        self.audio_quality = audio_quality
        self.voice_clarity = voice_clarity
        self.embedding_confidence = embedding_confidence
        self.noise_level = noise_level
        self.duration_score = duration_score
        self.recommendations = recommendations
        self.estimated_clone_fidelity = estimated_clone_fidelity
        self.timestamp = datetime.utcnow().isoformat()

    def to_dict(self) -> dict[str, Any]:
        return {
            "overall_score": self.overall_score,
            "audio_quality": self.audio_quality,
            "voice_clarity": self.voice_clarity,
            "embedding_confidence": self.embedding_confidence,
            "noise_level": self.noise_level,
            "duration_score": self.duration_score,
            "recommendations": self.recommendations,
            "estimated_clone_fidelity": self.estimated_clone_fidelity,
            "timestamp": self.timestamp,
        }


class InstantCloningResult:
    """Result of instant voice cloning."""

    def __init__(
        self,
        success: bool,
        profile_id: str | None,
        embedding_vector: np.ndarray | None,
        quality_estimate: CloneQualityEstimate | None,
        preview_audio_path: str | None,
        processing_time_ms: float,
        error_message: str | None = None,
    ):
        self.success = success
        self.profile_id = profile_id
        self.embedding_vector = embedding_vector
        self.quality_estimate = quality_estimate
        self.preview_audio_path = preview_audio_path
        self.processing_time_ms = processing_time_ms
        self.error_message = error_message
        self.timestamp = datetime.utcnow().isoformat()

    def to_dict(self) -> dict[str, Any]:
        return {
            "success": self.success,
            "profile_id": self.profile_id,
            "has_embedding": self.embedding_vector is not None,
            "quality_estimate": self.quality_estimate.to_dict() if self.quality_estimate else None,
            "preview_audio_path": self.preview_audio_path,
            "processing_time_ms": self.processing_time_ms,
            "error_message": self.error_message,
            "timestamp": self.timestamp,
        }


class InstantCloningService:
    """
    Service for instant voice cloning with minimal audio samples.

    Implements Phase 9.1 features:
    - 9.1.1: Zero-shot cloning enhancement (6-10s audio)
    - 9.1.2: Speaker embedding extraction
    - 9.1.3: Instant preview synthesis
    - 9.1.4: Clone quality estimator
    """

    def __init__(self):
        self._initialized = False
        self._speaker_encoder = None
        self._synthesis_engine = None
        self._embedding_cache: dict[str, np.ndarray] = {}
        self._quality_cache: dict[str, CloneQualityEstimate] = {}
        logger.info("InstantCloningService created")

    async def initialize(self) -> bool:
        """Initialize the instant cloning service."""
        if self._initialized:
            return True

        try:
            # Import speaker encoder
            try:
                from app.core.engines.speaker_encoder_engine import SpeakerEncoderEngine

                self._speaker_encoder = SpeakerEncoderEngine(
                    backend="resemblyzer",
                    device="cuda",
                    gpu=True,
                    enable_cache=True,
                )
                self._speaker_encoder.initialize()
                logger.info("Speaker encoder initialized for instant cloning")
            except ImportError as e:
                logger.warning(f"Speaker encoder not available: {e}")
                self._speaker_encoder = None

            self._initialized = True
            logger.info("InstantCloningService initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize InstantCloningService: {e}")
            return False

    async def estimate_clone_quality(
        self,
        audio_path: str,
        audio_array: np.ndarray | None = None,
        sample_rate: int | None = None,
    ) -> CloneQualityEstimate:
        """
        Estimate voice cloning quality before processing.

        Phase 9.1.4: Clone quality estimator

        Args:
            audio_path: Path to audio file
            audio_array: Optional pre-loaded audio array
            sample_rate: Optional sample rate

        Returns:
            CloneQualityEstimate with quality metrics and recommendations
        """
        recommendations = []

        try:
            # Load audio if not provided
            if audio_array is None:
                from app.core.audio import audio_utils

                audio_array, sample_rate = audio_utils.load_audio(audio_path)

            # Calculate duration
            duration = len(audio_array) / sample_rate if sample_rate else 0

            # Duration score (6-10s optimal)
            if duration >= OPTIMAL_AUDIO_DURATION_SECONDS:
                duration_score = 1.0
            elif duration >= MIN_AUDIO_DURATION_SECONDS:
                duration_score = (
                    0.7
                    + (duration - MIN_AUDIO_DURATION_SECONDS)
                    / (OPTIMAL_AUDIO_DURATION_SECONDS - MIN_AUDIO_DURATION_SECONDS)
                    * 0.3
                )
            elif duration >= 3.0:
                duration_score = 0.3 + (duration - 3.0) / (MIN_AUDIO_DURATION_SECONDS - 3.0) * 0.4
                recommendations.append(
                    f"Audio is only {duration:.1f}s. Aim for 6-10 seconds for best results."
                )
            else:
                duration_score = duration / 3.0 * 0.3
                recommendations.append(
                    f"Audio is very short ({duration:.1f}s). Need at least 6 seconds for good cloning."
                )

            # Convert to mono if needed
            audio_mono = np.mean(audio_array, axis=1) if len(audio_array.shape) > 1 else audio_array

            # Calculate audio quality metrics
            np.sqrt(np.mean(audio_mono**2))
            max_amplitude = np.max(np.abs(audio_mono))

            # Check for clipping
            if max_amplitude > 0.95:
                recommendations.append("Audio may be clipping. Reduce input gain.")
                audio_quality = 0.6
            elif max_amplitude < 0.1:
                recommendations.append("Audio level is very low. Increase input gain.")
                audio_quality = 0.5
            else:
                audio_quality = min(1.0, max_amplitude / 0.7)

            # Estimate noise level (using high-frequency energy ratio)
            try:
                from scipy import signal

                freqs, psd = signal.welch(audio_mono, sample_rate, nperseg=1024)
                high_freq_mask = freqs > 4000
                low_freq_mask = (freqs >= 80) & (freqs <= 4000)

                high_freq_energy = np.sum(psd[high_freq_mask]) if np.any(high_freq_mask) else 0
                low_freq_energy = np.sum(psd[low_freq_mask]) if np.any(low_freq_mask) else 1

                noise_ratio = high_freq_energy / (low_freq_energy + 1e-10)
                noise_level = min(1.0, noise_ratio * 2)

                if noise_level > 0.5:
                    recommendations.append(
                        "High background noise detected. Record in quieter environment."
                    )
            except Exception as e:
                # GAP-PY-001: Noise analysis failed, using default
                logger.debug(f"Noise level analysis failed: {e}")
                noise_level = 0.3  # Default if analysis fails

            # Voice clarity (using zero-crossing rate variance)
            try:
                frame_length = int(0.025 * sample_rate)  # 25ms frames
                hop_length = int(0.010 * sample_rate)  # 10ms hop

                zcr_values = []
                for i in range(0, len(audio_mono) - frame_length, hop_length):
                    frame = audio_mono[i : i + frame_length]
                    zcr = np.sum(np.abs(np.diff(np.sign(frame)))) / (2 * frame_length)
                    zcr_values.append(zcr)

                zcr_std = np.std(zcr_values) if zcr_values else 0
                voice_clarity = max(0.3, 1.0 - min(1.0, zcr_std * 10))
            except Exception as e:
                # GAP-PY-001: Voice clarity analysis failed, using default
                logger.debug(f"Voice clarity analysis failed: {e}")
                voice_clarity = 0.7  # Default if analysis fails

            # Embedding confidence (if speaker encoder available)
            embedding_confidence = 0.0
            if self._speaker_encoder:
                try:
                    embedding = self._speaker_encoder.extract_embedding(audio_mono, sample_rate)
                    if embedding is not None:
                        # Embedding confidence based on norm and variance
                        emb_norm = np.linalg.norm(embedding)
                        emb_std = np.std(embedding)
                        embedding_confidence = min(1.0, (emb_norm * emb_std) / 0.5)
                except Exception as e:
                    logger.debug(f"Failed to extract embedding for quality estimate: {e}")

            # Calculate overall score
            weights = {
                "duration": 0.25,
                "audio_quality": 0.20,
                "voice_clarity": 0.20,
                "embedding": 0.20,
                "noise": 0.15,
            }

            overall_score = (
                weights["duration"] * duration_score
                + weights["audio_quality"] * audio_quality
                + weights["voice_clarity"] * voice_clarity
                + weights["embedding"] * embedding_confidence
                + weights["noise"] * (1.0 - noise_level)
            )

            # Determine estimated fidelity
            if overall_score >= QUALITY_THRESHOLD_EXCELLENT:
                estimated_fidelity = "excellent"
            elif overall_score >= QUALITY_THRESHOLD_GOOD:
                estimated_fidelity = "good"
            elif overall_score >= QUALITY_THRESHOLD_ACCEPTABLE:
                estimated_fidelity = "acceptable"
            else:
                estimated_fidelity = "poor"
                recommendations.append(
                    "Voice cloning quality may be limited. Consider improving audio quality."
                )

            return CloneQualityEstimate(
                overall_score=overall_score,
                audio_quality=audio_quality,
                voice_clarity=voice_clarity,
                embedding_confidence=embedding_confidence,
                noise_level=noise_level,
                duration_score=duration_score,
                recommendations=recommendations,
                estimated_clone_fidelity=estimated_fidelity,
            )

        except Exception as e:
            logger.error(f"Failed to estimate clone quality: {e}")
            return CloneQualityEstimate(
                overall_score=0.0,
                audio_quality=0.0,
                voice_clarity=0.0,
                embedding_confidence=0.0,
                noise_level=1.0,
                duration_score=0.0,
                recommendations=[f"Quality estimation failed: {e!s}"],
                estimated_clone_fidelity="unknown",
            )

    async def extract_speaker_embedding(
        self,
        audio_path: str,
        use_cache: bool = True,
    ) -> tuple[np.ndarray | None, dict[str, Any]]:
        """
        Extract speaker embedding for rapid voice cloning.

        Phase 9.1.2: Speaker embedding extraction

        Args:
            audio_path: Path to audio file
            use_cache: Whether to use cached embeddings

        Returns:
            Tuple of (embedding vector, metadata dict)
        """
        if not self._initialized:
            await self.initialize()

        # Check cache
        cache_key = self._get_cache_key(audio_path)
        if use_cache and cache_key in self._embedding_cache:
            logger.debug(f"Using cached embedding for {audio_path}")
            return self._embedding_cache[cache_key], {"cached": True}

        if not self._speaker_encoder:
            logger.error("Speaker encoder not available")
            return None, {"error": "Speaker encoder not available"}

        try:
            start_time = time.perf_counter()

            # Extract embedding
            embedding = self._speaker_encoder.extract_embedding(
                audio_path,
                use_cache=True,
                normalize=True,
                extract_features=True,
            )

            processing_time = (time.perf_counter() - start_time) * 1000

            if embedding is not None:
                # Cache the embedding
                self._embedding_cache[cache_key] = embedding

                return embedding, {
                    "cached": False,
                    "processing_time_ms": processing_time,
                    "embedding_dim": len(embedding),
                    "backend": self._speaker_encoder.backend,
                }
            else:
                return None, {"error": "Failed to extract embedding"}

        except Exception as e:
            logger.error(f"Failed to extract speaker embedding: {e}")
            return None, {"error": str(e)}

    async def generate_instant_preview(
        self,
        audio_path: str,
        preview_text: str = "Hello, this is a preview of the cloned voice.",
        engine: str = "xtts",
    ) -> tuple[str | None, dict[str, Any]]:
        """
        Generate instant preview synthesis from reference audio.

        Phase 9.1.3: Instant preview synthesis

        Args:
            audio_path: Path to reference audio
            preview_text: Text to synthesize for preview
            engine: Synthesis engine to use

        Returns:
            Tuple of (preview audio path, metadata dict)
        """
        if not self._initialized:
            await self.initialize()

        try:
            start_time = time.perf_counter()

            # Import synthesis engine

            # Generate preview using zero-shot cloning
            preview_audio = await asyncio.get_event_loop().run_in_executor(
                None, lambda: self._synthesize_preview(audio_path, preview_text, engine)
            )

            processing_time = (time.perf_counter() - start_time) * 1000

            if preview_audio:
                return preview_audio, {
                    "processing_time_ms": processing_time,
                    "engine": engine,
                    "preview_text": preview_text,
                }
            else:
                return None, {"error": "Failed to generate preview"}

        except Exception as e:
            logger.error(f"Failed to generate instant preview: {e}")
            return None, {"error": str(e)}

    def _synthesize_preview(
        self,
        reference_audio: str,
        text: str,
        engine: str,
    ) -> str | None:
        """Synchronous preview synthesis helper."""
        try:
            import tempfile
            import uuid

            # Create preview output path
            preview_id = str(uuid.uuid4())[:8]
            output_dir = Path(tempfile.gettempdir()) / "voicestudio" / "previews"
            output_dir.mkdir(parents=True, exist_ok=True)
            output_path = output_dir / f"preview_{preview_id}.wav"

            # Use XTTS or Chatterbox for zero-shot synthesis
            if engine == "xtts":
                from app.core.engines.xtts_engine import XTTSEngine

                synth_engine = XTTSEngine()
                synth_engine.initialize()
                result = synth_engine.synthesize(
                    text=text,
                    speaker_wav=reference_audio,
                    output_path=str(output_path),
                )
                if result:
                    return str(output_path)
            elif engine == "chatterbox":
                from app.core.engines.chatterbox_engine import ChatterboxEngine

                synth_engine = ChatterboxEngine()
                synth_engine.initialize()
                result = synth_engine.synthesize(
                    text=text,
                    reference_audio=reference_audio,
                    output_path=str(output_path),
                )
                if result:
                    return str(output_path)

            return None

        except Exception as e:
            logger.error(f"Preview synthesis failed: {e}")
            return None

    async def instant_clone(
        self,
        audio_path: str,
        profile_name: str,
        profile_description: str | None = None,
        generate_preview: bool = True,
        engine: str = "xtts",
    ) -> InstantCloningResult:
        """
        Perform instant voice cloning from minimal audio sample.

        Phase 9.1.1: Zero-shot cloning enhancement

        Args:
            audio_path: Path to reference audio (6-10 seconds)
            profile_name: Name for the cloned voice profile
            profile_description: Optional description
            generate_preview: Whether to generate preview audio
            engine: Synthesis engine to use

        Returns:
            InstantCloningResult with cloning outcome
        """
        if not self._initialized:
            await self.initialize()

        start_time = time.perf_counter()

        try:
            # Step 1: Estimate quality
            quality_estimate = await self.estimate_clone_quality(audio_path)

            if quality_estimate.overall_score < 0.3:
                return InstantCloningResult(
                    success=False,
                    profile_id=None,
                    embedding_vector=None,
                    quality_estimate=quality_estimate,
                    preview_audio_path=None,
                    processing_time_ms=(time.perf_counter() - start_time) * 1000,
                    error_message="Audio quality too low for voice cloning",
                )

            # Step 2: Extract speaker embedding
            embedding, emb_metadata = await self.extract_speaker_embedding(audio_path)

            if embedding is None:
                return InstantCloningResult(
                    success=False,
                    profile_id=None,
                    embedding_vector=None,
                    quality_estimate=quality_estimate,
                    preview_audio_path=None,
                    processing_time_ms=(time.perf_counter() - start_time) * 1000,
                    error_message=emb_metadata.get("error", "Failed to extract embedding"),
                )

            # Step 3: Create voice profile
            import uuid

            profile_id = str(uuid.uuid4())

            # Step 4: Generate preview if requested
            preview_path = None
            if generate_preview:
                preview_path, _ = await self.generate_instant_preview(
                    audio_path,
                    preview_text="This is a preview of the cloned voice.",
                    engine=engine,
                )

            processing_time = (time.perf_counter() - start_time) * 1000

            return InstantCloningResult(
                success=True,
                profile_id=profile_id,
                embedding_vector=embedding,
                quality_estimate=quality_estimate,
                preview_audio_path=preview_path,
                processing_time_ms=processing_time,
            )

        except Exception as e:
            logger.error(f"Instant clone failed: {e}")
            return InstantCloningResult(
                success=False,
                profile_id=None,
                embedding_vector=None,
                quality_estimate=None,
                preview_audio_path=None,
                processing_time_ms=(time.perf_counter() - start_time) * 1000,
                error_message=str(e),
            )

    def _get_cache_key(self, file_path: str) -> str:
        """Generate cache key for file."""
        try:
            path = Path(file_path)
            if path.exists():
                stat = path.stat()
                key_string = f"{path.absolute()}_{stat.st_mtime}"
                return hashlib.md5(key_string.encode()).hexdigest()
        except Exception as e:
            logger.debug("Cache key generation using path stats failed: %s", e)
        return hashlib.md5(file_path.encode()).hexdigest()

    def clear_cache(self):
        """Clear all caches."""
        self._embedding_cache.clear()
        self._quality_cache.clear()
        logger.info("Instant cloning caches cleared")


# Singleton instance
_instant_cloning_service: InstantCloningService | None = None


def get_instant_cloning_service() -> InstantCloningService:
    """Get or create the instant cloning service singleton."""
    global _instant_cloning_service
    if _instant_cloning_service is None:
        _instant_cloning_service = InstantCloningService()
    return _instant_cloning_service
