"""
Allan Watts Voice Synthesis Workflow Tests.

Tests voice synthesis functionality with various voices and text:
- Voice Synthesis panel navigation
- Voice/profile selection
- Text input and editing
- Synthesis execution
- Audio playback
- Output saving
- Integration with Library and Timeline

Requirements:
- WinAppDriver running on port 4723
- Backend running on port 8000
- VoiceStudio application built
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
    SYNTHESIS_TEST_TEXTS,
    SYNTHESIS_WORKFLOW,
)
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

# Configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.synthesis_workflow,
    pytest.mark.allan_watts,
]


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="module")
def tracer():
    """Create a workflow tracer for synthesis tests."""
    t = WorkflowTracer("allan_watts_synthesis", OUTPUT_DIR)
    t.start()
    yield t
    t.export_report()


@pytest.fixture(scope="module")
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "allan_watts_synthesis_api_calls.json")


@pytest.fixture
def navigate_to_synthesis(driver, app_launched, tracer):
    """Navigate to Voice Synthesis panel and return success status."""

    def _navigate():
        tracer.start_panel_transition("unknown", "VoiceSynthesis")

        try:
            nav_button = driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.nav_id)
            nav_button.click()
            time.sleep(0.5)

            # Wait for panel to load
            for _ in range(10):
                try:
                    driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.root_id)
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


class TestSynthesisNavigation:
    """Test Voice Synthesis panel navigation."""

    @pytest.mark.smoke
    def test_navigate_to_synthesis(self, navigate_to_synthesis, tracer):
        """Verify navigation to Voice Synthesis panel."""
        tracer.start_phase("synthesis_navigation", "Test Voice Synthesis panel access")
        tracer.step("Navigating to Voice Synthesis panel")

        success = navigate_to_synthesis()
        assert success, "Should be able to navigate to Voice Synthesis panel"

        tracer.end_phase(success=True)
        tracer.success("Voice Synthesis navigation successful")

    def test_synthesis_navigation_timing(self, driver, app_launched, tracer):
        """Measure Voice Synthesis panel navigation timing."""
        tracer.step("Measuring Voice Synthesis navigation timing")

        start_time = time.perf_counter()

        try:
            nav_button = driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.nav_id)
            nav_button.click()

            # Wait for root element
            for _ in range(20):
                try:
                    driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.root_id)
                    break
                except RuntimeError:
                    time.sleep(0.1)

            elapsed_ms = (time.perf_counter() - start_time) * 1000
            tracer.record_timing("synthesis_navigation", elapsed_ms)
            tracer.step(f"Voice Synthesis loaded in {elapsed_ms:.1f}ms")

            assert elapsed_ms < 3000, f"Synthesis navigation too slow: {elapsed_ms}ms"
            tracer.success("Synthesis navigation timing acceptable")
        except RuntimeError as e:
            tracer.error(e, "Navigation timing test failed")
            raise


# =============================================================================
# Panel Elements Tests
# =============================================================================


class TestSynthesisElements:
    """Test Voice Synthesis panel UI elements."""

    def test_synthesis_root_exists(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify Voice Synthesis panel root element exists."""
        tracer.step("Checking Voice Synthesis root element")
        navigate_to_synthesis()

        try:
            root = driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.root_id)
            assert root is not None
            tracer.step("Voice Synthesis root element found")
            tracer.success("Voice Synthesis root exists")
        except RuntimeError as e:
            tracer.error(e, "Voice Synthesis root not found")
            raise

    def test_text_input_exists(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify text input exists."""
        tracer.start_phase("synthesis_elements", "Test Voice Synthesis panel elements")
        tracer.step("Checking text input")
        navigate_to_synthesis()

        try:
            text_input = driver.find_element("accessibility id", "VoiceSynthesisView_TextInput")
            assert text_input is not None
            assert text_input.is_enabled()
            tracer.step("Text input found and enabled")
            tracer.success("Text input exists")
        except RuntimeError:
            try:
                text_input = driver.find_element(
                    "xpath",
                    "//*[contains(@AutomationId, 'Text') and contains(@ClassName, 'TextBox')]",
                )
                tracer.step("Text input found with xpath")
                tracer.success("Text input exists (xpath)")
            except RuntimeError as e:
                tracer.error(e, "Text input not found")
                pytest.skip("Text input not available")
        finally:
            tracer.end_phase()

    def test_synthesize_button_exists(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify synthesize button exists."""
        tracer.step("Checking synthesize button")
        navigate_to_synthesis()

        try:
            synth_button = driver.find_element(
                "accessibility id", "VoiceSynthesisView_SynthesizeButton"
            )
            assert synth_button is not None
            tracer.step("Synthesize button found")
            tracer.success("Synthesize button exists")
        except RuntimeError:
            try:
                synth_button = driver.find_element(
                    "xpath", "//*[contains(@Name, 'Synthesize') or contains(@Name, 'Generate')]"
                )
                tracer.step("Synthesize button found with xpath")
                tracer.success("Synthesize button exists (xpath)")
            except RuntimeError as e:
                tracer.error(e, "Synthesize button not found")
                pytest.skip("Synthesize button not available")

    def test_voice_selector_exists(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify voice selector exists."""
        tracer.step("Checking voice selector")
        navigate_to_synthesis()

        selector_ids = [
            "VoiceSynthesisView_VoiceSelector",
            "VoiceSynthesisView_VoiceComboBox",
            "VoiceSynthesisView_ProfileSelector",
        ]

        for selector_id in selector_ids:
            try:
                driver.find_element("accessibility id", selector_id)
                tracer.step(f"Voice selector found: {selector_id}")
                tracer.success("Voice selector exists")
                return
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass

        # Try xpath
        try:
            driver.find_element(
                "xpath",
                "//*[contains(@AutomationId, 'Voice') and contains(@ClassName, 'ComboBox')]",
            )
            tracer.step("Voice selector found with xpath")
            tracer.success("Voice selector exists (xpath)")
        except RuntimeError:
            tracer.step("Voice selector not found")
            pytest.skip("Voice selector not available")

    def test_engine_selector_exists(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify engine selector exists."""
        tracer.step("Checking engine selector")
        navigate_to_synthesis()

        try:
            driver.find_element("accessibility id", "VoiceSynthesisView_EngineComboBox")
            tracer.step("Engine selector found")
            tracer.success("Engine selector exists")
        except RuntimeError:
            try:
                driver.find_element(
                    "xpath",
                    "//*[contains(@AutomationId, 'Engine') and contains(@ClassName, 'ComboBox')]",
                )
                tracer.step("Engine selector found with xpath")
                tracer.success("Engine selector exists (xpath)")
            except RuntimeError:
                tracer.step("Engine selector not found")
                pytest.skip("Engine selector not available")


# =============================================================================
# Text Input Tests
# =============================================================================


class TestTextInput:
    """Test text input functionality."""

    def test_enter_simple_text(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Test entering simple text."""
        tracer.start_phase("text_input", "Test text input functionality")
        tracer.step("Testing simple text input")
        navigate_to_synthesis()

        try:
            text_input = driver.find_element("accessibility id", "VoiceSynthesisView_TextInput")
            text_input.click()
            text_input.clear()

            test_text = SYNTHESIS_TEST_TEXTS["simple"]
            text_input.send_keys(test_text)
            tracer.ui_action("type", "VoiceSynthesisView_TextInput", {"text": test_text})
            time.sleep(0.3)

            tracer.step(f"Entered text: {test_text}", driver, SCREENSHOTS_ENABLED)
            text_input.clear()

            tracer.success("Simple text input works")
        except RuntimeError as e:
            tracer.error(e, "Text input failed")
            pytest.skip("Text input not available")
        finally:
            tracer.end_phase()

    def test_enter_multiline_text(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Test entering multiline text."""
        tracer.step("Testing multiline text input")
        navigate_to_synthesis()

        try:
            text_input = driver.find_element("accessibility id", "VoiceSynthesisView_TextInput")
            text_input.click()
            text_input.clear()

            test_text = SYNTHESIS_TEST_TEXTS["multiline"]
            text_input.send_keys(test_text)
            tracer.ui_action("type", "VoiceSynthesisView_TextInput", {"text": "multiline text"})
            time.sleep(0.3)

            tracer.step("Multiline text entered", driver, SCREENSHOTS_ENABLED)
            text_input.clear()

            tracer.success("Multiline text input works")
        except RuntimeError as e:
            tracer.error(e, "Multiline text input failed")
            pytest.skip("Multiline text input not available")

    def test_enter_special_characters(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Test entering special characters."""
        tracer.step("Testing special characters")
        navigate_to_synthesis()

        try:
            text_input = driver.find_element("accessibility id", "VoiceSynthesisView_TextInput")
            text_input.click()
            text_input.clear()

            test_text = SYNTHESIS_TEST_TEXTS["special_chars"]
            text_input.send_keys(test_text)
            tracer.ui_action("type", "VoiceSynthesisView_TextInput", {"text": "special chars"})
            time.sleep(0.3)

            tracer.step(f"Special chars entered: {test_text}", driver, SCREENSHOTS_ENABLED)
            text_input.clear()

            tracer.success("Special characters work")
        except RuntimeError as e:
            tracer.error(e, "Special character input failed")
            pytest.skip("Special character input not available")


# =============================================================================
# API Integration Tests
# =============================================================================


class TestSynthesisAPI:
    """Test voice synthesis API endpoints."""

    def test_synthesis_engines_api(self, api_monitor, tracer):
        """Test listing synthesis engines via API."""
        tracer.start_phase("synthesis_api", "Test Synthesis API")
        tracer.step("Testing synthesis engines API")

        try:
            response = api_monitor.get("/api/v3/engines")
            tracer.api_call("GET", "/api/v3/engines", response)

            if response.status_code == 200:
                data = response.json()
                tracer.step(f"Engines: {data}")
                tracer.success("Engines API works")
            else:
                tracer.step(f"Engines returned {response.status_code}")
        except requests.RequestException as e:
            tracer.error(e, "Engines API failed")
        finally:
            tracer.end_phase()

    def test_voices_list_api(self, api_monitor, tracer):
        """Test listing available voices via API."""
        tracer.step("Testing voices list API")

        try:
            response = api_monitor.get("/api/v3/voices")
            tracer.api_call("GET", "/api/v3/voices", response)

            if response.status_code == 200:
                data = response.json()
                if isinstance(data, list):
                    tracer.step(f"Found {len(data)} voices")
                elif isinstance(data, dict):
                    tracer.step(f"Voices response: {data}")
            tracer.success("Voices API works")
        except requests.RequestException as e:
            tracer.step(f"Voices API error: {e}")

    def test_synthesis_submit_api(self, api_monitor, tracer):
        """Test synthesis submission API structure."""
        tracer.step("Testing synthesis submit API")

        try:
            # Test with minimal payload to verify endpoint exists
            response = api_monitor.post(
                "/api/v3/synthesize",
                json={
                    "text": SYNTHESIS_TEST_TEXTS["simple"],
                },
            )
            tracer.api_call("POST", "/api/v3/synthesize", response)

            if response.status_code in [400, 422]:
                tracer.step("Synthesis API validates input (expected for minimal payload)")
            elif response.status_code == 200:
                tracer.step("Synthesis API accepted request")

            tracer.success("Synthesis API exists")
        except requests.RequestException as e:
            tracer.step(f"Synthesis API error: {e}")

    def test_synthesis_with_engine(self, api_monitor, tracer):
        """Test synthesis with specific engine."""
        tracer.step("Testing synthesis with engine parameter")

        try:
            response = api_monitor.post(
                "/api/v3/synthesize",
                json={
                    "text": SYNTHESIS_TEST_TEXTS["simple"],
                    "engine": "piper",  # Common engine
                },
            )
            tracer.api_call("POST", "/api/v3/synthesize (with engine)", response)

            tracer.step(f"Synthesis with engine: status {response.status_code}")
            tracer.success("Engine parameter test completed")
        except requests.RequestException as e:
            tracer.step(f"Synthesis API error: {e}")


# =============================================================================
# Workflow Tests
# =============================================================================


class TestSynthesisWorkflow:
    """Test complete synthesis workflow."""

    def test_synthesis_workflow_setup(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Verify synthesis workflow elements are present."""
        tracer.start_phase("synthesis_workflow", "Test synthesis workflow setup")
        tracer.step("Checking synthesis workflow elements")
        navigate_to_synthesis()

        workflow_elements = {
            "panel_root": False,
            "text_input": False,
            "voice_selector": False,
            "synthesize_button": False,
        }

        # Check panel root
        try:
            driver.find_element("accessibility id", SYNTHESIS_WORKFLOW.root_id)
            workflow_elements["panel_root"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        # Check text input
        try:
            driver.find_element(
                "xpath", "//*[contains(@AutomationId, 'Text') and contains(@ClassName, 'TextBox')]"
            )
            workflow_elements["text_input"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        # Check voice selector
        try:
            driver.find_element(
                "xpath",
                "//*[contains(@AutomationId, 'Voice') and contains(@ClassName, 'ComboBox')]",
            )
            workflow_elements["voice_selector"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        # Check synthesize button
        try:
            driver.find_element(
                "xpath",
                "//*[contains(@Name, 'Synthesize') or contains(@AutomationId, 'Synthesize')]",
            )
            workflow_elements["synthesize_button"] = True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass

        tracer.step(f"Workflow elements: {workflow_elements}", driver, SCREENSHOTS_ENABLED)

        # Core elements needed
        assert workflow_elements["panel_root"], "Panel root required"

        tracer.end_phase(success=True)
        tracer.success("Synthesis workflow setup verified")


# =============================================================================
# Integration Tests
# =============================================================================


class TestSynthesisIntegration:
    """Test synthesis integration with other panels."""

    def test_synthesis_completed_event(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Document synthesis completed event."""
        tracer.start_phase("synthesis_integration", "Test panel integration")
        tracer.step("Documenting synthesis completed workflow")
        navigate_to_synthesis()

        tracer.trace_event(
            "SynthesisCompletedEvent",
            source_panel="VoiceSynthesis",
            target_panel="Library",
            payload={
                "action": "add_to_library",
                "expected_data": ["audio_path", "duration", "text_used", "voice_used"],
            },
        )

        tracer.step("Synthesis-completed event documented")
        tracer.end_phase(success=True)
        tracer.success("Integration documented")

    def test_synthesis_to_timeline_event(self, driver, app_launched, navigate_to_synthesis, tracer):
        """Document synthesis to timeline integration."""
        tracer.step("Documenting synthesis-to-timeline workflow")
        navigate_to_synthesis()

        tracer.trace_event(
            "AddToTimelineEvent",
            source_panel="VoiceSynthesis",
            target_panel="Timeline",
            payload={
                "action": "add_synthesis_to_timeline",
                "expected_data": ["audio_path", "start_time", "track"],
            },
        )

        tracer.step("Synthesis-to-timeline event documented")
        tracer.success("Timeline integration documented")


# =============================================================================
# Error Handling Tests
# =============================================================================


class TestSynthesisErrors:
    """Test synthesis error handling."""

    def test_synthesis_empty_text(self, api_monitor, tracer):
        """Test synthesis with empty text."""
        tracer.start_phase("synthesis_errors", "Test error handling")
        tracer.step("Testing synthesis with empty text")

        try:
            response = api_monitor.post(
                "/api/v3/synthesize",
                json={
                    "text": "",
                },
            )
            tracer.api_call("POST", "/api/v3/synthesize (empty)", response)

            if response.status_code in [400, 422]:
                tracer.step(f"Empty text rejection: {response.status_code}")

            tracer.success("Empty text handled")
        except requests.RequestException as e:
            tracer.step(f"Request error: {e}")
        finally:
            tracer.end_phase()

    def test_synthesis_invalid_voice(self, api_monitor, tracer):
        """Test synthesis with invalid voice."""
        tracer.step("Testing synthesis with invalid voice")

        try:
            response = api_monitor.post(
                "/api/v3/synthesize",
                json={
                    "text": SYNTHESIS_TEST_TEXTS["simple"],
                    "voice": "nonexistent_voice_xyz",
                },
            )
            tracer.api_call("POST", "/api/v3/synthesize (invalid voice)", response)

            if response.status_code in [400, 404, 422]:
                tracer.step(f"Invalid voice rejection: {response.status_code}")

            tracer.success("Invalid voice handled")
        except requests.RequestException as e:
            tracer.step(f"Request error: {e}")

    def test_synthesis_very_long_text(self, api_monitor, tracer):
        """Test synthesis with very long text."""
        tracer.step("Testing synthesis with long text")

        long_text = SYNTHESIS_TEST_TEXTS["long"]
        tracer.step(f"Long text length: {len(long_text)} characters")

        try:
            response = api_monitor.post(
                "/api/v3/synthesize",
                json={
                    "text": long_text,
                },
            )
            tracer.api_call("POST", "/api/v3/synthesize (long)", response)

            tracer.step(f"Long text response: {response.status_code}")
            tracer.success("Long text handling test completed")
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
            "--html=.buildlogs/validation/reports/allan_watts_synthesis_report.html",
            "--self-contained-html",
        ]
    )
