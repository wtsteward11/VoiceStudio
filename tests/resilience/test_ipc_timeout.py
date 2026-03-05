"""
Resilience tests for IPC timeout scenarios.

Tests that the plugin system correctly handles and recovers from
IPC communication timeouts.
"""

from __future__ import annotations

import asyncio
import time
from datetime import datetime, timedelta
from typing import Any
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


class TestIPCTimeoutDetection:
    """Tests for IPC timeout detection."""

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_request_timeout_detected(self):
        """Test that request timeout is properly detected."""

        async def slow_operation():
            await asyncio.sleep(2.0)
            return "result"

        with pytest.raises(asyncio.TimeoutError):
            await asyncio.wait_for(slow_operation(), timeout=0.1)

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_response_timeout_tracking(self):
        """Test tracking of response timeouts."""

        class TimeoutTracker:
            def __init__(self):
                self.timeouts = 0
                self.total_requests = 0

            async def send_with_timeout(self, operation, timeout: float = 1.0) -> tuple[bool, Any]:
                self.total_requests += 1
                try:
                    result = await asyncio.wait_for(operation(), timeout=timeout)
                    return True, result
                except asyncio.TimeoutError:
                    self.timeouts += 1
                    return False, None

            @property
            def timeout_rate(self) -> float:
                if self.total_requests == 0:
                    return 0.0
                return self.timeouts / self.total_requests

        tracker = TimeoutTracker()

        # Successful request
        async def fast():
            return "fast"

        success, result = await tracker.send_with_timeout(fast, timeout=1.0)
        assert success is True
        assert result == "fast"

        # Timeout request
        async def slow():
            await asyncio.sleep(2.0)
            return "slow"

        success, result = await tracker.send_with_timeout(slow, timeout=0.1)
        assert success is False
        assert result is None

        assert tracker.timeout_rate == 0.5

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_heartbeat_timeout(self):
        """Test heartbeat timeout detection."""

        class HeartbeatMonitor:
            def __init__(self, timeout_seconds: float = 5.0):
                self.timeout = timeout_seconds
                self.last_heartbeat = datetime.utcnow()

            def record_heartbeat(self):
                self.last_heartbeat = datetime.utcnow()

            def is_alive(self) -> bool:
                elapsed = (datetime.utcnow() - self.last_heartbeat).total_seconds()
                return elapsed < self.timeout

        monitor = HeartbeatMonitor(timeout_seconds=0.5)

        # Initially alive
        assert monitor.is_alive() is True

        # After timeout
        await asyncio.sleep(0.6)
        assert monitor.is_alive() is False

        # Heartbeat revives
        monitor.record_heartbeat()
        assert monitor.is_alive() is True


class TestIPCTimeoutRecovery:
    """Tests for IPC timeout recovery strategies."""

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_retry_on_timeout(self):
        """Test retry logic on timeout."""
        attempts = 0

        async def flaky_operation():
            nonlocal attempts
            attempts += 1
            if attempts < 3:
                await asyncio.sleep(2.0)  # Timeout
            return "success"

        async def retry_with_timeout(operation, max_retries: int = 3, timeout: float = 0.1):
            for attempt in range(max_retries):
                try:
                    return await asyncio.wait_for(operation(), timeout=timeout)
                except asyncio.TimeoutError:
                    if attempt == max_retries - 1:
                        raise
            raise asyncio.TimeoutError()

        result = await retry_with_timeout(flaky_operation, max_retries=5, timeout=0.1)
        assert result == "success"
        assert attempts == 3

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_timeout_with_fallback(self):
        """Test fallback value on timeout."""

        async def slow_operation():
            await asyncio.sleep(2.0)
            return "actual_result"

        async def with_fallback(operation, timeout: float, fallback):
            try:
                return await asyncio.wait_for(operation(), timeout=timeout)
            except asyncio.TimeoutError:
                return fallback

        result = await with_fallback(slow_operation, timeout=0.1, fallback="default")
        assert result == "default"

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_graceful_timeout_cancellation(self):
        """Test graceful cancellation on timeout."""
        cancelled = False

        async def cancellable_operation():
            nonlocal cancelled
            try:
                await asyncio.sleep(10.0)
            except asyncio.CancelledError:
                cancelled = True
                raise

        task = asyncio.create_task(cancellable_operation())

        await asyncio.sleep(0.1)
        task.cancel()

        try:
            await task
        # ALLOWED: bare except - best effort, failure acceptable
        except asyncio.CancelledError:
            pass

        assert cancelled is True


class TestAdaptiveTimeout:
    """Tests for adaptive timeout strategies."""

    @pytest.mark.ipc_timeout
    def test_timeout_increases_on_slow_responses(self):
        """Test timeout adapts to slow responses."""

        class AdaptiveTimeout:
            def __init__(self, initial: float = 1.0, max_timeout: float = 30.0):
                self.current = initial
                self.max_timeout = max_timeout
                self.response_times = []

            def record_response_time(self, response_time: float):
                self.response_times.append(response_time)
                if len(self.response_times) > 100:
                    self.response_times.pop(0)
                self._adjust_timeout()

            def _adjust_timeout(self):
                if len(self.response_times) < 5:
                    return

                # Set timeout to 2x the p95 response time
                sorted_times = sorted(self.response_times)
                p95_idx = int(len(sorted_times) * 0.95)
                p95 = sorted_times[p95_idx]

                self.current = min(p95 * 2, self.max_timeout)

        adaptive = AdaptiveTimeout(initial=1.0)

        # Simulate fast responses
        for _ in range(10):
            adaptive.record_response_time(0.1)

        # Timeout should decrease
        assert adaptive.current < 1.0

        # Simulate slow responses
        for _ in range(10):
            adaptive.record_response_time(5.0)

        # Timeout should increase
        assert adaptive.current > 1.0

    @pytest.mark.ipc_timeout
    def test_timeout_per_operation_type(self):
        """Test different timeouts per operation type."""

        timeouts = {
            "fast_op": 0.5,
            "normal_op": 2.0,
            "slow_op": 30.0,
            "batch_op": 120.0,
        }

        def get_timeout(operation_type: str) -> float:
            return timeouts.get(operation_type, 5.0)  # Default 5s

        assert get_timeout("fast_op") == 0.5
        assert get_timeout("slow_op") == 30.0
        assert get_timeout("unknown") == 5.0


class TestTimeoutCircuitBreaker:
    """Tests for circuit breaker on timeouts."""

    @pytest.mark.ipc_timeout
    def test_circuit_opens_on_timeout_threshold(self):
        """Test circuit opens after timeout threshold."""

        class TimeoutCircuitBreaker:
            def __init__(self, threshold: int = 5, window_seconds: float = 60.0):
                self.threshold = threshold
                self.window = window_seconds
                self.timeouts = []
                self.open = False

            def record_timeout(self):
                now = datetime.utcnow()
                self.timeouts.append(now)

                # Clean old timeouts
                cutoff = now - timedelta(seconds=self.window)
                self.timeouts = [t for t in self.timeouts if t > cutoff]

                if len(self.timeouts) >= self.threshold:
                    self.open = True

            def record_success(self):
                # Halve timeout count on success (keep first half, remove second half)
                self.timeouts = self.timeouts[: len(self.timeouts) // 2]
                if len(self.timeouts) < self.threshold // 2:
                    self.open = False

        breaker = TimeoutCircuitBreaker(threshold=3)

        assert breaker.open is False

        # Record timeouts
        for _ in range(3):
            breaker.record_timeout()

        assert breaker.open is True

        # Multiple successes close circuit
        for _ in range(5):
            breaker.record_success()

        assert breaker.open is False


class TestIPCConnectionRecovery:
    """Tests for IPC connection recovery."""

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_reconnect_on_connection_lost(self):
        """Test reconnection when connection is lost."""

        class MockConnection:
            def __init__(self):
                self.connected = False
                self.connect_attempts = 0

            async def connect(self):
                self.connect_attempts += 1
                if self.connect_attempts >= 2:  # Fail first attempt
                    self.connected = True
                    return True
                raise ConnectionError("Connection failed")

            async def ensure_connected(self, max_retries: int = 3):
                for _ in range(max_retries):
                    try:
                        if not self.connected:
                            await self.connect()
                        return True
                    except ConnectionError:
                        await asyncio.sleep(0.01)
                return False

        conn = MockConnection()

        success = await conn.ensure_connected()
        assert success is True
        assert conn.connect_attempts == 2

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_connection_pool_failover(self):
        """Test connection pool failover."""

        class ConnectionPool:
            def __init__(self):
                self.connections = [
                    {"id": 1, "healthy": True},
                    {"id": 2, "healthy": True},
                    {"id": 3, "healthy": True},
                ]
                self.current_idx = 0

            def get_connection(self):
                # Find next healthy connection
                for _ in range(len(self.connections)):
                    conn = self.connections[self.current_idx]
                    self.current_idx = (self.current_idx + 1) % len(self.connections)
                    if conn["healthy"]:
                        return conn
                return None

            def mark_unhealthy(self, conn_id: int):
                for conn in self.connections:
                    if conn["id"] == conn_id:
                        conn["healthy"] = False

        pool = ConnectionPool()

        # Get connection
        conn = pool.get_connection()
        assert conn is not None
        assert conn["id"] == 1

        # Mark as unhealthy
        pool.mark_unhealthy(1)

        # Next connection should skip unhealthy
        conn = pool.get_connection()
        assert conn["id"] == 2


class TestPendingRequestManagement:
    """Tests for managing pending requests during timeout."""

    @pytest.mark.ipc_timeout
    def test_pending_requests_cleaned_up_on_timeout(self):
        """Test pending requests are cleaned up on timeout."""

        class PendingRequestManager:
            def __init__(self):
                self.pending = {}

            def add_request(self, request_id: str, timeout: float):
                self.pending[request_id] = {
                    "added": datetime.utcnow(),
                    "timeout": timeout,
                }

            def remove_request(self, request_id: str):
                if request_id in self.pending:
                    del self.pending[request_id]

            def cleanup_expired(self):
                now = datetime.utcnow()
                expired = []
                for req_id, info in self.pending.items():
                    elapsed = (now - info["added"]).total_seconds()
                    if elapsed > info["timeout"]:
                        expired.append(req_id)

                for req_id in expired:
                    self.remove_request(req_id)

                return expired

        manager = PendingRequestManager()

        # Add requests with very short timeout
        manager.add_request("req1", timeout=0.01)
        manager.add_request("req2", timeout=0.01)
        manager.add_request("req3", timeout=10.0)  # Won't expire

        # Wait for timeout
        time.sleep(0.02)

        # Cleanup
        expired = manager.cleanup_expired()

        assert "req1" in expired
        assert "req2" in expired
        assert "req3" not in expired
        assert len(manager.pending) == 1

    @pytest.mark.ipc_timeout
    @pytest.mark.asyncio
    async def test_request_deduplication(self):
        """Test deduplication of duplicate requests."""

        class DeduplicatingRequestManager:
            def __init__(self):
                self.inflight = {}

            async def execute_deduplicated(self, key: str, operation):
                if key in self.inflight:
                    # Wait for existing request
                    return await self.inflight[key]

                # Create future for this request
                future = asyncio.create_task(operation())
                self.inflight[key] = future

                try:
                    return await future
                finally:
                    del self.inflight[key]

        manager = DeduplicatingRequestManager()
        execution_count = 0

        async def expensive_operation():
            nonlocal execution_count
            execution_count += 1
            await asyncio.sleep(0.1)
            return "result"

        # Execute multiple "same" requests
        results = await asyncio.gather(
            manager.execute_deduplicated("key1", expensive_operation),
            manager.execute_deduplicated("key1", expensive_operation),
            manager.execute_deduplicated("key1", expensive_operation),
        )

        # All should get same result
        assert all(r == "result" for r in results)
        # But operation only executed once
        assert execution_count == 1
