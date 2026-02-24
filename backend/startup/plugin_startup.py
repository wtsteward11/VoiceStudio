"""Plugin Service Startup Task (Phase 4A Integration).

Registers PluginService initialization as a StartupTask so it is properly
managed within the application startup lifecycle.
"""
from __future__ import annotations

import logging
from pathlib import Path

from backend.startup.startup_service import StartupPhase, StartupTask

logger = logging.getLogger(__name__)


def create_plugin_startup_task() -> StartupTask:
    """Create the PluginService initialization startup task."""

    async def initialize_plugins() -> None:
        try:
            from backend.plugins.plugin_service import PluginService

            svc = PluginService(plugins_dir=Path("plugins"), enable_watcher=True)
            success = await svc.initialize()
            if not success:
                raise RuntimeError("PluginService.initialize() returned False")
            logger.info("PluginService initialized via startup task")
        except Exception as e:
            logger.warning("PluginService startup task failed (non-fatal): %s", e)

    return StartupTask(
        name="Initialize plugin service",
        phase=StartupPhase.LOADING_ENGINES,
        func=initialize_plugins,
        required=False,
        timeout=30.0,
    )
