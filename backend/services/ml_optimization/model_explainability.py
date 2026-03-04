"""
Model Explainability Integration
Integrates shap and lime libraries for model interpretability.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Try importing explainability libraries
HAS_SHAP = False
try:
    import shap

    HAS_SHAP = True
except ImportError:
    logger.warning("shap not available.")

HAS_LIME = False
try:
    import lime
    from lime import lime_tabular

    HAS_LIME = True
except ImportError:
    logger.warning("lime not available.")


class ModelExplainer:
    """
    Model explainability using shap and lime libraries.
    """

    def __init__(self):
        """Initialize explainer."""
        self.shap_available = HAS_SHAP
        self.lime_available = HAS_LIME

    def explain_with_shap(
        self,
        model: Any,
        X: np.ndarray,
        feature_names: list[str] | None = None,
        explainer_type: str = "TreeExplainer",
    ) -> dict[str, Any]:
        """
        Explain model predictions using SHAP.

        Args:
            model: Trained model (must be compatible with SHAP)
            X: Input features (samples x features)
            feature_names: Optional list of feature names
            explainer_type: Type of explainer ("TreeExplainer", "KernelExplainer", etc.)

        Returns:
            Dictionary with SHAP values and explanations
        """
        if not self.shap_available:
            raise ImportError("shap library not available")

        try:
            # Create appropriate explainer based on type
            if explainer_type == "TreeExplainer":
                explainer = shap.TreeExplainer(model)
            elif explainer_type == "KernelExplainer":
                explainer = shap.KernelExplainer(model.predict, X[:100] if len(X) > 100 else X)
            elif explainer_type == "LinearExplainer":
                explainer = shap.LinearExplainer(model, X)
            else:
                # Default to TreeExplainer
                explainer = shap.TreeExplainer(model)

            # Calculate SHAP values
            shap_values = explainer.shap_values(X)

            # Get feature importance
            if isinstance(shap_values, list):
                # Multi-class output
                feature_importance = np.abs(shap_values[0]).mean(0)
            else:
                feature_importance = np.abs(shap_values).mean(0)

            result = {
                "shap_values": (
                    shap_values.tolist()
                    if isinstance(shap_values, np.ndarray)
                    else [v.tolist() for v in shap_values]
                ),
                "feature_importance": feature_importance.tolist(),
                "explainer_type": explainer_type,
            }

            if feature_names:
                result["feature_names"] = feature_names
                # Create feature importance dict
                result["feature_importance_dict"] = {
                    name: float(importance)
                    for name, importance in zip(feature_names, feature_importance)
                }

            return result
        except Exception as e:
            logger.error(f"Error in SHAP explanation: {e}", exc_info=True)
            raise

    def explain_with_lime(
        self,
        model: Any,
        X: np.ndarray,
        instance: np.ndarray,
        feature_names: list[str] | None = None,
        num_features: int = 10,
    ) -> dict[str, Any]:
        """
        Explain model prediction for a single instance using LIME.

        Args:
            model: Trained model with predict method
            X: Training data (for background)
            instance: Single instance to explain
            feature_names: Optional list of feature names
            num_features: Number of top features to return

        Returns:
            Dictionary with LIME explanation
        """
        if not self.lime_available:
            raise ImportError("lime library not available")

        try:
            # Create LIME explainer
            explainer = lime_tabular.LimeTabularExplainer(
                X,
                feature_names=feature_names,
                mode="regression" if hasattr(model, "predict") else "classification",
                discretize_continuous=True,
            )

            # Explain instance
            explanation = explainer.explain_instance(
                instance, model.predict, num_features=num_features
            )

            # Extract explanation data
            exp_list = explanation.as_list()

            result = {
                "explanation": exp_list,
                "top_features": [
                    {"feature": feat, "weight": weight} for feat, weight in exp_list[:num_features]
                ],
                "prediction": float(model.predict(instance.reshape(1, -1))[0]),
            }

            if feature_names:
                result["feature_names"] = feature_names

            return result
        except Exception as e:
            logger.error(f"Error in LIME explanation: {e}", exc_info=True)
            raise

    def get_available_methods(self) -> list[str]:
        """
        Get list of available explainability methods.

        Returns:
            List of method names
        """
        methods = []
        if self.shap_available:
            methods.append("shap")
        if self.lime_available:
            methods.append("lime")
        return methods
