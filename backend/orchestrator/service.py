"""
OrchestrationService — Phase X-A Core Engine

Composes EngineRouter, QualityMetrics, and EnhancedJobQueue into a
self-optimizing voice production pipeline.

State machine:
  Queued -> SelectingEngine -> Synthesizing -> EvaluatingQuality
  -> (Completed | AdaptiveRetry -> Synthesizing | Enhancing -> EvaluatingQuality)
  -> (Failed if FailIfUnmet or no fallback)
"""

from __future__ import annotations

import logging
import random
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any

from .events import OrchestrationEventEmitter
from .schemas import (
    EngineExecutionPlan,
    OrchestrationQualityMetrics,
    OrchestrationRequest,
    OrchestrationResponse,
    OrchestrationStatus,
    OrchestrationStatusResponse,
    OrchestrationStrategy,
    ParameterAdjustment,
    ParameterMutationStrategy,
    PipelineStep,
    PipelineStepType,
    ProductionChain,
    QualityThresholdPolicy,
    RetryPolicy,
)

logger = logging.getLogger(__name__)

_OUTPUT_DIR = Path(tempfile.gettempdir()) / "voicestudio_orchestrator"
_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


class _JobState:
    """Mutable in-memory state for a running orchestration job."""

    def __init__(self, job_id: str, request: OrchestrationRequest) -> None:
        self.job_id = job_id
        self.request = request
        self.status = OrchestrationStatus.QUEUED
        self.progress: float = 0.0
        self.current_step: str | None = None
        self.engine_used: str | None = None
        self.retry_count: int = 0
        self.started_at: float = time.time()
        self.audio_path: str | None = None
        self.quality_metrics: OrchestrationQualityMetrics | None = None
        self.enhancements: list[str] = []
        self.execution_plan = EngineExecutionPlan(selected_engine="")
        self.cancelled = False


class OrchestrationService:
    """
    Autonomous voice production orchestrator.

    Integrates with:
    - EngineRouter for engine selection and fallback
    - QualityMetrics for quality evaluation
    - Event emitter for real-time WebSocket updates
    """

    def __init__(self) -> None:
        self._jobs: dict[str, _JobState] = {}
        self._emitter = OrchestrationEventEmitter()
        self._engine_router: Any = None
        self._initialized = False

    @property
    def emitter(self) -> OrchestrationEventEmitter:
        return self._emitter

    def _ensure_router(self) -> Any:
        """Lazy-load the engine router at first use."""
        if self._engine_router is not None:
            return self._engine_router
        try:
            from app.core.engines.router import EngineRouter

            self._engine_router = EngineRouter()
            engines_root = str(Path(__file__).resolve().parents[2] / "engines")
            self._engine_router.load_all_engines(engines_root)
            self._initialized = True
        except Exception:
            logger.exception("Failed to initialize EngineRouter")
            self._engine_router = None
        return self._engine_router

    def run_sync(self, request: OrchestrationRequest) -> OrchestrationResponse:
        """Execute orchestration synchronously (async_mode=false)."""
        job_id = str(uuid.uuid4())
        state = _JobState(job_id, request)
        self._jobs[job_id] = state

        self._emitter.job_queued(job_id, strategy=request.strategy.value)

        try:
            self._execute(state)
        except Exception as exc:
            state.status = OrchestrationStatus.FAILED
            self._emitter.job_failed(job_id, str(exc))
            logger.exception("Orchestration failed for job %s", job_id)

        return self._build_response(state)

    def submit_async(self, request: OrchestrationRequest) -> OrchestrationResponse:
        """
        Submit an orchestration job for background execution.

        Returns immediately with status=QUEUED and a job_id.
        Actual execution is delegated to the job scheduler.
        """
        job_id = str(uuid.uuid4())
        state = _JobState(job_id, request)
        self._jobs[job_id] = state

        self._emitter.job_queued(
            job_id,
            strategy=request.strategy.value,
            priority=request.priority.value,
        )

        return self._build_response(state)

    def execute_job(self, job_id: str) -> OrchestrationResponse:
        """Execute a previously submitted async job (called by scheduler)."""
        state = self._jobs.get(job_id)
        if state is None:
            return OrchestrationResponse(
                job_id=job_id, status=OrchestrationStatus.FAILED
            )
        try:
            self._execute(state)
        except Exception as exc:
            state.status = OrchestrationStatus.FAILED
            self._emitter.job_failed(job_id, str(exc))
            logger.exception("Orchestration failed for job %s", job_id)

        return self._build_response(state)

    def get_status(self, job_id: str) -> OrchestrationStatusResponse | None:
        state = self._jobs.get(job_id)
        if state is None:
            return None
        elapsed = (time.time() - state.started_at) * 1000
        return OrchestrationStatusResponse(
            job_id=job_id,
            status=state.status,
            progress_percent=state.progress,
            current_step=state.current_step,
            engine_active=state.engine_used,
            estimated_remaining_ms=max(0, elapsed * (1.0 / max(state.progress, 0.01) - 1))
            if state.progress > 0
            else None,
            started_at=str(state.started_at),
            retry_count=state.retry_count,
        )

    def cancel(self, job_id: str) -> bool:
        state = self._jobs.get(job_id)
        if state is None:
            return False
        state.cancelled = True
        state.status = OrchestrationStatus.CANCELLED
        return True

    def get_debug_info(self, job_id: str) -> EngineExecutionPlan | None:
        state = self._jobs.get(job_id)
        return state.execution_plan if state else None

    # ------------------------------------------------------------------
    # Core execution loop
    # ------------------------------------------------------------------

    def _execute(self, state: _JobState) -> None:
        request = state.request
        _ = request.production_chain or self._build_default_chain(request.strategy)
        quality_policy = request.target_quality
        retry_policy = request.retry_policy

        state.status = OrchestrationStatus.SELECTING_ENGINE
        state.current_step = "selecting_engine"
        state.progress = 0.05

        engine_id = self._select_engine(state, request)
        if engine_id is None:
            state.status = OrchestrationStatus.FAILED
            self._emitter.job_failed(state.job_id, "No engine available")
            return

        state.engine_used = engine_id
        state.execution_plan.selected_engine = engine_id
        self._emitter.engine_selected(state.job_id, engine_id)

        best_audio_path: str | None = None
        best_metrics: OrchestrationQualityMetrics | None = None
        attempt = 0

        while attempt < retry_policy.max_attempts:
            if state.cancelled:
                return

            attempt += 1
            state.retry_count = attempt - 1

            # --- Synthesis ---
            state.status = OrchestrationStatus.SYNTHESIZING
            state.current_step = "synthesizing"
            state.progress = 0.1 + (attempt - 1) * 0.2

            self._emitter.synthesis_started(state.job_id, engine_id, attempt=attempt)

            synth_start = time.time()
            audio_path = self._synthesize(state, engine_id, request)
            synth_ms = (time.time() - synth_start) * 1000

            self._emitter.synthesis_completed(state.job_id, synth_ms)

            if audio_path is None:
                if retry_policy.fallback_engines_enabled:
                    fallback = self._get_fallback_engine(state, engine_id)
                    if fallback:
                        state.execution_plan.fallback_engines.append(fallback)
                        engine_id = fallback
                        self._emitter.engine_selected(state.job_id, engine_id)
                        continue
                state.status = OrchestrationStatus.FAILED
                self._emitter.job_failed(state.job_id, "Synthesis failed")
                return

            # --- Quality Evaluation ---
            state.status = OrchestrationStatus.EVALUATING_QUALITY
            state.current_step = "evaluating_quality"
            state.progress = 0.2 + (attempt - 1) * 0.2

            metrics = self._evaluate_quality(audio_path, request.reference_audio_path)
            passed = self._quality_meets_threshold(metrics, quality_policy)

            self._emitter.quality_evaluated(
                state.job_id,
                metrics.model_dump(exclude_none=True),
                passed,
            )

            if passed or attempt == retry_policy.max_attempts:
                best_audio_path = audio_path
                best_metrics = metrics
                if passed:
                    break
            else:
                # --- Adaptive Retry ---
                state.status = OrchestrationStatus.RETRYING
                adjustment = self._mutate_parameters(
                    state, metrics, retry_policy, attempt
                )
                state.execution_plan.parameter_adjustments.append(adjustment)
                self._emitter.retry_triggered(
                    state.job_id,
                    attempt,
                    f"Quality below threshold (MOS={metrics.mos_score})",
                )

        # --- Auto-Enhance if needed ---
        if (
            best_audio_path
            and best_metrics
            and not self._quality_meets_threshold(best_metrics, quality_policy)
            and quality_policy.auto_enhance_if_below
        ):
            state.status = OrchestrationStatus.ENHANCING
            state.current_step = "enhancing"
            state.progress = 0.85

            self._emitter.enhancement_started(state.job_id)
            enhanced_path, enhancements = self._auto_enhance(best_audio_path)
            if enhanced_path:
                best_audio_path = enhanced_path
                state.enhancements = enhancements
                best_metrics = self._evaluate_quality(
                    enhanced_path, request.reference_audio_path
                )
            self._emitter.enhancement_completed(
                state.job_id, enhancements=enhancements
            )

        # --- Final result ---
        if best_audio_path is None:
            state.status = OrchestrationStatus.FAILED
            self._emitter.job_failed(state.job_id, "No audio produced")
            return

        if (
            quality_policy.fail_if_unmet
            and best_metrics
            and not self._quality_meets_threshold(best_metrics, quality_policy)
        ):
            state.status = OrchestrationStatus.FAILED
            self._emitter.job_failed(
                state.job_id, "Quality threshold not met after all attempts"
            )
            return

        state.audio_path = best_audio_path
        state.quality_metrics = best_metrics
        state.status = OrchestrationStatus.COMPLETED
        state.progress = 1.0
        state.current_step = "completed"

        total_ms = (time.time() - state.started_at) * 1000
        self._emitter.job_completed(
            state.job_id,
            audio_url=best_audio_path,
            total_ms=total_ms,
            engine=engine_id,
        )

    # ------------------------------------------------------------------
    # Engine selection
    # ------------------------------------------------------------------

    def _select_engine(
        self, state: _JobState, request: OrchestrationRequest
    ) -> str | None:
        router = self._ensure_router()
        if router is None:
            return None

        strategy = request.strategy

        if strategy == OrchestrationStrategy.SPEED_FIRST:
            for fast_engine in ["piper", "espeak", "xtts_v2"]:
                if fast_engine in router.list_engines():
                    return fast_engine

        if strategy == OrchestrationStrategy.QUALITY_FIRST:
            for hq_engine in ["tortoise", "chatterbox", "xtts_v2"]:
                if hq_engine in router.list_engines():
                    return hq_engine

        try:
            engine, _ = router.select_engine_with_fallback("tts")
            if engine:
                return getattr(engine, "engine_id", None) or "xtts_v2"
        except Exception:
            logger.debug("Router select_engine_with_fallback unavailable")

        available = router.list_engines()
        return available[0] if available else None

    def _get_fallback_engine(self, state: _JobState, current: str) -> str | None:
        router = self._ensure_router()
        if router is None:
            return None
        fallback_chain = ["xtts_v2", "chatterbox", "piper", "espeak"]
        available = router.list_engines()
        for candidate in fallback_chain:
            if candidate != current and candidate in available:
                return candidate
        return None

    # ------------------------------------------------------------------
    # Synthesis
    # ------------------------------------------------------------------

    def _synthesize(
        self,
        state: _JobState,
        engine_id: str,
        request: OrchestrationRequest,
    ) -> str | None:
        router = self._ensure_router()
        if router is None:
            return None

        engine = router.get_engine(engine_id)
        if engine is None:
            return None

        output_path = str(
            _OUTPUT_DIR / f"{state.job_id}_{state.retry_count}.wav"
        )

        try:
            if hasattr(engine, "synthesize"):
                result = engine.synthesize(
                    text=request.text,
                    output_path=output_path,
                    language=request.language,
                )
                if result is not None:
                    return output_path
        except Exception:
            logger.exception(
                "Synthesis failed for engine %s, job %s", engine_id, state.job_id
            )
        return None

    # ------------------------------------------------------------------
    # Quality evaluation
    # ------------------------------------------------------------------

    def _evaluate_quality(
        self, audio_path: str, reference_path: str | None = None
    ) -> OrchestrationQualityMetrics:
        try:
            from app.core.engines.quality_metrics import calculate_all_metrics
            import numpy as np

            audio = np.zeros(16000, dtype=np.float32)
            try:
                import soundfile as sf

                audio, _ = sf.read(audio_path)
            except Exception as read_err:
                logger.warning("Could not read audio for quality eval: %s", read_err)

            ref = None
            if reference_path:
                try:
                    import soundfile as sf

                    ref, _ = sf.read(reference_path)
                except Exception as ref_err:
                    logger.warning("Could not read reference audio: %s", ref_err)

            raw = calculate_all_metrics(audio, ref)
            return OrchestrationQualityMetrics(
                mos_score=raw.get("mos_score"),
                similarity=raw.get("similarity"),
                naturalness=raw.get("naturalness"),
                snr_db=raw.get("snr_db"),
                artifact_score=raw.get("artifact_score"),
                has_clicks=raw.get("has_clicks"),
                has_distortion=raw.get("has_distortion"),
            )
        except Exception:
            logger.debug("Quality metrics calculation unavailable, using defaults")
            return OrchestrationQualityMetrics(mos_score=3.0)

    def _quality_meets_threshold(
        self,
        metrics: OrchestrationQualityMetrics,
        policy: QualityThresholdPolicy,
    ) -> bool:
        if metrics.mos_score is not None and metrics.mos_score < policy.min_mos:
            return False
        if (
            metrics.similarity is not None
            and metrics.similarity < policy.min_similarity
        ):
            return False
        if (
            metrics.naturalness is not None
            and metrics.naturalness < policy.min_naturalness
        ):
            return False
        if (
            metrics.artifact_score is not None
            and metrics.artifact_score > policy.max_artifact_score
        ):
            return False
        if metrics.snr_db is not None and metrics.snr_db < policy.min_snr_db:
            return False
        return True

    # ------------------------------------------------------------------
    # Adaptive retry
    # ------------------------------------------------------------------

    def _mutate_parameters(
        self,
        state: _JobState,
        metrics: OrchestrationQualityMetrics,
        retry_policy: RetryPolicy,
        attempt: int,
    ) -> ParameterAdjustment:
        strategy = retry_policy.parameter_mutation_strategy
        changes: dict[str, Any] = {}

        if strategy == ParameterMutationStrategy.QUALITY_GRADIENT:
            if metrics.mos_score and metrics.mos_score < 3.5:
                changes["temperature"] = max(0.3, 0.7 - attempt * 0.05)
            if metrics.naturalness and metrics.naturalness < 0.7:
                changes["speed"] = 1.0
            if metrics.artifact_score and metrics.artifact_score > 0.1:
                changes["enhance_quality"] = True
        elif strategy == ParameterMutationStrategy.RANDOM_VARIATION:
            changes["temperature"] = round(0.5 + random.uniform(-0.15, 0.15), 3)
            changes["speed"] = round(1.0 + random.uniform(-0.1, 0.1), 3)

        return ParameterAdjustment(
            attempt=attempt,
            changes=changes,
            reason=f"Adaptive {strategy.value} (attempt {attempt})",
        )

    # ------------------------------------------------------------------
    # Auto-enhance
    # ------------------------------------------------------------------

    def _auto_enhance(self, audio_path: str) -> tuple[str | None, list[str]]:
        enhancements: list[str] = []
        enhanced_path = audio_path
        try:
            from app.core.audio.pipeline_optimized import OptimizedAudioPipeline

            try:
                import soundfile as sf

                audio, sr = sf.read(audio_path)
            except Exception:
                return None, []

            pipeline = OptimizedAudioPipeline()
            if hasattr(pipeline, "process"):
                result = pipeline.process(audio, sr)
                if result is not None:
                    out = str(
                        _OUTPUT_DIR
                        / f"{Path(audio_path).stem}_enhanced.wav"
                    )
                    sf.write(out, result, sr)
                    enhanced_path = out
                    enhancements.append("pipeline_optimized")
        except ImportError:
            logger.debug("OptimizedAudioPipeline not available")
        except Exception:
            logger.exception("Auto-enhance failed")

        return enhanced_path, enhancements

    # ------------------------------------------------------------------
    # Default chain builder
    # ------------------------------------------------------------------

    def _build_default_chain(
        self, strategy: OrchestrationStrategy
    ) -> ProductionChain:
        steps = [
            PipelineStep(step_id="s1", type=PipelineStepType.SYNTHESIS),
            PipelineStep(step_id="s2", type=PipelineStepType.QUALITY_EVALUATION),
        ]

        if strategy in (
            OrchestrationStrategy.AUTO,
            OrchestrationStrategy.QUALITY_FIRST,
        ):
            steps.append(
                PipelineStep(step_id="s3", type=PipelineStepType.POST_PROCESSING)
            )

        return ProductionChain(
            name=f"Default ({strategy.value})",
            steps=steps,
        )

    # ------------------------------------------------------------------
    # Response builder
    # ------------------------------------------------------------------

    def _build_response(self, state: _JobState) -> OrchestrationResponse:
        total_ms = (time.time() - state.started_at) * 1000
        return OrchestrationResponse(
            job_id=state.job_id,
            status=state.status,
            engine_used=state.engine_used,
            execution_plan=state.execution_plan,
            quality_metrics=state.quality_metrics,
            enhancements_applied=state.enhancements,
            audio_output_url=state.audio_path,
            total_execution_time_ms=round(total_ms, 1),
            retry_count=state.retry_count,
        )


_service_instance: OrchestrationService | None = None


def get_orchestration_service() -> OrchestrationService:
    """Singleton accessor for the orchestration service."""
    global _service_instance
    if _service_instance is None:
        _service_instance = OrchestrationService()
    return _service_instance
