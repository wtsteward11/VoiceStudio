"""
Machine Learning Optimization Routes

Endpoints for hyperparameter optimization and model explainability.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.services.ml_optimization import (
    HyperparameterOptimizer,
    ModelExplainer,
)

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/ml-optimization", tags=["ml-optimization"])


# Request/Response Models
class OptimizationRequest(BaseModel):
    """Request for hyperparameter optimization."""

    objective_function: str  # JSON string or function name
    search_space: dict[str, Any]
    method: str = "optuna"  # "optuna", "hyperopt", or "ray"
    n_trials: int = 100
    direction: str = "minimize"  # "minimize" or "maximize"


class OptimizationResponse(BaseModel):
    """Response from hyperparameter optimization."""

    best_params: dict[str, Any]
    best_score: float
    n_trials: int
    method: str
    trials: list[dict[str, Any]] | None = None


class ExplainabilityRequest(BaseModel):
    """Request for model explainability."""

    model_type: str  # "tree", "linear", "neural", etc.
    features: list[list[float]]  # Input features
    method: str = "shap"  # "shap" or "lime"
    feature_names: list[str] | None = None


class ExplainabilityResponse(BaseModel):
    """Response from model explainability."""

    feature_importance: dict[str, float]
    explanation: dict[str, Any]
    method: str


@router.post("/optimize", response_model=OptimizationResponse)
async def optimize_hyperparameters(request: OptimizationRequest):
    """
    Optimize hyperparameters using optuna, hyperopt, or ray[tune].

    Note: This is a simplified endpoint. For production use,
    consider implementing a proper job queue system.
    """
    try:
        optimizer = HyperparameterOptimizer()

        # Check if method is available
        available_methods = optimizer.get_available_methods()
        if request.method not in available_methods:
            raise HTTPException(
                status_code=400,
                detail=f"Method '{request.method}' not available. "
                f"Available: {', '.join(available_methods)}",
            )

        # For demo purposes, create a simple objective function
        # In production, this would be passed as a callable or job reference
        def simple_objective(params: dict[str, Any]) -> float:
            # Example: minimize sum of squared parameters
            score = sum(v**2 for v in params.values() if isinstance(v, (int, float)))
            return score

        if request.method == "optuna":
            result = optimizer.optimize_with_optuna(
                objective=simple_objective,
                search_space=request.search_space,
                n_trials=request.n_trials,
                direction=request.direction,
            )
        elif request.method == "hyperopt":
            result = optimizer.optimize_with_hyperopt(
                objective=simple_objective,
                search_space=request.search_space,
                max_evals=request.n_trials,
            )
        elif request.method == "ray":
            raise HTTPException(
                status_code=400,
                detail=("Ray[tune] is not supported. " "Use 'optuna' or 'hyperopt' instead."),
            )
        else:
            raise HTTPException(
                status_code=400,
                detail=f"Method '{request.method}' not available. "
                f"Available: {', '.join(available_methods)}",
            )

        return OptimizationResponse(
            best_params=result.best_params,
            best_score=result.best_score,
            n_trials=result.n_trials,
            method=result.method,
            trials=result.trials,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in hyperparameter optimization: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to optimize hyperparameters: {e!s}",
        )


@router.post("/explain", response_model=ExplainabilityResponse)
async def explain_model(request: ExplainabilityRequest):
    """
    Explain model predictions using SHAP or LIME.

    Provides feature importance and explanations for model predictions.
    """
    try:
        explainer = ModelExplainer()

        # Check if method is available
        available_methods = explainer.get_available_methods()
        if request.method not in available_methods:
            raise HTTPException(
                status_code=400,
                detail=f"Method '{request.method}' not available. "
                f"Available: {', '.join(available_methods)}",
            )

        # Convert features to numpy array
        X = np.array(request.features)

        # For demo purposes, create a simple model
        # In production, this would be a trained model passed as parameter
        # or loaded from storage
        try:
            from sklearn.ensemble import RandomForestRegressor

            # Create a simple demo model
            # In production, use actual trained model
            demo_model = RandomForestRegressor(n_estimators=10, random_state=42)
            # Fit on dummy data for demo
            dummy_X = np.random.rand(100, len(X[0]) if len(X) > 0 else 1)
            dummy_y = np.random.rand(100)
            demo_model.fit(dummy_X, dummy_y)
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="scikit-learn not available for demo model. "
                "In production, provide a trained model.",
            )

        # Generate explanation
        if request.method == "shap":
            try:
                result = explainer.explain_with_shap(
                    model=demo_model,
                    X=X,
                    feature_names=request.feature_names,
                    explainer_type=(
                        "TreeExplainer" if request.model_type == "tree" else "KernelExplainer"
                    ),
                )
            except Exception as e:
                logger.error(f"SHAP explanation failed: {e}", exc_info=True)
                raise HTTPException(
                    status_code=500,
                    detail=f"SHAP explanation failed: {e!s}",
                )
        elif request.method == "lime":
            try:
                result = explainer.explain_with_lime(
                    model=demo_model,
                    X=X,
                    instance=X[0],
                    feature_names=request.feature_names,
                )
            except Exception as e:
                logger.error(f"LIME explanation failed: {e}", exc_info=True)
                raise HTTPException(
                    status_code=500,
                    detail=f"LIME explanation failed: {e!s}",
                )
        else:
            raise HTTPException(
                status_code=400,
                detail=f"Method '{request.method}' not supported. " f"Use 'shap' or 'lime'.",
            )

        return ExplainabilityResponse(
            feature_importance=result.get("feature_importance", {}),
            explanation=result,
            method=request.method,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in model explainability: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to explain model: {e!s}",
        )


@router.get("/methods")
@cache_response(ttl=600)
async def get_available_methods():
    """Get list of available optimization and explainability methods."""
    optimizer = HyperparameterOptimizer()
    explainer = ModelExplainer()

    return {
        "optimization_methods": optimizer.get_available_methods(),
        "explainability_methods": explainer.get_available_methods(),
    }
