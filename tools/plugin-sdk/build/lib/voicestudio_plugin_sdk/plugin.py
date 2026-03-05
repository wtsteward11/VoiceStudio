"""
Base Plugin class for VoiceStudio plugins.

Provides the main interface for creating plugins with lifecycle
management and host API access.
"""

from __future__ import annotations

import asyncio
import logging
import sys
from abc import ABC, abstractmethod
from typing import Any

from .host_api import HostAPI
from .manifest import PluginManifest
from .protocol import (
    HostMethods,
    Message,
    Notification,
    Request,
    Response,
    RPCError,
    decode_message_header,
    encode_message,
)


class PluginLogger:
    """Logger that sends messages to both local logging and host."""

    def __init__(self, name: str, host_api: HostAPI | None = None):
        self._logger = logging.getLogger(name)
        self._host = host_api

    def set_host(self, host_api: HostAPI) -> None:
        """Set the host API for remote logging."""
        self._host = host_api

    def debug(self, message: str, **context: Any) -> None:
        """Log debug message."""
        self._logger.debug(message)
        if self._host:
            self._host.log("debug", message, context or None)

    def info(self, message: str, **context: Any) -> None:
        """Log info message."""
        self._logger.info(message)
        if self._host:
            self._host.log("info", message, context or None)

    def warning(self, message: str, **context: Any) -> None:
        """Log warning message."""
        self._logger.warning(message)
        if self._host:
            self._host.log("warning", message, context or None)

    def error(self, message: str, **context: Any) -> None:
        """Log error message."""
        self._logger.error(message)
        if self._host:
            self._host.log("error", message, context or None)


class Plugin(ABC):
    """
    Base class for VoiceStudio plugins.

    Subclass this to create a plugin:

        class MyPlugin(Plugin):
            manifest = PluginManifest(
                id="my-plugin",
                name="My Plugin",
                version="1.0.0",
            )

            async def on_initialize(self, config: dict) -> None:
                self.model = await self.load_model()

            async def on_invoke(self, capability: str, params: dict) -> dict:
                return {"result": "ok"}

        if __name__ == "__main__":
            plugin = MyPlugin()
            plugin.run()
    """

    manifest: PluginManifest

    def __init__(self):
        """Initialize the plugin."""
        if not hasattr(self, "manifest"):
            raise ValueError("Plugin must define a manifest class attribute")

        self._initialized = False
        self._activated = False
        self._running = False
        self._request_id = 0
        self._pending_requests: dict[int | str, asyncio.Future[Response]] = {}

        # Set up logging
        self.log = PluginLogger(self.manifest.id)

        # Host API (set during initialize)
        self._host: HostAPI | None = None

    @property
    def host(self) -> HostAPI:
        """Access the host API."""
        if self._host is None:
            raise RuntimeError("Host API not available. Is the plugin initialized?")
        return self._host

    # =========================================================================
    # Abstract methods to implement
    # =========================================================================

    async def on_initialize(self, config: dict[str, Any]) -> None:
        """
        Called when the plugin is initialized.

        Override to perform setup tasks like loading models.

        Args:
            config: Plugin configuration from host
        """
        pass

    async def on_shutdown(self) -> None:
        """
        Called when the plugin is shutting down.

        Override to perform cleanup tasks.
        """
        pass

    async def on_activate(self) -> None:
        """Called when the plugin is activated."""
        pass

    async def on_deactivate(self) -> None:
        """Called when the plugin is deactivated."""
        pass

    @abstractmethod
    async def on_invoke(
        self,
        capability: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """
        Handle capability invocation.

        Args:
            capability: Name of the capability being invoked
            params: Invocation parameters

        Returns:
            Result dictionary
        """
        ...

    # =========================================================================
    # Progress reporting
    # =========================================================================

    async def progress(
        self,
        operation_id: str,
        progress: float,
        message: str = "",
        status: str = "running",
    ) -> None:
        """
        Report progress on an operation.

        Args:
            operation_id: Operation identifier
            progress: Progress percentage (0-100)
            message: Progress message
            status: Operation status
        """
        if self._host:
            self._host.progress(operation_id, progress, message, status)

    # =========================================================================
    # IPC implementation
    # =========================================================================

    async def _send_request(
        self,
        method: str,
        params: dict[str, Any] | None,
    ) -> Response:
        """Send a request to the host and await response."""
        self._request_id += 1
        request_id = self._request_id

        request = Request(
            id=request_id,
            method=method,
            params=params,
        )

        # Create future for response
        future: asyncio.Future[Response] = asyncio.get_event_loop().create_future()
        self._pending_requests[request_id] = future

        # Send request
        await self._write_message(request)

        # Wait for response
        try:
            return await asyncio.wait_for(future, timeout=30.0)
        except asyncio.TimeoutError:
            del self._pending_requests[request_id]
            raise TimeoutError(f"Request {method} timed out")

    def _send_notification(
        self,
        method: str,
        params: dict[str, Any] | None,
    ) -> None:
        """Send a notification to the host (no response expected)."""
        notification = Notification(method=method, params=params)
        # Run write in background
        asyncio.create_task(self._write_message(notification))

    async def _write_message(self, message: Message) -> None:
        """Write a message to stdout."""
        data = encode_message(message)
        sys.stdout.buffer.write(data)
        sys.stdout.buffer.flush()

    async def _read_message(self) -> Message | None:
        """Read a message from stdin."""
        # Read length header
        header = sys.stdin.buffer.read(4)
        if not header or len(header) < 4:
            return None

        length = decode_message_header(header)

        # Read message body
        body = sys.stdin.buffer.read(length)
        if not body or len(body) < length:
            return None

        return Message.from_json(body.decode("utf-8"))

    async def _handle_request(self, request: Request) -> Response:
        """Handle an incoming request from the host."""
        try:
            result = await self._dispatch_method(request.method, request.params or {})
            return Response.success(request.id, result)
        except PermissionError as e:
            return Response.failure(
                request.id,
                RPCError.permission_denied(str(e)),
            )
        except ValueError as e:
            return Response.failure(
                request.id,
                RPCError.invalid_params(str(e)),
            )
        except Exception as e:
            self.log.error(f"Error handling {request.method}: {e}")
            return Response.failure(
                request.id,
                RPCError.plugin_error(str(e)),
            )

    async def _dispatch_method(
        self,
        method: str,
        params: dict[str, Any],
    ) -> Any:
        """Dispatch a method call to the appropriate handler."""
        if method == HostMethods.INITIALIZE:
            await self._do_initialize(params)
            return {
                "status": "ready",
                "version": self.manifest.version,
                "capabilities": [c.name for c in self.manifest.capabilities],
            }

        elif method == HostMethods.SHUTDOWN:
            await self._do_shutdown()
            return {"acknowledged": True}

        elif method == HostMethods.ACTIVATE:
            await self._do_activate()
            return {"activated": True}

        elif method == HostMethods.DEACTIVATE:
            await self._do_deactivate()
            return {"deactivated": True}

        elif method == HostMethods.GET_CAPABILITIES:
            return {
                "capabilities": [c.to_dict() for c in self.manifest.capabilities]
            }

        elif method == HostMethods.INVOKE_CAPABILITY:
            capability = params.get("capability")
            if not capability:
                raise ValueError("Missing required parameter: capability")
            cap_params = params.get("params", {})
            return await self.on_invoke(capability, cap_params)

        else:
            raise ValueError(f"Unknown method: {method}")

    async def _do_initialize(self, params: dict[str, Any]) -> None:
        """Perform initialization."""
        if self._initialized:
            raise RuntimeError("Plugin already initialized")

        # Set up host API
        self._host = HostAPI(self._send_request, self._send_notification)
        self.log.set_host(self._host)

        # Call user initialization
        config = params.get("config", {})
        await self.on_initialize(config)

        self._initialized = True
        self.log.info("Plugin initialized")

    async def _do_shutdown(self) -> None:
        """Perform shutdown."""
        self._running = False
        await self.on_shutdown()
        self.log.info("Plugin shutdown")

    async def _do_activate(self) -> None:
        """Perform activation."""
        if not self._initialized:
            raise RuntimeError("Plugin not initialized")
        await self.on_activate()
        self._activated = True
        self.log.info("Plugin activated")

    async def _do_deactivate(self) -> None:
        """Perform deactivation."""
        await self.on_deactivate()
        self._activated = False
        self.log.info("Plugin deactivated")

    async def _message_loop(self) -> None:
        """Main message processing loop."""
        while self._running:
            try:
                message = await self._read_message()
                if message is None:
                    # EOF - host closed connection
                    self._running = False
                    break

                if isinstance(message, Request):
                    response = await self._handle_request(message)
                    await self._write_message(response)

                elif isinstance(message, Response):
                    # Response to our request
                    future = self._pending_requests.pop(message.id, None)
                    if future:
                        future.set_result(message)

                elif isinstance(message, Notification):
                    # Handle notification (heartbeat, etc.)
                    pass

            except Exception as e:
                self.log.error(f"Error in message loop: {e}")

    # =========================================================================
    # Entry point
    # =========================================================================

    def run(self) -> None:
        """
        Run the plugin.

        This starts the message loop and processes requests from the host.
        """
        self._running = True

        # Set up event loop
        try:
            loop = asyncio.get_event_loop()
        except RuntimeError:
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)

        try:
            loop.run_until_complete(self._message_loop())
        # ALLOWED: bare except - best effort, failure acceptable
        except KeyboardInterrupt:
            pass
        finally:
            self._running = False
            loop.close()

    async def run_async(self) -> None:
        """Run the plugin asynchronously."""
        self._running = True
        await self._message_loop()
