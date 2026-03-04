"""Observability endpoints (metrics, cache, profiler, scheduler) for VoiceStudio API."""

from __future__ import annotations

import logging
from datetime import datetime

from fastapi import FastAPI

logger = logging.getLogger(__name__)


def register_observability_routes(app: FastAPI) -> None:
    """Register all inline observability/metrics endpoints on the app."""

    from .error_handling import get_error_metrics
    from .middleware_setup import get_performance_middleware, lazy_import_response_cache

    @app.get("/api/metrics")
    def api_metrics():
        """Minimum observability metrics for API, engines, and errors."""
        payload = {
            "timestamp": datetime.utcnow().isoformat(),
            "errors": get_error_metrics(),
            "endpoints": {"enabled": False},
            "engines": {"enabled": False},
        }

        try:
            middleware = get_performance_middleware()
            if middleware is not None:
                payload["endpoints"] = {
                    "stats": middleware.get_stats(),
                    "by_endpoint": middleware.get_metrics(),
                }
        except Exception as e:
            payload["endpoints"] = {"enabled": False, "error": str(e)}

        try:
            from app.core.engines.performance_metrics import get_engine_metrics

            metrics = get_engine_metrics()
            payload["engines"] = {
                "summary": metrics.get_summary(),
                "total_engines": len(metrics.get_all_stats()),
            }
        except Exception as e:
            payload["engines"] = {"enabled": False, "error": str(e)}

        return payload

    @app.get("/api/metrics/history")
    def api_metrics_history(window: str = "24h"):
        """
        Phase 8 WS4: Get metrics history for trend charts.

        Query param: window (e.g. 24h, 48h, 168h for 1 week). Default 24h.
        """
        try:
            from backend.platform.monitoring.metrics_history import get_metrics_history

            hours = 24
            if window.endswith("h"):
                try:
                    hours = int(window[:-1])
                except ValueError:
                    logger.debug("Invalid window format %s (expected Nh), using default", window)
            elif window.endswith("d"):
                try:
                    hours = int(window[:-1]) * 24
                except ValueError:
                    logger.debug("Invalid window format %s (expected Nd), using default", window)
            history = get_metrics_history(window_hours=min(hours, 720))
            return {
                "window": window,
                "window_hours": hours,
                "count": len(history),
                "snapshots": history,
            }
        except ImportError as e:
            return {"error": f"Metrics history not available: {e}", "snapshots": []}
        except Exception as e:
            logger.warning(f"Failed to get metrics history: {e}")
            return {"error": str(e), "snapshots": []}

    @app.get("/api/cache/stats")
    async def cache_stats():
        """Get response cache statistics."""
        try:
            get_response_cache, _ = lazy_import_response_cache()
            if get_response_cache is None:
                return {"error": "Response cache not initialized"}
            cache = get_response_cache()
            if cache is None:
                return {"error": "Response cache instance not available"}
            return cache.get_stats()
        except Exception as e:
            logger.warning(f"Failed to get cache stats: {e}")
            return {"error": str(e)}

    @app.post("/api/cache/clear")
    async def clear_cache():
        """Clear all response cache entries."""
        try:
            get_response_cache, _ = lazy_import_response_cache()
            if get_response_cache is None:
                return {"error": "Response cache not initialized"}
            cache = get_response_cache()
            if cache is None:
                return {"error": "Response cache instance not available"}
            count = len(cache._cache)
            cache.clear()
            return {"message": "Cache cleared", "entries_cleared": count}
        except Exception as e:
            logger.warning(f"Failed to clear cache: {e}")
            return {"error": str(e)}

    @app.get("/api/profiler/stats")
    def profiler_stats():
        """Get performance profiler statistics."""
        try:
            from app.core.monitoring.profiler import get_profiler

            profiler = get_profiler()
            return profiler.get_stats()
        except Exception as e:
            logger.warning(f"Failed to get profiler stats: {e}")
            return {"error": str(e)}

    @app.get("/api/profiler/detailed")
    def profiler_detailed():
        """Get detailed performance profiler statistics."""
        try:
            from app.core.monitoring.profiler import get_profiler

            profiler = get_profiler()
            return profiler.get_detailed_stats()
        except Exception as e:
            logger.warning(f"Failed to get detailed profiler stats: {e}")
            return {"error": str(e)}

    @app.post("/api/profiler/reset")
    def profiler_reset():
        """Reset performance profiler data."""
        try:
            from app.core.monitoring.profiler import get_profiler

            profiler = get_profiler()
            profiler.reset()
            return {"message": "Profiler reset successfully"}
        except Exception as e:
            logger.warning(f"Failed to reset profiler: {e}")
            return {"error": str(e)}

    @app.get("/api/engines/metrics")
    def engine_metrics():
        """Get engine performance metrics."""
        try:
            from app.core.engines.router import router

            return router.get_engine_performance_stats()
        except Exception as e:
            logger.warning(f"Failed to get engine metrics: {e}")
            return {"error": str(e)}

    @app.get("/api/engines/metrics/{engine_name}")
    def engine_metrics_detail(engine_name: str):
        """Get performance metrics for a specific engine."""
        try:
            from app.core.engines.performance_metrics import get_engine_metrics

            metrics = get_engine_metrics()
            return metrics.get_engine_stats(engine_name)
        except Exception as e:
            logger.warning(f"Failed to get engine metrics for {engine_name}: {e}")
            return {"error": str(e)}

    @app.post("/api/engines/metrics/reset")
    def engine_metrics_reset(engine_name: str | None = None):
        """Reset engine performance metrics."""
        try:
            from app.core.engines.performance_metrics import get_engine_metrics

            metrics = get_engine_metrics()
            metrics.clear(engine_name)
            return {"message": f"Metrics reset for {engine_name or 'all engines'}"}
        except Exception as e:
            logger.warning(f"Failed to reset engine metrics: {e}")
            return {"error": str(e)}

    @app.get("/api/endpoints/metrics")
    def endpoint_metrics():
        """Get API endpoint performance metrics."""
        try:
            middleware = get_performance_middleware()
            if middleware is None:
                return {"error": "Performance monitoring middleware not initialized"}
            return middleware.get_stats()
        except Exception as e:
            logger.warning(f"Failed to get endpoint metrics: {e}")
            return {"error": str(e)}

    @app.get("/api/endpoints/metrics/{endpoint_key:path}")
    def endpoint_metrics_detail(endpoint_key: str):
        """Get performance metrics for a specific endpoint."""
        try:
            middleware = get_performance_middleware()
            if middleware is None:
                return {"error": "Performance monitoring middleware not initialized"}
            return middleware.get_metrics(endpoint_key)
        except Exception as e:
            logger.warning(f"Failed to get endpoint metrics for {endpoint_key}: {e}")
            return {"error": str(e)}

    @app.post("/api/endpoints/metrics/reset")
    def endpoint_metrics_reset():
        """Reset API endpoint performance metrics."""
        try:
            middleware = get_performance_middleware()
            if middleware is None:
                return {"error": "Performance monitoring middleware not initialized"}
            middleware.reset()
            return {"message": "Endpoint metrics reset successfully"}
        except Exception as e:
            logger.warning(f"Failed to reset endpoint metrics: {e}")
            return {"error": str(e)}

    @app.post("/api/cache/invalidate")
    def invalidate_cache(
        pattern: str | None = None,
        tags: str | None = None,
        path_prefix: str | None = None,
    ):
        """
        Invalidate cache entries by pattern, tags, or path prefix.

        Args:
            pattern: Pattern to match in cache key
            tags: Comma-separated list of tags to invalidate
            path_prefix: Path prefix to invalidate (e.g., "/api/profiles")
        """
        get_response_cache, _ = lazy_import_response_cache()
        cache = get_response_cache()

        tag_list = tags.split(",") if tags else None
        if tag_list:
            tag_list = [tag.strip() for tag in tag_list]

        count = cache.invalidate(
            pattern=pattern,
            tags=tag_list,
            path_prefix=path_prefix,
        )

        return {
            "message": "Cache invalidated",
            "entries_invalidated": count,
            "pattern": pattern,
            "tags": tag_list,
            "path_prefix": path_prefix,
        }

    @app.get("/api/validation/stats")
    def validation_stats(model_name: str | None = None):
        """Get validation statistics."""
        try:
            from .validation_optimizer import get_cache_stats, get_validation_stats

            stats = get_validation_stats(model_name)
            cache_stats_data = get_cache_stats()
            return {
                "validation_stats": stats,
                "cache_stats": cache_stats_data,
            }
        except Exception as e:
            logger.warning(f"Failed to get validation stats: {e}")
            return {"error": str(e)}

    @app.post("/api/validation/cache/clear")
    def validation_cache_clear():
        """Clear validation cache."""
        try:
            from .validation_optimizer import clear_schema_cache, clear_validation_cache

            clear_validation_cache()
            clear_schema_cache()
            return {"message": "Validation cache cleared successfully"}
        except Exception as e:
            logger.warning(f"Failed to clear validation cache: {e}")
            return {"error": str(e)}

    @app.get("/api/scheduler/stats")
    def scheduler_stats():
        """Get background task scheduler statistics."""
        try:
            from app.core.tasks.scheduler import get_scheduler

            scheduler = get_scheduler()
            return scheduler.get_stats()
        except Exception as e:
            logger.warning(f"Failed to get scheduler stats: {e}")
            return {"error": str(e)}

    @app.get("/api/scheduler/tasks")
    def scheduler_tasks(status: str | None = None, priority: str | None = None):
        """List scheduled tasks."""
        try:
            from app.core.tasks.scheduler import TaskPriority, TaskStatus, get_scheduler

            scheduler = get_scheduler()

            # Parse filters
            status_filter = None
            if status:
                try:
                    status_filter = TaskStatus[status.upper()]
                except KeyError:
                    return {"error": f"Invalid status: {status}"}

            priority_filter = None
            if priority:
                try:
                    priority_filter = TaskPriority[priority.upper()]
                except KeyError:
                    return {"error": f"Invalid priority: {priority}"}

            tasks = scheduler.list_tasks(status=status_filter, priority=priority_filter)

            return {
                "tasks": [
                    {
                        "id": task.id,
                        "name": task.name,
                        "priority": task.priority.name,
                        "status": task.status.value,
                        "created_at": task.created_at.isoformat(),
                        "scheduled_at": (task.scheduled_at.isoformat() if task.scheduled_at else None),
                        "next_run": (task.next_run.isoformat() if task.next_run else None),
                        "last_run": (task.last_run.isoformat() if task.last_run else None),
                        "interval": task.interval,
                        "retry_count": task.retry_count,
                        "max_retries": task.max_retries,
                        "error": task.error,
                    }
                    for task in tasks
                ],
                "count": len(tasks),
            }
        except Exception as e:
            logger.warning(f"Failed to list scheduler tasks: {e}")
            return {"error": str(e)}

    @app.get("/api/scheduler/tasks/{task_id}")
    def scheduler_task_detail(task_id: str):
        """Get details for a specific task."""
        try:
            from app.core.tasks.scheduler import get_scheduler

            scheduler = get_scheduler()
            task = scheduler.get_task(task_id)

            if not task:
                from fastapi import HTTPException

                raise HTTPException(status_code=404, detail="Task not found")

            return {
                "id": task.id,
                "name": task.name,
                "priority": task.priority.name,
                "status": task.status.value,
                "created_at": task.created_at.isoformat(),
                "scheduled_at": (task.scheduled_at.isoformat() if task.scheduled_at else None),
                "next_run": task.next_run.isoformat() if task.next_run else None,
                "last_run": task.last_run.isoformat() if task.last_run else None,
                "interval": task.interval,
                "retry_count": task.retry_count,
                "max_retries": task.max_retries,
                "error": task.error,
                "resource_requirements": task.resource_requirements,
            }
        except HTTPException:
            raise
        except Exception as e:
            logger.warning(f"Failed to get task details: {e}")
            return {"error": str(e)}

    @app.post("/api/scheduler/tasks/{task_id}/cancel")
    def scheduler_task_cancel(task_id: str):
        """Cancel a scheduled task."""
        try:
            from app.core.tasks.scheduler import get_scheduler

            scheduler = get_scheduler()
            success = scheduler.cancel_task(task_id)

            if not success:
                from fastapi import HTTPException

                raise HTTPException(status_code=404, detail="Task not found")

            return {"message": f"Task {task_id} cancelled successfully"}
        except HTTPException:
            raise
        except Exception as e:
            logger.warning(f"Failed to cancel task: {e}")
            return {"error": str(e)}
