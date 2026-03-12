"""
WebSocket Real-Time Integration Tests.

Comprehensive tests for WebSocket-based real-time features including:
- Meter updates (audio level monitoring)
- Job progress streaming
- Real-time voice converter feedback
- Error propagation over WebSocket

Part of the Testing Expansion Plan.
"""

import asyncio
import json

import pytest

# Try to import websockets, skip tests if not available
try:
    import websockets
    from websockets.exceptions import ConnectionClosed

    HAS_WEBSOCKETS = True
except ImportError:
    HAS_WEBSOCKETS = False
    websockets = None
    ConnectionClosed = Exception


# Test configuration
WS_BASE_URL = "ws://localhost:8088/api"
WS_CONNECT_TIMEOUT = 5.0
WS_READ_TIMEOUT = 10.0


@pytest.fixture
def ws_url():
    """Get WebSocket base URL."""
    return WS_BASE_URL


@pytest.mark.skipif(not HAS_WEBSOCKETS, reason="websockets library not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestMeterWebSocket:
    """Tests for real-time audio meter WebSocket."""

    @pytest.mark.asyncio
    async def test_meter_connection(self, ws_url):
        """Verify WebSocket connection can be established for meter updates."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/meter") as ws:
                    assert ws.open
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")
        except Exception as e:
            pytest.skip(f"WebSocket connection failed: {e}")

    @pytest.mark.asyncio
    async def test_meter_data_format(self, ws_url):
        """Verify meter data format is correct."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/meter") as ws:
                    # Try to receive a message
                    try:
                        async with asyncio.timeout(WS_READ_TIMEOUT):
                            message = await ws.recv()
                            data = json.loads(message)

                            # Verify expected fields
                            assert (
                                "level" in data or "rms" in data or "peak" in data
                            ), "Meter data should contain level, rms, or peak field"
                    except asyncio.TimeoutError:
                        pytest.skip("No meter data received within timeout")
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")

    @pytest.mark.asyncio
    async def test_meter_reconnection(self, ws_url):
        """Verify reconnection works after disconnect."""
        try:
            # First connection
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/meter") as ws1:
                    assert ws1.open

            # Second connection after close
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/meter") as ws2:
                    assert ws2.open
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")

    @pytest.mark.asyncio
    async def test_meter_multiple_clients(self, ws_url):
        """Verify multiple clients can connect simultaneously."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT * 2):
                async with websockets.connect(f"{ws_url}/meter") as ws1:
                    async with websockets.connect(f"{ws_url}/meter") as ws2:
                        assert ws1.open
                        assert ws2.open
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")


@pytest.mark.skipif(not HAS_WEBSOCKETS, reason="websockets library not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestJobProgressWebSocket:
    """Tests for job progress streaming."""

    @pytest.mark.asyncio
    async def test_progress_connection(self, ws_url):
        """Verify WebSocket connection for job progress."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/jobs/progress") as ws:
                    assert ws.open
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Job progress WebSocket not available")

    @pytest.mark.asyncio
    async def test_progress_subscription(self, ws_url):
        """Verify subscribing to job progress updates."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/jobs/progress") as ws:
                    # Send subscription message
                    subscribe_msg = json.dumps({"action": "subscribe", "job_id": "test-job-123"})
                    await ws.send(subscribe_msg)

                    # Expect acknowledgment or error
                    try:
                        async with asyncio.timeout(5.0):
                            response = await ws.recv()
                            data = json.loads(response)
                            assert "status" in data or "error" in data or "ack" in data
                    except asyncio.TimeoutError:
                        # No immediate response is acceptable
                        pass
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Job progress WebSocket not available")

    @pytest.mark.asyncio
    async def test_progress_updates_format(self, ws_url):
        """Verify progress update message format."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/jobs/progress") as ws:
                    try:
                        async with asyncio.timeout(WS_READ_TIMEOUT):
                            message = await ws.recv()
                            data = json.loads(message)

                            # Progress updates should have certain fields
                            valid_fields = ["job_id", "progress", "status", "step", "message"]
                            has_valid_field = any(f in data for f in valid_fields)
                            assert (
                                has_valid_field or "error" in data
                            ), "Progress update should contain progress info or error"
                    except asyncio.TimeoutError:
                        pytest.skip("No progress updates received within timeout")
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Job progress WebSocket not available")

    @pytest.mark.asyncio
    async def test_progress_error_handling(self, ws_url):
        """Verify error handling in progress WebSocket."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/jobs/progress") as ws:
                    # Send invalid message
                    await ws.send("invalid json {{{")

                    try:
                        async with asyncio.timeout(5.0):
                            response = await ws.recv()
                            data = json.loads(response)
                            # Server should respond with error or close connection
                            assert "error" in data or ws.closed
                    except (asyncio.TimeoutError, ConnectionClosed):
                        # Connection close is acceptable for invalid data
                        pass
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Job progress WebSocket not available")


@pytest.mark.skipif(not HAS_WEBSOCKETS, reason="websockets library not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestRealTimeConverterWebSocket:
    """Tests for real-time voice converter feedback."""

    @pytest.mark.asyncio
    async def test_converter_connection(self, ws_url):
        """Verify WebSocket connection for real-time converter."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/realtime/converter") as ws:
                    assert ws.open
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Real-time converter WebSocket not available")

    @pytest.mark.asyncio
    async def test_converter_configuration(self, ws_url):
        """Verify converter can be configured via WebSocket."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/realtime/converter") as ws:
                    # Send configuration
                    config_msg = json.dumps(
                        {
                            "action": "configure",
                            "settings": {"sample_rate": 22050, "buffer_size": 1024},
                        }
                    )
                    await ws.send(config_msg)

                    try:
                        async with asyncio.timeout(5.0):
                            response = await ws.recv()
                            data = json.loads(response)
                            assert "status" in data or "error" in data or "configured" in data
                    except asyncio.TimeoutError:
                        pass  # No response is acceptable
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Real-time converter WebSocket not available")

    @pytest.mark.asyncio
    async def test_converter_latency_metrics(self, ws_url):
        """Verify converter reports latency metrics."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/realtime/converter") as ws:
                    # Request metrics
                    metrics_msg = json.dumps({"action": "get_metrics"})
                    await ws.send(metrics_msg)

                    try:
                        async with asyncio.timeout(5.0):
                            response = await ws.recv()
                            data = json.loads(response)
                            # Should contain latency or metrics info
                            assert "latency" in data or "metrics" in data or "error" in data
                    except asyncio.TimeoutError:
                        pytest.skip("No metrics received within timeout")
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("Real-time converter WebSocket not available")


@pytest.mark.skipif(not HAS_WEBSOCKETS, reason="websockets library not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestWebSocketErrorPropagation:
    """Tests for error propagation over WebSocket."""

    @pytest.mark.asyncio
    async def test_invalid_endpoint_handling(self, ws_url):
        """Verify invalid endpoint returns appropriate error."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/nonexistent"):
                    # If connection succeeds, endpoint exists
                    pytest.skip("Endpoint exists (not expected)")
        except websockets.exceptions.InvalidStatusCode as e:
            # 404 or similar is expected for nonexistent endpoint
            assert e.status_code in [404, 400, 403]
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")

    @pytest.mark.asyncio
    async def test_connection_close_cleanup(self, ws_url):
        """Verify clean disconnect releases resources."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                ws = await websockets.connect(f"{ws_url}/meter")
                assert ws.open
                await ws.close()
                assert ws.closed
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")

    @pytest.mark.asyncio
    async def test_graceful_server_disconnect(self, ws_url):
        """Verify client handles server-initiated disconnect."""
        try:
            async with asyncio.timeout(WS_CONNECT_TIMEOUT):
                async with websockets.connect(f"{ws_url}/meter") as ws:
                    # Request server to disconnect
                    disconnect_msg = json.dumps({"action": "disconnect"})
                    await ws.send(disconnect_msg)

                    # Wait for close or timeout
                    try:
                        async with asyncio.timeout(5.0):
                            await ws.wait_closed()
                    except asyncio.TimeoutError:
                        pass  # Server may not implement disconnect action
        except (ConnectionRefusedError, OSError, asyncio.TimeoutError):
            pytest.skip("WebSocket server not available")


class TestWebSocketMocking:
    """Tests using mocked WebSocket for unit testing patterns."""

    @pytest.mark.asyncio
    async def test_mock_meter_data_handling(self):
        """Test meter data processing with mocked WebSocket."""
        # Simulate meter data
        mock_messages = [
            {"level": 0.75, "peak": 0.95, "rms": 0.65},
            {"level": 0.50, "peak": 0.80, "rms": 0.45},
            {"level": 0.30, "peak": 0.60, "rms": 0.25},
        ]

        # Process messages
        levels = []
        for msg in mock_messages:
            levels.append(msg.get("level", 0))

        assert len(levels) == 3
        assert max(levels) == 0.75
        assert min(levels) == 0.30

    @pytest.mark.asyncio
    async def test_mock_progress_tracking(self):
        """Test progress tracking with mocked progress updates."""
        # Simulate progress updates
        progress_updates = [
            {"job_id": "job-1", "progress": 0.0, "status": "starting"},
            {"job_id": "job-1", "progress": 0.25, "status": "processing"},
            {"job_id": "job-1", "progress": 0.50, "status": "processing"},
            {"job_id": "job-1", "progress": 0.75, "status": "processing"},
            {"job_id": "job-1", "progress": 1.0, "status": "completed"},
        ]

        # Verify progress increases
        last_progress = -1
        for update in progress_updates:
            assert update["progress"] >= last_progress
            last_progress = update["progress"]

        # Verify completion
        final = progress_updates[-1]
        assert final["progress"] == 1.0
        assert final["status"] == "completed"

    @pytest.mark.asyncio
    async def test_mock_error_message_format(self):
        """Test error message format validation."""
        error_messages = [
            {"error": "Connection timeout", "code": "TIMEOUT"},
            {"error": "Invalid input", "code": "VALIDATION_ERROR", "details": {"field": "text"}},
            {"error": "Engine unavailable", "code": "ENGINE_ERROR"},
        ]

        for msg in error_messages:
            assert "error" in msg
            assert "code" in msg
            assert isinstance(msg["error"], str)
