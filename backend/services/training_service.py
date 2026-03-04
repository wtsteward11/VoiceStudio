"""
M4: Training service — owns dataset, job, and training execution logic.

Routes call TrainingService functions. No register_handler stubs.
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

from backend.config.path_config import get_path
from backend.services.persistent_store import PersistentStore
from backend.services.training_broadcaster import get_broadcaster

logger = logging.getLogger(__name__)

# Stores (owned by service) — datasets and jobs are split to avoid cleanup footgun
_datasets_store: PersistentStore = PersistentStore("training_datasets")
_datasets_timestamps: PersistentStore = PersistentStore("training_datasets_timestamps")
_training_jobs_store: PersistentStore = PersistentStore("training_jobs")
_training_logs: PersistentStore = PersistentStore("training_logs")
_training_quality_history: PersistentStore = PersistentStore("training_quality_history")
_training_job_timestamps: PersistentStore = PersistentStore("training_job_timestamps")
_MAX_DATASETS = 200
_MAX_TRAINING_JOBS = 100
_MAX_TRAINING_LOGS_PER_JOB = 1000
_MAX_QUALITY_HISTORY_PER_JOB = 1000

# One-time migration: move dataset_* keys from legacy training_jobs into _datasets_store
_MIGRATION_DONE_KEY = "_datasets_migrated_v1"


def _migrate_datasets_from_legacy() -> None:
    """Migrate dataset_* keys from training_jobs into training_datasets (one-time)."""
    if _datasets_timestamps.get(_MIGRATION_DONE_KEY):
        return
    migrated = 0
    for key in list(_training_jobs_store.keys()):
        if key.startswith("dataset_"):
            val = _training_jobs_store.get(key)
            ts = _training_job_timestamps.get(key)
            if val is not None:
                _datasets_store[key] = val
                _datasets_timestamps[key] = ts if ts is not None else time.time()
                del _training_jobs_store[key]
                _training_job_timestamps.pop(key, None)
                migrated += 1
    if migrated > 0:
        logger.info("Migrated %d datasets from legacy training_jobs to training_datasets", migrated)
    _datasets_timestamps[_MIGRATION_DONE_KEY] = True


# Run migration on first import
_migrate_datasets_from_legacy()

_training_repo: Any = None


def _get_training_repo():
    """Lazy-init the database-backed training job repository."""
    global _training_repo
    if _training_repo is None:
        try:
            from backend.data.repositories.training_repository import TrainingJobRepository

            _training_repo = TrainingJobRepository()
            logger.info("Training job repository initialized (database-backed)")
        except Exception as e:
            logger.warning("Failed to initialize TrainingJobRepository, using in-memory only: %s", e)
    return _training_repo


def _persist_training_job(job_id: str, job_data: dict) -> None:
    """Persist a training job to the database repository."""
    repo = _get_training_repo()
    if repo is None:
        return
    try:
        from backend.data.repositories.training_repository import TrainingJobEntity

        entity = TrainingJobEntity(
            id=job_id,
            dataset_id=job_data.get("dataset_id"),
            engine_id=job_data.get("engine_id"),
            model_name=job_data.get("model_name", ""),
            status=job_data.get("status", "pending"),
            progress=job_data.get("progress", 0.0),
            current_epoch=job_data.get("current_epoch", 0),
            total_epochs=job_data.get("total_epochs", 0),
            metrics=job_data.get("metrics"),
            hyperparameters=job_data.get("hyperparameters"),
            error=job_data.get("error"),
            output_path=job_data.get("output_path"),
        )
        repo.save(entity)
    except Exception as e:
        logger.debug("Failed to persist training job %s: %s", job_id, e)


def _cleanup_old_datasets() -> None:
    """Clean up old datasets from storage (datasets only, never jobs)."""
    job_keys = [k for k in _datasets_timestamps if k != _MIGRATION_DONE_KEY]
    if len(job_keys) <= _MAX_DATASETS:
        return
    sorted_items = sorted(
        ((k, _datasets_timestamps.get(k, 0)) for k in job_keys),
        key=lambda x: x[1],
    )
    excess = len(job_keys) - _MAX_DATASETS
    for key, _ in sorted_items[:excess]:
        _datasets_store.pop(key, None)
        _datasets_timestamps.pop(key, None)
    logger.info("Cleaned up %d old datasets from storage", excess)


def _cleanup_old_training_jobs() -> None:
    """Clean up old training jobs and logs from storage (jobs only, never datasets)."""
    if len(_training_jobs_store) > _MAX_TRAINING_JOBS:
        sorted_jobs = sorted(_training_job_timestamps.items(), key=lambda x: x[1])
        excess = len(_training_jobs_store) - _MAX_TRAINING_JOBS
        for job_id, _ in sorted_jobs[:excess]:
            _training_jobs_store.pop(job_id, None)
            if job_id.startswith("training_"):
                _training_logs.pop(job_id.replace("training_", "", 1), None)
            _training_job_timestamps.pop(job_id, None)
        logger.info("Cleaned up %d old training jobs from storage", excess)


def _cleanup_training_logs(job_id: str) -> None:
    """Clean up old log entries for a training job."""
    if job_id in _training_logs:
        logs = _training_logs[job_id]
        if len(logs) > _MAX_TRAINING_LOGS_PER_JOB:
            _training_logs[job_id] = logs[-_MAX_TRAINING_LOGS_PER_JOB:]
            logger.debug(
                "Cleaned up training logs for job %s, kept %d most recent entries",
                job_id,
                _MAX_TRAINING_LOGS_PER_JOB,
            )


# --- Dataset operations ---


def create_dataset(name: str, description: str | None = None, audio_files: list[str] | None = None) -> dict:
    """Create a new training dataset."""
    dataset_id = str(uuid.uuid4())
    now = datetime.utcnow()
    now_iso = now.isoformat()
    dataset = {
        "id": dataset_id,
        "name": name.strip(),
        "description": description,
        "audio_files": audio_files or [],
        "transcripts": None,
        "created": now_iso,
        "modified": now_iso,
    }
    job_key = f"dataset_{dataset_id}"
    _datasets_store[job_key] = dataset
    _datasets_timestamps[job_key] = time.time()
    if len(_datasets_store) > _MAX_DATASETS:
        _cleanup_old_datasets()
    logger.info("Created training dataset: %s (%s)", dataset_id, name)
    return dataset


def list_datasets() -> list[dict]:
    """List all training datasets."""
    return [dict(v) for k, v in _datasets_store.items() if k.startswith("dataset_")]


def get_dataset(dataset_id: str) -> dict | None:
    """Get dataset by ID."""
    key = f"dataset_{dataset_id}"
    if key not in _datasets_store:
        return None
    return dict(_datasets_store[key])


# --- Training data optimization ---


def optimize_training_data(
    dataset_id: str,
    analyze_quality: bool = True,
    analyze_diversity: bool = True,
    select_optimal: bool = True,
) -> dict:
    """Optimize training data (quality, diversity, optimal samples)."""
    from backend.ml.models.engine_service import get_engine_service

    job_key = f"dataset_{dataset_id}"
    if job_key not in _datasets_store:
        return None
    dataset_dict = _datasets_store[job_key]
    dataset = dict(dataset_dict)
    audio_files = dataset.get("audio_files", [])

    quality_score = 7.0
    diversity_score = 7.0
    coverage_score = 7.0
    optimal_samples = []
    recommendations = []
    augmentation_suggestions = []
    optimized_dataset_id = None
    quality_improvement = 0.0

    if analyze_quality and audio_files:
        try:
            from backend.services.audio_path_resolver import resolve_audio_path

            engine_service = get_engine_service()
            quality_scores = []
            for audio_file in audio_files[:20]:
                audio_path = resolve_audio_path(audio_file) if not os.path.exists(audio_file) else audio_file
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
                        logger.debug("MOS calculation failed for %s: %s", audio_file, mos_err)

            if quality_scores:
                avg_quality = sum(s[1] for s in quality_scores) / len(quality_scores)
                quality_score = min(10.0, (avg_quality / 5.0) * 10.0)
                quality_scores.sort(key=lambda x: x[1], reverse=True)
                optimal_samples = [s[0] for s in quality_scores[: min(10, len(quality_scores))]]
                if quality_score < 6.0:
                    recommendations.append("Low average quality detected - consider improving audio quality")
                    augmentation_suggestions.append("Apply noise reduction to low-quality samples")
        except ImportError:
            logger.warning("Quality metrics not available for training data analysis")

    if analyze_diversity and audio_files:
        diversity_score = min(10.0, len(audio_files) / 10.0)
        if diversity_score < 5.0:
            recommendations.append("Low diversity detected - add more varied audio samples")
            augmentation_suggestions.extend([
                "Apply pitch shifting for diversity",
                "Apply time stretching for diversity",
                "Apply speed variation for diversity",
            ])

    if select_optimal and audio_files and not optimal_samples:
        optimal_samples = audio_files[: min(10, len(audio_files))]

    if optimal_samples and len(optimal_samples) < len(audio_files):
        optimized_dataset_id = f"{dataset_id}_optimized_{uuid.uuid4().hex[:8]}"
        opt_dataset = {
            "id": optimized_dataset_id,
            "name": f"{dataset.get('name', '')} (Optimized)",
            "description": f"Optimized version of {dataset.get('name', '')}",
            "audio_files": optimal_samples,
            "transcripts": dataset.get("transcripts", [])[: len(optimal_samples)] if dataset.get("transcripts") else None,
            "created": datetime.utcnow().isoformat(),
            "modified": datetime.utcnow().isoformat(),
        }
        opt_key = f"dataset_{optimized_dataset_id}"
        _datasets_store[opt_key] = opt_dataset
        _datasets_timestamps[opt_key] = time.time()
        quality_improvement = min(1.0, (quality_score - 5.0) / 5.0)

    return {
        "dataset_id": dataset_id,
        "analysis": {
            "quality_score": quality_score,
            "diversity_score": diversity_score,
            "coverage_score": coverage_score,
            "optimal_samples": optimal_samples,
            "recommendations": recommendations,
            "augmentation_suggestions": augmentation_suggestions,
        },
        "optimized_dataset_id": optimized_dataset_id,
        "quality_improvement": quality_improvement,
    }


# --- Hyperparameter optimization ---


def optimize_hyperparameters(
    dataset_id: str,
    method: str = "optuna",
    n_trials: int = 20,
    timeout_seconds: int | None = None,
    hyperparameters: dict | None = None,
) -> dict | None:
    """Run hyperparameter optimization."""
    from backend.services.ml_optimization import HyperparameterOptimizer

    dataset_key = f"dataset_{dataset_id}"
    if dataset_key not in _datasets_store:
        return None

    optimizer = HyperparameterOptimizer()
    hyperparameter_space = hyperparameters or {
        "learning_rate": {"type": "float", "low": 1e-5, "high": 1e-3, "log": True},
        "batch_size": {"type": "int", "low": 4, "high": 32},
        "weight_decay": {"type": "float", "low": 1e-6, "high": 1e-3, "log": True},
    }

    start_time = time.time()
    result = optimizer.optimize(
        method=method,
        hyperparameter_space=hyperparameter_space,
        n_trials=n_trials,
        timeout_seconds=timeout_seconds,
    )
    optimization_time = time.time() - start_time

    if not result or "best_params" not in result:
        return None

    recommendations = []
    best_params = result.get("best_params", {})
    if best_params.get("learning_rate", 0) < 1e-4:
        recommendations.append("Low learning rate detected - training may be slow")
    elif best_params.get("learning_rate", 0) > 5e-4:
        recommendations.append("High learning rate detected - may cause instability")
    if best_params.get("batch_size", 0) < 8:
        recommendations.append("Small batch size - consider GPU memory constraints")
    elif best_params.get("batch_size", 0) > 24:
        recommendations.append("Large batch size - ensure sufficient GPU memory")

    return {
        "best_params": best_params,
        "best_score": result.get("best_score", 0.0),
        "optimization_method": method,
        "n_trials": n_trials,
        "trials_completed": result.get("n_trials_completed", n_trials),
        "optimization_time_seconds": optimization_time,
        "recommendations": recommendations,
    }


# --- Job control ---


def start_training(
    dataset_id: str,
    profile_id: str,
    engine: str = "xtts",
    epochs: int = 100,
    batch_size: int = 4,
    learning_rate: float = 0.0001,
    gpu: bool = True,
    output_path: str | None = None,
    on_started: Any = None,
) -> dict | None:
    """Start a training job. Returns status dict or None if dataset not found."""
    dataset_key = f"dataset_{dataset_id}"
    if dataset_key not in _datasets_store:
        return None

    training_id = str(uuid.uuid4())
    status = {
        "id": training_id,
        "dataset_id": dataset_id,
        "profile_id": profile_id,
        "engine": engine,
        "status": "pending",
        "progress": 0.0,
        "current_epoch": 0,
        "total_epochs": epochs,
        "started": None,
        "completed": None,
        "error_message": None,
    }

    job_key = f"training_{training_id}"
    _training_jobs_store[job_key] = status
    _training_job_timestamps[job_key] = time.time()
    _training_logs[training_id] = []

    if len(_training_jobs_store) > _MAX_TRAINING_JOBS:
        _cleanup_old_training_jobs()

    if on_started:
        asyncio.create_task(on_started(training_id, {
            "dataset_id": dataset_id,
            "profile_id": profile_id,
            "engine": engine,
            "epochs": epochs,
            "batch_size": batch_size,
            "learning_rate": learning_rate,
            "gpu": gpu,
            "output_path": output_path,
        }))

    logger.info("Training job started: %s", training_id)
    return status


def get_training_status(training_id: str) -> dict | None:
    """Get training job status."""
    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return None
    return dict(_training_jobs_store[key])


def list_training_jobs(profile_id: str | None = None, status_filter: str | None = None) -> list[dict]:
    """List training jobs with optional filters."""
    jobs = [dict(v) for k, v in _training_jobs_store.items() if k.startswith("training_")]
    if profile_id:
        jobs = [j for j in jobs if j.get("profile_id") == profile_id]
    if status_filter:
        jobs = [j for j in jobs if j.get("status") == status_filter]
    return jobs


def get_quality_history(training_id: str, limit: int | None = 100) -> list[dict]:
    """Get quality metrics history for a training job."""
    if training_id not in _training_quality_history:
        return []
    history = _training_quality_history[training_id]
    if limit and limit > 0:
        history = history[-limit:]
    return list(history)


def cancel_training(training_id: str) -> bool:
    """Cancel a running training job. Returns True if cancelled."""
    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return False
    status_dict = _training_jobs_store[key]
    if status_dict.get("status") not in ("pending", "running", "paused"):
        return False
    status_dict["status"] = "cancelled"
    status_dict["completed"] = datetime.utcnow().isoformat()
    if training_id not in _training_logs:
        _training_logs[training_id] = []
    _training_logs[training_id].append({
        "timestamp": datetime.utcnow().isoformat(),
        "level": "info",
        "message": "Training cancelled by user",
    })
    logger.info("Cancelled training job: %s", training_id)
    return True


def get_training_logs(training_id: str, limit: int = 100) -> list[dict]:
    """Get training logs for a job."""
    if training_id not in _training_logs:
        return []
    logs = _training_logs[training_id]
    effective_limit = min(limit, len(logs))
    return list(logs[-effective_limit:])


def delete_training_job(training_id: str) -> bool:
    """Delete a training job. Returns True if deleted."""
    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return False
    status_dict = _training_jobs_store[key]
    if status_dict.get("status") in ("pending", "running", "paused"):
        return False
    del _training_jobs_store[key]
    _training_logs.pop(training_id, None)
    logger.info("Deleted training job: %s", training_id)
    return True


# --- Training execution (real or simulation) ---


async def run_training(
    training_id: str,
    dataset_id: str,
    profile_id: str,
    engine: str = "xtts",
    epochs: int = 100,
    batch_size: int = 4,
    learning_rate: float = 0.0001,
    gpu: bool = True,
) -> None:
    """Start real or simulated training. Called as background task."""
    try:
        import backend.training.facade
        await _execute_real_training(training_id, dataset_id, profile_id, engine, epochs, batch_size, learning_rate, gpu)
    except ImportError as e:
        logger.warning(
            "XTTSTrainer not available (%s), falling back to simulation. "
            "For real training, install Coqui TTS: pip install coqui-tts==0.27.2",
            e,
        )
        await _simulate_training(training_id, epochs, batch_size, learning_rate)


async def _execute_real_training(
    training_id: str,
    dataset_id: str,
    profile_id: str,
    engine: str,
    epochs: int,
    batch_size: int,
    learning_rate: float,
    gpu: bool,
) -> None:
    """Execute real training using XTTSTrainer."""
    from backend.services.training_quality import (
        calculate_quality_score_from_loss,
        detect_overfitting,
        detect_quality_degradation,
        detect_quality_plateau,
        recommend_early_stopping,
    )

    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return

    status_dict = _training_jobs_store[key]
    status_dict["status"] = "running"
    status_dict["started"] = datetime.utcnow().isoformat()

    dataset_key = f"dataset_{dataset_id}"
    if dataset_key not in _datasets_store:
        status_dict["status"] = "failed"
        status_dict["error_message"] = f"Dataset {dataset_id} not found"
        return

    dataset = _datasets_store[dataset_key]
    audio_files = dataset.get("audio_files", [])
    if not audio_files:
        status_dict["status"] = "failed"
        status_dict["error_message"] = "Dataset has no audio files"
        return

    from backend.training.facade import XTTSTrainer

    trainer = XTTSTrainer(
        base_model=engine or "tts_models/multilingual/multi-dataset/xtts_v2",
        device=None,
        gpu=gpu,
    )

    metadata_path = trainer.prepare_dataset(
        audio_files=audio_files,
        transcripts=None,
        output_metadata=None,
    )

    if not trainer.initialize_model():
        status_dict["status"] = "failed"
        status_dict["error_message"] = "Failed to initialize XTTS model"
        return

    def progress_callback(progress_data: dict) -> None:
        if key not in _training_jobs_store:
            return
        sd = _training_jobs_store[key]
        epoch = progress_data.get("epoch", 0)
        total_epochs = progress_data.get("total_epochs", epochs)
        loss = progress_data.get("loss")
        sd["current_epoch"] = epoch
        sd["total_epochs"] = total_epochs
        sd["progress"] = epoch / total_epochs if total_epochs > 0 else 0.0
        if loss is not None:
            sd["loss"] = loss
        validation_loss = progress_data.get("validation_loss")
        if validation_loss is not None:
            sd["validation_loss"] = validation_loss
        if loss is not None:
            quality_score = calculate_quality_score_from_loss(loss, validation_loss)
            sd["quality_score"] = quality_score
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
            if len(_training_quality_history[training_id]) > _MAX_QUALITY_HISTORY_PER_JOB:
                _training_quality_history[training_id] = _training_quality_history[training_id][
                    -_MAX_QUALITY_HISTORY_PER_JOB:
                ]
            if epoch >= 5 and epoch % 5 == 0:
                quality_alerts = []
                for detect in (detect_quality_degradation, detect_quality_plateau, detect_overfitting):
                    alert = detect(_training_quality_history[training_id], epoch)
                    if alert:
                        quality_alerts.append(alert)
                if quality_alerts:
                    sd["quality_alerts"] = quality_alerts
                if epoch >= 15:
                    sd["early_stopping_recommendation"] = recommend_early_stopping(
                        _training_quality_history[training_id], epoch, total_epochs
                    )
        if training_id not in _training_logs:
            _training_logs[training_id] = []
        _training_logs[training_id].append({
            "timestamp": datetime.utcnow().isoformat(),
            "level": "info",
            "message": progress_data.get("message", f"Epoch {epoch}/{total_epochs}"),
            "epoch": epoch,
            "loss": loss,
        })
        _cleanup_training_logs(training_id)
        try:
            asyncio.create_task(get_broadcaster().broadcast_training_progress(
                training_id=training_id,
                progress_data={
                    "epoch": epoch,
                    "total_epochs": total_epochs,
                    "loss": loss,
                    "progress": sd["progress"],
                    "status": "running",
                },
            ))
        except Exception:
            pass

    import concurrent.futures

    loop = asyncio.get_event_loop()
    executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)

    def run_training_sync():
        return trainer.train(
            metadata_path=metadata_path,
            epochs=epochs,
            batch_size=batch_size,
            learning_rate=learning_rate,
            progress_callback=progress_callback,
            checkpoint_dir=None,
        )

    try:
        training_result = await loop.run_in_executor(executor, run_training_sync)
        if key in _training_jobs_store:
            sd = _training_jobs_store[key]
            sd["status"] = "completed"
            sd["completed"] = datetime.utcnow().isoformat()
            sd["progress"] = 1.0
            sd["loss"] = training_result.get("final_loss", 0.0)
            _training_logs[training_id].append({
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": "Training completed successfully",
                "epoch": epochs,
                "loss": sd["loss"],
            })
            try:
                default_output = str(get_path("models") / "trained" / training_id / "exported_model")
                export_path = trainer.export_model(output_path=default_output)
                sd["output_path"] = export_path
                logger.info("Trained model exported to: %s", export_path)
            except Exception as export_error:
                logger.warning("Failed to export model: %s", export_error)
    except Exception as e:
        logger.error("Real training error: %s", e, exc_info=True)
        if key in _training_jobs_store:
            sd = _training_jobs_store[key]
            sd["status"] = "failed"
            sd["error_message"] = str(e)
            sd["completed"] = datetime.utcnow().isoformat()
            _training_logs[training_id].append({
                "timestamp": datetime.utcnow().isoformat(),
                "level": "error",
                "message": f"Training failed: {e!s}",
            })


async def _simulate_training(
    training_id: str,
    epochs: int,
    batch_size: int,
    learning_rate: float,
) -> None:
    """Simulate training progress for testing."""
    from backend.services.training_quality import (
        calculate_quality_score_from_loss,
        detect_overfitting,
        detect_quality_degradation,
        detect_quality_plateau,
        recommend_early_stopping,
    )

    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return

    status_dict = _training_jobs_store[key]
    status_dict["status"] = "running"
    status_dict["started"] = datetime.utcnow().isoformat()
    status_dict["simulation_mode"] = True
    status_dict["simulation_reason"] = (
        "Coqui TTS not installed. Install with: pip install coqui-tts==0.27.2"
    )

    try:
        for epoch in range(1, epochs + 1):
            await asyncio.sleep(0.5)
            if key not in _training_jobs_store:
                break
            progress = epoch / epochs
            loss = 1.0 - (progress * 0.8)
            validation_loss = loss + 0.1
            status_dict["current_epoch"] = epoch
            status_dict["progress"] = progress
            status_dict["loss"] = loss
            status_dict["validation_loss"] = validation_loss
            quality_score = calculate_quality_score_from_loss(loss, validation_loss)
            status_dict["quality_score"] = quality_score
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
            if len(_training_quality_history[training_id]) > _MAX_QUALITY_HISTORY_PER_JOB:
                _training_quality_history[training_id] = _training_quality_history[training_id][
                    -_MAX_QUALITY_HISTORY_PER_JOB:
                ]
            if epoch >= 5:
                quality_alerts = []
                for detect in (detect_quality_degradation, detect_quality_plateau, detect_overfitting):
                    alert = detect(_training_quality_history[training_id], epoch)
                    if alert:
                        quality_alerts.append(alert)
                if quality_alerts:
                    status_dict["quality_alerts"] = quality_alerts
                if epoch >= 15:
                    status_dict["early_stopping_recommendation"] = recommend_early_stopping(
                        _training_quality_history[training_id], epoch, epochs
                    )
            if training_id not in _training_logs:
                _training_logs[training_id] = []
            _training_logs[training_id].append({
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": f"Epoch {epoch}/{epochs} completed",
                "epoch": epoch,
                "loss": loss,
            })
            _cleanup_training_logs(training_id)
            try:
                await get_broadcaster().broadcast_training_progress(
                    training_id=training_id,
                    progress_data={
                        "epoch": epoch,
                        "total_epochs": epochs,
                        "loss": loss,
                        "progress": progress,
                        "status": "running",
                        "quality_score": quality_score,
                        "validation_loss": validation_loss,
                    },
                )
            except Exception:
                pass

        if key in _training_jobs_store:
            status_dict["status"] = "completed"
            status_dict["completed"] = datetime.utcnow().isoformat()
            status_dict["progress"] = 1.0
            _training_logs[training_id].append({
                "timestamp": datetime.utcnow().isoformat(),
                "level": "info",
                "message": "Training completed successfully",
                "epoch": epochs,
                "loss": status_dict.get("loss", 0.2),
            })
    except Exception as e:
        logger.error("Training simulation error: %s", e, exc_info=True)
        if key in _training_jobs_store:
            status_dict["status"] = "failed"
            status_dict["error_message"] = str(e)
            status_dict["completed"] = datetime.utcnow().isoformat()


# --- Export / Import ---


def export_trained_model(
    training_id: str,
    profile_id: str | None = None,
    include_metadata: bool = True,
) -> dict | None:
    """Export a trained model as ZIP. Returns export info or None."""
    key = f"training_{training_id}"
    if key not in _training_jobs_store:
        return None
    status_dict = _training_jobs_store[key]
    if status_dict.get("status") != "completed":
        return None
    output_path = status_dict.get("output_path")
    if not output_path:
        output_path = str(get_path("models") / "trained" / training_id / "exported_model")
    model_dir = Path(output_path)
    if not model_dir.exists():
        return None
    export_id = str(uuid.uuid4())
    export_dir = get_path("models") / "exports" / export_id
    export_dir.mkdir(parents=True, exist_ok=True)
    zip_path = export_dir / f"model_{export_id}.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zipf:
        for root, _dirs, files in os.walk(model_dir):
            for file in files:
                file_path = Path(root) / file
                arcname = file_path.relative_to(model_dir.parent)
                zipf.write(file_path, arcname)
        metadata = {
            "export_id": export_id,
            "training_id": training_id,
            "profile_id": profile_id,
            "exported": datetime.utcnow().isoformat(),
            "model_type": status_dict.get("engine", "xtts"),
            "training_metadata": (status_dict if include_metadata else None),
        }
        zipf.writestr("metadata.json", json.dumps(metadata, indent=2))
    return {
        "export_id": export_id,
        "model_path": str(model_dir),
        "export_path": str(zip_path),
        "created": datetime.utcnow(),
    }


def import_trained_model(content: bytes, filename: str, profile_id: str | None = None) -> dict | None:
    """Import a trained model from ZIP bytes. Returns status dict or None on error."""
    from backend.core.security.file_validation import FileValidationError, validate_archive_file

    try:
        validate_archive_file(content, filename=filename)
    except FileValidationError:
        return None

    import_id = str(uuid.uuid4())
    training_id = str(uuid.uuid4())
    import_dir = get_path("models") / "imports" / import_id
    import_dir.mkdir(parents=True, exist_ok=True)
    zip_path = import_dir / (filename or "upload.zip")
    zip_path.write_bytes(content)
    extract_dir = import_dir / "extracted"
    extract_dir.mkdir(exist_ok=True)
    with zipfile.ZipFile(zip_path, "r") as zipf:
        zipf.extractall(extract_dir)
    metadata_path = extract_dir / "metadata.json"
    if not metadata_path.exists():
        return None
    with open(metadata_path) as f:
        metadata = json.load(f)
    model_dir = extract_dir / "model"
    if not model_dir.exists():
        model_files = list(extract_dir.glob("*.pth")) + list(extract_dir.glob("*.pt"))
        model_dir = extract_dir if model_files else None
    if not model_dir or not model_dir.exists():
        return None
    final_model_dir = get_path("models") / "trained" / training_id
    final_model_dir.mkdir(parents=True, exist_ok=True)
    exported_dir = final_model_dir / "exported_model"
    shutil.copytree(model_dir, exported_dir, dirs_exist_ok=True)
    shutil.copy2(metadata_path, final_model_dir / "metadata.json")
    original_metadata = metadata.get("training_metadata", {})
    status = {
        "id": training_id,
        "dataset_id": "",
        "profile_id": profile_id or metadata.get("profile_id"),
        "engine": metadata.get("model_type", "xtts"),
        "status": "completed",
        "progress": 1.0,
        "current_epoch": original_metadata.get("total_epochs", 0),
        "total_epochs": original_metadata.get("total_epochs", 0),
        "loss": original_metadata.get("loss"),
        "started": metadata.get("exported"),
        "completed": datetime.utcnow().isoformat(),
        "error_message": None,
        "output_path": str(exported_dir),
    }
    _training_jobs_store[f"training_{training_id}"] = status
    _training_job_timestamps[f"training_{training_id}"] = time.time()
    logger.info("Model imported: %s -> %s", import_id, final_model_dir)
    return status


def get_export_download_path(export_id: str) -> Path | None:
    """Get path to exported ZIP for download."""
    export_dir = get_path("models") / "exports" / export_id
    zip_files = list(export_dir.glob("*.zip"))
    return Path(zip_files[0]) if zip_files else None


# --- Backward compatibility for training_service.get_training_status / start_training ---


def get_training_status_legacy(job_id: str | None = None) -> dict:
    """Legacy facade for get_training_status. Returns status dict."""
    if job_id:
        status = get_training_status(job_id)
        return status if status else {"status": "not_found"}
    return {"jobs": list_training_jobs()}


def start_training_legacy(
    profile_id: str,
    engine: str = "xtts_v2",
    **kwargs: Any,
) -> dict:
    """Legacy facade for start_training. Requires dataset_id in kwargs."""
    dataset_id = kwargs.get("dataset_id")
    if not dataset_id:
        return {"status": "error", "message": "dataset_id required"}

    async def _on_started(tid: str, req: dict) -> None:
        await run_training(
            tid,
            req["dataset_id"],
            req["profile_id"],
            req.get("engine", "xtts"),
            req.get("epochs", 100),
            req.get("batch_size", 4),
            req.get("learning_rate", 0.0001),
            req.get("gpu", True),
        )

    result = start_training(
        dataset_id=dataset_id,
        profile_id=profile_id,
        engine=kwargs.get("engine", "xtts"),
        epochs=kwargs.get("epochs", 100),
        batch_size=kwargs.get("batch_size", 4),
        learning_rate=kwargs.get("learning_rate", 0.0001),
        gpu=kwargs.get("gpu", True),
        on_started=_on_started,
    )
    if result is None:
        return {"status": "error", "message": "Dataset not found"}
    return {"status": "created", "job_id": result.get("id")}
