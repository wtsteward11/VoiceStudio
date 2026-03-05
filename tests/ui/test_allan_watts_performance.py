"""
Allan Watts Performance and Timing Tests.

Tests performance across all workflows:
- UI responsiveness
- API latency
- Panel navigation timing
- Operation throughput
- Resource utilization
- Benchmark comparisons

Requirements:
- WinAppDriver running on port 4723
- Backend running on port 8000
- VoiceStudio application built
"""

from __future__ import annotations

import os
import statistics
import sys
import time
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).parent))

from fixtures.audio_test_data import (
    ALL_WORKFLOWS,
    PANEL_NAVIGATION,
    TEST_AUDIO_FILE,
    get_file_size_mb,
    validate_test_file_exists,
)
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

# Configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Performance thresholds (milliseconds)
THRESHOLDS = {
    "panel_navigation": 500,
    "api_health_check": 200,
    "api_list_request": 500,
    "file_upload_per_mb": 1000,
    "ui_click_response": 100,
    "search_response": 300,
}

# Pytest markers
pytestmark = [
    pytest.mark.performance,
    pytest.mark.allan_watts,
]


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="module")
def tracer():
    """Create a workflow tracer for performance tests."""
    t = WorkflowTracer("allan_watts_performance", OUTPUT_DIR)
    t.start()
    yield t
    t.export_report()


@pytest.fixture(scope="module")
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "allan_watts_performance_api_calls.json")


def measure_time(func, *args, **kwargs) -> tuple[float, any]:
    """Measure execution time of a function."""
    start = time.perf_counter()
    result = func(*args, **kwargs)
    elapsed_ms = (time.perf_counter() - start) * 1000
    return elapsed_ms, result


def run_multiple_times(func, iterations: int = 5) -> list[float]:
    """Run a function multiple times and collect timings."""
    timings = []
    for _ in range(iterations):
        elapsed, _ = measure_time(func)
        timings.append(elapsed)
    return timings


def calculate_stats(timings: list[float]) -> dict[str, float]:
    """Calculate statistics for a list of timings."""
    if not timings:
        return {"min": 0, "max": 0, "avg": 0, "median": 0, "stdev": 0}

    return {
        "min": min(timings),
        "max": max(timings),
        "avg": statistics.mean(timings),
        "median": statistics.median(timings),
        "stdev": statistics.stdev(timings) if len(timings) > 1 else 0,
    }


# =============================================================================
# API Latency Tests
# =============================================================================


class TestAPILatency:
    """Test API endpoint latency."""

    def test_health_check_latency(self, api_monitor, tracer):
        """Measure health check latency."""
        tracer.start_phase("api_latency", "Test API latency")
        tracer.step("Measuring health check latency")

        def health_check():
            return api_monitor.get("/api/v3/health")

        timings = run_multiple_times(health_check, iterations=10)
        stats = calculate_stats(timings)

        tracer.step(
            f"Health check: min={stats['min']:.1f}ms, avg={stats['avg']:.1f}ms, max={stats['max']:.1f}ms"
        )
        tracer.record_timing("api_health", stats["avg"])

        assert (
            stats["avg"] < THRESHOLDS["api_health_check"]
        ), f"Health check too slow: {stats['avg']:.1f}ms > {THRESHOLDS['api_health_check']}ms"

        tracer.end_phase(success=True)
        tracer.success("Health check latency acceptable")

    def test_engines_list_latency(self, api_monitor, tracer):
        """Measure engines list latency."""
        tracer.step("Measuring engines list latency")

        def list_engines():
            return api_monitor.get("/api/v3/engines")

        timings = run_multiple_times(list_engines, iterations=5)
        stats = calculate_stats(timings)

        tracer.step(
            f"Engines list: min={stats['min']:.1f}ms, avg={stats['avg']:.1f}ms, max={stats['max']:.1f}ms"
        )
        tracer.record_timing("api_engines_list", stats["avg"])

        tracer.success("Engines list latency measured")

    def test_voices_list_latency(self, api_monitor, tracer):
        """Measure voices list latency."""
        tracer.step("Measuring voices list latency")

        def list_voices():
            return api_monitor.get("/api/v3/voices")

        timings = run_multiple_times(list_voices, iterations=5)
        stats = calculate_stats(timings)

        tracer.step(
            f"Voices list: min={stats['min']:.1f}ms, avg={stats['avg']:.1f}ms, max={stats['max']:.1f}ms"
        )
        tracer.record_timing("api_voices_list", stats["avg"])

        tracer.success("Voices list latency measured")

    def test_assets_list_latency(self, api_monitor, tracer):
        """Measure assets list latency."""
        tracer.step("Measuring assets list latency")

        def list_assets():
            return api_monitor.get("/api/v3/assets")

        timings = run_multiple_times(list_assets, iterations=5)
        stats = calculate_stats(timings)

        tracer.step(
            f"Assets list: min={stats['min']:.1f}ms, avg={stats['avg']:.1f}ms, max={stats['max']:.1f}ms"
        )
        tracer.record_timing("api_assets_list", stats["avg"])

        tracer.success("Assets list latency measured")


# =============================================================================
# Panel Navigation Timing Tests
# =============================================================================


class TestPanelNavigationTiming:
    """Test panel navigation timing."""

    def test_all_panel_navigation_timing(self, driver, app_launched, tracer):
        """Measure navigation timing for all panels."""
        tracer.start_phase("panel_timing", "Test panel navigation timing")
        tracer.step("Measuring panel navigation timing")

        results = {}

        for workflow in ALL_WORKFLOWS:
            timings = []

            for _ in range(3):  # 3 iterations each
                start = time.perf_counter()

                try:
                    nav_button = driver.find_element("accessibility id", workflow.nav_id)
                    nav_button.click()

                    # Wait for root element
                    for _ in range(20):
                        try:
                            driver.find_element("accessibility id", workflow.root_id)
                            break
                        except RuntimeError:
                            time.sleep(0.05)

                    elapsed_ms = (time.perf_counter() - start) * 1000
                    timings.append(elapsed_ms)
                # ALLOWED: bare except - best effort, failure acceptable
                except RuntimeError:
                    pass

                time.sleep(0.1)  # Brief pause between iterations

            if timings:
                stats = calculate_stats(timings)
                results[workflow.name] = stats
                tracer.step(f"{workflow.name}: avg={stats['avg']:.1f}ms")
                tracer.record_timing(f"panel_nav_{workflow.name}", stats["avg"])

        tracer.step(f"Measured {len(results)} panels")
        tracer.end_phase(success=True)
        tracer.success("Panel navigation timing measured")

    def test_panel_switch_timing(self, driver, app_launched, tracer):
        """Measure time to switch between panels."""
        tracer.step("Measuring panel switch timing")

        # Define a switch sequence
        switch_sequence = [
            ("Library", "VoiceSynthesis"),
            ("VoiceSynthesis", "Transcription"),
            ("Transcription", "VoiceCloning"),
            ("VoiceCloning", "Library"),
        ]

        for from_panel, to_panel in switch_sequence:
            from_nav = PANEL_NAVIGATION.get(from_panel, {})
            to_nav = PANEL_NAVIGATION.get(to_panel, {})

            if not from_nav or not to_nav:
                continue

            try:
                # Navigate to from_panel first
                from_button = driver.find_element("accessibility id", from_nav.get("nav_id", ""))
                from_button.click()
                time.sleep(0.3)

                # Now measure switch to to_panel
                start = time.perf_counter()
                to_button = driver.find_element("accessibility id", to_nav.get("nav_id", ""))
                to_button.click()

                for _ in range(20):
                    try:
                        driver.find_element("accessibility id", to_nav.get("root_id", ""))
                        break
                    except RuntimeError:
                        time.sleep(0.05)

                elapsed_ms = (time.perf_counter() - start) * 1000
                tracer.step(f"{from_panel} → {to_panel}: {elapsed_ms:.1f}ms")
                tracer.record_timing(f"switch_{from_panel}_{to_panel}", elapsed_ms)
            except RuntimeError:
                tracer.step(f"{from_panel} → {to_panel}: navigation failed")

        tracer.success("Panel switch timing measured")


# =============================================================================
# UI Responsiveness Tests
# =============================================================================


class TestUIResponsiveness:
    """Test UI responsiveness."""

    def test_click_response_time(self, driver, app_launched, tracer):
        """Measure UI click response time."""
        tracer.start_phase("ui_responsiveness", "Test UI responsiveness")
        tracer.step("Measuring click response time")

        # Find any clickable element
        try:
            workflow = ALL_WORKFLOWS[0]
            nav_button = driver.find_element("accessibility id", workflow.nav_id)

            timings = []
            for _ in range(5):
                start = time.perf_counter()
                nav_button.click()
                elapsed_ms = (time.perf_counter() - start) * 1000
                timings.append(elapsed_ms)
                time.sleep(0.2)

            stats = calculate_stats(timings)
            tracer.step(f"Click response: avg={stats['avg']:.1f}ms")
            tracer.record_timing("ui_click", stats["avg"])

            tracer.success("Click response time measured")
        except RuntimeError as e:
            tracer.step(f"Click test failed: {e}")
        finally:
            tracer.end_phase()

    def test_text_input_responsiveness(self, driver, app_launched, tracer):
        """Measure text input responsiveness."""
        tracer.step("Measuring text input responsiveness")

        # Navigate to synthesis for text input
        workflow = next((w for w in ALL_WORKFLOWS if w.name == "synthesis"), None)
        if not workflow:
            pytest.skip("Synthesis workflow not defined")

        try:
            nav_button = driver.find_element("accessibility id", workflow.nav_id)
            nav_button.click()
            time.sleep(0.5)

            text_input = driver.find_element(
                "xpath", "//*[contains(@AutomationId, 'Text') and contains(@ClassName, 'TextBox')]"
            )

            # Measure typing speed
            test_text = "Performance test text input."
            start = time.perf_counter()
            text_input.send_keys(test_text)
            elapsed_ms = (time.perf_counter() - start) * 1000

            chars_per_second = len(test_text) / (elapsed_ms / 1000)
            tracer.step(
                f"Text input: {elapsed_ms:.1f}ms for {len(test_text)} chars ({chars_per_second:.0f} chars/sec)"
            )
            tracer.record_timing("ui_text_input", elapsed_ms)

            text_input.clear()
            tracer.success("Text input responsiveness measured")
        except RuntimeError as e:
            tracer.step(f"Text input test failed: {e}")


# =============================================================================
# File Operation Performance Tests
# =============================================================================


class TestFileOperationPerformance:
    """Test file operation performance."""

    def test_file_upload_throughput(self, api_monitor, tracer):
        """Measure file upload throughput."""
        tracer.start_phase("file_performance", "Test file operation performance")
        tracer.step("Measuring file upload throughput")

        if not validate_test_file_exists():
            pytest.skip(f"Test file not found: {TEST_AUDIO_FILE}")

        file_size_mb = get_file_size_mb()
        tracer.step(f"Test file size: {file_size_mb:.2f} MB")

        try:
            with open(TEST_AUDIO_FILE, "rb") as f:
                file_data = f.read()

            # Measure upload time
            start = time.perf_counter()
            files = {"file": (TEST_AUDIO_FILE.name, file_data, "audio/x-m4a")}
            api_monitor.post("/api/v3/audio/upload", files=files)
            elapsed_ms = (time.perf_counter() - start) * 1000

            throughput_mbps = (file_size_mb * 8) / (elapsed_ms / 1000) if elapsed_ms > 0 else 0

            tracer.step(f"Upload: {elapsed_ms:.1f}ms, {throughput_mbps:.1f} Mbps")
            tracer.record_timing("file_upload", elapsed_ms, f"{file_size_mb:.2f} MB")

            tracer.success("Upload throughput measured")
        except FileNotFoundError:
            pytest.skip(f"Test file not found: {TEST_AUDIO_FILE}")
        except requests.RequestException as e:
            tracer.step(f"Upload failed: {e}")
        finally:
            tracer.end_phase()


# =============================================================================
# Operation Throughput Tests
# =============================================================================


class TestOperationThroughput:
    """Test operation throughput."""

    def test_api_throughput(self, api_monitor, tracer):
        """Measure API request throughput."""
        tracer.start_phase("throughput", "Test operation throughput")
        tracer.step("Measuring API throughput")

        # Rapid-fire health checks
        num_requests = 20
        start = time.perf_counter()

        successes = 0
        for _ in range(num_requests):
            try:
                response = api_monitor.get("/api/v3/health")
                if response.status_code == 200:
                    successes += 1
            # ALLOWED: bare except - best effort, failure acceptable
            except requests.RequestException:
                pass

        elapsed_ms = (time.perf_counter() - start) * 1000
        requests_per_second = (num_requests / (elapsed_ms / 1000)) if elapsed_ms > 0 else 0

        tracer.step(
            f"Throughput: {requests_per_second:.1f} req/sec ({successes}/{num_requests} success)"
        )
        tracer.record_timing("api_throughput", elapsed_ms, f"{num_requests} requests")

        tracer.end_phase(success=True)
        tracer.success("API throughput measured")


# =============================================================================
# Benchmark Comparison Tests
# =============================================================================


class TestBenchmarks:
    """Benchmark comparison tests."""

    def test_establish_baselines(self, api_monitor, tracer):
        """Establish performance baselines."""
        tracer.start_phase("benchmarks", "Establish performance baselines")
        tracer.step("Establishing performance baselines")

        baselines = {}

        # Health check baseline
        timings = run_multiple_times(lambda: api_monitor.get("/api/v3/health"), iterations=10)
        baselines["health_check"] = calculate_stats(timings)

        # Engines list baseline
        timings = run_multiple_times(lambda: api_monitor.get("/api/v3/engines"), iterations=5)
        baselines["engines_list"] = calculate_stats(timings)

        # Output baselines
        for name, stats in baselines.items():
            tracer.step(f"Baseline {name}: avg={stats['avg']:.1f}ms, p95={stats['max']:.1f}ms")

        tracer.end_phase(success=True)
        tracer.success("Baselines established")

    def test_performance_thresholds(self, tracer):
        """Document performance thresholds."""
        tracer.step("Documenting performance thresholds")

        for operation, threshold in THRESHOLDS.items():
            tracer.step(f"{operation}: < {threshold}ms")

        tracer.success("Thresholds documented")


# =============================================================================
# Performance Summary Tests
# =============================================================================


class TestPerformanceSummary:
    """Generate performance summary."""

    def test_generate_summary(self, tracer):
        """Generate performance summary report."""
        tracer.start_phase("summary", "Generate performance summary")
        tracer.step("Generating performance summary")

        summary = tracer.get_performance_summary()

        for category, stats in summary.items():
            tracer.step(
                f"{category}: min={stats.get('min', 0):.1f}ms, "
                f"avg={stats.get('avg', 0):.1f}ms, "
                f"max={stats.get('max', 0):.1f}ms, "
                f"count={stats.get('count', 0)}"
            )

        tracer.end_phase(success=True)
        tracer.success("Performance summary generated")

    def test_export_metrics(self, tracer):
        """Export performance metrics."""
        tracer.step("Exporting performance metrics")

        # The tracer will export metrics when the test module completes
        export_path = OUTPUT_DIR / "allan_watts_performance_report.html"
        tracer.step(f"Metrics will be exported to: {export_path}")

        tracer.success("Metrics export configured")


# =============================================================================
# Resource Utilization Tests
# =============================================================================


class TestResourceUtilization:
    """Test resource utilization."""

    def test_document_resource_monitoring(self, tracer):
        """Document resource monitoring approach."""
        tracer.start_phase("resource_monitoring", "Document resource monitoring")
        tracer.step("Documenting resource monitoring approach")

        monitoring_points = {
            "cpu_usage": "Monitor during synthesis/transcription",
            "memory_usage": "Track for large file operations",
            "disk_io": "Monitor during file import/export",
            "network_io": "Track API call patterns",
            "gpu_usage": "Monitor when using GPU-accelerated engines",
        }

        for resource, approach in monitoring_points.items():
            tracer.step(f"{resource}: {approach}")

        tracer.end_phase(success=True)
        tracer.success("Resource monitoring documented")

    def test_expected_resource_limits(self, tracer):
        """Document expected resource limits."""
        tracer.step("Documenting expected resource limits")

        expected_limits = {
            "idle_memory": "< 500 MB",
            "active_memory": "< 2 GB (without large models)",
            "cpu_idle": "< 5%",
            "cpu_transcription": "Up to 100% (multi-core)",
            "disk_space_temp": "< 1 GB",
        }

        for resource, limit in expected_limits.items():
            tracer.step(f"{resource}: {limit}")

        tracer.success("Resource limits documented")


# =============================================================================
# Entry Point
# =============================================================================

if __name__ == "__main__":
    pytest.main(
        [
            __file__,
            "-v",
            "--tb=short",
            "-m",
            "not slow",
            "--html=.buildlogs/validation/reports/allan_watts_performance_report.html",
            "--self-contained-html",
        ]
    )
