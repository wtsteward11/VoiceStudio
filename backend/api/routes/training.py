"""
Training Routes

Endpoints for voice model training.
Supports dataset management, training job control, and progress tracking.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import shutil
import time
import uuid
import zipfile
from datetime import datetime
from pathlib import Path
from typing import Any

from backend.ml.models.engine_service import get_engine_service
from fastapi import APIRouter, Depends, File, HTTPException, Query, Request, UploadFile
from fastapi.responses import FileResponse
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_archive_file,
)

from ..middleware.auth_middleware import require_auth_if_enabled
from ..ml_optimization import HyperparameterOptimizer
from ..models import ApiOk
from ..models_additional import (
    TrainingDataAnalysis,
    TrainingDataOptimizationRequest,
    TrainingDataOptimizationResponse,
)
from ..optimization import cache_response

logger = logging.getLogger(__name__)

# Import WebSocket broadcasting
try:
    from ..ws import realtime

    HAS_WEBSOCKET = True
except ImportError:
    HAS_WEBSOCKET = False
    logger.warning("WebSocket realtime module not available")

router = APIRouter(
    prefix="/api/training",
    tags=["training"],
    dependencies=[Depends(require_auth_if_enabled)],
)

# In-memory storage (replace with database in production)
_training_jobs: dict[str, dict] = {}
_training_logs: dict[str, list[dict]] = {}
_training_quality_history: dict[str, list[dict]] = {}  # job_id -> list of quality metrics (IDEA 54)
_MAX_TRAINING_JOBS = 100  # Maximum number of training jobs
_MAX_TRAINING_LOGS_PER_JOB = 1000  # Maximum log entries per job
_MAX_QUALITY_HISTORY_PER_JOB = 1000  # Maximum quality history entries per job
_training_job_timestamps: dict[str, float] = {}  # job_id -> creation_time


def _cleanup_old_training_jobs():
    """
    Clean up old training jobs and logs from storage.

    Removes jobs beyond MAX_TRAINING_JOBS (oldest first).
    """
    if len(_training_jobs) > _MAX_TRAINING_JOBS:
        sorted_jobs = sorted(
            _training_job_timestamps.items(),
            key=lambda x: x[1],
        )
        excess = len(_training_jobs) - _MAX_TRAINING_JOBS
        for job_id, _ in sorted_jobs[:excess]:
            _training_jobs.pop(job_id, None)
            _training_logs.pop(job_id, None)
            _training_job_timestamps.pop(job_id, None)
        logger.info(f"Cleaned up {excess} old training jobs from storage")


def _cleanup_training_logs(job_id: str):
    """
    Clean up old log entries for a training job.

    Keeps only the most recent MAX_TRAINING_LOGS_PER_JOB entries.
    """
    if job_id in _training_logs:
        logs = _training_logs[job_id]
        if len(logs) > _MAX_TRAINING_LOGS_PER_JOB:
            # Keep most recent logs
            _training_logs[job_id] = logs[-_MAX_TRAINING_LOGS_PER_JOB:]
            logger.debug(
                f"Cleaned up training logs for job {job_id}, "
                f"kept {_MAX_TRAINING_LOGS_PER_JOB} most recent entries"
            )


class TrainingDataset(BaseModel):
    """Training dataset information."""

    id: str
    name: str
    description: str | None = None
    audio_files: list[str]  # List of audio IDs or file paths
    transcripts: list[str] | None = None  # Optional transcripts for each audio file
    created: datetime
    modified: datetime


class TrainingRequest(BaseModel):
    """Request to start training."""

    dataset_id: str
    profile_id: str
    engine: str = "xtts"  # xtts, rvc, coqui
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

    type: str  # degradation, plateau, overfitting
    severity: str = "info"  # info, warning, error
    message: str
    epoch: int
    timestamp: datetime


class EarlyStoppingRecommendation(BaseModel):
    """Early stopping recommendation (IDEA 54)."""

    should_stop: bool
    reason: str
    confidence: float  # 0.0 to 1.0
    current_epoch: int
    best_epoch: int | None = None
    best_metrics: TrainingQualityMetrics | None = None


class TrainingStatus(BaseModel):
    """Training job status."""

    id: str
    dataset_id: str
    profile_id: str
    engine: str
    status: str  # pending, running, paused, completed, failed, cancelled
    progress: float  # 0.0 to 1.0
    current_epoch: int
    total_epochs: int
    loss: float | None = None
    started: datetime | None = None
    completed: datetime | None = None
    error_message: str | None = None

    # Quality metrics (IDEA 54)
    quality_score: float | None = None
    validation_loss: float | None = None
    quality_alerts: list[TrainingQualityAlert] | None = None
    early_stopping_recommendation: EarlyStoppingRecommendation | None = None


class TrainingLogEntry(BaseModel):
    """Single log entry from training."""

    timestamp: datetime
    level: str  # info, warning, error
    message: str
    epoch: int | None = None
    loss: float | None = None


class DatasetCreateRequest(BaseModel):
    """Request to create a dataset."""

    name: str
    description: str | None = None
    audio_files: list[str] | None = None


@router.post("/datasets", response_model=TrainingDataset)
async def create_dataset(request: DatasetCreateRequest):
    """Create a new training dataset."""
    try:
        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Dataset name is required")

        dataset_id = str(uuid.uuid4())
        dataset = TrainingDataset(
            id=dataset_id,
            name=request.name.strip(),
            description=request.description,
            audio_files=request.audio_files or [],
            created=datetime.utcnow(),
            modified=datetime.utcnow(),
        )
        # Store dataset (in production, use database)
        job_key = f"dataset_{dataset_id}"
        _training_jobs[job_key] = dataset.model_dump()
        _training_job_timestamps[job_key] = time.time()

        # Clean up old training jobs if needed
        if len(_training_jobs) > _MAX_TRAINING_JOBS:
            _cleanup_old_training_jobs()

        logger.info(f"Created training dataset: {dataset_id} ({request.name})")
        return dataset
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating training dataset: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create dataset: {e!s}")


@router.get("/datasets", response_model=list[TrainingDataset])
@cache_response(ttl=60)  # Cache for 60 seconds (dataset list doesn't change frequently)
async def list_datasets():
    """List all training datasets."""
    try:
        datasets = [
            TrainingDataset(**v) for k, v in _training_jobs.items() if k.startswith("dataset_")
        ]
        logger.debug(f"Listed {len(datasets)} training datasets")
        return datasets
    except Exception as e:
        logger.error(f"Error listing training datasets: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list datasets: {e!s}")


@router.get("/datasets/{dataset_id}", response_model=TrainingDataset)
@cache_response(ttl=300)  # Cache for 5 minutes (dataset info is relatively static)
async def get_dataset(dataset_id: str):
    """Get dataset by ID."""
    try:
        if not dataset_id or not dataset_id.strip():
            raise HTTPException(status_code=400, detail="Dataset ID is required")

        key = f"dataset_{dataset_id}"
        if key not in _training_jobs:
            logger.warning(f"Training dataset not found: {dataset_id}")
            raise HTTPException(status_code=404, detail="Dataset not found")

        logger.debug(f"Retrieved training dataset: {dataset_id}")
        return TrainingDataset(**_training_jobs[key])
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting training dataset {dataset_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get dataset: {e!s}")


class HyperparameterOptimizationRequest(BaseModel):
    """Request to optimize training hyperparameters."""

    dataset_id: str
    profile_id: str
    engine: str = "xtts"
    method: str = "optuna"  # optuna, hyperopt, ray[tune]
    n_trials: int = 20
    timeout_seconds: int | None = None
    hyperparameters: dict | None = None  # Custom hyperparameter space


class HyperparameterOptimizationResponse(BaseModel):
    """Response from hyperparameter optimization."""

    best_params: dict[str, Any]
    best_score: float
    optimization_method: str
    n_trials: int
    trials_completed: int
    optimization_time_seconds: float
    recommendations: list[str]


@router.post(
    "/hyperparameters/optimize",
    response_model=HyperparameterOptimizationResponse,
)
async def optimize_hyperparameters(
    request: HyperparameterOptimizationRequest,
):
    """
    Optimize training hyperparameters using ML optimization libraries.

    Uses optuna, hyperopt, or ray[tune] to find optimal hyperparameters
    for voice model training.
    """
    try:
        # Validate dataset exists
        dataset_key = f"dataset_{request.dataset_id}"
        if dataset_key not in _training_jobs:
            raise HTTPException(status_code=404, detail="Dataset not found")

        # Initialize optimizer
        optimizer = HyperparameterOptimizer()

        # Define hyperparameter space (can be customized)
        if request.hyperparameters:
            hyperparameter_space = request.hyperparameters
        else:
            # Default hyperparameter space for voice training
            hyperparameter_space = {
                "learning_rate": {
                    "type": "float",
                    "low": 1e-5,
                    "high": 1e-3,
                    "log": True,
                },
                "batch_size": {
                    "type": "int",
                    "low": 4,
                    "high": 32,
                },
                "weight_decay": {
                    "type": "float",
                    "low": 1e-6,
                    "high": 1e-3,
                    "log": True,
                },
            }

        # Run optimization
        import time

        start_time = time.time()
        result = optimizer.optimize(
            method=request.method,
            hyperparameter_space=hyperparameter_space,
            n_trials=request.n_trials,
            timeout_seconds=request.timeout_seconds,
        )
        optimization_time = time.time() - start_time

        if not result or "best_params" not in result:
            raise HTTPException(
                status_code=500,
                detail="Hyperparameter optimization failed to return results",
            )

        # Generate recommendations
        recommendations = []
        best_params = result["best_params"]
        if "learning_rate" in best_params:
            lr = best_params["learning_rate"]
            if lr < 1e-4:
                recommendations.append("Low learning rate detected - training may be slow")
            elif lr > 5e-4:
                recommendations.append("High learning rate detected - may cause instability")

        if "batch_size" in best_params:
            bs = best_params["batch_size"]
            if bs < 8:
                recommendations.append("Small batch size - consider GPU memory constraints")
            elif bs > 24:
                recommendations.append("Large batch size - ensure sufficient GPU memory")

        return HyperparameterOptimizationResponse(
            best_params=best_params,
            best_score=result.get("best_score", 0.0),
            optimization_method=request.method,
            n_trials=request.n_trials,
            trials_completed=result.get("n_trials_completed", request.n_trials),
            optimization_time_seconds=optimization_time,
            recommendations=recommendations,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Hyperparameter optimization failed: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"Hyperparameter optimization failed: {e!s}",
        )


@router.post("/datasets/{dataset_id}/optimize", response_model=TrainingDataOptimizationResponse)
async def optimize_training_data(
    dataset_id: str, req: TrainingDataOptimizationRequest
) -> TrainingDataOptimizationResponse:
    """
    Advanced training data optimization (IDEA 68).

    Analyzes training data quality, diversity, and coverage,
    and recommends optimal samples and augmentation strategies.
    """
    try:
        # Get dataset
        job_key = f"dataset_{dataset_id}"
        if job_key not in _training_jobs:
            raise HTTPException(status_code=404, detail="Dataset not found")

        dataset_dict = _training_jobs[job_key]
        dataset = TrainingDataset(**dataset_dict)

        # Analyze training data
        quality_score = 7.0  # Default
        diversity_score = 7.0  # Default
        coverage_score = 7.0  # Default
        optimal_samples = []
        recommendations = []
        augmentation_suggestions = []

        if req.analyze_quality and dataset.audio_files:
            # Analyze quality of audio files (ADR-008 compliant)
            try:
                from ..routes.audio import _get_audio_path

                engine_service = get_engine_service()
                quality_scores = []
                for audio_file in dataset.audio_files[:20]:  # Limit analysis
                    audio_path = (
                        _get_audio_path(audio_file)
                        if not os.path.exists(audio_file)
                        else audio_file
                    )
                    if audio_path and os.path.exists(audio_path):
                        try:
                            import numpy as np
                            import soundfile as sf

                            audio, _sr = sf.read(audio_path)
                            if len(audio.shape) > 1:
                                audio = np.mean(audio, axis=1)
                            mos = engine_service.calculate_mos_score(audio)
                            quality_scores.append((audio_file, mos))
                        except (OSError, ValueError, RuntimeError) as mos_err:
                            logger.debug(f"MOS calculation failed for {audio_file}: {mos_err}")

                if quality_scores:
                    # Calculate average quality
                    avg_quality = sum(score[1] for score in quality_scores) / len(quality_scores)
                    quality_score = min(10.0, (avg_quality / 5.0) * 10.0)  # Normalize to 1-10

                    # Select optimal samples (top quality)
                    quality_scores.sort(key=lambda x: x[1], reverse=True)
                    optimal_samples = [
                        sample[0] for sample in quality_scores[: min(10, len(quality_scores))]
                    ]

                    if quality_score < 6.0:
                        recommendations.append(
                            "Low average quality detected - consider improving audio quality"
                        )
                        augmentation_suggestions.append(
                            "Apply noise reduction to low-quality samples"
                        )
            except ImportError:
                logger.warning("Quality metrics not available for training data analysis")

        if req.analyze_diversity and dataset.audio_files:
            # Analyze diversity (simplified - would use audio features)
            diversity_score = min(
                10.0, len(dataset.audio_files) / 10.0
            )  # More files = more diversity

            if diversity_score < 5.0:
                recommendations.append("Low diversity detected - add more varied audio samples")
                augmentation_suggestions.append("Apply pitch shifting for diversity")
                augmentation_suggestions.append("Apply time stretching for diversity")
                augmentation_suggestions.append("Apply speed variation for diversity")

        if req.select_optimal and dataset.audio_files:
            # Select optimal samples if not already selected
            if not optimal_samples:
                optimal_samples = dataset.audio_files[: min(10, len(dataset.audio_files))]

        # Create optimized dataset if requested
        optimized_dataset_id = None
        quality_improvement = 0.0

        if optimal_samples and len(optimal_samples) < len(dataset.audio_files):
            # Create optimized dataset with selected samples
            optimized_dataset_id = f"{dataset_id}_optimized_{uuid.uuid4().hex[:8]}"
            optimized_dataset = TrainingDataset(
                id=optimized_dataset_id,
                name=f"{dataset.name} (Optimized)",
                description=f"Optimized version of {dataset.name}",
                audio_files=optimal_samples,
                transcripts=(
                    dataset.transcripts[: len(optimal_samples)] if dataset.transcripts else None
                ),
                created=datetime.utcnow(),
                modified=datetime.utcnow(),
            )

            opt_key = f"dataset_{optimized_dataset_id}"
            _training_jobs[opt_key] = optimized_dataset.model_dump()
            _training_job_timestamps[opt_key] = time.time()

            # Estimate quality improvement
            quality_improvement = min(1.0, (quality_score - 5.0) / 5.0)  # Normalize

        analysis = TrainingDataAnalysis(
            quality_score=quality_score,
            diversity_score=diversity_score,
            coverage_score=coverage_score,
            optimal_samples=optimal_samples,
            recommendations=recommendations,
            augmentation_suggestions=augmentation_suggestions,
        )

        return TrainingDataOptimizationResponse(
            dataset_id=dataset_id,
            analysis=analysis,
            optimized_dataset_id=optimized_dataset_id,
            quality_improvement=quality_improvement,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Training data optimization error: {e}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Training data optimization failed: {e!s}"
        ) from e


@router.post("/start", response_model=TrainingStatus)
async def start_training(request: TrainingRequest):
    """
    Start a training job.

    Validates dataset and profile, initializes training process,
    starts training in background, and returns training status.

    Note: Currently uses simulation mode. Real training integration
    with XTTSTrainer available in app/core/training/xtts_trainer.py
    for future implementation.
    """
    try:
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

        training_id = str(uuid.uuid4())

        # Validate dataset exists
        dataset_key = f"dataset_{request.dataset_id}"
        if dataset_key not in _training_jobs:
            logger.warning(f"Dataset not found for training: {request.dataset_id}")
            raise HTTPException(status_code=404, detail="Dataset not found")

        # Create training status
        status = TrainingStatus(
            id=training_id,
            dataset_id=request.dataset_id,
            profile_id=request.profile_id,
            engine=request.engine,
            status="pending",
            progress=0.0,
            current_epoch=0,
            total_epochs=request.epochs,
            started=None,
            completed=None,
        )

        # Store training job
        job_key = f"training_{training_id}"
        _training_jobs[job_key] = status.model_dump()
        _training_job_timestamps[job_key] = time.time()
        _training_logs[training_id] = []

        # Clean up old training jobs if needed
        if len(_training_jobs) > _MAX_TRAINING_JOBS:
            _cleanup_old_training_jobs()

        # Start training process in background
        # Try to use real XTTSTrainer if available, otherwise fall back to simulation
        asyncio.create_task(_start_real_training(training_id, request))

        logger.info(f"Training job started: {training_id}")
        return status

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to start training: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to start training: {e!s}")


async def _start_real_training(training_id: str, request: TrainingRequest):
    """
    Start real training using XTTSTrainer if available, otherwise use simulation.

    This function attempts to use the real XTTSTrainer from app/core/training/xtts_trainer.py
    for actual model fine-tuning. If Coqui TTS is not available, it falls back to
    simulation mode for testing purposes.
    """
    # Try to import XTTSTrainer for real training
    try:
        import sys
        from pathlib import Path

        # Add app directory to path if needed
        app_path = Path(__file__).parent.parent.parent.parent / "app"
        if str(app_path) not in sys.path:
            sys.path.insert(0, str(app_path))

        from core.training.xtts_trainer import XTTSTrainer

        # Real training is available
        await _execute_real_training(training_id, request)
        return
    except ImportError as e:
        logger.warning(
            f"XTTSTrainer not available ({e}), falling back to simulation mode. "
            "For real training, install Coqui TTS: pip install coqui-tts==0.27.2"
        )
        # Fall back to simulation
        await _simulate_training(training_id, request)


async def _execute_real_training(training_id: str, request: TrainingRequest):
    """
    Execute real training using XTTSTrainer.

    This integrates with the actual XTTSTrainer class to perform
    real model fine-tuning on the provided dataset.
    """
    try:
        from core.training.xtts_trainer import XTTSTrainer

        key = f"training_{training_id}"
        if key not in _training_jobs:
            return

        status_dict = _training_jobs[key]
        status_dict["status"] = "running"
        status_dict["started"] = datetime.utcnow().isoformat()

        # Get dataset information
        dataset_key = f"dataset_{request.dataset_id}"
        if dataset_key not in _training_jobs:
            raise ValueError(f"Dataset {request.dataset_id} not found")

        dataset = _training_jobs[dataset_key]
        audio_files = dataset.get("audio_files", [])

        if not audio_files:
            raise ValueError("Dataset has no audio files")

        # Initialize trainer
        trainer = XTTSTrainer(
            base_model=request.engine or "tts_models/multilingual/multi-dataset/xtts_v2",
            device=None,  # Auto-detect
            gpu=request.gpu if hasattr(request, "gpu") else True,
        )

        # Prepare dataset
        metadata_path = trainer.prepare_dataset(
            audio_files=audio_files,
            transcripts=None,  # Could be extended to support transcripts
            output_metadata=None,  # Use default location
        )

        # Initialize model
        if not trainer.initialize_model():
            raise RuntimeError("Failed to initialize XTTS model for training")

        # Progress callback for real-time updates
        def progress_callback(progress_data: dict):
            """Update training status with real progress."""
            if key not in _training_jobs:
                return

            status_dict = _training_jobs[key]
            epoch = progress_data.get("epoch", 0)
            total_epochs = progress_data.get("total_epochs", request.epochs)
            loss = progress_data.get("loss")

            status_dict["current_epoch"] = epoch
            status_dict["total_epochs"] = total_epochs
            status_dict["progress"] = epoch / total_epochs if total_epochs > 0 else 0.0
            if loss is not None:
                status_dict["loss"] = loss

            # Calculate and store quality metrics (IDEA 54)
            validation_loss = progress_data.get("validation_loss")
            if validation_loss is not None:
                status_dict["validation_loss"] = validation_loss

            # Calculate quality score from loss
            if loss is not None:
                from api.utils.training_quality import calculate_quality_score_from_loss

                quality_score = calculate_quality_score_from_loss(loss, validation_loss)
                status_dict["quality_score"] = quality_score

                # Store quality metrics history
                quality_metrics = {
                    "epoch": epoch,
                    "training_loss": loss,
                    "validation_loss": validation_loss,
                    "quality_score": quality_score,
                    "timestamp": datetime.utcnow().isoformat(),
                }
                if training_id not in _training_quality_history:
                    _training_quality_history[training_id] = []
                _training_quality_history[training_id].append(quality_metrics)

                # Clean up old quality history
                if len(_training_quality_history[training_id]) > _MAX_QUALITY_HISTORY_PER_JOB:
                    _training_quality_history[training_id] = _training_quality_history[training_id][
                        -_MAX_QUALITY_HISTORY_PER_JOB:
                    ]

                # Detect quality alerts and early stopping (every 5 epochs)
                if epoch >= 5 and epoch % 5 == 0:
                    from api.utils.training_quality import (
                        detect_overfitting,
                        detect_quality_degradation,
                        detect_quality_plateau,
                        recommend_early_stopping,
                    )

                    quality_alerts = []
                    degradation_alert = detect_quality_degradation(
                        _training_quality_history[training_id], epoch
                    )
                    if degradation_alert:
                        quality_alerts.append(degradation_alert)

                    plateau_alert = detect_quality_plateau(
                        _training_quality_history[training_id], epoch
                    )
                    if plateau_alert:
                        quality_alerts.append(plateau_alert)

                    overfitting_alert = detect_overfitting(
                        _training_quality_history[training_id], epoch
                    )
                    if overfitting_alert:
                        quality_alerts.append(overfitting_alert)

                    if quality_alerts:
                        status_dict["quality_alerts"] = quality_alerts

                    # Early stopping recommendation (check every 15 epochs)
                    if epoch >= 15:
                        early_stop_rec = recommend_early_stopping(
                            _training_quality_history[training_id], epoch, total_epochs
                        )
                        status_dict["early_stopping_recommendation"] = early_stop_rec

            # Add log entry
            if training_id not in _training_logs:
                _training_logs[training_id] = []
            _training_logs[training_id].append(
                {
                    "timestamp": datetime.utcnow().isoformat(),
                    "level": "info",
                    "message": progress_data.get("message", f"Epoch {epoch}/{total_epochs}"),
                    "epoch": epoch,
                    "loss": loss,
                }
            )

            # Clean up old log entries
            _cleanup_training_logs(training_id)

            # Broadcast via WebSocket if available
            if HAS_WEBSOCKET:
                asyncio.create_task(
                    realtime.broadcast_training_progress(
                        training_id=training_id,
                        progress_data={
                            "epoch": epoch,
                            "total_epochs": total_epochs,
                            "loss": loss,
                            "progress": status_dict["progress"],
                            "status": "running",
                        },
                    )
                )

        # Run training in executor to avoid blocking
        import concurrent.futures

        loop = asyncio.get_event_loop()
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)

        # Start training (this is a sync method, run in executor)
        def run_training():
            return trainer.train(
                metadata_path=metadata_path,
                epochs=request.epochs,
                batch_size=getattr(request, "batch_size", 4),
                learning_rate=getattr(request, "learning_rate", 0.0001),
                progress_callback=progress_callback,
                checkpoint_dir=None,  # Use default
            )

        # Run training in background thread
        training_result = await loop.run_in_executor(executor, run_training)

        # Mark as completed
        if key in _training_jobs:
            status_dict["status"] = "completed"
            status_dict["completed"] = datetime.utcnow().isoformat()
            status_dict["progress"] = 1.0
            status_dict["loss"] = training_result.get("final_loss", 0.0)

            _training_logs[training_id].append(
                {
                    "timestamp": datetime.utcnow().isoformat(),
                    "level": "info",
                    "message": "Training completed successfully",
                    "epoch": request.epochs,
                    "loss": status_dict["loss"],
                }
            )

            # Export model if training succeeded
            try:
                export_path = trainer.export_model(output_path=None)  # Use default
                logger.info(f"Trained model exported to: {export_path}")
            except Exception as export_error:
                logger.warning(f"Failed to export model: {export_error}")

    except Exception as e:
        logger.error(f"Real training error: {e}", exc_info=True)
        key = f"training_{training_id}"
        if key in _training_jobs:
            status_dict = _training_jobs[key]
            status_dict["status"] = "failed"
            status_dict["error_message"] = str(e)
            status_dict["completed"] = datetime.utcnow().isoformat()

            _training_logs[training_id].append(
                {
                    "timestamp": datetime.utcnow().isoformat(),
                    "level": "error",
                    "message": f"Training failed: {e!s}",
                }
            )


async def _simulate_training(training_id: str, request: TrainingRequest):
    """
    Simulate training progress for testing purposes.

    This fallback implementation is used when XTTSTrainer is not available.
    For real training, ensure Coqui TTS is installed: pip install coqui-tts==0.27.2
    """
    try:
        # Update status to running
        key = f"training_{training_id}"
        if key not in _training_jobs:
            return

        status_dict = _training_jobs[key]
        status_dict["status"] = "running"
        status_dict["started"] = datetime.utcnow().isoformat()

        # Simulate epochs
        for epoch in range(1, request.epochs + 1):
            await asyncio.sleep(0.5)  # Simulate training time

            if key not in _training_jobs:
                break  # Training was cancelled

            # Update progress
            progress = epoch / request.epochs
            loss = 1.0 - (progress * 0.8)  # Simulate decreasing loss
            validation_loss = loss + 0.1  # Simulate slightly higher validation loss

            status_dict["current_epoch"] = epoch
            status_dict["progress"] = progress
            status_dict["loss"] = loss
            status_dict["validation_loss"] = validation_loss

            # Calculate quality metrics (IDEA 54)
            from api.utils.training_quality import (
                calculate_quality_score_from_loss,
                detect_overfitting,
                detect_quality_degradation,
                detect_quality_plateau,
                recommend_early_stopping,
            )

            # Calculate quality score
            quality_score = calculate_quality_score_from_loss(loss, validation_loss)
            status_dict["quality_score"] = quality_score

            # Store quality metrics history
            quality_metrics = {
                "epoch": epoch,
                "training_loss": loss,
                "validation_loss": validation_loss,
                "quality_score": quality_score,
                "timestamp": datetime.utcnow().isoformat(),
            }
            if training_id not in _training_quality_history:
                _training_quality_history[training_id] = []
            _training_quality_history[training_id].append(quality_metrics)

            # Clean up old quality history entries
            if len(_training_quality_history[training_id]) > _MAX_QUALITY_HISTORY_PER_JOB:
                _training_quality_history[training_id] = _training_quality_history[training_id][
                    -_MAX_QUALITY_HISTORY_PER_JOB:
                ]

            # Detect quality alerts
            quality_alerts = []
            if epoch >= 5:
                degradation_alert = detect_quality_degradation(
                    _training_quality_history[training_id], epoch
                )
                if degradation_alert:
                    quality_alerts.append(degradation_alert)

                plateau_alert = detect_quality_plateau(
                    _training_quality_history[training_id], epoch
                )
                if plateau_alert:
                    quality_alerts.append(plateau_alert)

                overfitting_alert = detect_overfitting(
                    _training_quality_history[training_id], epoch
                )
                if overfitting_alert:
                    quality_alerts.append(overfitting_alert)

            if quality_alerts:
                status_dict["quality_alerts"] = quality_alerts

            # Generate early stopping recommendation
            if epoch >= 15:
                early_stop_rec = recommend_early_stopping(
                    _training_quality_history[training_id], epoch, request.epochs
                )
                status_dict["early_stopping_recommendation"] = early_stop_rec

            # Add log entry
            log_entry = {
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": f"Epoch {epoch}/{request.epochs} completed",
                "epoch": epoch,
                "loss": loss,
            }
            if training_id not in _training_logs:
                _training_logs[training_id] = []
            _training_logs[training_id].append(log_entry)

            # Clean up old log entries if needed
            _cleanup_training_logs(training_id)

            # Broadcast progress via WebSocket if available
            if HAS_WEBSOCKET:
                progress_data = {
                    "epoch": epoch,
                    "total_epochs": request.epochs,
                    "loss": loss,
                    "progress": progress,
                    "status": "running",
                }
                # Include quality metrics if available (IDEA 54)
                if "quality_score" in status_dict:
                    progress_data["quality_score"] = status_dict["quality_score"]
                if "validation_loss" in status_dict:
                    progress_data["validation_loss"] = status_dict["validation_loss"]

                await realtime.broadcast_training_progress(
                    training_id=training_id,
                    progress_data=progress_data,
                )

        # Mark as completed
        if key in _training_jobs:
            status_dict["status"] = "completed"
            status_dict["completed"] = datetime.utcnow().isoformat()
            status_dict["progress"] = 1.0

            log_entry = {
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": "Training completed successfully",
                "epoch": request.epochs,
                "loss": status_dict.get("loss", 0.2),
            }
            _training_logs[training_id].append(log_entry)

    except Exception as e:
        logger.error(f"Training simulation error: {e}", exc_info=True)
        key = f"training_{training_id}"
        if key in _training_jobs:
            status_dict = _training_jobs[key]
            status_dict["status"] = "failed"
            status_dict["error_message"] = str(e)
            status_dict["completed"] = datetime.utcnow().isoformat()


@router.get("/status/{training_id}", response_model=TrainingStatus)
@cache_response(ttl=5)  # Cache for 5 seconds (status changes frequently during training)
async def get_training_status(training_id: str):
    """Get training job status."""
    try:
        if not training_id or not training_id.strip():
            raise HTTPException(status_code=400, detail="Training ID is required")

        key = f"training_{training_id}"
        if key not in _training_jobs:
            logger.warning(f"Training job not found: {training_id}")
            raise HTTPException(status_code=404, detail="Training job not found")

        status_dict = _training_jobs[key]
        # Convert ISO strings back to datetime
        if status_dict.get("started"):
            status_dict["started"] = datetime.fromisoformat(status_dict["started"])
        if status_dict.get("completed"):
            status_dict["completed"] = datetime.fromisoformat(status_dict["completed"])

        # Convert quality alerts and early stopping recommendation (IDEA 54)
        if status_dict.get("quality_alerts"):
            status_dict["quality_alerts"] = [
                TrainingQualityAlert(**alert) if isinstance(alert, dict) else alert
                for alert in status_dict["quality_alerts"]
            ]
        if status_dict.get("early_stopping_recommendation"):
            rec = status_dict["early_stopping_recommendation"]
            if isinstance(rec, dict):
                # Convert best_metrics if present
                if rec.get("best_metrics"):
                    rec["best_metrics"] = TrainingQualityMetrics(**rec["best_metrics"])
                status_dict["early_stopping_recommendation"] = EarlyStoppingRecommendation(**rec)

        logger.debug(f"Retrieved training status: {training_id}")
        return TrainingStatus(**status_dict)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting training status {training_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get training status: {e!s}")


@router.get("/status", response_model=list[TrainingStatus])
@cache_response(ttl=10)  # Cache for 10 seconds (job list may change frequently)
async def list_training_jobs(
    profile_id: str | None = Query(None, description="Filter by profile ID"),
    status: str | None = Query(None, description="Filter by status"),
):
    """List all training jobs, optionally filtered."""
    try:
        jobs = [TrainingStatus(**v) for k, v in _training_jobs.items() if k.startswith("training_")]

        if profile_id:
            jobs = [j for j in jobs if j.profile_id == profile_id]

        if status:
            jobs = [j for j in jobs if j.status == status]

        logger.debug(f"Listed {len(jobs)} training jobs (profile_id={profile_id}, status={status})")
        return jobs
    except Exception as e:
        logger.error(f"Error listing training jobs: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list training jobs: {e!s}")


@router.get("/{training_id}/quality-history", response_model=list[TrainingQualityMetrics])
@cache_response(ttl=5)  # Cache for 5 seconds (quality history updates frequently during training)
async def get_training_quality_history(
    training_id: str,
    limit: int | None = Query(100, description="Maximum number of entries to return"),
):
    """
    Get quality metrics history for a training job (IDEA 54).

    Returns quality metrics for each epoch during training.
    """
    try:
        if not training_id or not training_id.strip():
            raise HTTPException(status_code=400, detail="Training ID is required")

        if training_id not in _training_quality_history:
            logger.debug(f"No quality history found for training job: {training_id}")
            return []

        history = _training_quality_history[training_id]

        # Limit results
        if limit and limit > 0:
            history = history[-limit:]

        # Convert to TrainingQualityMetrics objects
        metrics_list = []
        for entry in history:
            timestamp = entry.get("timestamp")
            if isinstance(timestamp, str):
                timestamp = datetime.fromisoformat(timestamp)
            elif timestamp is None:
                timestamp = datetime.utcnow()

            metrics_dict = {
                "epoch": entry.get("epoch", 0),
                "training_loss": entry.get("training_loss"),
                "validation_loss": entry.get("validation_loss"),
                "quality_score": entry.get("quality_score"),
                "mos_score": entry.get("mos_score"),
                "similarity": entry.get("similarity"),
                "naturalness": entry.get("naturalness"),
                "timestamp": timestamp,
            }
            metrics_list.append(TrainingQualityMetrics(**metrics_dict))

        logger.debug(
            f"Retrieved {len(metrics_list)} quality history entries for training: {training_id}"
        )
        return metrics_list

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting quality history for {training_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get quality history: {e!s}")


@router.post("/cancel/{training_id}", response_model=ApiOk)
async def cancel_training(training_id: str):
    """Cancel a running training job."""
    try:
        if not training_id or not training_id.strip():
            raise HTTPException(status_code=400, detail="Training ID is required")

        key = f"training_{training_id}"
        if key not in _training_jobs:
            logger.warning(f"Training job not found for cancellation: {training_id}")
            raise HTTPException(status_code=404, detail="Training job not found")

        status_dict = _training_jobs[key]
        if status_dict["status"] not in ["pending", "running", "paused"]:
            raise HTTPException(
                status_code=400,
                detail=f"Cannot cancel training job with status: {status_dict['status']}",
            )

        status_dict["status"] = "cancelled"
        status_dict["completed"] = datetime.utcnow().isoformat()

        # Add log entry
        if training_id not in _training_logs:
            _training_logs[training_id] = []
        _training_logs[training_id].append(
            {
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": "Training cancelled by user",
            }
        )

        logger.info(f"Cancelled training job: {training_id}")
        return ApiOk(message="Training cancelled")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error cancelling training job {training_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to cancel training: {e!s}")


@router.get("/logs/{training_id}", response_model=list[TrainingLogEntry])
@cache_response(ttl=5)  # Cache for 5 seconds (logs update frequently during training)
async def get_training_logs(
    training_id: str,
    limit: int | None = Query(100, description="Maximum number of log entries to return"),
):
    """Get training logs for a training job."""
    try:
        if not training_id or not training_id.strip():
            raise HTTPException(status_code=400, detail="Training ID is required")
        if limit is not None and limit <= 0:
            raise HTTPException(status_code=400, detail="Limit must be greater than 0")

        if training_id not in _training_logs:
            logger.debug(f"No logs found for training job: {training_id}")
            return []

        logs = _training_logs[training_id]
        effective_limit = min(limit or 100, len(logs))

        # Convert ISO strings to datetime
        log_entries = []
        for log in logs[-effective_limit:]:  # Get last N entries
            log["timestamp"] = datetime.fromisoformat(log["timestamp"])
            log_entries.append(TrainingLogEntry(**log))

        logger.debug(f"Retrieved {len(log_entries)} log entries for training job: {training_id}")
        return log_entries
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting training logs for {training_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get training logs: {e!s}")


@router.delete("/{training_id}", response_model=ApiOk)
async def delete_training_job(training_id: str):
    """Delete a training job."""
    try:
        if not training_id or not training_id.strip():
            raise HTTPException(status_code=400, detail="Training ID is required")

        key = f"training_{training_id}"
        if key not in _training_jobs:
            logger.warning(f"Training job not found for deletion: {training_id}")
            raise HTTPException(status_code=404, detail="Training job not found")

        # Only allow deletion of completed, failed, or cancelled jobs
        status_dict = _training_jobs[key]
        if status_dict["status"] in ["pending", "running", "paused"]:
            raise HTTPException(
                status_code=400,
                detail="Cannot delete active training job. Cancel it first.",
            )

        del _training_jobs[key]
        _training_logs.pop(training_id, None)

        logger.info(f"Deleted training job: {training_id}")
        return ApiOk(message="Training job deleted")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting training job {training_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete training job: {e!s}")


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


@router.post("/export", response_model=ModelExportResponse)
async def export_trained_model(request: ModelExportRequest, http_request: Request):
    """Export a trained model as a ZIP package."""
    # Get request ID from middleware
    request_id = getattr(http_request.state, "request_id", None)

    # Instrument export flow
    from ..utils.instrumentation import EventType, instrument_flow

    with instrument_flow(
        EventType.EXPORT_START,
        EventType.EXPORT_COMPLETE,
        EventType.EXPORT_ERROR,
        request_id=request_id,
        training_id=request.training_id,
        profile_id=request.profile_id,
    ):
        try:
            # Get training job
            key = f"training_{request.training_id}"
            if key not in _training_jobs:
                raise HTTPException(status_code=404, detail="Training job not found")

            status_dict = _training_jobs[key]
            if status_dict["status"] != "completed":
                raise HTTPException(
                    status_code=400, detail="Can only export completed training jobs"
                )

            # Get model path from training job
            output_path = status_dict.get("output_path")
            if not output_path:
                # Try standard location
                output_path = f"models/trained/{request.training_id}/exported_model"

            model_dir = Path(output_path)
            if not model_dir.exists():
                raise HTTPException(status_code=404, detail="Model files not found")

            # Create export directory
            export_id = str(uuid.uuid4())
            export_dir = Path("models/exports") / export_id
            export_dir.mkdir(parents=True, exist_ok=True)

            # Create ZIP archive
            zip_path = export_dir / f"model_{export_id}.zip"
            with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zipf:
                # Add model files
                for root, _dirs, files in os.walk(model_dir):
                    for file in files:
                        file_path = Path(root) / file
                        arcname = file_path.relative_to(model_dir.parent)
                        zipf.write(file_path, arcname)

                # Add metadata
                metadata = {
                    "export_id": export_id,
                    "training_id": request.training_id,
                    "profile_id": request.profile_id,
                    "exported": datetime.utcnow().isoformat(),
                    "model_type": status_dict.get("engine", "xtts"),
                    "training_metadata": (status_dict if request.include_metadata else None),
                }
                zipf.writestr("metadata.json", json.dumps(metadata, indent=2))

            response = ModelExportResponse(
                export_id=export_id,
                model_path=str(model_dir),
                export_path=str(zip_path),
                created=datetime.utcnow(),
            )

            logger.info(f"Model exported: {export_id} -> {zip_path}")
            return response
        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Failed to export model: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to export model: {e!s}")


@router.post("/import", response_model=TrainingStatus)
async def import_trained_model(
    file: UploadFile = File(...),
    profile_id: str | None = Query(None),
    request: Request | None = None,
):
    """Import a trained model from a ZIP package."""
    # Get request ID from middleware (Request is injected by FastAPI)
    request_id = getattr(request.state, "request_id", None) if request else None

    # Instrument import flow
    from ..utils.instrumentation import EventType, instrument_flow

    with instrument_flow(
        EventType.IMPORT_START,
        EventType.IMPORT_COMPLETE,
        EventType.IMPORT_ERROR,
        request_id=request_id,
        profile_id=profile_id,
        filename=file.filename if file else None,
    ):
        try:
            import_id = str(uuid.uuid4())
            training_id = str(uuid.uuid4())

            # Read and validate uploaded archive file
            content = await file.read()
            try:
                validate_archive_file(content, filename=file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid archive file: {e.message}",
                ) from e

            # Create import directory
            import_dir = Path("models/imports") / import_id
            import_dir.mkdir(parents=True, exist_ok=True)

            # Save uploaded file
            zip_path = import_dir / (file.filename or "upload.zip")
            with open(zip_path, "wb") as f:
                f.write(content)

            # Extract ZIP
            extract_dir = import_dir / "extracted"
            extract_dir.mkdir(exist_ok=True)

            with zipfile.ZipFile(zip_path, "r") as zipf:
                zipf.extractall(extract_dir)

            # Load metadata
            metadata_path = extract_dir / "metadata.json"
            if not metadata_path.exists():
                raise HTTPException(
                    status_code=400,
                    detail="Invalid model package: missing metadata.json",
                )

            with open(metadata_path) as f:
                metadata = json.load(f)

            # Find model directory
            model_dir = extract_dir / "model"
            if not model_dir.exists():
                # Try to find model files
                model_files = list(extract_dir.glob("*.pth")) + list(extract_dir.glob("*.pt"))
                if model_files:
                    model_dir = extract_dir
                else:
                    raise HTTPException(status_code=400, detail="Model files not found in package")

            # Move model to trained models directory
            final_model_dir = Path("models/trained") / training_id
            final_model_dir.mkdir(parents=True, exist_ok=True)
            exported_dir = final_model_dir / "exported_model"

            shutil.copytree(model_dir, exported_dir, dirs_exist_ok=True)
            shutil.copy2(metadata_path, final_model_dir / "metadata.json")

            # Create training status for imported model
            original_metadata = metadata.get("training_metadata", {})
            status = TrainingStatus(
                id=training_id,
                dataset_id="",  # Imported models don't have dataset
                profile_id=profile_id or metadata.get("profile_id"),
                engine=metadata.get("model_type", "xtts"),
                status="completed",
                progress=1.0,
                current_epoch=original_metadata.get("total_epochs", 0),
                total_epochs=original_metadata.get("total_epochs", 0),
                loss=original_metadata.get("loss"),
                started=(
                    datetime.fromisoformat(metadata["exported"])
                    if metadata.get("exported")
                    else None
                ),
                completed=datetime.utcnow(),
                error_message=None,
            )

            # Store training job
            status_dict = status.model_dump()
            status_dict["output_path"] = str(exported_dir)
            _training_jobs[f"training_{training_id}"] = status_dict

            logger.info(f"Model imported: {import_id} -> {final_model_dir}")
            return status
        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Failed to import model: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to import model: {e!s}")


@router.get("/exports/{export_id}/download")
async def download_export(export_id: str):
    """Download exported model ZIP file."""
    export_dir = Path("models/exports") / export_id
    zip_files = list(export_dir.glob("*.zip"))

    if not zip_files:
        raise HTTPException(status_code=404, detail="Export not found")

    zip_path = zip_files[0]
    return FileResponse(path=str(zip_path), filename=zip_path.name, media_type="application/zip")
