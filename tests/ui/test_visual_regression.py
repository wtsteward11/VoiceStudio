"""
VoiceStudio Visual Regression Tests.

Tests visual consistency of UI panels:
- Capture baseline screenshots
- Compare current screenshots to baselines
- Detect visual changes and regressions
- Support for different themes/modes
"""

from __future__ import annotations

import contextlib
import hashlib
import json
import os
import sys
import time
from dataclasses import dataclass
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
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.support.ui import WebDriverWait
except ImportError:
    webdriver = None
    By = None

try:
    from PIL import Image, ImageChops

    PIL_AVAILABLE = True
except ImportError:
    PIL_AVAILABLE = False

try:
    from fixtures.automation_ids import CATEGORIES, PANELS_BY_CATEGORY, PanelInfo, get_all_panels
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
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/visual"))
BASELINE_DIR = OUTPUT_DIR / "baselines"
CURRENT_DIR = OUTPUT_DIR / "current"
DIFF_DIR = OUTPUT_DIR / "diffs"
DIFF_THRESHOLD = 0.01  # 1% pixel difference allowed

# Ensure directories exist
BASELINE_DIR.mkdir(parents=True, exist_ok=True)
CURRENT_DIR.mkdir(parents=True, exist_ok=True)
DIFF_DIR.mkdir(parents=True, exist_ok=True)

# Pytest markers
pytestmark = [
    pytest.mark.visual,
    pytest.mark.ui,
]


@dataclass
class VisualComparisonResult:
    """Result of visual comparison."""

    panel_name: str
    baseline_exists: bool
    screenshots_match: bool
    difference_percent: float
    baseline_path: Path | None = None
    current_path: Path | None = None
    diff_path: Path | None = None
    error: str | None = None


# Key panels for visual regression testing
KEY_PANELS = [
    # Core synthesis panels
    {"name": "VoiceSynthesis", "nav_name": "Synthesize", "category": "synthesis"},
    {"name": "MultiVoiceGenerator", "nav_name": "Multi-Voice", "category": "synthesis"},
    # Transcription panels
    {"name": "Transcribe", "nav_name": "Transcribe", "category": "transcription"},
    # Voice cloning panels
    {"name": "VoiceCloningWizard", "nav_name": "Voice Cloning", "category": "cloning"},
    {"name": "VoiceQuickClone", "nav_name": "Quick Clone", "category": "cloning"},
    # Effects panels
    {"name": "Effects", "nav_name": "Effects", "category": "effects"},
    {"name": "AudioEnhancer", "nav_name": "Enhancer", "category": "effects"},
    # Library panels
    {"name": "Library", "nav_name": "Library", "category": "library"},
    {"name": "Profiles", "nav_name": "Profiles", "category": "library"},
    # Training panels
    {"name": "Training", "nav_name": "Training", "category": "training"},
    # Settings panels
    {"name": "Settings", "nav_name": "Settings", "category": "settings"},
    {"name": "Diagnostics", "nav_name": "Diagnostics", "category": "settings"},
    # Timeline
    {"name": "Timeline", "nav_name": "Timeline", "category": "timeline"},
    # Batch processing
    {"name": "BatchProcessing", "nav_name": "Batch", "category": "batch"},
]


@pytest.fixture(scope="module")
def winappdriver_process():
    """Start WinAppDriver if not running."""
    import subprocess

    import requests

    WINAPPDRIVER_PATH = r"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"

    if not Path(WINAPPDRIVER_PATH).exists():
        pytest.skip("WinAppDriver not installed")

    # Check if already running
    try:
        resp = requests.get(f"{WINAPPDRIVER_URL}/status", timeout=2)
        if resp.status_code == 200:
            return None
    # ALLOWED: bare except - best effort, failure acceptable
    except Exception:
        pass

    # Start WinAppDriver
    process = subprocess.Popen(
        [WINAPPDRIVER_PATH],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(2)

    yield process

    if process:
        process.terminate()
        try:
            process.wait(timeout=5)
        except Exception:
            process.kill()


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


def capture_screenshot(driver, name: str, output_dir: Path) -> Path | None:
    """Capture screenshot and save to specified directory."""
    filepath = output_dir / f"{name}.png"

    try:
        driver.save_screenshot(str(filepath))
        return filepath
    except Exception as e:
        print(f"Screenshot capture failed: {e}")
        return None


def compute_image_hash(image_path: Path) -> str:
    """Compute hash of image for quick comparison."""
    with open(image_path, "rb") as f:
        return hashlib.md5(f.read()).hexdigest()


def compare_images(baseline_path: Path, current_path: Path, diff_path: Path) -> tuple[bool, float]:
    """Compare two images and generate diff image."""
    if not PIL_AVAILABLE:
        # Fall back to hash comparison
        baseline_hash = compute_image_hash(baseline_path)
        current_hash = compute_image_hash(current_path)
        match = baseline_hash == current_hash
        return match, 0.0 if match else 100.0

    try:
        baseline = Image.open(baseline_path)
        current = Image.open(current_path)

        # Resize if dimensions differ
        if baseline.size != current.size:
            current = current.resize(baseline.size)

        # Convert to same mode
        if baseline.mode != current.mode:
            baseline = baseline.convert("RGB")
            current = current.convert("RGB")

        # Calculate difference
        diff = ImageChops.difference(baseline, current)

        # Calculate difference percentage
        diff_data = list(diff.getdata())
        total_pixels = len(diff_data)
        if total_pixels == 0:
            return True, 0.0

        # Count different pixels (any channel > threshold)
        different_pixels = sum(1 for pixel in diff_data if any(c > 10 for c in pixel))
        difference_percent = (different_pixels / total_pixels) * 100

        # Save diff image if there are differences
        if difference_percent > 0:
            # Enhance diff visibility
            diff = ImageChops.multiply(diff, Image.new("RGB", diff.size, (10, 10, 10)))
            diff.save(diff_path)

        match = difference_percent <= DIFF_THRESHOLD
        return match, difference_percent

    except Exception as e:
        print(f"Image comparison failed: {e}")
        return False, 100.0


def navigate_to_panel(driver, panel_config: dict) -> bool:
    """Navigate to a panel by name."""
    nav_name = panel_config.get("nav_name", panel_config["name"])

    # Try various locator strategies
    locators = [
        (By.NAME, nav_name),
        (By.ACCESSIBILITY_ID, f"Nav{panel_config['name']}"),
        (By.XPATH, f"//*[contains(@Name, '{nav_name}')]"),
    ]

    for by, value in locators:
        try:
            wait = WebDriverWait(driver, 5)
            element = wait.until(EC.element_to_be_clickable((by, value)))
            element.click()
            time.sleep(0.5)  # Wait for panel to load
            return True
        except Exception:
            continue

    return False


class TestBaselineCapture:
    """Tests for capturing baseline screenshots."""

    @pytest.mark.parametrize("panel_config", KEY_PANELS)
    def test_capture_baseline(self, driver, panel_config):
        """Capture baseline screenshot for a panel."""
        panel_name = panel_config["name"]

        # Navigate to panel
        if not navigate_to_panel(driver, panel_config):
            pytest.skip(f"Could not navigate to {panel_name}")

        time.sleep(0.5)  # Wait for render

        # Capture screenshot
        screenshot_path = capture_screenshot(driver, panel_name, BASELINE_DIR)

        if screenshot_path:
            print(f"Baseline captured: {screenshot_path}")
            assert screenshot_path.exists(), f"Failed to save baseline for {panel_name}"
        else:
            pytest.fail(f"Failed to capture baseline for {panel_name}")

    def test_capture_all_baselines(self, driver):
        """Capture baselines for all key panels in one test."""
        results = []

        for panel_config in KEY_PANELS:
            panel_name = panel_config["name"]

            success = navigate_to_panel(driver, panel_config)
            if success:
                time.sleep(0.3)
                screenshot_path = capture_screenshot(driver, panel_name, BASELINE_DIR)
                results.append(
                    {
                        "panel": panel_name,
                        "captured": screenshot_path is not None,
                        "path": str(screenshot_path) if screenshot_path else None,
                    }
                )
            else:
                results.append(
                    {
                        "panel": panel_name,
                        "captured": False,
                        "error": "Navigation failed",
                    }
                )

        # Report
        captured_count = sum(1 for r in results if r["captured"])
        print(f"\nCaptured {captured_count}/{len(results)} baselines")

        # Save manifest
        manifest_path = BASELINE_DIR / "manifest.json"
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(
                {
                    "timestamp": datetime.now().isoformat(),
                    "panels": results,
                },
                f,
                indent=2,
            )


class TestVisualComparison:
    """Tests for visual regression comparison."""

    @pytest.mark.parametrize("panel_config", KEY_PANELS)
    def test_compare_panel(self, driver, panel_config):
        """Compare current panel screenshot to baseline."""
        panel_name = panel_config["name"]
        baseline_path = BASELINE_DIR / f"{panel_name}.png"

        # Check if baseline exists
        if not baseline_path.exists():
            pytest.skip(f"No baseline for {panel_name} - run baseline capture first")

        # Navigate and capture current
        if not navigate_to_panel(driver, panel_config):
            pytest.skip(f"Could not navigate to {panel_name}")

        time.sleep(0.5)

        current_path = capture_screenshot(driver, panel_name, CURRENT_DIR)
        if not current_path:
            pytest.fail(f"Failed to capture current screenshot for {panel_name}")

        # Compare
        diff_path = DIFF_DIR / f"{panel_name}_diff.png"
        match, diff_percent = compare_images(baseline_path, current_path, diff_path)

        print(f"{panel_name}: {'MATCH' if match else 'DIFF'} ({diff_percent:.2f}% different)")

        if not match:
            print(f"  Baseline: {baseline_path}")
            print(f"  Current: {current_path}")
            print(f"  Diff: {diff_path}")

        assert (
            match
        ), f"Visual regression detected in {panel_name}: {diff_percent:.2f}% pixels differ"

    def test_compare_all_panels(self, driver):
        """Compare all key panels to baselines."""
        results: list[VisualComparisonResult] = []

        for panel_config in KEY_PANELS:
            panel_name = panel_config["name"]
            baseline_path = BASELINE_DIR / f"{panel_name}.png"

            result = VisualComparisonResult(
                panel_name=panel_name,
                baseline_exists=baseline_path.exists(),
                screenshots_match=False,
                difference_percent=100.0,
            )

            if not baseline_path.exists():
                result.error = "No baseline"
                results.append(result)
                continue

            result.baseline_path = baseline_path

            # Navigate and capture
            if not navigate_to_panel(driver, panel_config):
                result.error = "Navigation failed"
                results.append(result)
                continue

            time.sleep(0.3)

            current_path = capture_screenshot(driver, panel_name, CURRENT_DIR)
            if not current_path:
                result.error = "Screenshot failed"
                results.append(result)
                continue

            result.current_path = current_path

            # Compare
            diff_path = DIFF_DIR / f"{panel_name}_diff.png"
            match, diff_percent = compare_images(baseline_path, current_path, diff_path)

            result.screenshots_match = match
            result.difference_percent = diff_percent
            if not match:
                result.diff_path = diff_path

            results.append(result)

        # Generate report
        self._generate_report(results)

        # Assert no regressions
        regressions = [
            r for r in results if not r.screenshots_match and r.baseline_exists and not r.error
        ]
        if regressions:
            regression_names = [r.panel_name for r in regressions]
            pytest.fail(f"Visual regressions detected in: {regression_names}")

    def _generate_report(self, results: list[VisualComparisonResult]):
        """Generate visual regression report."""
        print("\n" + "=" * 60)
        print("VISUAL REGRESSION REPORT")
        print("=" * 60)

        match_count = sum(1 for r in results if r.screenshots_match)
        baseline_count = sum(1 for r in results if r.baseline_exists)
        regression_count = sum(
            1 for r in results if not r.screenshots_match and r.baseline_exists and not r.error
        )

        print(f"\nTotal panels: {len(results)}")
        print(f"Baselines exist: {baseline_count}")
        print(f"Matching: {match_count}")
        print(f"Regressions: {regression_count}")

        print("\nDetails:")
        for r in results:
            if r.screenshots_match:
                status = "✓ MATCH"
            elif r.error:
                status = f"⚠ {r.error}"
            elif not r.baseline_exists:
                status = "○ No baseline"
            else:
                status = f"✗ DIFF ({r.difference_percent:.2f}%)"

            print(f"  {status}: {r.panel_name}")

        # Save JSON report
        report_path = OUTPUT_DIR / "visual_regression_report.json"
        report_data = {
            "timestamp": datetime.now().isoformat(),
            "summary": {
                "total": len(results),
                "baselines": baseline_count,
                "matching": match_count,
                "regressions": regression_count,
            },
            "results": [
                {
                    "panel": r.panel_name,
                    "baseline_exists": r.baseline_exists,
                    "match": r.screenshots_match,
                    "difference_percent": r.difference_percent,
                    "error": r.error,
                }
                for r in results
            ],
        }

        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report_data, f, indent=2)

        print(f"\nReport saved to: {report_path}")


class TestThemeComparison:
    """Tests for different themes/modes."""

    def test_capture_light_theme(self, driver):
        """Capture screenshots in light theme."""
        # This would require theme switching capability
        pytest.skip("Theme switching not implemented - requires Settings panel interaction")

    def test_capture_dark_theme(self, driver):
        """Capture screenshots in dark theme."""
        pytest.skip("Theme switching not implemented - requires Settings panel interaction")

    def test_compare_themes(self, driver):
        """Compare light and dark theme screenshots."""
        pytest.skip("Theme comparison not implemented")


class TestResolutionVariants:
    """Tests for different window sizes/resolutions."""

    def test_capture_at_1920x1080(self, driver):
        """Capture at 1920x1080."""
        try:
            driver.set_window_size(1920, 1080)
            time.sleep(0.5)

            for panel_config in KEY_PANELS[:3]:  # First 3 panels
                panel_name = panel_config["name"]
                if navigate_to_panel(driver, panel_config):
                    time.sleep(0.3)
                    output_dir = OUTPUT_DIR / "1920x1080"
                    output_dir.mkdir(parents=True, exist_ok=True)
                    capture_screenshot(driver, panel_name, output_dir)
        except Exception as e:
            pytest.skip(f"Resolution change not supported: {e}")

    def test_capture_at_1280x720(self, driver):
        """Capture at 1280x720."""
        try:
            driver.set_window_size(1280, 720)
            time.sleep(0.5)

            for panel_config in KEY_PANELS[:3]:  # First 3 panels
                panel_name = panel_config["name"]
                if navigate_to_panel(driver, panel_config):
                    time.sleep(0.3)
                    output_dir = OUTPUT_DIR / "1280x720"
                    output_dir.mkdir(parents=True, exist_ok=True)
                    capture_screenshot(driver, panel_name, output_dir)
        except Exception as e:
            pytest.skip(f"Resolution change not supported: {e}")


# Standalone baseline capture script
def capture_all_baselines():
    """Standalone function to capture all baselines."""
    if webdriver is None:
        print("selenium not installed")
        return

    if not Path(APP_PATH).exists():
        print(f"App not found at {APP_PATH}")
        return

    print("Starting baseline capture...")

    # Use custom WinAppDriverSession to bypass Selenium 4.x compatibility issues
    from conftest import WinAppDriverSession

    try:
        driver = WinAppDriverSession(APP_PATH, WINAPPDRIVER_URL)
        driver.implicitly_wait(10)

        time.sleep(3)

        results = []
        for panel_config in KEY_PANELS:
            panel_name = panel_config["name"]
            print(f"Capturing {panel_name}...")

            if navigate_to_panel(driver, panel_config):
                time.sleep(0.5)
                path = capture_screenshot(driver, panel_name, BASELINE_DIR)
                results.append({"panel": panel_name, "success": path is not None})
            else:
                results.append(
                    {"panel": panel_name, "success": False, "error": "Navigation failed"}
                )

        # Report
        success_count = sum(1 for r in results if r["success"])
        print(f"\nCaptured {success_count}/{len(results)} baselines")
        print(f"Baselines saved to: {BASELINE_DIR}")

        driver.quit()

    except Exception as e:
        print(f"Baseline capture failed: {e}")


if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1 and sys.argv[1] == "--capture-baselines":
        capture_all_baselines()
    else:
        pytest.main([__file__, "-v", "-s"])
