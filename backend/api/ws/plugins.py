"""
Plugin WebSocket Handler

Phase 1: Real-time plugin state synchronization between backend and frontend.

Provides WebSocket endpoint for:
- Broadcasting plugin state changes to connected clients
- Receiving plugin commands from frontend
- Full state synchronization on connect
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime
from typing import Any

from fastapi import WebSocket, WebSocketDisconnect

from backend.api.ws.protocol import (
    MessageType,
    create_ack,
    create_data,
    create_error,
    create_message,
    generate_request_id,
)

logger = logging.getLogger(__name__)

# Connected WebSocket clients for plugin sync (explicit set for reliable lifecycle)
_connected_clients: set[WebSocket] = set()
_clients_lock = asyncio.Lock()


class PluginSyncAction:
    """Plugin synchronization action constants."""

    SYNC_REQUEST = "sync_request"
    SYNC_ALL = "sync_all"
    STATE_CHANGED = "state_changed"
    PLUGIN_ADDED = "plugin_added"
    PLUGIN_REMOVED = "plugin_removed"
    ENABLE_REQUEST = "enable_request"
    DISABLE_REQUEST = "disable_request"
    PERMISSION_CHANGED = "permission_changed"


class PluginCommand:
    """Plugin command constants."""

    ENABLE = "enable"
    DISABLE = "disable"
    RELOAD = "reload"
    HEALTH_CHECK = "health_check"
    INSTALL = "install"
    UNINSTALL = "uninstall"


async def register_client(websocket: WebSocket) -> None:
    """Register a WebSocket client for plugin sync."""
    async with _clients_lock:
        _connected_clients.add(websocket)
    logger.debug(f"Plugin sync client registered, total: {len(_connected_clients)}")


async def unregister_client(websocket: WebSocket) -> None:
    """Unregister a WebSocket client."""
    async with _clients_lock:
        _connected_clients.discard(websocket)
    logger.debug("Plugin sync client unregistered, total: %s", len(_connected_clients))


async def broadcast_plugin_state(
    action: str,
    plugin_id: str | None = None,
    status: dict[str, Any] | None = None,
    all_plugins: list[dict[str, Any]] | None = None,
) -> None:
    """
    Broadcast plugin state update to all connected clients.

    Args:
        action: The sync action (see PluginSyncAction)
        plugin_id: Target plugin ID (optional)
        status: Plugin status dict (optional)
        all_plugins: List of all plugin statuses for sync_all (optional)
    """
    message = create_plugin_sync_message(
        action=action,
        plugin_id=plugin_id,
        status=status,
        all_plugins=all_plugins,
    )

    # Get clients snapshot under lock
    async with _clients_lock:
        clients = list(_connected_clients)

    # Broadcast without holding lock; remove dead clients
    dead = []
    for client in clients:
        try:
            await client.send_json(message)
        except Exception as e:
            logger.warning("Failed to send plugin sync to client: %s", e)
            dead.append(client)
    if dead:
        async with _clients_lock:
            for c in dead:
                _connected_clients.discard(c)


def create_plugin_sync_message(
    action: str,
    plugin_id: str | None = None,
    status: dict[str, Any] | None = None,
    all_plugins: list[dict[str, Any]] | None = None,
    request_id: str | None = None,
) -> dict[str, Any]:
    """Create a plugin synchronization message."""
    payload: dict[str, Any] = {"action": action}

    if plugin_id is not None:
        payload["plugin_id"] = plugin_id

    if status is not None:
        payload["status"] = status

    if all_plugins is not None:
        payload["all_plugins"] = all_plugins

    payload["timestamp"] = datetime.utcnow().isoformat() + "Z"

    return create_message("plugin_sync", payload, request_id=request_id)


async def handle_plugin_command(
    websocket: WebSocket,
    command: str,
    plugin_id: str,
    parameters: dict[str, Any] | None = None,
    request_id: str | None = None,
) -> None:
    """
    Handle a plugin command from the frontend.

    Args:
        websocket: Client WebSocket connection
        command: Command to execute
        plugin_id: Target plugin ID
        parameters: Optional command parameters
        request_id: Request correlation ID
    """
    # Import here to avoid circular imports
    from backend.plugins.plugin_service import get_plugin_service

    logger.info(f"Plugin command: {command} for {plugin_id}")

    try:
        plugin_service = get_plugin_service()

        if command == PluginCommand.ENABLE:
            success = await _enable_plugin(plugin_service, plugin_id)
            await _send_command_response(
                websocket,
                success,
                plugin_id,
                "Plugin enabled" if success else "Failed to enable plugin",
                request_id,
            )

        elif command == PluginCommand.DISABLE:
            success = await _disable_plugin(plugin_service, plugin_id)
            await _send_command_response(
                websocket,
                success,
                plugin_id,
                "Plugin disabled" if success else "Failed to disable plugin",
                request_id,
            )

        elif command == PluginCommand.RELOAD:
            success = await _reload_plugin(plugin_service, plugin_id)
            await _send_command_response(
                websocket,
                success,
                plugin_id,
                "Plugin reloaded" if success else "Failed to reload plugin",
                request_id,
            )

        elif command == PluginCommand.HEALTH_CHECK:
            health = await _check_plugin_health(plugin_service, plugin_id)
            response = create_message(
                "plugin_command_response",
                {
                    "success": True,
                    "plugin_id": plugin_id,
                    "health_status": health,
                },
                request_id=request_id,
            )
            await websocket.send_json(response)

        else:
            await websocket.send_json(
                create_error(
                    f"Unknown command: {command}", code="INVALID_COMMAND", request_id=request_id
                )
            )

    except Exception as e:
        logger.exception(f"Error handling plugin command {command}: {e}")
        await websocket.send_json(create_error(str(e), code="COMMAND_ERROR", request_id=request_id))


async def _enable_plugin(plugin_service: Any, plugin_id: str) -> bool:
    """Enable a plugin via the plugin service."""
    try:
        if hasattr(plugin_service, "enable_plugin"):
            await plugin_service.enable_plugin(plugin_id)
            return True
        # Fallback: try to load the plugin
        if hasattr(plugin_service, "load_plugin"):
            await plugin_service.load_plugin(plugin_id)
            return True
        logger.warning(f"Plugin service does not support enable: {plugin_id}")
        return False
    except Exception as e:
        logger.error(f"Failed to enable plugin {plugin_id}: {e}")
        return False


async def _disable_plugin(plugin_service: Any, plugin_id: str) -> bool:
    """Disable a plugin via the plugin service."""
    try:
        if hasattr(plugin_service, "disable_plugin"):
            await plugin_service.disable_plugin(plugin_id)
            return True
        # Fallback: try to unload the plugin
        if hasattr(plugin_service, "unload_plugin"):
            await plugin_service.unload_plugin(plugin_id)
            return True
        logger.warning(f"Plugin service does not support disable: {plugin_id}")
        return False
    except Exception as e:
        logger.error(f"Failed to disable plugin {plugin_id}: {e}")
        return False


async def _reload_plugin(plugin_service: Any, plugin_id: str) -> bool:
    """Reload a plugin via the plugin service."""
    try:
        if hasattr(plugin_service, "reload_plugin"):
            await plugin_service.reload_plugin(plugin_id)
            return True
        logger.warning(f"Plugin service does not support reload: {plugin_id}")
        return False
    except Exception as e:
        logger.error(f"Failed to reload plugin {plugin_id}: {e}")
        return False


async def _check_plugin_health(plugin_service: Any, plugin_id: str) -> str:
    """Check plugin health status."""
    try:
        if hasattr(plugin_service, "check_plugin_health"):
            return str(await plugin_service.check_plugin_health(plugin_id))
        return "unknown"
    except Exception as e:
        logger.error(f"Health check failed for plugin {plugin_id}: {e}")
        return "error"


async def _send_command_response(
    websocket: WebSocket,
    success: bool,
    plugin_id: str,
    message: str,
    request_id: str | None,
) -> None:
    """Send a command response to the client."""
    response = create_message(
        "plugin_command_response",
        {
            "success": success,
            "plugin_id": plugin_id,
            "message": message,
        },
        request_id=request_id,
    )
    await websocket.send_json(response)


async def send_full_sync(websocket: WebSocket, request_id: str | None = None) -> None:
    """
    Send full plugin state synchronization to a client.

    Args:
        websocket: Client WebSocket connection
        request_id: Request correlation ID
    """
    from backend.plugins.plugin_service import get_plugin_service

    try:
        plugin_service = get_plugin_service()
        plugins = plugin_service.list_plugins()

        all_statuses = []
        for plugin_info in plugins:
            status = _create_plugin_status(
                plugin_info.manifest.plugin_id, plugin_info.manifest, plugin_service
            )
            all_statuses.append(status)

        message = create_plugin_sync_message(
            action=PluginSyncAction.SYNC_ALL,
            all_plugins=all_statuses,
            request_id=request_id,
        )
        await websocket.send_json(message)
        logger.debug(f"Sent full sync with {len(all_statuses)} plugins")

    except Exception as e:
        logger.error(f"Error sending full sync: {e}")
        await websocket.send_json(create_error(str(e), code="SYNC_ERROR", request_id=request_id))


def _create_plugin_status(plugin_id: str, manifest: Any, plugin_service: Any) -> dict[str, Any]:
    """Create a plugin status dictionary for sync messages."""
    # Determine state
    if hasattr(plugin_service, "is_plugin_loaded") and plugin_service.is_plugin_loaded(plugin_id):
        state = "active"
    elif hasattr(plugin_service, "is_plugin_enabled") and not plugin_service.is_plugin_enabled(
        plugin_id
    ):
        state = "disabled"
    else:
        state = "discovered"

    # Get error if any
    error_message = None
    if hasattr(plugin_service, "get_plugin_error"):
        error_message = plugin_service.get_plugin_error(plugin_id)
        if error_message:
            state = "error"

    return {
        "plugin_id": plugin_id,
        "state": state,
        "version": getattr(manifest, "version", "0.0.0"),
        "backend_loaded": state == "active",
        "frontend_loaded": False,  # Updated by frontend
        "error_message": error_message,
        "last_updated": datetime.utcnow().isoformat() + "Z",
        "granted_permissions": getattr(manifest, "permissions", []),
        "health_status": "healthy" if state == "active" else None,
    }


async def plugin_websocket_handler(websocket: WebSocket) -> None:
    """
    WebSocket handler for plugin synchronization.

    Protocol:
    - On connect: Sends full sync automatically
    - Client can send:
      - {"type": "sync_request"}: Request full sync
      - {"type": "plugin_command", "command": "...", "plugin_id": "..."}: Execute command
      - {"type": "ping"}: Heartbeat
    - Server sends:
      - {"type": "plugin_sync", "action": "..."}: State updates
      - {"type": "plugin_command_response", ...}: Command results
      - {"type": "pong"}: Heartbeat response
    """
    await websocket.accept()
    await register_client(websocket)

    try:
        # Send initial full sync
        await send_full_sync(websocket, request_id=generate_request_id())

        # Message loop
        while True:
            data = await websocket.receive_json()
            msg_type = data.get("type", "")

            if msg_type == "ping":
                await websocket.send_json(create_message("pong"))

            elif msg_type == "sync_request":
                await send_full_sync(websocket, request_id=data.get("request_id"))

            elif msg_type == "plugin_command":
                await handle_plugin_command(
                    websocket,
                    command=data.get("command", ""),
                    plugin_id=data.get("plugin_id", ""),
                    parameters=data.get("parameters"),
                    request_id=data.get("request_id"),
                )

            else:
                await websocket.send_json(
                    create_error(f"Unknown message type: {msg_type}", code="INVALID_MESSAGE")
                )

    except WebSocketDisconnect:
        logger.debug("Plugin sync client disconnected")
    except Exception as e:
        logger.exception(f"Plugin WebSocket error: {e}")
    finally:
        await unregister_client(websocket)


# Notification functions for plugin service to call on state changes


async def notify_plugin_state_changed(plugin_id: str, status: dict[str, Any]) -> None:
    """Notify all clients that a plugin's state changed."""
    await broadcast_plugin_state(
        action=PluginSyncAction.STATE_CHANGED,
        plugin_id=plugin_id,
        status=status,
    )


async def notify_plugin_added(plugin_id: str, status: dict[str, Any]) -> None:
    """Notify all clients that a plugin was added."""
    await broadcast_plugin_state(
        action=PluginSyncAction.PLUGIN_ADDED,
        plugin_id=plugin_id,
        status=status,
    )


async def notify_plugin_removed(plugin_id: str) -> None:
    """Notify all clients that a plugin was removed."""
    await broadcast_plugin_state(
        action=PluginSyncAction.PLUGIN_REMOVED,
        plugin_id=plugin_id,
    )


async def notify_permission_changed(plugin_id: str, granted_permissions: list[str]) -> None:
    """Notify all clients that plugin permissions changed."""
    await broadcast_plugin_state(
        action=PluginSyncAction.PERMISSION_CHANGED,
        plugin_id=plugin_id,
        status={"granted_permissions": granted_permissions},
    )
