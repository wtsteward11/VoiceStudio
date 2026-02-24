"""
Plugin Registry.

Task 3.3.3: Central registry for plugin management.
GAP-ARCH-001: Migrated imports from deprecated backend.plugins.core to app.core.plugins_api.
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from pathlib import Path
from typing import Any

from app.core.plugins_api import Plugin

# Use core PluginMetadata for loader compatibility
from backend.plugins.core.base import PluginMetadata as CorePluginMetadata

# PluginLoader remains in backend.plugins.core until full migration
from backend.plugins.core.loader import PluginLoader

# PluginState enum from plugin_service (canonical location)
from backend.plugins.plugin_service import PluginState

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
        self._metadata: dict[str, CorePluginMetadata] = {}
        self._plugin_states: dict[str, PluginState] = {}
        self._hooks: dict[str, list[Callable[..., Any]]] = {
            "on_load": [],
            "on_unload": [],
            "on_activate": [],
            "on_deactivate": [],
            "on_error": [],
        }

    def add_plugin_directory(self, path: Path) -> None:
        """Add a directory to scan for plugins."""
        self._loader.add_plugin_directory(path)

    async def discover_all(self) -> list[CorePluginMetadata]:
        """
        Discover all available plugins.

        Returns:
            List of discovered plugin metadata
        """
        discovered = self._loader.discover()

        for metadata in discovered:
            self._metadata[metadata.id] = metadata

        logger.info(f"Discovered {len(discovered)} plugins")
        return list(discovered)

    async def load(
        self,
        plugin_id: str,
        config: dict[str, Any] | None = None,
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
            self._plugin_states[plugin_id] = PluginState.LOADED
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
            plugin.cleanup()
            await self._loader.unload_plugin(plugin_id)
            del self._plugins[plugin_id]
            self._plugin_states.pop(plugin_id, None)
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

        current_state = self._plugin_states.get(plugin_id)
        if current_state == PluginState.ACTIVATED:
            return True

        try:
            if current_state == PluginState.LOADED:
                plugin.initialize()

            if await plugin.activate():
                self._plugin_states[plugin_id] = PluginState.ACTIVATED
                await self._fire_hooks("on_activate", plugin)
                return True
            return False

        except Exception as e:
            logger.error(f"Error activating plugin {plugin_id}: {e}")
            self._plugin_states[plugin_id] = PluginState.ERROR
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

        current_state = self._plugin_states.get(plugin_id)
        if current_state != PluginState.ACTIVATED:
            return True

        if await plugin.deactivate():
            self._plugin_states[plugin_id] = PluginState.DEACTIVATED
            await self._fire_hooks("on_deactivate", plugin)
            return True

        return False

    def get(self, plugin_id: str) -> Plugin | None:
        """Get a loaded plugin by ID."""
        return self._plugins.get(plugin_id)

    def get_metadata(self, plugin_id: str) -> CorePluginMetadata | None:
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
            plugins = [p for p in plugins if self._plugin_states.get(p.metadata.plugin_id) == state]

        return plugins

    def list_available(self) -> list[CorePluginMetadata]:
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
            for pid, p in self._plugins.items()
            if capability in (p.metadata.capabilities or {})
            and self._plugin_states.get(pid) == PluginState.ACTIVATED
        ]

    def add_hook(self, event: str, callback: Callable[..., Any]) -> None:
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

    def get_stats(self) -> dict[str, Any]:
        """Get registry statistics."""
        states: dict[str, int] = {}
        for plugin_id in self._plugins:
            ps = self._plugin_states.get(plugin_id)
            state_val = ps.value if ps else "unknown"
            states[state_val] = states.get(state_val, 0) + 1

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
