"""
A/B Testing Experiments API Routes

CRUD endpoints for managing A/B test experiments.
Integrates with ABTestingService for experiment lifecycle management.
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, Field

from backend.ml.models.ab_testing import (
    ABTestingService,
    Experiment,
    ExperimentStatus,
    Variant,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/experiments", tags=["experiments"])


# ==============================================================================
# Request/Response Models
# ==============================================================================


class VariantRequest(BaseModel):
    """Request model for creating/updating a variant."""

    id: str = Field(..., description="Unique variant identifier")
    name: str = Field(..., description="Variant display name")
    description: str = Field("", description="Variant description")
    weight: int = Field(50, ge=0, le=100, description="Traffic weight percentage")
    config: dict[str, Any] = Field(default_factory=dict, description="Variant configuration")


class CreateExperimentRequest(BaseModel):
    """Request model for creating a new experiment."""

    id: str = Field(..., description="Unique experiment identifier")
    name: str = Field(..., description="Experiment display name")
    description: str = Field("", description="Experiment description")
    variants: list[VariantRequest] = Field(
        ..., min_length=2, description="At least 2 variants required"
    )
    metrics: list[str] = Field(default_factory=list, description="Metrics to track")
    tags: list[str] = Field(default_factory=list, description="Experiment tags")
    target_sample_size: int = Field(0, ge=0, description="Target sample size (0 = unlimited)")


class UpdateExperimentRequest(BaseModel):
    """Request model for updating an experiment."""

    name: str | None = None
    description: str | None = None
    variants: list[VariantRequest] | None = None
    metrics: list[str] | None = None
    tags: list[str] | None = None
    target_sample_size: int | None = None


class ExperimentResponse(BaseModel):
    """Response model for experiment data."""

    id: str
    name: str
    description: str
    status: str
    variants: list[dict[str, Any]]
    created_at: str | None = None
    updated_at: str | None = None
    start_date: str | None = None
    end_date: str | None = None
    target_sample_size: int = 0
    current_sample_size: int = 0
    metrics: list[str] = []
    tags: list[str] = []


class ExperimentStatsResponse(BaseModel):
    """Response model for experiment statistics."""

    experiment_id: str
    status: str
    total_exposures: int = 0
    total_conversions: int = 0
    conversion_rate: float = 0.0
    variant_stats: dict[str, Any] = {}


class ExperimentListResponse(BaseModel):
    """Response model for experiment list."""

    experiments: list[ExperimentResponse]
    total: int


# ==============================================================================
# Service Instance
# ==============================================================================


def get_ab_service() -> ABTestingService:
    """Get or create ABTestingService instance."""
    return ABTestingService()


# ==============================================================================
# CRUD Endpoints
# ==============================================================================


@router.post("", response_model=ExperimentResponse, status_code=201)
async def create_experiment(request: CreateExperimentRequest):
    """
    Create a new A/B test experiment.

    The experiment starts in DRAFT status and must be explicitly started.
    """
    service = get_ab_service()

    # Check if experiment already exists
    existing = service.get_experiment(request.id)
    if existing:
        raise HTTPException(
            status_code=409, detail=f"Experiment with ID '{request.id}' already exists"
        )

    # Convert variant requests to Variant objects
    variants = [
        Variant(
            id=v.id,
            name=v.name,
            description=v.description,
            weight=v.weight,
            config=v.config,
        )
        for v in request.variants
    ]

    # Create experiment
    experiment = Experiment(
        id=request.id,
        name=request.name,
        description=request.description,
        variants=variants,
        metrics=request.metrics,
        tags=request.tags,
        target_sample_size=request.target_sample_size,
    )

    # Register with service
    service.register_experiment(experiment)

    return _experiment_to_response(experiment)


@router.get("", response_model=ExperimentListResponse)
async def list_experiments(
    status: str | None = Query(None, description="Filter by status"),
    tag: str | None = Query(None, description="Filter by tag"),
):
    """
    List all experiments, optionally filtered by status or tag.
    """
    service = get_ab_service()
    all_experiments = service.list_experiments()

    # Apply filters
    filtered = all_experiments

    if status:
        try:
            status_enum = ExperimentStatus(status)
            filtered = [e for e in filtered if e.status == status_enum]
        except ValueError:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid status: {status}. Valid values: {[s.value for s in ExperimentStatus]}",
            )

    if tag:
        filtered = [e for e in filtered if tag in e.tags]

    return ExperimentListResponse(
        experiments=[_experiment_to_response(e) for e in filtered],
        total=len(filtered),
    )


@router.get("/{experiment_id}", response_model=ExperimentResponse)
async def get_experiment(experiment_id: str):
    """
    Get a specific experiment by ID.
    """
    service = get_ab_service()
    experiment = service.get_experiment(experiment_id)

    if not experiment:
        raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")

    return _experiment_to_response(experiment)


@router.put("/{experiment_id}", response_model=ExperimentResponse)
async def update_experiment(experiment_id: str, request: UpdateExperimentRequest):
    """
    Update an existing experiment.

    Only DRAFT experiments can have their variants modified.
    RUNNING experiments can only have metadata updated.
    """
    service = get_ab_service()
    experiment = service.get_experiment(experiment_id)

    if not experiment:
        raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")

    # Check if modifying variants on a non-draft experiment
    if request.variants and experiment.status != ExperimentStatus.DRAFT:
        raise HTTPException(
            status_code=400, detail="Cannot modify variants on a non-DRAFT experiment"
        )

    # Update fields
    if request.name is not None:
        experiment.name = request.name
    if request.description is not None:
        experiment.description = request.description
    if request.metrics is not None:
        experiment.metrics = request.metrics
    if request.tags is not None:
        experiment.tags = request.tags
    if request.target_sample_size is not None:
        experiment.target_sample_size = request.target_sample_size
    if request.variants is not None:
        experiment.variants = [
            Variant(
                id=v.id,
                name=v.name,
                description=v.description,
                weight=v.weight,
                config=v.config,
            )
            for v in request.variants
        ]

    experiment.updated_at = datetime.utcnow()

    # Re-register to save changes
    service.register_experiment(experiment)

    return _experiment_to_response(experiment)


@router.delete("/{experiment_id}")
async def delete_experiment(experiment_id: str):
    """
    Delete an experiment.

    Only DRAFT, COMPLETED, or ARCHIVED experiments can be deleted.
    """
    service = get_ab_service()
    experiment = service.get_experiment(experiment_id)

    if not experiment:
        raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")

    if experiment.status == ExperimentStatus.RUNNING:
        raise HTTPException(
            status_code=400, detail="Cannot delete a RUNNING experiment. Stop it first."
        )

    # Archive instead of hard delete for audit trail
    service.archive_experiment(experiment_id)

    return {"message": f"Experiment '{experiment_id}' archived successfully"}


# ==============================================================================
# Lifecycle Endpoints
# ==============================================================================


@router.post("/{experiment_id}/start")
async def start_experiment(experiment_id: str):
    """
    Start an experiment (change status from DRAFT to RUNNING).
    """
    service = get_ab_service()

    success = service.start_experiment(experiment_id)
    if not success:
        experiment = service.get_experiment(experiment_id)
        if not experiment:
            raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")
        raise HTTPException(
            status_code=400, detail=f"Cannot start experiment in status: {experiment.status.value}"
        )

    return {"message": f"Experiment '{experiment_id}' started", "status": "running"}


@router.post("/{experiment_id}/pause")
async def pause_experiment(experiment_id: str):
    """
    Pause a running experiment.
    """
    service = get_ab_service()

    success = service.pause_experiment(experiment_id)
    if not success:
        experiment = service.get_experiment(experiment_id)
        if not experiment:
            raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")
        raise HTTPException(
            status_code=400, detail=f"Cannot pause experiment in status: {experiment.status.value}"
        )

    return {"message": f"Experiment '{experiment_id}' paused", "status": "paused"}


@router.post("/{experiment_id}/resume")
async def resume_experiment(experiment_id: str):
    """
    Resume a paused experiment.
    """
    service = get_ab_service()

    success = service.resume_experiment(experiment_id)
    if not success:
        experiment = service.get_experiment(experiment_id)
        if not experiment:
            raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")
        raise HTTPException(
            status_code=400, detail=f"Cannot resume experiment in status: {experiment.status.value}"
        )

    return {"message": f"Experiment '{experiment_id}' resumed", "status": "running"}


@router.post("/{experiment_id}/complete")
async def complete_experiment(experiment_id: str):
    """
    Mark an experiment as completed.
    """
    service = get_ab_service()

    success = service.complete_experiment(experiment_id)
    if not success:
        experiment = service.get_experiment(experiment_id)
        if not experiment:
            raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")
        raise HTTPException(
            status_code=400,
            detail=f"Cannot complete experiment in status: {experiment.status.value}",
        )

    return {"message": f"Experiment '{experiment_id}' completed", "status": "completed"}


# ==============================================================================
# Analytics Endpoints
# ==============================================================================


@router.get("/{experiment_id}/stats", response_model=ExperimentStatsResponse)
async def get_experiment_stats(experiment_id: str):
    """
    Get statistics for an experiment.
    """
    service = get_ab_service()
    experiment = service.get_experiment(experiment_id)

    if not experiment:
        raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")

    # Get variant stats
    variant_stats = {}
    for variant in experiment.variants:
        stats = service.get_variant_stats(experiment_id, variant.id)
        if stats:
            variant_stats[variant.id] = stats

    # Calculate totals
    total_exposures = sum(s.get("exposures", 0) for s in variant_stats.values())
    total_conversions = sum(s.get("conversions", 0) for s in variant_stats.values())
    conversion_rate = (total_conversions / total_exposures * 100) if total_exposures > 0 else 0.0

    return ExperimentStatsResponse(
        experiment_id=experiment_id,
        status=experiment.status.value,
        total_exposures=total_exposures,
        total_conversions=total_conversions,
        conversion_rate=conversion_rate,
        variant_stats=variant_stats,
    )


@router.get("/{experiment_id}/events")
async def get_experiment_events(
    experiment_id: str,
    event_type: str | None = Query(None, description="Filter by event type"),
    limit: int = Query(100, ge=1, le=1000),
):
    """
    Get events for an experiment.
    """
    service = get_ab_service()
    experiment = service.get_experiment(experiment_id)

    if not experiment:
        raise HTTPException(status_code=404, detail=f"Experiment '{experiment_id}' not found")

    events = service.get_experiment_events(experiment_id, event_type=event_type, limit=limit)

    return {
        "experiment_id": experiment_id,
        "events": [e.to_dict() if hasattr(e, "to_dict") else e for e in events],
        "count": len(events),
    }


# ==============================================================================
# Helper Functions
# ==============================================================================


def _experiment_to_response(experiment: Experiment) -> ExperimentResponse:
    """Convert Experiment to ExperimentResponse."""
    return ExperimentResponse(
        id=experiment.id,
        name=experiment.name,
        description=experiment.description,
        status=experiment.status.value,
        variants=[
            {
                "id": v.id,
                "name": v.name,
                "description": v.description,
                "weight": v.weight,
                "config": v.config,
            }
            for v in experiment.variants
        ],
        created_at=experiment.created_at.isoformat() if experiment.created_at else None,
        updated_at=experiment.updated_at.isoformat() if experiment.updated_at else None,
        start_date=experiment.start_date.isoformat() if experiment.start_date else None,
        end_date=experiment.end_date.isoformat() if experiment.end_date else None,
        target_sample_size=experiment.target_sample_size,
        current_sample_size=experiment.current_sample_size,
        metrics=experiment.metrics,
        tags=experiment.tags,
    )
