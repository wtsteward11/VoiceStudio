"""
Visual Regression Testing Utilities for VoiceStudio.

Provides utilities for comparing screenshots against baseline images
to detect unintended visual changes.

Usage:
    from tests.ui.helpers.visual import compare_screenshot, capture_baseline

    # Capture a baseline (run once to establish expected appearance)
    capture_baseline(driver, "settings_panel")

    # Compare current state against baseline
    passed, diff_pct = compare_screenshot(actual_path, "settings_panel")
"""

from __future__ import annotations

import os
from pathlib import Path

# Directories for visual testing
PROJECT_ROOT = Path(__file__).parent.parent.parent.parent
BASELINE_DIR = PROJECT_ROOT / "tests" / "ui" / "baselines"
DIFF_DIR = PROJECT_ROOT / ".buildlogs" / "ui_tests" / "diffs"
SCREENSHOT_DIR = PROJECT_ROOT / ".buildlogs" / "ui_tests" / "screenshots"


def ensure_directories():
    """Ensure all required directories exist."""
    BASELINE_DIR.mkdir(parents=True, exist_ok=True)
    DIFF_DIR.mkdir(parents=True, exist_ok=True)
    SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)


def get_baseline_path(name: str) -> Path:
    """Get the path to a baseline image."""
    return BASELINE_DIR / f"{name}.png"


def get_diff_path(name: str) -> Path:
    """Get the path for a diff output image."""
    return DIFF_DIR / f"{name}_diff.png"


def capture_baseline(driver, name: str) -> Path | None:
    """
    Capture a screenshot as a baseline image.

    Args:
        driver: WinAppDriver session
        name: Name for the baseline (e.g., "settings_panel", "profiles_view")

    Returns:
        Path to saved baseline image, or None on failure
    """
    ensure_directories()

    baseline_path = get_baseline_path(name)

    try:
        if driver.save_screenshot(str(baseline_path)):
            print(f"Baseline captured: {baseline_path}")
            return baseline_path
        return None
    except Exception as e:
        print(f"Failed to capture baseline: {e}")
        return None


def compare_screenshot(
    actual_path: Path,
    baseline_name: str,
    threshold: float = 0.01,
    generate_diff: bool = True,
    auto_capture_missing: bool = True,
) -> tuple[bool, float]:
    """
    Compare a screenshot against a baseline image.

    Args:
        actual_path: Path to the current screenshot
        baseline_name: Name of the baseline to compare against
        threshold: Maximum allowed difference percentage (0.0 to 1.0)
        generate_diff: Whether to generate a visual diff image
        auto_capture_missing: If True, auto-capture baseline on first run

    Returns:
        Tuple of (passed: bool, diff_percentage: float)
        - passed: True if difference is within threshold
        - diff_percentage: Percentage of pixels that differ
    """
    ensure_directories()

    baseline_path = get_baseline_path(baseline_name)

    if not baseline_path.exists():
        if auto_capture_missing and actual_path.exists():
            # Auto-capture baseline on first run
            import shutil

            shutil.copy(actual_path, baseline_path)
            print(f"Auto-captured baseline (first run): {baseline_path}")
            return True, 0.0
        print(f"Baseline not found: {baseline_path}")
        print("Run capture_baseline() first to establish expected appearance")
        return False, 1.0

    if not actual_path.exists():
        print(f"Actual screenshot not found: {actual_path}")
        return False, 1.0

    try:
        import pixelmatch
        from PIL import Image
    except ImportError as e:
        print(f"Required packages not installed: {e}")
        print("Install with: pip install Pillow pixelmatch")
        return False, 1.0

    try:
        # Load images
        baseline_img = Image.open(baseline_path).convert("RGBA")
        actual_img = Image.open(actual_path).convert("RGBA")

        # Check dimensions match
        if baseline_img.size != actual_img.size:
            print(f"Size mismatch: baseline={baseline_img.size}, " f"actual={actual_img.size}")
            # Resize actual to match baseline for comparison
            actual_img = actual_img.resize(baseline_img.size, Image.Resampling.LANCZOS)

        width, height = baseline_img.size
        total_pixels = width * height

        # Create output image for diff
        diff_img = Image.new("RGBA", (width, height))

        # Compare pixels using pixelmatch
        diff_pixels = pixelmatch.pixelmatch(
            baseline_img.tobytes(),
            actual_img.tobytes(),
            width,
            height,
            diff_img.tobytes(),
            threshold=0.1,  # Per-pixel threshold
            includeAA=False,  # Ignore anti-aliasing differences
        )

        diff_pct = diff_pixels / total_pixels
        passed = diff_pct <= threshold

        # Generate diff image if requested and there are differences
        if generate_diff and diff_pixels > 0:
            diff_path = get_diff_path(baseline_name)

            # Create a side-by-side comparison
            comparison = Image.new("RGBA", (width * 3, height))
            comparison.paste(baseline_img, (0, 0))
            comparison.paste(actual_img, (width, 0))
            comparison.paste(diff_img, (width * 2, 0))
            comparison.save(diff_path)

            print(f"Diff image saved: {diff_path}")

        result_str = "PASS" if passed else "FAIL"
        print(
            f"Visual comparison [{result_str}]: "
            f"{baseline_name} - {diff_pct:.2%} difference "
            f"(threshold: {threshold:.2%})"
        )

        return passed, diff_pct

    except Exception as e:
        print(f"Comparison failed: {e}")
        return False, 1.0


def capture_and_compare(
    driver, name: str, threshold: float = 0.01, update_baseline: bool = False
) -> tuple[bool, float]:
    """
    Capture a screenshot and compare against baseline.

    Convenience function that combines capture and comparison.

    Args:
        driver: WinAppDriver session
        name: Name for the screenshot/baseline
        threshold: Maximum allowed difference percentage
        update_baseline: If True, update baseline instead of comparing

    Returns:
        Tuple of (passed: bool, diff_percentage: float)
    """
    ensure_directories()

    if update_baseline:
        result = capture_baseline(driver, name)
        return result is not None, 0.0

    # Capture current screenshot
    from datetime import datetime

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    actual_path = SCREENSHOT_DIR / f"{name}_{timestamp}.png"

    try:
        driver.save_screenshot(str(actual_path))
    except Exception as e:
        print(f"Failed to capture screenshot: {e}")
        return False, 1.0

    return compare_screenshot(actual_path, name, threshold)


def list_baselines() -> list[str]:
    """List all available baseline names."""
    ensure_directories()

    baselines = []
    for path in BASELINE_DIR.glob("*.png"):
        baselines.append(path.stem)
    return sorted(baselines)


def delete_baseline(name: str) -> bool:
    """Delete a baseline image."""
    baseline_path = get_baseline_path(name)

    if baseline_path.exists():
        baseline_path.unlink()
        print(f"Deleted baseline: {name}")
        return True
    return False


# =============================================================================
# Visual Regression Test Helpers
# =============================================================================


class VisualRegressionChecker:
    """
    Helper class for running visual regression checks in tests.

    Usage:
        checker = VisualRegressionChecker(driver)

        # Check a panel
        checker.check_panel("settings", "SettingsView_Root")

        # Check with custom threshold
        checker.check_panel("profiles", "ProfilesView_Root", threshold=0.02)
    """

    def __init__(self, driver, update_baselines: bool = False):
        """
        Initialize the checker.

        Args:
            driver: WinAppDriver session
            update_baselines: If True, update baselines instead of comparing
        """
        self.driver = driver
        self.update_baselines = update_baselines or os.environ.get(
            "UPDATE_VISUAL_BASELINES", ""
        ).lower() in ("1", "true", "yes")
        self.results: list[tuple[str, bool, float]] = []

    def check_panel(self, name: str, panel_id: str, threshold: float = 0.01) -> tuple[bool, float]:
        """
        Navigate to a panel and check its visual appearance.

        Args:
            name: Name for the baseline/screenshot
            panel_id: AutomationId of the panel root element
            threshold: Maximum allowed difference percentage

        Returns:
            Tuple of (passed: bool, diff_percentage: float)
        """
        import time

        # Wait for panel to be visible
        max_wait = 3.0
        start = time.time()

        while time.time() - start < max_wait:
            try:
                panel = self.driver.find_element("accessibility id", panel_id)
                if panel.is_displayed():
                    break
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
            time.sleep(0.1)

        # Brief wait for rendering to complete
        time.sleep(0.3)

        # Capture and compare
        passed, diff_pct = capture_and_compare(self.driver, name, threshold, self.update_baselines)

        self.results.append((name, passed, diff_pct))
        return passed, diff_pct

    def check_current_state(self, name: str, threshold: float = 0.01) -> tuple[bool, float]:
        """
        Check the current visual state without navigation.

        Args:
            name: Name for the baseline/screenshot
            threshold: Maximum allowed difference percentage

        Returns:
            Tuple of (passed: bool, diff_percentage: float)
        """
        import time

        time.sleep(0.2)  # Brief wait for any animations

        passed, diff_pct = capture_and_compare(self.driver, name, threshold, self.update_baselines)

        self.results.append((name, passed, diff_pct))
        return passed, diff_pct

    def get_summary(self) -> dict:
        """Get a summary of all visual checks performed."""
        passed = sum(1 for _, p, _ in self.results if p)
        failed = len(self.results) - passed

        return {
            "total": len(self.results),
            "passed": passed,
            "failed": failed,
            "details": [{"name": name, "passed": p, "diff_pct": d} for name, p, d in self.results],
        }

    def assert_all_passed(self):
        """Assert that all visual checks passed."""
        summary = self.get_summary()

        if summary["failed"] > 0:
            failed_names = [d["name"] for d in summary["details"] if not d["passed"]]
            raise AssertionError(
                f"Visual regression detected in: {', '.join(failed_names)}. "
                f"See .buildlogs/ui_tests/diffs/ for details."
            )
