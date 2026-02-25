"""
Lifecycle Manager — per-plugin state machine.

Extracted from plugin_service.py monolith.
Manages the DISCOVERED -> LOADED -> ACTIVATED lifecycle transitions,
plugin module loading/instantiation, and hot-reload orchestration.
"""

from __future__ import annotations

import importlib
import importlib.util
import logging
from datetime import datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any

from backend.plugins.plugin_service import (
    EnginePlugin,
    ExporterPlugin,
    ImporterPlugin,
    PluginBase,
    PluginInfo,
    PluginState,
    ProcessorPlugin,
)

if TYPE_CHECKING:
    from backend.plugins.manifest_service import ManifestService
    from backend.plugins.plugin_registry import PluginIndex

logger = logging.getLogger(__name__)

# Phase 6 lazy-init singletons (kept module-private to avoid circular imports)
_phase6_ai_quality: Any = None
_phase6_compliance: Any = None
_phase6_ecosystem: Any = None


def _get_phase6_ai_quality() -> Any:
    """Get or create the Phase 6B AI Quality integration."""
    global _phase6_ai_quality
    if _phase6_ai_quality is None:
        from backend.plugins.plugin_service import Phase6AIQuality

        _phase6_ai_quality = Phase6AIQuality()
    return _phase6_ai_quality


def _get_phase6_compliance() -> Any:
    """Get or create the Phase 6C Compliance integration."""
    global _phase6_compliance
    if _phase6_compliance is None:
        from backend.plugins.plugin_service import Phase6Compliance

        _phase6_compliance = Phase6Compliance()
    return _phase6_compliance


def _get_phase6_ecosystem() -> Any:
    """Get or create the Phase 6D Ecosystem integration."""
    global _phase6_ecosystem
    if _phase6_ecosystem is None:
        from backend.plugins.plugin_service import Phase6Ecosystem

        _phase6_ecosystem = Phase6Ecosystem()
    return _phase6_ecosystem


class LifecycleManager:
    """Per-plugin state machine managing DISCOVERED -> LOADED -> ACTIVATED transitions.

    Responsible for:
    - Loading plugin Python modules and instantiating plugin classes
    - Activating / deactivating plugin instances
    - Unloading plugins and resetting state
    - Hot-reload orchestration (unload -> re-discover -> reload)
    - Phase 6 integration hooks (code review, compliance, analytics)
    """

    def __init__(
        self,
        plugin_index: PluginIndex,
        manifest_service: ManifestService,
    ) -> None:
        self._index = plugin_index
        self._manifest = manifest_service

    async def load_plugin(self, plugin_id: str) -> bool:
        """Load a plugin module and instantiate the plugin class.

        Transitions the plugin from DISCOVERED to LOADED.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True on success, False on failure (state set to ERROR).
        """
        plugin_info = self._index.get_plugin(plugin_id)
        if plugin_info is None:
            logger.error(f"Plugin not found: {plugin_id}")
            return False

        try:
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

            from app.core.plugins_api import Plugin as UnifiedPlugin

            plugin_class: type | None = None
            uses_unified_plugin = False

            for name in dir(module):
                obj = getattr(module, name)
                if not isinstance(obj, type):
                    continue

                if issubclass(obj, UnifiedPlugin) and obj is not UnifiedPlugin:
                    plugin_class = obj
                    uses_unified_plugin = True
                    break

                if (
                    issubclass(obj, PluginBase)
                    and obj is not PluginBase
                    and obj not in (EnginePlugin, ProcessorPlugin, ExporterPlugin, ImporterPlugin)
                ):
                    plugin_class = obj
                    break

            if plugin_class is None:
                raise TypeError(f"No plugin class found in {entry_point}")

            if uses_unified_plugin:
                plugin_info.instance = plugin_class(plugin_info.path)
            else:
                # Legacy PluginBase expects a PluginService-like object; pass the index
                # which implements get_plugin_setting / set_plugin_setting.
                plugin_info.instance = plugin_class(self._index)
            plugin_info.state = PluginState.LOADED
            plugin_info.loaded_at = datetime.now()

            await self._run_phase6_on_load(plugin_info)

            logger.info(f"Loaded plugin: {plugin_info.manifest.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to load plugin {plugin_id}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return False

    async def unload_plugin(self, plugin_id: str) -> bool:
        """Unload a plugin, deactivating first if needed.

        Transitions the plugin back to DISCOVERED state.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True on success, False if plugin not found.
        """
        plugin_info = self._index.get_plugin(plugin_id)
        if plugin_info is None:
            return False

        if plugin_info.state == PluginState.ACTIVATED:
            await self.deactivate_plugin(plugin_id)

        plugin_info.instance = None
        plugin_info.state = PluginState.DISCOVERED

        logger.info(f"Unloaded plugin: {plugin_info.manifest.name}")
        return True

    async def activate_plugin(self, plugin_id: str) -> bool:
        """Activate a loaded plugin.

        If the plugin is in DISCOVERED state, it will be loaded first.
        Transitions from LOADED or DEACTIVATED to ACTIVATED.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True on success, False on failure.
        """
        plugin_info = self._index.get_plugin(plugin_id)
        if plugin_info is None:
            return False

        if plugin_info.state == PluginState.DISCOVERED:
            if not await self.load_plugin(plugin_id):
                return False

        if plugin_info.state not in (PluginState.LOADED, PluginState.DEACTIVATED):
            logger.warning(f"Cannot activate plugin in state: {plugin_info.state}")
            return False

        try:
            if plugin_info.instance:
                await plugin_info.instance.activate()

            plugin_info.state = PluginState.ACTIVATED

            ecosystem = _get_phase6_ecosystem()
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
        """Deactivate an active plugin.

        Transitions from ACTIVATED to DEACTIVATED.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True on success, False if not currently active.
        """
        plugin_info = self._index.get_plugin(plugin_id)
        if plugin_info is None:
            return False

        if plugin_info.state != PluginState.ACTIVATED:
            return False

        try:
            if plugin_info.instance:
                await plugin_info.instance.deactivate()

            plugin_info.state = PluginState.DEACTIVATED

            ecosystem = _get_phase6_ecosystem()
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

    async def reload_plugin(self, plugin_id: str) -> bool:
        """Hot-reload a plugin: unload, re-read manifest, and reload.

        If the plugin was previously activated, it is re-activated after reload.

        Args:
            plugin_id: Unique plugin identifier.

        Returns:
            True on success, False on failure (state set to ERROR).
        """
        plugin_info = self._index.get_plugin(plugin_id)
        if plugin_info is None:
            logger.warning(f"Cannot reload unknown plugin: {plugin_id}")
            return False

        was_activated = plugin_info.state == PluginState.ACTIVATED

        try:
            await self.unload_plugin(plugin_id)

            new_manifest = self._manifest.reload_manifest(plugin_info)
            if new_manifest is not None:
                plugin_info.manifest = new_manifest

            if was_activated:
                success = await self.activate_plugin(plugin_id)
                if success:
                    logger.info(f"Hot-reloaded and reactivated plugin: {plugin_id}")
                return success
            else:
                success = await self.load_plugin(plugin_id)
                if success:
                    logger.info(f"Hot-reloaded plugin: {plugin_id}")
                return success

        except Exception as e:
            logger.error(f"Failed to reload plugin {plugin_id}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return False

    async def _run_phase6_on_load(self, plugin_info: PluginInfo) -> None:
        """Run Phase 6 integrations when a plugin is loaded.

        Non-blocking: Phase 6 failures do not prevent plugin loading.
        """
        try:
            ecosystem = _get_phase6_ecosystem()
            ecosystem.record_plugin_event(
                plugin_info.manifest.plugin_id,
                "plugin_loaded",
                {"version": plugin_info.manifest.version},
            )

            ai_quality = _get_phase6_ai_quality()
            review_result = await ai_quality.review_plugin_code(plugin_info.path)
            if review_result.get("error"):
                logger.warning(
                    f"Code review for {plugin_info.manifest.plugin_id}: {review_result['error']}"
                )

            compliance = _get_phase6_compliance()
            compliance_result = await compliance.scan_compliance(plugin_info.path)
            if compliance_result.get("error"):
                logger.warning(
                    f"Compliance scan for {plugin_info.manifest.plugin_id}: "
                    f"{compliance_result['error']}"
                )

        except Exception as e:
            logger.warning(f"Phase 6 integration error during load: {e}")
