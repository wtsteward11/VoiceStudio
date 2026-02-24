"""
Enhanced Ensemble Router Module for VoiceStudio
Intelligent multi-engine routing and ensemble synthesis

Compatible with:
- Python 3.10+
- numpy>=1.26.0
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Import engine router (conditional to avoid circular imports)
HAS_ENGINE_ROUTER = False
EngineRouter = None
global_router = None

try:
    import importlib.util

    # Try to import router module directly
    spec = importlib.util.find_spec("app.core.engines.router")
    if spec is not None:
        router_module = importlib.util.module_from_spec(spec)
        try:
            if spec.loader is not None:
                spec.loader.exec_module(router_module)
            EngineRouter = router_module.EngineRouter
            global_router = getattr(router_module, "router", None)
            HAS_ENGINE_ROUTER = True
        except Exception as e:
            logger.warning(f"Failed to load engine router: {e}")
except Exception as e:
    logger.warning(f"Engine router not available: {e}")

# Import quality metrics
try:
    from .enhanced_quality_metrics import EnhancedQualityMetrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False
    logger.warning("Enhanced quality metrics not available")

# Import audio utilities
try:
    from .audio_utils import resample_audio

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    logger.warning("audio_utils not available")


class EnhancedEnsembleRouter:
    """
    Enhanced Ensemble Router for intelligent multi-engine synthesis.

    Supports:
    - Intelligent engine selection based on quality requirements
    - Multi-engine ensemble synthesis
    - Quality-based routing
    - Segment-level ensemble (best segments from different engines)
    - Weighted fusion of multiple engines
    - Adaptive engine selection
    """

    def __init__(
        self,
        engine_router: Any | None = None,
        sample_rate: int = 24000,
    ):
        """
        Initialize Enhanced Ensemble Router.

        Args:
            engine_router: Engine router instance (uses global if None)
            sample_rate: Default sample rate for processing
        """
        self.engine_router = engine_router or (global_router if HAS_ENGINE_ROUTER else None)
        self.sample_rate = sample_rate
        self.quality_metrics = None

        if HAS_QUALITY_METRICS:
            try:
                self.quality_metrics = EnhancedQualityMetrics(sample_rate=sample_rate)
            except Exception as e:
                logger.warning(f"Failed to initialize quality metrics: {e}")

    def select_best_engine(
        self,
        task_type: str = "tts",
        quality_requirements: dict | None = None,
        prefer_speed: bool = False,
    ) -> str | None:
        """
        Select the best engine for a task based on quality requirements.

        Args:
            task_type: Task type (e.g., "tts")
            quality_requirements: Quality requirements dictionary:
                - min_mos_score: Minimum MOS score
                - min_similarity: Minimum similarity
                - min_naturalness: Minimum naturalness
                - quality_tier: Quality tier preference
            prefer_speed: If True, prefer faster engines

        Returns:
            Best engine name or None
        """
        if not self.engine_router:
            logger.warning("Engine router not available")
            return None

        if quality_requirements is None:
            quality_requirements = {}

        try:
            engine = self.engine_router.select_engine_by_quality(
                task_type=task_type,
                min_mos_score=quality_requirements.get("min_mos_score"),
                min_similarity=quality_requirements.get("min_similarity"),
                min_naturalness=quality_requirements.get("min_naturalness"),
                prefer_speed=prefer_speed,
                quality_tier=quality_requirements.get("quality_tier"),
            )

            if engine:
                for name, eng in self.engine_router._engines.items():
                    if eng == engine:
                        return str(name)
                for name in self.engine_router.list_engines():
                    if self.engine_router.get_engine(name) == engine:
                        return str(name)

            return None

        except Exception as e:
            logger.warning(f"Engine selection failed: {e}")
            # Fallback: return first available engine
            engines = self.engine_router.list_engines()
            return engines[0] if engines else None

    def synthesize_ensemble(
        self,
        text: str,
        speaker_wav: str | list[str],
        engines: list[str],
        language: str = "en",
        selection_mode: str = "voting",
        fusion_strategy: str | None = None,
        segment_size: float = 0.5,
        quality_threshold: float = 0.85,
    ) -> tuple[np.ndarray | None, dict]:
        """
        Synthesize using multiple engines and combine results.

        Args:
            text: Text to synthesize
            speaker_wav: Speaker reference audio
            engines: List of engine names to use
            language: Language code
            selection_mode: Selection mode ("voting", "hybrid", "fusion")
            fusion_strategy: Fusion strategy ("quality_weighted", "equal", "best_segment")
            segment_size: Segment size in seconds for hybrid mode
            quality_threshold: Minimum quality threshold

        Returns:
            Tuple of (ensemble_audio, metrics_dict)
        """
        if not self.engine_router:
            logger.error("Engine router not available")
            return None, {"error": "Engine router not available"}

        # Synthesize with all engines
        engine_outputs = {}
        engine_qualities = {}
        engine_audios = {}
        sample_rates = {}

        for engine_name in engines:
            try:
                engine = self.engine_router.get_engine(engine_name)
                if not engine:
                    logger.warning(f"Engine {engine_name} not available")
                    continue

                # Synthesize
                audio = engine.synthesize(
                    text=text,
                    speaker_wav=speaker_wav,
                    language=language,
                )

                if audio is None:
                    logger.warning(f"Engine {engine_name} returned None")
                    continue

                # Convert to numpy array if needed
                if not isinstance(audio, np.ndarray):
                    audio = np.array(audio)

                engine_audios[engine_name] = audio
                sample_rates[engine_name] = getattr(engine, "sample_rate", self.sample_rate)

                # Calculate quality metrics
                if self.quality_metrics:
                    try:
                        quality = self.quality_metrics.calculate_all(
                            audio, sample_rates[engine_name], include_advanced=False
                        )
                        engine_qualities[engine_name] = quality
                        raw_score = quality.get("overall_quality_score", 0.0)
                        quality_score = (
                            float(raw_score) if isinstance(raw_score, (int, float)) else 0.0
                        )
                        if quality_score < quality_threshold:
                            logger.warning(
                                f"Engine {engine_name} quality {quality_score:.3f} below threshold {quality_threshold}"
                            )
                    except Exception as e:
                        logger.warning(f"Quality calculation failed for {engine_name}: {e}")

                engine_outputs[engine_name] = {
                    "audio": audio,
                    "sample_rate": sample_rates[engine_name],
                }

            except Exception as e:
                logger.error(f"Engine {engine_name} synthesis failed: {e}")
                continue

        if not engine_outputs:
            return None, {"error": "All engines failed"}

        # Combine outputs based on selection mode
        if selection_mode == "voting":
            return self._voting_mode(engine_outputs, engine_qualities)
        elif selection_mode == "hybrid":
            return self._hybrid_mode(engine_outputs, engine_qualities, segment_size)
        elif selection_mode == "fusion":
            return self._fusion_mode(engine_outputs, engine_qualities, fusion_strategy)
        else:
            logger.warning(f"Unknown selection mode: {selection_mode}, using voting")
            return self._voting_mode(engine_outputs, engine_qualities)

    def _voting_mode(
        self, engine_outputs: dict, engine_qualities: dict
    ) -> tuple[np.ndarray | None, dict]:
        """Select best engine output based on quality voting."""
        best_engine = None
        best_quality = -1.0

        for engine_name, _output in engine_outputs.items():
            quality = engine_qualities.get(engine_name, {})
            quality_score = quality.get("overall_quality_score", 0.0)

            if quality_score > best_quality:
                best_quality = quality_score
                best_engine = engine_name

        if best_engine:
            result = engine_outputs[best_engine]
            return result["audio"], {
                "selected_engine": best_engine,
                "quality_score": best_quality,
                "mode": "voting",
            }

        # Fallback: return first engine
        first_engine = next(iter(engine_outputs.keys()))
        result = engine_outputs[first_engine]
        return result["audio"], {
            "selected_engine": first_engine,
            "mode": "voting",
        }

    def _hybrid_mode(
        self,
        engine_outputs: dict,
        engine_qualities: dict,
        segment_size: float,
    ) -> tuple[np.ndarray | None, dict]:
        """Select best segments from different engines."""
        # Get target sample rate (use first engine's rate)
        target_sr = next(iter(engine_outputs.values()))["sample_rate"]

        # Resample all to same rate
        engine_audios_resampled = {}
        for engine_name, output in engine_outputs.items():
            audio = output["audio"]
            sr = output["sample_rate"]

            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)  # Convert to mono

            if sr != target_sr and HAS_AUDIO_UTILS:
                audio = resample_audio(audio, sr, target_sr)
            elif sr != target_sr:
                # Simple resampling fallback
                from scipy import signal

                num_samples = int(len(audio) * target_sr / sr)
                audio = signal.resample(audio, num_samples)

            engine_audios_resampled[engine_name] = audio

        # Find max length
        max_length = max(len(audio) for audio in engine_audios_resampled.values())
        segment_size_samples = int(segment_size * target_sr)

        # Select best segments
        segments = []
        for i in range(0, max_length, segment_size_samples):
            segment_end = min(i + segment_size_samples, max_length)
            best_segment = None
            best_quality = -1.0

            for engine_name, audio in engine_audios_resampled.items():
                if i < len(audio):
                    segment = audio[i:segment_end]
                    quality = engine_qualities.get(engine_name, {})
                    quality_score = quality.get("overall_quality_score", 0.0)

                    if quality_score > best_quality:
                        best_quality = quality_score
                        best_segment = segment

            if best_segment is not None:
                segments.append(best_segment)

        if segments:
            combined = np.concatenate(segments)
            return combined, {
                "mode": "hybrid",
                "num_segments": len(segments),
                "segment_size": segment_size,
            }

        return None, {"error": "Failed to create hybrid audio"}

    def _fusion_mode(
        self,
        engine_outputs: dict,
        engine_qualities: dict,
        fusion_strategy: str | None,
    ) -> tuple[np.ndarray | None, dict]:
        """Fuse multiple engine outputs with weighted combination."""
        if fusion_strategy is None:
            fusion_strategy = "quality_weighted"

        # Get target sample rate
        target_sr = next(iter(engine_outputs.values()))["sample_rate"]

        # Resample and prepare all audios
        engine_audios_resampled = {}
        engine_weights = {}

        for engine_name, output in engine_outputs.items():
            audio = output["audio"]
            sr = output["sample_rate"]

            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)  # Convert to mono

            if sr != target_sr and HAS_AUDIO_UTILS:
                audio = resample_audio(audio, sr, target_sr)
            elif sr != target_sr:
                from scipy import signal

                num_samples = int(len(audio) * target_sr / sr)
                audio = signal.resample(audio, num_samples)

            engine_audios_resampled[engine_name] = audio

            # Calculate weight
            if fusion_strategy == "quality_weighted":
                quality = engine_qualities.get(engine_name, {})
                quality_score = quality.get("overall_quality_score", 0.5)
                engine_weights[engine_name] = max(0.0, quality_score)
            elif fusion_strategy == "equal":
                engine_weights[engine_name] = 1.0
            else:
                engine_weights[engine_name] = 1.0

        # Normalize weights
        total_weight = sum(engine_weights.values())
        if total_weight > 0:
            engine_weights = {k: v / total_weight for k, v in engine_weights.items()}
        else:
            engine_weights = {
                k: 1.0 / len(engine_audios_resampled) for k in engine_audios_resampled
            }

        # Find max length and fuse
        max_length = max(len(audio) for audio in engine_audios_resampled.values())
        fused_audio = np.zeros(max_length)

        for engine_name, audio in engine_audios_resampled.items():
            # Pad or truncate to max_length
            if len(audio) < max_length:
                audio = np.pad(audio, (0, max_length - len(audio)), mode="constant")
            elif len(audio) > max_length:
                audio = audio[:max_length]

            # Weighted sum
            weight = engine_weights[engine_name]
            fused_audio += audio * weight

        # Normalize to prevent clipping
        max_amp = np.max(np.abs(fused_audio))
        if max_amp > 0.95:
            fused_audio = fused_audio * (0.95 / max_amp)

        return fused_audio, {
            "mode": "fusion",
            "fusion_strategy": fusion_strategy,
            "weights": engine_weights,
        }


def create_enhanced_ensemble_router(
    engine_router: Any | None = None,
    sample_rate: int = 24000,
) -> EnhancedEnsembleRouter:
    """
    Factory function to create an Enhanced Ensemble Router instance.

    Args:
        engine_router: Engine router instance (uses global if None)
        sample_rate: Default sample rate for processing

    Returns:
        Initialized EnhancedEnsembleRouter instance
    """
    return EnhancedEnsembleRouter(engine_router=engine_router, sample_rate=sample_rate)
