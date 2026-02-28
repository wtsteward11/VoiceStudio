"""
GAP-I06: Health Aggregation Service

Provides a unified health endpoint that aggregates data from multiple
health sources into a single, consistent response.
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any, Callable

logger = logging.getLogger(__name__)


class HealthCategory(str, Enum):
    """Categories for health check grouping."""

    CORE = "core"
    INFRASTRUCTURE = "infrastructure"
    ENGINES = "engines"
    RESOURCES = "resources"
    EXTERNAL = "external"


class AggregateHealthStatus(str, Enum):
    """Overall health status levels."""

    HEALTHY = "healthy"
    DEGRADED = "degraded"
    UNHEALTHY = "unhealthy"
    UNKNOWN = "unknown"


@dataclass
class HealthSource:
    """A health data source configuration."""

    name: str
    category: HealthCategory
    check_fn: Callable[[], Any]  # Sync or async callable returning dict
    is_async: bool = False
    critical: bool = False
    timeout: float = 5.0
    cache_ttl: float = 5.0  # Seconds to cache results
    _last_result: dict[str, Any] = field(default_factory=dict)
    _last_check_time: float = 0.0


@dataclass
class AggregatedHealthReport:
    """Complete aggregated health report."""

    status: AggregateHealthStatus
    timestamp: datetime
    categories: dict[str, dict[str, Any]]
    summary: dict[str, int]
    latency_ms: float
    version: str


class HealthAggregationService:
    """
    GAP-I06: Unified health aggregation service.

    Aggregates health data from multiple sources (health checks, resource
    monitors, engine status, etc.) into a single unified response.

    Features:
    - Configurable health sources by category
    - Caching with TTL for performance
    - Parallel async execution
    - Graceful degradation on source failures
    - Filtering by category or component
    """

    def __init__(self, app_version: str = "1.0.0"):
        self._sources: dict[str, HealthSource] = {}
        self._app_version = app_version
        self._start_time = datetime.now()
        self._lock = asyncio.Lock()

        # Register default sources
        self._register_default_sources()

    def _register_default_sources(self) -> None:
        """Register default health sources."""
        # Core API health
        self.register_source(
            name="api",
            category=HealthCategory.CORE,
            check_fn=self._check_api,
            critical=True,
            timeout=2.0,
        )

        # System resources
        self.register_source(
            name="memory",
            category=HealthCategory.RESOURCES,
            check_fn=self._check_memory,
            timeout=2.0,
        )

        self.register_source(
            name="disk",
            category=HealthCategory.RESOURCES,
            check_fn=self._check_disk,
            timeout=2.0,
        )

        # Engine status
        self.register_source(
            name="engines",
            category=HealthCategory.ENGINES,
            check_fn=self._check_engines,
            timeout=5.0,
        )

        # Infrastructure
        self.register_source(
            name="database",
            category=HealthCategory.INFRASTRUCTURE,
            check_fn=self._check_database,
            critical=True,
            timeout=3.0,
        )

    def register_source(
        self,
        name: str,
        category: HealthCategory,
        check_fn: Callable,
        critical: bool = False,
        timeout: float = 5.0,
        cache_ttl: float = 5.0,
    ) -> None:
        """
        Register a health source.

        Args:
            name: Unique source identifier.
            category: Health category for grouping.
            check_fn: Function that returns health data (sync or async).
            critical: If True, failure affects overall status.
            timeout: Maximum time to wait for check (seconds).
            cache_ttl: Time to cache results (seconds).
        """
        is_async = asyncio.iscoroutinefunction(check_fn)
        self._sources[name] = HealthSource(
            name=name,
            category=category,
            check_fn=check_fn,
            is_async=is_async,
            critical=critical,
            timeout=timeout,
            cache_ttl=cache_ttl,
        )

    def unregister_source(self, name: str) -> bool:
        """
        Unregister a health source.

        Returns:
            True if the source was found and removed.
        """
        if name in self._sources:
            del self._sources[name]
            return True
        return False

    async def get_aggregated_health(
        self,
        categories: list[HealthCategory] | None = None,
        include_details: bool = True,
        use_cache: bool = True,
    ) -> AggregatedHealthReport:
        """
        Get aggregated health from all registered sources.

        Args:
            categories: Filter to specific categories (None = all).
            include_details: Include detailed health data.
            use_cache: Use cached results within TTL.

        Returns:
            Aggregated health report.
        """
        start_time = time.perf_counter()

        # Filter sources by category
        sources = list(self._sources.values())
        if categories:
            sources = [s for s in sources if s.category in categories]

        # Run checks in parallel
        results = await self._run_checks(sources, use_cache)

        # Aggregate by category
        categories_data: dict[str, dict[str, Any]] = {}
        for source, result in results.items():
            cat = source.category.value
            if cat not in categories_data:
                categories_data[cat] = {}

            if include_details:
                categories_data[cat][source.name] = result
            else:
                # Simplified status only
                status = result.get("status", "unknown")
                categories_data[cat][source.name] = {"status": status}

        # Calculate overall status
        overall_status = self._calculate_overall_status(results)

        # Summary counts
        summary = self._calculate_summary(results)

        latency_ms = (time.perf_counter() - start_time) * 1000

        return AggregatedHealthReport(
            status=overall_status,
            timestamp=datetime.utcnow(),
            categories=categories_data,
            summary=summary,
            latency_ms=latency_ms,
            version=self._app_version,
        )

    async def get_component_health(self, component: str) -> dict[str, Any]:
        """
        Get health for a specific component.

        Args:
            component: Component name to check.

        Returns:
            Health data for the component.
        """
        source = self._sources.get(component)
        if not source:
            return {
                "status": "unknown",
                "message": f"Component '{component}' not registered",
            }

        results = await self._run_checks([source], use_cache=False)
        return results.get(source, {"status": "unknown"})

    async def _run_checks(
        self,
        sources: list[HealthSource],
        use_cache: bool = True,
    ) -> dict[HealthSource, dict[str, Any]]:
        """Run health checks for given sources."""
        results: dict[HealthSource, dict[str, Any]] = {}
        tasks = []

        now = time.time()

        for source in sources:
            # Check cache
            if use_cache and source._last_result:
                elapsed = now - source._last_check_time
                if elapsed < source.cache_ttl:
                    results[source] = source._last_result
                    continue

            # Schedule check
            tasks.append(self._run_single_check(source))

        # Execute all checks in parallel
        if tasks:
            check_results = await asyncio.gather(*tasks, return_exceptions=True)

            # Map results back to sources
            task_sources = [s for s in sources if s not in results]
            for source, check_result in zip(task_sources, check_results):
                if isinstance(check_result, BaseException):
                    result_data: dict[str, Any] = {
                        "status": "unhealthy",
                        "message": str(check_result),
                        "error": type(check_result).__name__,
                    }
                else:
                    result_data = check_result

                # Update cache
                source._last_result = result_data
                source._last_check_time = time.time()
                results[source] = result_data

        return results

    async def _run_single_check(self, source: HealthSource) -> dict[str, Any]:
        """Run a single health check with timeout."""
        try:
            if source.is_async:
                result = await asyncio.wait_for(
                    source.check_fn(),
                    timeout=source.timeout,
                )
            else:
                # Run sync function in executor
                loop = asyncio.get_running_loop()
                result = await asyncio.wait_for(
                    loop.run_in_executor(None, source.check_fn),
                    timeout=source.timeout,
                )

            # Normalize result
            if isinstance(result, dict):
                if "status" not in result:
                    result["status"] = "healthy"
                return result
            elif isinstance(result, bool):
                return {
                    "status": "healthy" if result else "unhealthy",
                    "ok": result,
                }
            else:
                return {
                    "status": "healthy",
                    "data": result,
                }

        except asyncio.TimeoutError:
            return {
                "status": "unhealthy",
                "message": f"Check timed out after {source.timeout}s",
            }
        except Exception as e:
            logger.warning(f"Health check '{source.name}' failed: {e}")
            return {
                "status": "unhealthy",
                "message": str(e),
                "error": type(e).__name__,
            }

    def _calculate_overall_status(
        self,
        results: dict[HealthSource, dict[str, Any]],
    ) -> AggregateHealthStatus:
        """Calculate overall status from individual results."""
        has_unhealthy_critical = False
        has_unhealthy = False
        has_degraded = False

        for source, result in results.items():
            status = result.get("status", "unknown")

            if status == "unhealthy":
                if source.critical:
                    has_unhealthy_critical = True
                has_unhealthy = True
            elif status == "degraded":
                has_degraded = True

        if has_unhealthy_critical:
            return AggregateHealthStatus.UNHEALTHY
        elif has_unhealthy or has_degraded:
            return AggregateHealthStatus.DEGRADED
        else:
            return AggregateHealthStatus.HEALTHY

    def _calculate_summary(
        self,
        results: dict[HealthSource, dict[str, Any]],
    ) -> dict[str, int]:
        """Calculate summary counts."""
        summary = {
            "total": len(results),
            "healthy": 0,
            "degraded": 0,
            "unhealthy": 0,
            "unknown": 0,
        }

        for result in results.values():
            status = result.get("status", "unknown")
            if status in summary:
                summary[status] += 1
            else:
                summary["unknown"] += 1

        return summary

    def get_uptime_seconds(self) -> float:
        """Get service uptime in seconds."""
        return (datetime.now() - self._start_time).total_seconds()

    # Default health check implementations

    def _check_api(self) -> dict[str, Any]:
        """Check API health."""
        return {
            "status": "healthy",
            "message": "API is running",
            "uptime_seconds": self.get_uptime_seconds(),
        }

    def _check_memory(self) -> dict[str, Any]:
        """Check memory usage."""
        try:
            import psutil

            memory = psutil.virtual_memory()
            status = "degraded" if memory.percent > 90 else "healthy"

            return {
                "status": status,
                "usage_percent": memory.percent,
                "used_gb": memory.used / (1024**3),
                "available_gb": memory.available / (1024**3),
                "total_gb": memory.total / (1024**3),
            }
        except ImportError:
            return {
                "status": "unknown",
                "message": "psutil not available",
            }
        except Exception as e:
            return {
                "status": "unhealthy",
                "message": str(e),
            }

    def _check_disk(self) -> dict[str, Any]:
        """Check disk space."""
        try:
            import psutil

            disk = psutil.disk_usage(".")
            free_gb = disk.free / (1024**3)
            status = "degraded" if free_gb < 1.0 else "healthy"

            return {
                "status": status,
                "free_gb": free_gb,
                "used_gb": disk.used / (1024**3),
                "total_gb": disk.total / (1024**3),
                "usage_percent": disk.percent,
            }
        except ImportError:
            return {
                "status": "unknown",
                "message": "psutil not available",
            }
        except Exception as e:
            return {
                "status": "unhealthy",
                "message": str(e),
            }

    def _check_engines(self) -> dict[str, Any]:
        """Check engine availability."""
        try:
            from backend.services.engine_service import get_engine_service

            engine_service = get_engine_service()
            if engine_service is None:
                return {
                    "status": "degraded",
                    "message": "Engine service not initialized",
                    "available_engines": 0,
                }

            engines = engine_service.list_engines()
            count = len(engines)

            return {
                "status": "healthy" if count > 0 else "degraded",
                "message": f"{count} engine(s) available",
                "available_engines": count,
                "engines": [e.get("id") for e in engines[:10]],
            }
        except ImportError:
            return {
                "status": "unknown",
                "message": "Engine service not available",
            }
        except Exception as e:
            return {
                "status": "degraded",
                "message": str(e),
            }

    def _check_database(self) -> dict[str, Any]:
        """Check database connectivity."""
        try:
            # Simple filesystem check for SQLite-style databases
            from pathlib import Path

            # Try to find database file
            db_paths = [
                Path("voicestudio.db"),
                Path("data/voicestudio.db"),
                Path(".cache/voicestudio.db"),
            ]

            for db_path in db_paths:
                if db_path.exists():
                    return {
                        "status": "healthy",
                        "message": "Database file accessible",
                        "path": str(db_path),
                        "size_mb": db_path.stat().st_size / (1024 * 1024),
                    }

            # No database file found, but that might be OK for in-memory
            return {
                "status": "healthy",
                "message": "No persistent database configured",
            }
        except Exception as e:
            return {
                "status": "unhealthy",
                "message": str(e),
            }


# Singleton instance
_health_aggregation_service: HealthAggregationService | None = None


def get_health_aggregation_service() -> HealthAggregationService:
    """Get the singleton health aggregation service instance."""
    global _health_aggregation_service
    if _health_aggregation_service is None:
        _health_aggregation_service = HealthAggregationService()
    return _health_aggregation_service


def reset_health_aggregation_service() -> None:
    """Reset the singleton instance (for testing)."""
    global _health_aggregation_service
    _health_aggregation_service = None
