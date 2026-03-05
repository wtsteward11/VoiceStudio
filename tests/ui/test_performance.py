"""
VoiceStudio Performance Tests.

Tests performance of UI and API operations:
- Panel load times
- Navigation responsiveness
- API response times
- Memory usage tracking
- Resource utilization
"""

from __future__ import annotations

import contextlib
import json
import os
import statistics
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

import pytest

# Add tests/ui to path for local imports (consistent with other UI tests)
sys.path.insert(0, str(Path(__file__).parent))

# Define PROJECT_ROOT at module level (required before conftest loads)
PROJECT_ROOT = Path(__file__).parent.parent.parent

try:
    from selenium import webdriver
    from selenium.webdriver.common.by import By
    from selenium.webdriver.common.keys import Keys
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.support.ui import WebDriverWait
except ImportError:
    webdriver = None
    By = None

try:
    import requests
except ImportError:
    requests = None

try:
    import psutil

    PSUTIL_AVAILABLE = True
except ImportError:
    PSUTIL_AVAILABLE = False

try:
    from fixtures.automation_ids import CATEGORIES, PANELS_BY_CATEGORY, get_all_panels
except ImportError:
    PANELS_BY_CATEGORY = {}

    def get_all_panels():
        return []

    CATEGORIES = []

# Configuration
APP_PATH = os.getenv(
    "VOICESTUDIO_APP_PATH",
    str(
        PROJECT_ROOT
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "x64"
        / "Debug"
        / "net8.0-windows10.0.22621.0"
        / "win-x64"
        / "VoiceStudio.App.exe"
    ),
)
WINAPPDRIVER_URL = "http://127.0.0.1:4723"
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/performance"))

# Performance thresholds (in milliseconds)
THRESHOLDS = {
    "panel_load_max_ms": 2000,  # Max panel load time
    "api_response_max_ms": 500,  # Max API response time
    "navigation_max_ms": 500,  # Max navigation time
    "memory_increase_max_mb": 100,  # Max memory increase per operation
}

OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

pytestmark = [
    pytest.mark.performance,
    pytest.mark.ui,
]


@dataclass
class PerformanceMeasurement:
    """A single performance measurement."""

    name: str
    duration_ms: float
    timestamp: datetime = field(default_factory=datetime.now)
    category: str = "general"
    success: bool = True
    metadata: dict = field(default_factory=dict)


@dataclass
class PerformanceReport:
    """Performance test report."""

    measurements: list[PerformanceMeasurement] = field(default_factory=list)
    start_time: datetime = field(default_factory=datetime.now)
    end_time: datetime | None = None
    initial_memory_mb: float = 0
    final_memory_mb: float = 0

    def add(self, name: str, duration_ms: float, category: str = "general", **kwargs):
        self.measurements.append(
            PerformanceMeasurement(name=name, duration_ms=duration_ms, category=category, **kwargs)
        )

    def get_stats(self, category: str | None = None) -> dict:
        """Get statistics for measurements."""
        measurements = self.measurements
        if category:
            measurements = [m for m in measurements if m.category == category]

        if not measurements:
            return {"count": 0}

        durations = [m.duration_ms for m in measurements]
        return {
            "count": len(durations),
            "min_ms": min(durations),
            "max_ms": max(durations),
            "mean_ms": statistics.mean(durations),
            "median_ms": statistics.median(durations),
            "stdev_ms": statistics.stdev(durations) if len(durations) > 1 else 0,
        }


# Key panels for performance testing
KEY_PANELS = [
    {"name": "VoiceSynthesis", "nav_name": "Synthesize"},
    {"name": "Transcribe", "nav_name": "Transcribe"},
    {"name": "VoiceCloningWizard", "nav_name": "Voice Cloning"},
    {"name": "Library", "nav_name": "Library"},
    {"name": "Profiles", "nav_name": "Profiles"},
    {"name": "Settings", "nav_name": "Settings"},
    {"name": "BatchProcessing", "nav_name": "Batch"},
    {"name": "Training", "nav_name": "Training"},
    {"name": "Diagnostics", "nav_name": "Diagnostics"},
    {"name": "Effects", "nav_name": "Effects"},
]


@pytest.fixture(scope="module")
def winappdriver_process():
    """Start WinAppDriver if not running."""
    import subprocess

    WINAPPDRIVER_PATH = r"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"

    if not Path(WINAPPDRIVER_PATH).exists():
        pytest.skip("WinAppDriver not installed")

    try:
        resp = requests.get(f"{WINAPPDRIVER_URL}/status", timeout=2)
        if resp.status_code == 200:
            return None
    # ALLOWED: bare except - best effort, failure acceptable
    except Exception:
        pass

    process = subprocess.Popen([WINAPPDRIVER_PATH], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    time.sleep(2)

    yield process

    if process:
        process.terminate()


@pytest.fixture(scope="module")
def driver(winappdriver_process):
    """Create WinAppDriver session."""
    if webdriver is None:
        pytest.skip("selenium not installed")

    if not Path(APP_PATH).exists():
        pytest.skip(f"App not found at {APP_PATH}")

    # Import the custom WinAppDriverSession from UI conftest
    # This bypasses Selenium 4.x W3C capabilities issue
    try:
        from conftest import WinAppDriverSession

        session = WinAppDriverSession(APP_PATH, WINAPPDRIVER_URL)
        session.implicitly_wait(10)
        time.sleep(3)
        yield session
        with contextlib.suppress(Exception):
            session.quit()
    except ImportError:
        pytest.skip("WinAppDriverSession not available")


@pytest.fixture(scope="module")
def performance_report():
    """Create performance report."""
    report = PerformanceReport()

    if PSUTIL_AVAILABLE:
        process = psutil.Process()
        report.initial_memory_mb = process.memory_info().rss / (1024 * 1024)

    yield report

    report.end_time = datetime.now()

    if PSUTIL_AVAILABLE:
        process = psutil.Process()
        report.final_memory_mb = process.memory_info().rss / (1024 * 1024)


@pytest.fixture
def api_client():
    """Create API client."""
    if requests is None:
        pytest.skip("requests not installed")

    class APIClient:
        def __init__(self, base_url: str):
            self.base_url = base_url
            self.session = requests.Session()

        def timed_get(self, path: str, **kwargs) -> tuple[requests.Response, float]:
            start = time.perf_counter()
            response = self.session.get(f"{self.base_url}{path}", **kwargs)
            duration_ms = (time.perf_counter() - start) * 1000
            return response, duration_ms

        def timed_post(self, path: str, **kwargs) -> tuple[requests.Response, float]:
            start = time.perf_counter()
            response = self.session.post(f"{self.base_url}{path}", **kwargs)
            duration_ms = (time.perf_counter() - start) * 1000
            return response, duration_ms

    return APIClient(BACKEND_URL)


def measure_time(func):
    """Decorator to measure function execution time."""

    def wrapper(*args, **kwargs):
        start = time.perf_counter()
        result = func(*args, **kwargs)
        duration_ms = (time.perf_counter() - start) * 1000
        return result, duration_ms

    return wrapper


def navigate_to_panel_timed(driver, panel_config: dict) -> tuple[bool, float]:
    """Navigate to panel and measure time."""
    nav_name = panel_config.get("nav_name", panel_config["name"])

    start = time.perf_counter()

    try:
        wait = WebDriverWait(driver, 5)
        element = wait.until(EC.element_to_be_clickable((By.NAME, nav_name)))
        element.click()

        # Wait for panel to be ready
        time.sleep(0.3)

        duration_ms = (time.perf_counter() - start) * 1000
        return True, duration_ms
    except Exception:
        duration_ms = (time.perf_counter() - start) * 1000
        return False, duration_ms


class TestPanelLoadTimes:
    """Tests for panel load performance."""

    @pytest.mark.parametrize("panel_config", KEY_PANELS)
    def test_panel_load_time(self, driver, panel_config, performance_report):
        """Test individual panel load time."""
        panel_name = panel_config["name"]

        success, duration_ms = navigate_to_panel_timed(driver, panel_config)

        performance_report.add(
            name=panel_name,
            duration_ms=duration_ms,
            category="panel_load",
            success=success,
        )

        print(f"{panel_name}: {duration_ms:.0f}ms")

        if success:
            assert (
                duration_ms < THRESHOLDS["panel_load_max_ms"]
            ), f"{panel_name} load time ({duration_ms:.0f}ms) exceeds threshold ({THRESHOLDS['panel_load_max_ms']}ms)"

    def test_all_panels_load_time(self, driver, performance_report):
        """Test loading all panels in sequence."""
        results = []

        for panel_config in KEY_PANELS:
            panel_name = panel_config["name"]
            success, duration_ms = navigate_to_panel_timed(driver, panel_config)

            results.append(
                {
                    "panel": panel_name,
                    "duration_ms": duration_ms,
                    "success": success,
                }
            )

            performance_report.add(
                name=f"sequential_{panel_name}",
                duration_ms=duration_ms,
                category="sequential_navigation",
                success=success,
            )

        # Statistics
        successful = [r for r in results if r["success"]]
        if successful:
            durations = [r["duration_ms"] for r in successful]
            print("\nPanel load statistics:")
            print(f"  Panels tested: {len(results)}")
            print(f"  Successful: {len(successful)}")
            print(f"  Min: {min(durations):.0f}ms")
            print(f"  Max: {max(durations):.0f}ms")
            print(f"  Mean: {statistics.mean(durations):.0f}ms")
            print(f"  Median: {statistics.median(durations):.0f}ms")

    def test_panel_navigation_cycle(self, driver, performance_report):
        """Test navigating through panels multiple times."""
        iterations = 3
        all_times = []

        for iteration in range(iterations):
            for panel_config in KEY_PANELS[:5]:  # First 5 panels
                success, duration_ms = navigate_to_panel_timed(driver, panel_config)
                if success:
                    all_times.append(duration_ms)
                    performance_report.add(
                        name=f"cycle_{iteration}_{panel_config['name']}",
                        duration_ms=duration_ms,
                        category="navigation_cycle",
                    )

        if all_times:
            print(f"\nNavigation cycle ({iterations} iterations):")
            print(f"  Total measurements: {len(all_times)}")
            print(f"  Mean: {statistics.mean(all_times):.0f}ms")

            # Check for degradation
            first_half = all_times[: len(all_times) // 2]
            second_half = all_times[len(all_times) // 2 :]

            if first_half and second_half:
                degradation = statistics.mean(second_half) - statistics.mean(first_half)
                print(f"  Degradation: {degradation:.0f}ms")


class TestAPIResponseTimes:
    """Tests for API response performance."""

    def test_health_endpoint(self, api_client, performance_report):
        """Test health endpoint response time."""
        response, duration_ms = api_client.timed_get("/api/health", timeout=10)

        performance_report.add(
            name="health_endpoint",
            duration_ms=duration_ms,
            category="api",
            metadata={"status_code": response.status_code},
        )

        print(f"Health endpoint: {duration_ms:.0f}ms")

        assert response.status_code == 200
        assert duration_ms < THRESHOLDS["api_response_max_ms"]

    def test_engines_endpoint(self, api_client, performance_report):
        """Test engines listing endpoint."""
        response, duration_ms = api_client.timed_get("/api/voice/engines", timeout=10)

        performance_report.add(
            name="engines_endpoint",
            duration_ms=duration_ms,
            category="api",
        )

        print(f"Engines endpoint: {duration_ms:.0f}ms")

        if response.status_code == 200:
            assert duration_ms < THRESHOLDS["api_response_max_ms"]

    def test_profiles_endpoint(self, api_client, performance_report):
        """Test profiles listing endpoint."""
        response, duration_ms = api_client.timed_get("/api/profiles", timeout=10)

        performance_report.add(
            name="profiles_endpoint",
            duration_ms=duration_ms,
            category="api",
        )

        print(f"Profiles endpoint: {duration_ms:.0f}ms")

        if response.status_code == 200:
            assert duration_ms < THRESHOLDS["api_response_max_ms"]

    def test_critical_api_endpoints(self, api_client, performance_report):
        """Test critical API endpoint response times."""
        endpoints = [
            ("/api/health", "Health"),
            ("/api/voice/engines", "Engines"),
            ("/api/profiles", "Profiles"),
            ("/api/settings", "Settings"),
            ("/api/jobs", "Jobs"),
            ("/api/audio/formats", "Audio Formats"),
        ]

        results = []

        for path, name in endpoints:
            try:
                response, duration_ms = api_client.timed_get(path, timeout=10)

                results.append(
                    {
                        "endpoint": name,
                        "path": path,
                        "duration_ms": duration_ms,
                        "status": response.status_code,
                    }
                )

                performance_report.add(
                    name=f"api_{name.lower().replace(' ', '_')}",
                    duration_ms=duration_ms,
                    category="api",
                )
            except Exception as e:
                results.append(
                    {
                        "endpoint": name,
                        "path": path,
                        "error": str(e),
                    }
                )

        # Report
        print("\nAPI Endpoint Performance:")
        for r in results:
            if "error" not in r:
                status = "✓" if r["duration_ms"] < THRESHOLDS["api_response_max_ms"] else "⚠"
                print(f"  {status} {r['endpoint']}: {r['duration_ms']:.0f}ms")
            else:
                print(f"  ✗ {r['endpoint']}: {r['error']}")


class TestMemoryUsage:
    """Tests for memory usage."""

    @pytest.mark.skipif(not PSUTIL_AVAILABLE, reason="psutil not installed")
    def test_memory_baseline(self, performance_report):
        """Record baseline memory usage."""
        process = psutil.Process()
        memory_mb = process.memory_info().rss / (1024 * 1024)

        performance_report.add(
            name="memory_baseline",
            duration_ms=0,
            category="memory",
            metadata={"memory_mb": memory_mb},
        )

        print(f"Baseline memory: {memory_mb:.1f} MB")

    @pytest.mark.skipif(not PSUTIL_AVAILABLE, reason="psutil not installed")
    def test_memory_after_navigation(self, driver, performance_report):
        """Test memory usage after panel navigation."""
        process = psutil.Process()
        initial_memory = process.memory_info().rss / (1024 * 1024)

        # Navigate through all panels
        for panel_config in KEY_PANELS:
            _success, _ = navigate_to_panel_timed(driver, panel_config)

        final_memory = process.memory_info().rss / (1024 * 1024)
        memory_increase = final_memory - initial_memory

        performance_report.add(
            name="memory_after_navigation",
            duration_ms=0,
            category="memory",
            metadata={
                "initial_mb": initial_memory,
                "final_mb": final_memory,
                "increase_mb": memory_increase,
            },
        )

        print(f"Memory after navigation: {final_memory:.1f} MB (+{memory_increase:.1f} MB)")

        assert (
            memory_increase < THRESHOLDS["memory_increase_max_mb"]
        ), f"Memory increase ({memory_increase:.1f} MB) exceeds threshold"


class TestRenderPerformance:
    """Tests for UI render performance."""

    def test_tab_navigation_responsiveness(self, driver, performance_report):
        """Test Tab key navigation responsiveness."""
        times = []

        for _i in range(10):
            start = time.perf_counter()
            driver.switch_to.active_element.send_keys(Keys.TAB)
            duration_ms = (time.perf_counter() - start) * 1000
            times.append(duration_ms)
            time.sleep(0.05)

        mean_time = statistics.mean(times)

        performance_report.add(
            name="tab_navigation",
            duration_ms=mean_time,
            category="render",
            metadata={"samples": len(times), "max_ms": max(times)},
        )

        print(f"Tab navigation: mean {mean_time:.0f}ms, max {max(times):.0f}ms")

        assert mean_time < 100, "Tab navigation is too slow"


class TestPerformanceReport:
    """Generate performance report."""

    def test_generate_performance_report(self, performance_report):
        """Generate comprehensive performance report."""
        report = performance_report

        print("\n" + "=" * 60)
        print("PERFORMANCE REPORT")
        print("=" * 60)

        # Overall statistics
        print(f"\nTotal measurements: {len(report.measurements)}")

        if report.initial_memory_mb > 0:
            print(
                f"Memory - Initial: {report.initial_memory_mb:.1f} MB, Final: {report.final_memory_mb:.1f} MB"
            )

        # Category breakdown
        categories = {m.category for m in report.measurements}

        for category in sorted(categories):
            stats = report.get_stats(category)
            print(f"\n{category.upper()}:")
            print(f"  Count: {stats['count']}")
            if stats["count"] > 0:
                print(f"  Min: {stats['min_ms']:.0f}ms")
                print(f"  Max: {stats['max_ms']:.0f}ms")
                print(f"  Mean: {stats['mean_ms']:.0f}ms")
                print(f"  Median: {stats['median_ms']:.0f}ms")

        # Save JSON report
        report_path = OUTPUT_DIR / "performance_report.json"

        report_data = {
            "timestamp": datetime.now().isoformat(),
            "duration_seconds": (
                (report.end_time - report.start_time).total_seconds() if report.end_time else 0
            ),
            "memory": {
                "initial_mb": report.initial_memory_mb,
                "final_mb": report.final_memory_mb,
            },
            "thresholds": THRESHOLDS,
            "categories": {cat: report.get_stats(cat) for cat in categories},
            "measurements": [
                {
                    "name": m.name,
                    "duration_ms": m.duration_ms,
                    "category": m.category,
                    "success": m.success,
                    "metadata": m.metadata,
                }
                for m in report.measurements
            ],
        }

        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report_data, f, indent=2)

        print(f"\nReport saved to: {report_path}")


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
