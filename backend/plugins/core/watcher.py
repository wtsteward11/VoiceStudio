"""
Plugin File Watcher for VoiceStudio (Phase 12.1.1)

Monitors the plugins/ directory for file changes and triggers
automatic plugin reload when modifications are detected.
Uses watchdog for cross-platform file system monitoring.

Integrates with SafePluginReloader (Phase 23.1) for safe reload handling.
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import Callable
from pathlib import Path
from typing import TYPE_CHECKING, Optional

if TYPE_CHECKING:
    from backend.plugins.core.safe_reload import SafePluginReloader

logger = logging.getLogger(__name__)

try:
    from watchdog.events import (
        FileCreatedEvent,
        FileDeletedEvent,
        FileModifiedEvent,
        FileMovedEvent,
        FileSystemEventHandler,
    )
    from watchdog.observers import Observer

    _HAS_WATCHDOG = True
except ImportError:
    _HAS_WATCHDOG = False


class PluginFileWatcher:
    """
    Monitors the plugins/ directory for changes.

    When a plugin file changes, it notifies registered handlers
    after a debounce period to avoid rapid-fire reloads.

    Optionally integrates with SafePluginReloader for automatic safe reload.
    """

    WATCH_EXTENSIONS = {".py", ".json", ".yaml", ".yml", ".toml"}
    DEFAULT_DEBOUNCE_MS = 500

    def __init__(
        self,
        plugins_dir: str = "plugins",
        debounce_ms: int = DEFAULT_DEBOUNCE_MS,
        safe_reloader: Optional[SafePluginReloader] = None,
        auto_reload: bool = True,
    ):
        self._plugins_dir = Path(plugins_dir).resolve()
        self._debounce_ms = debounce_ms
        self._observer = None
        self._handlers: list[Callable] = []
        self._pending_changes: dict[str, float] = {}
        self._debounce_task: asyncio.Task | None = None
        self._running = False
        self._safe_reloader = safe_reloader
        self._auto_reload = auto_reload

    def on_change(self, handler: Callable) -> None:
        """Register a handler for plugin file changes."""
        self._handlers.append(handler)

    def set_safe_reloader(self, reloader: SafePluginReloader) -> None:
        """Set the SafePluginReloader for automatic reload on changes."""
        self._safe_reloader = reloader

    @classmethod
    def create_with_reloader(
        cls,
        plugins_dir: str = "plugins",
        debounce_ms: int = DEFAULT_DEBOUNCE_MS,
    ) -> PluginFileWatcher:
        """
        Factory method to create a watcher with SafePluginReloader integration.

        Example:
            watcher = PluginFileWatcher.create_with_reloader("plugins")
            watcher.start()
        """
        from backend.plugins.core.safe_reload import SafePluginReloader

        reloader = SafePluginReloader()
        return cls(
            plugins_dir=plugins_dir,
            debounce_ms=debounce_ms,
            safe_reloader=reloader,
            auto_reload=True,
        )

    def start(self) -> bool:
        """Start watching the plugins directory."""
        if not _HAS_WATCHDOG:
            logger.warning(
                "watchdog not installed. Plugin hot-reload disabled. "
                "Install with: pip install watchdog"
            )
            return False

        if not self._plugins_dir.exists():
            logger.warning(f"Plugins directory not found: {self._plugins_dir}")
            return False

        try:
            handler = _PluginEventHandler(self._on_file_event)
            self._observer = Observer()
            self._observer.schedule(handler, str(self._plugins_dir), recursive=True)
            self._observer.start()
            self._running = True
            logger.info(f"Plugin watcher started: {self._plugins_dir}")
            return True
        except Exception as exc:
            logger.error(f"Failed to start plugin watcher: {exc}")
            return False

    def stop(self) -> None:
        """Stop watching."""
        self._running = False
        if self._observer:
            self._observer.stop()
            self._observer.join(timeout=5.0)
            self._observer = None
        if self._debounce_task:
            self._debounce_task.cancel()
        logger.info("Plugin watcher stopped")

    def _on_file_event(self, event_type: str, path: str) -> None:
        """Handle a file system event with debouncing."""
        file_path = Path(path)

        # Only watch relevant extensions
        if file_path.suffix not in self.WATCH_EXTENSIONS:
            return

        # Ignore __pycache__ and hidden files
        if "__pycache__" in str(file_path) or file_path.name.startswith("."):
            return

        # Determine plugin ID from path
        try:
            relative = file_path.relative_to(self._plugins_dir)
            plugin_id = relative.parts[0] if relative.parts else ""
        except ValueError:
            plugin_id = ""

        if plugin_id:
            self._pending_changes[plugin_id] = time.time()
            logger.debug(f"Plugin change detected: {plugin_id} ({event_type}: {file_path.name})")

            # Schedule debounced processing
            self._schedule_debounce()

    def _schedule_debounce(self) -> None:
        """Schedule a debounced change notification."""
        try:
            loop = asyncio.get_event_loop()
            if self._debounce_task and not self._debounce_task.done():
                self._debounce_task.cancel()
            self._debounce_task = loop.create_task(self._process_pending_changes())
        except RuntimeError:
            # No event loop -- process synchronously for testing
            pass

    async def _process_pending_changes(self) -> None:
        """Process pending changes after debounce period."""
        await asyncio.sleep(self._debounce_ms / 1000.0)

        if not self._pending_changes:
            return

        # Collect all plugin IDs that changed
        changed_plugins = set(self._pending_changes.keys())
        self._pending_changes.clear()

        # Auto-reload plugins using SafePluginReloader if configured (Phase 23.1)
        if self._auto_reload and self._safe_reloader is not None:
            for plugin_id in changed_plugins:
                try:
                    logger.info(f"Auto-reloading plugin: {plugin_id}")
                    result = await self._safe_reloader.reload_plugin(
                        plugin_id, str(self._plugins_dir)
                    )
                    if result.success:
                        logger.info(
                            f"Plugin reloaded successfully: {plugin_id} "
                            f"({result.duration_ms:.0f}ms)"
                        )
                    else:
                        logger.error(
                            f"Plugin reload failed: {plugin_id} - {result.error}"
                            + (" (rolled back)" if result.rolled_back else "")
                        )
                except Exception as exc:
                    logger.error(f"Safe reload failed for {plugin_id}: {exc}")

        # Notify handlers
        for handler in self._handlers:
            for plugin_id in changed_plugins:
                try:
                    if asyncio.iscoroutinefunction(handler):
                        await handler(plugin_id)
                    else:
                        handler(plugin_id)
                except Exception as exc:
                    logger.error(f"Plugin change handler failed for {plugin_id}: {exc}")


if _HAS_WATCHDOG:

    class _PluginEventHandler(FileSystemEventHandler):
        """Watchdog event handler that delegates to the watcher."""

        def __init__(self, callback: Callable):
            self._callback = callback

        def on_created(self, event):
            if not event.is_directory:
                self._callback("created", event.src_path)

        def on_modified(self, event):
            if not event.is_directory:
                self._callback("modified", event.src_path)

        def on_deleted(self, event):
            if not event.is_directory:
                self._callback("deleted", event.src_path)

        def on_moved(self, event):
            self._callback("moved", event.dest_path)
