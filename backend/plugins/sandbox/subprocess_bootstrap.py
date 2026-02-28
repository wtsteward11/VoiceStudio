"""
Subprocess Bootstrap for Plugin Isolation.

Phase 4 Enhancement: Minimal bootstrap code that runs in the plugin
subprocess to establish communication with the host.

This module provides a fallback when the full voicestudio-plugin-sdk
is not available. It handles:
    - IPC bridge client setup
    - Plugin module loading
    - Message loop execution
    - Graceful shutdown
"""

import asyncio
import importlib
import json
import logging
import os
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Optional, Union

# Set up logging for subprocess
logging.basicConfig(
    level=logging.DEBUG,
    format="%(levelname)s [%(name)s] %(message)s",
    stream=sys.stderr,
)
logger = logging.getLogger("voicestudio.subprocess")


@dataclass
class SubprocessBridge:
    """
    Client-side IPC bridge for plugin subprocess.

    Handles communication with the VoiceStudio host through
    stdin/stdout using the JSON-RPC protocol.
    """

    _reader: Optional[asyncio.StreamReader] = None
    _writer: Optional[asyncio.StreamWriter] = None
    _running: bool = False
    _handlers: Dict[str, Any] = field(default_factory=dict, repr=False)
    _pending: Dict[Union[int, str], asyncio.Future] = field(default_factory=dict, repr=False)
    _next_id: int = 0

    async def connect(self) -> None:
        """Connect to host via stdin/stdout."""
        loop = asyncio.get_event_loop()

        # Create stream reader for stdin
        self._reader = asyncio.StreamReader()
        protocol = asyncio.StreamReaderProtocol(self._reader)
        await loop.connect_read_pipe(lambda: protocol, sys.stdin)

        # Create stream writer for stdout
        write_transport, write_protocol = await loop.connect_write_pipe(
            asyncio.streams.FlowControlMixin, sys.stdout
        )
        self._writer = asyncio.StreamWriter(write_transport, write_protocol, None, loop)

        self._running = True
        logger.debug("Subprocess bridge connected")

    async def run_message_loop(self) -> None:
        """Run the main message processing loop."""
        if not self._reader:
            raise RuntimeError("Bridge not connected")

        while self._running:
            try:
                # Read length prefix (4 bytes)
                header = await self._reader.readexactly(4)
                length = int.from_bytes(header, byteorder="big")

                # Read message body
                body = await self._reader.readexactly(length)
                message = json.loads(body.decode("utf-8"))

                await self._handle_message(message)

            except asyncio.IncompleteReadError:
                logger.info("Host connection closed")
                self._running = False
                break
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in message loop: {e}")

    async def _handle_message(self, message: Dict[str, Any]) -> None:
        """Handle an incoming message from host."""
        if "method" in message and "id" in message:
            # Request from host
            await self._handle_request(message)
        elif "method" in message:
            # Notification from host
            await self._handle_notification(message)
        elif "id" in message:
            # Response to our request
            await self._handle_response(message)

    async def _handle_request(self, message: Dict[str, Any]) -> None:
        """Handle an RPC request from host."""
        request_id = message["id"]
        method = message["method"]
        params = message.get("params", {})

        handler = self._handlers.get(method)

        if handler is None:
            response = {
                "jsonrpc": "2.0",
                "id": request_id,
                "error": {"code": -32601, "message": f"Method not found: {method}"},
            }
        else:
            try:
                if isinstance(params, dict):
                    result = await handler(**params)
                elif isinstance(params, list):
                    result = await handler(*params)
                else:
                    result = await handler()

                response = {
                    "jsonrpc": "2.0",
                    "id": request_id,
                    "result": result,
                }
            except Exception as e:
                logger.exception(f"Error handling request {method}")
                response = {
                    "jsonrpc": "2.0",
                    "id": request_id,
                    "error": {"code": -32603, "message": str(e)},
                }

        await self._send_message(response)

    async def _handle_notification(self, message: Dict[str, Any]) -> None:
        """Handle a notification from host."""
        method = message["method"]
        params = message.get("params", {})

        handler = self._handlers.get(method)
        if handler:
            try:
                if isinstance(params, dict):
                    await handler(**params)
                elif isinstance(params, list):
                    await handler(*params)
                else:
                    await handler()
            except Exception as e:
                logger.error(f"Error handling notification {method}: {e}")

    async def _handle_response(self, message: Dict[str, Any]) -> None:
        """Handle a response to one of our requests."""
        request_id = message.get("id")
        future = self._pending.pop(request_id, None)

        if future and not future.done():
            if "error" in message:
                future.set_exception(
                    Exception(
                        f"RPC error {message['error']['code']}: {message['error']['message']}"
                    )
                )
            else:
                future.set_result(message.get("result"))

    async def call_host(
        self,
        method: str,
        params: Optional[Dict[str, Any]] = None,
        timeout: float = 30.0,
    ) -> Any:
        """
        Call a Host API method.

        Args:
            method: The RPC method name
            params: Optional parameters
            timeout: Request timeout in seconds

        Returns:
            The result from the host
        """
        self._next_id += 1
        request_id = self._next_id

        request = {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": method,
        }
        if params:
            request["params"] = params

        future: asyncio.Future = asyncio.Future()
        self._pending[request_id] = future

        await self._send_message(request)

        try:
            return await asyncio.wait_for(future, timeout=timeout)
        except asyncio.TimeoutError:
            self._pending.pop(request_id, None)
            raise TimeoutError(f"Request {method} timed out")

    async def notify_host(
        self,
        method: str,
        params: Optional[Dict[str, Any]] = None,
    ) -> None:
        """Send a notification to the host (no response expected)."""
        notification = {
            "jsonrpc": "2.0",
            "method": method,
        }
        if params:
            notification["params"] = params

        await self._send_message(notification)

    async def _send_message(self, message: Dict[str, Any]) -> None:
        """Send a message to the host."""
        if not self._writer:
            raise RuntimeError("Bridge not connected")

        message["_timestamp"] = time.time()
        json_data = json.dumps(message).encode("utf-8")
        length = len(json_data)

        # Write length prefix + message
        self._writer.write(length.to_bytes(4, byteorder="big"))
        self._writer.write(json_data)
        await self._writer.drain()

    def register_handler(self, method: str, handler: Any) -> None:
        """Register a handler for incoming requests/notifications."""
        self._handlers[method] = handler

    def stop(self) -> None:
        """Stop the message loop."""
        self._running = False


class PluginContext:
    """
    Context object provided to plugins in subprocess mode.

    Provides access to Host API through the subprocess bridge.
    """

    def __init__(self, bridge: SubprocessBridge, plugin_id: str):
        self._bridge = bridge
        self.plugin_id = plugin_id
        self.permissions: Dict[str, Any] = {}

    async def audio_play(self, **kwargs) -> Dict[str, Any]:
        """Play audio through the host."""
        return await self._bridge.call_host("host.audio.play", kwargs)

    async def audio_stop(self, **kwargs) -> Dict[str, Any]:
        """Stop audio playback."""
        return await self._bridge.call_host("host.audio.stop", kwargs)

    async def ui_notify(
        self,
        title: str,
        message: str,
        level: str = "info",
    ) -> Dict[str, Any]:
        """Show a notification in the UI."""
        return await self._bridge.call_host(
            "host.ui.notify",
            {"title": title, "message": message, "level": level},
        )

    async def storage_get(self, key: str) -> Any:
        """Get a value from plugin storage."""
        return await self._bridge.call_host("host.storage.get", {"key": key})

    async def storage_set(self, key: str, value: Any) -> Dict[str, Any]:
        """Set a value in plugin storage."""
        return await self._bridge.call_host("host.storage.set", {"key": key, "value": value})

    async def settings_get(self, key: str) -> Any:
        """Get a setting value."""
        return await self._bridge.call_host("host.settings.get", {"key": key})

    async def engine_invoke(
        self,
        engine_id: str,
        method: str,
        params: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """Invoke a method on another engine."""
        return await self._bridge.call_host(
            "host.engine.invoke",
            {"engine_id": engine_id, "method": method, "params": params or {}},
        )

    async def log(self, level: str, message: str) -> None:
        """Send a log message to the host."""
        await self._bridge.notify_host(
            "notify.log",
            {"level": level, "message": message, "plugin_id": self.plugin_id},
        )

    async def progress(self, progress: float, message: Optional[str] = None) -> None:
        """Report progress to the host."""
        await self._bridge.notify_host(
            "notify.progress",
            {"progress": progress, "message": message, "plugin_id": self.plugin_id},
        )


async def run_plugin_subprocess(entry_module: str) -> None:
    """
    Bootstrap and run a plugin in subprocess mode.

    Args:
        entry_module: The plugin's entry point module path
    """
    plugin_id = os.environ.get("VOICESTUDIO_PLUGIN_ID", "unknown")
    plugin_path = os.environ.get("VOICESTUDIO_PLUGIN_PATH", "")

    logger.info(f"Starting plugin subprocess: {plugin_id}")
    logger.debug(f"Entry module: {entry_module}")
    logger.debug(f"Plugin path: {plugin_path}")

    # Create and connect bridge
    bridge = SubprocessBridge()
    await bridge.connect()

    # Create plugin context
    context = PluginContext(bridge, plugin_id)

    # Load the plugin module
    plugin_instance = None

    try:
        # Import the entry module
        module = importlib.import_module(entry_module)

        # Look for a Plugin class or register function
        if hasattr(module, "Plugin"):
            plugin_class = module.Plugin
            plugin_instance = plugin_class()
        elif hasattr(module, "create_plugin"):
            plugin_instance = module.create_plugin()
        elif hasattr(module, "register"):
            # Legacy register function
            plugin_instance = module.register()

    except ImportError as e:
        logger.error(f"Failed to import plugin module: {e}")
        raise

    # Register lifecycle handlers
    async def handle_initialize(**params) -> Dict[str, Any]:
        """Handle initialization request from host."""
        context.permissions = params.get("permissions", {})

        if plugin_instance and hasattr(plugin_instance, "initialize"):
            await plugin_instance.initialize(context)

        return {"status": "initialized", "plugin_id": plugin_id}

    async def handle_shutdown() -> Dict[str, Any]:
        """Handle shutdown request from host."""
        if plugin_instance and hasattr(plugin_instance, "cleanup"):
            await plugin_instance.cleanup()

        bridge.stop()
        return {"status": "shutdown"}

    async def handle_invoke_capability(**params) -> Any:
        """Handle capability invocation."""
        capability = params.get("capability")
        cap_params = params.get("params", {})

        if plugin_instance and hasattr(plugin_instance, capability):
            method = getattr(plugin_instance, capability)
            return await method(**cap_params)
        else:
            raise ValueError(f"Unknown capability: {capability}")

    async def handle_heartbeat(**params) -> None:
        """Handle heartbeat from host."""
        # Just acknowledge receipt
        pass

    bridge.register_handler("plugin.initialize", handle_initialize)
    bridge.register_handler("plugin.shutdown", handle_shutdown)
    bridge.register_handler("plugin.invokeCapability", handle_invoke_capability)
    bridge.register_handler("notify.heartbeat", handle_heartbeat)

    # Run the message loop
    try:
        await bridge.run_message_loop()
    except Exception as e:
        logger.error(f"Message loop error: {e}")
    finally:
        logger.info(f"Plugin subprocess exiting: {plugin_id}")


if __name__ == "__main__":
    # Direct execution for testing
    if len(sys.argv) > 1:
        entry = sys.argv[1]
    else:
        entry = os.environ.get("VOICESTUDIO_ENTRY_MODULE", "plugin")

    asyncio.run(run_plugin_subprocess(entry))
