"""
WebSocketMonitor - Traces streaming synthesis and real-time events.

Monitors WebSocket connections for:
- Synthesis progress events
- Audio streaming chunks
- Engine status updates
- Error notifications
- Job completion events

Usage:
    monitor = WebSocketMonitor(tracer)
    async with monitor.connect("ws://localhost:8000/ws/synthesis"):
        await monitor.send({"job_id": "123", "action": "start"})
        messages = await monitor.wait_for(lambda m: m.get("status") == "complete")
"""

from __future__ import annotations

import asyncio
import json
import time
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from typing import Any, Callable

try:
    import websockets
    from websockets.client import WebSocketClientProtocol

    HAS_WEBSOCKETS = True
except ImportError:
    HAS_WEBSOCKETS = False
    WebSocketClientProtocol = None


class MessageDirection(Enum):
    """Direction of WebSocket message."""

    SENT = "sent"
    RECEIVED = "received"


@dataclass
class WSMessage:
    """Represents a WebSocket message."""

    direction: MessageDirection
    data: Any
    timestamp: float
    message_type: str = "text"
    parsed: dict | None = None

    @property
    def is_json(self) -> bool:
        """Check if message is JSON."""
        return self.parsed is not None

    @classmethod
    def from_raw(cls, direction: MessageDirection, raw: str | bytes) -> WSMessage:
        """Create message from raw data."""
        timestamp = time.time()
        msg_type = "binary" if isinstance(raw, bytes) else "text"

        parsed = None
        if isinstance(raw, str):
            try:
                parsed = json.loads(raw)
            # ALLOWED: bare except - best effort, failure acceptable
            except json.JSONDecodeError:
                pass

        return cls(
            direction=direction,
            data=raw if not isinstance(raw, bytes) else f"<binary:{len(raw)} bytes>",
            timestamp=timestamp,
            message_type=msg_type,
            parsed=parsed,
        )


@dataclass
class SynthesisProgress:
    """Tracks synthesis job progress."""

    job_id: str
    started_at: float
    chunks_received: int = 0
    total_chunks: int | None = None
    bytes_received: int = 0
    last_update: float = 0
    status: str = "pending"
    error: str | None = None
    completed_at: float | None = None

    @property
    def duration_ms(self) -> float:
        """Get duration in milliseconds."""
        end = self.completed_at or time.time()
        return (end - self.started_at) * 1000

    @property
    def progress_percent(self) -> float | None:
        """Get progress percentage if total is known."""
        if self.total_chunks and self.total_chunks > 0:
            return (self.chunks_received / self.total_chunks) * 100
        return None


class WebSocketMonitor:
    """
    Monitor WebSocket connections for synthesis and real-time events.

    Integrates with WorkflowTracer for comprehensive debugging.
    """

    def __init__(
        self,
        tracer=None,
        auto_parse_json: bool = True,
        timeout: float = 30.0,
    ):
        """
        Initialize WebSocket monitor.

        Args:
            tracer: WorkflowTracer instance for logging.
            auto_parse_json: Automatically parse JSON messages.
            timeout: Default timeout for operations.
        """
        self.tracer = tracer
        self.auto_parse_json = auto_parse_json
        self.timeout = timeout

        self.messages: list[WSMessage] = []
        self.connections: dict[str, dict[str, Any]] = {}
        self.synthesis_jobs: dict[str, SynthesisProgress] = {}

        self._active_ws: WebSocketClientProtocol | None = None
        self._active_url: str | None = None
        self._receive_task: asyncio.Task | None = None
        self._message_handlers: list[Callable[[WSMessage], None]] = []

    def connect(self, url: str) -> _WSContextManager:
        """
        Connect to a WebSocket endpoint.

        Args:
            url: WebSocket URL to connect to.

        Returns:
            Async context manager for the connection.
        """
        return _WSContextManager(self, url)

    async def _connect(self, url: str) -> None:
        """Internal connect implementation."""
        if not HAS_WEBSOCKETS:
            raise ImportError("websockets package required: pip install websockets")

        self._active_url = url
        connect_start = time.time()

        self._active_ws = await websockets.connect(url)

        connect_duration = (time.time() - connect_start) * 1000

        self.connections[url] = {
            "connected_at": datetime.now().isoformat(),
            "messages_sent": 0,
            "messages_received": 0,
            "status": "connected",
        }

        if self.tracer:
            self.tracer.step(f"WebSocket connected: {url}")
            self.tracer.record_timing("websocket_connect", connect_duration)

        # Start receive task
        self._receive_task = asyncio.create_task(self._receive_loop())

    async def _disconnect(self) -> None:
        """Internal disconnect implementation."""
        if self._receive_task:
            self._receive_task.cancel()
            try:
                await self._receive_task
            # ALLOWED: bare except - best effort, failure acceptable
            except asyncio.CancelledError:
                pass

        if self._active_ws:
            await self._active_ws.close()

        if self._active_url and self._active_url in self.connections:
            self.connections[self._active_url]["status"] = "disconnected"
            self.connections[self._active_url]["disconnected_at"] = datetime.now().isoformat()

        if self.tracer:
            self.tracer.step(f"WebSocket disconnected: {self._active_url}")

        self._active_ws = None
        self._active_url = None

    async def _receive_loop(self) -> None:
        """Background task to receive messages."""
        try:
            async for raw in self._active_ws:
                msg = WSMessage.from_raw(MessageDirection.RECEIVED, raw)
                self._handle_message(msg)
        except websockets.ConnectionClosed:
            if self.tracer:
                self.tracer.step("WebSocket connection closed")
        # ALLOWED: bare except - best effort, failure acceptable
        except asyncio.CancelledError:
            pass

    def _handle_message(self, msg: WSMessage) -> None:
        """Handle an incoming message."""
        self.messages.append(msg)

        if self._active_url and self._active_url in self.connections:
            self.connections[self._active_url]["messages_received"] += 1

        # Check for synthesis events
        if msg.parsed:
            self._handle_synthesis_event(msg.parsed)

        # Call registered handlers
        for handler in self._message_handlers:
            try:
                handler(msg)
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

        if self.tracer:
            self.tracer.trace_log.append(
                {
                    "event": "websocket_message",
                    "direction": msg.direction.value,
                    "url": self._active_url,
                    "data": msg.parsed or str(msg.data)[:500],
                    "timestamp": datetime.now().isoformat(),
                }
            )

    def _handle_synthesis_event(self, data: dict) -> None:
        """Handle synthesis-related events."""
        event_type = data.get("type") or data.get("event")
        job_id = data.get("job_id") or data.get("synthesis_id")

        if not job_id:
            return

        if job_id not in self.synthesis_jobs:
            self.synthesis_jobs[job_id] = SynthesisProgress(job_id=job_id, started_at=time.time())

        job = self.synthesis_jobs[job_id]
        job.last_update = time.time()

        if event_type in ("chunk", "audio_chunk", "data"):
            job.chunks_received += 1
            if "size" in data:
                job.bytes_received += data["size"]
            if "total" in data:
                job.total_chunks = data["total"]
            job.status = "streaming"

        elif event_type in ("progress", "status"):
            job.status = data.get("status", job.status)
            if "progress" in data:
                job.total_chunks = 100
                job.chunks_received = int(data["progress"])

        elif event_type in ("complete", "done", "finished"):
            job.status = "completed"
            job.completed_at = time.time()

        elif event_type in ("error", "failed"):
            job.status = "failed"
            job.error = data.get("error") or data.get("message")
            job.completed_at = time.time()

    async def send(self, data: dict | str) -> None:
        """
        Send a message.

        Args:
            data: Data to send (dict will be JSON-encoded).
        """
        if not self._active_ws:
            raise RuntimeError("Not connected")

        if isinstance(data, dict):
            payload = json.dumps(data)
        else:
            payload = data

        await self._active_ws.send(payload)

        msg = WSMessage.from_raw(MessageDirection.SENT, payload)
        self.messages.append(msg)

        if self._active_url and self._active_url in self.connections:
            self.connections[self._active_url]["messages_sent"] += 1

        if self.tracer:
            self.tracer.trace_log.append(
                {
                    "event": "websocket_send",
                    "url": self._active_url,
                    "data": msg.parsed or str(payload)[:500],
                    "timestamp": datetime.now().isoformat(),
                }
            )

    async def wait_for(
        self,
        condition: Callable[[WSMessage], bool],
        timeout: float | None = None,
    ) -> WSMessage | None:
        """
        Wait for a message matching a condition.

        Args:
            condition: Function that returns True for matching messages.
            timeout: Timeout in seconds (uses default if None).

        Returns:
            Matching message or None if timeout.
        """
        timeout = timeout or self.timeout
        start = time.time()

        # Check existing messages first
        for msg in reversed(self.messages):
            if msg.direction == MessageDirection.RECEIVED and condition(msg):
                return msg

        # Wait for new messages
        while time.time() - start < timeout:
            await asyncio.sleep(0.1)
            for msg in reversed(self.messages):
                if msg.direction == MessageDirection.RECEIVED and condition(msg):
                    return msg

        return None

    async def wait_for_synthesis(
        self,
        job_id: str,
        timeout: float | None = None,
    ) -> SynthesisProgress:
        """
        Wait for a synthesis job to complete.

        Args:
            job_id: Job ID to wait for.
            timeout: Timeout in seconds.

        Returns:
            SynthesisProgress with final status.
        """
        timeout = timeout or self.timeout
        start = time.time()

        while time.time() - start < timeout:
            if job_id in self.synthesis_jobs:
                job = self.synthesis_jobs[job_id]
                if job.status in ("completed", "failed"):
                    return job
            await asyncio.sleep(0.1)

        # Return current state even if not complete
        if job_id in self.synthesis_jobs:
            return self.synthesis_jobs[job_id]

        return SynthesisProgress(job_id=job_id, started_at=start, status="timeout")

    def on_message(self, handler: Callable[[WSMessage], None]) -> None:
        """
        Register a message handler.

        Args:
            handler: Function to call for each message.
        """
        self._message_handlers.append(handler)

    def get_messages(
        self,
        direction: MessageDirection | None = None,
        json_only: bool = False,
    ) -> list[WSMessage]:
        """
        Get filtered messages.

        Args:
            direction: Filter by direction.
            json_only: Only include JSON messages.

        Returns:
            List of matching messages.
        """
        result = self.messages

        if direction:
            result = [m for m in result if m.direction == direction]

        if json_only:
            result = [m for m in result if m.is_json]

        return result

    def get_synthesis_summary(self) -> dict[str, Any]:
        """
        Get summary of all synthesis jobs.

        Returns:
            Summary dict.
        """
        jobs = list(self.synthesis_jobs.values())

        return {
            "total_jobs": len(jobs),
            "completed": sum(1 for j in jobs if j.status == "completed"),
            "failed": sum(1 for j in jobs if j.status == "failed"),
            "in_progress": sum(1 for j in jobs if j.status not in ("completed", "failed")),
            "total_chunks": sum(j.chunks_received for j in jobs),
            "total_bytes": sum(j.bytes_received for j in jobs),
            "jobs": {
                j.job_id: {
                    "status": j.status,
                    "duration_ms": j.duration_ms,
                    "chunks": j.chunks_received,
                    "bytes": j.bytes_received,
                    "error": j.error,
                }
                for j in jobs
            },
        }

    def export_log(self) -> list[dict]:
        """
        Export message log for debugging.

        Returns:
            List of message dicts.
        """
        return [
            {
                "direction": msg.direction.value,
                "timestamp": msg.timestamp,
                "type": msg.message_type,
                "data": msg.parsed or str(msg.data)[:1000],
            }
            for msg in self.messages
        ]


class _WSContextManager:
    """Async context manager for WebSocket connections."""

    def __init__(self, monitor: WebSocketMonitor, url: str):
        self.monitor = monitor
        self.url = url

    async def __aenter__(self) -> WebSocketMonitor:
        await self.monitor._connect(self.url)
        return self.monitor

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        await self.monitor._disconnect()
        return False


# Sync wrapper for non-async tests
class SyncWebSocketMonitor:
    """
    Synchronous wrapper for WebSocketMonitor.

    For use in synchronous test code.
    """

    def __init__(self, tracer=None):
        self._async_monitor = WebSocketMonitor(tracer)
        self._loop: asyncio.AbstractEventLoop | None = None

    def _get_loop(self) -> asyncio.AbstractEventLoop:
        """Get or create event loop."""
        if self._loop is None or self._loop.is_closed():
            self._loop = asyncio.new_event_loop()
        return self._loop

    def connect(self, url: str) -> None:
        """Connect to WebSocket."""
        self._get_loop().run_until_complete(self._async_monitor._connect(url))

    def disconnect(self) -> None:
        """Disconnect from WebSocket."""
        self._get_loop().run_until_complete(self._async_monitor._disconnect())

    def send(self, data: dict | str) -> None:
        """Send a message."""
        self._get_loop().run_until_complete(self._async_monitor.send(data))

    def wait_for_synthesis(self, job_id: str, timeout: float = 30.0) -> SynthesisProgress:
        """Wait for synthesis completion."""
        return self._get_loop().run_until_complete(
            self._async_monitor.wait_for_synthesis(job_id, timeout)
        )

    @property
    def messages(self) -> list[WSMessage]:
        """Get all messages."""
        return self._async_monitor.messages

    @property
    def synthesis_jobs(self) -> dict[str, SynthesisProgress]:
        """Get synthesis jobs."""
        return self._async_monitor.synthesis_jobs

    def get_synthesis_summary(self) -> dict[str, Any]:
        """Get synthesis summary."""
        return self._async_monitor.get_synthesis_summary()

    def close(self) -> None:
        """Clean up resources."""
        if self._loop and not self._loop.is_closed():
            self._loop.close()
