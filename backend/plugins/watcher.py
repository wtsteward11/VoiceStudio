"""
Plugin File Watcher — filesystem monitoring for hot-reload.

Extracted from plugin_service.py monolith.
Monitors the plugins directory for changes to manifest files and Python
source files, debounces rapid edits, and triggers plugin hot-reloads.
"""

from __future__ import annotations

import asyncio
import logging
import threading
from pathlib import Path
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from backend.plugins.plugin_registry import PluginIndex

logger = logging.getLogger(__name__)

try:
    from watchdog.events import FileSystemEventHandler
    from watchdog.observers import Observer

    WATCHDOG_AVAILABLE = True
except ImportError:
    WATCHDOG_AVAILABLE = False
    Observer = None  # type: ignore[assignment,misc]
    FileSystemEventHandler = object  # type: ignore[assignment,misc]


class PluginFileWatcher(FileSystemEventHandler):  # type: ignore[misc]
    """File system watcher for hot-reloading plugins.

    Monitors the plugins directory for changes and triggers plugin reload
    when plugin files are modified.  Uses debouncing to avoid rapid-fire
    reloads during multi-file saves.

    Args:
        reload_callback: Async callable ``(plugin_id: str) -> bool`` invoked on change.
        plugin_index: PluginIndex used to resolve file paths to plugin IDs.
        plugins_dir: Root plugins directory being watched.
    """

    def __init__(
        self,
        reload_callback: Any,
        plugin_index: PluginIndex,
        plugins_dir: Path,
    ) -> None:
        if WATCHDOG_AVAILABLE:
            super().__init__()
        self._reload_callback = reload_callback
        self._plugin_index = plugin_index
        self._plugins_dir = plugins_dir
        self._observer: Any | None = None
        self._debounce_timers: dict[str, threading.Timer] = {}
        self._debounce_delay = 1.0
        self._running = False

    def start(self) -> bool:
        """Start watching the plugins directory.

        Returns:
            True if the watcher started successfully, False otherwise.
        """
        if not WATCHDOG_AVAILABLE:
            logger.warning("watchdog not installed - plugin hot-reload disabled")
            return False

        if self._running:
            return True

        try:
            self._observer = Observer()
            self._observer.schedule(self, str(self._plugins_dir), recursive=True)
            self._observer.start()
            self._running = True
            logger.info(f"Plugin watcher started for: {self._plugins_dir}")
            return True
        except Exception as e:
            logger.error(f"Failed to start plugin watcher: {e}")
            return False

    def stop(self) -> None:
        """Stop watching and cancel pending debounce timers."""
        if self._observer and self._running:
            self._observer.stop()
            self._observer.join(timeout=5.0)
            self._running = False
            logger.info("Plugin watcher stopped")

        for timer in self._debounce_timers.values():
            timer.cancel()
        self._debounce_timers.clear()

    def on_modified(self, event: Any) -> None:
        """Handle file modification events from watchdog."""
        if event.is_directory:
            return

        file_path = Path(event.src_path)

        if file_path.suffix not in (".json", ".py"):
            return

        plugin_id = self._find_plugin_id(file_path)
        if not plugin_id:
            return

        if plugin_id in self._debounce_timers:
            self._debounce_timers[plugin_id].cancel()

        timer = threading.Timer(self._debounce_delay, self._trigger_reload, args=[plugin_id])
        self._debounce_timers[plugin_id] = timer
        timer.start()

    def _find_plugin_id(self, file_path: Path) -> str | None:
        """Resolve a file path to the owning plugin's ID."""
        try:
            rel_path = file_path.relative_to(self._plugins_dir)
            plugin_folder = rel_path.parts[0] if rel_path.parts else None

            if plugin_folder:
                for plugin_id, info in self._plugin_index.plugins.items():
                    if info.path.name == plugin_folder:
                        return plugin_id
        except ValueError:
            pass  # ALLOWED: path may fall outside plugins_dir for symlinks

        return None

    def _trigger_reload(self, plugin_id: str) -> None:
        """Trigger plugin reload (runs in timer thread)."""
        if plugin_id in self._debounce_timers:
            del self._debounce_timers[plugin_id]

        logger.info(f"Hot-reloading plugin: {plugin_id}")

        try:
            loop = asyncio.get_event_loop()
            if loop.is_running():
                asyncio.run_coroutine_threadsafe(self._reload_callback(plugin_id), loop)
            else:
                asyncio.run(self._reload_callback(plugin_id))
        except Exception as e:
            logger.error(f"Failed to reload plugin {plugin_id}: {e}")
