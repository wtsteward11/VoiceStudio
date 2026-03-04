"""Tests for orchestrator Pydantic schemas — round-trip serialization and validation."""

from __future__ import annotations

import json

import pytest

from backend.orchestrator.schemas import (
    EngineExecutionPlan,
    GpuStatusResponse,
    OrchestrationEvent,
    OrchestrationEventType,
    OrchestrationPriority,
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
    PresetCategory,
    ProductionChain,
    QualityThresholdPolicy,
    RetryPolicy,
    StrategyPreset,
)


class TestOrchestrationRequest:
    def test_minimal_request(self):
        req = OrchestrationRequest(text="Hello world", voice_profile_id="p1")
        assert req.text == "Hello world"
        assert req.language == "en"
        assert req.strategy == OrchestrationStrategy.AUTO
        assert req.async_mode is True

    def test_full_request(self):
        req = OrchestrationRequest(
            text="Test text",
            voice_profile_id="profile_abc",
            language="es",
            strategy=OrchestrationStrategy.QUALITY_FIRST,
            priority=OrchestrationPriority.REALTIME,
            async_mode=False,
        )
        assert req.strategy == OrchestrationStrategy.QUALITY_FIRST
        assert req.priority == OrchestrationPriority.REALTIME

    def test_round_trip_serialization(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        data = json.loads(req.model_dump_json())
        restored = OrchestrationRequest(**data)
        assert restored.text == req.text
        assert restored.voice_profile_id == req.voice_profile_id

    def test_text_validation(self):
        with pytest.raises(Exception):
            OrchestrationRequest(text="", voice_profile_id="p1")


class TestOrchestrationResponse:
    def test_basic_response(self):
        resp = OrchestrationResponse(
            job_id="j1", status=OrchestrationStatus.COMPLETED
        )
        assert resp.job_id == "j1"
        assert resp.retry_count == 0

    def test_full_response(self):
        resp = OrchestrationResponse(
            job_id="j2",
            status=OrchestrationStatus.COMPLETED,
            engine_used="xtts_v2",
            enhancements_applied=["denoise", "loudness"],
            total_execution_time_ms=4500.0,
            retry_count=1,
        )
        assert resp.engine_used == "xtts_v2"
        assert len(resp.enhancements_applied) == 2


class TestProductionChain:
    def test_default_chain(self):
        chain = ProductionChain(name="Test")
        assert chain.schema_version == "1.0"
        assert len(chain.steps) == 0

    def test_chain_with_steps(self):
        chain = ProductionChain(
            name="My Pipeline",
            steps=[
                PipelineStep(step_id="s1", type=PipelineStepType.SYNTHESIS),
                PipelineStep(
                    step_id="s2",
                    type=PipelineStepType.QUALITY_EVALUATION,
                ),
                PipelineStep(
                    step_id="s3",
                    type=PipelineStepType.POST_PROCESSING,
                    parameters={"preset": "cinematic"},
                ),
            ],
        )
        assert len(chain.steps) == 3
        assert chain.steps[2].parameters["preset"] == "cinematic"

    def test_chain_round_trip(self):
        chain = ProductionChain(
            name="Round Trip",
            steps=[PipelineStep(step_id="s1", type=PipelineStepType.SYNTHESIS)],
        )
        data = json.loads(chain.model_dump_json())
        restored = ProductionChain(**data)
        assert restored.name == "Round Trip"
        assert len(restored.steps) == 1


class TestQualityThresholdPolicy:
    def test_defaults(self):
        policy = QualityThresholdPolicy()
        assert policy.min_mos == 3.5
        assert policy.auto_enhance_if_below is True
        assert policy.fail_if_unmet is False

    def test_custom_policy(self):
        policy = QualityThresholdPolicy(
            min_mos=4.5, min_similarity=0.95, fail_if_unmet=True
        )
        assert policy.min_mos == 4.5
        assert policy.fail_if_unmet is True


class TestRetryPolicy:
    def test_defaults(self):
        policy = RetryPolicy()
        assert policy.max_attempts == 3
        assert policy.adaptive_adjustment is True
        assert policy.parameter_mutation_strategy == ParameterMutationStrategy.QUALITY_GRADIENT

    def test_custom(self):
        policy = RetryPolicy(
            max_attempts=5,
            parameter_mutation_strategy=ParameterMutationStrategy.RANDOM_VARIATION,
        )
        assert policy.max_attempts == 5


class TestStrategyPreset:
    def test_preset_creation(self):
        preset = StrategyPreset(
            name="Cinematic",
            category=PresetCategory.CINEMATIC,
            description="High drama narration",
        )
        assert preset.name == "Cinematic"
        assert preset.is_builtin is False

    def test_preset_round_trip(self):
        preset = StrategyPreset(
            name="Podcast",
            category=PresetCategory.PODCAST,
        )
        data = json.loads(preset.model_dump_json())
        restored = StrategyPreset(**data)
        assert restored.name == "Podcast"


class TestOrchestrationEvent:
    def test_event_creation(self):
        event = OrchestrationEvent(
            event_type=OrchestrationEventType.ENGINE_SELECTED,
            job_id="j1",
            data={"engine": "xtts_v2"},
        )
        assert event.event_type == OrchestrationEventType.ENGINE_SELECTED
        assert event.data["engine"] == "xtts_v2"
        assert event.timestamp  # auto-generated


class TestGpuStatusResponse:
    def test_defaults(self):
        gpu = GpuStatusResponse()
        assert gpu.gpu_available is False
        assert gpu.can_schedule is True

    def test_with_gpu(self):
        gpu = GpuStatusResponse(
            gpu_available=True,
            total_vram_mb=8192,
            used_vram_mb=4096,
            free_vram_mb=4096,
            utilization_percent=50.0,
        )
        assert gpu.utilization_percent == 50.0


class TestEngineExecutionPlan:
    def test_plan(self):
        plan = EngineExecutionPlan(
            selected_engine="chatterbox",
            fallback_engines=["xtts_v2", "piper"],
            parameter_adjustments=[
                ParameterAdjustment(
                    attempt=2,
                    changes={"temperature": 0.6},
                    reason="Quality gradient",
                )
            ],
        )
        assert plan.selected_engine == "chatterbox"
        assert len(plan.fallback_engines) == 2
        assert plan.parameter_adjustments[0].attempt == 2
