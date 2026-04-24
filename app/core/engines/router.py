"""
Engine Router - Runtime engine selection and management

Enhanced with:
- Language-based engine selection
- A/B testing framework
- Load-based selection
- Integration with UnifiedConfigService
"""

from __future__ import annotations

import importlib
import logging
import os
import random
import time
from dataclasses import dataclass, field
from typing import Any, cast

try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    psutil = None

from .manifest_loader import find_engine_manifests, get_engine_entry_point, load_engine_manifest
from .protocols import EngineProtocol

# Try importing engine performance metrics
try:
    from .performance_metrics import get_engine_metrics

    HAS_ENGINE_METRICS = True
except ImportError:
    HAS_ENGINE_METRICS = False
    get_engine_metrics = cast(Any, None)

# Try importing UnifiedConfigService
try:
    from backend.platform.config.unified_config import get_config

    HAS_UNIFIED_CONFIG = True
except ImportError:
    HAS_UNIFIED_CONFIG = False
    get_config = cast(Any, None)

# Try importing language detection
try:
    import langdetect

    HAS_LANGDETECT = True
except ImportError:
    HAS_LANGDETECT = False
    langdetect = None

# Try importing ABTestingService for exposure tracking
_ABTestingServiceClass: type[Any] | None = None
try:
    from backend.ml.models.ab_testing import ABTestingService

    _ABTestingServiceClass = ABTestingService
    HAS_AB_SERVICE = True
except ImportError:
    HAS_AB_SERVICE = False

logger = logging.getLogger(__name__)

# Legacy config / API tokens that do not match manifest engine_id (Slice 24).
_STT_ENGINE_ID_ALIASES: dict[str, str] = {
    "faster_whisper": "whisper",
}


def normalize_engine_request_id(engine_id: str) -> str:
    """Map deprecated or alternate engine ids to the manifest router id."""
    key = (engine_id or "").strip()
    return _STT_ENGINE_ID_ALIASES.get(key, key)


# ==============================================================================
# A/B Testing Data Classes
# ==============================================================================


@dataclass
class ABTestResult:
    """Result of an A/B test selection."""

    experiment_id: str
    selected_engine: str
    group: str  # "control" or "treatment"
    timestamp: float = field(default_factory=time.time)


@dataclass
class EngineLoadStats:
    """Load statistics for an engine."""

    engine_id: str
    active_requests: int = 0
    total_requests: int = 0
    avg_latency_ms: float = 0.0
    error_rate: float = 0.0
    last_updated: float = field(default_factory=time.time)


class EngineRouter:
    """
    Manages multiple engine instances and runtime selection.

    Allows registering multiple engine types and getting instances on demand.
    Supports audio, image, video, and other engine types.
    """

    def __init__(
        self,
        idle_timeout_seconds: float = 300.0,
        memory_threshold_mb: float = 8192.0,  # 8GB default
        auto_cleanup_enabled: bool = True,
        memory_pressure_threshold: float = 0.85,  # 85% system memory
        low_memory_threshold: float = 0.70,  # 70% system memory
    ):
        """
        Initialize engine router.

        Args:
            idle_timeout_seconds: Time in seconds before unloading idle engines
                                 (default: 300 = 5 minutes)
            memory_threshold_mb: Memory threshold in MB for automatic cleanup
                                (default: 8192 = 8GB)
            auto_cleanup_enabled: Enable automatic cleanup based on memory
            memory_pressure_threshold: System memory usage threshold (0.0-1.0)
                                      for aggressive cleanup (default: 0.85)
            low_memory_threshold: System memory usage threshold (0.0-1.0)
                                 for proactive cleanup (default: 0.70)
        """
        self._engines: dict[str, EngineProtocol] = {}
        self._engine_types: dict[str, type[EngineProtocol]] = {}
        self._manifests: dict[str, dict[str, Any]] = {}
        # engine_name -> timestamp
        self._engine_last_access: dict[str, float] = {}
        # engine_name -> MB (RSS memory)
        self._engine_memory_usage: dict[str, float] = {}
        # engine_name -> MB (GPU memory if applicable)
        self._engine_gpu_memory_usage: dict[str, float] = {}
        self._idle_timeout_seconds = idle_timeout_seconds
        self._memory_threshold_mb = memory_threshold_mb
        self._auto_cleanup_enabled = auto_cleanup_enabled
        self._memory_pressure_threshold = memory_pressure_threshold
        self._low_memory_threshold = low_memory_threshold
        # engine_id -> error_message (for engines that failed to load)
        self._failed_engines: dict[str, str] = {}
        self._process = None
        if HAS_PSUTIL:
            try:
                self._process = psutil.Process(os.getpid())
            except Exception as e:
                logger.warning(f"Failed to get process: {e}")

    def register_engine(self, name: str, engine_class: type[EngineProtocol]) -> None:
        """
        Register an engine class.

        Args:
            name: Engine name (e.g., "xtts", "whisper", "rvc")
            engine_class: Engine class (must inherit EngineProtocol)
        """
        if not isinstance(engine_class, type):
            raise TypeError(
                f"Engine class for '{name}' must be a class, got {type(engine_class).__name__}"
            )
        if not issubclass(engine_class, EngineProtocol):
            raise TypeError(
                f"Engine class '{engine_class.__name__}' for '{name}' must inherit from EngineProtocol"
            )
        self._engine_types[name] = engine_class

    def get_engine(self, name: str, **kwargs: Any) -> EngineProtocol | None:
        """
        Get or create an engine instance.

        Automatically unloads idle engines before creating new ones
        to manage memory.
        Updates last access time for the requested engine.

        Args:
            name: Engine name
            **kwargs: Additional arguments for engine initialization

        Returns:
            Engine instance or None if not found
        """
        name = normalize_engine_request_id(name)

        # Clean up idle engines and check memory before getting new one
        self._cleanup_idle_engines()

        # Check memory and cleanup if needed
        if self._auto_cleanup_enabled:
            self._cleanup_if_memory_high()

        # Update last access time if engine exists
        if name in self._engines:
            self._engine_last_access[name] = time.time()
            return self._engines[name]

        # Create new engine if type is registered
        if name in self._engine_types:
            try:
                # Track memory before engine creation
                memory_before = self._get_memory_usage_mb()

                merged_kwargs = dict(kwargs)
                # RHVoice / whisper_cpp: apply engine_config.json parameters so preflight and
                # runtime share one source of truth (Slice 16 RHVoice; Slice 27 whisper_cpp).
                if name in ("rhvoice", "whisper_cpp"):
                    try:
                        from backend.services.EngineConfigService import get_engine_config_service

                        ecs = get_engine_config_service()
                        manifest = self._manifests.get(name)
                        cfg_kwargs = ecs.get_engine_init_kwargs(name, manifest)
                        merged_kwargs = {**cfg_kwargs, **kwargs}
                    except Exception as e:
                        logger.debug(
                            "Engine %s: init kwargs not merged from engine_config.json: %s",
                            name,
                            e,
                        )

                engine = self._engine_types[name](**merged_kwargs)
                engine.initialize()

                # Track memory after engine creation
                memory_after = self._get_memory_usage_mb()
                memory_delta = memory_after - memory_before

                # Track GPU memory if available
                gpu_memory = self._get_gpu_memory_usage_mb()

                self._engines[name] = engine
                self._engine_last_access[name] = time.time()
                self._engine_memory_usage[name] = max(memory_delta, 0.0)
                self._engine_gpu_memory_usage[name] = gpu_memory

                logger.info(
                    f"Engine '{name}' loaded and initialized "
                    f"(memory: +{memory_delta:.1f}MB, GPU: {gpu_memory:.1f}MB)"
                )

                # Record engine initialization in metrics if available
                if HAS_ENGINE_METRICS:
                    try:
                        metrics = get_engine_metrics()
                        # Record initialization time (approximate)
                        init_time = time.time() - (self._engine_last_access[name] - 0.1)
                        metrics.record_synthesis_time(name, init_time, cached=False)
                    except Exception as e:
                        logger.debug(f"Failed to record engine metrics: {e}")

                return engine
            except Exception as e:
                logger.error(f"Failed to initialize engine '{name}': {e}")
                return None
        else:
            logger.warning(f"Engine '{name}' not registered")
            return None

    def list_engines(self) -> list[str]:
        """
        List available engine names.

        Returns:
            List of registered engine names
        """
        return list(self._engine_types.keys())

    def get_failed_engines(self) -> dict[str, str]:
        """
        Get engines that failed to load during startup.

        Returns:
            Dict mapping engine_id to error message
        """
        return dict(self._failed_engines)

    def unregister_engine(self, name: str) -> None:
        """
        Unregister an engine and cleanup instance.

        Args:
            name: Engine name to unregister
        """
        if name in self._engines:
            try:
                # Track memory before cleanup
                memory_before = self._get_memory_usage_mb()
                gpu_memory_before = self._get_gpu_memory_usage_mb()

                # Enhanced cleanup: clear GPU cache if engine uses GPU
                engine = self._engines[name]
                if hasattr(engine, "device") and engine.device == "cuda":
                    try:
                        import torch

                        if torch.cuda.is_available():
                            torch.cuda.empty_cache()
                            logger.debug(f"Cleared GPU cache for engine '{name}'")
                    except ImportError:
                        ...
                    except Exception as e:
                        logger.debug(f"Failed to clear GPU cache: {e}")

                engine.cleanup()

                # Track memory after cleanup
                memory_after = self._get_memory_usage_mb()
                gpu_memory_after = self._get_gpu_memory_usage_mb()
                memory_freed = memory_before - memory_after
                gpu_memory_freed = gpu_memory_before - gpu_memory_after

                logger.info(
                    f"Engine '{name}' unloaded "
                    f"(memory freed: {memory_freed:.1f}MB, "
                    f"GPU: {gpu_memory_freed:.1f}MB)"
                )
            except Exception as e:
                logger.warning(f"Error cleaning up engine '{name}': {e}")

            del self._engines[name]
            if name in self._engine_last_access:
                del self._engine_last_access[name]
            if name in self._engine_memory_usage:
                del self._engine_memory_usage[name]
            if name in self._engine_gpu_memory_usage:
                del self._engine_gpu_memory_usage[name]
        # Do NOT remove ``name`` from ``_engine_types``: memory/idle unload must drop only the
        # runtime instance so ``list_engines()`` and synthesis validation still see the engine id
        # and ``get_engine`` can re-instantiate (Slice 12 eSpeak NG second-call regression).

    def cleanup_all(self) -> None:
        """Cleanup all engine instances."""
        memory_before = self._get_memory_usage_mb()

        for name, engine in self._engines.items():
            try:
                engine.cleanup()
            except Exception as e:
                logger.warning(f"Error cleaning up engine '{name}': {e}")

        memory_after = self._get_memory_usage_mb()
        memory_freed = memory_before - memory_after

        self._engines.clear()
        self._engine_last_access.clear()
        self._engine_memory_usage.clear()

        logger.info(f"All engines cleaned up (memory freed: {memory_freed:.1f}MB)")

    def _get_memory_usage_mb(self) -> float:
        """
        Get current memory usage in MB.

        Returns:
            Memory usage in MB, or 0.0 if psutil not available
        """
        if not HAS_PSUTIL or self._process is None:
            return 0.0

        try:
            memory_info = self._process.memory_info()
            return float(memory_info.rss) / (1024 * 1024)
        except Exception as e:
            logger.warning(f"Failed to get memory usage: {e}")
            return 0.0

    def _get_system_memory_usage(self) -> float | None:
        """
        Get current system memory usage percentage.

        Returns:
            Memory usage percentage (0.0-1.0) or None if unavailable
        """
        if not HAS_PSUTIL:
            return None

        try:
            system_memory = psutil.virtual_memory()
            return float(system_memory.percent) / 100.0
        except Exception as e:
            logger.debug(f"Failed to get system memory usage: {e}")
            return None

    def _get_gpu_memory_usage_mb(self) -> float:
        """
        Get current GPU memory usage in MB.

        Returns:
            GPU memory usage in MB, or 0.0 if unavailable
        """
        try:
            import torch

            if torch.cuda.is_available():
                return torch.cuda.memory_allocated(0) / (1024 * 1024)  # Convert to MB
        except ImportError:
            ...
        except Exception as e:
            logger.debug(f"Failed to get GPU memory usage: {e}")
        return 0.0

    def _cleanup_if_memory_high(self) -> None:
        """
        Cleanup engines if memory usage exceeds threshold.

        Enhanced with system memory pressure detection and GPU memory tracking.
        Unloads least recently used engines until memory is below threshold.
        """
        if not self._auto_cleanup_enabled:
            return

        # Check system memory pressure first
        system_memory_usage = self._get_system_memory_usage()
        if system_memory_usage is not None:
            # Aggressive cleanup if system memory pressure is high
            if system_memory_usage >= self._memory_pressure_threshold:
                logger.warning(
                    f"System memory pressure detected ({system_memory_usage:.1%} >= "
                    f"{self._memory_pressure_threshold:.1%}), "
                    f"performing aggressive cleanup"
                )
                self._aggressive_cleanup()
                return

            # Proactive cleanup if system memory is getting high
            elif system_memory_usage >= self._low_memory_threshold:
                logger.info(
                    f"System memory usage high ({system_memory_usage:.1%} >= "
                    f"{self._low_memory_threshold:.1%}), "
                    f"performing proactive cleanup"
                )
                self._proactive_cleanup()
                return

        # Check process memory threshold
        current_memory = self._get_memory_usage_mb()
        if current_memory < self._memory_threshold_mb:
            return

        # Sort engines by last access time (oldest first)
        sorted_engines = sorted(self._engine_last_access.items(), key=lambda x: x[1])

        # Unload engines until memory is below threshold
        for name, _ in sorted_engines:
            if self._get_memory_usage_mb() < self._memory_threshold_mb:
                break

            logger.info(
                f"Memory threshold exceeded ({current_memory:.1f}MB > "
                f"{self._memory_threshold_mb:.1f}MB), "
                f"unloading engine '{name}'"
            )
            self.unregister_engine(name)

    def _aggressive_cleanup(self) -> None:
        """
        Aggressive cleanup: unload all idle engines and some active ones.
        """
        # First, unload all idle engines
        self._cleanup_idle_engines()

        # If still under pressure, unload least recently used engines
        system_memory_usage = self._get_system_memory_usage()
        if system_memory_usage is None:
            return

        target_usage = self._memory_pressure_threshold * 0.8  # Target 80% of threshold

        while system_memory_usage > target_usage and self._engines:
            # Sort by last access (oldest first)
            sorted_engines = sorted(self._engine_last_access.items(), key=lambda x: x[1])

            if not sorted_engines:
                break

            name, _ = sorted_engines[0]
            logger.info(
                f"Aggressive cleanup: unloading engine '{name}' "
                f"(system memory: {system_memory_usage:.1%})"
            )
            self.unregister_engine(name)

            # Recheck system memory
            system_memory_usage = self._get_system_memory_usage()
            if system_memory_usage is None:
                break

    def _proactive_cleanup(self) -> None:
        """
        Proactive cleanup: unload idle engines to prevent memory pressure.
        """
        # Unload all idle engines
        self._cleanup_idle_engines()

        # If still above threshold, unload oldest engine
        system_memory_usage = self._get_system_memory_usage()
        if system_memory_usage is None:
            return

        if system_memory_usage >= self._low_memory_threshold and self._engines:
            # Sort by last access (oldest first)
            sorted_engines = sorted(self._engine_last_access.items(), key=lambda x: x[1])

            if sorted_engines:
                name, _ = sorted_engines[0]
                logger.info(
                    f"Proactive cleanup: unloading oldest engine '{name}' "
                    f"(system memory: {system_memory_usage:.1%})"
                )
                self.unregister_engine(name)

    def _cleanup_idle_engines(self) -> None:
        """
        Unload engines that have been idle for longer than the timeout.

        This helps manage memory by automatically freeing resources from
        engines that haven't been used recently.
        """
        if not self._engine_last_access:
            return

        current_time = time.time()
        idle_engines = []

        for name, last_access in self._engine_last_access.items():
            idle_time = current_time - last_access
            if idle_time > self._idle_timeout_seconds:
                idle_engines.append(name)

        for name in idle_engines:
            logger.info(
                f"Unloading idle engine '{name}' "
                f"(idle for {idle_time:.1f}s, "
                f"timeout: {self._idle_timeout_seconds}s)"
            )
            self.unregister_engine(name)

    def unload_engine(self, name: str) -> bool:
        """
        Manually unload an engine instance to free memory.

        Args:
            name: Engine name to unload

        Returns:
            True if engine was unloaded, False if not found
        """
        if name in self._engines:
            self.unregister_engine(name)
            return True
        return False

    def get_engine_performance_stats(self) -> dict[str, Any]:
        """
        Get engine performance statistics from metrics collector.

        Returns:
            Dictionary with engine performance statistics
        """
        if not HAS_ENGINE_METRICS:
            return {"error": "Engine performance metrics not available"}

        try:
            metrics = get_engine_metrics()
            return metrics.get_summary()
        except Exception as e:
            logger.warning(f"Failed to get engine performance stats: {e}")
            return {"error": str(e)}

    def get_engine_stats(self) -> dict[str, Any]:
        """
        Get statistics about loaded engines.

        Returns:
            Dictionary with engine statistics
        """
        current_time = time.time()
        current_memory = self._get_memory_usage_mb()

        system_memory_usage = self._get_system_memory_usage()
        gpu_memory = self._get_gpu_memory_usage_mb()

        stats: dict[str, Any] = {
            "total_loaded": len(self._engines),
            "total_registered": len(self._engine_types),
            "idle_timeout_seconds": self._idle_timeout_seconds,
            "memory_threshold_mb": self._memory_threshold_mb,
            "auto_cleanup_enabled": self._auto_cleanup_enabled,
            "current_memory_mb": current_memory,
            "gpu_memory_mb": gpu_memory,
            "memory_usage_percent": (
                (current_memory / self._memory_threshold_mb * 100)
                if self._memory_threshold_mb > 0
                else 0.0
            ),
            "system_memory_usage": (
                system_memory_usage if system_memory_usage is not None else None
            ),
            "memory_pressure": (
                system_memory_usage >= self._memory_pressure_threshold
                if system_memory_usage is not None
                else False
            ),
            "low_memory": (
                system_memory_usage >= self._low_memory_threshold
                if system_memory_usage is not None
                else False
            ),
            "engines": {},
        }

        for name, engine in self._engines.items():
            last_access = self._engine_last_access.get(name, 0)
            idle_time = current_time - last_access
            memory_usage = self._engine_memory_usage.get(name, 0.0)
            gpu_memory_usage = self._engine_gpu_memory_usage.get(name, 0.0)

            stats["engines"][name] = {
                "initialized": (
                    engine.is_initialized() if hasattr(engine, "is_initialized") else True
                ),
                "idle_seconds": idle_time,
                "is_idle": idle_time > self._idle_timeout_seconds,
                "memory_usage_mb": memory_usage,
                "gpu_memory_usage_mb": gpu_memory_usage,
                "device": getattr(engine, "device", "unknown"),
            }

        return stats

    def load_engine_from_manifest(self, manifest_path: str) -> None:
        """
        Load and register an engine from a manifest file.

        Args:
            manifest_path: Path to engine.manifest.json file
        """
        manifest = load_engine_manifest(manifest_path)
        engine_id = manifest["engine_id"]

        # Get entry point class
        entry_point = get_engine_entry_point(manifest)
        if not entry_point:
            raise ValueError(f"Manifest missing entry_point: {manifest_path}")

        # Import and get class
        # Handle both formats: "module:class" (colon) and "module.class" (dot)
        if ":" in entry_point:
            module_path, class_name = entry_point.split(":", 1)
        else:
            module_path, class_name = entry_point.rsplit(".", 1)
        try:
            module = importlib.import_module(module_path)
        except ImportError as e:
            raise ImportError(
                f"Failed to import module '{module_path}' for engine '{engine_id}': {e}"
            ) from e

        try:
            engine_class = getattr(module, class_name)
        except AttributeError:
            raise AttributeError(
                f"Module '{module_path}' has no attribute '{class_name}' for engine '{engine_id}'. "
                f"Available attributes: {[x for x in dir(module) if not x.startswith('_')]}"
            )

        # Verify it's a class
        if not isinstance(engine_class, type):
            raise TypeError(
                f"Entry point '{entry_point}' for engine '{engine_id}' is not a class. "
                f"Got type: {type(engine_class).__name__}"
            )

        # Register engine
        self.register_engine(engine_id, engine_class)
        self._manifests[engine_id] = manifest

    def load_all_engines(self, engines_root: str = "engines") -> None:
        """
        Load all engines from manifest files in engines directory.

        Args:
            engines_root: Root directory containing engine manifests
        """
        manifests = find_engine_manifests(engines_root)

        for engine_id, manifest_path in manifests.items():
            # Skip if already registered (idempotent - safe to call multiple times)
            if engine_id in self._engine_types:
                continue

            try:
                self.load_engine_from_manifest(manifest_path)
            except Exception as e:
                # Track failed engine with error message
                self._failed_engines[engine_id] = str(e)
                logger.error(f"Failed to load engine {engine_id}: {e}")

    def get_manifest(self, engine_id: str) -> dict[str, Any] | None:
        """
        Get manifest for an engine.

        Args:
            engine_id: Engine identifier

        Returns:
            Manifest dictionary or None
        """
        return self._manifests.get(engine_id)

    def get_engine_for_task_type(
        self, task_type: str, prefer_default: bool = True
    ) -> EngineProtocol | None:
        """
        Get engine for a task type (e.g., "tts", "image_gen").

        Args:
            task_type: Task type
            prefer_default: If True, prefer default engine from config

        Returns:
            Engine instance or None
        """
        # Try to get default engine if prefer_default
        if prefer_default:
            try:
                from app.core.engines.config import get_engine_config

                config = get_engine_config()
                default_id = config.get_default_engine(task_type)
                if default_id:
                    return self.get_engine(default_id)
            except Exception as e:
                import logging

                logger = logging.getLogger(__name__)
                logger.debug(f"Failed to get default engine: {e}")

        # Fallback: return first available engine (could be improved)
        available = self.list_engines()
        if available:
            return self.get_engine(available[0])

        return None

    def select_engine_by_quality(
        self,
        task_type: str = "tts",
        min_mos_score: float | None = None,
        min_similarity: float | None = None,
        min_naturalness: float | None = None,
        prefer_speed: bool = False,
        quality_tier: str | None = None,
    ) -> EngineProtocol | None:
        """
        Select the best engine based on quality requirements.

        Uses engine manifests to determine quality capabilities and selects
        the engine that best matches the requirements.

        Args:
            task_type: Task type (e.g., "tts")
            min_mos_score: Minimum MOS score required (1.0-5.0)
            min_similarity: Minimum similarity required (0.0-1.0)
            min_naturalness: Minimum naturalness required (0.0-1.0)
            prefer_speed: If True, prefer faster engines over highest quality
            quality_tier: Quality tier preference ("fast", "standard", "high", "ultra")

        Returns:
            Engine instance that best matches requirements,
            or None if no match
        """
        available_engines: list[dict[str, Any]] = []

        # Score each available engine
        for engine_id in self.list_engines():
            manifest = self.get_manifest(engine_id)
            if not manifest:
                continue

            # Check task type match
            manifest_type = manifest.get("type")
            manifest_subtype = manifest.get("subtype")

            # Handle different engine types
            if task_type in ("tts", "stt", "rvc"):
                # Audio engines require type="audio" and matching subtype
                if manifest_type != "audio" or manifest_subtype != task_type:
                    continue
            elif task_type == "llm":
                # LLM engines require type="llm"
                if manifest_type != "llm":
                    continue
            elif task_type == "s2s":
                # S2S engines require type="llm" with subtype="s2s"
                if manifest_type != "llm" or manifest_subtype != "s2s":
                    continue
            else:
                # Default: require exact type and subtype match
                if manifest_type != task_type and manifest_subtype != task_type:
                    continue

            # Get quality features from manifest
            quality_features = manifest.get("quality_features", {})

            # Parse MOS estimate (e.g., "4.5-5.0" -> 4.5)
            mos_estimate = None
            if "mos_estimate" in quality_features:
                mos_str = quality_features["mos_estimate"]
                try:
                    # Parse range like "4.5-5.0" or single value
                    if "-" in mos_str:
                        mos_estimate = float(mos_str.split("-")[0])
                    else:
                        mos_estimate = float(mos_str)
                except (ValueError, AttributeError):
                    ...

            # Map similarity/naturalness text to numeric estimates
            similarity_estimate = None
            naturalness_estimate = None

            sim_text = quality_features.get("similarity_score", "").lower()
            if "very_high" in sim_text or "ultra" in sim_text:
                similarity_estimate = 0.90
            elif "high" in sim_text:
                similarity_estimate = 0.85
            elif "medium" in sim_text:
                similarity_estimate = 0.75

            nat_text = quality_features.get("naturalness", "").lower()
            if "ultra_high" in nat_text or "ultra" in nat_text:
                naturalness_estimate = 0.95
            elif "very_high" in nat_text:
                naturalness_estimate = 0.90
            elif "high" in nat_text:
                naturalness_estimate = 0.85

            # Check if engine meets minimum requirements
            meets_requirements = True

            if min_mos_score and (mos_estimate is None or mos_estimate < min_mos_score):
                meets_requirements = False

            if min_similarity and (
                similarity_estimate is None or similarity_estimate < min_similarity
            ):
                meets_requirements = False

            if min_naturalness and (
                naturalness_estimate is None or naturalness_estimate < min_naturalness
            ):
                meets_requirements = False

            if not meets_requirements:
                continue

            # Calculate quality score (higher is better)
            quality_score = 0.0
            if mos_estimate:
                quality_score += mos_estimate * 0.4  # 40% weight
            if similarity_estimate:
                quality_score += similarity_estimate * 0.3  # 30% weight
            if naturalness_estimate:
                quality_score += naturalness_estimate * 0.3  # 30% weight

            # Speed factor (XTTS is fastest, Tortoise is slowest)
            speed_factor = 1.0
            if engine_id == "xtts_v2" or engine_id == "xtts":
                speed_factor = 1.0  # Fastest
            elif engine_id == "chatterbox":
                speed_factor = 0.8  # Medium
            elif engine_id == "tortoise":
                speed_factor = 0.5  # Slowest

            # Quality tier matching
            tier_match = 1.0
            if quality_tier:
                if (
                    (quality_tier == "fast" and engine_id in ["xtts_v2", "xtts"])
                    or (quality_tier == "standard" and engine_id == "chatterbox")
                    or (quality_tier in ["high", "ultra"] and engine_id == "tortoise")
                ):
                    tier_match = 1.2

            # Final score
            if prefer_speed:
                final_score = quality_score * 0.5 + speed_factor * 0.5
            else:
                final_score = quality_score * tier_match

            available_engines.append(
                {
                    "engine_id": engine_id,
                    "quality_score": quality_score,
                    "final_score": final_score,
                    "speed_factor": speed_factor,
                    "mos_estimate": mos_estimate,
                    "similarity_estimate": similarity_estimate,
                    "naturalness_estimate": naturalness_estimate,
                }
            )

        if not available_engines:
            return None

        # Sort by final score (descending)
        available_engines.sort(key=lambda x: float(x.get("final_score", 0)), reverse=True)

        # Return best matching engine
        best_engine_id = str(available_engines[0]["engine_id"])
        return self.get_engine(best_engine_id)

    # ==========================================================================
    # Language Detection and Selection
    # ==========================================================================

    def detect_language(self, text: str) -> str | None:
        """
        Detect language of input text.

        Args:
            text: Input text to analyze

        Returns:
            ISO language code (e.g., "en", "zh", "ja") or None if detection fails
        """
        if not HAS_LANGDETECT:
            logger.debug("langdetect not available, language detection skipped")
            return None

        if not text or len(text.strip()) < 3:
            return None

        try:
            detected = str(langdetect.detect(text))
            logger.debug(f"Detected language: {detected}")
            return detected
        except Exception as e:
            logger.debug(f"Language detection failed: {e}")
            return None

    def get_engine_for_language(
        self, language: str, task_type: str = "tts", fallback_to_default: bool = True
    ) -> str | None:
        """
        Get the recommended engine ID for a specific language.

        Uses config/engines.config.yaml language mappings.

        Args:
            language: ISO language code (e.g., "en", "zh", "ja")
            task_type: Task type (e.g., "tts", "stt")
            fallback_to_default: If True, return default engine when no mapping exists

        Returns:
            Engine ID or None
        """
        if HAS_UNIFIED_CONFIG:
            try:
                config = get_config()
                engine_id = config.get_engine_for_language(language, task_type)
                if engine_id:
                    return engine_id
            except Exception as e:
                logger.debug(f"Failed to get engine from config: {e}")

        # Fallback to default engine
        if fallback_to_default:
            return self._get_default_engine_id(task_type)

        return None

    def select_engine(
        self,
        task_type: str = "tts",
        text: str | None = None,
        language: str | None = None,
        quality_mode: str = "balanced",
        ab_test_group: str | None = None,
        prefer_low_load: bool = False,
    ) -> EngineProtocol | None:
        """
        Enhanced engine selection with language, quality, A/B testing, and load balancing.

        This is the main entry point for intelligent engine selection.

        Args:
            task_type: Type of task ("tts", "stt", "rvc", etc.)
            text: Optional input text (used for language detection)
            language: Optional explicit language code (overrides detection)
            quality_mode: Quality preference ("fast", "balanced", "high", "ultra")
            ab_test_group: Optional A/B test group to use
            prefer_low_load: If True, prefer engines with lower load

        Returns:
            Engine instance or None
        """
        selected_engine_id = None
        selection_reason = []

        # 1. Check A/B testing first
        if ab_test_group or self._is_ab_testing_enabled():
            ab_result = self._select_ab_test_engine(task_type, ab_test_group)
            if ab_result:
                selected_engine_id = ab_result.selected_engine
                selection_reason.append(f"A/B test: {ab_result.experiment_id}")

        # 2. Language-based selection (if no A/B result)
        if not selected_engine_id:
            # Detect language if not provided
            detected_language = language
            if not detected_language and text:
                detected_language = self.detect_language(text)

            if detected_language:
                lang_engine = self.get_engine_for_language(
                    detected_language, task_type, fallback_to_default=False
                )
                if lang_engine:
                    selected_engine_id = lang_engine
                    selection_reason.append(f"language: {detected_language}")

        # 3. Quality-based selection (if still no selection)
        if not selected_engine_id:
            quality_tier = self._map_quality_mode_to_tier(quality_mode)
            if quality_tier:
                selected_engine_id = self._get_engine_for_quality_tier(task_type, quality_tier)
                if selected_engine_id:
                    selection_reason.append(f"quality: {quality_mode}")

        # 4. Load-based adjustment (if enabled)
        if prefer_low_load and selected_engine_id:
            alternative = self._get_lower_load_alternative(selected_engine_id, task_type)
            if alternative:
                selected_engine_id = alternative
                selection_reason.append("load-balanced")

        # 5. Fallback to default
        if not selected_engine_id:
            selected_engine_id = self._get_default_engine_id(task_type)
            selection_reason.append("default")

        if selected_engine_id:
            logger.info(f"Engine selected: {selected_engine_id} ({', '.join(selection_reason)})")
            return self.get_engine(selected_engine_id)

        logger.warning(f"No engine available for task type: {task_type}")
        return None

    # ==========================================================================
    # A/B Testing
    # ==========================================================================

    def _is_ab_testing_enabled(self) -> bool:
        """Check if A/B testing is enabled in config."""
        if HAS_UNIFIED_CONFIG:
            try:
                config = get_config()
                return config.is_feature_enabled("ab_testing") and config.engines.ab_testing.enabled
            except Exception as e:
                logger.debug("A/B testing config check failed, defaulting to disabled: %s", e)
        return False

    def _select_ab_test_engine(
        self, task_type: str, forced_group: str | None = None
    ) -> ABTestResult | None:
        """
        Select an engine based on active A/B test experiments.

        Args:
            task_type: Task type to find experiments for
            forced_group: Optional forced group selection

        Returns:
            ABTestResult or None if no active experiment
        """
        if not HAS_UNIFIED_CONFIG:
            return None

        try:
            config = get_config()
            experiments = config.get_active_ab_experiments()

            for exp in experiments:
                # Check if experiment applies to this task type
                # (could be enhanced with experiment-specific task filtering)
                if not exp.engines:
                    continue

                # Select engine based on weights
                if forced_group and forced_group in exp.engines:
                    selected = forced_group
                    group = "forced"
                else:
                    selected = self._weighted_random_choice(exp.engines, exp.weights)
                    group = "treatment" if selected != exp.engines[0] else "control"

                result = ABTestResult(
                    experiment_id=exp.id,
                    selected_engine=selected,
                    group=group,
                )

                # Log A/B test selection
                logger.debug(f"A/B test '{exp.id}': selected '{selected}' ({group})")

                # Track exposure in ABTestingService for analytics
                if HAS_AB_SERVICE and _ABTestingServiceClass is not None:
                    try:
                        ab_service = _ABTestingServiceClass()
                        user_id = getattr(self, "_current_user_id", "anonymous")
                        ab_service.track_exposure(
                            user_id=user_id,
                            experiment_id=exp.id,
                            metadata={
                                "selected_engine": selected,
                                "group": group,
                                "task_type": task_type,
                            },
                        )
                    except Exception as track_err:
                        # Non-critical: don't fail engine selection if tracking fails
                        logger.debug(f"Failed to track A/B exposure: {track_err}")

                return result

        except Exception as e:
            logger.debug(f"A/B test selection failed: {e}")

        return None

    def _weighted_random_choice(self, items: list[str], weights: list[float]) -> str:
        """Select an item based on weights."""
        if not items:
            raise ValueError("No items to choose from")

        if not weights or len(weights) != len(items):
            # Equal weights if not specified
            return random.choice(items)

        total = sum(weights)
        if total <= 0:
            return random.choice(items)

        # Normalize weights
        normalized = [w / total for w in weights]

        r = random.random()
        cumulative = 0.0
        for item, weight in zip(items, normalized):
            cumulative += weight
            if r <= cumulative:
                return item

        return items[-1]

    # ==========================================================================
    # Load-Based Selection
    # ==========================================================================

    def _get_engine_load_stats(self, engine_id: str) -> EngineLoadStats | None:
        """
        Get load statistics for an engine.

        Returns:
            EngineLoadStats or None if unavailable
        """
        if not HAS_ENGINE_METRICS:
            return None

        try:
            metrics = get_engine_metrics()
            summary = metrics.get_summary()

            # Engine stats are nested under "engines" key in summary
            engine_stats = summary.get("engines", {}).get(engine_id, {})
            if engine_stats:
                total_requests = engine_stats.get("total_requests", 0)
                errors = engine_stats.get("errors", 0)

                # Calculate error_rate as float (it's stored as string like "0.00%")
                error_rate = errors / max(total_requests, 1)

                # Get avg latency from synthesis_times.mean (in seconds, convert to ms)
                synthesis_times = engine_stats.get("synthesis_times")
                avg_latency_ms = 0.0
                if synthesis_times and isinstance(synthesis_times, dict):
                    avg_latency_ms = synthesis_times.get("mean", 0.0) * 1000

                return EngineLoadStats(
                    engine_id=engine_id,
                    active_requests=0,  # Not tracked - could use queue depth in future
                    total_requests=total_requests,
                    avg_latency_ms=avg_latency_ms,
                    error_rate=error_rate,
                )
        except Exception as e:
            logger.debug(f"Failed to get load stats for {engine_id}: {e}")

        return None

    def _get_lower_load_alternative(self, current_engine_id: str, task_type: str) -> str | None:
        """
        Find an alternative engine with lower load.

        Args:
            current_engine_id: Currently selected engine
            task_type: Task type for finding alternatives

        Returns:
            Alternative engine ID or None if current is best
        """
        current_stats = self._get_engine_load_stats(current_engine_id)
        if not current_stats:
            return None

        # STT: never load-balance across engines (no silent substitution; Slice 24).
        if task_type == "stt":
            return None

        # Get fallback chain as alternatives
        alternatives = self._get_fallback_chain(task_type)
        if current_engine_id in alternatives:
            alternatives = [e for e in alternatives if e != current_engine_id]

        best_alternative = None
        best_load_score = current_stats.active_requests + (current_stats.error_rate * 10)

        for alt_id in alternatives:
            alt_stats = self._get_engine_load_stats(alt_id)
            if not alt_stats:
                continue

            # Simple load score: active requests + error rate penalty
            alt_load_score = alt_stats.active_requests + (alt_stats.error_rate * 10)

            # Only switch if significantly lower load
            if alt_load_score < best_load_score * 0.7:  # 30% lower
                best_alternative = alt_id
                best_load_score = alt_load_score

        return best_alternative

    # ==========================================================================
    # Helper Methods
    # ==========================================================================

    def _get_default_engine_id(self, task_type: str) -> str | None:
        """Get default engine ID for a task type from config."""
        if HAS_UNIFIED_CONFIG:
            try:
                config = get_config()
                return config.get_default_engine(task_type)
            except Exception as e:
                logger.debug("Config lookup for default engine failed, using fallback: %s", e)

        # Fallback defaults
        defaults = {
            "tts": "xtts_v2",
            "stt": "whisper_cpp",
            "rvc": "rvc_v2",
        }
        return defaults.get(task_type)

    def _get_fallback_chain(self, task_type: str) -> list[str]:
        """Get fallback chain for a task type (GAP-053: user settings → YAML → defaults)."""
        # STT: exactly one engine — configured default only. YAML multi-engine lists are
        # ignored here so orchestration cannot silently substitute STT engines (Slice 24).
        if task_type == "stt":
            default_id = self._get_default_engine_id("stt") or "whisper_cpp"
            return [normalize_engine_request_id(default_id)]

        try:
            from backend.api.routes.settings import load_settings

            loaded = load_settings()
            if (
                loaded
                and loaded.engine
                and loaded.engine.engine_priority_order
                and task_type == "tts"
            ):
                return list(loaded.engine.engine_priority_order)
        except Exception as e:
            logger.debug("User engine priority unavailable in router: %s", e)

        chain: list[str] = []
        if HAS_UNIFIED_CONFIG:
            try:
                config = get_config()
                chain = list(config.get_fallback_chain(task_type) or [])
            except Exception as e:
                logger.debug("Config lookup for fallback chain failed, using fallback: %s", e)

        if chain:
            return chain

        defaults = {
            "tts": ["xtts_v2", "openvoice", "piper", "espeak"],
            "rvc": ["rvc_v2", "sovits_svc", "ddsp"],
        }
        return defaults.get(task_type, [])

    def _map_quality_mode_to_tier(self, quality_mode: str) -> str | None:
        """Map quality mode string to tier."""
        mapping = {
            "fast": "fast",
            "balanced": "standard",
            "standard": "standard",
            "high": "high",
            "ultra": "ultra",
            "realtime": "realtime",
        }
        return mapping.get(quality_mode.lower())

    def _get_engine_for_quality_tier(self, task_type: str, tier: str) -> str | None:
        """Get engine ID for a quality tier."""
        if HAS_UNIFIED_CONFIG:
            try:
                config = get_config()
                return config.get_quality_tier_engine(tier, task_type)
            except Exception as e:
                logger.debug("Config lookup for quality tier engine failed, using fallback: %s", e)

        # Fallback tier mapping for TTS
        if task_type == "tts":
            tier_engines = {
                "fast": "piper",
                "realtime": "piper",
                "standard": "xtts_v2",
                "high": "xtts_v2",
                "ultra": "tortoise",
            }
            return tier_engines.get(tier)

        return None

    def select_engine_with_fallback(
        self,
        task_type: str = "tts",
        *,
        explicit_engine_id: str | None = None,
        **kwargs: Any,
    ) -> tuple[EngineProtocol | None, list[str]]:
        """
        Select engine with automatic fallback chain (TTS/RVC only).

        For STT, the chain is always a single configured default unless
        ``explicit_engine_id`` is set — then only that id is attempted (fail-closed).

        Args:
            task_type: Task type
            explicit_engine_id: When set, only this manifest engine id is resolved
                (aliases normalized); no substitution on failure.
            **kwargs: Reserved for future selection hints

        Returns:
            Tuple of (engine instance, list of attempted engines)
        """
        if explicit_engine_id is not None:
            resolved = normalize_engine_request_id(explicit_engine_id)
            attempted: list[str] = [resolved]
            try:
                engine = self.get_engine(resolved)
                if engine and engine.is_initialized():
                    logger.info("Using explicitly requested engine '%s'", resolved)
                    return engine, attempted
            except Exception as e:
                logger.debug("Explicit engine '%s' failed: %s", resolved, e)
            logger.warning("Explicit engine '%s' unavailable or not initialized", resolved)
            return None, attempted

        fallback_chain = self._get_fallback_chain(task_type)
        attempted = []

        for engine_id in fallback_chain:
            attempted.append(engine_id)
            try:
                engine = self.get_engine(engine_id)
                if engine and engine.is_initialized():
                    logger.info(f"Using engine '{engine_id}' (fallback position: {len(attempted)})")
                    return engine, attempted
            except Exception as e:
                logger.debug(f"Fallback engine '{engine_id}' failed: {e}")
                continue

        logger.warning(f"All fallback engines failed for task type '{task_type}'")
        return None, attempted


# Global router instance for easy access
router = EngineRouter()
