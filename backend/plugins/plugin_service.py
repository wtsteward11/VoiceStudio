"""
Plugin Service — thin facade over decomposed service modules.

Phase 12.2: Plugin Architecture
Extensible plugin system for VoiceStudio.

The heavy lifting is delegated to:
- ``manifest_service.ManifestService``  — schema validation, discovery, version checks
- ``lifecycle_manager.LifecycleManager`` — load / unload / activate / deactivate
- ``security_service.SecurityService``   — signing policy enforcement
- ``plugin_registry.PluginIndex``        — in-memory index & settings persistence
- ``watcher.PluginFileWatcher``          — filesystem hot-reload

This module retains the shared data types (enums, dataclasses, base classes) and
the ``PluginService`` facade class so that existing imports remain valid.
"""

from __future__ import annotations

import asyncio
import logging
import re
import time
from abc import ABC, abstractmethod
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

# Phase 6A: Wasm execution imports
WasmRunner: type | None = None
WasmPluginConfig: type | None = None
WasmExecutionResult: type | None = None
CapabilitySet: type | None = None
WASM_RUNNER_AVAILABLE = False
WASMTIME_AVAILABLE = False

try:
    from backend.plugins.wasm.capability_tokens import CapabilitySet as _CapabilitySet
    from backend.plugins.wasm.wasm_runner import (
        WASMTIME_AVAILABLE,
    )
    from backend.plugins.wasm.wasm_runner import WasmExecutionResult as _WasmExecutionResult
    from backend.plugins.wasm.wasm_runner import WasmPluginConfig as _WasmPluginConfig
    from backend.plugins.wasm.wasm_runner import WasmRunner as _WasmRunner

    WasmRunner = _WasmRunner
    WasmPluginConfig = _WasmPluginConfig
    WasmExecutionResult = _WasmExecutionResult
    CapabilitySet = _CapabilitySet
    WASM_RUNNER_AVAILABLE = True
except ImportError:  # ALLOWED: bare except - optional wasm runner
    pass

logger = logging.getLogger(__name__)

# Application version for compatibility checking
APP_VERSION = "1.0.0"


# ---------------------------------------------------------------------------
# Shared utility functions
# ---------------------------------------------------------------------------

def parse_version(version_str: str) -> tuple[int, ...]:
    """Parse a version string into a tuple of integers for comparison."""
    match = re.match(r"^(\d+)\.(\d+)\.(\d+)", version_str)
    if match:
        return tuple(int(x) for x in match.groups())
    return (0, 0, 0)


def is_version_compatible(app_version: str, min_required: str) -> bool:
    """Check if app version meets minimum required version."""
    return parse_version(app_version) >= parse_version(min_required)


# ---------------------------------------------------------------------------
# Enums
# ---------------------------------------------------------------------------

class PluginType(Enum):
    """Types of plugins."""

    ENGINE = "engine"
    PROCESSOR = "processor"
    EXPORTER = "exporter"
    IMPORTER = "importer"
    UI_PANEL = "ui_panel"
    TOOL = "tool"


class PluginState(Enum):
    """Plugin lifecycle states."""

    DISCOVERED = "discovered"
    LOADED = "loaded"
    ACTIVATED = "activated"
    DEACTIVATED = "deactivated"
    ERROR = "error"


# ---------------------------------------------------------------------------
# Dataclasses
# ---------------------------------------------------------------------------

@dataclass
class PluginManifest:
    """Plugin manifest describing a plugin."""

    plugin_id: str
    name: str
    version: str
    description: str
    author: str
    plugin_type: PluginType
    entry_point: str
    dependencies: list[str] = field(default_factory=list)
    min_app_version: str = "1.0.0"
    permissions: list[str] = field(default_factory=list)
    settings_schema: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> PluginManifest:
        return cls(
            plugin_id=data["plugin_id"],
            name=data["name"],
            version=data["version"],
            description=data.get("description", ""),
            author=data.get("author", "Unknown"),
            plugin_type=PluginType(data["plugin_type"]),
            entry_point=data["entry_point"],
            dependencies=data.get("dependencies", []),
            min_app_version=data.get("min_app_version", "1.0.0"),
            permissions=data.get("permissions", []),
            settings_schema=data.get("settings_schema", {}),
        )

    def to_dict(self) -> dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "name": self.name,
            "version": self.version,
            "description": self.description,
            "author": self.author,
            "plugin_type": self.plugin_type.value,
            "entry_point": self.entry_point,
            "dependencies": self.dependencies,
            "min_app_version": self.min_app_version,
            "permissions": self.permissions,
            "settings_schema": self.settings_schema,
        }


@dataclass
class PluginInfo:
    """Runtime plugin information."""

    manifest: PluginManifest
    state: PluginState
    path: Path
    instance: Any | None = None
    error_message: str | None = None
    loaded_at: datetime | None = None
    settings: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return {
            "manifest": self.manifest.to_dict(),
            "state": self.state.value,
            "path": str(self.path),
            "error_message": self.error_message,
            "loaded_at": self.loaded_at.isoformat() if self.loaded_at else None,
            "settings": self.settings,
        }


# ---------------------------------------------------------------------------
# Base classes (deprecated — see ADR-038)
# ---------------------------------------------------------------------------

class PluginBase(ABC):
    """
    Base class for all plugins.

    .. deprecated:: 1.3.0
       Use :class:`Plugin` from `app.core.plugins_api` instead.
       This class will be removed in version 1.5.0. See ADR-038.
    """

    def __init__(self, plugin_service: PluginService):
        import warnings

        warnings.warn(
            f"{self.__class__.__name__} inherits from deprecated PluginBase. "
            "Migrate to 'from app.core.plugins_api import Plugin'. "
            "See ADR-038 for guidance. Will be removed in v1.5.0.",
            DeprecationWarning,
            stacklevel=2,
        )
        self.plugin_service = plugin_service
        self._initialized = False

    @abstractmethod
    async def activate(self) -> bool:
        """Activate the plugin. Called when plugin is enabled."""

    @abstractmethod
    async def deactivate(self) -> bool:
        """Deactivate the plugin. Called when plugin is disabled."""

    @property
    @abstractmethod
    def manifest(self) -> PluginManifest:
        """Return plugin manifest."""

    def get_setting(self, key: str, default: Any = None) -> Any:
        """Get plugin setting."""
        return self.plugin_service.get_plugin_setting(self.manifest.plugin_id, key, default)

    def set_setting(self, key: str, value: Any) -> None:
        """Set plugin setting."""
        self.plugin_service.set_plugin_setting(self.manifest.plugin_id, key, value)


class EnginePlugin(PluginBase):
    """
    Base class for engine plugins.

    .. deprecated:: 1.3.0
       Use :class:`Plugin` with :class:`EngineMixin` from `app.core.plugins_api`.
       See ADR-038 for guidance. Will be removed in v1.5.0.
    """

    @abstractmethod
    async def synthesize(
        self,
        text: str,
        voice_id: str,
        options: dict[str, Any],
    ) -> bytes:
        """Synthesize speech."""

    @abstractmethod
    async def list_voices(self) -> list[dict[str, Any]]:
        """List available voices."""


class ProcessorPlugin(PluginBase):
    """
    Base class for audio processor plugins.

    .. deprecated:: 1.3.0
       Use :class:`Plugin` with :class:`ProcessorMixin` from `app.core.plugins_api`.
       See ADR-038 for guidance. Will be removed in v1.5.0.
    """

    @abstractmethod
    async def process(
        self,
        audio_data: bytes,
        sample_rate: int,
        options: dict[str, Any],
    ) -> bytes:
        """Process audio."""


class ExporterPlugin(PluginBase):
    """
    Base class for exporter plugins.

    .. deprecated:: 1.3.0
       Use :class:`Plugin` with :class:`ExporterMixin` from `app.core.plugins_api`.
       See ADR-038 for guidance. Will be removed in v1.5.0.
    """

    @abstractmethod
    async def export(
        self,
        audio_data: bytes,
        output_path: Path,
        options: dict[str, Any],
    ) -> bool:
        """Export audio to file."""

    @property
    @abstractmethod
    def supported_formats(self) -> list[str]:
        """Return list of supported export formats."""


class ImporterPlugin(PluginBase):
    """
    Base class for importer plugins.

    .. deprecated:: 1.3.0
       Use :class:`Plugin` with :class:`ImporterMixin` from `app.core.plugins_api`.
       See ADR-038 for guidance. Will be removed in v1.5.0.
    """

    @abstractmethod
    async def import_file(
        self,
        input_path: Path,
        options: dict[str, Any],
    ) -> bytes:
        """Import audio from file."""

    @property
    @abstractmethod
    def supported_formats(self) -> list[str]:
        """Return list of supported import formats."""


# ---------------------------------------------------------------------------
# Extension points
# ---------------------------------------------------------------------------

ExtensionPoint = Callable[..., Any]
EXTENSION_POINTS: dict[str, list[ExtensionPoint]] = {
    "pre_synthesis": [],
    "post_synthesis": [],
    "voice_loaded": [],
    "audio_processed": [],
    "export_complete": [],
}


def register_extension(point_name: str) -> Callable:
    """Decorator to register an extension point handler."""

    def decorator(func: ExtensionPoint) -> ExtensionPoint:
        if point_name in EXTENSION_POINTS:
            EXTENSION_POINTS[point_name].append(func)
        return func

    return decorator


# ---------------------------------------------------------------------------
# PluginService facade
# ---------------------------------------------------------------------------

class PluginService:
    """Plugin management service — thin facade composing extracted services.

    All heavy logic lives in the service modules; this class wires them
    together and presents the original public API for backward compatibility.
    """

    def __init__(
        self,
        plugins_dir: Path | None = None,
        app_version: str | None = None,
        enable_watcher: bool = True,
    ):
        from backend.plugins.lifecycle_manager import LifecycleManager
        from backend.plugins.manifest_service import ManifestService
        from backend.plugins.plugin_registry import PluginIndex
        from backend.plugins.security_service import SecurityService
        from backend.plugins.watcher import PluginFileWatcher

        self._plugins_dir = plugins_dir or Path("plugins")
        self._app_version = app_version or APP_VERSION
        self._enable_watcher = enable_watcher
        self._initialized = False

        # Compose services
        self._plugin_index = PluginIndex()
        self._manifest_service = ManifestService(self._plugins_dir, self._app_version)
        self._lifecycle_manager = LifecycleManager(self._plugin_index, self._manifest_service)
        self._security_service = SecurityService(self._plugin_index, self._lifecycle_manager)

        # Backward-compat: expose the dict directly so existing code
        # referencing ``service._plugins`` still works.
        self._plugins = self._plugin_index.plugins
        self._settings = self._plugin_index.settings

        # Watcher (created on initialize)
        self._watcher_class = PluginFileWatcher
        self._watcher: PluginFileWatcher | None = None

        logger.info(f"PluginService created with plugins dir: {self._plugins_dir}")

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    async def initialize(self) -> bool:
        """Initialize the plugin service."""
        if self._initialized:
            return True

        try:
            self._plugins_dir.mkdir(parents=True, exist_ok=True)

            await self._plugin_index.load_settings(self._plugins_dir)

            await self.discover_plugins()

            if self._enable_watcher:
                self._watcher = self._watcher_class(
                    reload_callback=self.reload_plugin,
                    plugin_index=self._plugin_index,
                    plugins_dir=self._plugins_dir,
                )
                self._watcher.start()

            self._initialized = True
            logger.info(f"PluginService initialized (app version: {self._app_version})")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize PluginService: {e}")
            return False

    async def shutdown(self) -> None:
        """Shutdown the plugin service."""
        if self._watcher:
            self._watcher.stop()
            self._watcher = None

        for plugin_id in list(self._plugins.keys()):
            if self._plugins[plugin_id].state == PluginState.ACTIVATED:
                await self.deactivate_plugin(plugin_id)

        self._initialized = False
        logger.info("PluginService shutdown complete")

    # ------------------------------------------------------------------
    # Discovery (delegates to ManifestService)
    # ------------------------------------------------------------------

    async def discover_plugins(self) -> list[PluginInfo]:
        """Discover available plugins."""
        discovered = await self._manifest_service.discover_plugins(self._plugin_index.settings)
        for info in discovered:
            self._plugin_index.register(info.manifest.plugin_id, info)
        return discovered

    def _convert_manifest_data(
        self, data: dict[str, Any], using_unified_schema: bool
    ) -> PluginManifest:
        """Convert manifest data to internal PluginManifest."""
        return self._manifest_service._convert_manifest_data(data, using_unified_schema)

    def check_version_compatibility(self, manifest: PluginManifest) -> bool:
        """Check if a plugin is compatible with the current app version."""
        return self._manifest_service.check_version_compatibility(manifest)

    # ------------------------------------------------------------------
    # Loading / activation (delegates to LifecycleManager)
    # ------------------------------------------------------------------

    async def load_plugin(self, plugin_id: str) -> bool:
        """Load a plugin."""
        return await self._lifecycle_manager.load_plugin(plugin_id)

    async def unload_plugin(self, plugin_id: str) -> bool:
        """Unload a plugin."""
        return await self._lifecycle_manager.unload_plugin(plugin_id)

    async def activate_plugin(self, plugin_id: str) -> bool:
        """Activate a loaded plugin."""
        return await self._lifecycle_manager.activate_plugin(plugin_id)

    async def deactivate_plugin(self, plugin_id: str) -> bool:
        """Deactivate an active plugin."""
        return await self._lifecycle_manager.deactivate_plugin(plugin_id)

    async def reload_plugin(self, plugin_id: str) -> bool:
        """Reload a plugin (hot-reload support)."""
        return await self._lifecycle_manager.reload_plugin(plugin_id)

    # ------------------------------------------------------------------
    # Security (delegates to SecurityService)
    # ------------------------------------------------------------------

    def verify_plugin_signature(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """Verify the cryptographic signature of a plugin."""
        return self._security_service.verify_plugin_signature(plugin_id, require_signature)

    async def load_plugin_with_verification(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """Load a plugin with optional signature verification."""
        return await self._security_service.load_plugin_with_verification(
            plugin_id, require_signature
        )

    # ------------------------------------------------------------------
    # Registry queries (delegates to PluginIndex)
    # ------------------------------------------------------------------

    def get_plugin(self, plugin_id: str) -> PluginInfo | None:
        """Get plugin info by ID."""
        return self._plugin_index.get_plugin(plugin_id)

    def list_plugins(
        self,
        plugin_type: PluginType | None = None,
        state: PluginState | None = None,
    ) -> list[PluginInfo]:
        """List plugins with optional filtering."""
        return self._plugin_index.list_plugins(plugin_type=plugin_type, state=state)

    def get_active_plugins(self, plugin_type: PluginType | None = None) -> list[PluginInfo]:
        """Get all activated plugins."""
        return self._plugin_index.get_active_plugins(plugin_type=plugin_type)

    def get_engine_plugins(self) -> list[PluginInfo]:
        """Get all engine plugins."""
        return self._plugin_index.get_engine_plugins()

    def get_processor_plugins(self) -> list[PluginInfo]:
        """Get all processor plugins."""
        return self._plugin_index.get_processor_plugins()

    # ------------------------------------------------------------------
    # Settings (delegates to PluginIndex)
    # ------------------------------------------------------------------

    def get_plugin_setting(self, plugin_id: str, key: str, default: Any = None) -> Any:
        """Get a plugin setting."""
        return self._plugin_index.get_plugin_setting(plugin_id, key, default)

    def set_plugin_setting(self, plugin_id: str, key: str, value: Any) -> None:
        """Set a plugin setting."""
        self._plugin_index.set_plugin_setting(plugin_id, key, value)
        self._plugin_index.schedule_save(self._plugins_dir)

    async def _load_settings(self) -> None:
        """Load plugin settings from file."""
        await self._plugin_index.load_settings(self._plugins_dir)

    async def _save_settings(self) -> None:
        """Save plugin settings to file."""
        await self._plugin_index.save_settings(self._plugins_dir)

    # ------------------------------------------------------------------
    # Wasm execution (kept on facade — cross-cuts multiple services)
    # ------------------------------------------------------------------

    def is_wasm_plugin(self, plugin_id: str) -> bool:
        """Check if a plugin is a Wasm plugin."""
        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return False

        manifest = plugin_info.manifest
        if hasattr(manifest, "runtime") and manifest.runtime == "wasm":
            return True

        if hasattr(manifest, "entry_points"):
            entry_points = manifest.entry_points
            if isinstance(entry_points, dict):
                for ep in entry_points.values():
                    if isinstance(ep, str) and ep.endswith(".wasm"):
                        return True
            elif isinstance(entry_points, list):
                for ep in entry_points:
                    if isinstance(ep, str) and ep.endswith(".wasm"):
                        return True

        plugin_dir = self._plugins_dir / plugin_id
        if plugin_dir.exists():
            wasm_files = list(plugin_dir.glob("*.wasm"))
            if wasm_files:
                return True

        return False

    def get_wasm_path(self, plugin_id: str) -> Path | None:
        """Get the path to the Wasm binary for a plugin."""
        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return None

        plugin_dir = self._plugins_dir / plugin_id
        manifest = plugin_info.manifest

        if hasattr(manifest, "entry_points"):
            entry_points = manifest.entry_points
            wasm_entry: str | None = None

            if isinstance(entry_points, dict):
                for key in ["wasm", "main", "default"]:
                    if key in entry_points and str(entry_points[key]).endswith(".wasm"):
                        wasm_entry = entry_points[key]
                        break
                if wasm_entry is None:
                    for ep in entry_points.values():
                        if isinstance(ep, str) and ep.endswith(".wasm"):
                            wasm_entry = ep
                            break

            if wasm_entry:
                wasm_path = plugin_dir / wasm_entry
                if wasm_path.exists():
                    return wasm_path

        if plugin_dir.exists():
            wasm_files = list(plugin_dir.glob("*.wasm"))
            if wasm_files:
                return wasm_files[0]

        return None

    async def execute_wasm_plugin(
        self,
        plugin_id: str,
        function_name: str | None = None,
        input_data: bytes | None = None,
        capabilities: list[str] | None = None,
        memory_limit_mb: int = 64,
        timeout_seconds: float = 30.0,
    ) -> dict[str, Any]:
        """Execute a Wasm plugin function."""
        if not WASM_RUNNER_AVAILABLE:
            return {
                "success": False,
                "error": "Wasm runtime not available: backend.plugins.wasm module not found",
                "output": None,
                "execution_time_ms": 0,
            }

        if not WASMTIME_AVAILABLE:
            return {
                "success": False,
                "error": "Wasm runtime not available: wasmtime-py not installed",
                "output": None,
                "execution_time_ms": 0,
            }

        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return {
                "success": False,
                "error": f"Plugin '{plugin_id}' not found",
                "output": None,
                "execution_time_ms": 0,
            }

        if not self.is_wasm_plugin(plugin_id):
            return {
                "success": False,
                "error": f"Plugin '{plugin_id}' is not a Wasm plugin",
                "output": None,
                "execution_time_ms": 0,
            }

        wasm_path = self.get_wasm_path(plugin_id)
        if wasm_path is None or not wasm_path.exists():
            return {
                "success": False,
                "error": f"Wasm binary not found for plugin '{plugin_id}'",
                "output": None,
                "execution_time_ms": 0,
            }

        from backend.plugins.wasm.capability_tokens import CapabilitySet as _CapabilitySet
        from backend.plugins.wasm.capability_tokens import (
            parse_capabilities_from_manifest,
        )
        from backend.plugins.wasm.wasm_runner import WasmPluginConfig as _WasmPluginConfig
        from backend.plugins.wasm.wasm_runner import WasmRunner as _WasmRunner

        cap_set = _CapabilitySet.empty()
        if capabilities:
            cap_set = parse_capabilities_from_manifest(capabilities)

        config = _WasmPluginConfig(
            plugin_id=plugin_id,
            wasm_path=wasm_path,
            capabilities=cap_set,
            memory_pages=memory_limit_mb * 16,
            timeout_seconds=timeout_seconds,
        )

        start_time = time.perf_counter()

        try:
            runner = _WasmRunner()
            result = await runner.execute(config, function_name or "_start", {})

            execution_time_ms = (time.perf_counter() - start_time) * 1000

            return {
                "success": result.success,
                "output": result.output,
                "error": result.error if not result.success else None,
                "execution_time_ms": execution_time_ms,
                "metrics": {
                    "memory_used_mb": getattr(result, "memory_used_mb", None),
                    "instructions_executed": getattr(result, "instructions_executed", None),
                },
            }

        except Exception as e:
            execution_time_ms = (time.perf_counter() - start_time) * 1000
            logger.error(f"Wasm execution failed for plugin '{plugin_id}': {e}")
            return {
                "success": False,
                "error": str(e),
                "output": None,
                "execution_time_ms": execution_time_ms,
            }

    async def list_wasm_plugins(self) -> list[PluginInfo]:
        """List all Wasm plugins."""
        all_plugins = self.list_plugins()
        return [p for p in all_plugins if self.is_wasm_plugin(p.manifest.name)]

    # ------------------------------------------------------------------
    # Extension points
    # ------------------------------------------------------------------

    async def call_extension_point(
        self,
        point_name: str,
        *args: Any,
        **kwargs: Any,
    ) -> list[Any]:
        """Call all handlers registered for an extension point."""
        results: list[Any] = []

        if point_name not in EXTENSION_POINTS:
            return results

        for handler in EXTENSION_POINTS[point_name]:
            try:
                if asyncio.iscoroutinefunction(handler):
                    result = await handler(*args, **kwargs)
                else:
                    result = handler(*args, **kwargs)
                results.append(result)
            except Exception as e:
                logger.error(f"Extension point handler error: {e}")

        return results


# ---------------------------------------------------------------------------
# Singleton
# ---------------------------------------------------------------------------

_plugin_service: PluginService | None = None


def get_plugin_service() -> PluginService:
    """Get or create the plugin service singleton."""
    global _plugin_service
    if _plugin_service is None:
        _plugin_service = PluginService()
    return _plugin_service


# =============================================================================
# Phase 6 Integration Classes (kept here — used internally by lifecycle_manager)
# =============================================================================


class Phase6AIQuality:
    """Phase 6B: AI-Assisted Plugin Quality integration."""

    def __init__(self) -> None:
        self._code_reviewer: Any | None = None
        self._anomaly_detector: Any | None = None
        self._recommendation_engine: Any | None = None
        self._initialized = False

    def _lazy_init(self) -> bool:
        """Lazy initialization of Phase 6B modules."""
        if self._initialized:
            return True
        try:
            from backend.plugins.ai_quality import (
                AnomalyDetector,
                CodeReviewer,
                RecommendationEngine,
            )

            self._code_reviewer = CodeReviewer()
            self._anomaly_detector = AnomalyDetector()
            self._recommendation_engine = RecommendationEngine()
            self._initialized = True
            logger.info("Phase 6B AI Quality modules initialized")
            return True
        except ImportError as e:
            logger.warning(f"Phase 6B modules not available: {e}")
            return False
        except Exception as e:
            logger.error(f"Phase 6B initialization failed: {e}")
            return False

    async def review_plugin_code(self, plugin_path: Path) -> dict[str, Any]:
        """Run AI-assisted code review on plugin."""
        if not self._lazy_init() or not self._code_reviewer:
            return {"skipped": True, "reason": "Phase 6B not available"}
        try:
            result = await self._code_reviewer.review_plugin(str(plugin_path))
            return result.to_dict() if hasattr(result, "to_dict") else {"result": result}
        except Exception as e:
            logger.error(f"Code review failed for {plugin_path}: {e}")
            return {"error": str(e)}

    def check_anomalies(self, plugin_id: str, metrics: dict[str, float]) -> list[dict[str, Any]]:
        """Check for anomalies in plugin metrics."""
        if not self._lazy_init() or not self._anomaly_detector:
            return []
        try:
            anomalies = self._anomaly_detector.detect(plugin_id, metrics)
            return [a.to_dict() if hasattr(a, "to_dict") else a for a in anomalies]
        except Exception as e:
            logger.error(f"Anomaly detection failed for {plugin_id}: {e}")
            return []

    def get_recommendations(self, user_id: str, limit: int = 5) -> list[str]:
        """Get plugin recommendations for a user."""
        if not self._lazy_init() or not self._recommendation_engine:
            return []
        try:
            recs: list[str] = self._recommendation_engine.get_recommendations(user_id, limit)
            return recs
        except Exception as e:
            logger.error(f"Recommendations failed for {user_id}: {e}")
            return []


class Phase6Compliance:
    """Phase 6C: Automated Compliance & Privacy integration."""

    def __init__(self) -> None:
        self._compliance_scanner: Any | None = None
        self._privacy_engine: Any | None = None
        self._initialized = False

    def _lazy_init(self) -> bool:
        """Lazy initialization of Phase 6C modules."""
        if self._initialized:
            return True
        try:
            from backend.plugins.compliance import ComplianceScanner, PrivacyEngine

            self._compliance_scanner = ComplianceScanner()
            self._privacy_engine = PrivacyEngine()
            self._initialized = True
            logger.info("Phase 6C Compliance modules initialized")
            return True
        except ImportError as e:
            logger.warning(f"Phase 6C modules not available: {e}")
            return False
        except Exception as e:
            logger.error(f"Phase 6C initialization failed: {e}")
            return False

    async def scan_compliance(self, plugin_path: Path) -> dict[str, Any]:
        """Scan plugin for compliance issues."""
        if not self._lazy_init() or not self._compliance_scanner:
            return {"skipped": True, "reason": "Phase 6C not available"}
        try:
            result = await self._compliance_scanner.scan(str(plugin_path))
            return result.to_dict() if hasattr(result, "to_dict") else {"result": result}
        except Exception as e:
            logger.error(f"Compliance scan failed for {plugin_path}: {e}")
            return {"error": str(e)}

    def get_privacy_engine(self) -> Any:
        """Get the privacy engine for data handling."""
        self._lazy_init()
        return self._privacy_engine


class Phase6Ecosystem:
    """Phase 6D: Ecosystem Growth & Analytics integration."""

    def __init__(self) -> None:
        self._developer_analytics: Any | None = None
        self._featured_plugins: Any | None = None
        self._initialized = False

    def _lazy_init(self) -> bool:
        """Lazy initialization of Phase 6D modules."""
        if self._initialized:
            return True
        try:
            from backend.plugins.ecosystem import DeveloperAnalytics, FeaturedPluginsManager

            self._developer_analytics = DeveloperAnalytics()
            self._featured_plugins = FeaturedPluginsManager()
            self._initialized = True
            logger.info("Phase 6D Ecosystem modules initialized")
            return True
        except ImportError as e:
            logger.warning(f"Phase 6D modules not available: {e}")
            return False
        except Exception as e:
            logger.error(f"Phase 6D initialization failed: {e}")
            return False

    def record_plugin_event(
        self,
        plugin_id: str,
        event_type: str,
        metadata: dict[str, Any] | None = None,
    ) -> None:
        """Record a plugin lifecycle event for analytics."""
        if not self._lazy_init() or not self._developer_analytics:
            return
        try:
            self._developer_analytics.record_event(plugin_id, event_type, metadata or {})
        except Exception as e:
            logger.error(f"Failed to record event for {plugin_id}: {e}")

    def get_featured_plugins(self, limit: int = 10) -> list[str]:
        """Get featured plugin IDs."""
        if not self._lazy_init() or not self._featured_plugins:
            return []
        try:
            featured: list[str] = self._featured_plugins.get_featured(limit)
            return featured
        except Exception as e:
            logger.error(f"Failed to get featured plugins: {e}")
            return []


# Phase 6 singletons
_phase6_ai_quality: Phase6AIQuality | None = None
_phase6_compliance: Phase6Compliance | None = None
_phase6_ecosystem: Phase6Ecosystem | None = None


def get_phase6_ai_quality() -> Phase6AIQuality:
    """Get or create the Phase 6B AI Quality integration."""
    global _phase6_ai_quality
    if _phase6_ai_quality is None:
        _phase6_ai_quality = Phase6AIQuality()
    return _phase6_ai_quality


def get_phase6_compliance() -> Phase6Compliance:
    """Get or create the Phase 6C Compliance integration."""
    global _phase6_compliance
    if _phase6_compliance is None:
        _phase6_compliance = Phase6Compliance()
    return _phase6_compliance


def get_phase6_ecosystem() -> Phase6Ecosystem:
    """Get or create the Phase 6D Ecosystem integration."""
    global _phase6_ecosystem
    if _phase6_ecosystem is None:
        _phase6_ecosystem = Phase6Ecosystem()
    return _phase6_ecosystem
