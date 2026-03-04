"""
Backend facade for app.core.models.

Routes must import from backend.services.model_facade, not app.core.models.*.
"""

from __future__ import annotations

from app.core.models.cache import get_model_cache
from app.core.models.storage import ModelStorage

__all__ = ["ModelStorage", "get_model_cache"]
