"""Base plugin class for VoiceStudio plugins."""

from __future__ import annotations

import logging
from abc import ABC, abstractmethod
from typing import Any

logger = logging.getLogger(__name__)


class VoiceStudioPlugin(ABC):
    """Base class for all VoiceStudio plugins.

    Subclass this to create a plugin. The lifecycle is:
    1. ``initialize()`` -- called once when the plugin is loaded
    2. Plugin is active and can receive calls
    3. ``cleanup()`` -- called when the plugin is unloaded

    Attributes:
        plugin_id: Unique identifier (reverse-domain, e.g. com.example.my-plugin)
        name: Human-readable name
        version: Semver version string
        description: Short description of the plugin
    """

    plugin_id: str = ""
    name: str = ""
    version: str = "0.1.0"
    description: str = ""

    @abstractmethod
    def initialize(self, context: dict[str, Any]) -> None:
        """Called when the plugin is loaded. Set up resources here.

        Args:
            context: Plugin context with host API access, config paths, etc.
        """

    @abstractmethod
    def cleanup(self) -> None:
        """Called when the plugin is unloaded. Release resources here."""

    def get_capabilities(self) -> list[str]:
        """Return list of capabilities this plugin declares.

        Override to declare: audio_read, audio_write, file_system, network, ui_panel.
        """
        return []

    def get_settings_schema(self) -> dict[str, Any]:
        """Return JSON schema for plugin settings. Override if plugin has settings."""
        return {}

    def on_settings_changed(self, settings: dict[str, Any]) -> None:
        """Called when plugin settings are updated by the user."""

    def health_check(self) -> dict[str, Any]:
        """Return health status. Override for custom health checks."""
        return {"status": "ready", "plugin_id": self.plugin_id}
