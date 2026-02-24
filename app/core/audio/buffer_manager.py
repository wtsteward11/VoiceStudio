"""
Audio Buffer Management System
Automatic buffer cleanup, buffer pooling/reuse, memory-efficient buffer handling

Compatible with:
- Python 3.10+
- NumPy 1.26.4+
"""

from __future__ import annotations

import logging
import threading
import time
from collections import OrderedDict
from typing import Any

import numpy as np

# Try importing psutil for system memory monitoring
try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    psutil = None

# Try importing torch for GPU memory tracking
try:
    import torch

    HAS_TORCH = True
except ImportError:
    HAS_TORCH = False

logger = logging.getLogger(__name__)


class AudioBufferPool:
    """
    Pool of reusable audio buffers for memory efficiency.

    Features:
    - Buffer pooling by size and dtype
    - Automatic cleanup of unused buffers
    - Memory-efficient buffer reuse
    - LRU eviction policy
    """

    def __init__(
        self,
        max_pool_size: int = 50,
        max_buffer_age_seconds: float = 300.0,  # 5 minutes
        cleanup_interval_seconds: float = 60.0,  # 1 minute
        memory_pressure_threshold: float = 0.85,  # 85% system memory
        enable_memory_pressure_cleanup: bool = True,
    ):
        """
        Initialize audio buffer pool.

        Args:
            max_pool_size: Maximum number of buffers in pool
            max_buffer_age_seconds: Maximum age before buffer is removed
            cleanup_interval_seconds: Interval for cleanup
            memory_pressure_threshold: System memory usage threshold (0.0-1.0)
            enable_memory_pressure_cleanup: Enable cleanup on memory pressure
        """
        self.max_pool_size = max_pool_size
        self.max_buffer_age_seconds = max_buffer_age_seconds
        self.cleanup_interval_seconds = cleanup_interval_seconds
        self.memory_pressure_threshold = memory_pressure_threshold
        self.enable_memory_pressure_cleanup = enable_memory_pressure_cleanup

        # Pool storage: {buffer_key: (buffer, timestamp)}
        self._pool: OrderedDict[str, tuple[np.ndarray, float]] = OrderedDict()

        # Statistics
        self._hits = 0
        self._misses = 0
        self._created = 0
        self._reused = 0
        self._pressure_cleanups = 0
        self._last_cleanup = time.time()
        self._lock = threading.Lock()

    def _get_buffer_key(self, size: int, dtype: np.dtype[Any] | type) -> str:
        """Generate buffer key from size and dtype."""
        return f"{size}_{dtype}"

    def get_buffer(self, size: int, dtype: np.dtype | type = np.float32) -> np.ndarray:
        """
        Get buffer from pool or create new one (optimized).

        Args:
            size: Buffer size in samples
            dtype: Buffer dtype (default: float32)

        Returns:
            Buffer array
        """
        with self._lock:
            self._cleanup_if_needed()

            buffer_key = self._get_buffer_key(size, dtype)

            # Try to get from pool (exact match preferred)
            if buffer_key in self._pool:
                buffer, timestamp = self._pool.pop(buffer_key)
                # Check if buffer is still valid size
                if len(buffer) >= size:
                    # Resize if needed (reuse larger buffer)
                    if len(buffer) > size:
                        buffer = buffer[:size]
                    self._hits += 1
                    self._reused += 1
                    logger.debug(f"Reused buffer: {buffer_key}")
                    return buffer.copy()  # Return copy to avoid sharing
                else:
                    # Buffer too small, remove from pool
                    del self._pool[buffer_key]

            # Try to find suitable buffer (size >= requested)
            best_match_key = None
            best_match_size = float("inf")
            for key, (buf, _) in self._pool.items():
                buf_size = len(buf)
                if buf_size >= size and buf_size < best_match_size:
                    best_match_key = key
                    best_match_size = buf_size

            if best_match_key:
                buffer, _timestamp = self._pool.pop(best_match_key)
                if len(buffer) > size:
                    buffer = buffer[:size]
                self._hits += 1
                self._reused += 1
                logger.debug(f"Reused buffer (size match): {best_match_key}")
                return buffer.copy()

            # Create new buffer
            self._misses += 1
            self._created += 1
            buffer = np.zeros(size, dtype=dtype)
            logger.debug(f"Created new buffer: {buffer_key}")
            return buffer

    def return_buffer(self, buffer: np.ndarray):
        """
        Return buffer to pool for reuse.

        Args:
            buffer: Buffer to return
        """
        if buffer is None or buffer.size == 0:
            return

        with self._lock:
            self._cleanup_if_needed()

            buffer_key = self._get_buffer_key(len(buffer), buffer.dtype)

            # Check pool size
            if len(self._pool) >= self.max_pool_size:
                # Remove oldest buffer (LRU)
                oldest_key = next(iter(self._pool))
                del self._pool[oldest_key]

            # Add to pool
            self._pool[buffer_key] = (buffer.copy(), time.time())
            self._pool.move_to_end(buffer_key)  # LRU update
            logger.debug(f"Returned buffer to pool: {buffer_key}")

    def _get_system_memory_usage_percent(self) -> float | None:
        """Get current system memory usage percentage."""
        if not HAS_PSUTIL:
            return None
        try:
            return float(psutil.virtual_memory().percent) / 100.0
        except Exception as e:
            logger.debug(f"Failed to get system memory usage: {e}")
            return None

    def _cleanup_if_needed(self):
        """Clean up old buffers if cleanup interval has passed or memory pressure."""
        current_time = time.time()
        should_cleanup = current_time - self._last_cleanup >= self.cleanup_interval_seconds

        # Check for memory pressure
        memory_pressure = False
        if self.enable_memory_pressure_cleanup:
            system_memory = self._get_system_memory_usage_percent()
            if system_memory is not None and system_memory >= self.memory_pressure_threshold:
                memory_pressure = True
                should_cleanup = True

        if not should_cleanup:
            return

        # Remove expired buffers
        expired_keys = []
        for key, (_, timestamp) in self._pool.items():
            if current_time - timestamp > self.max_buffer_age_seconds:
                expired_keys.append(key)

        # Aggressive cleanup on memory pressure
        if memory_pressure:
            # Remove oldest buffers until memory pressure is relieved
            sorted_keys = list(self._pool.keys())
            # Remove up to 50% of buffers
            target_removal = max(1, len(sorted_keys) // 2)
            for key in sorted_keys[:target_removal]:
                if key not in expired_keys:
                    expired_keys.append(key)
            self._pressure_cleanups += 1
            logger.warning(
                f"Memory pressure detected ({system_memory:.2%}), "
                f"aggressively cleaning up {len(expired_keys)} buffers"
            )

        for key in expired_keys:
            del self._pool[key]

        self._last_cleanup = current_time

        if expired_keys:
            logger.debug(f"Cleaned up {len(expired_keys)} buffers")

    def clear(self):
        """Clear all buffers from pool."""
        with self._lock:
            count = len(self._pool)
            self._pool.clear()
            logger.info(f"Cleared {count} buffers from pool")

    def get_stats(self) -> dict[str, Any]:
        """Get pool statistics (enhanced)."""
        with self._lock:
            total_requests = self._hits + self._misses
            hit_rate = (self._hits / total_requests * 100) if total_requests > 0 else 0

            # Calculate memory usage
            total_memory_mb = sum(
                buffer.nbytes / (1024 * 1024) for buffer, _ in self._pool.values()
            )

            system_memory = self._get_system_memory_usage_percent()

            stats = {
                "pool_size": len(self._pool),
                "max_pool_size": self.max_pool_size,
                "hits": self._hits,
                "misses": self._misses,
                "created": self._created,
                "reused": self._reused,
                "pressure_cleanups": self._pressure_cleanups,
                "hit_rate": f"{hit_rate:.2f}%",
                "total_memory_mb": f"{total_memory_mb:.2f}",
                "memory_pressure_threshold": self.memory_pressure_threshold,
                "memory_pressure_cleanup_enabled": self.enable_memory_pressure_cleanup,
            }

            if system_memory is not None:
                stats["system_memory_usage_percent"] = f"{system_memory:.2%}"
                stats["under_memory_pressure"] = system_memory >= self.memory_pressure_threshold

            return stats


class AudioBufferManager:
    """
    Centralized audio buffer management system.

    Features:
    - Automatic buffer cleanup
    - Buffer pooling/reuse
    - Memory-efficient buffer handling
    - Buffer lifecycle tracking
    """

    def __init__(
        self,
        enable_pooling: bool = True,
        max_pool_size: int = 50,
        auto_cleanup_enabled: bool = True,
        cleanup_interval_seconds: float = 60.0,
        memory_pressure_threshold: float = 0.85,
        enable_memory_pressure_cleanup: bool = True,
    ):
        """
        Initialize audio buffer manager.

        Args:
            enable_pooling: Enable buffer pooling
            max_pool_size: Maximum buffers in pool
            auto_cleanup_enabled: Enable automatic cleanup
            cleanup_interval_seconds: Cleanup interval
            memory_pressure_threshold: System memory usage threshold (0.0-1.0)
            enable_memory_pressure_cleanup: Enable cleanup on memory pressure
        """
        self.enable_pooling = enable_pooling
        self.auto_cleanup_enabled = auto_cleanup_enabled
        self.cleanup_interval_seconds = cleanup_interval_seconds
        self.memory_pressure_threshold = memory_pressure_threshold
        self.enable_memory_pressure_cleanup = enable_memory_pressure_cleanup

        # Buffer pool
        self._pool: AudioBufferPool | None = None
        if enable_pooling:
            self._pool = AudioBufferPool(
                max_pool_size=max_pool_size,
                cleanup_interval_seconds=cleanup_interval_seconds,
                memory_pressure_threshold=memory_pressure_threshold,
                enable_memory_pressure_cleanup=enable_memory_pressure_cleanup,
            )

        # Active buffers tracking
        self._active_buffers: dict[int, tuple[np.ndarray, float]] = {}
        self._buffer_counter = 0
        self._lock = threading.Lock()

        # Statistics
        self._total_allocated = 0.0
        self._total_freed = 0.0
        self._peak_memory_mb = 0.0
        self._pressure_cleanups = 0

    def allocate_buffer(
        self, size: int, dtype: np.dtype | type = np.float32
    ) -> tuple[int, np.ndarray]:
        """
        Allocate a new audio buffer.

        Args:
            size: Buffer size in samples
            dtype: Buffer dtype (default: float32)

        Returns:
            Tuple of (buffer_id, buffer_array)
        """
        with self._lock:
            # Get buffer from pool or create new
            if self._pool:
                buffer = self._pool.get_buffer(size, dtype)
            else:
                buffer = np.zeros(size, dtype=dtype)

            # Track buffer
            buffer_id = self._buffer_counter
            self._buffer_counter += 1
            self._active_buffers[buffer_id] = (buffer, time.time())

            # Update statistics
            buffer_memory_mb = buffer.nbytes / (1024 * 1024)
            self._total_allocated += buffer_memory_mb
            current_memory = sum(
                buf.nbytes / (1024 * 1024) for buf, _ in self._active_buffers.values()
            )
            if current_memory > self._peak_memory_mb:
                self._peak_memory_mb = current_memory

            logger.debug(
                f"Allocated buffer {buffer_id} ({size} samples, " f"{buffer_memory_mb:.2f} MB)"
            )

            return buffer_id, buffer

    def free_buffer(self, buffer_id: int, return_to_pool: bool = True):
        """
        Free an audio buffer.

        Args:
            buffer_id: Buffer ID to free
            return_to_pool: Whether to return buffer to pool
        """
        with self._lock:
            if buffer_id not in self._active_buffers:
                logger.warning(f"Buffer {buffer_id} not found")
                return

            buffer, _ = self._active_buffers.pop(buffer_id)

            # Return to pool if enabled
            if return_to_pool and self._pool:
                self._pool.return_buffer(buffer)

            # Update statistics
            buffer_memory_mb = buffer.nbytes / (1024 * 1024)
            self._total_freed += buffer_memory_mb

            logger.debug(f"Freed buffer {buffer_id} ({buffer_memory_mb:.2f} MB)")

    def _get_system_memory_usage_percent(self) -> float | None:
        """Get current system memory usage percentage."""
        if not HAS_PSUTIL:
            return None
        try:
            return float(psutil.virtual_memory().percent) / 100.0
        except Exception as e:
            logger.debug(f"Failed to get system memory usage: {e}")
            return None

    def cleanup_old_buffers(self, max_age_seconds: float = 300.0):
        """
        Clean up old buffers (enhanced with memory pressure detection).

        Args:
            max_age_seconds: Maximum age for buffers
        """
        if not self.auto_cleanup_enabled:
            return

        with self._lock:
            current_time = time.time()
            to_remove = []

            # Check for memory pressure
            memory_pressure = False
            if self.enable_memory_pressure_cleanup:
                system_memory = self._get_system_memory_usage_percent()
                if system_memory is not None and system_memory >= self.memory_pressure_threshold:
                    memory_pressure = True
                    # Aggressive cleanup - reduce max_age for pressure
                    max_age_seconds = min(max_age_seconds, 60.0)  # Max 1 minute

            for buffer_id, (_buffer, timestamp) in self._active_buffers.items():
                if current_time - timestamp > max_age_seconds:
                    to_remove.append(buffer_id)

            # Aggressive cleanup on memory pressure
            if memory_pressure and len(to_remove) < len(self._active_buffers) // 2:
                # Remove oldest buffers until we've removed at least 50%
                sorted_buffers = sorted(
                    self._active_buffers.items(), key=lambda x: x[1][1]  # Sort by timestamp
                )
                target_removal = max(1, len(sorted_buffers) // 2)
                for buffer_id, _ in sorted_buffers[:target_removal]:
                    if buffer_id not in to_remove:
                        to_remove.append(buffer_id)
                self._pressure_cleanups += 1
                logger.warning(
                    f"Memory pressure detected ({system_memory:.2%}), "
                    f"aggressively cleaning up {len(to_remove)} buffers"
                )

            for buffer_id in to_remove:
                self.free_buffer(buffer_id, return_to_pool=True)

            if to_remove:
                logger.info(f"Cleaned up {len(to_remove)} old buffers")

    def get_stats(self) -> dict[str, Any]:
        """Get buffer manager statistics (enhanced)."""
        with self._lock:
            current_memory = sum(
                buf.nbytes / (1024 * 1024) for buf, _ in self._active_buffers.values()
            )

            system_memory = self._get_system_memory_usage_percent()

            stats = {
                "active_buffers": len(self._active_buffers),
                "total_allocated_mb": f"{self._total_allocated:.2f}",
                "total_freed_mb": f"{self._total_freed:.2f}",
                "current_memory_mb": f"{current_memory:.2f}",
                "peak_memory_mb": f"{self._peak_memory_mb:.2f}",
                "pressure_cleanups": self._pressure_cleanups,
                "pooling_enabled": self.enable_pooling,
                "memory_pressure_threshold": self.memory_pressure_threshold,
                "memory_pressure_cleanup_enabled": self.enable_memory_pressure_cleanup,
            }

            if system_memory is not None:
                stats["system_memory_usage_percent"] = f"{system_memory:.2%}"
                stats["under_memory_pressure"] = system_memory >= self.memory_pressure_threshold

            if self._pool:
                stats["pool"] = self._pool.get_stats()

            return stats

    def clear_all(self):
        """Clear all buffers and pool."""
        with self._lock:
            # Free all active buffers
            buffer_ids = list(self._active_buffers.keys())
            for buffer_id in buffer_ids:
                self.free_buffer(buffer_id, return_to_pool=False)

            # Clear pool
            if self._pool:
                self._pool.clear()

            logger.info("Cleared all buffers")


# Global buffer manager instance
_buffer_manager: AudioBufferManager | None = None


def get_buffer_manager() -> AudioBufferManager:
    """Get or create global buffer manager instance."""
    global _buffer_manager
    if _buffer_manager is None:
        _buffer_manager = AudioBufferManager()
    return _buffer_manager


def set_buffer_manager(manager: AudioBufferManager):
    """Set global buffer manager instance."""
    global _buffer_manager
    _buffer_manager = manager


# Export
__all__ = [
    "AudioBufferManager",
    "AudioBufferPool",
    "get_buffer_manager",
    "set_buffer_manager",
]
