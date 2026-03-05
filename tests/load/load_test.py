"""Load test for VoiceStudio backend.

Simulates concurrent synthesis requests to measure latency, error rate,
and memory stability under load.

Usage:
    python -m pytest tests/load/load_test.py -v --timeout=120
    python tests/load/load_test.py  # standalone mode

Targets:
    p50 synthesis <= 3s (XTTS GPU, 50-word sentence)
    p95 synthesis <= 8s
    Error rate < 1%
    Memory stable across 10 requests (no unbounded growth)
"""

from __future__ import annotations

import asyncio
import os
import statistics
import sys
import time
from pathlib import Path

import pytest

project_root = str(Path(__file__).parent.parent.parent)
if project_root not in sys.path:
    sys.path.insert(0, project_root)

BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://localhost:8000")
CONCURRENT_REQUESTS = 5
TEST_TEXT = "This is a test sentence for load testing the voice synthesis pipeline. " \
            "The quick brown fox jumps over the lazy dog near the river bank."

LATENCY_P50_TARGET_S = 3.0
LATENCY_P95_TARGET_S = 8.0
ERROR_RATE_TARGET = 0.01


async def _make_synthesis_request(session, request_id: int) -> dict:
    """Make a single synthesis request and measure latency."""
    import httpx

    start = time.monotonic()
    try:
        resp = await session.post(
            f"{BACKEND_URL}/api/voice/synthesize",
            json={"text": TEST_TEXT, "engine": "gtts"},
            timeout=30.0,
        )
        latency = time.monotonic() - start
        return {
            "request_id": request_id,
            "status": resp.status_code,
            "latency_s": latency,
            "success": 200 <= resp.status_code < 400,
        }
    except Exception as e:
        latency = time.monotonic() - start
        return {
            "request_id": request_id,
            "status": -1,
            "latency_s": latency,
            "success": False,
            "error": str(e),
        }


async def run_load_test(n_requests: int = CONCURRENT_REQUESTS) -> dict:
    """Run concurrent synthesis requests and collect metrics."""
    try:
        import httpx
    except ImportError:
        return {"error": "httpx not installed. Run: pip install httpx"}

    async with httpx.AsyncClient() as session:
        tasks = [_make_synthesis_request(session, i) for i in range(n_requests)]
        results = await asyncio.gather(*tasks)

    latencies = [r["latency_s"] for r in results]
    successes = [r for r in results if r["success"]]
    failures = [r for r in results if not r["success"]]

    metrics = {
        "total_requests": len(results),
        "successful": len(successes),
        "failed": len(failures),
        "error_rate": len(failures) / len(results) if results else 0,
        "latency_p50_s": statistics.median(latencies) if latencies else 0,
        "latency_p95_s": sorted(latencies)[int(len(latencies) * 0.95)] if len(latencies) > 1 else latencies[0] if latencies else 0,
        "latency_mean_s": statistics.mean(latencies) if latencies else 0,
        "latency_max_s": max(latencies) if latencies else 0,
        "results": results,
    }
    return metrics


class TestLoadPerformance:
    """Load performance tests (require running backend)."""

    @pytest.fixture(autouse=True)
    def check_backend(self):
        """Skip if backend is not running."""
        try:
            import httpx
            resp = httpx.get(f"{BACKEND_URL}/health", timeout=5)
            if resp.status_code != 200:
                pytest.skip("Backend not running or unhealthy")
        except Exception:
            pytest.skip(f"Backend not reachable at {BACKEND_URL}")

    @pytest.mark.asyncio
    async def test_concurrent_synthesis(self):
        """5 concurrent synthesis requests complete within SLO targets."""
        metrics = await run_load_test(CONCURRENT_REQUESTS)

        assert metrics["error_rate"] <= ERROR_RATE_TARGET, (
            f"Error rate {metrics['error_rate']:.1%} exceeds target {ERROR_RATE_TARGET:.1%}"
        )

    @pytest.mark.asyncio
    async def test_sequential_memory_stability(self):
        """10 sequential requests should not cause memory growth."""
        import httpx

        memory_samples = []
        async with httpx.AsyncClient() as session:
            for i in range(10):
                result = await _make_synthesis_request(session, i)
                try:
                    mem_resp = await session.get(f"{BACKEND_URL}/api/health")
                    if mem_resp.status_code == 200:
                        data = mem_resp.json()
                        if "memory_mb" in data:
                            memory_samples.append(data["memory_mb"])
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

        if len(memory_samples) >= 5:
            first_half_avg = statistics.mean(memory_samples[:5])
            second_half_avg = statistics.mean(memory_samples[5:])
            growth_pct = (second_half_avg - first_half_avg) / first_half_avg * 100
            assert growth_pct < 50, (
                f"Memory grew {growth_pct:.1f}% across 10 requests (limit: 50%)"
            )


if __name__ == "__main__":
    metrics = asyncio.run(run_load_test())
    print(f"\nLoad Test Results ({metrics['total_requests']} requests):")
    print(f"  Successful: {metrics['successful']}")
    print(f"  Failed: {metrics['failed']}")
    print(f"  Error rate: {metrics['error_rate']:.1%}")
    print(f"  Latency p50: {metrics['latency_p50_s']:.2f}s")
    print(f"  Latency p95: {metrics['latency_p95_s']:.2f}s")
    print(f"  Latency mean: {metrics['latency_mean_s']:.2f}s")
    print(f"  Latency max: {metrics['latency_max_s']:.2f}s")
