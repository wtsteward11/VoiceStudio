"""
Model Caching System

Provides LRU cache for loaded models with memory limits, statistics,
and cache warming. Supports all engine types (XTTS, Whisper, RVC, etc.).
"""

from __future__ import annotations

import logging
import os
import time
from collections import OrderedDict
from collections.abc import Callable
from typing import Any

# Try importing psutil for memory pressure detection
try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    psutil = None

logger = logging.getLogger(__name__)


class ModelCache:
    """
    LRU cache for loaded models with memory limits and statistics.

    Features:
    - LRU eviction policy
    - Memory size limits
    - Cache statistics
    - Cache warming support
    - Per-model memory tracking
    """

    def __init__(
        self,
        max_models: int = 10,
        max_memory_mb: float | None = None,
        default_ttl: float | None = None,
        enable_dynamic_limits: bool = True,
        memory_pressure_threshold: float = 0.85,  # 85% memory usage
        low_memory_threshold: float = 0.70,  # 70% memory usage
        auto_eviction_enabled: bool = True,
        track_gpu_memory: bool = True,
    ):
        """
        Initialize model cache.

        Args:
            max_models: Maximum number of models to cache
            max_memory_mb: Maximum memory usage in MB (None = unlimited)
            default_ttl: Default time-to-live in seconds (None = no expiration)
            enable_dynamic_limits: Enable dynamic memory limit adjustment
            memory_pressure_threshold: Memory pressure threshold (0.0-1.0)
            low_memory_threshold: Low memory threshold for proactive eviction (0.0-1.0)
            auto_eviction_enabled: Enable automatic eviction on high memory
            track_gpu_memory: Track GPU memory usage if available
        """
        self.max_models = max_models
        self.max_memory_mb = max_memory_mb
        self.default_ttl = default_ttl
        self.enable_dynamic_limits = enable_dynamic_limits
        self.memory_pressure_threshold = memory_pressure_threshold
        self.low_memory_threshold = low_memory_threshold
        self.auto_eviction_enabled = auto_eviction_enabled
        self.track_gpu_memory = track_gpu_memory

        # Original limits (for dynamic adjustment)
        self._original_max_models = max_models
        self._original_max_memory_mb = max_memory_mb

        # Cache storage: OrderedDict for LRU behavior
        self._cache: OrderedDict[str, dict[str, Any]] = OrderedDict()

        # Statistics
        self._stats = {
            "hits": 0,
            "misses": 0,
            "evictions": 0,
            "total_loaded": 0,
            "current_memory_mb": 0.0,
            "current_gpu_memory_mb": 0.0,
            "dynamic_adjustments": 0,
            "pressure_evictions": 0,
            "proactive_evictions": 0,
        }

        # Timestamps for TTL
        self._timestamps: dict[str, float] = {}

        # Process for memory monitoring
        self._process = None
        if HAS_PSUTIL:
            try:
                self._process = psutil.Process(os.getpid())
            except Exception as e:
                logger.warning(f"Failed to get process: {e}")

        logger.info(
            f"ModelCache initialized: max_models={max_models}, "
            f"max_memory_mb={max_memory_mb}, default_ttl={default_ttl}, "
            f"dynamic_limits={enable_dynamic_limits}, "
            f"pressure_threshold={memory_pressure_threshold:.1%}, "
            f"low_memory_threshold={low_memory_threshold:.1%}"
        )

    def _generate_key(self, engine: str, model_name: str, device: str | None = None) -> str:
        """
        Generate cache key for model.

        Args:
            engine: Engine name
            model_name: Model name/identifier
            device: Device (cuda/cpu) - models on different devices
                are cached separately

        Returns:
            Cache key string
        """
        if device:
            return f"{engine}::{model_name}::{device}"
        return f"{engine}::{model_name}"

    def _estimate_memory_mb(self, model: Any) -> float:
        """
        Estimate memory usage of a model in MB.

        Args:
            model: Model object

        Returns:
            Estimated memory in MB
        """
        try:
            # Try to get memory from PyTorch models
            if hasattr(model, "parameters"):
                # Check if it's a PyTorch model
                try:
                    import torch

                    total_params: int = sum(p.numel() for p in model.parameters())
                    memory_bytes = total_params * 4
                    return float(memory_bytes) / (1024 * 1024)
                except ImportError:
                    ...

            # Try to get size attribute
            if hasattr(model, "__sizeof__"):
                size: int = model.__sizeof__()
                return float(size) / (1024 * 1024)

            # Default estimate
            return 100.0  # 100 MB default estimate
        except Exception as e:
            logger.debug(f"Failed to estimate memory for model: {e}")
            return 100.0  # Default estimate

    def _check_ttl(self, key: str) -> bool:
        """
        Check if cached model has expired.

        Args:
            key: Cache key

        Returns:
            True if valid (not expired), False if expired
        """
        if self.default_ttl is None:
            return True  # No TTL

        if key not in self._timestamps:
            return False

        age = time.time() - self._timestamps[key]
        return age < self.default_ttl

    def _get_gpu_memory_usage_mb(self) -> float:
        """
        Get current GPU memory usage in MB.

        Returns:
            GPU memory usage in MB, or 0.0 if unavailable
        """
        if not self.track_gpu_memory:
            return 0.0

        try:
            import torch

            if torch.cuda.is_available():
                return torch.cuda.memory_allocated(0) / (1024 * 1024)  # Convert to MB
        except ImportError:
            ...
        except Exception as e:
            logger.debug(f"Failed to get GPU memory usage: {e}")
        return 0.0

    def _evict_oldest(self) -> str | None:
        """
        Evict oldest model from cache.

        Returns:
            Evicted key or None if cache empty
        """
        if not self._cache:
            return None

        # Remove oldest (first) item
        oldest_key = next(iter(self._cache))
        evicted = self._cache.pop(oldest_key)

        # Update statistics
        if oldest_key in self._timestamps:
            del self._timestamps[oldest_key]

        # Update memory stats
        if "memory_mb" in evicted:
            self._stats["current_memory_mb"] -= evicted["memory_mb"]

        # Update GPU memory stats if tracked
        if "gpu_memory_mb" in evicted:
            self._stats["current_gpu_memory_mb"] -= evicted.get("gpu_memory_mb", 0.0)

        # Clear GPU cache if this was a GPU model
        if evicted.get("device") == "cuda":
            try:
                import torch

                if torch.cuda.is_available():
                    torch.cuda.empty_cache()
                    logger.debug(f"Cleared GPU cache after evicting: {oldest_key}")
            except ImportError:
                ...

        self._stats["evictions"] += 1
        logger.debug(f"Evicted model from cache: {oldest_key}")

        return oldest_key

    def _get_system_memory_usage(self) -> float | None:
        """
        Get current system memory usage percentage.

        Returns:
            Memory usage percentage (0.0-1.0) or None if unavailable
        """
        if not HAS_PSUTIL or self._process is None:
            return None

        try:
            memory_info = self._process.memory_info()
            system_memory = psutil.virtual_memory()
            # Calculate process memory as percentage of system memory
            process_memory_mb: float = memory_info.rss / (1024 * 1024)
            total_memory_mb: float = system_memory.total / (1024 * 1024)
            if total_memory_mb > 0:
                return process_memory_mb / total_memory_mb
            return None
        except Exception as e:
            logger.debug(f"Failed to get system memory usage: {e}")
            return None

    def _detect_memory_pressure(self) -> bool:
        """
        Detect if system is under memory pressure.

        Returns:
            True if memory pressure detected
        """
        if not self.auto_eviction_enabled:
            return False

        memory_usage = self._get_system_memory_usage()
        if memory_usage is None:
            return False

        return memory_usage >= self.memory_pressure_threshold

    def _adjust_memory_limits(self):
        """
        Dynamically adjust memory limits based on system memory pressure.
        """
        if not self.enable_dynamic_limits:
            return

        memory_usage = self._get_system_memory_usage()
        if memory_usage is None:
            return

        # Reduce limits if under memory pressure
        if memory_usage >= self.memory_pressure_threshold:
            cur_mem = self.max_memory_mb
            orig_mem = self._original_max_memory_mb
            if cur_mem is not None and orig_mem is not None:
                new_limit = orig_mem * 0.7
                if cur_mem != new_limit:
                    logger.info(
                        f"Reducing memory limit due to pressure: "
                        f"{cur_mem:.1f}MB -> "
                        f"{new_limit:.1f}MB"
                    )
                    self.max_memory_mb = new_limit
                    self._stats["dynamic_adjustments"] += 1

            if self.max_models > 1:
                new_max = max(1, int(self._original_max_models * 0.7))
                if self.max_models != new_max:
                    logger.info(
                        f"Reducing model limit due to pressure: " f"{self.max_models} -> {new_max}"
                    )
                    self.max_models = new_max
                    self._stats["dynamic_adjustments"] += 1

        # Restore limits if memory pressure is low
        elif memory_usage < self.memory_pressure_threshold * 0.7:
            cur_mem = self.max_memory_mb
            orig_mem = self._original_max_memory_mb
            if cur_mem is not None and orig_mem is not None and cur_mem < orig_mem:
                logger.info(f"Restoring memory limit: " f"{cur_mem:.1f}MB -> " f"{orig_mem:.1f}MB")
                self.max_memory_mb = orig_mem
                self._stats["dynamic_adjustments"] += 1

            if self.max_models < self._original_max_models:
                logger.info(
                    f"Restoring model limit: {self.max_models} -> " f"{self._original_max_models}"
                )
                self.max_models = self._original_max_models
                self._stats["dynamic_adjustments"] += 1

    def _evict_on_low_memory(self):
        """
        Proactive eviction when system memory is getting high (low_memory_threshold).
        """
        memory_usage = self._get_system_memory_usage()
        if memory_usage is None:
            return

        if memory_usage < self.low_memory_threshold:
            return

        # Evict oldest models until below low memory threshold
        target_usage = self.low_memory_threshold * 0.9  # Target 90% of threshold

        evicted_count = 0
        while memory_usage is not None and memory_usage > target_usage and self._cache:
            evicted_key = self._evict_oldest()
            if evicted_key:
                evicted_count += 1
                self._stats["proactive_evictions"] += 1
                memory_usage = self._get_system_memory_usage()
                if memory_usage is not None:
                    logger.debug(f"Proactive eviction: {evicted_key} (usage: {memory_usage:.2%})")
            else:
                break

        if evicted_count > 0 and memory_usage is not None:
            logger.info(
                f"Proactive eviction: removed {evicted_count} models "
                f"(memory usage: {memory_usage:.2%})"
            )

    def _evict_on_memory_pressure(self):
        """
        Aggressive eviction when memory pressure is detected (memory_pressure_threshold).
        """
        if not self._detect_memory_pressure():
            return

        # Evict until memory usage is below threshold
        memory_usage = self._get_system_memory_usage()
        if memory_usage is None:
            return

        # Target 80% of threshold
        target_usage = self.memory_pressure_threshold * 0.8

        evicted_count = 0
        while memory_usage is not None and memory_usage > target_usage and self._cache:
            evicted_key = self._evict_oldest()
            if evicted_key:
                evicted_count += 1
                self._stats["pressure_evictions"] += 1
                memory_usage = self._get_system_memory_usage()
                if memory_usage is not None:
                    logger.warning(
                        f"Memory pressure eviction: {evicted_key} " f"(usage: {memory_usage:.2%})"
                    )
            else:
                break

        if evicted_count > 0 and memory_usage is not None:
            logger.warning(
                f"Memory pressure eviction: removed {evicted_count} models "
                f"(memory usage: {memory_usage:.2%})"
            )

    def _check_memory_limit(self) -> bool:
        """
        Check if memory limit would be exceeded.

        Returns:
            True if memory limit would be exceeded
        """
        # Check for low memory first (proactive)
        if self.auto_eviction_enabled:
            self._evict_on_low_memory()

        # Check for memory pressure (aggressive)
        if self.auto_eviction_enabled:
            self._evict_on_memory_pressure()

        # Adjust limits dynamically
        if self.enable_dynamic_limits:
            self._adjust_memory_limits()

        if self.max_memory_mb is None:
            return False  # No memory limit

        return self._stats["current_memory_mb"] >= self.max_memory_mb

    def get(
        self,
        engine: str,
        model_name: str,
        device: str | None = None,
    ) -> Any | None:
        """
        Get cached model if available and not expired.

        Args:
            engine: Engine name
            model_name: Model name/identifier
            device: Device (cuda/cpu)

        Returns:
            Cached model or None if not found/expired
        """
        key = self._generate_key(engine, model_name, device)

        # Check if in cache
        if key not in self._cache:
            self._stats["misses"] += 1
            return None

        # Check TTL
        if not self._check_ttl(key):
            # Expired, remove
            logger.debug(f"Model expired: {key}")
            evicted = self._cache.pop(key)
            if key in self._timestamps:
                del self._timestamps[key]
            if "memory_mb" in evicted:
                self._stats["current_memory_mb"] -= evicted["memory_mb"]
            self._stats["misses"] += 1
            return None

        # Move to end (most recently used)
        self._cache.move_to_end(key)
        self._stats["hits"] += 1

        logger.debug(f"Cache hit: {key}")
        return self._cache[key]["model"]

    def set(
        self,
        engine: str,
        model_name: str,
        model: Any,
        device: str | None = None,
        memory_mb: float | None = None,
        gpu_memory_mb: float | None = None,
        ttl: float | None = None,
    ):
        """
        Cache a model.

        Args:
            engine: Engine name
            model_name: Model name/identifier
            model: Model object to cache
            device: Device (cuda/cpu)
            memory_mb: Memory usage in MB (auto-estimated if None)
            gpu_memory_mb: GPU memory usage in MB (auto-detected if None and device=cuda)
            ttl: Time-to-live in seconds (uses default if None)
        """
        key = self._generate_key(engine, model_name, device)

        # Estimate memory if not provided
        if memory_mb is None:
            memory_mb = self._estimate_memory_mb(model)

        # Estimate GPU memory if not provided and device is CUDA
        if gpu_memory_mb is None and device == "cuda" and self.track_gpu_memory:
            try:
                import torch

                if torch.cuda.is_available():
                    # Get GPU memory before and after (rough estimate)
                    gpu_memory_mb = self._get_gpu_memory_usage_mb()
                else:
                    # CUDA not available, use 0.0
                    gpu_memory_mb = 0.0
            except ImportError:
                gpu_memory_mb = 0.0
        elif gpu_memory_mb is None:
            gpu_memory_mb = 0.0

        # Check memory limit
        if self.max_memory_mb is not None:
            # Evict until we have enough space
            current_mem = self._stats["current_memory_mb"]
            while current_mem + memory_mb > self.max_memory_mb and self._cache:
                self._evict_oldest()
                current_mem = self._stats["current_memory_mb"]

        # Remove if already exists
        if key in self._cache:
            old_memory = self._cache[key].get("memory_mb", 0)
            old_gpu_memory = self._cache[key].get("gpu_memory_mb", 0)
            self._stats["current_memory_mb"] -= old_memory
            self._stats["current_gpu_memory_mb"] -= old_gpu_memory
            self._cache.move_to_end(key)
        else:
            # Check model count limit
            while len(self._cache) >= self.max_models:
                self._evict_oldest()

        # Add to cache
        self._cache[key] = {
            "model": model,
            "engine": engine,
            "model_name": model_name,
            "device": device,
            "memory_mb": memory_mb,
            "gpu_memory_mb": gpu_memory_mb,
            "cached_at": time.time(),
        }

        # Update memory stats
        self._stats["current_memory_mb"] += memory_mb
        self._stats["current_gpu_memory_mb"] += gpu_memory_mb
        self._stats["total_loaded"] += 1

        # Set timestamp for TTL
        ttl_to_use = ttl if ttl is not None else self.default_ttl
        if ttl_to_use is not None:
            self._timestamps[key] = time.time()

        logger.debug(
            f"Cached model: {key} "
            f"(memory: {memory_mb:.2f}MB, GPU: {gpu_memory_mb:.2f}MB, "
            f"total: {self._stats['current_memory_mb']:.2f}MB, "
            f"GPU total: {self._stats['current_gpu_memory_mb']:.2f}MB)"
        )

    def remove(
        self,
        engine: str,
        model_name: str,
        device: str | None = None,
    ) -> bool:
        """
        Remove model from cache.

        Args:
            engine: Engine name
            model_name: Model name/identifier
            device: Device (cuda/cpu)

        Returns:
            True if removed, False if not found
        """
        key = self._generate_key(engine, model_name, device)

        if key not in self._cache:
            return False

        evicted = self._cache.pop(key)
        if key in self._timestamps:
            del self._timestamps[key]

        if "memory_mb" in evicted:
            self._stats["current_memory_mb"] -= evicted["memory_mb"]

        logger.debug(f"Removed model from cache: {key}")
        return True

    def clear(self):
        """Clear all cached models."""
        count = len(self._cache)
        self._cache.clear()
        self._timestamps.clear()
        self._stats["current_memory_mb"] = 0.0
        logger.info(f"Cleared {count} models from cache")

    def get_stats(self) -> dict[str, Any]:
        """
        Get cache statistics.

        Returns:
            Dictionary with cache statistics
        """
        total_requests = self._stats["hits"] + self._stats["misses"]
        hit_rate = self._stats["hits"] / total_requests if total_requests > 0 else 0.0

        memory_usage = self._get_system_memory_usage()

        stats: dict[str, Any] = {
            "cache_size": len(self._cache),
            "max_models": self.max_models,
            "original_max_models": self._original_max_models,
            "current_memory_mb": self._stats["current_memory_mb"],
            "current_gpu_memory_mb": self._stats["current_gpu_memory_mb"],
            "max_memory_mb": self.max_memory_mb,
            "original_max_memory_mb": self._original_max_memory_mb,
            "hits": self._stats["hits"],
            "misses": self._stats["misses"],
            "hit_rate": hit_rate,
            "evictions": self._stats["evictions"],
            "pressure_evictions": self._stats["pressure_evictions"],
            "proactive_evictions": self._stats["proactive_evictions"],
            "total_loaded": self._stats["total_loaded"],
            "dynamic_adjustments": self._stats["dynamic_adjustments"],
            "dynamic_limits_enabled": self.enable_dynamic_limits,
            "auto_eviction_enabled": self.auto_eviction_enabled,
            "memory_pressure_threshold": self.memory_pressure_threshold,
            "low_memory_threshold": self.low_memory_threshold,
        }

        if memory_usage is not None:
            usage_str = f"{memory_usage:.2%}"
            stats["system_memory_usage"] = usage_str
            stats["memory_pressure"] = memory_usage >= self.memory_pressure_threshold
            stats["low_memory"] = memory_usage >= self.low_memory_threshold

        return stats

    def list_cached_models(self) -> list[dict[str, Any]]:
        """
        List all cached models with metadata.

        Returns:
            List of dictionaries with model metadata
        """
        return [
            {
                "key": key,
                "engine": info["engine"],
                "model_name": info["model_name"],
                "device": info.get("device"),
                "memory_mb": info.get("memory_mb", 0),
                "cached_at": info.get("cached_at", 0),
                "age_seconds": time.time() - info.get("cached_at", 0),
            }
            for key, info in self._cache.items()
        ]

    def warm_cache(
        self,
        models_to_warm: list[tuple[str, str, str | None, Callable[[], Any]]],
    ):
        """
        Warm cache by pre-loading models.

        Args:
            models_to_warm: List of tuples (engine, model_name, device,
                load_func) where load_func is a callable that loads the model
        """
        logger.info(f"Warming cache with {len(models_to_warm)} models")

        for engine, model_name, device, load_func in models_to_warm:
            try:
                # Check if already cached
                if self.get(engine, model_name, device) is not None:
                    logger.debug(f"Model already cached: " f"{engine}::{model_name}::{device}")
                    continue

                # Load model
                logger.debug(f"Loading model for cache warming: " f"{engine}::{model_name}")
                model = load_func()

                # Cache it
                self.set(engine, model_name, model, device=device)

            except Exception as e:
                logger.warning(f"Failed to warm cache for " f"{engine}::{model_name}: {e}")

        logger.info("Cache warming complete")


# Global model cache instance
_global_model_cache: ModelCache | None = None


def get_model_cache(
    max_models: int = 10,
    max_memory_mb: float | None = None,
    default_ttl: float | None = None,
) -> ModelCache:
    """
    Get or create global model cache instance.

    Args:
        max_models: Maximum number of models (only used on first call)
        max_memory_mb: Maximum memory in MB (only used on first call)
        default_ttl: Default TTL in seconds (only used on first call)

    Returns:
        Global ModelCache instance
    """
    global _global_model_cache

    if _global_model_cache is None:
        _global_model_cache = ModelCache(
            max_models=max_models,
            max_memory_mb=max_memory_mb,
            default_ttl=default_ttl,
        )

    return _global_model_cache


def clear_global_cache():
    """Clear the global model cache."""
    global _global_model_cache
    if _global_model_cache is not None:
        _global_model_cache.clear()


# Export
__all__ = ["ModelCache", "clear_global_cache", "get_model_cache"]
