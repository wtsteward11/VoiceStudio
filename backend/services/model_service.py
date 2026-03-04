"""
M4: Model service wrapper.

Provides model loading and inspection facade.
Routes import from backend.services.model_service.
"""

from __future__ import annotations

from typing import Any


def get_model_info(engine_id: str | None = None, model_id: str | None = None) -> dict[str, Any]:
    """Get model info. Delegates to model registry when available."""
    try:
        from backend.services.model_registry import get_model_registry_service

        registry = get_model_registry_service()
        if registry:
            models = registry.list_models(engine_id=engine_id)
            if model_id:
                for m in models:
                    if m.get("id") == model_id or m.get("model_id") == model_id:
                        return m
                return {}
            return {"models": models}
    except ImportError:
        pass
    return {"models": []}


def list_models(engine_id: str | None = None) -> list[dict[str, Any]]:
    """List available models. Delegates to model registry."""
    try:
        from backend.services.model_registry import get_model_registry_service

        registry = get_model_registry_service()
        if registry:
            return registry.list_models(engine_id=engine_id)
    except ImportError:
        pass
    return []
