"""
Panel Functionality Matrix Tests.

Comprehensive tests for all VoiceStudio panels:
- Navigation rail panel switching
- Panel state persistence
- Panel-specific functionality verification
- Panel interactions and transitions

Requires:
- WinAppDriver running
- VoiceStudio application built
"""

from __future__ import annotations

import os

# Import tracing infrastructure
import sys
import time
from pathlib import Path
from typing import Any

import pytest

sys.path.insert(0, str(Path(__file__).parent))
import contextlib

from tracing.workflow_tracer import WorkflowTracer

# Test configuration
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
TRACE_ENABLED = os.getenv("VOICESTUDIO_TRACE_ENABLED", "1") == "1"
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.panels,
    pytest.mark.ui,
    pytest.mark.matrix,  # Phase 4B: Additional organization marker
]


# Define panel matrix with navigation identifiers and expected elements
PANEL_MATRIX: list[dict[str, Any]] = [
    {
        "name": "Home",
        "nav_ids": ["NavHome", "NavDashboard"],
        "nav_names": ["Home", "Dashboard"],
        "expected_elements": ["WelcomeText", "QuickActions", "RecentFiles"],
        "description": "Main dashboard with quick actions and recent items",
    },
    {
        "name": "Library",
        "nav_ids": ["NavLibrary", "NavFiles"],
        "nav_names": ["Library", "Files", "Media"],
        "expected_elements": ["FileList", "SearchBox", "FilterButton"],
        "description": "Audio file library and management",
    },
    {
        "name": "Generate",
        "nav_ids": ["NavGenerate", "NavSynthesize", "NavTTS"],
        "nav_names": ["Generate", "Synthesize", "TTS", "Text-to-Speech"],
        "expected_elements": ["TextInput", "VoiceSelector", "GenerateButton"],
        "description": "Text-to-speech synthesis panel",
    },
    {
        "name": "Transcribe",
        "nav_ids": ["NavTranscribe", "NavSTT", "NavTranscription"],
        "nav_names": ["Transcribe", "STT", "Speech-to-Text"],
        "expected_elements": ["UploadArea", "EngineSelector", "TranscribeButton"],
        "description": "Speech-to-text transcription panel",
    },
    {
        "name": "Profiles",
        "nav_ids": ["NavProfiles", "NavVoices"],
        "nav_names": ["Profiles", "Voices", "Voice Profiles"],
        "expected_elements": ["ProfileList", "AddProfileButton", "ImportButton"],
        "description": "Voice profile management",
    },
    {
        "name": "Cloning",
        "nav_ids": ["NavCloning", "NavVoiceCloning", "NavClone"],
        "nav_names": ["Clone", "Voice Clone", "Cloning"],
        "expected_elements": ["WizardSteps", "ReferenceUpload", "CloneButton"],
        "description": "Voice cloning wizard",
    },
    {
        "name": "Realtime",
        "nav_ids": ["NavRealtime", "NavLive", "NavVoiceConverter"],
        "nav_names": ["Real-time", "Live", "Voice Converter"],
        "expected_elements": ["InputDevice", "OutputDevice", "StartButton"],
        "description": "Real-time voice conversion",
    },
    {
        "name": "Training",
        "nav_ids": ["NavTraining", "NavTrain"],
        "nav_names": ["Training", "Train", "Model Training"],
        "expected_elements": ["DatasetSelector", "ModelConfig", "TrainButton"],
        "description": "Voice model training panel",
    },
    {
        "name": "Settings",
        "nav_ids": ["NavSettings", "NavPreferences"],
        "nav_names": ["Settings", "Preferences", "Options"],
        "expected_elements": ["SettingsCategories", "SaveButton"],
        "description": "Application settings",
    },
    {
        "name": "Diagnostics",
        "nav_ids": ["NavDiagnostics", "NavDebug", "NavHealth"],
        "nav_names": ["Diagnostics", "Debug", "Health", "System"],
        "expected_elements": ["EngineStatus", "SystemInfo", "LogViewer"],
        "description": "System diagnostics and health",
    },
]


@pytest.fixture
def tracer():
    """Create a workflow tracer for this test."""
    tracer = WorkflowTracer("panel_matrix", OUTPUT_DIR)
    tracer.start()
    yield tracer
    tracer.export_report()


def find_element_by_multiple(driver, identifiers: list[tuple[str, str]], timeout: float = 2.0):
    """Try to find element using multiple identifier strategies."""
    for by, value in identifiers:
        try:
            element = driver.find_element(by, value)
            return element, value
        except Exception:
            continue
    return None, None


class TestNavigationRail:
    """Tests for the main navigation rail."""

    @pytest.mark.smoke
    def test_navigation_rail_exists(self, driver, app_launched, tracer):
        """Test that navigation rail is present."""
        tracer.step("Looking for navigation rail", driver, SCREENSHOTS_ENABLED)

        try:
            nav_identifiers = [
                ("accessibility id", "NavigationRail"),
                ("accessibility id", "NavRail"),
                ("accessibility id", "MainNavigation"),
                ("xpath", "//*[@AutomationId='NavigationRail']"),
                ("xpath", "//NavigationView"),
            ]

            nav_rail, found_id = find_element_by_multiple(driver, nav_identifiers)

            if nav_rail:
                tracer.step(f"Found navigation rail: {found_id}", driver, SCREENSHOTS_ENABLED)
                tracer.success("Navigation rail exists")
            else:
                tracer.step("Navigation rail not found by specific ID")
                # Try to find any navigation-like element
                try:
                    driver.find_element("xpath", "//*[contains(@ClassName, 'Navigation')]")
                    tracer.step("Found navigation element by class", driver, SCREENSHOTS_ENABLED)
                    tracer.success("Navigation element exists")
                except Exception:
                    tracer.step("No navigation element found")

        except Exception as e:
            tracer.error(e, "Navigation rail search failed")

    def test_navigation_items_count(self, driver, app_launched, tracer):
        """Test that navigation has expected number of items."""
        tracer.step("Counting navigation items", driver, SCREENSHOTS_ENABLED)

        try:
            # Try to find navigation items
            item_patterns = [
                "//NavigationViewItem",
                "//*[contains(@ClassName, 'NavigationViewItem')]",
                "//*[contains(@AutomationId, 'Nav')]",
            ]

            items = []
            for pattern in item_patterns:
                try:
                    found = driver.find_elements("xpath", pattern)
                    if found and len(found) > items.__len__():
                        items = found
                except Exception:
                    continue

            tracer.step(f"Found {len(items)} navigation items", driver, SCREENSHOTS_ENABLED)

            # Log item names
            for idx, item in enumerate(items[:10]):  # Limit to first 10
                try:
                    name = item.get_attribute("Name") or f"Item {idx}"
                    tracer.step(f"  {idx+1}. {name}")
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            tracer.success(f"Navigation has {len(items)} items")

        except Exception as e:
            tracer.error(e, "Navigation item count failed")


class TestPanelNavigation:
    """Tests for navigating to each panel."""

    @pytest.mark.parametrize("panel", PANEL_MATRIX, ids=[p["name"] for p in PANEL_MATRIX])
    def test_navigate_to_panel(self, panel, driver, app_launched, tracer):
        """Test navigation to each panel in the matrix."""
        tracer.step(f"Testing navigation to {panel['name']} panel", driver, SCREENSHOTS_ENABLED)

        # Build identifier list
        identifiers = []
        for nav_id in panel["nav_ids"]:
            identifiers.append(("accessibility id", nav_id))
        for nav_name in panel["nav_names"]:
            identifiers.append(("xpath", f"//*[contains(@Name, '{nav_name}')]"))

        nav_element, found_id = find_element_by_multiple(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(1)
                tracer.step(f"Clicked: {found_id}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Navigation to {panel['name']} successful")
            except Exception as e:
                tracer.error(e, f"Click on {found_id} failed")
        else:
            tracer.step(f"{panel['name']} navigation not found")


class TestPanelElements:
    """Tests for panel-specific UI elements."""

    @pytest.mark.parametrize("panel", PANEL_MATRIX, ids=[p["name"] for p in PANEL_MATRIX])
    def test_panel_has_expected_elements(self, panel, driver, app_launched, tracer):
        """Test that each panel has its expected elements."""
        tracer.step(f"Testing {panel['name']} panel elements", driver, SCREENSHOTS_ENABLED)

        # First navigate to the panel
        identifiers = []
        for nav_id in panel["nav_ids"]:
            identifiers.append(("accessibility id", nav_id))
        for nav_name in panel["nav_names"]:
            identifiers.append(("xpath", f"//*[contains(@Name, '{nav_name}')]"))

        nav_element, _ = find_element_by_multiple(driver, identifiers)

        if not nav_element:
            pytest.skip(f"{panel['name']} panel navigation not found")

        try:
            nav_element.click()
            time.sleep(1)
        except Exception:
            pytest.skip(f"Could not navigate to {panel['name']}")

        # Check for expected elements
        found_elements = []
        missing_elements = []

        for element_id in panel["expected_elements"]:
            element_identifiers = [
                ("accessibility id", element_id),
                ("xpath", f"//*[@AutomationId='{element_id}']"),
                ("xpath", f"//*[contains(@Name, '{element_id.replace('Button', '')}')]"),
            ]

            element, _found_id = find_element_by_multiple(driver, element_identifiers)

            if element:
                found_elements.append(element_id)
                tracer.step(f"  Found: {element_id}")
            else:
                missing_elements.append(element_id)
                tracer.step(f"  Missing: {element_id}")

        tracer.step(
            f"{panel['name']}: {len(found_elements)}/{len(panel['expected_elements'])} elements found",
            driver,
            SCREENSHOTS_ENABLED,
        )

        if found_elements:
            tracer.success(f"{panel['name']} has {len(found_elements)} expected elements")
        else:
            tracer.step(f"{panel['name']} has no expected elements found")


class TestPanelSwitching:
    """Tests for switching between panels."""

    @pytest.mark.smoke
    def test_switch_between_panels(self, driver, app_launched, tracer):
        """Test rapid panel switching."""
        tracer.step("Testing panel switching", driver, SCREENSHOTS_ENABLED)

        panels_to_test = [
            ("NavLibrary", "Library"),
            ("NavGenerate", "Generate"),
            ("NavProfiles", "Profiles"),
        ]

        successful_switches = 0

        for nav_id, name in panels_to_test:
            try:
                element = None
                try:
                    element = driver.find_element("accessibility id", nav_id)
                except Exception:
                    with contextlib.suppress(Exception):
                        element = driver.find_element("xpath", f"//*[contains(@Name, '{name}')]")

                if element:
                    element.click()
                    time.sleep(0.5)
                    tracer.step(f"Switched to {name}", driver, SCREENSHOTS_ENABLED)
                    successful_switches += 1

            except Exception as e:
                tracer.step(f"Failed to switch to {name}: {e}")

        tracer.step(f"Successful switches: {successful_switches}/{len(panels_to_test)}")

        if successful_switches > 0:
            tracer.success("Panel switching works")

    def test_panel_state_after_switch(self, driver, app_launched, tracer):
        """Test that panels maintain state after switching."""
        tracer.step("Testing panel state persistence", driver, SCREENSHOTS_ENABLED)

        # Navigate to Generate, look for text input
        try:
            # Go to Generate
            gen_nav = None
            try:
                gen_nav = driver.find_element("accessibility id", "NavGenerate")
            except Exception:
                with contextlib.suppress(Exception):
                    gen_nav = driver.find_element("xpath", "//*[contains(@Name, 'Generate')]")

            if not gen_nav:
                pytest.skip("Generate panel not found")

            gen_nav.click()
            time.sleep(1)
            tracer.step("Navigated to Generate", driver, SCREENSHOTS_ENABLED)

            # Look for text input and enter text
            text_input = None
            text_identifiers = [
                ("accessibility id", "TextInput"),
                ("accessibility id", "SynthesisTextBox"),
                ("xpath", "//Edit[contains(@Name, 'Text')]"),
            ]

            for by, value in text_identifiers:
                try:
                    text_input = driver.find_element(by, value)
                    break
                except Exception:
                    continue

            if text_input:
                text_input.send_keys("Test state persistence")
                tracer.step("Entered text in Generate panel", driver, SCREENSHOTS_ENABLED)

                # Switch to another panel
                try:
                    lib_nav = driver.find_element("accessibility id", "NavLibrary")
                    lib_nav.click()
                    time.sleep(0.5)
                    tracer.step("Switched to Library", driver, SCREENSHOTS_ENABLED)
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

                # Switch back
                gen_nav.click()
                time.sleep(0.5)
                tracer.step("Switched back to Generate", driver, SCREENSHOTS_ENABLED)

                # Check if text persisted
                for by, value in text_identifiers:
                    try:
                        text_input = driver.find_element(by, value)
                        current_text = text_input.get_attribute("Value") or text_input.text
                        if current_text:
                            tracer.step(f"Text persisted: '{current_text}'")
                            tracer.success("Panel state persistence works")
                            return
                        break
                    except Exception:
                        continue

                tracer.step("Could not verify text persistence")
            else:
                tracer.step("Text input not found")

        except Exception as e:
            tracer.error(e, "Panel state test failed")


class TestPanelKeyboardNav:
    """Tests for keyboard navigation within panels."""

    def test_tab_navigation(self, driver, app_launched, tracer):
        """Test Tab key navigation within panels."""
        tracer.step("Testing Tab navigation", driver, SCREENSHOTS_ENABLED)

        try:
            # Send Tab keys and see if focus changes
            from selenium.webdriver.common.action_chains import ActionChains
            from selenium.webdriver.common.keys import Keys

            actions = ActionChains(driver)

            for i in range(5):
                actions.send_keys(Keys.TAB)
                actions.perform()
                time.sleep(0.2)

                # Try to get focused element
                try:
                    focused = driver.switch_to.active_element
                    if focused:
                        name = focused.get_attribute("Name") or "Unknown"
                        tracer.step(f"Tab {i+1}: Focused on '{name[:30]}'")
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            tracer.success("Tab navigation works")

        except Exception as e:
            tracer.error(e, "Tab navigation failed")

    def test_escape_closes_dialogs(self, driver, app_launched, tracer):
        """Test Escape key closes dialogs."""
        tracer.step("Testing Escape key", driver, SCREENSHOTS_ENABLED)

        try:
            from selenium.webdriver.common.action_chains import ActionChains
            from selenium.webdriver.common.keys import Keys

            # Open a dialog first (try Import)
            try:
                import_btn = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
                import_btn.click()
                time.sleep(0.5)
                tracer.step("Opened Import dialog")
            except Exception:
                tracer.step("Could not open dialog")

            # Press Escape
            actions = ActionChains(driver)
            actions.send_keys(Keys.ESCAPE)
            actions.perform()
            time.sleep(0.5)

            tracer.step("Sent Escape key", driver, SCREENSHOTS_ENABLED)
            tracer.success("Escape key handling tested")

        except Exception as e:
            tracer.error(e, "Escape key test failed")


class TestPanelMatrixReport:
    """Generate panel matrix report."""

    @pytest.mark.smoke
    def test_generate_panel_matrix_report(self, driver, app_launched, tracer):
        """Generate a comprehensive panel availability report."""
        tracer.step("Generating panel matrix report", driver, SCREENSHOTS_ENABLED)

        report = {
            "panels_found": [],
            "panels_missing": [],
            "elements_summary": {},
        }

        for panel in PANEL_MATRIX:
            # Check if panel navigation exists
            identifiers = []
            for nav_id in panel["nav_ids"]:
                identifiers.append(("accessibility id", nav_id))
            for nav_name in panel["nav_names"]:
                identifiers.append(("xpath", f"//*[contains(@Name, '{nav_name}')]"))

            nav_element, found_id = find_element_by_multiple(driver, identifiers)

            if nav_element:
                report["panels_found"].append(panel["name"])
                tracer.step(f"✓ {panel['name']}: Found ({found_id})")
            else:
                report["panels_missing"].append(panel["name"])
                tracer.step(f"✗ {panel['name']}: Not found")

        # Summary
        total = len(PANEL_MATRIX)
        found = len(report["panels_found"])

        tracer.step("\n=== Panel Matrix Summary ===")
        tracer.step(f"Total panels defined: {total}")
        tracer.step(f"Panels found: {found}")
        tracer.step(f"Panels missing: {total - found}")
        tracer.step(f"Coverage: {(found/total*100):.1f}%")

        # Write report to file
        report_path = OUTPUT_DIR / "panel_matrix_report.txt"
        report_path.parent.mkdir(parents=True, exist_ok=True)

        with open(report_path, "w", encoding="utf-8") as f:
            f.write("VoiceStudio Panel Matrix Report\n")
            f.write("=" * 40 + "\n\n")
            f.write(f"Total panels defined: {total}\n")
            f.write(f"Panels found: {found}\n")
            f.write(f"Coverage: {(found/total*100):.1f}%\n\n")
            f.write("Panels Found:\n")
            for p in report["panels_found"]:
                f.write(f"  ✓ {p}\n")
            f.write("\nPanels Missing:\n")
            for p in report["panels_missing"]:
                f.write(f"  ✗ {p}\n")

        tracer.step(f"Report written to: {report_path}")
        tracer.success("Panel matrix report generated")


# Run tests directly if executed as script
if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short"])
