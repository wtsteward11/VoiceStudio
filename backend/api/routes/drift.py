"""
Model Drift API Routes — Phase 9 Sprint 2

Provides endpoints for model data drift monitoring.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.services.model_drift_detector import (
    DriftMetric,
    DriftStatus,
    get_model_drift_detector,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/drift", tags=["drift"])


class DriftMetricResponse(BaseModel):
    """Response model for a drift metric."""

    engine_id: str
    metric_name: str
    psi: float
    baseline_sample_count: int
    current_sample_count: int
    last_updated: str
    is_drifted: bool


class DriftStatusResponse(BaseModel):
    """Response model for drift status per engine."""

    engine_id: str
    has_baseline: bool
    metrics: list[DriftMetricResponse]
    any_drifted: bool
    last_checked: str


class DriftStatusListResponse(BaseModel):
    """Response model for drift status list."""

    statuses: list[DriftStatusResponse]
    psi_threshold: float = 0.2


class SetBaselineRequest(BaseModel):
    """Request body for setting baseline."""

    engine_id: str
    metric_name: str
    values: list[float]


@router.get("/status", response_model=DriftStatusListResponse)
async def get_drift_status(engine_id: str | None = None) -> DriftStatusListResponse:
    """
    Get current drift status per engine.

    Query param engine_id: optional filter by engine.
    """
    try:
        detector = get_model_drift_detector()
        statuses = detector.get_status(engine_id=engine_id)
        return DriftStatusListResponse(
            statuses=[
                DriftStatusResponse(
                    engine_id=s.engine_id,
                    has_baseline=s.has_baseline,
                    metrics=[
                        DriftMetricResponse(
                            engine_id=m.engine_id,
                            metric_name=m.metric_name,
                            psi=m.psi,
                            baseline_sample_count=m.baseline_sample_count,
                            current_sample_count=m.current_sample_count,
                            last_updated=m.last_updated,
                            is_drifted=m.is_drifted,
                        )
                        for m in s.metrics
                    ],
                    any_drifted=s.any_drifted,
                    last_checked=s.last_checked,
                )
                for s in statuses
            ],
        )
    except Exception as e:
        logger.error("Error getting drift status: %s", e)
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.get("/history")
async def get_drift_history(
    engine_id: str | None = None,
    metric_name: str | None = None,
    limit: int = 100,
) -> dict[str, list[dict[str, Any]]]:
    """
    Get drift metrics history.

    Query params: engine_id, metric_name (optional filters), limit.
    """
    try:
        detector = get_model_drift_detector()
        history = detector.get_history(
            engine_id=engine_id,
            metric_name=metric_name,
            limit=limit,
        )
        return {"history": history}
    except Exception as e:
        logger.error("Error getting drift history: %s", e)
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.post("/baseline")
async def set_baseline(body: SetBaselineRequest) -> dict[str, str | int | bool]:
    """
    Set baseline distribution from current model version.

    Requires engine_id, metric_name, and list of values.
    """
    try:
        if len(body.values) < 5:
            raise HTTPException(
                status_code=400,
                detail="At least 5 values required for baseline",
            )
        detector = get_model_drift_detector()
        detector.set_baseline(
            engine_id=body.engine_id,
            metric_name=body.metric_name,
            values=body.values,
        )
        return {
            "success": True,
            "engine_id": body.engine_id,
            "metric_name": body.metric_name,
            "sample_count": len(body.values),
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error("Error setting drift baseline: %s", e)
        raise HTTPException(status_code=500, detail=str(e)) from e
