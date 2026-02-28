"""
Orchestrator Contract Schemas — Phase X-A

Defines all Pydantic models for the orchestration system:
- ProductionChain: declarative pipeline definition
- OrchestrationRequest/Response: primary API contracts
- QualityThresholdPolicy: reusable quality gate
- EngineExecutionPlan: diagnostic output
- RetryPolicy: adaptive retry configuration
- StrategyPreset: saved orchestration template
- OrchestrationEvent: WebSocket real-time event

All models inherit VoiceStudioBaseModel for consistent null handling.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timezone
from enum import Enum
from typing import Any

from pydantic import Field

from backend.api.models import VoiceStudioBaseModel


class PipelineStepType(str, Enum):
    """Types of steps in a production chain."""

    SYNTHESIS = "synthesis"
    ENHANCEMENT = "enhancement"
    QUALITY_EVALUATION = "quality_evaluation"
    POST_PROCESSING = "post_processing"
    MASTERING = "mastering"
    EXPORT = "export"


class OrchestrationStrategy(str, Enum):
    """Engine selection and optimization strategy."""

    AUTO = "auto"
    QUALITY_FIRST = "quality_first"
    SPEED_FIRST = "speed_first"
    DETERMINISTIC = "deterministic"


class OrchestrationPriority(str, Enum):
    """Job priority levels, matching EnhancedJobQueue conventions."""

    REALTIME = "realtime"
    INTERACTIVE = "interactive"
    BATCH = "batch"


class OrchestrationStatus(str, Enum):
    """Orchestration job lifecycle states."""

    QUEUED = "queued"
    SELECTING_ENGINE = "selecting_engine"
    SYNTHESIZING = "synthesizing"
    EVALUATING_QUALITY = "evaluating_quality"
    RETRYING = "retrying"
    ENHANCING = "enhancing"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


class ParameterMutationStrategy(str, Enum):
    """How synthesis parameters are adjusted on retry."""

    NONE = "none"
    RANDOM_VARIATION = "random_variation"
    QUALITY_GRADIENT = "quality_gradient"


class OrchestrationEventType(str, Enum):
    """WebSocket event types emitted during orchestration."""

    JOB_QUEUED = "job_queued"
    ENGINE_SELECTED = "engine_selected"
    SYNTHESIS_STARTED = "synthesis_started"
    SYNTHESIS_COMPLETED = "synthesis_completed"
    QUALITY_EVALUATED = "quality_evaluated"
    QUALITY_BELOW_THRESHOLD = "quality_below_threshold"
    RETRY_TRIGGERED = "retry_triggered"
    ENHANCEMENT_STARTED = "enhancement_started"
    ENHANCEMENT_COMPLETED = "enhancement_completed"
    JOB_COMPLETED = "job_completed"
    JOB_FAILED = "job_failed"
    ERROR = "error"


class PresetCategory(str, Enum):
    """Categories for strategy presets."""

    CINEMATIC = "cinematic"
    AUDIOBOOK = "audiobook"
    PODCAST = "podcast"
    BROADCAST = "broadcast"
    GAME_CHARACTER = "game_character"
    CONVERSATIONAL = "conversational"


# ---------------------------------------------------------------------------
# Sub-models
# ---------------------------------------------------------------------------


class PipelineStep(VoiceStudioBaseModel):
    """A single step in a production chain."""

    step_id: str = Field(default_factory=lambda: str(uuid.uuid4())[:8])
    type: PipelineStepType
    engine: str | None = None
    parameters: dict[str, Any] = Field(default_factory=dict)
    condition: dict[str, Any] | None = None


class QualityThresholdPolicy(VoiceStudioBaseModel):
    """Reusable quality gate applied during orchestration."""

    min_mos: float = Field(default=3.5, ge=1.0, le=5.0)
    min_similarity: float = Field(default=0.7, ge=0.0, le=1.0)
    min_naturalness: float = Field(default=0.8, ge=0.0, le=1.0)
    max_artifact_score: float = Field(default=0.05, ge=0.0, le=1.0)
    min_snr_db: float = Field(default=30.0, ge=0.0)
    auto_enhance_if_below: bool = True
    fail_if_unmet: bool = False


class RetryPolicy(VoiceStudioBaseModel):
    """Orchestration-level retry configuration."""

    max_attempts: int = Field(default=3, ge=1, le=10)
    adaptive_adjustment: bool = True
    fallback_engines_enabled: bool = True
    parameter_mutation_strategy: ParameterMutationStrategy = (
        ParameterMutationStrategy.QUALITY_GRADIENT
    )


class ParameterAdjustment(VoiceStudioBaseModel):
    """Record of parameter changes made during a retry attempt."""

    attempt: int
    changes: dict[str, Any] = Field(default_factory=dict)
    reason: str | None = None


class EngineExecutionPlan(VoiceStudioBaseModel):
    """Diagnostic output showing the orchestration decisions made."""

    selected_engine: str
    fallback_engines: list[str] = Field(default_factory=list)
    parameter_adjustments: list[ParameterAdjustment] = Field(default_factory=list)


class OrchestrationQualityMetrics(VoiceStudioBaseModel):
    """Quality metrics snapshot captured during orchestration."""

    mos_score: float | None = None
    similarity: float | None = None
    naturalness: float | None = None
    snr_db: float | None = None
    artifact_score: float | None = None
    has_clicks: bool | None = None
    has_distortion: bool | None = None


# ---------------------------------------------------------------------------
# Top-level contracts
# ---------------------------------------------------------------------------


class ProductionChain(VoiceStudioBaseModel):
    """Declarative pipeline definition — an ordered list of processing steps."""

    schema_version: str = "1.0"
    chain_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    name: str = "Default"
    description: str = ""
    steps: list[PipelineStep] = Field(default_factory=list)
    quality_policy: QualityThresholdPolicy = Field(
        default_factory=QualityThresholdPolicy
    )


class OrchestrationRequest(VoiceStudioBaseModel):
    """Primary input contract for POST /api/orchestrator/run."""

    text: str = Field(..., min_length=1, max_length=50000)
    voice_profile_id: str = Field(..., min_length=1)
    reference_audio_path: str | None = None
    language: str = "en"
    production_chain: ProductionChain | None = None
    strategy: OrchestrationStrategy = OrchestrationStrategy.AUTO
    target_quality: QualityThresholdPolicy = Field(
        default_factory=QualityThresholdPolicy
    )
    retry_policy: RetryPolicy = Field(default_factory=RetryPolicy)
    async_mode: bool = True
    priority: OrchestrationPriority = OrchestrationPriority.INTERACTIVE


class OrchestrationResponse(VoiceStudioBaseModel):
    """Output contract returned by the orchestrator."""

    job_id: str
    status: OrchestrationStatus
    engine_used: str | None = None
    execution_plan: EngineExecutionPlan | None = None
    quality_metrics: OrchestrationQualityMetrics | None = None
    enhancements_applied: list[str] = Field(default_factory=list)
    audio_output_url: str | None = None
    total_execution_time_ms: float | None = None
    retry_count: int = 0


class OrchestrationStatusResponse(VoiceStudioBaseModel):
    """Response for GET /api/orchestrator/status/{job_id}."""

    job_id: str
    status: OrchestrationStatus
    progress_percent: float = 0.0
    current_step: str | None = None
    engine_active: str | None = None
    estimated_remaining_ms: float | None = None
    started_at: str | None = None
    retry_count: int = 0


class StrategyPreset(VoiceStudioBaseModel):
    """A saved orchestration template combining chain + quality policy."""

    preset_id: str = Field(default_factory=lambda: str(uuid.uuid4())[:12])
    name: str
    category: PresetCategory
    default_chain: ProductionChain = Field(default_factory=ProductionChain)
    default_quality_policy: QualityThresholdPolicy = Field(
        default_factory=QualityThresholdPolicy
    )
    description: str = ""
    is_builtin: bool = False


class OrchestrationEvent(VoiceStudioBaseModel):
    """WebSocket real-time event emitted during orchestration."""

    event_type: OrchestrationEventType
    job_id: str
    timestamp: str = Field(
        default_factory=lambda: datetime.now(timezone.utc).isoformat()
    )
    data: dict[str, Any] = Field(default_factory=dict)


class GpuStatusResponse(VoiceStudioBaseModel):
    """GPU utilization snapshot for the scheduler."""

    gpu_available: bool = False
    total_vram_mb: float = 0.0
    used_vram_mb: float = 0.0
    free_vram_mb: float = 0.0
    utilization_percent: float = 0.0
    active_engine_count: int = 0
    can_schedule: bool = True
