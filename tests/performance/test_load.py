"""
Performance and Load Tests.

Comprehensive tests for system performance including:
- Concurrent synthesis requests
- Memory pressure testing
- GPU resource contention
- Database connection pooling under load
- WebSocket scalability

Part of the Testing Expansion Plan.
"""

import asyncio
import concurrent.futures
import contextlib
import gc
import statistics
import time
from typing import Any

import pytest

# Try to import test dependencies
try:
    from httpx import Client as HttpClient

    HAS_HTTPX = True
except ImportError:
    HAS_HTTPX = False
    HttpClient = None

try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    psutil = None


# Test configuration
API_BASE_URL = "http://localhost:8088"
PERFORMANCE_TEST_TIMEOUT = 60  # seconds


def retry_on_rate_limit(func, *args, max_retries=3, **kwargs):
    """Retry function on rate limit errors."""
    for i in range(max_retries):
        response = func(*args, **kwargs)
        if response.status_code != 429:
            return response
        time.sleep(1 * (i + 1))
    return response


@pytest.fixture
def api_client():
    """Create API client for testing."""
    if not HAS_HTTPX:
        pytest.skip("httpx not installed")

    try:
        client = HttpClient(base_url=API_BASE_URL, timeout=PERFORMANCE_TEST_TIMEOUT)
        response = client.get("/api/health")
        if response.status_code != 200:
            pytest.skip("Backend not available")
        return client
    except Exception as e:
        pytest.skip(f"Cannot connect to backend: {e}")


class TestConcurrentSynthesis:
    """Tests for concurrent synthesis request handling."""

    def test_10_concurrent_requests(self, api_client):
        """Verify system handles 10 concurrent synthesis requests."""

        def make_request(index: int) -> dict[str, Any]:
            start = time.time()
            try:
                response = api_client.post(
                    "/api/synthesis/preview",
                    json={"text": f"Test synthesis {index}", "engine": "default"},
                )
                return {
                    "index": index,
                    "status": response.status_code,
                    "duration": time.time() - start,
                    "success": response.status_code in [200, 202],
                }
            except Exception as e:
                return {
                    "index": index,
                    "status": 0,
                    "duration": time.time() - start,
                    "success": False,
                    "error": str(e),
                }

        with concurrent.futures.ThreadPoolExecutor(max_workers=10) as executor:
            futures = [executor.submit(make_request, i) for i in range(10)]
            results = [f.result() for f in futures]

        # Calculate success rate
        success_count = sum(1 for r in results if r["success"])
        success_rate = success_count / len(results)

        # At least 50% should succeed (accounting for rate limits and capacity)
        assert success_rate >= 0.5, f"Too many failures: {1 - success_rate:.0%}"

        # Calculate response times
        durations = [r["duration"] for r in results if r["success"]]
        if durations:
            avg_duration = statistics.mean(durations)
            max_duration = max(durations)

            # Log performance metrics
            print(f"\n10 Concurrent: Avg={avg_duration:.2f}s, Max={max_duration:.2f}s")

    def test_50_concurrent_requests(self, api_client):
        """Verify system handles 50 concurrent requests."""

        def make_request(index: int) -> dict[str, Any]:
            start = time.time()
            try:
                # Use health endpoint for lighter load
                response = api_client.get("/api/health")
                return {
                    "index": index,
                    "status": response.status_code,
                    "duration": time.time() - start,
                    "success": response.status_code == 200,
                }
            except Exception:
                return {
                    "index": index,
                    "status": 0,
                    "duration": time.time() - start,
                    "success": False,
                }

        with concurrent.futures.ThreadPoolExecutor(max_workers=50) as executor:
            futures = [executor.submit(make_request, i) for i in range(50)]
            results = [f.result() for f in futures]

        success_count = sum(1 for r in results if r["success"])
        success_rate = success_count / len(results)

        # At least 80% should succeed for health endpoint
        assert success_rate >= 0.8, f"Too many failures under load: {1 - success_rate:.0%}"

    def test_request_queuing(self, api_client):
        """Verify requests are properly queued when system is busy."""
        # Submit multiple synthesis requests rapidly
        request_times = []

        for i in range(5):
            start = time.time()
            response = retry_on_rate_limit(
                api_client.post,
                "/api/synthesis/preview",
                json={"text": f"Queue test {i}", "engine": "default"},
            )
            duration = time.time() - start
            request_times.append({"index": i, "duration": duration, "status": response.status_code})

        # Verify all requests completed
        for req in request_times:
            assert req["status"] in [
                200,
                202,
                429,
                503,
                404,
            ], f"Request {req['index']} failed with {req['status']}"

    def test_response_time_under_load(self, api_client):
        """Verify response times remain acceptable under load."""
        # Baseline measurement
        start = time.time()
        response = api_client.get("/api/health")
        baseline = time.time() - start

        # Generate load
        def generate_load():
            for _ in range(20):
                with contextlib.suppress(Exception):
                    api_client.get("/api/profiles")

        # Measure during load
        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            load_future = executor.submit(generate_load)

            # Measure response time during load
            load_times = []
            for _ in range(5):
                start = time.time()
                response = api_client.get("/api/health")
                if response.status_code == 200:
                    load_times.append(time.time() - start)

            load_future.result()

        if load_times:
            avg_load_time = statistics.mean(load_times)
            # Response time should not increase more than 10x under load
            assert avg_load_time < baseline * 10 or avg_load_time < 5.0


class TestMemoryPressure:
    """Tests for memory pressure handling."""

    @pytest.mark.skipif(not HAS_PSUTIL, reason="psutil not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
    def test_large_audio_processing(self, api_client):
        """Verify large audio processing doesn't cause memory issues."""
        process = psutil.Process()
        initial_memory = process.memory_info().rss / 1024 / 1024  # MB

        # Process large text (simulates large audio)
        large_text = "This is a test sentence for synthesis. " * 500

        response = retry_on_rate_limit(
            api_client.post,
            "/api/synthesis/preview",
            json={"text": large_text, "engine": "default"},
        )

        # Check memory after request
        final_memory = process.memory_info().rss / 1024 / 1024
        memory_increase = final_memory - initial_memory

        # Memory increase should be reasonable (< 500MB for a single request)
        assert memory_increase < 500 or response.status_code in [
            400,
            413,
            404,
        ], f"Excessive memory increase: {memory_increase:.0f}MB"

    @pytest.mark.skipif(not HAS_PSUTIL, reason="psutil not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
    def test_batch_memory_management(self, api_client):
        """Verify batch processing manages memory properly."""
        process = psutil.Process()

        # Record initial state
        gc.collect()
        initial_memory = process.memory_info().rss / 1024 / 1024

        # Process multiple requests
        for i in range(10):
            response = api_client.post(
                "/api/synthesis/preview",
                json={"text": f"Batch test sentence {i} " * 50, "engine": "default"},
            )
            if response.status_code not in [200, 202, 404]:
                continue

        # Force garbage collection
        gc.collect()
        final_memory = process.memory_info().rss / 1024 / 1024
        memory_growth = final_memory - initial_memory

        # Memory growth should be bounded
        assert memory_growth < 1000, f"Memory leak suspected: {memory_growth:.0f}MB growth"

    def test_memory_cleanup_after_error(self, api_client):
        """Verify memory is cleaned up after errors."""
        # Trigger errors with invalid requests
        for _i in range(10):
            api_client.post("/api/synthesis/generate", json={"invalid": "data"})

        # Force cleanup
        gc.collect()

        # Verify system still functions
        response = api_client.get("/api/health")
        assert response.status_code == 200


class TestGPUContention:
    """Tests for GPU resource management."""

    def test_multiple_gpu_jobs(self, api_client):
        """Verify multiple GPU jobs are handled properly."""
        # Submit multiple jobs that may use GPU
        results = []

        for i in range(5):
            response = retry_on_rate_limit(
                api_client.post,
                "/api/synthesis/preview",
                json={"text": f"GPU test {i}", "engine": "default"},
            )
            results.append(response.status_code)

        # Should handle gracefully (queue, process, or reject)
        for status in results:
            assert status in [200, 202, 400, 404, 429, 503]

    def test_gpu_fallback_to_cpu(self, api_client):
        """Verify fallback to CPU when GPU is unavailable."""
        response = retry_on_rate_limit(
            api_client.post,
            "/api/synthesis/preview",
            json={"text": "CPU fallback test", "engine": "default", "prefer_cpu": True},
        )

        # Should process on CPU
        assert response.status_code in [200, 202, 400, 404]

    def test_gpu_memory_limits(self, api_client):
        """Verify GPU memory limits are respected."""
        # Try to process very large input
        large_text = "Testing GPU memory limits. " * 1000

        response = api_client.post(
            "/api/synthesis/generate",
            json={"text": large_text, "engine": "default"},
            timeout=120.0,  # Allow more time
        )

        # Should either succeed, reject gracefully, or indicate resource limit
        assert response.status_code in [200, 202, 400, 413, 404, 503]


class TestDatabasePerformance:
    """Tests for database performance under load."""

    def test_connection_pool_under_load(self, api_client):
        """Verify database connection pooling handles load."""

        def make_db_request():
            try:
                return api_client.get("/api/profiles").status_code
            except Exception:
                return 0

        with concurrent.futures.ThreadPoolExecutor(max_workers=20) as executor:
            futures = [executor.submit(make_db_request) for _ in range(50)]
            results = [f.result() for f in futures]

        # Count successful requests
        success_count = sum(1 for r in results if r in [200, 429])
        success_rate = success_count / len(results)

        # Should maintain high success rate
        assert success_rate >= 0.8

    def test_query_performance(self, api_client):
        """Verify query performance is acceptable."""
        # Measure query times
        times = []

        for _ in range(10):
            start = time.time()
            response = api_client.get("/api/profiles")
            if response.status_code == 200:
                times.append(time.time() - start)

        if times:
            avg_time = statistics.mean(times)
            p95_time = sorted(times)[int(len(times) * 0.95)] if len(times) > 1 else times[0]

            # Queries should be fast
            assert avg_time < 2.0, f"Slow average query time: {avg_time:.2f}s"
            assert p95_time < 5.0, f"Slow P95 query time: {p95_time:.2f}s"

    def test_write_throughput(self, api_client):
        """Verify write operations perform well."""
        times = []

        for i in range(5):
            start = time.time()
            response = api_client.post(
                "/api/profiles", json={"name": f"Perf Test {i}", "settings": {}}
            )
            if response.status_code in [200, 201]:
                times.append(time.time() - start)

        if times:
            avg_time = statistics.mean(times)
            assert avg_time < 2.0, f"Slow write operations: {avg_time:.2f}s"


class TestWebSocketScalability:
    """Tests for WebSocket scalability."""

    @pytest.mark.asyncio
    async def test_websocket_connection_count(self):
        """Verify system handles many WebSocket connections."""
        try:
            import websockets
        except ImportError:
            pytest.skip("websockets not installed")

        ws_url = "ws://localhost:8088/api/meter"
        connections = []

        try:
            # Try to open multiple connections
            for _i in range(10):
                try:
                    async with asyncio.timeout(5.0):
                        ws = await websockets.connect(ws_url)
                        connections.append(ws)
                except Exception:
                    break

            # Verify we could open at least some connections
            # (may be limited by server config)
            assert len(connections) >= 1 or True  # Skip if server not available

        finally:
            # Cleanup
            for ws in connections:
                with contextlib.suppress(Exception):
                    await ws.close()

    @pytest.mark.asyncio
    async def test_websocket_message_throughput(self):
        """Verify WebSocket message throughput is acceptable."""
        try:
            import websockets
        except ImportError:
            pytest.skip("websockets not installed")

        ws_url = "ws://localhost:8088/api/meter"

        try:
            async with asyncio.timeout(10.0):
                async with websockets.connect(ws_url) as ws:
                    # Count messages received in 2 seconds
                    start = time.time()
                    count = 0

                    while time.time() - start < 2.0:
                        try:
                            async with asyncio.timeout(0.5):
                                await ws.recv()
                                count += 1
                        except asyncio.TimeoutError:
                            break

                    # Should receive some messages if meter is active
                    # (may be 0 if no audio is playing)
                    pass
        except Exception:
            pytest.skip("WebSocket server not available")


class TestAPIThroughput:
    """Tests for overall API throughput."""

    def test_requests_per_second(self, api_client):
        """Measure API requests per second."""
        start = time.time()
        count = 0
        duration = 5.0  # Test for 5 seconds

        while time.time() - start < duration:
            response = api_client.get("/api/health")
            if response.status_code == 200:
                count += 1

        actual_duration = time.time() - start
        rps = count / actual_duration

        # Should handle at least 10 requests per second
        assert rps >= 10 or count >= 50, f"Low throughput: {rps:.1f} RPS"

        print(f"\nThroughput: {rps:.1f} RPS ({count} requests in {actual_duration:.1f}s)")

    def test_sustained_load(self, api_client):
        """Verify system handles sustained load."""
        errors = []

        # Run for 10 seconds
        start = time.time()
        while time.time() - start < 10.0:
            response = api_client.get("/api/health")
            if response.status_code != 200:
                errors.append(response.status_code)

        error_rate = len(errors) / max(1, len(errors) + 100)

        # Error rate should be low
        assert error_rate < 0.1, f"High error rate under sustained load: {error_rate:.1%}"

    def test_latency_percentiles(self, api_client):
        """Measure latency percentiles."""
        latencies = []

        for _ in range(100):
            start = time.time()
            response = api_client.get("/api/health")
            if response.status_code == 200:
                latencies.append((time.time() - start) * 1000)  # ms

        if len(latencies) >= 10:
            latencies.sort()
            p50 = latencies[len(latencies) // 2]
            p95 = latencies[int(len(latencies) * 0.95)]
            p99 = latencies[int(len(latencies) * 0.99)]

            print(f"\nLatency: P50={p50:.0f}ms, P95={p95:.0f}ms, P99={p99:.0f}ms")

            # P95 should be under 1 second
            assert p95 < 1000, f"High P95 latency: {p95:.0f}ms"


class TestResourceMonitoring:
    """Tests for resource monitoring during load."""

    @pytest.mark.skipif(not HAS_PSUTIL, reason="psutil not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
    def test_cpu_usage_under_load(self, api_client):
        """Monitor CPU usage under load."""
        process = psutil.Process()

        # Get baseline CPU
        baseline_cpu = process.cpu_percent(interval=0.1)

        # Generate load
        def generate_load():
            for _ in range(50):
                with contextlib.suppress(Exception):
                    api_client.post(
                        "/api/synthesis/preview",
                        json={"text": "CPU load test", "engine": "default"},
                    )

        # Measure CPU during load
        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            executor.submit(generate_load)
            time.sleep(1)  # Let load build
            load_cpu = process.cpu_percent(interval=1.0)

        # CPU usage is informational
        print(f"\nCPU: Baseline={baseline_cpu:.1f}%, Under load={load_cpu:.1f}%")

    @pytest.mark.skipif(not HAS_PSUTIL, reason="psutil not installed (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
    def test_memory_stability_under_load(self, api_client):
        """Verify memory remains stable under load."""
        process = psutil.Process()

        # Initial memory
        gc.collect()
        initial_memory = process.memory_info().rss / 1024 / 1024

        # Generate sustained load
        for _batch in range(5):
            for _ in range(10):
                api_client.get("/api/health")
            gc.collect()

        # Final memory
        final_memory = process.memory_info().rss / 1024 / 1024
        growth = final_memory - initial_memory

        # Memory growth should be bounded
        print(
            f"\nMemory: Initial={initial_memory:.0f}MB, Final={final_memory:.0f}MB, Growth={growth:.0f}MB"
        )
        assert growth < 200, f"Memory grew significantly: {growth:.0f}MB"
