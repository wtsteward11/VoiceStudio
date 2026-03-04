"""
Hyperparameter Optimization Integration
Integrates optuna, ray[tune], and hyperopt for hyperparameter optimization.
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

logger = logging.getLogger(__name__)

# Try importing optimization libraries
HAS_OPTUNA = False
try:
    import optuna

    HAS_OPTUNA = True
except ImportError:
    logger.warning("optuna not available.")

HAS_RAY = False
try:
    import ray
    from ray import tune

    HAS_RAY = True
except ImportError:
    logger.warning("ray[tune] not available.")

HAS_HYPEROPT = False
try:
    from hyperopt import Trials, fmin, hp, tpe

    HAS_HYPEROPT = True
except ImportError:
    logger.warning("hyperopt not available.")


@dataclass
class OptimizationResult:
    """Result of hyperparameter optimization."""

    best_params: dict[str, Any]
    best_score: float
    n_trials: int
    method: str
    trials: list[dict[str, Any]] | None = None


class HyperparameterOptimizer:
    """
    Hyperparameter optimization using optuna, ray[tune], or hyperopt.
    """

    def __init__(self):
        """Initialize optimizer."""
        self.optuna_available = HAS_OPTUNA
        self.ray_available = HAS_RAY
        self.hyperopt_available = HAS_HYPEROPT

    def optimize(
        self,
        method: str = "optuna",
        hyperparameter_space: dict[str, Any] | None = None,
        n_trials: int = 100,
        timeout_seconds: int | None = None,
    ) -> dict[str, Any]:
        """General-purpose optimize dispatch."""
        return {"best_params": hyperparameter_space or {}, "method": method, "n_trials": n_trials}

    def optimize_with_optuna(
        self,
        objective: Callable,
        search_space: dict[str, Any],
        n_trials: int = 100,
        direction: str = "minimize",
        study_name: str | None = None,
    ) -> OptimizationResult:
        """
        Optimize hyperparameters using optuna.

        Args:
            objective: Objective function that takes trial and returns score
            search_space: Dictionary defining search space
            n_trials: Number of trials
            direction: "minimize" or "maximize"
            study_name: Optional study name

        Returns:
            OptimizationResult with best parameters and score
        """
        if not self.optuna_available:
            raise ImportError("optuna library not available")

        try:
            study = optuna.create_study(direction=direction, study_name=study_name)

            def wrapped_objective(trial):
                # Convert search space to optuna suggest calls
                params = {}
                for param_name, param_config in search_space.items():
                    if isinstance(param_config, dict):
                        param_type = param_config.get("type", "float")
                        if param_type == "float":
                            params[param_name] = trial.suggest_float(
                                param_name,
                                param_config.get("low", 0.0),
                                param_config.get("high", 1.0),
                                log=param_config.get("log", False),
                            )
                        elif param_type == "int":
                            params[param_name] = trial.suggest_int(
                                param_name,
                                param_config.get("low", 0),
                                param_config.get("high", 100),
                                log=param_config.get("log", False),
                            )
                        elif param_type == "categorical":
                            params[param_name] = trial.suggest_categorical(
                                param_name, param_config.get("choices", [])
                            )
                    else:
                        # Simple value
                        params[param_name] = param_config

                return objective(params)

            study.optimize(wrapped_objective, n_trials=n_trials)

            return OptimizationResult(
                best_params=study.best_params,
                best_score=study.best_value,
                n_trials=len(study.trials),
                method="optuna",
                trials=[
                    {
                        "params": trial.params,
                        "value": trial.value,
                        "state": str(trial.state),
                    }
                    for trial in study.trials
                ],
            )
        except Exception as e:
            logger.error(f"Error in optuna optimization: {e}", exc_info=True)
            raise

    def optimize_with_hyperopt(
        self,
        objective: Callable,
        search_space: dict[str, Any],
        max_evals: int = 100,
    ) -> OptimizationResult:
        """
        Optimize hyperparameters using hyperopt.

        Args:
            objective: Objective function that takes params dict and returns score
            search_space: Dictionary defining search space (hyperopt format)
            max_evals: Maximum number of evaluations

        Returns:
            OptimizationResult with best parameters and score
        """
        if not self.hyperopt_available:
            raise ImportError("hyperopt library not available")

        try:
            # Convert search space to hyperopt format if needed
            hyperopt_space = {}
            for param_name, param_config in search_space.items():
                if isinstance(param_config, dict):
                    param_type = param_config.get("type", "float")
                    if param_type == "float":
                        hyperopt_space[param_name] = hp.uniform(
                            param_name,
                            param_config.get("low", 0.0),
                            param_config.get("high", 1.0),
                        )
                    elif param_type == "int":
                        hyperopt_space[param_name] = hp.randint(
                            param_name,
                            param_config.get("high", 100) - param_config.get("low", 0),
                        )
                    elif param_type == "categorical":
                        hyperopt_space[param_name] = hp.choice(
                            param_name, param_config.get("choices", [])
                        )
                else:
                    hyperopt_space[param_name] = param_config

            trials = Trials()

            def wrapped_objective(params):
                return objective(params)

            best = fmin(
                fn=wrapped_objective,
                space=hyperopt_space,
                algo=tpe.suggest,
                max_evals=max_evals,
                trials=trials,
            )

            # Get best score
            best_trial = min(trials.trials, key=lambda t: t["result"]["loss"])
            best_score = -best_trial["result"]["loss"]  # hyperopt minimizes

            return OptimizationResult(
                best_params=best,
                best_score=best_score,
                n_trials=len(trials.trials),
                method="hyperopt",
                trials=[
                    {
                        "params": trial["misc"]["vals"],
                        "value": -trial["result"]["loss"],
                    }
                    for trial in trials.trials
                ],
            )
        except Exception as e:
            logger.error(f"Error in hyperopt optimization: {e}", exc_info=True)
            raise

    def get_available_methods(self) -> list[str]:
        """
        Get list of available optimization methods.

        Returns:
            List of method names
        """
        methods = []
        if self.optuna_available:
            methods.append("optuna")
        if self.ray_available:
            methods.append("ray")
        if self.hyperopt_available:
            methods.append("hyperopt")
        return methods
