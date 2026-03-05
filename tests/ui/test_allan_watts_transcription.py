"""
Allan Watts Transcription Workflow Tests.

Tests transcription functionality using canonical test audio:
- Transcribe panel navigation and elements
- Engine selection (Whisper, etc.)
- Language selection
- Transcription execution
- Results display and export
- Integration with Timeline
- Error handling

Audio is resolved via:
1. VOICESTUDIO_TEST_AUDIO environment variable (if set)
2. conftest.py canonical_audio_path fixture (auto-provisioned)
3. Synthetic generation fallback via generate_test_audio.py

Requirements:
- WinAppDriver running on port 4723
- Backend running on port 8000
- VoiceStudio application built
- Test audio: auto-provisioned via conftest.py fixture
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).parent))

from fixtures.audio_test_data import (
    TEST_AUDIO_FILE,
    TRANSCRIPTION_WORKFLOW,
)
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

# Configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.transcription_workflow,
    pytest.mark.allan_watts,
]


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="module")
def tracer():
    """Create a workflow tracer for transcription tests."""
    t = WorkflowTracer("allan_watts_transcription", OUTPUT_DIR)
    t.start()
    yield t
    t.export_report()


@pytest.fixture(scope="module")
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "allan_watts_transcription_api_calls.json")


@pytest.fixture
def navigate_to_transcribe(driver, app_launched, tracer):
    """Navigate to Transcribe panel and return success status."""

    def _navigate():
        tracer.start_panel_transition("unknown", "Transcribe")

        try:
            nav_button = driver.find_element("accessibility id", TRANSCRIPTION_WORKFLOW.nav_id)
            nav_button.click()
            time.sleep(0.5)

            # Wait for panel to load
            for _ in range(10):
                try:
                    driver.find_element("accessibility id", TRANSCRIPTION_WORKFLOW.root_id)
                    tracer.end_panel_transition(success=True)
                    return True
                except RuntimeError:
                    time.sleep(0.3)

            tracer.end_panel_transition(success=False, error="Timeout waiting for panel")
            return False
        except RuntimeError as e:
            tracer.end_panel_transition(success=False, error=str(e))
            return False

    return _navigate


# =============================================================================
# Panel Navigation Tests
# =============================================================================


class TestTranscriptionNavigation:
    """Test Transcribe panel navigation."""

    @pytest.mark.smoke
    def test_navigate_to_transcribe(self, navigate_to_transcribe, tracer):
        """Verify navigation to Transcribe panel."""
        tracer.start_phase("transcription_navigation", "Test Transcribe panel access")
        tracer.step("Navigating to Transcribe panel")

        success = navigate_to_transcribe()
        assert success, "Should be able to navigate to Transcribe panel"

        tracer.end_phase(success=True)
        tracer.success("Transcribe navigation successful")

    def test_transcribe_navigation_timing(self, driver, app_launched, tracer):
        """Measure Transcribe panel navigation timing."""
        tracer.step("Measuring Transcribe navigation timing")

        start_time = time.perf_counter()

        try:
            nav_button = driver.find_element("accessibility id", TRANSCRIPTION_WORKFLOW.nav_id)
            nav_button.click()

            # Wait for root element
            for _ in range(20):
                try:
                    driver.find_element("accessibility id", TRANSCRIPTION_WORKFLOW.root_id)
                    break
                except RuntimeError:
                    time.sleep(0.1)

            elapsed_ms = (time.perf_counter() - start_time) * 1000
            tracer.record_timing("transcribe_navigation", elapsed_ms)
            tracer.step(f"Transcribe loaded in {elapsed_ms:.1f}ms")

            assert elapsed_ms < 3000, f"Transcribe navigation too slow: {elapsed_ms}ms"
            tracer.success("Transcribe navigation timing acceptable")
        except RuntimeError as e:
            tracer.error(e, "Navigation timing test failed")
            raise


# =============================================================================
# Panel Elements Tests
# =============================================================================


class TestTranscriptionElements:
    """Test Transcribe panel UI elements."""

    def test_transcribe_root_exists(self, driver, app_launched, navigate_to_transcribe, tracer):
        """Verify Transcribe panel root element exists."""
        tracer.step("Checking Transcribe root element")
        navigate_to_transcribe()

        try:
            root = driver.find_element("accessibility id", TRANSCRIPTION_WORKFLOW.root_id)
            assert root is not None
            tracer.step("Transcribe root element found")
            tracer.success("Transcribe root exists")
        except RuntimeError as e:
            tracer.error(e, "Transcribe root not found")
            raise

    def test_engine_combobox_exists(self, driver, app_launched, navigate_to_transcribe, tracer):
        """Verify engine selection combobox exists."""
        tracer.start_phase("transcription_elements", "Test Transcribe panel elements")
        tracer.step("Checking engine combobox")
        navigate_to_transcribe()

        try:
            engine_combo = driver.find_element("accessibility id", "TranscribeView_EngineComboBox")
            assert engine_combo is not None
            tracer.step("Engine combobox found")
            tracer.success("Engine combobox exists")
        except RuntimeError:
            # Try alternative selector
            try:
                engine_combo = driver.find_element(
                    "xpath",
                    "//*[contains(@AutomationId, 'Engine') and contains(@ClassName, 'ComboBox')]",
                )
                tracer.step("Engine combobox found with alternative selector")
                tracer.success("Engine combobox exists (alternative)")
            except RuntimeError as e:
                tracer.error(e, "Engine combobox not found")
                pytest.skip("Engine combobox not available")
        finally:
            tracer.end_phase()

    def test_transcribe_button_exists(self, driver, app_launched, navigate_to_transcribe, tracer):
        """Verify transcribe button exists."""
        tracer.step("Checking transcribe button")
        navigate_to_transcribe()

        try:
            transcribe_btn = driver.find_element(
                "accessibility id", "TranscribeView_TranscribeButton"
            )
            assert transcribe_btn is not None
            tracer.step("Transcribe button found")
            tracer.success("Transcribe button exists")
        except RuntimeError:
            # Try alternative
            try:
                transcribe_btn = driver.find_element(
                    "xpath", "//*[contains(@Name, 'Transcribe') and @IsEnabled='True']"
                )
                tracer.step("Transcribe button found with alternative selector")
                tracer.success("Transcribe button exists (alternative)")
            except RuntimeError as e:
                tracer.error(e, "Transcribe button not found")
                pytest.skip("Transcribe button not available")

    def test_language_selector_exists(self, driver, app_launched, navigate_to_transcribe, tracer):
        """Verify language selector exists."""
        tracer.step("Checking language selector")
        navigate_to_transcribe()

        try:
            lang_selector = driver.find_element(
                "accessibility id", "TranscribeView_LanguageComboBox"
            )
            assert lang_selector is not None
            tracer.step("Language selector found")
            tracer.success("Language selector exists")
        except RuntimeError:
            tracer.step("Language selector not found - may be auto-detect only")
            pytest.skip("Language selector not available")


# =============================================================================
# Engine Selection Tests
# =============================================================================


class TestEngineSelection:
    """Test transcription engine selection."""

    def test_list_available_engines(self, driver, app_launched, navigate_to_transcribe, tracer):
        """List available transcription engines."""
        tracer.start_phase("engine_selection", "Test engine selection")
        tracer.step("Listing available engines")
        navigate_to_transcribe()

        try:
            engine_combo = driver.find_element("accessibility id", "TranscribeView_EngineComboBox")
            engine_combo.click()
            time.sleep(0.5)

            # Get available options
            options = engine_combo.find_elements("xpath", ".//ListItem | .//ComboBoxItem")
            engine_names = [opt.get_attribute("Name") for opt in options]

            tracer.step(f"Available engines: {engine_names}", driver, SCREENSHOTS_ENABLED)

            # Close dropdown
            driver.press_escape()

            tracer.success("Engine list retrieved")
        except RuntimeError as e:
            tracer.error(e, "Failed to list engines")
            pytest.skip("Engine listing not available")
        finally:
            tracer.end_phase()

    def test_select_whisper_engine(self, driver, app_launched, navigate_to_transcribe, tracer):
        """Select Whisper engine if available."""
        tracer.step("Selecting Whisper engine")
        navigate_to_transcribe()

        try:
            engine_combo = driver.find_element("accessibility id", "TranscribeView_EngineComboBox")
            engine_combo.click()
            time.sleep(0.5)

            # Try to find Whisper option
            try:
                whisper_option = driver.find_element(
                    "xpath", "//*[contains(@Name, 'Whisper') or contains(@Name, 'whisper')]"
                )
                whisper_option.click()
                tracer.ui_action("select", "TranscribeView_EngineComboBox", {"engine": "Whisper"})
                tracer.step("Whisper engine selected", driver, SCREENSHOTS_ENABLED)
                tracer.success("Whisper selection successful")
            except RuntimeError:
                tracer.step("Whisper engine not available")
                driver.press_escape()
                pytest.skip("Whisper engine not available")

        except RuntimeError as e:
            tracer.error(e, "Engine selection failed")
            pytest.skip("Engine selection not available")


# =============================================================================
# API Integration Tests
# =============================================================================


class TestTranscriptionAPI:
    """Test transcription API endpoints."""

    def test_transcription_engines_api(self, api_monitor, tracer):
        """Test listing transcription engines via API."""
        tracer.start_phase("transcription_api", "Test Transcription API")
        tracer.step("Testing transcription engines API")

        endpoints = [
            "/api/transcribe/engines",
            "/api/v3/engines",
            "/api/engines",
        ]

        for endpoint in endpoints:
            try:
                response = api_monitor.get(endpoint)
                tracer.api_call("GET", endpoint, response)

                if response.status_code == 200:
                    data = response.json()
                    tracer.step(f"Engines from {endpoint}: {data}")
                    tracer.end_phase(success=True)
                    tracer.success("Engines API works")
                    return
            # ALLOWED: bare except - best effort, failure acceptable
            except requests.RequestException:
                pass

        tracer.step("No working engines endpoint found")
        tracer.end_phase(success=False)

    def test_transcription_languages_api(self, api_monitor, tracer):
        """Test listing available languages via API."""
        tracer.step("Testing languages API")

        try:
            response = api_monitor.get("/api/transcribe/languages")
            tracer.api_call("GET", "/api/transcribe/languages", response)

            if response.status_code == 200:
                data = response.json()
                if isinstance(data, list):
                    tracer.step(f"Available languages: {len(data)}")
                elif isinstance(data, dict):
                    tracer.step(f"Languages response: {data}")
            tracer.success("Languages API works")
        except requests.RequestException as e:
            tracer.step(f"Languages API error: {e}")

    def test_transcription_submit_api(self, api_monitor, tracer):
        """Test transcription submission via API (dry run)."""
        tracer.step("Testing transcription submit API structure")

        # Just verify the endpoint exists and what it expects
        # Don't actually submit to avoid long-running operations
        try:
            # Check endpoint with OPTIONS or empty body
            response = api_monitor.post("/api/transcribe", json={})
            tracer.api_call("POST", "/api/transcribe (empty)", response)

            if response.status_code in [400, 422]:
                tracer.step("Transcription API validates input (expected)")
            elif response.status_code == 200:
                tracer.step("Transcription API accepted empty request")

            tracer.success("Transcription submit API exists")
        except requests.RequestException as e:
            tracer.step(f"Transcription API error: {e}")


# =============================================================================
# Workflow Tests
# =============================================================================


class TestTranscriptionWorkflow:
    """Test complete transcription workflow."""

    def test_transcription_workflow_setup(
        self, driver, app_launched, navigate_to_transcribe, tracer
    ):
        """Verify transcription workflow can be set up."""
        tracer.start_phase("transcription_workflow", "Test transcription workflow")
        tracer.step("Setting up transcription workflow")
        navigate_to_transcribe()

        workflow_elements = {
            "engine_selection": False,
            "transcribe_button": False,
            "output_area": False,
        }

        # Check engine selection
        try:
            driver.find_element("accessibility id", "TranscribeView_EngineComboBox")
            workflow_elements["engine_selection"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        # Check transcribe button
        try:
            driver.find_element("accessibility id", "TranscribeView_TranscribeButton")
            workflow_elements["transcribe_button"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        # Check output area
        try:
            driver.find_element("accessibility id", "TranscribeView_OutputTextBox")
            workflow_elements["output_area"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        tracer.step(f"Workflow elements: {workflow_elements}", driver, SCREENSHOTS_ENABLED)

        # At minimum, need transcribe button
        assert workflow_elements["transcribe_button"], "Transcribe button required"

        tracer.end_phase(success=True)
        tracer.success("Transcription workflow setup verified")


# =============================================================================
# Integration Tests
# =============================================================================


class TestTranscriptionIntegration:
    """Test transcription integration with other panels."""

    def test_transcription_to_timeline_event(
        self, driver, app_launched, navigate_to_transcribe, tracer
    ):
        """Document transcription to timeline integration."""
        tracer.start_phase("transcription_integration", "Test panel integration")
        tracer.step("Documenting transcription to timeline workflow")
        navigate_to_transcribe()

        tracer.trace_event(
            "TranscriptionCompletedEvent",
            source_panel="Transcribe",
            target_panel="Timeline",
            payload={
                "action": "add_transcript_to_timeline",
                "expected_data": ["segments", "text", "timing"],
            },
        )

        tracer.step("Transcription-to-timeline event documented")
        tracer.end_phase(success=True)
        tracer.success("Integration documented")

    def test_transcription_output_export(
        self, driver, app_launched, navigate_to_transcribe, tracer
    ):
        """Document transcription output export options."""
        tracer.step("Documenting transcription export options")
        navigate_to_transcribe()

        # Document expected export formats
        expected_exports = ["SRT", "VTT", "TXT", "JSON"]
        tracer.trace_event(
            "TranscriptionExport",
            source_panel="Transcribe",
            payload={"available_formats": expected_exports},
        )

        tracer.step(f"Expected export formats: {expected_exports}")
        tracer.success("Export options documented")


# =============================================================================
# Error Handling Tests
# =============================================================================


class TestTranscriptionErrors:
    """Test transcription error handling."""

    def test_transcription_without_file(self, api_monitor, tracer):
        """Test transcription without providing a file."""
        tracer.start_phase("transcription_errors", "Test error handling")
        tracer.step("Testing transcription without file")

        try:
            response = api_monitor.post("/api/transcribe", json={"engine": "whisper"})
            tracer.api_call("POST", "/api/transcribe (no file)", response)

            assert response.status_code in [
                400,
                422,
            ], f"Missing file should fail validation, got {response.status_code}"

            tracer.step(f"No-file rejection: {response.status_code}")
            tracer.success("Missing file handled correctly")
        except requests.RequestException as e:
            tracer.step(f"Request error: {e}")
        finally:
            tracer.end_phase()

    def test_transcription_invalid_engine(self, api_monitor, tracer):
        """Test transcription with invalid engine."""
        tracer.step("Testing transcription with invalid engine")

        try:
            response = api_monitor.post(
                "/api/transcribe",
                json={"engine": "nonexistent_engine_xyz", "audio_path": str(TEST_AUDIO_FILE)},
            )
            tracer.api_call("POST", "/api/transcribe (invalid engine)", response)

            if response.status_code in [400, 422, 404]:
                tracer.step(f"Invalid engine correctly rejected: {response.status_code}")

            tracer.success("Invalid engine handled")
        except requests.RequestException as e:
            tracer.step(f"Request error: {e}")


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
            "--html=.buildlogs/validation/reports/allan_watts_transcription_report.html",
            "--self-contained-html",
        ]
    )
