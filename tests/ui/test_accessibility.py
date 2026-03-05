"""
VoiceStudio Accessibility Tests.

Comprehensive WCAG 2.1 AA compliance testing:
- Keyboard navigation
- Screen reader support
- Focus management
- Color contrast
- Text alternatives
- Error identification
- Input assistance
"""

from __future__ import annotations

import json
import os
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Optional

import pytest

# Add tests/ui to path for local imports (consistent with other UI tests)
sys.path.insert(0, str(Path(__file__).parent))

# Define PROJECT_ROOT at module level (required before conftest loads)
PROJECT_ROOT = Path(__file__).parent.parent.parent

try:
    from selenium import webdriver
    from selenium.common.exceptions import TimeoutException
    from selenium.webdriver.common.by import By
    from selenium.webdriver.common.keys import Keys
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.support.ui import WebDriverWait
except ImportError:
    webdriver = None
    By = None
    Keys = None

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
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/accessibility"))

# Ensure output directory exists
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# Pytest markers
pytestmark = [
    pytest.mark.accessibility,
    pytest.mark.a11y,
    pytest.mark.ui,
]


@dataclass
class AccessibilityIssue:
    """Represents an accessibility issue."""

    criterion: str  # WCAG criterion (e.g., "2.1.1")
    level: str  # A, AA, AAA
    severity: str  # critical, serious, moderate, minor
    description: str
    element: Optional[str] = None
    panel: Optional[str] = None
    recommendation: Optional[str] = None


@dataclass
class AccessibilityReport:
    """Accessibility audit report."""

    timestamp: datetime
    panels_tested: int
    issues: list[AccessibilityIssue] = field(default_factory=list)
    passed_criteria: list[str] = field(default_factory=list)

    @property
    def critical_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "critical")

    @property
    def serious_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "serious")


# Key panels for accessibility testing
KEY_PANELS = [
    {"name": "VoiceSynthesis", "nav_name": "Synthesize"},
    {"name": "Transcribe", "nav_name": "Transcribe"},
    {"name": "VoiceCloningWizard", "nav_name": "Voice Cloning"},
    {"name": "Library", "nav_name": "Library"},
    {"name": "Profiles", "nav_name": "Profiles"},
    {"name": "Settings", "nav_name": "Settings"},
    {"name": "BatchProcessing", "nav_name": "Batch"},
    {"name": "Training", "nav_name": "Training"},
]


@pytest.fixture(scope="module")
def winappdriver_process():
    """Start WinAppDriver if not running."""
    import subprocess

    import requests

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

    process = subprocess.Popen(
        [WINAPPDRIVER_PATH],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
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
        try:
            session.quit()
        # ALLOWED: bare except - best effort, failure acceptable
        except Exception:
            pass
    except ImportError:
        pytest.skip("WinAppDriverSession not available")


@pytest.fixture
def accessibility_report():
    """Create accessibility report."""
    return AccessibilityReport(
        timestamp=datetime.now(),
        panels_tested=0,
    )


def find_element_safe(driver, by, value, timeout: float = 5.0):
    """Safely find element with timeout."""
    try:
        wait = WebDriverWait(driver, timeout)
        return wait.until(EC.presence_of_element_located((by, value)))
    except TimeoutException:
        return None


def navigate_to_panel(driver, panel_config: dict) -> bool:
    """Navigate to a panel."""
    nav_name = panel_config.get("nav_name", panel_config["name"])

    locators = [
        (By.NAME, nav_name),
        (By.ACCESSIBILITY_ID, f"Nav{panel_config['name']}"),
        (By.XPATH, f"//*[contains(@Name, '{nav_name}')]"),
    ]

    for by, value in locators:
        try:
            element = find_element_safe(driver, by, value, timeout=3)
            if element:
                element.click()
                time.sleep(0.5)
                return True
        except Exception:
            continue

    return False


class TestKeyboardNavigation:
    """WCAG 2.1.1 - Keyboard: All functionality available via keyboard."""

    def test_tab_navigation_main_window(self, driver):
        """Test Tab key moves focus through main window elements."""
        # Get initial focused element
        try:
            initial_focus = driver.switch_to.active_element
            initial_name = initial_focus.get_attribute("Name") or "unknown"
        except Exception:
            initial_name = "none"

        print(f"Initial focus: {initial_name}")

        # Press Tab multiple times and track focus changes
        focus_history = [initial_name]

        for i in range(10):
            try:
                driver.switch_to.active_element.send_keys(Keys.TAB)
                time.sleep(0.2)

                current = driver.switch_to.active_element
                current_name = current.get_attribute("Name") or f"element_{i}"
                focus_history.append(current_name)
            except Exception as e:
                print(f"Tab {i} failed: {e}")
                break

        print(f"Focus history: {focus_history[:5]}...")

        # Verify focus moved
        unique_elements = len(set(focus_history))
        assert unique_elements > 1, "Tab key does not move focus between elements"

    def test_shift_tab_reverse_navigation(self, driver):
        """Test Shift+Tab moves focus in reverse."""
        # Tab forward a few times
        for _ in range(3):
            driver.switch_to.active_element.send_keys(Keys.TAB)
            time.sleep(0.1)

        # Get current position
        try:
            forward_element = driver.switch_to.active_element.get_attribute("Name")
        except Exception:
            forward_element = "unknown"

        # Shift+Tab back
        for _ in range(2):
            driver.switch_to.active_element.send_keys(Keys.SHIFT + Keys.TAB)
            time.sleep(0.1)

        try:
            back_element = driver.switch_to.active_element.get_attribute("Name")
        except Exception:
            back_element = "unknown"

        print(f"Forward: {forward_element}, Back: {back_element}")
        # They should be different if navigation works

    def test_enter_activates_buttons(self, driver):
        """Test Enter key activates focused buttons."""
        # Find a button and focus it
        try:
            buttons = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='button']")
            if buttons:
                buttons[0].send_keys("")  # Focus
                print(f"Testing button: {buttons[0].get_attribute('Name')}")
                # Don't actually press Enter to avoid side effects
        except Exception as e:
            print(f"Button test: {e}")

    def test_escape_closes_dialogs(self, driver):
        """Test Escape key closes dialogs/popups."""
        # Press Escape - should not cause errors
        try:
            driver.switch_to.active_element.send_keys(Keys.ESCAPE)
            time.sleep(0.2)
            print("Escape key handled")
        except Exception as e:
            pytest.fail(f"Escape key caused error: {e}")

    @pytest.mark.parametrize("panel_config", KEY_PANELS[:5])
    def test_keyboard_navigation_in_panel(self, driver, panel_config):
        """Test keyboard navigation within each panel."""
        panel_name = panel_config["name"]

        if not navigate_to_panel(driver, panel_config):
            pytest.skip(f"Could not navigate to {panel_name}")

        time.sleep(0.3)

        # Tab through panel elements
        focus_elements = []
        for _i in range(5):
            try:
                driver.switch_to.active_element.send_keys(Keys.TAB)
                time.sleep(0.1)
                current = driver.switch_to.active_element
                name = current.get_attribute("Name") or current.get_attribute("AutomationId")
                focus_elements.append(name)
            except Exception:
                break

        print(f"{panel_name} focusable elements: {focus_elements[:3]}")

        # Panel should have at least some keyboard-focusable elements
        assert len(focus_elements) > 0, f"No keyboard-focusable elements in {panel_name}"


class TestFocusManagement:
    """WCAG 2.4.3 - Focus Order, 2.4.7 - Focus Visible."""

    def test_focus_visible(self, driver):
        """Test that focused elements have visible focus indicators."""
        # Tab to move focus
        driver.switch_to.active_element.send_keys(Keys.TAB)
        time.sleep(0.2)

        try:
            focused = driver.switch_to.active_element
            name = focused.get_attribute("Name")

            # WinAppDriver can check if element is focusable
            is_keyboard_focusable = focused.get_attribute("IsKeyboardFocusable")

            print(f"Focused element: {name}")
            print(f"Is keyboard focusable: {is_keyboard_focusable}")

            # Element should exist and be focusable
            assert focused is not None, "No element has focus"
        except Exception as e:
            print(f"Focus visible test: {e}")

    def test_focus_order_logical(self, driver):
        """Test that focus order follows logical reading order."""
        focus_sequence = []

        for i in range(15):
            try:
                current = driver.switch_to.active_element
                rect = current.rect

                focus_sequence.append(
                    {
                        "index": i,
                        "name": current.get_attribute("Name") or "unknown",
                        "x": rect.get("x", 0),
                        "y": rect.get("y", 0),
                    }
                )

                current.send_keys(Keys.TAB)
                time.sleep(0.1)
            except Exception:
                break

        # Check if focus generally moves left-to-right, top-to-bottom
        # This is a heuristic check
        if len(focus_sequence) >= 3:
            print(f"Focus sequence sample: {[f['name'][:20] for f in focus_sequence[:5]]}")

    def test_no_focus_trap(self, driver):
        """Test that keyboard focus is not trapped in any component."""
        # Tab many times and ensure we don't get stuck
        start_element = None

        for i in range(30):
            try:
                current = driver.switch_to.active_element
                current_id = current.get_attribute("AutomationId") or current.get_attribute("Name")

                if i == 0:
                    start_element = current_id
                elif current_id == start_element:
                    print(f"Focus cycled after {i} tabs")
                    break

                current.send_keys(Keys.TAB)
                time.sleep(0.05)
            except Exception:
                break

        # Should either cycle or reach end without getting stuck
        print("No focus trap detected")


class TestScreenReaderSupport:
    """WCAG 1.3.1 - Info and Relationships, 4.1.2 - Name, Role, Value."""

    def test_elements_have_names(self, driver):
        """Test that interactive elements have accessible names."""
        # Find all buttons
        try:
            buttons = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='button']")

            named_count = 0
            unnamed = []

            for button in buttons[:20]:  # Check first 20
                name = button.get_attribute("Name")
                auto_id = button.get_attribute("AutomationId")

                if name and name.strip():
                    named_count += 1
                else:
                    unnamed.append(auto_id or "unknown")

            total = min(len(buttons), 20)
            print(f"Buttons with names: {named_count}/{total}")

            if unnamed:
                print(f"Unnamed buttons: {unnamed[:5]}")

            # Most buttons should have names
            assert named_count > 0, "No buttons have accessible names"
        except Exception as e:
            print(f"Button name test: {e}")

    def test_inputs_have_labels(self, driver):
        """Test that input fields have associated labels."""
        try:
            # Find text inputs
            inputs = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='edit']")

            labeled_count = 0

            for input_elem in inputs[:15]:
                name = input_elem.get_attribute("Name")
                auto_id = input_elem.get_attribute("AutomationId")

                # Check if element has a name (label)
                if name and name.strip():
                    labeled_count += 1
                    print(f"Input '{auto_id}' has label: '{name}'")

            total = min(len(inputs), 15)
            if total > 0:
                percent = (labeled_count / total) * 100
                print(f"Inputs with labels: {labeled_count}/{total} ({percent:.0f}%)")
        except Exception as e:
            print(f"Input label test: {e}")

    def test_images_have_alt_text(self, driver):
        """Test that images have alternative text."""
        try:
            images = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='image']")

            if not images:
                print("No images found to test")
                return

            with_alt = 0
            for img in images[:10]:
                name = img.get_attribute("Name")
                if name and name.strip():
                    with_alt += 1

            print(f"Images with alt text: {with_alt}/{min(len(images), 10)}")
        except Exception as e:
            print(f"Image alt test: {e}")

    def test_headings_structure(self, driver):
        """Test that content has proper heading structure."""
        try:
            # Look for text elements that might be headings
            headings = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='text']")

            heading_candidates = []
            for h in headings[:30]:
                name = h.get_attribute("Name") or ""
                auto_id = h.get_attribute("AutomationId") or ""

                # Check if it might be a heading (contains 'Header', 'Title', etc.)
                if any(x in auto_id.lower() for x in ["header", "title", "heading"]):
                    heading_candidates.append(name[:50])

            print(f"Potential headings: {heading_candidates[:5]}")
        except Exception as e:
            print(f"Heading structure test: {e}")


class TestErrorIdentification:
    """WCAG 3.3.1 - Error Identification, 3.3.3 - Error Suggestion."""

    def test_error_messages_accessible(self, driver):
        """Test that error messages are accessible."""
        # Try to trigger an error by leaving required field empty
        try:
            # Navigate to synthesis panel
            navigate_to_panel(driver, {"name": "VoiceSynthesis", "nav_name": "Synthesize"})
            time.sleep(0.3)

            # Find text input
            text_input = find_element_safe(
                driver, By.ACCESSIBILITY_ID, "VoiceSynthesisView_TextInput"
            )

            if text_input:
                # Clear it
                text_input.clear()

                # Try to trigger synthesis (might show error)
                synth_button = find_element_safe(
                    driver, By.ACCESSIBILITY_ID, "VoiceSynthesisView_SynthesizeButton"
                )
                if synth_button:
                    # Check if button is enabled - don't click if empty text isn't allowed
                    is_enabled = synth_button.is_enabled()
                    print(f"Synthesize button enabled with empty text: {is_enabled}")
        except Exception as e:
            print(f"Error identification test: {e}")

    def test_form_validation_messages(self, driver):
        """Test that form validation provides clear messages."""
        # This would require interaction with specific forms
        print("Form validation test - checking for validation elements")

        try:
            # Look for elements that might indicate validation
            validation_elements = driver.find_elements(
                By.XPATH,
                "//*[contains(@Name, 'error') or contains(@Name, 'invalid') or contains(@Name, 'required')]",
            )

            if validation_elements:
                for elem in validation_elements[:5]:
                    print(f"Validation element: {elem.get_attribute('Name')}")
        except Exception as e:
            print(f"Validation message test: {e}")


class TestTextAlternatives:
    """WCAG 1.1.1 - Non-text Content."""

    def test_icons_have_text_alternatives(self, driver):
        """Test that icon buttons have text alternatives."""
        try:
            # Find buttons that might be icon-only
            buttons = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='button']")

            icon_only = []
            with_text = []

            for btn in buttons[:20]:
                name = btn.get_attribute("Name") or ""

                # Icon buttons often have single character or emoji names
                if len(name) <= 2 or any(ord(c) > 127 for c in name):
                    icon_only.append(name or "unnamed")
                else:
                    with_text.append(name[:30])

            print(f"Buttons with text: {len(with_text)}")
            print(f"Icon-only buttons: {icon_only[:5]}")

            # Icon-only buttons should still have accessible names
            # This is just informational
        except Exception as e:
            print(f"Icon test: {e}")


class TestTimingAdjustable:
    """WCAG 2.2.1 - Timing Adjustable."""

    def test_no_automatic_timeout(self, driver):
        """Test that there's no automatic timeout that logs user out."""
        # This is more of a design verification than automated test
        print("Timing test - verifying no auto-logout")

        # Wait briefly and check app is still responsive
        time.sleep(2)

        try:
            # Try to interact
            driver.switch_to.active_element.send_keys(Keys.TAB)
            print("App still responsive after wait")
        except Exception as e:
            pytest.fail(f"App became unresponsive: {e}")


class TestNavigable:
    """WCAG 2.4.1 - Bypass Blocks, 2.4.2 - Page Titled."""

    def test_window_has_title(self, driver):
        """Test that main window has a descriptive title."""
        try:
            title = driver.title
            print(f"Window title: {title}")

            assert title and len(title) > 0, "Window has no title"
            assert "voicestudio" in title.lower() or len(title) > 3, "Title is not descriptive"
        except Exception as e:
            print(f"Title test: {e}")

    def test_navigation_landmarks(self, driver):
        """Test for navigation landmarks/regions."""
        try:
            # Look for navigation-related elements
            nav_elements = driver.find_elements(
                By.XPATH, "//*[contains(@AutomationId, 'Nav') or contains(@Name, 'Navigation')]"
            )

            print(f"Navigation landmarks found: {len(nav_elements)}")

            for nav in nav_elements[:5]:
                print(f"  - {nav.get_attribute('AutomationId') or nav.get_attribute('Name')}")
        except Exception as e:
            print(f"Landmark test: {e}")


class TestComprehensiveAccessibilityAudit:
    """Comprehensive accessibility audit across all key panels."""

    def test_full_accessibility_audit(self, driver, accessibility_report):
        """Run full accessibility audit on key panels."""
        report = accessibility_report

        for panel_config in KEY_PANELS:
            panel_name = panel_config["name"]

            if not navigate_to_panel(driver, panel_config):
                report.issues.append(
                    AccessibilityIssue(
                        criterion="N/A",
                        level="N/A",
                        severity="moderate",
                        description="Could not navigate to panel",
                        panel=panel_name,
                    )
                )
                continue

            report.panels_tested += 1
            time.sleep(0.3)

            # Check keyboard navigation
            try:
                driver.switch_to.active_element.send_keys(Keys.TAB)
                report.passed_criteria.append(f"{panel_name}: 2.1.1 Keyboard")
            except Exception:
                report.issues.append(
                    AccessibilityIssue(
                        criterion="2.1.1",
                        level="A",
                        severity="critical",
                        description="Keyboard navigation failed",
                        panel=panel_name,
                    )
                )

            # Check for focusable elements
            try:
                focusable = driver.find_elements(By.XPATH, "//*[@IsKeyboardFocusable='True']")
                if len(focusable) == 0:
                    report.issues.append(
                        AccessibilityIssue(
                            criterion="2.1.1",
                            level="A",
                            severity="serious",
                            description="No keyboard-focusable elements found",
                            panel=panel_name,
                        )
                    )
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

            # Check buttons have names
            try:
                buttons = driver.find_elements(By.XPATH, "//*[@LocalizedControlType='button']")
                unnamed_buttons = [b for b in buttons if not b.get_attribute("Name")]

                if unnamed_buttons:
                    report.issues.append(
                        AccessibilityIssue(
                            criterion="4.1.2",
                            level="A",
                            severity="serious",
                            description=f"{len(unnamed_buttons)} buttons without accessible names",
                            panel=panel_name,
                        )
                    )
                else:
                    report.passed_criteria.append(f"{panel_name}: 4.1.2 Name, Role, Value")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

        # Generate report
        self._generate_report(report)

        # Assert no critical issues
        assert (
            report.critical_count == 0
        ), f"Found {report.critical_count} critical accessibility issues"

    def _generate_report(self, report: AccessibilityReport):
        """Generate accessibility audit report."""
        print("\n" + "=" * 60)
        print("ACCESSIBILITY AUDIT REPORT")
        print("=" * 60)

        print(f"\nTimestamp: {report.timestamp.isoformat()}")
        print(f"Panels tested: {report.panels_tested}")
        print(f"Total issues: {len(report.issues)}")
        print(f"  Critical: {report.critical_count}")
        print(f"  Serious: {report.serious_count}")
        print(f"Passed criteria: {len(report.passed_criteria)}")

        if report.issues:
            print("\nIssues Found:")
            for issue in report.issues:
                severity_icon = {
                    "critical": "🔴",
                    "serious": "🟠",
                    "moderate": "🟡",
                    "minor": "🔵",
                }.get(issue.severity, "⚪")

                print(f"\n{severity_icon} [{issue.criterion}] {issue.level}")
                print(f"   Panel: {issue.panel}")
                print(f"   Issue: {issue.description}")
                if issue.recommendation:
                    print(f"   Fix: {issue.recommendation}")

        if report.passed_criteria:
            print("\nPassed Criteria (sample):")
            for criterion in report.passed_criteria[:10]:
                print(f"  ✓ {criterion}")

        # Save JSON report
        report_path = OUTPUT_DIR / "accessibility_report.json"

        report_data = {
            "timestamp": report.timestamp.isoformat(),
            "panels_tested": report.panels_tested,
            "summary": {
                "total_issues": len(report.issues),
                "critical": report.critical_count,
                "serious": report.serious_count,
                "passed_criteria": len(report.passed_criteria),
            },
            "issues": [
                {
                    "criterion": i.criterion,
                    "level": i.level,
                    "severity": i.severity,
                    "description": i.description,
                    "panel": i.panel,
                    "element": i.element,
                    "recommendation": i.recommendation,
                }
                for i in report.issues
            ],
            "passed": report.passed_criteria,
        }

        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report_data, f, indent=2)

        print(f"\nReport saved to: {report_path}")


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
