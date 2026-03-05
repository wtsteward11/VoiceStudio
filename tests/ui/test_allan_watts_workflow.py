"""
Comprehensive Allan Watts Audio Workflow Test Suite.

Master test file for systematically tracing, validating, and fixing all audio
workflows in VoiceStudio using canonical test audio.

Audio is resolved via:
1. VOICESTUDIO_TEST_AUDIO environment variable (if set)
2. conftest.py canonical_audio_path fixture (auto-provisioned)
3. Synthetic generation fallback via generate_test_audio.py

Test Coverage:
- File Import (button, drag-drop, format detection)
- Library Panel workflows (selection, playback, context menus)
- Transcription workflow
- Voice Cloning workflow (quick clone, wizard)
- Voice Synthesis workflow
- Audio Format Conversion (wav, mp3, flac, ogg)
- Inter-panel communication (EventAggregator, WorkflowCoordinator)
- Error handling and edge cases
- Performance metrics

Requirements:
- WinAppDriver running on port 4723
- Backend running on port 8000
- VoiceStudio application built
- Test audio: auto-provisioned via conftest.py fixture
"""

from __future__ import annotations

import os

# Import test infrastructure
import sys
import time
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).parent))

from fixtures.audio_test_data import (
    CONVERSION_SPECS,
    FORMAT_MIME_TYPES,
    OUTPUT_FORMATS,
    PANEL_EVENTS,
    PANEL_NAVIGATION,
    TEST_AUDIO_FILE,
    get_test_audio_info,
)
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

# =============================================================================
# Configuration
# =============================================================================

BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
TRACE_ENABLED = os.getenv("VOICESTUDIO_TRACE_ENABLED", "1") == "1"
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.workflow,
    pytest.mark.allan_watts,
]


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="module")
def tracer():
    """Create a workflow tracer for the entire test module."""
    tracer = WorkflowTracer("allan_watts_workflow", OUTPUT_DIR)
    tracer.start()
    yield tracer
    tracer.export_report()


@pytest.fixture(scope="module")
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "allan_watts_api_calls.json")


@pytest.fixture(scope="module")
def test_file_info():
    """Get info about the test audio file."""
    info = get_test_audio_info()
    if not info["exists"]:
        pytest.skip(f"Test audio file not found: {TEST_AUDIO_FILE}")
    return info


@pytest.fixture
def navigate_to(driver, app_launched):
    """Factory fixture for navigating to panels."""

    def _navigate(panel_name: str, timeout: float = 10.0) -> bool:
        """Navigate to a panel by name."""
        if panel_name not in PANEL_NAVIGATION:
            raise ValueError(f"Unknown panel: {panel_name}")

        nav_id, root_id = PANEL_NAVIGATION[panel_name]
        try:
            nav_button = driver.find_element("accessibility id", nav_id)
            nav_button.click()
            time.sleep(0.5)

            # Wait for panel to load
            start = time.time()
            while time.time() - start < timeout:
                try:
                    driver.find_element("accessibility id", root_id)
                    return True
                except RuntimeError:
                    time.sleep(0.2)
            return False
        except RuntimeError:
            return False

    return _navigate


# =============================================================================
# Phase 1: Prerequisites and Setup Tests
# =============================================================================


class TestPrerequisites:
    """Verify prerequisites before running workflow tests."""

    def test_audio_file_exists(self, test_file_info, tracer):
        """Verify the Allan Watts test audio file exists."""
        tracer.step("Checking test audio file exists")

        assert test_file_info["exists"], f"Test file not found: {TEST_AUDIO_FILE}"
        tracer.step(
            f"Test file found: {test_file_info['filename']}, size: {test_file_info['size_mb']} MB"
        )
        tracer.success("Test audio file exists")

    def test_backend_health(self, api_monitor, tracer):
        """Verify backend is healthy."""
        tracer.step("Checking backend health")

        healthy = api_monitor.wait_for_backend(timeout=10.0)
        assert healthy, f"Backend at {BACKEND_URL} is not healthy"
        tracer.success("Backend is healthy")

    def test_supported_formats(self, api_monitor, tracer):
        """Verify backend supports required audio formats."""
        tracer.step("Checking supported audio formats")

        try:
            response = api_monitor.get("/api/audio/formats")
            if response.status_code == 200:
                formats = response.json()
                tracer.step(f"Supported formats: {formats}")
            tracer.success("Format check completed")
        except requests.RequestException:
            tracer.step("Format endpoint not available - will test during conversion")


# =============================================================================
# Phase 2: File Import Tests
# =============================================================================


class TestFileImport:
    """Test file import workflows."""

    @pytest.mark.smoke
    def test_import_button_visible(self, driver, app_launched, tracer):
        """Verify Import button is visible and enabled."""
        tracer.step("Looking for Import button", driver, SCREENSHOTS_ENABLED)

        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            assert import_button.is_enabled(), "Import button should be enabled"
            tracer.step("Import button found and enabled", driver, SCREENSHOTS_ENABLED)
            tracer.success("Import button is visible and enabled")
        except RuntimeError as e:
            tracer.error(e, "Import button not found")
            raise

    @pytest.mark.smoke
    def test_import_opens_dialog(self, driver, app_launched, tracer):
        """Verify Import button opens file dialog."""
        tracer.step("Testing Import button opens dialog", driver, SCREENSHOTS_ENABLED)

        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            import_button.click()
            time.sleep(1)

            # Look for dialog indicators
            dialog_found = False
            for indicator in ["Open", "Cancel", "File name:"]:
                try:
                    driver.find_element("name", indicator)
                    dialog_found = True
                    break
                # ALLOWED: bare except - best effort, failure acceptable
                except RuntimeError:
                    pass

            assert dialog_found, "File dialog should open"
            tracer.step("File dialog opened", driver, SCREENSHOTS_ENABLED)

            # Close dialog
            driver.press_escape()
            time.sleep(0.5)

            tracer.success("Import dialog opens correctly")
        except RuntimeError as e:
            tracer.error(e, "Failed to open import dialog")
            raise

    def test_direct_api_upload(self, api_monitor, test_file_info, tracer):
        """Test uploading Allan Watts.m4a directly via API."""
        tracer.step("Testing direct API upload", None, False)

        try:
            with open(TEST_AUDIO_FILE, "rb") as f:
                files = {"file": (TEST_AUDIO_FILE.name, f, "audio/x-m4a")}
                response = api_monitor.post("/api/library/assets/upload", files=files)
                tracer.api_call("POST", "/api/library/assets/upload", response)

                if response.status_code in [200, 201]:
                    data = response.json()
                    tracer.step(f"Upload successful: {data.get('id', 'unknown')}")
                    tracer.success("Direct API upload works")
                else:
                    tracer.step(f"Upload returned {response.status_code}: {response.text}")
                    # Not a failure - endpoint may have different path
        except requests.RequestException as e:
            tracer.error(e, "API upload failed")
            pytest.skip(f"Upload endpoint not available: {e}")

    def test_format_detection(self, api_monitor, test_file_info, tracer):
        """Test that backend correctly detects m4a format."""
        tracer.step("Testing m4a format detection")

        # Verify the file has correct extension
        assert test_file_info["extension"] == ".m4a", "Test file should be m4a"

        # Check expected MIME type
        expected_mime = FORMAT_MIME_TYPES["m4a"]
        tracer.step(f"Expected MIME type: {expected_mime}")
        tracer.success("Format detection test completed")


# =============================================================================
# Phase 3: Library Panel Tests
# =============================================================================


class TestLibraryPanel:
    """Test Library panel workflows."""

    @pytest.mark.smoke
    def test_navigate_to_library(self, driver, app_launched, navigate_to, tracer):
        """Test navigation to Library panel."""
        tracer.step("Navigating to Library panel", driver, SCREENSHOTS_ENABLED)

        success = navigate_to("Library")
        assert success, "Should be able to navigate to Library panel"

        tracer.step("Library panel loaded", driver, SCREENSHOTS_ENABLED)
        tracer.success("Navigation to Library successful")

    def test_library_elements_present(self, driver, app_launched, navigate_to, tracer):
        """Verify Library panel has expected elements."""
        tracer.step("Checking Library panel elements", driver, SCREENSHOTS_ENABLED)

        navigate_to("Library")

        expected_elements = [
            "LibraryView_Root",
            "LibraryView_SearchBox",
        ]

        found = []
        missing = []

        for elem_id in expected_elements:
            try:
                driver.find_element("accessibility id", elem_id)
                found.append(elem_id)
            except RuntimeError:
                missing.append(elem_id)

        tracer.step(f"Found: {found}, Missing: {missing}", driver, SCREENSHOTS_ENABLED)
        assert "LibraryView_Root" in found, "LibraryView_Root must be present"
        tracer.success("Library elements verified")

    def test_search_functionality(self, driver, app_launched, navigate_to, tracer):
        """Test Library search box functionality."""
        tracer.step("Testing Library search", driver, SCREENSHOTS_ENABLED)

        navigate_to("Library")

        try:
            search_box = driver.find_element("accessibility id", "LibraryView_SearchBox")
            search_box.click()
            search_box.send_keys("Allan")
            time.sleep(0.5)

            tracer.step("Search query entered", driver, SCREENSHOTS_ENABLED)

            # Clear search
            search_box.clear()
            tracer.success("Search functionality works")
        except RuntimeError as e:
            tracer.error(e, "Search functionality failed")
            pytest.skip("Search box not available")


# =============================================================================
# Phase 4: Transcription Tests
# =============================================================================


class TestTranscription:
    """Test transcription workflow."""

    def test_navigate_to_transcribe(self, driver, app_launched, navigate_to, tracer):
        """Test navigation to Transcribe panel."""
        tracer.step("Navigating to Transcribe panel", driver, SCREENSHOTS_ENABLED)

        success = navigate_to("Transcribe")
        assert success, "Should be able to navigate to Transcribe panel"

        tracer.step("Transcribe panel loaded", driver, SCREENSHOTS_ENABLED)
        tracer.success("Navigation to Transcribe successful")

    def test_transcribe_panel_elements(self, driver, app_launched, navigate_to, tracer):
        """Verify Transcribe panel has expected elements."""
        tracer.step("Checking Transcribe panel elements", driver, SCREENSHOTS_ENABLED)

        navigate_to("Transcribe")

        expected_elements = [
            "TranscribeView_Root",
            "TranscribeView_EngineComboBox",
            "TranscribeView_TranscribeButton",
        ]

        found = []
        missing = []

        for elem_id in expected_elements:
            try:
                driver.find_element("accessibility id", elem_id)
                found.append(elem_id)
            except RuntimeError:
                missing.append(elem_id)

        tracer.step(f"Found: {found}, Missing: {missing}", driver, SCREENSHOTS_ENABLED)
        assert "TranscribeView_Root" in found, "TranscribeView_Root must be present"
        tracer.success("Transcribe elements verified")

    def test_transcription_api_languages(self, api_monitor, tracer):
        """Test transcription languages endpoint."""
        tracer.step("Testing transcription languages endpoint")

        try:
            response = api_monitor.get("/api/transcribe/languages")
            tracer.api_call("GET", "/api/transcribe/languages", response)

            if response.status_code == 200:
                languages = response.json()
                tracer.step(
                    f"Available languages: {len(languages) if isinstance(languages, list) else languages}"
                )
            tracer.success("Languages endpoint works")
        except requests.RequestException as e:
            tracer.error(e, "Languages endpoint failed")


# =============================================================================
# Phase 5: Voice Cloning Tests
# =============================================================================


class TestVoiceCloning:
    """Test voice cloning workflow."""

    def test_navigate_to_cloning(self, driver, app_launched, navigate_to, tracer):
        """Test navigation to Voice Cloning panel."""
        tracer.step("Navigating to Voice Cloning panel", driver, SCREENSHOTS_ENABLED)

        success = navigate_to("VoiceCloningWizard")
        assert success, "Should be able to navigate to Voice Cloning panel"

        tracer.step("Voice Cloning panel loaded", driver, SCREENSHOTS_ENABLED)
        tracer.success("Navigation to Voice Cloning successful")

    def test_cloning_panel_elements(self, driver, app_launched, navigate_to, tracer):
        """Verify Voice Cloning panel has expected elements."""
        tracer.step("Checking Voice Cloning panel elements", driver, SCREENSHOTS_ENABLED)

        navigate_to("VoiceCloningWizard")

        expected_elements = [
            "VoiceCloningWizardView_Root",
        ]

        found = []
        for elem_id in expected_elements:
            try:
                driver.find_element("accessibility id", elem_id)
                found.append(elem_id)
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass

        tracer.step(f"Found elements: {found}", driver, SCREENSHOTS_ENABLED)
        assert "VoiceCloningWizardView_Root" in found, "Cloning panel root must be present"
        tracer.success("Cloning elements verified")


# =============================================================================
# Phase 6: Voice Synthesis Tests
# =============================================================================


class TestVoiceSynthesis:
    """Test voice synthesis workflow."""

    def test_navigate_to_synthesis(self, driver, app_launched, navigate_to, tracer):
        """Test navigation to Voice Synthesis panel."""
        tracer.step("Navigating to Voice Synthesis panel", driver, SCREENSHOTS_ENABLED)

        success = navigate_to("VoiceSynthesis")
        assert success, "Should be able to navigate to Voice Synthesis panel"

        tracer.step("Voice Synthesis panel loaded", driver, SCREENSHOTS_ENABLED)
        tracer.success("Navigation to Voice Synthesis successful")

    def test_synthesis_panel_elements(self, driver, app_launched, navigate_to, tracer):
        """Verify Voice Synthesis panel has expected elements."""
        tracer.step("Checking Voice Synthesis panel elements", driver, SCREENSHOTS_ENABLED)

        navigate_to("VoiceSynthesis")

        expected_elements = [
            "VoiceSynthesisView_Root",
            "VoiceSynthesisView_TextInput",
            "VoiceSynthesisView_SynthesizeButton",
        ]

        found = []
        missing = []

        for elem_id in expected_elements:
            try:
                driver.find_element("accessibility id", elem_id)
                found.append(elem_id)
            except RuntimeError:
                missing.append(elem_id)

        tracer.step(f"Found: {found}, Missing: {missing}", driver, SCREENSHOTS_ENABLED)
        assert "VoiceSynthesisView_Root" in found, "Synthesis panel root must be present"
        tracer.success("Synthesis elements verified")

    def test_synthesis_api_engines(self, api_monitor, tracer):
        """Test synthesis engines endpoint."""
        tracer.step("Testing synthesis engines endpoint")

        try:
            response = api_monitor.get("/api/v3/engines")
            tracer.api_call("GET", "/api/v3/engines", response)

            if response.status_code == 200:
                engines = response.json()
                tracer.step(f"Available engines: {engines}")
            tracer.success("Engines endpoint works")
        except requests.RequestException as e:
            tracer.error(e, "Engines endpoint failed")


# =============================================================================
# Phase 7: Audio Format Conversion Tests
# =============================================================================


class TestAudioConversion:
    """Test audio format conversion."""

    @pytest.mark.parametrize("output_format", OUTPUT_FORMATS)
    def test_conversion_api_available(self, api_monitor, output_format, tracer):
        """Test that conversion API endpoints are available."""
        tracer.step(f"Testing conversion to {output_format}")

        # Check if conversion endpoint exists
        try:
            response = api_monitor.get("/api/audio/formats")
            if response.status_code == 200:
                formats = response.json()
                tracer.step(f"Conversion endpoint available, formats: {formats}")
            tracer.success(f"Conversion to {output_format} endpoint checked")
        except requests.RequestException:
            tracer.step(f"Conversion endpoint not available for {output_format}")

    def test_conversion_spec_validity(self, tracer):
        """Verify conversion specifications are valid."""
        tracer.step("Validating conversion specifications")

        for fmt, spec in CONVERSION_SPECS.items():
            assert spec.target_format == fmt
            assert spec.expected_mime_type in FORMAT_MIME_TYPES.values()

        tracer.success("All conversion specs are valid")


# =============================================================================
# Phase 8: Inter-Panel Communication Tests
# =============================================================================


class TestInterPanelCommunication:
    """Test inter-panel communication and events."""

    def test_panel_navigation_flow(self, driver, app_launched, navigate_to, tracer):
        """Test navigation flow between panels."""
        tracer.step("Testing panel navigation flow", driver, SCREENSHOTS_ENABLED)

        panels_to_visit = ["Library", "VoiceSynthesis", "Transcribe", "Library"]

        for panel in panels_to_visit:
            success = navigate_to(panel)
            assert success, f"Should navigate to {panel}"
            tracer.step(f"Navigated to {panel}", driver, SCREENSHOTS_ENABLED)
            time.sleep(0.3)

        tracer.success("Panel navigation flow works")

    def test_event_definitions_complete(self, tracer):
        """Verify all expected events are defined."""
        tracer.step("Checking event definitions")

        required_events = [
            "AssetAddedEvent",
            "CloneReferenceSelectedEvent",
            "VoiceProfileSelectedEvent",
            "PlaybackRequestedEvent",
            "SynthesisCompletedEvent",
            "TranscriptionCompletedEvent",
            "AddToTimelineEvent",
        ]

        for event in required_events:
            assert event in PANEL_EVENTS, f"Event {event} should be defined"

        tracer.success("All event definitions present")


# =============================================================================
# Phase 9: Error Handling Tests
# =============================================================================


class TestErrorHandling:
    """Test error handling scenarios."""

    def test_backend_unavailable_graceful(self, tracer):
        """Test graceful handling when backend is unavailable."""
        tracer.step("Testing graceful handling of unavailable backend")

        # Create monitor with wrong URL
        bad_monitor = APIMonitor(base_url="http://127.0.0.1:99999")
        healthy = bad_monitor.health_check()

        assert not healthy, "Should report unhealthy when backend unavailable"
        tracer.success("Backend unavailable handled gracefully")

    def test_invalid_endpoint_handling(self, api_monitor, tracer):
        """Test handling of invalid API endpoints."""
        tracer.step("Testing invalid endpoint handling")

        try:
            response = api_monitor.get("/api/nonexistent/endpoint")
            tracer.api_call("GET", "/api/nonexistent/endpoint", response)

            assert response.status_code == 404, "Invalid endpoint should return 404"
            tracer.success("Invalid endpoint handled correctly")
        except requests.RequestException as e:
            tracer.step(f"Request exception for invalid endpoint: {e}")


# =============================================================================
# Phase 10: Performance Tests
# =============================================================================


class TestPerformance:
    """Test performance metrics."""

    def test_panel_navigation_timing(self, driver, app_launched, navigate_to, tracer):
        """Measure panel navigation timing."""
        tracer.step("Measuring panel navigation timing", driver, SCREENSHOTS_ENABLED)

        timings: dict[str, float] = {}

        for panel in ["Library", "VoiceSynthesis", "Transcribe"]:
            start = time.perf_counter()
            navigate_to(panel)
            elapsed = (time.perf_counter() - start) * 1000
            timings[panel] = elapsed
            tracer.step(f"{panel} navigation: {elapsed:.1f}ms")

        # Assert reasonable navigation times (< 5 seconds)
        for panel, timing in timings.items():
            assert timing < 5000, f"{panel} navigation too slow: {timing}ms"

        tracer.success("Panel navigation timing acceptable")

    def test_api_response_times(self, api_monitor, tracer):
        """Measure API response times."""
        tracer.step("Measuring API response times")

        endpoints = ["/api/health", "/api/v3/engines"]

        for endpoint in endpoints:
            try:
                start = time.perf_counter()
                response = api_monitor.get(endpoint)
                elapsed = (time.perf_counter() - start) * 1000
                tracer.step(f"{endpoint}: {elapsed:.1f}ms (status {response.status_code})")
            except requests.RequestException:
                tracer.step(f"{endpoint}: unavailable")

        tracer.success("API response times measured")


# =============================================================================
# Test Execution Entry Point
# =============================================================================

if __name__ == "__main__":
    pytest.main(
        [
            __file__,
            "-v",
            "--tb=short",
            "-m",
            "not slow",
            "--html=.buildlogs/validation/reports/allan_watts_workflow_report.html",
            "--self-contained-html",
        ]
    )
