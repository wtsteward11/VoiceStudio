"""
Training Routes

Endpoints for voice model training.
Supports dataset management, training job control, and progress tracking.
M4: Thin route layer — business logic in backend.services.training_service.
"""

from __future__ import annotations

import logging
from datetime import datetime
from pathlib import Path
from typing import Any

from fastapi import APIRouter, Depends, File, HTTPException, Query, Request, UploadFile
from fastapi.responses import FileResponse
from pydantic import BaseModel

from backend.config.path_config import get_path
from backend.core.security.file_validation import FileValidationError, validate_archive_file
from backend.services import training_service as svc

from ..middleware.auth_middleware import require_auth_if_enabled
from ..models import ApiOk
from ..models_additional import (
    TrainingDataAnalysis,
    TrainingDataOptimizationRequest,
    TrainingDataOptimizationResponse,
)
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/training",
    tags=["training"],
    dependencies=[Depends(require_auth_if_enabled)],
)


# --- Pydantic models (API contracts) ---


class TrainingDataset(BaseModel):
    """Training dataset information."""

    id: str
    name: str
    description: str | None = None
    audio_files: list[str]
    transcripts: list[str] | None = None
    created: datetime
    modified: datetime


class TrainingRequest(BaseModel):
    """Request to start training."""

    dataset_id: str
    profile_id: str
    engine: str = "xtts"
    epochs: int = 100
    batch_size: int = 4
    learning_rate: float = 0.0001
    gpu: bool = True
    output_path: str | None = None


class TrainingQualityMetrics(BaseModel):
    """Quality metrics for a training epoch (IDEA 54)."""

    epoch: int
    training_loss: float | None = None
    validation_loss: float | None = None
    quality_score: float | None = None
    mos_score: float | None = None
    similarity: float | None = None
    naturalness: float | None = None
    timestamp: datetime


class TrainingQualityAlert(BaseModel):
    """Quality alert for training monitoring (IDEA 54)."""

    type: str
    severity: str = "info"
    message: str
    epoch: int
    timestamp: datetime


class EarlyStoppingRecommendation(BaseModel):
    """Early stopping recommendation (IDEA 54)."""

    should_stop: bool
    reason: str
    confidence: float
    current_epoch: int
    best_epoch: int | None = None
    best_metrics: TrainingQualityMetrics | None = None


class TrainingStatus(BaseModel):
    """Training job status."""

    id: str
    dataset_id: str
    profile_id: str
    engine: str
    status: str
    progress: float
    current_epoch: int
    total_epochs: int
    loss: float | None = None
    started: datetime | None = None
    completed: datetime | None = None
    error_message: str | None = None
    quality_score: float | None = None
    validation_loss: float | None = None
    quality_alerts: list[TrainingQualityAlert] | None = None
    early_stopping_recommendation: EarlyStoppingRecommendation | None = None
    simulation_mode: bool = False
    simulation_reason: str | None = None


class TrainingLogEntry(BaseModel):
    """Single log entry from training."""

    timestamp: datetime
    level: str
    message: str
    epoch: int | None = None
    loss: float | None = None


class DatasetCreateRequest(BaseModel):
    """Request to create a dataset."""

    name: str
    description: str | None = None
    audio_files: list[str] | None = None


class HyperparameterOptimizationRequest(BaseModel):
    """Request to optimize training hyperparameters."""

    dataset_id: str
    profile_id: str
    engine: str = "xtts"
    method: str = "optuna"
    n_trials: int = 20
    timeout_seconds: int | None = None
    hyperparameters: dict | None = None


class HyperparameterOptimizationResponse(BaseModel):
    """Response from hyperparameter optimization."""

    best_params: dict[str, Any]
    best_score: float
    optimization_method: str
    n_trials: int
    trials_completed: int
    optimization_time_seconds: float
    recommendations: list[str]


class ModelExportRequest(BaseModel):
    """Request to export a trained model."""

    training_id: str
    profile_id: str | None = None
    include_metadata: bool = True


class ModelExportResponse(BaseModel):
    """Response from model export."""

    export_id: str
    model_path: str
    export_path: str
    created: datetime


def _status_dict_to_model(d: dict) -> TrainingStatus:
    """Convert service status dict to TrainingStatus model."""
    if d.get("started") and isinstance(d["started"], str):
        d = dict(d)
        d["started"] = datetime.fromisoformat(d["started"])
    if d.get("completed") and isinstance(d["completed"], str):
        d = dict(d)
        d["completed"] = datetime.fromisoformat(d["completed"])
    if d.get("quality_alerts"):
        d = dict(d)
        d["quality_alerts"] = [
            TrainingQualityAlert(**a) if isinstance(a, dict) else a
            for a in d["quality_alerts"]
        ]
    if d.get("early_stopping_recommendation"):
        d = dict(d)
        rec = dict(d["early_stopping_recommendation"]) if isinstance(d["early_stopping_recommendation"], dict) else d["early_stopping_recommendation"]
        if isinstance(rec, dict) and rec.get("best_metrics"):
            rec = dict(rec)
            rec["best_metrics"] = TrainingQualityMetrics(**rec["best_metrics"])
        d["early_stopping_recommendation"] = EarlyStoppingRecommendation(**rec)
    return TrainingStatus(**d)


def _dataset_dict_to_model(d: dict) -> TrainingDataset:
    """Convert service dataset dict to TrainingDataset model."""
    if d.get("created") and isinstance(d["created"], str):
        d = dict(d)
        d["created"] = datetime.fromisoformat(d["created"])
        d["modified"] = datetime.fromisoformat(d["modified"]) if isinstance(d.get("modified"), str) else d["modified"]
    return TrainingDataset(**d)


# --- Dataset endpoints ---


@router.post("/datasets", response_model=TrainingDataset)
async def create_dataset(request: DatasetCreateRequest):
    """Create a new training dataset."""
    if not request.name or not request.name.strip():
        raise HTTPException(status_code=400, detail="Dataset name is required")
    try:
        result = svc.create_dataset(
            name=request.name.strip(),
            description=request.description,
            audio_files=request.audio_files,
        )
        return TrainingDataset(**result)
    except Exception as e:
        logger.error("Error creating training dataset: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create dataset: {e!s}")


@router.get("/datasets", response_model=list[TrainingDataset])
@cache_response(ttl=60)
async def list_datasets():
    """List all training datasets."""
    try:
        items = svc.list_datasets()
        return [_dataset_dict_to_model(d) for d in items]
    except Exception as e:
        logger.error("Error listing training datasets: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list datasets: {e!s}")


@router.get("/datasets/{dataset_id}", response_model=TrainingDataset)
@cache_response(ttl=300)
async def get_dataset(dataset_id: str):
    """Get dataset by ID."""
    if not dataset_id or not dataset_id.strip():
        raise HTTPException(status_code=400, detail="Dataset ID is required")
    result = svc.get_dataset(dataset_id)
    if result is None:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return _dataset_dict_to_model(result)


@router.post("/datasets/{dataset_id}/optimize", response_model=TrainingDataOptimizationResponse)
async def optimize_training_data(
    dataset_id: str,
    req: TrainingDataOptimizationRequest,
) -> TrainingDataOptimizationResponse:
    """Optimize training data (quality, diversity, optimal samples)."""
    result = svc.optimize_training_data(
        dataset_id=dataset_id,
        analyze_quality=req.analyze_quality,
        analyze_diversity=req.analyze_diversity,
        select_optimal=req.select_optimal,
    )
    if result is None:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return TrainingDataOptimizationResponse(
        dataset_id=result["dataset_id"],
        analysis=TrainingDataAnalysis(**result["analysis"]),
        optimized_dataset_id=result["optimized_dataset_id"],
        quality_improvement=result["quality_improvement"],
    )


@router.post("/hyperparameters/optimize", response_model=HyperparameterOptimizationResponse)
async def optimize_hyperparameters(request: HyperparameterOptimizationRequest):
    """Optimize training hyperparameters."""
    result = svc.optimize_hyperparameters(
        dataset_id=request.dataset_id,
        method=request.method,
        n_trials=request.n_trials,
        timeout_seconds=request.timeout_seconds,
        hyperparameters=request.hyperparameters,
    )
    if result is None:
        raise HTTPException(status_code=404, detail="Dataset not found")
    if "best_params" not in result:
        raise HTTPException(status_code=500, detail="Hyperparameter optimization failed to return results")
    return HyperparameterOptimizationResponse(**result)


# --- Training job endpoints ---


@router.post("/start", response_model=TrainingStatus)
async def start_training(request: TrainingRequest):
    """Start a training job."""
    if not request.dataset_id or not request.dataset_id.strip():
        raise HTTPException(status_code=400, detail="Dataset ID is required")
    if not request.profile_id or not request.profile_id.strip():
        raise HTTPException(status_code=400, detail="Profile ID is required")
    if request.epochs <= 0:
        raise HTTPException(status_code=400, detail="Epochs must be greater than 0")
    if request.batch_size <= 0:
        raise HTTPException(status_code=400, detail="Batch size must be greater than 0")
    if request.learning_rate <= 0:
        raise HTTPException(status_code=400, detail="Learning rate must be greater than 0")

    async def _on_started(tid: str, req: dict) -> None:
        await svc.run_training(
            tid,
            req["dataset_id"],
            req["profile_id"],
            req.get("engine", "xtts"),
            req.get("epochs", 100),
            req.get("batch_size", 4),
            req.get("learning_rate", 0.0001),
            req.get("gpu", True),
        )

    result = svc.start_training(
        dataset_id=request.dataset_id,
        profile_id=request.profile_id,
        engine=request.engine,
        epochs=request.epochs,
        batch_size=request.batch_size,
        learning_rate=request.learning_rate,
        gpu=request.gpu,
        output_path=request.output_path,
        on_started=_on_started,
    )
    if result is None:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return _status_dict_to_model(result)


@router.get("/status/{training_id}", response_model=TrainingStatus)
@cache_response(ttl=5)
async def get_training_status(training_id: str):
    """Get training job status."""
    if not training_id or not training_id.strip():
        raise HTTPException(status_code=400, detail="Training ID is required")
    result = svc.get_training_status(training_id)
    if result is None:
        raise HTTPException(status_code=404, detail="Training job not found")
    return _status_dict_to_model(result)


@router.get("/status", response_model=list[TrainingStatus])
@cache_response(ttl=10)
async def list_training_jobs(
    profile_id: str | None = Query(None),
    status: str | None = Query(None),
):
    """List all training jobs."""
    try:
        jobs = svc.list_training_jobs(profile_id=profile_id, status_filter=status)
        return [_status_dict_to_model(j) for j in jobs]
    except Exception as e:
        logger.error("Error listing training jobs: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list training jobs: {e!s}")


@router.get("/{training_id}/quality-history", response_model=list[TrainingQualityMetrics])
@cache_response(ttl=5)
async def get_training_quality_history(
    training_id: str,
    limit: int | None = Query(100),
):
    """Get quality metrics history for a training job."""
    if not training_id or not training_id.strip():
        raise HTTPException(status_code=400, detail="Training ID is required")
    history = svc.get_quality_history(training_id, limit=limit)
    result = []
    for entry in history:
        ts = entry.get("timestamp")
        if isinstance(ts, str):
            ts = datetime.fromisoformat(ts)
        elif ts is None:
            ts = datetime.utcnow()
        result.append(TrainingQualityMetrics(
            epoch=entry.get("epoch", 0),
            training_loss=entry.get("training_loss"),
            validation_loss=entry.get("validation_loss"),
            quality_score=entry.get("quality_score"),
            mos_score=entry.get("mos_score"),
            similarity=entry.get("similarity"),
            naturalness=entry.get("naturalness"),
            timestamp=ts,
        ))
    return result


@router.post("/cancel/{training_id}", response_model=ApiOk)
async def cancel_training(training_id: str):
    """Cancel a running training job."""
    if not training_id or not training_id.strip():
        raise HTTPException(status_code=400, detail="Training ID is required")
    if not svc.cancel_training(training_id):
        raise HTTPException(status_code=404, detail="Training job not found or cannot be cancelled")
    return ApiOk(message="Training cancelled")


@router.get("/logs/{training_id}", response_model=list[TrainingLogEntry])
@cache_response(ttl=5)
async def get_training_logs(
    training_id: str,
    limit: int | None = Query(100),
):
    """Get training logs for a training job."""
    if not training_id or not training_id.strip():
        raise HTTPException(status_code=400, detail="Training ID is required")
    if limit is not None and limit <= 0:
        raise HTTPException(status_code=400, detail="Limit must be greater than 0")
    logs = svc.get_training_logs(training_id, limit=limit or 100)
    return [
        TrainingLogEntry(
            timestamp=datetime.fromisoformat(log["timestamp"]) if isinstance(log.get("timestamp"), str) else log["timestamp"],
            level=log.get("level", "info"),
            message=log.get("message", ""),
            epoch=log.get("epoch"),
            loss=log.get("loss"),
        )
        for log in logs
    ]


@router.delete("/{training_id}", response_model=ApiOk)
async def delete_training_job(training_id: str):
    """Delete a training job."""
    if not training_id or not training_id.strip():
        raise HTTPException(status_code=400, detail="Training ID is required")
    if not svc.delete_training_job(training_id):
        raise HTTPException(
            status_code=400,
            detail="Training job not found or cannot delete active job. Cancel it first.",
        )
    return ApiOk(message="Training job deleted")


# --- Export / Import ---


@router.post("/export", response_model=ModelExportResponse)
async def export_trained_model(request: ModelExportRequest, http_request: Request):
    """Export a trained model as ZIP package."""
    from ..utils.instrumentation import EventType, instrument_flow

    request_id = getattr(http_request.state, "request_id", None)
    with instrument_flow(
        EventType.EXPORT_START,
        EventType.EXPORT_COMPLETE,
        EventType.EXPORT_ERROR,
        request_id=request_id,
        training_id=request.training_id,
        profile_id=request.profile_id,
    ):
        try:
            result = svc.export_trained_model(
                training_id=request.training_id,
                profile_id=request.profile_id,
                include_metadata=request.include_metadata,
            )
            if result is None:
                raise HTTPException(status_code=404, detail="Training job not found or not completed")
            return ModelExportResponse(
                export_id=result["export_id"],
                model_path=result["model_path"],
                export_path=result["export_path"],
                created=result["created"],
            )
        except HTTPException:
            raise
        except Exception as e:
            logger.error("Failed to export model: %s", e, exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to export model: {e!s}")


@router.post("/import", response_model=None)
async def import_trained_model(
    file: UploadFile = File(...),
    profile_id: str | None = Query(None),
    request: Request = None,
):
    """Import a trained model from a ZIP package."""
    from ..utils.instrumentation import EventType, instrument_flow

    request_id = getattr(request.state, "request_id", None) if request else None
    with instrument_flow(
        EventType.IMPORT_START,
        EventType.IMPORT_COMPLETE,
        EventType.IMPORT_ERROR,
        request_id=request_id,
        profile_id=profile_id,
        filename=file.filename if file else None,
    ):
        try:
            content = await file.read()
            try:
                validate_archive_file(content, filename=file.filename)
            except FileValidationError as e:
                raise HTTPException(status_code=400, detail=f"Invalid archive file: {e.message}") from e

            result = svc.import_trained_model(
                content=content,
                filename=file.filename or "upload.zip",
                profile_id=profile_id,
            )
            if result is None:
                raise HTTPException(status_code=400, detail="Invalid model package or failed to import")
            return _status_dict_to_model(result)
        except HTTPException:
            raise
        except Exception as e:
            logger.error("Failed to import model: %s", e, exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to import model: {e!s}")


@router.get("/exports/{export_id}/download")
async def download_export(export_id: str):
    """Download exported model ZIP file."""
    zip_path = svc.get_export_download_path(export_id)
    if zip_path is None:
        raise HTTPException(status_code=404, detail="Export not found")
    return FileResponse(path=str(zip_path), filename=zip_path.name, media_type="application/zip")
