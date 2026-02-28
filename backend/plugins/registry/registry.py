"""
Plugin Registry.

Task 3.3.3: Central registry for plugin management.
GAP-ARCH-001: Migrated imports from deprecated backend.plugins.core to app.core.plugins_api.
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from pathlib import Path

# GAP-ARCH-001: Import from app.core.plugins_api (ADR-038 migration)
from app.core.plugins_api import Plugin, PluginMetadata

# PluginLoader remains in backend.plugins.core until full migration
from backend.plugins.core.loader import PluginLoader

# PluginState enum from plugin_service (canonical location)
from backend.services.plugin_service import PluginState

logger = logging.getLogger(__name__)


class PluginRegistry:
    """
    Central registry for managing plugins.

    Features:
    - Plugin lifecycle management
    - Capability-based lookup
    - Event hooks for plugin changes
    - Configuration persistence
    """

    def __init__(self, plugin_dirs: list[Path] | None = None):
        """
        Initialize plugin registry.

        Args:
            plugin_dirs: Directories to scan for plugins
        """
        self._loader = PluginLoader(plugin_dirs)
        self._plugins: dict[str, Plugin] = {}
        self._metadata: dict[str, PluginMetadata] = {}
        self._hooks: dict[str, list[Callable]] = {
            "on_load": [],
            "on_unload": [],
            "on_activate": [],
            "on_deactivate": [],
            "on_error": [],
        }

    def add_plugin_directory(self, path: Path) -> None:
        """Add a directory to scan for plugins."""
        self._loader.add_plugin_directory(path)

    async def discover_all(self) -> list[PluginMetadata]:
        """
        Discover all available plugins.

        Returns:
            List of discovered plugin metadata
        """
        discovered = self._loader.discover()

        for metadata in discovered:
            self._metadata[metadata.id] = metadata

        logger.info(f"Discovered {len(discovered)} plugins")
        return discovered

    async def load(
        self,
        plugin_id: str,
        config: dict | None = None,
    ) -> Plugin | None:
        """
        Load a plugin.

        Args:
            plugin_id: Plugin identifier
            config: Optional configuration

        Returns:
            Loaded plugin instance
        """
        if plugin_id in self._plugins:
            logger.warning(f"Plugin already loaded: {plugin_id}")
            return self._plugins[plugin_id]

        plugin = await self._loader.load_plugin(plugin_id, config)

        if plugin:
            self._plugins[plugin_id] = plugin
            await self._fire_hooks("on_load", plugin)

        return plugin

    async def unload(self, plugin_id: str) -> bool:
        """
        Unload a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if unloaded successfully
        """
        plugin = self._plugins.get(plugin_id)

        if not plugin:
            return False

        try:
            await plugin.unload()
            await self._loader.unload_plugin(plugin_id)
            del self._plugins[plugin_id]
            await self._fire_hooks("on_unload", plugin)
            return True

        except Exception as e:
            logger.error(f"Error unloading plugin {plugin_id}: {e}")
            return False

    async def activate(self, plugin_id: str) -> bool:
        """
        Activate a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if activated successfully
        """
        plugin = self._plugins.get(plugin_id)

        if not plugin:
            return False

        if plugin.state == PluginState.ACTIVE:
            return True

        try:
            if plugin.state == PluginState.LOADED:
                await plugin.initialize()

            if await plugin.activate():
                await self._fire_hooks("on_activate", plugin)
                return True
            return False

        except Exception as e:
            logger.error(f"Error activating plugin {plugin_id}: {e}")
            plugin.set_error(str(e))
            await self._fire_hooks("on_error", plugin)
            return False

    async def deactivate(self, plugin_id: str) -> bool:
        """
        Deactivate a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if deactivated successfully
        """
        plugin = self._plugins.get(plugin_id)

        if not plugin:
            return False

        if plugin.state != PluginState.ACTIVE:
            return True

        if await plugin.deactivate():
            await self._fire_hooks("on_deactivate", plugin)
            return True

        return False

    def get(self, plugin_id: str) -> Plugin | None:
        """Get a loaded plugin by ID."""
        return self._plugins.get(plugin_id)

    def get_metadata(self, plugin_id: str) -> PluginMetadata | None:
        """Get plugin metadata by ID."""
        return self._metadata.get(plugin_id)

    def list_plugins(
        self,
        state: PluginState | None = None,
    ) -> list[Plugin]:
        """
        List loaded plugins.

        Args:
            state: Optional filter by state

        Returns:
            List of plugins
        """
        plugins = list(self._plugins.values())

        if state:
            plugins = [p for p in plugins if p.state == state]

        return plugins

    def list_available(self) -> list[PluginMetadata]:
        """List all discovered plugin metadata."""
        return list(self._metadata.values())

    def find_by_capability(self, capability: str) -> list[Plugin]:
        """
        Find plugins with a specific capability.

        Args:
            capability: Capability to search for

        Returns:
            List of plugins with the capability
        """
        return [
            p
            for p in self._plugins.values()
            if p.has_capability(capability) and p.state == PluginState.ACTIVE
        ]

    def add_hook(self, event: str, callback: Callable) -> None:
        """
        Add a hook for plugin events.

        Args:
            event: Event name (on_load, on_unload, etc.)
            callback: Callback function
        """
        if event in self._hooks:
            self._hooks[event].append(callback)

    async def _fire_hooks(self, event: str, plugin: Plugin) -> None:
        """Fire all hooks for an event."""
        for callback in self._hooks.get(event, []):
            try:
                result = callback(plugin)
                if hasattr(result, "__await__"):
                    await result
            except Exception as e:
                logger.error(f"Hook error for {event}: {e}")

    def get_stats(self) -> dict:
        """Get registry statistics."""
        states = {}
        for plugin in self._plugins.values():
            state = plugin.state.value
            states[state] = states.get(state, 0) + 1

        return {
            "discovered": len(self._metadata),
            "loaded": len(self._plugins),
            "by_state": states,
        }


# Global registry instance
_registry: PluginRegistry | None = None


def get_plugin_registry() -> PluginRegistry:
    """Get or create the global plugin registry."""
    global _registry
    if _registry is None:
        _registry = PluginRegistry()
    return _registry
