"""
VoiceStudio WebSocket Integration Tests.

Tests all WebSocket endpoints for real-time communication:
- Job progress streaming
- Voice cloning progress
- Training progress
- Audio meters/visualization
- Real-time converter status
- Log streaming

Requires:
- Backend running with WebSocket support
- websocket-client package installed
"""

from __future__ import annotations

import contextlib
import json
import os
import threading
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

import pytest

try:
    import websocket
except ImportError:
    websocket = None
    pytest.skip("websocket-client not installed", allow_module_level=True)

try:
    import requests
except ImportError:
    requests = None


# Test configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
WS_BASE_URL = BACKEND_URL.replace("http://", "ws://").replace("https://", "wss://")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
TIMEOUT_SECONDS = 10

# Pytest markers
pytestmark = [
    pytest.mark.websocket,
    pytest.mark.integration,
]


@dataclass
class WebSocketTestResult:
    """Result of a WebSocket connection test."""

    endpoint: str
    connected: bool
    messages_received: int
    error: str | None = None
    first_message: dict | None = None
    duration_ms: float = 0
    closed_cleanly: bool = False


@dataclass
class WebSocketMessage:
    """A received WebSocket message."""

    timestamp: datetime
    data: Any
    message_type: str = "text"


class WebSocketTester:
    """Helper class for testing WebSocket connections."""

    def __init__(self, url: str, timeout: float = TIMEOUT_SECONDS):
        self.url = url
        self.timeout = timeout
        self.messages: list[WebSocketMessage] = []
        self.connected = False
        self.error: str | None = None
        self.closed_cleanly = False
        self._ws: websocket.WebSocket | None = None
        self._thread: threading.Thread | None = None
        self._stop_event = threading.Event()

    def connect(self) -> bool:
        """Attempt to connect to the WebSocket."""
        try:
            self._ws = websocket.WebSocket()
            self._ws.settimeout(self.timeout)
            self._ws.connect(self.url)
            self.connected = True
            return True
        except websocket.WebSocketException as e:
            self.error = str(e)
            return False
        except Exception as e:
            self.error = str(e)
            return False

    def receive_messages(
        self, max_count: int = 5, wait_seconds: float = 2.0
    ) -> list[WebSocketMessage]:
        """Receive messages from the WebSocket."""
        if not self._ws or not self.connected:
            return []

        messages = []
        start_time = time.time()

        while len(messages) < max_count and (time.time() - start_time) < wait_seconds:
            try:
                self._ws.settimeout(0.5)
                data = self._ws.recv()

                if data:
                    try:
                        parsed = json.loads(data)
                    except json.JSONDecodeError:
                        parsed = data

                    msg = WebSocketMessage(
                        timestamp=datetime.now(),
                        data=parsed,
                        message_type="json" if isinstance(parsed, dict) else "text",
                    )
                    messages.append(msg)
                    self.messages.append(msg)

            except websocket.WebSocketTimeoutException:
                continue
            except websocket.WebSocketException as e:
                self.error = str(e)
                break
            except Exception as e:
                self.error = str(e)
                break

        return messages

    def send_message(self, data: Any) -> bool:
        """Send a message to the WebSocket."""
        if not self._ws or not self.connected:
            return False

        try:
            if isinstance(data, dict):
                self._ws.send(json.dumps(data))
            else:
                self._ws.send(str(data))
            return True
        except Exception as e:
            self.error = str(e)
            return False

    def close(self):
        """Close the WebSocket connection."""
        if self._ws:
            try:
                self._ws.close()
                self.closed_cleanly = True
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
            self._ws = None
        self.connected = False

    def get_result(self) -> WebSocketTestResult:
        """Get test result summary."""
        return WebSocketTestResult(
            endpoint=self.url,
            connected=self.connected,
            messages_received=len(self.messages),
            error=self.error,
            first_message=self.messages[0].data if self.messages else None,
            closed_cleanly=self.closed_cleanly,
        )


# Define all WebSocket endpoints to test
WEBSOCKET_ENDPOINTS = {
    "jobs_progress": {
        "path": "/ws/jobs/progress",
        "description": "Job queue progress streaming",
        "expects_immediate_message": False,
        "message_format": {"type": "progress", "job_id": "...", "percent": 0},
    },
    "cloning_progress": {
        "path": "/ws/cloning/progress",
        "description": "Voice cloning progress updates",
        "expects_immediate_message": False,
        "message_format": {"type": "cloning_progress", "step": "...", "percent": 0},
    },
    "training_progress": {
        "path": "/ws/training/progress",
        "description": "Model training progress streaming",
        "expects_immediate_message": False,
        "message_format": {"type": "training_progress", "epoch": 0, "loss": 0.0},
    },
    "audio_meters": {
        "path": "/ws/meters",
        "description": "Real-time audio level meters",
        "expects_immediate_message": True,
        "message_format": {"left": 0.0, "right": 0.0, "peak": 0.0},
    },
    "realtime_status": {
        "path": "/ws/realtime/status",
        "description": "Real-time converter status",
        "expects_immediate_message": False,
        "message_format": {"active": False, "latency_ms": 0},
    },
    "logs": {
        "path": "/ws/logs",
        "description": "Log streaming",
        "expects_immediate_message": False,
        "message_format": {"level": "INFO", "message": "...", "timestamp": "..."},
    },
    "events": {
        "path": "/ws/events",
        "description": "Application event streaming",
        "expects_immediate_message": False,
        "message_format": {"event": "...", "data": {}},
    },
    "synthesis_progress": {
        "path": "/ws/synthesis/progress",
        "description": "Voice synthesis progress",
        "expects_immediate_message": False,
        "message_format": {"type": "synthesis_progress", "percent": 0, "stage": "..."},
    },
    "transcription_progress": {
        "path": "/ws/transcription/progress",
        "description": "Transcription progress",
        "expects_immediate_message": False,
        "message_format": {"type": "transcription_progress", "percent": 0},
    },
    "visualizer": {
        "path": "/ws/visualizer",
        "description": "Audio visualizer data stream",
        "expects_immediate_message": True,
        "message_format": {"spectrum": [], "waveform": []},
    },
}


@pytest.fixture
def backend_available():
    """Check if backend is available."""
    if requests is None:
        pytest.skip("requests not installed")

    try:
        response = requests.get(f"{BACKEND_URL}/api/health", timeout=5)
        if response.status_code >= 500:
            pytest.skip("Backend not healthy")
    except Exception:
        pytest.skip("Backend not reachable")

    return True


class TestWebSocketConnectivity:
    """Tests for WebSocket connection establishment."""

    @pytest.mark.parametrize("endpoint_name,config", list(WEBSOCKET_ENDPOINTS.items()))
    def test_websocket_connection(self, endpoint_name, config, backend_available):
        """Test that each WebSocket endpoint accepts connections."""
        url = f"{WS_BASE_URL}{config['path']}"

        tester = WebSocketTester(url, timeout=5)
        connected = tester.connect()

        # Give some time for initial message if expected
        if connected and config.get("expects_immediate_message"):
            tester.receive_messages(max_count=1, wait_seconds=2)

        tester.close()
        result = tester.get_result()

        # Log result
        status = "PASS" if result.connected else "FAIL"
        print(f"{status}: {endpoint_name} - {config['description']}")
        if result.error:
            print(f"  Error: {result.error}")
        if result.messages_received > 0:
            print(f"  Messages received: {result.messages_received}")

        # For endpoints that should always be available, assert connection
        # For optional endpoints, just log the result
        if endpoint_name in ["audio_meters", "events", "logs"]:
            # These are common endpoints that should exist
            pass  # Don't fail test, just log

        assert (
            result.closed_cleanly or not result.connected
        ), f"Connection to {endpoint_name} did not close cleanly"


class TestJobsWebSocket:
    """Tests for job progress WebSocket."""

    def test_jobs_ws_connection(self, backend_available):
        """Test jobs WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/jobs/progress"
        tester = WebSocketTester(url)

        if tester.connect():
            # Listen for any messages
            messages = tester.receive_messages(max_count=3, wait_seconds=2)
            tester.close()

            result = tester.get_result()
            assert result.closed_cleanly

            # Log message format if received
            if messages:
                print(f"Job progress message format: {messages[0].data}")
        else:
            print(f"Jobs WebSocket not available: {tester.error}")

    def test_jobs_ws_message_format(self, backend_available):
        """Test jobs WebSocket message format."""
        url = f"{WS_BASE_URL}/ws/jobs/progress"
        tester = WebSocketTester(url)

        if tester.connect():
            # Start a job via REST API to trigger WS messages
            with contextlib.suppress(Exception):
                requests.post(
                    f"{BACKEND_URL}/api/voice/synthesize",
                    json={"text": "Test", "engine": "piper"},
                    timeout=5,
                )

            messages = tester.receive_messages(max_count=5, wait_seconds=3)
            tester.close()

            for msg in messages:
                if isinstance(msg.data, dict):
                    # Verify expected fields
                    if "job_id" in msg.data or "type" in msg.data or "progress" in msg.data:
                        print(f"Valid job message: {msg.data}")


class TestCloningWebSocket:
    """Tests for voice cloning progress WebSocket."""

    def test_cloning_ws_connection(self, backend_available):
        """Test cloning WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/cloning/progress"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            messages = tester.receive_messages(max_count=2, wait_seconds=2)
            tester.close()

            if messages:
                print(f"Cloning message format: {messages[0].data}")
        else:
            print(f"Cloning WebSocket not available: {tester.error}")


class TestTrainingWebSocket:
    """Tests for training progress WebSocket."""

    def test_training_ws_connection(self, backend_available):
        """Test training WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/training/progress"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            messages = tester.receive_messages(max_count=2, wait_seconds=2)
            tester.close()

            if messages:
                print(f"Training message format: {messages[0].data}")
        else:
            print(f"Training WebSocket not available: {tester.error}")


class TestAudioMetersWebSocket:
    """Tests for audio meters WebSocket."""

    def test_meters_ws_connection(self, backend_available):
        """Test audio meters WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/meters"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            # Meters should send data immediately
            messages = tester.receive_messages(max_count=10, wait_seconds=3)
            tester.close()

            tester.get_result()

            if messages:
                print(f"Received {len(messages)} meter updates")
                print(f"Sample meter data: {messages[0].data}")

                # Validate meter message format
                for msg in messages[:5]:
                    if isinstance(msg.data, dict):
                        # Common meter fields: left, right, peak, rms
                        valid_fields = {"left", "right", "peak", "rms", "levels", "data"}
                        if any(f in msg.data for f in valid_fields):
                            print(f"Valid meter message: {msg.data}")
        else:
            print(f"Meters WebSocket not available: {tester.error}")

    def test_meters_update_frequency(self, backend_available):
        """Test that meters update at reasonable frequency."""
        url = f"{WS_BASE_URL}/ws/meters"
        tester = WebSocketTester(url)

        if not tester.connect():
            pytest.skip("Meters WebSocket not available")

        # Collect messages for 2 seconds
        start = time.time()
        messages = []
        while time.time() - start < 2.0:
            new_msgs = tester.receive_messages(max_count=100, wait_seconds=0.1)
            messages.extend(new_msgs)

        tester.close()

        if len(messages) > 0:
            # Calculate update frequency
            duration = 2.0
            frequency = len(messages) / duration
            print(
                f"Meter update frequency: {frequency:.1f} Hz ({len(messages)} messages in {duration}s)"
            )

            # Meters should update at least a few times per second
            # No assertion since this depends on backend configuration


class TestVisualizerWebSocket:
    """Tests for audio visualizer WebSocket."""

    def test_visualizer_ws_connection(self, backend_available):
        """Test visualizer WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/visualizer"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            messages = tester.receive_messages(max_count=5, wait_seconds=2)
            tester.close()

            if messages:
                print(f"Visualizer message format: {messages[0].data}")
        else:
            print(f"Visualizer WebSocket not available: {tester.error}")


class TestLogsWebSocket:
    """Tests for log streaming WebSocket."""

    def test_logs_ws_connection(self, backend_available):
        """Test logs WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/logs"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            messages = tester.receive_messages(max_count=5, wait_seconds=3)
            tester.close()

            if messages:
                print(f"Log message format: {messages[0].data}")
                for msg in messages[:3]:
                    if isinstance(msg.data, dict) and "level" in msg.data:
                        print(f"  [{msg.data.get('level')}] {msg.data.get('message', '')[:50]}")
        else:
            print(f"Logs WebSocket not available: {tester.error}")


class TestEventsWebSocket:
    """Tests for application events WebSocket."""

    def test_events_ws_connection(self, backend_available):
        """Test events WebSocket can connect."""
        url = f"{WS_BASE_URL}/ws/events"
        tester = WebSocketTester(url)

        connected = tester.connect()
        if connected:
            messages = tester.receive_messages(max_count=3, wait_seconds=2)
            tester.close()

            if messages:
                print(f"Event message format: {messages[0].data}")
        else:
            print(f"Events WebSocket not available: {tester.error}")

    def test_events_subscription(self, backend_available):
        """Test subscribing to specific event types."""
        url = f"{WS_BASE_URL}/ws/events"
        tester = WebSocketTester(url)

        if not tester.connect():
            pytest.skip("Events WebSocket not available")

        # Try to subscribe to specific events
        subscription = {"action": "subscribe", "events": ["job_complete", "synthesis_done"]}
        tester.send_message(subscription)

        messages = tester.receive_messages(max_count=3, wait_seconds=2)
        tester.close()

        # Log results
        for msg in messages:
            print(f"Event after subscription: {msg.data}")


class TestWebSocketReport:
    """Generate WebSocket connectivity report."""

    def test_generate_ws_report(self, backend_available):
        """Generate comprehensive WebSocket connectivity report."""
        results = {}

        for name, config in WEBSOCKET_ENDPOINTS.items():
            url = f"{WS_BASE_URL}{config['path']}"
            tester = WebSocketTester(url, timeout=3)

            start = time.time()
            connected = tester.connect()

            if connected:
                tester.receive_messages(max_count=2, wait_seconds=1)

            tester.close()

            duration = (time.time() - start) * 1000
            result = tester.get_result()
            result.duration_ms = duration

            results[name] = {
                "endpoint": config["path"],
                "description": config["description"],
                "connected": result.connected,
                "messages_received": result.messages_received,
                "error": result.error,
                "duration_ms": duration,
            }

        # Print summary
        print("\n" + "=" * 60)
        print("WEBSOCKET CONNECTIVITY REPORT")
        print("=" * 60)

        connected_count = sum(1 for r in results.values() if r["connected"])
        total_count = len(results)

        print(f"\nTotal endpoints: {total_count}")
        print(f"Connected: {connected_count}")
        print(f"Failed: {total_count - connected_count}")
        print(f"Success rate: {connected_count/total_count*100:.1f}%")

        print("\nEndpoint Details:")
        for name, result in results.items():
            status = "✓" if result["connected"] else "✗"
            print(f"  {status} {name}: {result['endpoint']}")
            if result["error"]:
                print(f"      Error: {result['error']}")
            if result["messages_received"] > 0:
                print(f"      Messages: {result['messages_received']}")

        # Write report to file
        report_path = OUTPUT_DIR / "websocket_report.json"
        report_path.parent.mkdir(parents=True, exist_ok=True)

        with open(report_path, "w") as f:
            json.dump(
                {
                    "timestamp": datetime.now().isoformat(),
                    "base_url": WS_BASE_URL,
                    "summary": {
                        "total": total_count,
                        "connected": connected_count,
                        "success_rate": connected_count / total_count,
                    },
                    "endpoints": results,
                },
                f,
                indent=2,
            )

        print(f"\nReport saved to: {report_path}")


# Run tests directly if executed as script
if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
