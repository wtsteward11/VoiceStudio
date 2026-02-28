"""
Testing helpers for plugin service contracts.

Provides stubs and mocks for use by plugins that require a PluginService-like
object when run standalone or in tests (e.g. ExporterPlugin, ProcessorPlugin).
"""

from __future__ import annotations

from typing import Any


class PluginServiceStub:
    """
    Minimal stub implementing get_plugin_setting and set_plugin_setting.

    Use when instantiating PluginBase-derived plugins (e.g. ExporterPlugin)
    outside the full PluginService lifecycle (e.g. in register() or tests).
    """

    def get_plugin_setting(self, plugin_id: str, key: str, default: Any = None) -> Any:
        return default

    def set_plugin_setting(self, plugin_id: str, key: str, value: Any) -> None:
        return None
