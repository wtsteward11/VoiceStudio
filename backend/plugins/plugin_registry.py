"""
Plugin Registry — in-memory plugin index with settings persistence.

Extracted from plugin_service.py monolith.
Provides the central data store for plugin state, lookup, filtering,
and per-plugin settings management.

Note: This is distinct from ``backend.plugins.registry.PluginRegistry`` which wraps
the higher-level ``PluginLoader`` / ``Plugin`` (unified API).  This module manages the
lower-level ``PluginInfo`` index used by the ``PluginService`` facade.
"""

from __future__ import annotations

import asyncio
import json
import logging
from pathlib import Path
from typing import Any

from backend.plugins.plugin_service import (
    PluginInfo,
    PluginState,
    PluginType,
)

logger = logging.getLogger(__name__)


class PluginIndex:
    """In-memory plugin index with JSON-backed settings persistence.

    Owns the ``_plugins`` dictionary that all other extracted services reference.
    Provides lookup, filtering, and settings CRUD operations.
    """

    def __init__(self) -> None:
        self._plugins: dict[str, PluginInfo] = {}
        self._settings: dict[str, dict[str, Any]] = {}

    @property
    def plugins(self) -> dict[str, PluginInfo]:
        """Direct access to the plugins dictionary (for backward-compat wiring)."""
        return self._plugins

    @property
    def settings(self) -> dict[str, dict[str, Any]]:
        """Direct access to the settings dictionary."""
        return self._settings

    def register(self, plugin_id: str, info: PluginInfo) -> None:
        """Register a discovered plugin in the index.

        Args:
            plugin_id: Unique plugin identifier.
            info: Plugin info to store.
        """
        self._plugins[plugin_id] = info

    def unregister(self, plugin_id: str) -> bool:
        """Remove a plugin from the index.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True if the plugin was removed, False if not found.
        """
        if plugin_id in self._plugins:
            del self._plugins[plugin_id]
            return True
        return False

    def get_plugin(self, plugin_id: str) -> PluginInfo | None:
        """Get plugin info by ID.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            PluginInfo or None if not found.
        """
        return self._plugins.get(plugin_id)

    def list_plugins(
        self,
        plugin_type: PluginType | None = None,
        state: PluginState | None = None,
    ) -> list[PluginInfo]:
        """List plugins with optional filtering.

        Args:
            plugin_type: Filter by plugin type (engine, processor, etc.).
            state: Filter by lifecycle state.

        Returns:
            Filtered list of PluginInfo instances.
        """
        plugins = list(self._plugins.values())

        if plugin_type:
            plugins = [p for p in plugins if p.manifest.plugin_type == plugin_type]

        if state:
            plugins = [p for p in plugins if p.state == state]

        return plugins

    def get_active_plugins(self, plugin_type: PluginType | None = None) -> list[PluginInfo]:
        """Get all activated plugins, optionally filtered by type.

        Args:
            plugin_type: Optional type filter.

        Returns:
            List of activated PluginInfo instances.
        """
        return self.list_plugins(plugin_type=plugin_type, state=PluginState.ACTIVATED)

    def get_engine_plugins(self) -> list[PluginInfo]:
        """Get all active engine plugins."""
        return self.list_plugins(plugin_type=PluginType.ENGINE, state=PluginState.ACTIVATED)

    def get_processor_plugins(self) -> list[PluginInfo]:
        """Get all active processor plugins."""
        return self.list_plugins(plugin_type=PluginType.PROCESSOR, state=PluginState.ACTIVATED)

    def get_plugin_setting(self, plugin_id: str, key: str, default: Any = None) -> Any:
        """Get a plugin setting value.

        Args:
            plugin_id: Plugin identifier.
            key: Setting key.
            default: Default value if key is absent.

        Returns:
            Setting value or *default*.
        """
        if plugin_id not in self._settings:
            return default
        return self._settings[plugin_id].get(key, default)

    def set_plugin_setting(self, plugin_id: str, key: str, value: Any) -> None:
        """Set a plugin setting value and trigger async persistence.

        Args:
            plugin_id: Plugin identifier.
            key: Setting key.
            value: Value to store.
        """
        if plugin_id not in self._settings:
            self._settings[plugin_id] = {}
        self._settings[plugin_id][key] = value

        if plugin_id in self._plugins:
            self._plugins[plugin_id].settings = self._settings[plugin_id]

    async def load_settings(self, plugins_dir: Path) -> None:
        """Load plugin settings from ``settings.json`` on disk.

        Args:
            plugins_dir: Root plugins directory containing settings.json.
        """
        settings_path = plugins_dir / "settings.json"

        if settings_path.exists():
            try:
                with open(settings_path) as f:
                    self._settings = json.load(f)
            except Exception as e:
                logger.warning(f"Failed to load plugin settings: {e}")
                self._settings = {}

    async def save_settings(self, plugins_dir: Path) -> None:
        """Save plugin settings to ``settings.json`` on disk.

        Args:
            plugins_dir: Root plugins directory for settings.json.
        """
        settings_path = plugins_dir / "settings.json"

        try:
            with open(settings_path, "w") as f:
                json.dump(self._settings, f, indent=2)
        except Exception as e:
            logger.error(f"Failed to save plugin settings: {e}")

    def schedule_save(self, plugins_dir: Path) -> None:
        """Schedule an async save of settings (fire-and-forget).

        Args:
            plugins_dir: Root plugins directory for settings.json.
        """
        asyncio.create_task(self.save_settings(plugins_dir))
