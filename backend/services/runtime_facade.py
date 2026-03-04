"""
Backend facade for app.core.runtime.

Routes must import from backend.services.runtime_facade, not app.core.runtime.*.
"""

from __future__ import annotations

from app.core.runtime.job_queue_enhanced import create_enhanced_job_queue
from app.core.runtime.resource_manager import get_resource_manager

__all__ = ["create_enhanced_job_queue", "get_resource_manager"]
