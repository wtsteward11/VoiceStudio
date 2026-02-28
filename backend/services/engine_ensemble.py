"""
Engine Ensemble System.

Task 4.4.5: Combine multiple TTS engines for optimal output.
Enables quality-based engine selection and output blending.
"""

from __future__ import annotations

import asyncio
import logging
from collections.abc import Callable
from dataclasses import dataclass, field
from enum import Enum
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


class SelectionStrategy(Enum):
    """Engine selection strategies."""

    QUALITY_FIRST = "quality_first"  # Best quality regardless of speed
    SPEED_FIRST = "speed_first"  # Fastest engine
    BALANCED = "balanced"  # Balance quality and speed
    ENSEMBLE = "ensemble"  # Combine multiple outputs
    FALLBACK = "fallback"  # Try engines in order until success


@dataclass
class EngineScore:
    """Score for an engine."""

    engine_id: str
    quality_score: float = 0.5  # 0-1, higher is better
    speed_score: float = 0.5  # 0-1, higher is faster
    memory_usage_mb: float = 0.0
    last_latency_ms: float = 0.0
    success_rate: float = 1.0

    @property
    def balanced_score(self) -> float:
        return (self.quality_score + self.speed_score + self.success_rate) / 3


@dataclass
class EnsembleConfig:
    """Configuration for engine ensemble."""

    strategy: SelectionStrategy = SelectionStrategy.BALANCED
    engines: list[str] = field(default_factory=list)
    quality_threshold: float = 0.7
    max_retries: int = 2
    parallel_inference: bool = False


class EngineEnsemble:
    """
    Ensemble system for combining multiple TTS engines.

    Features:
    - Automatic engine selection
    - Quality-based routing
    - Fallback handling
    - Parallel inference for ensemble
    - Adaptive scoring
    """

    def __init__(self, config: EnsembleConfig | None = None):
        self.config = config or EnsembleConfig()
        self._engine_registry: dict[str, Any] = {}
        self._engine_scores: dict[str, EngineScore] = {}
        self._quality_evaluator: Callable | None = None

    def register_engine(
        self,
        engine_id: str,
        engine: Any,
        initial_quality: float = 0.5,
        initial_speed: float = 0.5,
    ) -> None:
        """Register an engine with the ensemble."""
        self._engine_registry[engine_id] = engine
        self._engine_scores[engine_id] = EngineScore(
            engine_id=engine_id,
            quality_score=initial_quality,
            speed_score=initial_speed,
        )
        logger.info(f"Registered engine: {engine_id}")

    def unregister_engine(self, engine_id: str) -> None:
        """Remove an engine from the ensemble."""
        self._engine_registry.pop(engine_id, None)
        self._engine_scores.pop(engine_id, None)

    def set_quality_evaluator(
        self,
        evaluator: Callable[[np.ndarray, int], float],
    ) -> None:
        """Set quality evaluation function."""
        self._quality_evaluator = evaluator

    async def synthesize(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """
        Synthesize using ensemble strategy.

        Args:
            text: Text to synthesize
            **kwargs: Additional arguments for engines

        Returns:
            Tuple of (audio, engine_id_used)
        """
        strategy = self.config.strategy

        if strategy == SelectionStrategy.QUALITY_FIRST:
            return await self._synthesize_quality_first(text, **kwargs)
        elif strategy == SelectionStrategy.SPEED_FIRST:
            return await self._synthesize_speed_first(text, **kwargs)
        elif strategy == SelectionStrategy.ENSEMBLE:
            return await self._synthesize_ensemble(text, **kwargs)
        elif strategy == SelectionStrategy.FALLBACK:
            return await self._synthesize_fallback(text, **kwargs)
        else:
            return await self._synthesize_balanced(text, **kwargs)

    async def _synthesize_quality_first(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """Select engine with highest quality score."""
        sorted_engines = sorted(
            self._engine_scores.values(),
            key=lambda x: x.quality_score,
            reverse=True,
        )

        for score in sorted_engines:
            engine = self._engine_registry.get(score.engine_id)
            if engine is None:
                continue

            try:
                audio = await self._run_engine(engine, text, **kwargs)
                return audio, score.engine_id
            except Exception as e:
                logger.warning(f"Engine {score.engine_id} failed: {e}")
                self._update_score_on_failure(score.engine_id)

        raise RuntimeError("All engines failed")

    async def _synthesize_speed_first(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """Select fastest engine."""
        sorted_engines = sorted(
            self._engine_scores.values(),
            key=lambda x: x.speed_score,
            reverse=True,
        )

        for score in sorted_engines:
            engine = self._engine_registry.get(score.engine_id)
            if engine is None:
                continue

            try:
                audio = await self._run_engine(engine, text, **kwargs)
                return audio, score.engine_id
            except Exception as e:
                logger.warning(f"Engine {score.engine_id} failed: {e}")

        raise RuntimeError("All engines failed")

    async def _synthesize_balanced(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """Select engine with best balanced score."""
        sorted_engines = sorted(
            self._engine_scores.values(),
            key=lambda x: x.balanced_score,
            reverse=True,
        )

        for score in sorted_engines:
            engine = self._engine_registry.get(score.engine_id)
            if engine is None:
                continue

            try:
                audio = await self._run_engine(engine, text, **kwargs)
                return audio, score.engine_id
            except Exception as e:
                logger.warning(f"Engine {score.engine_id} failed: {e}")

        raise RuntimeError("All engines failed")

    async def _synthesize_fallback(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """Try engines in configured order."""
        for engine_id in self.config.engines:
            engine = self._engine_registry.get(engine_id)
            if engine is None:
                continue

            retries = 0
            while retries < self.config.max_retries:
                try:
                    audio = await self._run_engine(engine, text, **kwargs)
                    return audio, engine_id
                except Exception as e:
                    logger.warning(f"Engine {engine_id} attempt {retries + 1} failed: {e}")
                    retries += 1

        raise RuntimeError("All engines failed")

    async def _synthesize_ensemble(
        self,
        text: str,
        **kwargs,
    ) -> tuple[np.ndarray, str]:
        """Run multiple engines and blend outputs."""
        tasks = []
        engine_ids = []

        for engine_id in self.config.engines[:3]:  # Max 3 for ensemble
            engine = self._engine_registry.get(engine_id)
            if engine is not None:
                tasks.append(self._run_engine_safe(engine, text, **kwargs))
                engine_ids.append(engine_id)

        if not tasks:
            raise RuntimeError("No engines available")

        # Run in parallel
        results = await asyncio.gather(*tasks)

        # Filter successful results
        successful = [
            (audio, engine_id) for audio, engine_id in zip(results, engine_ids) if audio is not None
        ]

        if not successful:
            raise RuntimeError("All engines failed")

        if len(successful) == 1:
            return successful[0]

        # Blend outputs (simple averaging)
        audios = [s[0] for s in successful]

        # Align lengths
        min_len = min(len(a) for a in audios)
        aligned = [a[:min_len] for a in audios]

        # Average
        blended = np.mean(aligned, axis=0)

        return blended, "ensemble"

    async def _run_engine(self, engine: Any, text: str, **kwargs) -> np.ndarray:
        """Run a single engine."""
        import time

        start = time.time()

        if hasattr(engine, "synthesize"):
            audio = await engine.synthesize(text, **kwargs)
        elif hasattr(engine, "tts"):
            audio = await asyncio.to_thread(engine.tts, text, **kwargs)
        else:
            raise AttributeError("Engine has no synthesize or tts method")

        latency = (time.time() - start) * 1000

        # Update scores
        engine_id = getattr(engine, "ENGINE_ID", str(id(engine)))
        if engine_id in self._engine_scores:
            self._update_score_on_success(engine_id, latency, audio)

        return audio

    async def _run_engine_safe(
        self,
        engine: Any,
        text: str,
        **kwargs,
    ) -> np.ndarray | None:
        """Run engine, returning None on failure."""
        try:
            return await self._run_engine(engine, text, **kwargs)
        except Exception as e:
            logger.debug(f"Engine failed: {e}")
            return None

    def _update_score_on_success(
        self,
        engine_id: str,
        latency_ms: float,
        audio: np.ndarray,
    ) -> None:
        """Update engine score after successful synthesis."""
        score = self._engine_scores.get(engine_id)
        if score is None:
            return

        score.last_latency_ms = latency_ms

        # Update speed score based on latency
        speed = 1.0 - min(latency_ms / 5000, 0.9)  # Cap at 5s
        score.speed_score = score.speed_score * 0.9 + speed * 0.1

        # Update success rate
        score.success_rate = min(1.0, score.success_rate * 0.95 + 0.05)

        # Update quality if evaluator available
        if self._quality_evaluator is not None:
            try:
                quality = self._quality_evaluator(audio, 22050)  # Assume 22050 Hz
                score.quality_score = score.quality_score * 0.9 + quality * 0.1
            except Exception as e:
                logger.debug("Quality evaluator failed: %s", e)

    def _update_score_on_failure(self, engine_id: str) -> None:
        """Update engine score after failure."""
        score = self._engine_scores.get(engine_id)
        if score is not None:
            score.success_rate *= 0.8

    def get_engine_scores(self) -> dict[str, EngineScore]:
        """Get current engine scores."""
        return self._engine_scores.copy()

    def get_best_engine(self, strategy: SelectionStrategy | None = None) -> str | None:
        """Get ID of best engine according to strategy."""
        strat = strategy or self.config.strategy

        if not self._engine_scores:
            return None

        if strat == SelectionStrategy.QUALITY_FIRST:
            best = max(self._engine_scores.values(), key=lambda x: x.quality_score)
        elif strat == SelectionStrategy.SPEED_FIRST:
            best = max(self._engine_scores.values(), key=lambda x: x.speed_score)
        else:
            best = max(self._engine_scores.values(), key=lambda x: x.balanced_score)

        return best.engine_id
