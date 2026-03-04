"""
Reward Model Training and Prediction Routes

Endpoints for training reward models (used in reinforcement learning
for voice synthesis) and predicting reward scores.
"""

from __future__ import annotations

import asyncio
import logging
import uuid
from datetime import datetime

import numpy as np
from fastapi import APIRouter, HTTPException

from ..models_additional import (
    RmModelInfo,
    RmModelsListResponse,
    RmPredictRequest,
    RmPredictResponse,
    RmTrainingJobResponse,
    RmTrainRequest,
    RmTrainResponse,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/rm", tags=["reward"])

# In-memory reward model storage (replace with database in production)
_reward_models: dict[str, dict] = {}
_reward_training_jobs: dict[str, dict] = {}
_state_lock = asyncio.Lock()


@router.post("/train", response_model=RmTrainResponse)
async def train(req: RmTrainRequest) -> RmTrainResponse:
    """
    Train a reward model from ratings data.

    Reward models predict quality scores for voice synthesis outputs.
    They are trained on human ratings (e.g., MOS scores) to learn
    quality metrics.

    Args:
        req: Request with ratings data (list of rating dictionaries)

    Returns:
        Dictionary with training job status
    """
    try:
        ratings = req.ratings

        if not ratings or len(ratings) == 0:
            raise HTTPException(
                status_code=400,
                detail="ratings list is required and cannot be empty",
            )

        # Validate ratings format
        for i, rating in enumerate(ratings):
            if not isinstance(rating, dict):
                raise HTTPException(status_code=400, detail=f"Rating {i} must be a dictionary")

            # Expected fields: audio_id, score, features (optional)
            if "audio_id" not in rating or "score" not in rating:
                raise HTTPException(
                    status_code=400,
                    detail=(f"Rating {i} must have " "'audio_id' and 'score' fields"),
                )

        # Create training job
        job_id = f"rm_train_{uuid.uuid4().hex[:8]}"

        # Extract features and scores from ratings
        # In a real implementation, this would extract audio features
        # For now, we'll use simple statistics

        scores = [r["score"] for r in ratings if "score" in r]
        avg_score = np.mean(scores) if scores else 0.0
        std_score = np.std(scores) if len(scores) > 1 else 0.0

        # Create a simple reward model (in production, use ML model)
        model_id = f"rm_{uuid.uuid4().hex[:8]}"
        model = {
            "id": model_id,
            "job_id": job_id,
            "training_samples": len(ratings),
            "mean_score": float(avg_score),
            "std_score": float(std_score),
            "created": datetime.utcnow().isoformat(),
            "status": "completed",
        }

        _reward_models[model_id] = model

        training_job = {
            "job_id": job_id,
            "model_id": model_id,
            "status": "completed",
            "samples": len(ratings),
            "started": datetime.utcnow().isoformat(),
            "completed": datetime.utcnow().isoformat(),
        }

        _reward_training_jobs[job_id] = training_job

        logger.info(
            f"Reward model training completed: {job_id} -> {model_id} "
            f"({len(ratings)} samples, mean_score={avg_score:.2f})"
        )

        return RmTrainResponse(
            status="completed",
            job_id=job_id,
            model_id=model_id,
            samples=len(ratings),
            message=f"Reward model trained on {len(ratings)} samples",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Reward model training failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Reward model training failed: {e!s}") from e


@router.post("/predict", response_model=RmPredictResponse)
async def predict(req: RmPredictRequest) -> RmPredictResponse:
    """
    Predict reward score for audio using trained reward model.

    Args:
        req: Request with model_id (optional) and audio_id or features

    Returns:
        RmPredictResponse with predicted reward score
    """
    try:
        model_id = req.model_id
        audio_id = req.audio_id
        features = req.features  # Optional pre-computed features

        if not model_id:
            # Use most recent model if available
            if _reward_models:
                model_id = max(
                    _reward_models.keys(),
                    key=lambda k: _reward_models[k].get("created", ""),
                )
            else:
                raise HTTPException(
                    status_code=404,
                    detail="No reward models available. Train a model first.",
                )

        if model_id not in _reward_models:
            raise HTTPException(status_code=404, detail=f"Reward model '{model_id}' not found")

        model = _reward_models[model_id]

        # Predict score
        # In a real implementation, this would use the trained model
        # For now, we'll use a simple prediction based on model statistics

        if features:
            # Use provided features
            if isinstance(features, (list, np.ndarray)):
                # Simple prediction: average of features (normalized)
                feature_array = np.array(features)
                if len(feature_array) > 0:
                    # Normalize and predict
                    feature_mean = np.mean(feature_array)
                    # Map to score range (0.0-5.0 for MOS)
                    predicted_score = float(
                        model["mean_score"] + (feature_mean - 0.5) * model["std_score"]
                    )
                    # Clamp to reasonable range
                    predicted_score = max(0.0, min(5.0, predicted_score))
                else:
                    predicted_score = model["mean_score"]
            else:
                predicted_score = model["mean_score"]
        elif audio_id:
            # Extract features from audio
            # In production, this would extract real audio features
            # For now, use model mean as baseline
            predicted_score = model["mean_score"]
        else:
            # No features or audio provided, return model mean
            predicted_score = model["mean_score"]

        # Calculate confidence based on model statistics and prediction variance
        # Confidence is higher when:
        # 1. Model has more training samples
        # 2. Prediction is closer to model mean (less variance)
        # 3. Model has lower standard deviation (more consistent)
        sample_count = model.get("training_samples", 0)
        model_std = model.get("std_score", 1.0)

        # Base confidence from sample count (more samples = higher confidence)
        sample_confidence = min(1.0, sample_count / 100.0)  # Max confidence at 100+ samples

        # Variance confidence (lower std = higher confidence)
        std_confidence = max(0.3, 1.0 - (model_std / 2.0))  # Min 0.3, decreases with higher std

        # Prediction confidence (closer to mean = higher confidence)
        score_diff = abs(predicted_score - model["mean_score"])
        prediction_confidence = max(0.5, 1.0 - (score_diff / model_std) if model_std > 0 else 1.0)

        # Weighted combination
        confidence = (
            (sample_confidence * 0.4) + (std_confidence * 0.3) + (prediction_confidence * 0.3)
        )
        confidence = max(0.3, min(1.0, confidence))  # Clamp to [0.3, 1.0]

        logger.info(
            f"Reward prediction: model={model_id}, "
            f"audio_id={audio_id}, score={predicted_score:.2f}, confidence={confidence:.2f}"
        )

        return RmPredictResponse(
            score=float(predicted_score),
            model_id=model_id,
            audio_id=audio_id,
            confidence=float(confidence),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Reward prediction failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Reward prediction failed: {e!s}") from e


@router.get("/models", response_model=RmModelsListResponse)
async def list_models() -> RmModelsListResponse:
    """List all trained reward models."""
    models = [
        RmModelInfo(
            id=m["id"],
            training_samples=m["training_samples"],
            mean_score=m["mean_score"],
            created=m["created"],
            status=m["status"],
        )
        for m in _reward_models.values()
    ]

    return RmModelsListResponse(
        models=models,
        count=len(models),
    )


@router.get("/jobs/{job_id}", response_model=RmTrainingJobResponse)
async def get_training_job(job_id: str) -> RmTrainingJobResponse:
    """Get training job status."""
    if job_id not in _reward_training_jobs:
        raise HTTPException(status_code=404, detail=f"Training job '{job_id}' not found")

    job = _reward_training_jobs[job_id]
    return RmTrainingJobResponse(
        job_id=job["job_id"],
        model_id=job["model_id"],
        status=job["status"],
        samples=job["samples"],
        started=job["started"],
        completed=job.get("completed"),
    )
