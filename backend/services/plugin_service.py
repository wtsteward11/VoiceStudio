"""
Plugin Service

Phase 12.2: Plugin Architecture
Extensible plugin system for VoiceStudio.

Features:
- Plugin discovery and loading
- Plugin lifecycle management
- Extension points (engines, processors, UI)
- Plugin sandboxing
- Unified manifest validation (Phase 1)
"""

from __future__ import annotations

import asyncio
import importlib
import importlib.util
import json
import logging
import re
import threading
from abc import ABC, abstractmethod
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

# Schema validation (Phase 1)
from backend.services.plugin_schema_validator import (
    PluginSchemaValidator,
    get_validator,
    validate_plugin_manifest,
    validate_plugin_manifest_file,
)

# Phase 6A: Wasm execution imports
try:
    from backend.plugins.wasm.capability_tokens import CapabilitySet
    from backend.plugins.wasm.wasm_runner import (
        WASMTIME_AVAILABLE,
        WasmExecutionResult,
        WasmPluginConfig,
        WasmRunner,
    )

    WASM_RUNNER_AVAILABLE = True
except ImportError:
    WASM_RUNNER_AVAILABLE = False
    WASMTIME_AVAILABLE = False
    WasmRunner = None
    WasmPluginConfig = None
    WasmExecutionResult = None
    CapabilitySet = None

# Phase 5B: Signature verification imports
try:
    from backend.plugins.supply_chain.signer import (
        VerificationResult,
        check_signing_available,
        verify_package_auto,
    )

    SIGNING_AVAILABLE = check_signing_available()
except ImportError:
    SIGNING_AVAILABLE = False
    verify_package_auto = None
    VerificationResult = None

# Phase 6 module imports - lazy loaded to avoid circular imports
_phase6_ai_quality: Phase6AIQuality | None = None
_phase6_compliance: Phase6Compliance | None = None
_phase6_ecosystem: Phase6Ecosystem | None = None

try:
    from watchdog.events import FileModifiedEvent, FileSystemEventHandler
    from watchdog.observers import Observer

    WATCHDOG_AVAILABLE = True
except ImportError:
    WATCHDOG_AVAILABLE = False
    Observer = None
    FileSystemEventHandler = object
    FileModifiedEvent = None

logger = logging.getLogger(__name__)

# Application version for compatibility checking
APP_VERSION = "1.0.0"


def parse_version(version_str: str) -> tuple[int, ...]:
    """Parse a version string into a tuple of integers for comparison."""
    match = re.match(r"^(\d+)\.(\d+)\.(\d+)", version_str)
    if match:
        return tuple(int(x) for x in match.groups())
    return (0, 0, 0)


def is_version_compatible(app_version: str, min_required: str) -> bool:
    """Check if app version meets minimum required version."""
    return parse_version(app_version) >= parse_version(min_required)


class PluginType(Enum):
    """Types of plugins."""

    ENGINE = "engine"  # Audio synthesis/processing engine
    PROCESSOR = "processor"  # Audio post-processor
    EXPORTER = "exporter"  # Export format handler
    IMPORTER = "importer"  # Import format handler
    UI_PANEL = "ui_panel"  # Custom UI panel
    TOOL = "tool"  # Utility tool


class PluginState(Enum):
    """Plugin lifecycle states."""

    DISCOVERED = "discovered"
    LOADED = "loaded"
    ACTIVATED = "activated"
    DEACTIVATED = "deactivated"
    ERROR = "error"


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
        pass

    @abstractmethod
    async def deactivate(self) -> bool:
        """Deactivate the plugin. Called when plugin is disabled."""
        pass

    @property
    @abstractmethod
    def manifest(self) -> PluginManifest:
        """Return plugin manifest."""
        pass

    def get_setting(self, key: str, default: Any = None) -> Any:
        """Get plugin setting."""
        return self.plugin_service.get_plugin_setting(self.manifest.plugin_id, key, default)

    def set_setting(self, key: str, value: Any):
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
        pass

    @abstractmethod
    async def list_voices(self) -> list[dict[str, Any]]:
        """List available voices."""
        pass


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
        pass


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
        pass

    @property
    @abstractmethod
    def supported_formats(self) -> list[str]:
        """Return list of supported export formats."""
        pass


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
        pass

    @property
    @abstractmethod
    def supported_formats(self) -> list[str]:
        """Return list of supported import formats."""
        pass


# Extension points registry
ExtensionPoint = Callable[..., Any]
EXTENSION_POINTS: dict[str, list[ExtensionPoint]] = {
    "pre_synthesis": [],
    "post_synthesis": [],
    "voice_loaded": [],
    "audio_processed": [],
    "export_complete": [],
}


class PluginFileWatcher(FileSystemEventHandler if WATCHDOG_AVAILABLE else object):
    """
    File system watcher for hot-reloading plugins.

    Monitors the plugins directory for changes and triggers
    plugin reload when plugin files are modified.
    """

    def __init__(self, plugin_service: PluginService):
        if WATCHDOG_AVAILABLE:
            super().__init__()
        self._plugin_service = plugin_service
        self._observer: Observer | None = None
        self._debounce_timers: dict[str, threading.Timer] = {}
        self._debounce_delay = 1.0  # seconds
        self._running = False

    def start(self, plugins_dir: Path) -> bool:
        """Start watching the plugins directory."""
        if not WATCHDOG_AVAILABLE:
            logger.warning("watchdog not installed - plugin hot-reload disabled")
            return False

        if self._running:
            return True

        try:
            self._observer = Observer()
            self._observer.schedule(self, str(plugins_dir), recursive=True)
            self._observer.start()
            self._running = True
            logger.info(f"Plugin watcher started for: {plugins_dir}")
            return True
        except Exception as e:
            logger.error(f"Failed to start plugin watcher: {e}")
            return False

    def stop(self):
        """Stop watching."""
        if self._observer and self._running:
            self._observer.stop()
            self._observer.join(timeout=5.0)
            self._running = False
            logger.info("Plugin watcher stopped")

        # Cancel pending debounce timers
        for timer in self._debounce_timers.values():
            timer.cancel()
        self._debounce_timers.clear()

    def on_modified(self, event):
        """Handle file modification events."""
        if event.is_directory:
            return

        file_path = Path(event.src_path)

        # Only watch for plugin.json or .py files
        if file_path.suffix not in (".json", ".py"):
            return

        # Find which plugin was modified
        plugin_id = self._find_plugin_id(file_path)
        if not plugin_id:
            return

        # Debounce rapid changes
        if plugin_id in self._debounce_timers:
            self._debounce_timers[plugin_id].cancel()

        timer = threading.Timer(self._debounce_delay, self._trigger_reload, args=[plugin_id])
        self._debounce_timers[plugin_id] = timer
        timer.start()

    def _find_plugin_id(self, file_path: Path) -> str | None:
        """Find the plugin ID from a file path."""
        plugins_dir = self._plugin_service._plugins_dir

        try:
            rel_path = file_path.relative_to(plugins_dir)
            plugin_folder = rel_path.parts[0] if rel_path.parts else None

            if plugin_folder:
                # Check if this folder is a known plugin
                for plugin_id, info in self._plugin_service._plugins.items():
                    if info.path.name == plugin_folder:
                        return plugin_id
        except ValueError:
            pass  # ALLOWED: bare except - path parsing may fail for non-standard paths

        return None

    def _trigger_reload(self, plugin_id: str):
        """Trigger plugin reload (runs in timer thread)."""
        if plugin_id in self._debounce_timers:
            del self._debounce_timers[plugin_id]

        logger.info(f"Hot-reloading plugin: {plugin_id}")

        # Schedule reload in the asyncio event loop
        try:
            loop = asyncio.get_event_loop()
            if loop.is_running():
                asyncio.run_coroutine_threadsafe(
                    self._plugin_service.reload_plugin(plugin_id), loop
                )
            else:
                asyncio.run(self._plugin_service.reload_plugin(plugin_id))
        except Exception as e:
            logger.error(f"Failed to reload plugin {plugin_id}: {e}")


def register_extension(point_name: str) -> Callable:
    """Decorator to register an extension point handler."""

    def decorator(func: ExtensionPoint) -> ExtensionPoint:
        if point_name in EXTENSION_POINTS:
            EXTENSION_POINTS[point_name].append(func)
        return func

    return decorator


class PluginService:
    """
    Plugin management service.

    Phase 12.2: Plugin Architecture

    Features:
    - Plugin discovery from plugins directory
    - Plugin lifecycle management
    - Settings persistence
    - Extension point system
    """

    def __init__(
        self,
        plugins_dir: Path | None = None,
        app_version: str | None = None,
        enable_watcher: bool = True,
    ):
        self._plugins_dir = plugins_dir or Path("plugins")
        self._plugins: dict[str, PluginInfo] = {}
        self._settings: dict[str, dict[str, Any]] = {}
        self._initialized = False
        self._app_version = app_version or APP_VERSION
        self._enable_watcher = enable_watcher
        self._watcher: PluginFileWatcher | None = None

        logger.info(f"PluginService created with plugins dir: {self._plugins_dir}")

    async def initialize(self) -> bool:
        """Initialize the plugin service."""
        if self._initialized:
            return True

        try:
            # Create plugins directory
            self._plugins_dir.mkdir(parents=True, exist_ok=True)

            # Load settings
            await self._load_settings()

            # Discover plugins
            await self.discover_plugins()

            # Start file watcher for hot-reload
            if self._enable_watcher:
                self._watcher = PluginFileWatcher(self)
                self._watcher.start(self._plugins_dir)

            self._initialized = True
            logger.info(f"PluginService initialized (app version: {self._app_version})")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize PluginService: {e}")
            return False

    async def shutdown(self) -> None:
        """Shutdown the plugin service."""
        # Stop file watcher
        if self._watcher:
            self._watcher.stop()
            self._watcher = None

        # Deactivate all active plugins
        for plugin_id in list(self._plugins.keys()):
            if self._plugins[plugin_id].state == PluginState.ACTIVATED:
                await self.deactivate_plugin(plugin_id)

        self._initialized = False
        logger.info("PluginService shutdown complete")

    async def discover_plugins(self) -> list[PluginInfo]:
        """Discover available plugins."""
        discovered = []

        if not self._plugins_dir.exists():
            return discovered

        for plugin_path in self._plugins_dir.iterdir():
            if not plugin_path.is_dir():
                continue

            # Prefer manifest.json (unified schema), fallback to plugin.json (legacy)
            manifest_path = plugin_path / "manifest.json"
            using_unified_schema = True
            if not manifest_path.exists():
                manifest_path = plugin_path / "plugin.json"
                using_unified_schema = False
                if not manifest_path.exists():
                    continue

            try:
                # Validate using unified schema validator (Phase 1)
                if using_unified_schema:
                    is_valid, errors, manifest_data = validate_plugin_manifest_file(manifest_path)
                    if not is_valid:
                        logger.warning(
                            f"Plugin manifest validation failed for {plugin_path.name}: {errors}"
                        )
                        continue
                else:
                    # Legacy manifest - load without unified validation but log warning
                    with open(manifest_path) as f:
                        manifest_data = json.load(f)
                    logger.info(
                        f"Plugin {plugin_path.name} uses legacy plugin.json format - "
                        "consider migrating to manifest.json with unified schema"
                    )

                manifest = self._convert_manifest_data(manifest_data, using_unified_schema)

                # Check version compatibility
                if not self.check_version_compatibility(manifest):
                    logger.warning(
                        f"Plugin {manifest.name} requires app version {manifest.min_app_version}, "
                        f"current version is {self._app_version} - skipping"
                    )
                    continue

                plugin_info = PluginInfo(
                    manifest=manifest,
                    state=PluginState.DISCOVERED,
                    path=plugin_path,
                    settings=self._settings.get(manifest.plugin_id, {}),
                )

                self._plugins[manifest.plugin_id] = plugin_info
                discovered.append(plugin_info)

                logger.info(f"Discovered plugin: {manifest.name} ({manifest.plugin_id})")

            except Exception as e:
                logger.warning(f"Failed to load plugin from {plugin_path}: {e}")

        return discovered

    def _convert_manifest_data(
        self, data: dict[str, Any], using_unified_schema: bool
    ) -> PluginManifest:
        """
        Convert manifest data to internal PluginManifest.

        Handles both unified schema (manifest.json) and legacy (plugin.json) formats.
        """
        if using_unified_schema:
            # Map unified schema fields to internal PluginManifest
            plugin_type_str = data.get("plugin_type", "tool")

            # Determine plugin type from capabilities
            capabilities = data.get("capabilities", {})
            if capabilities.get("engines"):
                plugin_type = PluginType.ENGINE
            elif capabilities.get("effects"):
                plugin_type = PluginType.PROCESSOR
            elif capabilities.get("export_formats"):
                plugin_type = PluginType.EXPORTER
            elif capabilities.get("import_formats"):
                plugin_type = PluginType.IMPORTER
            elif capabilities.get("ui_panels"):
                plugin_type = PluginType.UI_PANEL
            else:
                plugin_type = PluginType.TOOL

            entry_points = data.get("entry_points", {})
            entry_point = entry_points.get("backend", "")

            dependencies = data.get("dependencies", {})
            python_deps = dependencies.get("python", [])
            plugin_deps = dependencies.get("plugins", [])

            return PluginManifest(
                plugin_id=data.get("name", ""),
                name=data.get("display_name") or data.get("name", ""),
                version=data.get("version", "0.0.0"),
                description=data.get("description", ""),
                author=data.get("author", ""),
                plugin_type=plugin_type,
                entry_point=entry_point,
                dependencies=python_deps + plugin_deps,
                min_app_version=data.get("min_app_version", "1.0.0"),
                permissions=data.get("permissions", []),
                settings_schema=data.get("settings_schema", {}),
            )
        else:
            # Legacy format - use existing from_dict method
            return PluginManifest.from_dict(data)

    def check_version_compatibility(self, manifest: PluginManifest) -> bool:
        """Check if a plugin is compatible with the current app version."""
        return is_version_compatible(self._app_version, manifest.min_app_version)

    async def reload_plugin(self, plugin_id: str) -> bool:
        """
        Reload a plugin (hot-reload support).

        This unloads the plugin (if loaded), re-reads the manifest,
        and reloads the plugin code.
        """
        if plugin_id not in self._plugins:
            logger.warning(f"Cannot reload unknown plugin: {plugin_id}")
            return False

        plugin_info = self._plugins[plugin_id]
        was_activated = plugin_info.state == PluginState.ACTIVATED

        try:
            # Unload existing plugin
            await self.unload_plugin(plugin_id)

            # Re-read manifest (prefer manifest.json, fallback to plugin.json)
            manifest_path = plugin_info.path / "manifest.json"
            using_unified_schema = True
            if not manifest_path.exists():
                manifest_path = plugin_info.path / "plugin.json"
                using_unified_schema = False

            if manifest_path.exists():
                if using_unified_schema:
                    is_valid, errors, manifest_data = validate_plugin_manifest_file(manifest_path)
                    if not is_valid:
                        logger.error(f"Manifest validation failed on reload: {errors}")
                        plugin_info.state = PluginState.ERROR
                        plugin_info.error_message = f"Validation failed: {errors}"
                        return False
                else:
                    with open(manifest_path) as f:
                        manifest_data = json.load(f)

                new_manifest = self._convert_manifest_data(manifest_data, using_unified_schema)

                # Check version compatibility
                if not self.check_version_compatibility(new_manifest):
                    logger.error(
                        f"Reloaded plugin {new_manifest.name} requires app version "
                        f"{new_manifest.min_app_version}, current is {self._app_version}"
                    )
                    plugin_info.state = PluginState.ERROR
                    plugin_info.error_message = "Incompatible version"
                    return False

                plugin_info.manifest = new_manifest

            # Reload plugin module
            if was_activated:
                # Re-activate if it was active before
                success = await self.activate_plugin(plugin_id)
                if success:
                    logger.info(f"Hot-reloaded and reactivated plugin: {plugin_id}")
                return success
            else:
                # Just load without activating
                success = await self.load_plugin(plugin_id)
                if success:
                    logger.info(f"Hot-reloaded plugin: {plugin_id}")
                return success

        except Exception as e:
            logger.error(f"Failed to reload plugin {plugin_id}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return False

    async def load_plugin(self, plugin_id: str) -> bool:
        """Load a plugin."""
        if plugin_id not in self._plugins:
            logger.error(f"Plugin not found: {plugin_id}")
            return False

        plugin_info = self._plugins[plugin_id]

        try:
            # Load the plugin module
            entry_point = plugin_info.manifest.entry_point
            module_path = plugin_info.path / entry_point

            if not module_path.exists():
                raise FileNotFoundError(f"Entry point not found: {entry_point}")

            spec = importlib.util.spec_from_file_location(
                f"plugin_{plugin_id}",
                module_path,
            )

            if spec is None or spec.loader is None:
                raise ImportError(f"Cannot load module: {entry_point}")

            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)

            # Find and instantiate the plugin class
            # Support both unified Plugin (Phase 4+) and deprecated PluginBase
            from app.core.plugins_api import Plugin as UnifiedPlugin

            plugin_class = None
            uses_unified_plugin = False

            for name in dir(module):
                obj = getattr(module, name)
                if not isinstance(obj, type):
                    continue

                # Prefer unified Plugin class (Phase 4+)
                if issubclass(obj, UnifiedPlugin) and obj is not UnifiedPlugin:
                    plugin_class = obj
                    uses_unified_plugin = True
                    break

                # Fallback to deprecated PluginBase
                if (
                    issubclass(obj, PluginBase)
                    and obj is not PluginBase
                    and obj not in (EnginePlugin, ProcessorPlugin, ExporterPlugin, ImporterPlugin)
                ):
                    plugin_class = obj
                    break

            if plugin_class is None:
                raise TypeError(f"No plugin class found in {entry_point}")

            # Instantiate based on class type
            if uses_unified_plugin:
                plugin_info.instance = plugin_class(plugin_info.path)
            else:
                plugin_info.instance = plugin_class(self)
            plugin_info.state = PluginState.LOADED
            plugin_info.loaded_at = datetime.now()

            # Phase 6 integration: record load event and run code review
            await self._run_phase6_on_load(plugin_info)

            logger.info(f"Loaded plugin: {plugin_info.manifest.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to load plugin {plugin_id}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return False

    async def _run_phase6_on_load(self, plugin_info: PluginInfo) -> None:
        """Run Phase 6 integrations when a plugin is loaded."""
        try:
            # Phase 6D: Record load event for analytics
            ecosystem = get_phase6_ecosystem()
            ecosystem.record_plugin_event(
                plugin_info.manifest.plugin_id,
                "plugin_loaded",
                {"version": plugin_info.manifest.version},
            )

            # Phase 6B: Run code review (async, non-blocking)
            ai_quality = get_phase6_ai_quality()
            review_result = await ai_quality.review_plugin_code(plugin_info.path)
            if review_result.get("error"):
                logger.warning(
                    f"Code review for {plugin_info.manifest.plugin_id}: {review_result['error']}"
                )

            # Phase 6C: Run compliance scan
            compliance = get_phase6_compliance()
            compliance_result = await compliance.scan_compliance(plugin_info.path)
            if compliance_result.get("error"):
                logger.warning(
                    f"Compliance scan for {plugin_info.manifest.plugin_id}: {compliance_result['error']}"
                )

        except Exception as e:
            # Phase 6 integration errors should not block plugin loading
            logger.warning(f"Phase 6 integration error during load: {e}")

    # =========================================================================
    # P4-1: Signature Verification Integration
    # =========================================================================

    def verify_plugin_signature(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """
        Verify the cryptographic signature of a plugin.

        P4-1: Integrates signature verification into plugin install/update flow.

        Args:
            plugin_id: Plugin identifier
            require_signature: If True, unsigned plugins are rejected

        Returns:
            Dictionary with verification result:
            - verified: bool indicating if signature is valid
            - signed: bool indicating if plugin has a signature
            - key_id: signing key ID (if signed)
            - message: human-readable status
        """
        if not SIGNING_AVAILABLE or verify_package_auto is None:
            return {
                "verified": False,
                "signed": False,
                "key_id": "",
                "message": "Signing functionality not available",
                "error": not require_signature,  # Only error if required
            }

        plugin_info = self._plugins.get(plugin_id)
        if not plugin_info:
            return {
                "verified": False,
                "signed": False,
                "key_id": "",
                "message": f"Plugin not found: {plugin_id}",
                "error": True,
            }

        result = verify_package_auto(plugin_info.path)

        return {
            "verified": result.valid,
            "signed": bool(result.key_id),
            "key_id": result.key_id,
            "algorithm": result.algorithm,
            "signed_at": result.signed_at,
            "fingerprint": result.fingerprint,
            "message": result.message,
            "error": require_signature and not result.valid,
        }

    async def load_plugin_with_verification(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """
        Load a plugin with optional signature verification.

        P4-1: Full integration of signature verification into plugin load flow.

        Args:
            plugin_id: Plugin identifier
            require_signature: If True, reject unsigned/invalid plugins

        Returns:
            Dictionary with load result and verification status
        """
        result = {
            "loaded": False,
            "verification": None,
            "error": None,
        }

        # Verify signature before loading
        verification = self.verify_plugin_signature(plugin_id, require_signature)
        result["verification"] = verification

        if verification.get("error"):
            result["error"] = verification["message"]
            logger.warning(f"Plugin {plugin_id} rejected: {verification['message']}")
            return result

        # Log verification status
        if verification["signed"]:
            if verification["verified"]:
                logger.info(
                    f"Plugin {plugin_id} signature verified (key: {verification['key_id']})"
                )
            else:
                logger.warning(f"Plugin {plugin_id} signature invalid: {verification['message']}")
        else:
            logger.debug(f"Plugin {plugin_id} is unsigned")

        # Proceed with loading
        loaded = await self.load_plugin(plugin_id)
        result["loaded"] = loaded

        if not loaded:
            plugin_info = self._plugins.get(plugin_id)
            result["error"] = plugin_info.error_message if plugin_info else "Unknown error"

        return result

    async def activate_plugin(self, plugin_id: str) -> bool:
        """Activate a loaded plugin."""
        if plugin_id not in self._plugins:
            return False

        plugin_info = self._plugins[plugin_id]

        if plugin_info.state == PluginState.DISCOVERED:
            if not await self.load_plugin(plugin_id):
                return False

        if plugin_info.state != PluginState.LOADED and plugin_info.state != PluginState.DEACTIVATED:
            logger.warning(f"Cannot activate plugin in state: {plugin_info.state}")
            return False

        try:
            if plugin_info.instance:
                await plugin_info.instance.activate()

            plugin_info.state = PluginState.ACTIVATED

            # Phase 6D: Record activation event
            ecosystem = get_phase6_ecosystem()
            ecosystem.record_plugin_event(
                plugin_id,
                "plugin_activated",
                {"version": plugin_info.manifest.version},
            )

            logger.info(f"Activated plugin: {plugin_info.manifest.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to activate plugin {plugin_id}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return False

    async def deactivate_plugin(self, plugin_id: str) -> bool:
        """Deactivate an active plugin."""
        if plugin_id not in self._plugins:
            return False

        plugin_info = self._plugins[plugin_id]

        if plugin_info.state != PluginState.ACTIVATED:
            return False

        try:
            if plugin_info.instance:
                await plugin_info.instance.deactivate()

            plugin_info.state = PluginState.DEACTIVATED

            # Phase 6D: Record deactivation event
            ecosystem = get_phase6_ecosystem()
            ecosystem.record_plugin_event(
                plugin_id,
                "plugin_deactivated",
                {"version": plugin_info.manifest.version},
            )

            logger.info(f"Deactivated plugin: {plugin_info.manifest.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to deactivate plugin {plugin_id}: {e}")
            plugin_info.error_message = str(e)
            return False

    async def unload_plugin(self, plugin_id: str) -> bool:
        """Unload a plugin."""
        if plugin_id not in self._plugins:
            return False

        plugin_info = self._plugins[plugin_id]

        if plugin_info.state == PluginState.ACTIVATED:
            await self.deactivate_plugin(plugin_id)

        plugin_info.instance = None
        plugin_info.state = PluginState.DISCOVERED

        logger.info(f"Unloaded plugin: {plugin_info.manifest.name}")
        return True

    def get_plugin(self, plugin_id: str) -> PluginInfo | None:
        """Get plugin info by ID."""
        return self._plugins.get(plugin_id)

    def list_plugins(
        self,
        plugin_type: PluginType | None = None,
        state: PluginState | None = None,
    ) -> list[PluginInfo]:
        """List plugins with optional filtering."""
        plugins = list(self._plugins.values())

        if plugin_type:
            plugins = [p for p in plugins if p.manifest.plugin_type == plugin_type]

        if state:
            plugins = [p for p in plugins if p.state == state]

        return plugins

    def get_active_plugins(self, plugin_type: PluginType | None = None) -> list[PluginInfo]:
        """Get all activated plugins."""
        return self.list_plugins(plugin_type=plugin_type, state=PluginState.ACTIVATED)

    def get_engine_plugins(self) -> list[PluginInfo]:
        """Get all engine plugins."""
        return self.list_plugins(plugin_type=PluginType.ENGINE, state=PluginState.ACTIVATED)

    def get_processor_plugins(self) -> list[PluginInfo]:
        """Get all processor plugins."""
        return self.list_plugins(plugin_type=PluginType.PROCESSOR, state=PluginState.ACTIVATED)

    # =============================================================================
    # Phase 6A: Wasm Plugin Execution (W-5)
    # =============================================================================

    def is_wasm_plugin(self, plugin_id: str) -> bool:
        """Check if a plugin is a Wasm plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if the plugin is a Wasm plugin, False otherwise
        """
        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return False

        manifest = plugin_info.manifest
        # Check for Wasm runtime declaration in manifest
        if hasattr(manifest, "runtime") and manifest.runtime == "wasm":
            return True

        # Check for .wasm entry point
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

        # Check for .wasm file in plugin directory
        plugin_dir = self._plugins_dir / plugin_id
        if plugin_dir.exists():
            wasm_files = list(plugin_dir.glob("*.wasm"))
            if wasm_files:
                return True

        return False

    def get_wasm_path(self, plugin_id: str) -> Path | None:
        """Get the path to the Wasm binary for a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            Path to the Wasm file, or None if not found
        """
        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return None

        plugin_dir = self._plugins_dir / plugin_id

        # Check entry_points first
        manifest = plugin_info.manifest
        if hasattr(manifest, "entry_points"):
            entry_points = manifest.entry_points
            wasm_entry = None

            if isinstance(entry_points, dict):
                # Look for 'wasm', 'main', or any .wasm entry
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

        # Fall back to first .wasm file in directory
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
        """Execute a Wasm plugin function.

        Args:
            plugin_id: Plugin identifier
            function_name: Function to call in the Wasm module (default: main or _start)
            input_data: Input data to pass to the function
            capabilities: List of capability tokens to grant
            memory_limit_mb: Maximum memory in MB
            timeout_seconds: Execution timeout in seconds

        Returns:
            Execution result dictionary with keys:
                - success: bool
                - output: Any output data
                - error: Error message if failed
                - execution_time_ms: Execution time in milliseconds
        """
        # Check Wasm runtime availability
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

        # Get plugin info
        plugin_info = self.get_plugin(plugin_id)
        if plugin_info is None:
            return {
                "success": False,
                "error": f"Plugin '{plugin_id}' not found",
                "output": None,
                "execution_time_ms": 0,
            }

        # Verify it's a Wasm plugin
        if not self.is_wasm_plugin(plugin_id):
            return {
                "success": False,
                "error": f"Plugin '{plugin_id}' is not a Wasm plugin",
                "output": None,
                "execution_time_ms": 0,
            }

        # Get the Wasm file path
        wasm_path = self.get_wasm_path(plugin_id)
        if wasm_path is None or not wasm_path.exists():
            return {
                "success": False,
                "error": f"Wasm binary not found for plugin '{plugin_id}'",
                "output": None,
                "execution_time_ms": 0,
            }

        # Build capability set
        capability_set = None
        if capabilities and CapabilitySet is not None:
            capability_set = CapabilitySet()
            for cap in capabilities:
                capability_set.add(cap)

        # Create Wasm plugin config
        config = WasmPluginConfig(
            wasm_path=wasm_path,
            memory_limit_mb=memory_limit_mb,
            capabilities=capability_set,
        )

        # Create runner and execute
        import time

        start_time = time.perf_counter()

        try:
            runner = WasmRunner()
            result: WasmExecutionResult = runner.execute(config)

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
        """List all Wasm plugins.

        Returns:
            List of PluginInfo for all Wasm plugins
        """
        all_plugins = self.list_plugins()
        return [p for p in all_plugins if self.is_wasm_plugin(p.manifest.name)]

    # Settings management

    def get_plugin_setting(self, plugin_id: str, key: str, default: Any = None) -> Any:
        """Get a plugin setting."""
        if plugin_id not in self._settings:
            return default
        return self._settings[plugin_id].get(key, default)

    def set_plugin_setting(self, plugin_id: str, key: str, value: Any):
        """Set a plugin setting."""
        if plugin_id not in self._settings:
            self._settings[plugin_id] = {}
        self._settings[plugin_id][key] = value

        # Update plugin info
        if plugin_id in self._plugins:
            self._plugins[plugin_id].settings = self._settings[plugin_id]

        # Save settings (async)
        asyncio.create_task(self._save_settings())

    async def _load_settings(self):
        """Load plugin settings from file."""
        settings_path = self._plugins_dir / "settings.json"

        if settings_path.exists():
            try:
                with open(settings_path) as f:
                    self._settings = json.load(f)
            except Exception as e:
                logger.warning(f"Failed to load plugin settings: {e}")
                self._settings = {}

    async def _save_settings(self):
        """Save plugin settings to file."""
        settings_path = self._plugins_dir / "settings.json"

        try:
            with open(settings_path, "w") as f:
                json.dump(self._settings, f, indent=2)
        except Exception as e:
            logger.error(f"Failed to save plugin settings: {e}")

    # Extension points

    async def call_extension_point(
        self,
        point_name: str,
        *args,
        **kwargs,
    ) -> list[Any]:
        """Call all handlers registered for an extension point."""
        results = []

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


# Singleton instance
_plugin_service: PluginService | None = None


def get_plugin_service() -> PluginService:
    """Get or create the plugin service singleton."""
    global _plugin_service
    if _plugin_service is None:
        _plugin_service = PluginService()
    return _plugin_service


# =============================================================================
# Phase 6 Integration Classes
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
            return self._recommendation_engine.get_recommendations(user_id, limit)
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
            return self._featured_plugins.get_featured(limit)
        except Exception as e:
            logger.error(f"Failed to get featured plugins: {e}")
            return []


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
