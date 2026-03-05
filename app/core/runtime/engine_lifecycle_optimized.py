"""
Optimized Engine Lifecycle Management

High-performance engine lifecycle management with:
- Parallel health checks
- Optimized locking strategies
- Event-driven monitoring
- Health check caching
- Pre-warming support
"""

from __future__ import annotations

import logging
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from queue import Empty, Queue
from typing import Any

from .engine_lifecycle import (
    EngineInstance,
    EngineLifecycleManager,
    EngineState,
    PortManager,
    ResourceManager,
)

logger = logging.getLogger(__name__)

HAS_RUNTIME_ENGINE = False
RuntimeEngine: Any = None

try:
    from .runtime_engine_enhanced import EnhancedRuntimeEngine

    RuntimeEngine = EnhancedRuntimeEngine
    HAS_RUNTIME_ENGINE = True
except ImportError:
    try:
        from .runtime_engine import RuntimeEngine as _FallbackRuntimeEngine

        RuntimeEngine = _FallbackRuntimeEngine
        HAS_RUNTIME_ENGINE = True
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except ImportError:
        pass


class OptimizedEngineLifecycleManager(EngineLifecycleManager):
    """
    Optimized engine lifecycle manager with performance improvements.

    Optimizations:
    - Parallel health checks
    - Health check caching with TTL
    - Event-driven monitoring
    - Optimized locking (read-write locks)
    - Pre-warming support
    - Batch operations
    """

    def __init__(
        self,
        workspace_root: str = ".",
        port_manager: PortManager | None = None,
        resource_manager: ResourceManager | None = None,
        health_check_workers: int = 4,
        health_check_cache_ttl: float = 2.0,  # Cache health checks for 2 seconds
        enable_prewarming: bool = True,
    ):
        """
        Initialize optimized engine lifecycle manager.

        Args:
            workspace_root: Workspace root directory
            port_manager: Port manager instance
            resource_manager: Resource manager instance
            health_check_workers: Number of parallel health check workers
            health_check_cache_ttl: Health check cache TTL in seconds
            enable_prewarming: Enable engine pre-warming
        """
        super().__init__(
            workspace_root=workspace_root,
            port_manager=port_manager,
            resource_manager=resource_manager,
        )

        self.health_check_workers = health_check_workers
        self.health_check_cache_ttl = health_check_cache_ttl
        self.enable_prewarming = enable_prewarming

        # Health check cache: {engine_id: (is_healthy, timestamp)}
        self._health_cache: dict[str, tuple[bool, float]] = {}

        # Event queue for event-driven monitoring
        self._event_queue: Queue = Queue()

        # Read-write lock for better concurrency
        self._read_lock = threading.RLock()  # Reentrant read lock
        self._write_lock = threading.Lock()  # Write lock

        # Health check executor
        self._health_executor: ThreadPoolExecutor | None = None

        # Pre-warming configuration
        self._prewarm_configs: dict[str, dict[str, Any]] = {}

        # Statistics
        self._stats = {
            "health_checks": 0,
            "health_cache_hits": 0,
            "health_cache_misses": 0,
            "parallel_health_checks": 0,
            "prewarmed_engines": 0,
        }

    def _get_health_executor(self) -> ThreadPoolExecutor:
        """Get or create health check executor."""
        if self._health_executor is None or self._health_executor._shutdown:
            self._health_executor = ThreadPoolExecutor(max_workers=self.health_check_workers)
        return self._health_executor

    def _check_health_cached(self, engine: EngineInstance) -> bool:
        """
        Check engine health with caching.

        Args:
            engine: Engine instance

        Returns:
            True if healthy, False otherwise
        """
        now = time.time()
        engine_id = engine.engine_id

        # Check cache
        if engine_id in self._health_cache:
            is_healthy, cached_time = self._health_cache[engine_id]
            age = now - cached_time
            if age < self.health_check_cache_ttl:
                self._stats["health_cache_hits"] += 1
                return is_healthy

        # Cache miss or expired, perform actual check
        self._stats["health_cache_misses"] += 1
        is_healthy = super()._check_health(engine)

        # Update cache
        self._health_cache[engine_id] = (is_healthy, now)
        self._stats["health_checks"] += 1

        return is_healthy

    def _monitor_engines_optimized(self):
        """
        Optimized engine monitoring with parallel health checks.
        """
        with self._read_lock:
            engines_to_check = [
                engine
                for engine in list(self.engines.values())
                if engine.state in [EngineState.HEALTHY, EngineState.BUSY]
            ]

        if not engines_to_check:
            return

        # Parallel health checks
        executor = self._get_health_executor()
        futures = {
            executor.submit(self._check_health_cached, engine): engine
            for engine in engines_to_check
        }

        health_results = {}
        for future in as_completed(futures):
            engine = futures[future]
            try:
                is_healthy = future.result()
                health_results[engine.engine_id] = is_healthy
            except Exception as e:
                logger.warning(f"Health check failed for {engine.engine_id}: {e}")
                health_results[engine.engine_id] = False

        self._stats["parallel_health_checks"] += len(engines_to_check)

        # Process results
        with self._write_lock:
            for engine in engines_to_check:
                engine_id = engine.engine_id
                is_healthy = health_results.get(engine_id, False)

                if not is_healthy:
                    engine.health_check_failures += 1
                    if engine.health_check_failures >= engine.max_health_failures:
                        logger.error(
                            f"Engine {engine_id} health check failed "
                            f"{engine.health_check_failures} times"
                        )
                        engine.set_state(EngineState.ERROR)
                else:
                    # Reset failure count on success
                    engine.health_check_failures = 0

                # Check idle timeout
                if engine.state == EngineState.HEALTHY and engine.is_idle():
                    logger.info(f"Engine {engine_id} is idle, transitioning to draining")
                    self._request_drain(engine_id)

    def _start_monitor(self):
        """Start optimized lifecycle monitoring thread."""

        def monitor_loop():
            while self.running:
                try:
                    # Check for events first (event-driven)
                    try:
                        event = self._event_queue.get(timeout=1.0)
                        self._handle_event(event)
                    except Empty:
                        ...

                    # Periodic monitoring (optimized)
                    self._monitor_engines_optimized()

                    # Adaptive sleep based on activity
                    sleep_time = 5.0  # Default
                    with self._read_lock:
                        active_engines = sum(
                            1
                            for e in self.engines.values()
                            if e.state in [EngineState.HEALTHY, EngineState.BUSY]
                        )
                        if active_engines == 0:
                            sleep_time = 10.0  # Sleep longer when idle
                        elif active_engines > 5:
                            sleep_time = 2.0  # Check more frequently when busy

                    time.sleep(sleep_time)
                except Exception as e:
                    logger.error(f"Error in lifecycle monitor: {e}")
                    time.sleep(5)

        self.monitor_thread = threading.Thread(target=monitor_loop, daemon=True)
        self.monitor_thread.start()

    def _handle_event(self, event: dict[str, Any]):
        """Handle lifecycle event."""
        event_type = event.get("type")

        if event_type == "health_check":
            engine_id_val: str | None = event.get("engine_id")
            if engine_id_val is not None:
                with self._read_lock:
                    engine = self.engines.get(engine_id_val)
                if engine:
                    self._check_health_cached(engine)
        elif event_type == "state_change":
            engine_id_val_sc: str | None = event.get("engine_id")
            if engine_id_val_sc is not None:
                self._health_cache.pop(engine_id_val_sc, None)

    def acquire_engine(
        self, engine_id: str, job_id: str | None = None, auto_start: bool = True
    ) -> EngineInstance | None:
        """
        Acquire engine with optimized locking.
        """
        with self._read_lock:
            # Fast path: check if engine is available
            if engine_id in self.singletons:
                engine = self.singletons[engine_id]
                if engine.state == EngineState.HEALTHY and engine.job_lease is None:
                    # Upgrade to write lock for state change
                    with self._write_lock:
                        engine.job_lease = job_id
                        engine.set_state(EngineState.BUSY)
                        engine.update_activity()
                        # Invalidate health cache
                        self._health_cache.pop(engine_id, None)
                    return engine

        # Fall back to parent implementation for complex cases
        with self._write_lock:
            return super().acquire_engine(engine_id, job_id, auto_start)

    def _start_engine(self, engine: EngineInstance) -> bool:
        """Start engine with optimized health check."""
        result = super()._start_engine(engine)

        if result:
            # Invalidate health cache
            self._health_cache.pop(engine.engine_id, None)
            # Queue health check event
            self._event_queue.put(
                {
                    "type": "health_check",
                    "engine_id": engine.engine_id,
                }
            )

        return result

    def prewarm_engine(
        self,
        engine_id: str,
        count: int = 1,
        config: dict[str, Any] | None = None,
    ) -> int:
        """
        Pre-warm engine instances for faster access.

        Args:
            engine_id: Engine identifier
            count: Number of instances to pre-warm
            config: Optional pre-warming configuration

        Returns:
            Number of successfully pre-warmed instances
        """
        if not self.enable_prewarming:
            return 0

        if engine_id not in self.engines:
            logger.warning(f"Engine {engine_id} not registered, cannot pre-warm")
            return 0

        prewarmed = 0
        with self._write_lock:
            # Check if pool-based
            if engine_id in self.engine_pools:
                pool = self.engine_pools[engine_id]
                pool_size = self.pool_sizes.get(engine_id, 1)

                # Pre-warm up to pool size
                target_count = min(count, pool_size - len(pool))

                for _i in range(target_count):
                    engine = EngineInstance(
                        engine_id=f"{engine_id}_prewarm_{len(pool)}",
                        manifest=self.engines[engine_id].manifest,
                    )
                    pool.append(engine)

                    if self._start_engine(engine):
                        prewarmed += 1
                        self._stats["prewarmed_engines"] += 1
                    else:
                        pool.remove(engine)
            else:
                # Singleton - can only pre-warm if not already started
                singleton = self.singletons.get(engine_id)
                if singleton and singleton.state == EngineState.STOPPED:
                    engine = singleton
                    if self._start_engine(engine):
                        prewarmed += 1
                        self._stats["prewarmed_engines"] += 1

        logger.info(f"Pre-warmed {prewarmed} instances of engine {engine_id}")
        return prewarmed

    def get_stats(self) -> dict[str, Any]:
        """
        Get lifecycle manager statistics.

        Returns:
            Dictionary with statistics
        """
        with self._read_lock:
            engine_counts = {
                state.name: sum(1 for e in self.engines.values() if e.state == state)
                for state in EngineState
            }

        return {
            "engines": engine_counts,
            "health_checks": self._stats["health_checks"],
            "health_cache_hits": self._stats["health_cache_hits"],
            "health_cache_misses": self._stats["health_cache_misses"],
            "health_cache_hit_rate": (
                self._stats["health_cache_hits"]
                / (self._stats["health_cache_hits"] + self._stats["health_cache_misses"])
                if (self._stats["health_cache_hits"] + self._stats["health_cache_misses"]) > 0
                else 0.0
            ),
            "parallel_health_checks": self._stats["parallel_health_checks"],
            "prewarmed_engines": self._stats["prewarmed_engines"],
        }

    def clear_health_cache(self):
        """Clear health check cache."""
        with self._write_lock:
            self._health_cache.clear()
            logger.debug("Health check cache cleared")

    def shutdown(self):
        """Shutdown lifecycle manager."""
        self.running = False

        # Shutdown health check executor
        if self._health_executor:
            self._health_executor.shutdown(wait=True)
            self._health_executor = None


# Factory function
def create_optimized_lifecycle_manager(
    workspace_root: str = ".",
    health_check_workers: int = 4,
    health_check_cache_ttl: float = 2.0,
    enable_prewarming: bool = True,
) -> OptimizedEngineLifecycleManager:
    """
    Create optimized engine lifecycle manager.

    Args:
        workspace_root: Workspace root directory
        health_check_workers: Number of parallel health check workers
        health_check_cache_ttl: Health check cache TTL in seconds
        enable_prewarming: Enable engine pre-warming

    Returns:
        OptimizedEngineLifecycleManager instance
    """
    return OptimizedEngineLifecycleManager(
        workspace_root=workspace_root,
        health_check_workers=health_check_workers,
        health_check_cache_ttl=health_check_cache_ttl,
        enable_prewarming=enable_prewarming,
    )


# Export
__all__ = ["OptimizedEngineLifecycleManager", "create_optimized_lifecycle_manager"]
