"""
Phase 8: Health Check System
Task 8.3: Health checks for application components.
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import Any

# Import configuration
app_config: Any = None
try:
    from backend.settings import config as app_config
except ImportError:  # ALLOWED: bare except - optional app config
    pass

logger = logging.getLogger(__name__)


def _get_timeout(config_attr: str, default: float) -> float:
    """Get timeout from configuration with fallback to default."""
    if app_config is not None:
        return getattr(app_config.timeouts, config_attr, default)
    return default


def _get_server_url(default: str) -> str:
    """Get server URL from configuration."""
    if app_config is not None:
        return str(app_config.server.base_url)
    return default


class HealthStatus(Enum):
    """Health status levels."""

    HEALTHY = "healthy"
    DEGRADED = "degraded"
    UNHEALTHY = "unhealthy"
    UNKNOWN = "unknown"


@dataclass
class HealthCheckResult:
    """Result of a health check."""

    component: str
    status: HealthStatus
    message: str = ""
    latency_ms: float = 0.0
    details: dict[str, Any] = field(default_factory=dict)
    last_check: datetime = field(default_factory=datetime.now)


@dataclass
class HealthReport:
    """Complete health report."""

    overall_status: HealthStatus
    timestamp: datetime
    checks: list[HealthCheckResult]
    uptime_seconds: float
    version: str


class HealthCheck:
    """Base class for health checks."""

    def __init__(self, name: str, timeout: float = 5.0, critical: bool = True):
        self.name = name
        self.timeout = timeout
        self.critical = critical

    async def check(self) -> HealthCheckResult:
        """Perform the health check."""
        start = time.perf_counter()

        try:
            result = await asyncio.wait_for(self._do_check(), timeout=self.timeout)
            result.latency_ms = (time.perf_counter() - start) * 1000
            return result

        except asyncio.TimeoutError:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.UNHEALTHY,
                message=f"Check timed out after {self.timeout}s",
                latency_ms=(time.perf_counter() - start) * 1000,
            )
        except Exception as e:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.UNHEALTHY,
                message=str(e),
                latency_ms=(time.perf_counter() - start) * 1000,
            )

    async def _do_check(self) -> HealthCheckResult:
        """Implement the actual check logic."""
        raise NotImplementedError


class BackendHealthCheck(HealthCheck):
    """Health check for the backend API."""

    def __init__(self, backend_url: str | None = None):
        timeout = _get_timeout("health_check", 5.0)
        super().__init__("backend", timeout=timeout, critical=True)
        self.backend_url = backend_url or _get_server_url("http://localhost:8000")

    async def _do_check(self) -> HealthCheckResult:
        """Check if backend is responsive."""
        try:
            import aiohttp

            async with aiohttp.ClientSession() as session:
                async with session.get(f"{self.backend_url}/health") as response:
                    if response.status == 200:
                        return HealthCheckResult(
                            component=self.name,
                            status=HealthStatus.HEALTHY,
                            message="Backend is responding",
                        )
                    else:
                        return HealthCheckResult(
                            component=self.name,
                            status=HealthStatus.DEGRADED,
                            message=f"Backend returned status {response.status}",
                        )
        except ImportError:
            # aiohttp not available, skip check
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.HEALTHY,
                message="Backend check skipped (aiohttp not available)",
            )
        except Exception as e:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.UNHEALTHY,
                message=str(e),
            )


class DatabaseHealthCheck(HealthCheck):
    """Health check for database connectivity."""

    def __init__(self, db_path: str = ""):
        timeout = _get_timeout("database_check", 3.0)
        super().__init__("database", timeout=timeout, critical=True)
        self.db_path = db_path

    async def _do_check(self) -> HealthCheckResult:
        """Check database connectivity."""
        from pathlib import Path

        if not self.db_path:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.HEALTHY,
                message="No database configured",
            )

        db_file = Path(self.db_path)
        if db_file.exists():
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.HEALTHY,
                message="Database file exists",
                details={"size_mb": db_file.stat().st_size / (1024 * 1024)},
            )

        return HealthCheckResult(
            component=self.name,
            status=HealthStatus.UNHEALTHY,
            message="Database file not found",
        )


class EngineHealthCheck(HealthCheck):
    """Health check for voice engines."""

    def __init__(self):
        timeout = _get_timeout("engine_check", 10.0)
        super().__init__("engines", timeout=timeout, critical=False)

    async def _do_check(self) -> HealthCheckResult:
        """Check if engines are loaded and responsive."""
        try:
            # Try to get actual engine status from registry
            from backend.ml.models.engine_service import get_engine_service

            engine_service = get_engine_service()

            if engine_service is None:
                return HealthCheckResult(
                    component=self.name,
                    status=HealthStatus.DEGRADED,
                    message="Engine service not initialized",
                    details={"loaded_engines": []},
                )

            available_engines = engine_service.list_engines()
            engine_count = len(available_engines)

            if engine_count == 0:
                return HealthCheckResult(
                    component=self.name,
                    status=HealthStatus.DEGRADED,
                    message="No engines available",
                    details={"loaded_engines": []},
                )

            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.HEALTHY,
                message=f"{engine_count} engine(s) available",
                details={"loaded_engines": available_engines},
            )
        except ImportError:
            # Engine service not available, return placeholder
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.UNKNOWN,
                message="Engine service module not available",
                details={"loaded_engines": []},
            )
        except Exception as e:
            logger.warning(f"Engine health check failed: {e}")
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.DEGRADED,
                message=f"Engine check error: {e!s}",
                details={"loaded_engines": [], "error": str(e)},
            )


class DiskHealthCheck(HealthCheck):
    """Health check for disk space."""

    def __init__(self, path: str = ".", min_free_gb: float = 1.0):
        timeout = _get_timeout("disk_check", 2.0)
        super().__init__("disk", timeout=timeout, critical=False)
        self.path = path
        self.min_free_gb = min_free_gb

    async def _do_check(self) -> HealthCheckResult:
        """Check disk space."""
        import psutil

        disk = psutil.disk_usage(self.path)
        free_gb = disk.free / (1024**3)

        if free_gb < self.min_free_gb:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.DEGRADED,
                message=f"Low disk space: {free_gb:.1f} GB free",
                details={"free_gb": free_gb, "total_gb": disk.total / (1024**3)},
            )

        return HealthCheckResult(
            component=self.name,
            status=HealthStatus.HEALTHY,
            message=f"{free_gb:.1f} GB free",
            details={"free_gb": free_gb, "total_gb": disk.total / (1024**3)},
        )


class MemoryHealthCheck(HealthCheck):
    """Health check for memory usage."""

    def __init__(self, max_usage_percent: float = 90.0):
        timeout = _get_timeout("memory_check", 2.0)
        super().__init__("memory", timeout=timeout, critical=False)
        self.max_usage_percent = max_usage_percent

    async def _do_check(self) -> HealthCheckResult:
        """Check memory usage."""
        import psutil

        memory = psutil.virtual_memory()
        usage_percent = memory.percent

        if usage_percent > self.max_usage_percent:
            return HealthCheckResult(
                component=self.name,
                status=HealthStatus.DEGRADED,
                message=f"High memory usage: {usage_percent:.1f}%",
                details={
                    "usage_percent": usage_percent,
                    "used_gb": memory.used / (1024**3),
                    "total_gb": memory.total / (1024**3),
                },
            )

        return HealthCheckResult(
            component=self.name,
            status=HealthStatus.HEALTHY,
            message=f"Memory usage: {usage_percent:.1f}%",
            details={
                "usage_percent": usage_percent,
                "used_gb": memory.used / (1024**3),
                "total_gb": memory.total / (1024**3),
            },
        )


class HealthCheckService:
    """Service for running health checks."""

    def __init__(self, app_version: str = "1.0.0"):
        self._checks: list[HealthCheck] = []
        self._app_version = app_version
        self._start_time = datetime.now()

        # Register default checks
        self.register(DiskHealthCheck())
        self.register(MemoryHealthCheck())
        self.register(EngineHealthCheck())

    def register(self, check: HealthCheck) -> None:
        """Register a health check."""
        self._checks.append(check)

    async def run_all(self) -> HealthReport:
        """Run all health checks."""
        tasks = [check.check() for check in self._checks]
        results = await asyncio.gather(*tasks, return_exceptions=True)

        check_results = []
        for result in results:
            if isinstance(result, HealthCheckResult):
                check_results.append(result)
            elif isinstance(result, Exception):
                check_results.append(
                    HealthCheckResult(
                        component="unknown",
                        status=HealthStatus.UNHEALTHY,
                        message=str(result),
                    )
                )

        # Determine overall status
        overall = HealthStatus.HEALTHY

        for result in check_results:
            check = next((c for c in self._checks if c.name == result.component), None)

            if result.status == HealthStatus.UNHEALTHY:
                if check and check.critical:
                    overall = HealthStatus.UNHEALTHY
                    break
                elif overall != HealthStatus.UNHEALTHY:
                    overall = HealthStatus.DEGRADED

            elif result.status == HealthStatus.DEGRADED:
                if overall == HealthStatus.HEALTHY:
                    overall = HealthStatus.DEGRADED

        uptime = (datetime.now() - self._start_time).total_seconds()

        return HealthReport(
            overall_status=overall,
            timestamp=datetime.now(),
            checks=check_results,
            uptime_seconds=uptime,
            version=self._app_version,
        )

    async def run_check(self, component: str) -> HealthCheckResult | None:
        """Run a specific health check."""
        for check in self._checks:
            if check.name == component:
                return await check.check()

        return None

    def get_uptime(self) -> timedelta:
        """Get application uptime."""
        return datetime.now() - self._start_time
