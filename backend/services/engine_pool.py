"""
Engine Warm-Pool System.

Task 1.2.2: Keep frequently-used engines loaded for faster inference.
Manages a pool of preloaded engines to reduce cold-start latency.
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Generic, TypeVar

logger = logging.getLogger(__name__)

T = TypeVar("T")  # Engine type


class EngineState(Enum):
    """State of a pooled engine."""

    UNLOADED = "unloaded"
    LOADING = "loading"
    READY = "ready"
    BUSY = "busy"
    ERROR = "error"
    UNLOADING = "unloading"


@dataclass
class EngineStats:
    """Statistics for a pooled engine."""

    load_count: int = 0
    use_count: int = 0
    error_count: int = 0
    total_use_time_ms: float = 0.0
    total_load_time_ms: float = 0.0
    last_used: datetime | None = None
    last_loaded: datetime | None = None


@dataclass
class PooledEngine(Generic[T]):
    """A pooled engine instance."""

    engine_type: str
    instance: T | None = None
    state: EngineState = EngineState.UNLOADED
    stats: EngineStats = field(default_factory=EngineStats)
    config: dict[str, Any] = field(default_factory=dict)
    memory_bytes: int = 0

    @property
    def is_available(self) -> bool:
        """Whether engine is available for use."""
        return self.state == EngineState.READY


@dataclass
class PoolConfig:
    """Configuration for the engine pool."""

    max_engines: int = 5
    preload_engines: list[str] = field(default_factory=list)
    idle_timeout_seconds: float = 300.0  # Unload after 5 min idle
    max_memory_bytes: int | None = None
    enable_preloading: bool = True


class EnginePool(Generic[T]):
    """
    Pool of preloaded engines for low-latency inference.

    Features:
    - Automatic preloading of common engines
    - LRU eviction when pool is full
    - Idle timeout for memory management
    - Usage statistics and monitoring
    - Async loading/unloading
    """

    def __init__(
        self,
        loader: Callable[[str, dict[str, Any]], Awaitable[T]],
        unloader: Callable[[T], Awaitable[None]],
        config: PoolConfig | None = None,
        memory_estimator: Callable[[str], int] | None = None,
    ):
        """
        Args:
            loader: Async function to load an engine (engine_type, config) -> instance
            unloader: Async function to unload an engine (instance) -> None
            config: Pool configuration
            memory_estimator: Function to estimate engine memory usage
        """
        self._loader = loader
        self._unloader = unloader
        self.config = config or PoolConfig()
        self._memory_estimator = memory_estimator or (lambda x: 1024 * 1024 * 1024)  # Default 1GB

        self._engines: dict[str, PooledEngine[T]] = {}
        self._locks: dict[str, asyncio.Lock] = {}
        self._global_lock = asyncio.Lock()
        self._total_memory = 0
        self._running = True

        # Track usage for LRU
        self._usage_order: list[str] = []

    @property
    def loaded_engines(self) -> list[str]:
        """List of currently loaded engine types."""
        return [k for k, v in self._engines.items() if v.state == EngineState.READY]

    @property
    def engine_count(self) -> int:
        """Number of loaded engines."""
        return len(self.loaded_engines)

    @property
    def total_memory_bytes(self) -> int:
        """Total memory used by loaded engines."""
        return self._total_memory

    async def start(self) -> None:
        """Start the engine pool and preload engines."""
        if self.config.enable_preloading:
            await self._preload_engines()

        # Start idle timeout checker
        asyncio.create_task(self._idle_checker())

    async def _preload_engines(self) -> None:
        """Preload configured engines."""
        for engine_type in self.config.preload_engines:
            try:
                await self.get_or_load(engine_type)
                logger.info(f"Preloaded engine: {engine_type}")
            except Exception as e:
                logger.warning(f"Failed to preload {engine_type}: {e}")

    async def _idle_checker(self) -> None:
        """Periodically check and unload idle engines."""
        while self._running:
            try:
                await asyncio.sleep(60)  # Check every minute

                cutoff = datetime.now() - timedelta(seconds=self.config.idle_timeout_seconds)

                for engine_type, engine in list(self._engines.items()):
                    if engine.state == EngineState.READY:
                        if engine.stats.last_used and engine.stats.last_used < cutoff:
                            # Don't unload preloaded engines
                            if engine_type not in self.config.preload_engines:
                                logger.info(f"Unloading idle engine: {engine_type}")
                                await self.unload(engine_type)

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Idle checker error: {e}")

    def _get_lock(self, engine_type: str) -> asyncio.Lock:
        """Get or create a lock for an engine type."""
        if engine_type not in self._locks:
            self._locks[engine_type] = asyncio.Lock()
        return self._locks[engine_type]

    async def _ensure_capacity(self, memory_needed: int) -> bool:
        """Ensure there's capacity for a new engine."""
        # Check engine count
        if self.engine_count >= self.config.max_engines and not await self._evict_lru():
            return False

        # Check memory limit
        if self.config.max_memory_bytes:
            while self._total_memory + memory_needed > self.config.max_memory_bytes:
                if not await self._evict_lru():
                    return False

        return True

    async def _evict_lru(self) -> bool:
        """Evict the least recently used engine."""
        # Find evictable engine (not preloaded, not busy)
        for engine_type in self._usage_order:
            if engine_type in self.config.preload_engines:
                continue

            engine = self._engines.get(engine_type)
            if engine and engine.state == EngineState.READY:
                await self.unload(engine_type)
                return True

        return False

    def _update_usage(self, engine_type: str) -> None:
        """Update LRU tracking."""
        if engine_type in self._usage_order:
            self._usage_order.remove(engine_type)
        self._usage_order.append(engine_type)

    async def get_or_load(
        self,
        engine_type: str,
        config: dict[str, Any] | None = None,
    ) -> T:
        """
        Get a loaded engine or load it.

        Args:
            engine_type: Type of engine to get
            config: Engine configuration

        Returns:
            Loaded engine instance
        """
        lock = self._get_lock(engine_type)

        async with lock:
            # Check if already loaded
            if engine_type in self._engines:
                engine = self._engines[engine_type]
                if engine.state == EngineState.READY and engine.instance:
                    self._update_usage(engine_type)
                    engine.stats.last_used = datetime.now()
                    return engine.instance

            # Need to load
            memory_needed = self._memory_estimator(engine_type)

            async with self._global_lock:
                if not await self._ensure_capacity(memory_needed):
                    raise MemoryError(f"Cannot load {engine_type}: pool at capacity")

            # Create engine entry
            engine = PooledEngine[T](
                engine_type=engine_type,
                state=EngineState.LOADING,
                config=config or {},
                memory_bytes=memory_needed,
            )
            self._engines[engine_type] = engine

            try:
                start_time = time.time()
                engine.instance = await self._loader(engine_type, engine.config)
                load_time = (time.time() - start_time) * 1000

                engine.state = EngineState.READY
                engine.stats.load_count += 1
                engine.stats.total_load_time_ms += load_time
                engine.stats.last_loaded = datetime.now()
                engine.stats.last_used = datetime.now()

                self._total_memory += memory_needed
                self._update_usage(engine_type)

                logger.info(f"Loaded engine {engine_type} in {load_time:.0f}ms")
                return engine.instance

            except Exception as e:
                engine.state = EngineState.ERROR
                engine.stats.error_count += 1
                del self._engines[engine_type]
                raise RuntimeError(f"Failed to load {engine_type}: {e}") from e

    async def unload(self, engine_type: str) -> bool:
        """Unload an engine."""
        lock = self._get_lock(engine_type)

        async with lock:
            if engine_type not in self._engines:
                return False

            engine = self._engines[engine_type]

            if engine.state == EngineState.BUSY:
                logger.warning(f"Cannot unload busy engine: {engine_type}")
                return False

            engine.state = EngineState.UNLOADING

            try:
                if engine.instance:
                    await self._unloader(engine.instance)

                self._total_memory -= engine.memory_bytes
                del self._engines[engine_type]

                if engine_type in self._usage_order:
                    self._usage_order.remove(engine_type)

                logger.info(f"Unloaded engine: {engine_type}")
                return True

            except Exception as e:
                engine.state = EngineState.ERROR
                logger.error(f"Error unloading {engine_type}: {e}")
                return False

    async def use_engine(
        self,
        engine_type: str,
        operation: Callable[[T], Awaitable[Any]],
        config: dict[str, Any] | None = None,
    ) -> Any:
        """
        Use an engine for an operation.

        Handles loading, locking, and statistics.
        """
        engine_instance = await self.get_or_load(engine_type, config)
        engine = self._engines[engine_type]

        lock = self._get_lock(engine_type)
        async with lock:
            engine.state = EngineState.BUSY

            try:
                start_time = time.time()
                result = await operation(engine_instance)
                use_time = (time.time() - start_time) * 1000

                engine.stats.use_count += 1
                engine.stats.total_use_time_ms += use_time
                engine.stats.last_used = datetime.now()

                return result

            finally:
                engine.state = EngineState.READY

    async def stop(self) -> None:
        """Stop the pool and unload all engines."""
        self._running = False

        for engine_type in list(self._engines.keys()):
            await self.unload(engine_type)

        logger.info("Engine pool stopped")

    def get_engine_stats(self, engine_type: str) -> dict[str, Any] | None:
        """Get statistics for an engine."""
        engine = self._engines.get(engine_type)
        if not engine:
            return None

        return {
            "state": engine.state.value,
            "memory_mb": round(engine.memory_bytes / 1e6, 1),
            "load_count": engine.stats.load_count,
            "use_count": engine.stats.use_count,
            "error_count": engine.stats.error_count,
            "avg_load_time_ms": (
                round(engine.stats.total_load_time_ms / engine.stats.load_count, 1)
                if engine.stats.load_count > 0
                else 0
            ),
            "avg_use_time_ms": (
                round(engine.stats.total_use_time_ms / engine.stats.use_count, 1)
                if engine.stats.use_count > 0
                else 0
            ),
            "last_used": engine.stats.last_used.isoformat() if engine.stats.last_used else None,
        }

    def get_stats(self) -> dict:
        """Get pool statistics."""
        return {
            "loaded_engines": self.loaded_engines,
            "engine_count": self.engine_count,
            "max_engines": self.config.max_engines,
            "total_memory_mb": round(self._total_memory / 1e6, 1),
            "preloaded": self.config.preload_engines,
            "engines": {k: self.get_engine_stats(k) for k in self._engines},
        }


# Global engine pool instance
_engine_pool: EnginePool | None = None


async def get_engine_pool() -> EnginePool:
    """Get or create the global engine pool."""
    global _engine_pool
    if _engine_pool is None:
        # Default loader/unloader - returns stub when not properly configured
        async def default_loader(engine_type: str, config: dict[str, Any]) -> Any:
            """Default fallback loader that returns a stub engine.

            This should be replaced with the actual engine loader via
            configure_engine_pool() during application startup.
            """
            logger.warning(
                f"Engine pool not configured - returning stub for {engine_type}. "
                "Call configure_engine_pool() during startup to enable real engines."
            )

            class FallbackStubEngine:
                """Fallback stub when engine pool is not configured."""

                def __init__(self, name: str):
                    self.name = name
                    self.is_stub = True
                    self.available = False

                def synthesize(self, *args, **kwargs):
                    return {
                        "success": False,
                        "error": "Engine pool not configured",
                        "error_code": "POOL_NOT_CONFIGURED",
                        "engine": self.name,
                    }

                def transcribe(self, *args, **kwargs):
                    return {
                        "success": False,
                        "error": "Engine pool not configured",
                        "error_code": "POOL_NOT_CONFIGURED",
                        "engine": self.name,
                    }

                def process(self, *args, **kwargs):
                    return {
                        "success": False,
                        "error": "Engine pool not configured",
                        "error_code": "POOL_NOT_CONFIGURED",
                        "engine": self.name,
                    }

                def is_available(self) -> bool:
                    return False

                def cleanup(self):
                    pass

            return FallbackStubEngine(engine_type)

        async def default_unloader(instance: Any) -> None:
            """Default unloader that handles cleanup gracefully."""
            if hasattr(instance, "cleanup"):
                instance.cleanup()

        _engine_pool = EnginePool(
            loader=default_loader,
            unloader=default_unloader,
        )
    return _engine_pool


def configure_engine_pool(
    loader: Callable[[str, dict[str, Any]], Awaitable[Any]],
    unloader: Callable[[Any], Awaitable[None]],
    config: PoolConfig | None = None,
) -> EnginePool:
    """Configure and return the global engine pool."""
    global _engine_pool
    _engine_pool = EnginePool(
        loader=loader,
        unloader=unloader,
        config=config,
    )
    return _engine_pool
