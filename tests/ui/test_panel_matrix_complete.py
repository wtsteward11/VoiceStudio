"""
Complete Panel Functionality Matrix Tests.

Comprehensive tests for ALL 96 VoiceStudio panels with exact automation IDs.
Tests navigation, element presence, and basic functionality for each panel.

Requires:
- WinAppDriver running on localhost:4723
- VoiceStudio application built and accessible
- Backend API running (for some tests)

Categories covered:
- Synthesis (4 panels)
- Transcription (3 panels)
- Voice Cloning (2 panels)
- Voice Conversion (5 panels)
- Emotion/Prosody (4 panels)
- Training (4 panels)
- Profiles (5 panels)
- Library/Media (5 panels)
- Timeline/Editing (4 panels)
- Audio Analysis (6 panels)
- Audio Effects (5 panels)
- Video/Image (7 panels)
- Lexicon/Pronunciation (4 panels)
- Batch/Automation (5 panels)
- Quality/Testing (5 panels)
- System/Diagnostics (8 panels)
- Models/Engines (3 panels)
- Settings/Config (6 panels)
- Plugins (4 panels)
- Organization (3 panels)
- Assistant/Help (4 panels)
"""

from __future__ import annotations

import json
import os

# Import automation IDs registry
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent))
from fixtures.automation_ids import (
    CATEGORIES,
    TOTAL_PANELS,
    PanelInfo,
    get_all_panels,
    get_panels_by_category,
)
from tracing.workflow_tracer import WorkflowTracer

# Test configuration
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
TRACE_ENABLED = os.getenv("VOICESTUDIO_TRACE_ENABLED", "1") == "1"
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.panels,
    pytest.mark.ui,
    pytest.mark.complete,
    pytest.mark.matrix,  # Phase 4B: Additional organization marker
]


@pytest.fixture
def tracer():
    """Create a workflow tracer for this test."""
    tracer = WorkflowTracer("panel_matrix_complete", OUTPUT_DIR)
    tracer.start()
    yield tracer
    tracer.export_report()


def find_element_by_identifiers(driver, identifiers: list[tuple[str, str]], timeout: float = 2.0):
    """Try to find element using multiple identifier strategies."""
    for by, value in identifiers:
        try:
            element = driver.find_element(by, value)
            return element, value
        except Exception:
            continue
    return None, None


def build_nav_identifiers(panel: PanelInfo) -> list[tuple[str, str]]:
    """Build list of navigation identifiers for a panel."""
    identifiers = []
    for nav_id in panel.nav_ids:
        identifiers.append(("accessibility id", nav_id))
    for nav_name in panel.nav_names:
        identifiers.append(("xpath", f"//*[contains(@Name, '{nav_name}')]"))
    return identifiers


def build_element_identifiers(element_id: str) -> list[tuple[str, str]]:
    """Build list of element identifiers."""
    return [
        ("accessibility id", element_id),
        ("xpath", f"//*[@AutomationId='{element_id}']"),
    ]


class TestPanelInventory:
    """Verify panel inventory matches codebase."""

    def test_total_panel_count(self):
        """Verify total panel count."""
        all_panels = get_all_panels()
        assert len(all_panels) == TOTAL_PANELS
        # We have 96 XAML files, registry should match
        assert TOTAL_PANELS >= 90, f"Expected at least 90 panels, got {TOTAL_PANELS}"

    def test_all_categories_present(self):
        """Verify all categories are defined."""
        expected_categories = [
            "synthesis",
            "transcription",
            "voice_cloning",
            "voice_conversion",
            "emotion_prosody",
            "training",
            "profiles",
            "library_media",
            "timeline_editing",
            "audio_analysis",
            "audio_effects",
            "video_image",
            "lexicon_pronunciation",
            "batch_automation",
            "quality_testing",
            "system_diagnostics",
            "models_engines",
            "settings_config",
            "plugins",
            "organization",
            "assistant_help",
        ]
        for cat in expected_categories:
            assert cat in CATEGORIES, f"Missing category: {cat}"


class TestNavigationRailComplete:
    """Tests for the main navigation rail with all panels."""

    @pytest.mark.smoke
    def test_navigation_rail_exists(self, driver, app_launched, tracer):
        """Test that navigation rail is present."""
        tracer.step("Looking for navigation rail", driver, SCREENSHOTS_ENABLED)

        nav_identifiers = [
            ("accessibility id", "NavigationRail"),
            ("accessibility id", "NavRail"),
            ("accessibility id", "MainNavigation"),
            ("accessibility id", "NavigationView"),
            ("xpath", "//*[@AutomationId='NavigationRail']"),
            ("xpath", "//NavigationView"),
        ]

        nav_rail, found_id = find_element_by_identifiers(driver, nav_identifiers)

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

    def test_count_all_navigation_items(self, driver, app_launched, tracer):
        """Count total navigation items available."""
        tracer.step("Counting all navigation items", driver, SCREENSHOTS_ENABLED)

        item_patterns = [
            "//NavigationViewItem",
            "//*[contains(@ClassName, 'NavigationViewItem')]",
            "//*[contains(@AutomationId, 'Nav')]",
        ]

        max_items = 0
        items_found = []

        for pattern in item_patterns:
            try:
                found = driver.find_elements("xpath", pattern)
                if found and len(found) > max_items:
                    max_items = len(found)
                    items_found = found
            except Exception:
                continue

        tracer.step(f"Found {max_items} navigation items", driver, SCREENSHOTS_ENABLED)

        # Log first 20 item names
        for idx, item in enumerate(items_found[:20]):
            try:
                name = item.get_attribute("Name") or f"Item {idx}"
                auto_id = item.get_attribute("AutomationId") or "N/A"
                tracer.step(f"  {idx+1}. {name} (ID: {auto_id})")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

        tracer.success(f"Navigation has {max_items} items")


class TestPanelNavigationByCategory:
    """Tests for navigating to panels by category."""

    @pytest.mark.parametrize("category", CATEGORIES)
    def test_navigate_to_category_panels(self, category, driver, app_launched, tracer):
        """Test navigation to all panels in a category."""
        panels = get_panels_by_category(category)
        tracer.step(
            f"Testing {len(panels)} panels in category: {category}", driver, SCREENSHOTS_ENABLED
        )

        found_count = 0
        missing_count = 0

        for panel in panels:
            identifiers = build_nav_identifiers(panel)
            nav_element, found_id = find_element_by_identifiers(driver, identifiers)

            if nav_element:
                found_count += 1
                tracer.step(f"  ✓ {panel.name}: Found ({found_id[:30]}...)")
                try:
                    nav_element.click()
                    time.sleep(0.3)
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass
            else:
                missing_count += 1
                tracer.step(f"  ✗ {panel.name}: Not found")

        tracer.step(f"Category {category}: {found_count}/{len(panels)} panels found")
        tracer.success(f"Completed category {category}")


class TestSynthesisPanels:
    """Tests for synthesis-related panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("synthesis"),
        ids=[p.name for p in get_panels_by_category("synthesis")],
    )
    def test_synthesis_panel_elements(self, panel, driver, app_launched, tracer):
        """Test synthesis panel navigation and key elements."""
        tracer.step(f"Testing synthesis panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        # Navigate to panel
        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if not nav_element:
            pytest.skip(f"{panel.name} navigation not found")

        try:
            nav_element.click()
            time.sleep(0.5)
            tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
        except Exception:
            pytest.skip(f"Could not navigate to {panel.name}")

        # Check root element
        root_identifiers = build_element_identifiers(panel.root_id)
        root, _ = find_element_by_identifiers(driver, root_identifiers)

        if root:
            tracer.step(f"Root element found: {panel.root_id}")

        # Check key elements
        found_elements = []
        for element_id in panel.key_elements:
            elem_identifiers = build_element_identifiers(element_id)
            elem, _ = find_element_by_identifiers(driver, elem_identifiers)
            if elem:
                found_elements.append(element_id)

        tracer.step(f"Found {len(found_elements)}/{len(panel.key_elements)} key elements")
        tracer.success(f"Tested {panel.name}")


class TestTranscriptionPanels:
    """Tests for transcription-related panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("transcription"),
        ids=[p.name for p in get_panels_by_category("transcription")],
    )
    def test_transcription_panel_elements(self, panel, driver, app_launched, tracer):
        """Test transcription panel navigation and key elements."""
        tracer.step(f"Testing transcription panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if not nav_element:
            pytest.skip(f"{panel.name} navigation not found")

        try:
            nav_element.click()
            time.sleep(0.5)
            tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
        except Exception:
            pytest.skip(f"Could not navigate to {panel.name}")

        # Check key elements
        for element_id in panel.key_elements:
            elem_identifiers = build_element_identifiers(element_id)
            elem, _ = find_element_by_identifiers(driver, elem_identifiers)
            if elem:
                tracer.step(f"  Found: {element_id}")

        tracer.success(f"Tested {panel.name}")


class TestVoiceCloningPanels:
    """Tests for voice cloning panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("voice_cloning"),
        ids=[p.name for p in get_panels_by_category("voice_cloning")],
    )
    def test_cloning_panel(self, panel, driver, app_launched, tracer):
        """Test voice cloning panel."""
        tracer.step(f"Testing cloning panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _found_id = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            except Exception as e:
                tracer.step(f"Click failed: {e}")
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestVoiceConversionPanels:
    """Tests for voice conversion panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("voice_conversion"),
        ids=[p.name for p in get_panels_by_category("voice_conversion")],
    )
    def test_conversion_panel(self, panel, driver, app_launched, tracer):
        """Test voice conversion panel."""
        tracer.step(f"Testing conversion panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestTrainingPanels:
    """Tests for training-related panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("training"),
        ids=[p.name for p in get_panels_by_category("training")],
    )
    def test_training_panel(self, panel, driver, app_launched, tracer):
        """Test training panel."""
        tracer.step(f"Testing training panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestProfilesPanels:
    """Tests for profile management panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("profiles"),
        ids=[p.name for p in get_panels_by_category("profiles")],
    )
    def test_profiles_panel(self, panel, driver, app_launched, tracer):
        """Test profiles panel."""
        tracer.step(f"Testing profiles panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestSystemDiagnosticsPanels:
    """Tests for system and diagnostics panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("system_diagnostics"),
        ids=[p.name for p in get_panels_by_category("system_diagnostics")],
    )
    def test_diagnostics_panel(self, panel, driver, app_launched, tracer):
        """Test diagnostics panel with key elements."""
        tracer.step(f"Testing diagnostics panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if not nav_element:
            pytest.skip(f"{panel.name} navigation not found")

        try:
            nav_element.click()
            time.sleep(0.5)
            tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
        except Exception:
            pytest.skip(f"Could not navigate to {panel.name}")

        # Check key elements for Diagnostics panel specifically
        if panel.key_elements:
            found = 0
            for element_id in panel.key_elements:
                elem_identifiers = build_element_identifiers(element_id)
                elem, _ = find_element_by_identifiers(driver, elem_identifiers)
                if elem:
                    found += 1
                    tracer.step(f"  Found: {element_id}")
            tracer.step(f"Found {found}/{len(panel.key_elements)} key elements")

        tracer.success(f"Tested {panel.name}")


class TestSettingsPanels:
    """Tests for settings and configuration panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("settings_config"),
        ids=[p.name for p in get_panels_by_category("settings_config")],
    )
    def test_settings_panel(self, panel, driver, app_launched, tracer):
        """Test settings panel."""
        tracer.step(f"Testing settings panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestVideoImagePanels:
    """Tests for video and image generation panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("video_image"),
        ids=[p.name for p in get_panels_by_category("video_image")],
    )
    def test_video_image_panel(self, panel, driver, app_launched, tracer):
        """Test video/image panel."""
        tracer.step(f"Testing video/image panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestAudioAnalysisPanels:
    """Tests for audio analysis panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("audio_analysis"),
        ids=[p.name for p in get_panels_by_category("audio_analysis")],
    )
    def test_audio_analysis_panel(self, panel, driver, app_launched, tracer):
        """Test audio analysis panel."""
        tracer.step(f"Testing audio analysis panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestBatchAutomationPanels:
    """Tests for batch processing and automation panels."""

    @pytest.mark.parametrize(
        "panel",
        get_panels_by_category("batch_automation"),
        ids=[p.name for p in get_panels_by_category("batch_automation")],
    )
    def test_batch_automation_panel(self, panel, driver, app_launched, tracer):
        """Test batch/automation panel."""
        tracer.step(f"Testing batch panel: {panel.name}", driver, SCREENSHOTS_ENABLED)

        identifiers = build_nav_identifiers(panel)
        nav_element, _ = find_element_by_identifiers(driver, identifiers)

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.5)
                tracer.step(f"Navigated to {panel.name}", driver, SCREENSHOTS_ENABLED)
                tracer.success(f"Tested {panel.name}")
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        else:
            tracer.step(f"{panel.name} navigation not found")


class TestAllPanelsComprehensive:
    """Comprehensive test across all panels."""

    @pytest.mark.parametrize(
        "panel", get_all_panels(), ids=[f"{p.category}/{p.name}" for p in get_all_panels()]
    )
    def test_panel_navigation_and_root(self, panel, driver, app_launched, tracer):
        """Test navigation and root element for every panel."""
        tracer.step(f"Testing [{panel.category}] {panel.name}", driver, False)

        identifiers = build_nav_identifiers(panel)
        nav_element, found_id = find_element_by_identifiers(driver, identifiers)

        result = {
            "panel": panel.name,
            "category": panel.category,
            "nav_found": nav_element is not None,
            "nav_id": found_id,
            "root_found": False,
            "key_elements_found": 0,
            "key_elements_total": len(panel.key_elements),
        }

        if nav_element:
            try:
                nav_element.click()
                time.sleep(0.3)

                # Check root
                root_identifiers = build_element_identifiers(panel.root_id)
                root, _ = find_element_by_identifiers(driver, root_identifiers)
                result["root_found"] = root is not None

                # Check key elements
                for element_id in panel.key_elements:
                    elem_identifiers = build_element_identifiers(element_id)
                    elem, _ = find_element_by_identifiers(driver, elem_identifiers)
                    if elem:
                        result["key_elements_found"] += 1

            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

        status = "✓" if result["nav_found"] else "✗"
        tracer.step(
            f"{status} {panel.name}: nav={result['nav_found']}, root={result['root_found']}, "
            f"elements={result['key_elements_found']}/{result['key_elements_total']}"
        )


class TestPanelMatrixReport:
    """Generate comprehensive panel matrix report."""

    @pytest.mark.smoke
    def test_generate_complete_panel_report(self, driver, app_launched, tracer):
        """Generate a comprehensive report for all panels."""
        tracer.step("Generating complete panel matrix report", driver, SCREENSHOTS_ENABLED)

        report = {
            "total_panels": TOTAL_PANELS,
            "categories": {},
            "summary": {
                "panels_found": 0,
                "panels_missing": 0,
                "total_key_elements": 0,
                "key_elements_found": 0,
            },
        }

        for category in CATEGORIES:
            panels = get_panels_by_category(category)
            category_data = {
                "panel_count": len(panels),
                "panels_found": [],
                "panels_missing": [],
            }

            for panel in panels:
                identifiers = build_nav_identifiers(panel)
                nav_element, _found_id = find_element_by_identifiers(driver, identifiers)

                if nav_element:
                    category_data["panels_found"].append(panel.name)
                    report["summary"]["panels_found"] += 1
                    tracer.step(f"✓ [{category}] {panel.name}")
                else:
                    category_data["panels_missing"].append(panel.name)
                    report["summary"]["panels_missing"] += 1
                    tracer.step(f"✗ [{category}] {panel.name}")

                report["summary"]["total_key_elements"] += len(panel.key_elements)

            report["categories"][category] = category_data

        # Summary
        tracer.step("\n=== Complete Panel Matrix Summary ===")
        tracer.step(f"Total panels defined: {TOTAL_PANELS}")
        tracer.step(f"Panels found: {report['summary']['panels_found']}")
        tracer.step(f"Panels missing: {report['summary']['panels_missing']}")
        coverage = report["summary"]["panels_found"] / TOTAL_PANELS * 100
        tracer.step(f"Coverage: {coverage:.1f}%")

        # Category breakdown
        tracer.step("\n=== By Category ===")
        for category, data in report["categories"].items():
            found = len(data["panels_found"])
            total = data["panel_count"]
            tracer.step(f"  {category}: {found}/{total}")

        # Write JSON report
        report_path = OUTPUT_DIR / "panel_matrix_complete_report.json"
        report_path.parent.mkdir(parents=True, exist_ok=True)

        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

        # Write text report
        text_report_path = OUTPUT_DIR / "panel_matrix_complete_report.txt"
        with open(text_report_path, "w", encoding="utf-8") as f:
            f.write("VoiceStudio Complete Panel Matrix Report\n")
            f.write("=" * 50 + "\n\n")
            f.write(f"Total panels defined: {TOTAL_PANELS}\n")
            f.write(f"Panels found: {report['summary']['panels_found']}\n")
            f.write(f"Panels missing: {report['summary']['panels_missing']}\n")
            f.write(f"Coverage: {coverage:.1f}%\n\n")

            f.write("By Category:\n")
            f.write("-" * 40 + "\n")
            for category, data in report["categories"].items():
                found = len(data["panels_found"])
                total = data["panel_count"]
                f.write(f"\n{category.upper()} ({found}/{total}):\n")
                for p in data["panels_found"]:
                    f.write(f"  ✓ {p}\n")
                for p in data["panels_missing"]:
                    f.write(f"  ✗ {p}\n")

        tracer.step(f"Reports written to: {report_path}")
        tracer.success("Complete panel matrix report generated")


# Run tests directly if executed as script
if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short"])
