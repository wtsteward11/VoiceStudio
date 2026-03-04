"""
Backend facade for health-check related app.core imports.

Routes must import from backend.services.health_facade, not app.core.*.
"""

from __future__ import annotations

from app.core.database.query_optimizer import DatabaseQueryOptimizer
from app.core.resilience.health_check import (
    HealthCheckResult,
    HealthStatus,
    get_health_checker,
)
from app.core.runtime.resource_manager import ResourceManager
from app.core.security.database import WatermarkDatabase
from app.core.tasks.scheduler import get_scheduler
from app.core.utils.temp_file_manager import get_temp_file_manager

__all__ = [
    "DatabaseQueryOptimizer",
    "HealthCheckResult",
    "HealthStatus",
    "ResourceManager",
    "WatermarkDatabase",
    "get_health_checker",
    "get_scheduler",
    "get_temp_file_manager",
]
