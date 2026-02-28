"""
Plugin Loader.

Task 3.3.2: Plugin discovery and loading from directories.
Phase 23.2: Integrated version check to block incompatible plugins.
"""

from __future__ import annotations

import importlib
import importlib.util
import json
import logging
import sys
from pathlib import Path

# Import both unified Plugin (preferred) and deprecated Plugin for backward compatibility
from app.core.plugins_api import Plugin as UnifiedPlugin
from backend.plugins.core.base import Plugin as LegacyPlugin
from backend.plugins.core.base import PluginMetadata

# For type hints, prefer the unified Plugin
Plugin = UnifiedPlugin
from backend.plugins.core.version_check import APP_VERSION, check_plugin_compatibility

logger = logging.getLogger(__name__)


class PluginLoader:
    """
    Discovers and loads plugins from directories.

    Features:
    - Directory scanning for plugins
    - Manifest-based discovery
    - Dependency resolution
    - Hot reloading support
    """

    MANIFEST_FILE = "plugin.json"

    def __init__(self, plugin_dirs: list[Path] | None = None):
        """
        Initialize plugin loader.

        Args:
            plugin_dirs: Directories to scan for plugins
        """
        self._plugin_dirs = plugin_dirs or []
        self._loaded_modules: dict[str, any] = {}

    def add_plugin_directory(self, path: Path) -> None:
        """Add a directory to scan for plugins."""
        if path.exists() and path.is_dir():
            self._plugin_dirs.append(path)
            logger.info(f"Added plugin directory: {path}")

    def discover(self) -> list[PluginMetadata]:
        """
        Discover available plugins.

        Returns:
            List of plugin metadata
        """
        discovered = []

        for plugin_dir in self._plugin_dirs:
            for item in plugin_dir.iterdir():
                if item.is_dir():
                    manifest_path = item / self.MANIFEST_FILE
                    if manifest_path.exists():
                        try:
                            metadata = self._load_manifest(manifest_path)
                            discovered.append(metadata)
                            logger.debug(f"Discovered plugin: {metadata.id}")
                        except Exception as e:
                            logger.error(f"Failed to load manifest {manifest_path}: {e}")

        return discovered

    def _load_manifest(self, path: Path) -> PluginMetadata:
        """Load plugin manifest from JSON file."""
        with open(path) as f:
            data = json.load(f)

        return PluginMetadata.from_dict(data)

    async def load_plugin(
        self,
        plugin_id: str,
        config: dict | None = None,
        skip_version_check: bool = False,
    ) -> Plugin | None:
        """
        Load a plugin by ID.

        Args:
            plugin_id: Plugin identifier
            config: Optional configuration
            skip_version_check: Skip version compatibility check (not recommended)

        Returns:
            Loaded plugin instance or None
        """
        # Find plugin directory
        plugin_dir = self._find_plugin_dir(plugin_id)
        if not plugin_dir:
            logger.error(f"Plugin not found: {plugin_id}")
            return None

        try:
            # Load manifest
            manifest_path = plugin_dir / self.MANIFEST_FILE
            metadata = self._load_manifest(manifest_path)

            # Phase 23.2: Version compatibility check
            if not skip_version_check:
                manifest_dict = metadata.to_dict()
                compat_result = check_plugin_compatibility(manifest_dict)

                if not compat_result["compatible"]:
                    for error in compat_result["errors"]:
                        logger.error(f"Plugin {plugin_id} incompatible: {error}")
                    logger.error(
                        f"Plugin {plugin_id} blocked due to version incompatibility. "
                        f"Required: {manifest_dict.get('min_app_version', 'N/A')}, "
                        f"Current app: {APP_VERSION}"
                    )
                    return None

                for warning in compat_result.get("warnings", []):
                    logger.warning(f"Plugin {plugin_id}: {warning}")

            # Load the plugin module
            module = self._load_module(plugin_dir, plugin_id)
            if not module:
                return None

            # Find the plugin class
            plugin_class = self._find_plugin_class(module)
            if not plugin_class:
                logger.error(f"No Plugin class found in {plugin_id}")
                return None

            # Create instance based on plugin type
            if issubclass(plugin_class, UnifiedPlugin):
                # Unified Plugin (Phase 4+) takes plugin_dir
                plugin = plugin_class(plugin_dir)
                # Apply config if supported
                if hasattr(plugin, "update_config") and config:
                    plugin.update_config(config)
                # Initialize the plugin
                plugin.initialize()
                logger.info(f"Loaded plugin: {plugin_id}")
                return plugin
            else:
                # Legacy Plugin takes config dict
                merged_config = {**metadata.default_config, **(config or {})}
                plugin = plugin_class(merged_config)

                # Load the plugin (legacy async lifecycle)
                if await plugin.load():
                    logger.info(f"Loaded plugin: {plugin_id}")
                    return plugin
                else:
                    logger.error(f"Plugin load failed: {plugin_id}")
                    return None

        except Exception as e:
            logger.exception(f"Error loading plugin {plugin_id}: {e}")
            return None

    def _find_plugin_dir(self, plugin_id: str) -> Path | None:
        """Find the directory for a plugin."""
        for plugin_dir in self._plugin_dirs:
            candidate = plugin_dir / plugin_id
            if candidate.exists() and (candidate / self.MANIFEST_FILE).exists():
                return candidate
        return None

    def _load_module(self, plugin_dir: Path, plugin_id: str) -> any | None:
        """Load the plugin Python module."""
        module_path = plugin_dir / "__init__.py"

        if not module_path.exists():
            # Try plugin.py as fallback
            module_path = plugin_dir / "plugin.py"

        if not module_path.exists():
            logger.error(f"No module file found for plugin {plugin_id}")
            return None

        try:
            spec = importlib.util.spec_from_file_location(
                f"voicestudio_plugins.{plugin_id}",
                module_path,
            )

            if spec and spec.loader:
                module = importlib.util.module_from_spec(spec)
                sys.modules[spec.name] = module
                spec.loader.exec_module(module)
                self._loaded_modules[plugin_id] = module
                return module

        except Exception as e:
            logger.exception(f"Failed to load module for {plugin_id}: {e}")

        return None

    def _find_plugin_class(self, module: any) -> type[UnifiedPlugin] | type[LegacyPlugin] | None:
        """Find the Plugin subclass in a module.

        Supports both unified Plugin (Phase 4+) and legacy Plugin (deprecated).
        Prefers unified Plugin if both are found.
        """
        unified_class = None
        legacy_class = None

        for name in dir(module):
            obj = getattr(module, name)
            if not isinstance(obj, type):
                continue

            # Check for unified Plugin (preferred)
            if issubclass(obj, UnifiedPlugin) and obj is not UnifiedPlugin:
                unified_class = obj

            # Check for legacy Plugin (deprecated)
            if issubclass(obj, LegacyPlugin) and obj is not LegacyPlugin:
                legacy_class = obj

        # Prefer unified Plugin
        return unified_class or legacy_class

    async def unload_plugin(self, plugin_id: str) -> bool:
        """
        Unload a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if unloaded successfully
        """
        module_name = f"voicestudio_plugins.{plugin_id}"

        if module_name in sys.modules:
            del sys.modules[module_name]
            logger.info(f"Unloaded plugin: {plugin_id}")

        if plugin_id in self._loaded_modules:
            del self._loaded_modules[plugin_id]

        return True

    async def reload_plugin(
        self,
        plugin_id: str,
        config: dict | None = None,
        skip_version_check: bool = False,
    ) -> Plugin | None:
        """
        Reload a plugin (hot reload).

        Args:
            plugin_id: Plugin identifier
            config: Optional new configuration
            skip_version_check: Skip version compatibility check

        Returns:
            Reloaded plugin instance
        """
        await self.unload_plugin(plugin_id)
        return await self.load_plugin(plugin_id, config, skip_version_check)

    def get_loaded_modules(self) -> list[str]:
        """Get list of loaded plugin module names."""
        return list(self._loaded_modules.keys())
