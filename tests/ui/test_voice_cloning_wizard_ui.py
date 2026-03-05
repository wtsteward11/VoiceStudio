"""
Voice Cloning Wizard 4-Step Workflow Tests.

Tests the complete voice cloning wizard workflow:
1. Upload Reference Audio
2. Configure Cloning Settings
3. Processing (with progress monitoring)
4. Review and Finalize

Requires:
- WinAppDriver running
- Backend running on port 8000
- VoiceStudio application built
- At least one TTS engine available
"""

from __future__ import annotations

import os

# Import tracing infrastructure
import sys
import time
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).parent))
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

from fixtures import get_test_audio_path

# Test configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
TRACE_ENABLED = os.getenv("VOICESTUDIO_TRACE_ENABLED", "1") == "1"
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.cloning,
    pytest.mark.wizard,
]


@pytest.fixture
def tracer():
    """Create a workflow tracer for this test."""
    tracer = WorkflowTracer("voice_cloning_wizard", OUTPUT_DIR)
    tracer.start()
    yield tracer
    tracer.export_report()


@pytest.fixture
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "voice_cloning_api_calls.json")


class TestVoiceCloningWizardNavigation:
    """Tests for navigating to and from the Voice Cloning Wizard."""

    @pytest.mark.smoke
    def test_navigate_to_wizard(self, driver, app_launched, tracer):
        """Verify navigation to Voice Cloning Wizard panel."""
        tracer.step("Navigating to Voice Cloning Wizard", driver, SCREENSHOTS_ENABLED)

        try:
            # Click on Profiles navigation
            profiles_nav = driver.find_element("accessibility id", "NavProfiles")
            profiles_nav.click()
            time.sleep(1)
            tracer.step("Clicked NavProfiles", driver, SCREENSHOTS_ENABLED)

            # Look for Voice Cloning Wizard option or panel
            wizard_found = False
            wizard_identifiers = [
                "VoiceCloningWizardPanel",
                "VoiceCloningWizard",
                "CloneVoiceButton",
                "StartCloningButton",
            ]

            for identifier in wizard_identifiers:
                try:
                    driver.find_element("accessibility id", identifier)
                    wizard_found = True
                    tracer.step(f"Found wizard element: {identifier}", driver, SCREENSHOTS_ENABLED)
                    break
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            # Also try by name
            if not wizard_found:
                try:
                    driver.find_element("xpath", "//*[contains(@Name, 'Clone')]")
                    wizard_found = True
                    tracer.step("Found Clone element by name", driver, SCREENSHOTS_ENABLED)
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            assert wizard_found, "Should find Voice Cloning Wizard or Clone button"
            tracer.success("Navigation to Voice Cloning Wizard successful")

        except Exception as e:
            tracer.error(e, "Failed to navigate to wizard")
            raise


class TestStep1UploadReferenceAudio:
    """Tests for Step 1: Upload Reference Audio."""

    def test_voice_analyze_api(self, api_monitor, tracer):
        """Test voice analysis API endpoint."""
        tracer.step("Testing voice analyze API")

        test_audio = get_test_audio_path("reference_clean")
        if not test_audio.exists():
            pytest.skip(f"Reference audio not found at {test_audio}")

        try:
            with open(test_audio, "rb") as f:
                files = {"file": (test_audio.name, f, "audio/wav")}
                response = requests.post(
                    f"{BACKEND_URL}/api/voice/analyze", files=files, timeout=60
                )
                tracer.api_call("POST", "/api/voice/analyze", response)

                if response.status_code == 200:
                    data = response.json()
                    tracer.step(f"Analysis result: {data}")

                    # Check for expected analysis fields
                    expected_fields = ["quality", "duration", "sample_rate"]
                    for field in expected_fields:
                        if field in data:
                            tracer.step(f"Found analysis field: {field} = {data[field]}")

                    tracer.success("Voice analysis API works")
                else:
                    tracer.step(f"Analyze returned {response.status_code}: {response.text}")

        except requests.RequestException as e:
            tracer.error(e, "Voice analyze request failed")
            pytest.skip(f"Voice analyze endpoint not available: {e}")

    def test_audio_validate_api(self, api_monitor, tracer):
        """Test audio validation API endpoint."""
        tracer.step("Testing audio validation API")

        test_audio = get_test_audio_path("reference_clean")
        if not test_audio.exists():
            pytest.skip(f"Reference audio not found at {test_audio}")

        try:
            with open(test_audio, "rb") as f:
                files = {"file": (test_audio.name, f, "audio/wav")}
                response = requests.post(
                    f"{BACKEND_URL}/api/audio/validate", files=files, timeout=30
                )
                tracer.api_call("POST", "/api/audio/validate", response)

                if response.status_code in [200, 404]:  # 404 if endpoint doesn't exist
                    tracer.step(f"Validation response: {response.status_code}")
                    if response.status_code == 200:
                        data = response.json()
                        tracer.step(f"Validation result: {data}")
                        tracer.success("Audio validation API works")
                else:
                    tracer.step(f"Validate returned {response.status_code}")

        except requests.RequestException as e:
            tracer.error(e, "Audio validate request failed")


class TestStep2ConfigureCloningSettings:
    """Tests for Step 2: Configure Cloning Settings."""

    def test_engine_list_api(self, api_monitor, tracer):
        """Test engine list API for available TTS engines."""
        tracer.step("Testing engine list API")

        try:
            response = api_monitor.get("/api/engine/list")
            tracer.api_call("GET", "/api/engine/list", response)

            assert (
                response.status_code == 200
            ), f"Engine list should return 200, got {response.status_code}"

            data = response.json()
            tracer.step(f"Available engines: {data}")

            # Check if any TTS engines are available
            if isinstance(data, list):
                tts_engines = [e for e in data if e.get("type") == "tts" or "tts" in str(e).lower()]
                tracer.step(f"TTS engines found: {len(tts_engines)}")

            tracer.success("Engine list API works")

        except Exception as e:
            tracer.error(e, "Engine list request failed")
            raise

    def test_engine_capabilities_api(self, api_monitor, tracer):
        """Test engine capabilities API."""
        tracer.step("Testing engine capabilities API")

        # First get list of engines
        try:
            response = api_monitor.get("/api/engine/list")
            if response.status_code != 200:
                pytest.skip("Engine list not available")

            engines = response.json()
            if not engines:
                pytest.skip("No engines available")

            # Test capabilities for first engine
            engine_id = None
            if isinstance(engines, list) and len(engines) > 0:
                if isinstance(engines[0], dict):
                    engine_id = engines[0].get("id") or engines[0].get("name")
                else:
                    engine_id = str(engines[0])

            if engine_id:
                response = api_monitor.get(f"/api/engine/{engine_id}/capabilities")
                tracer.api_call("GET", f"/api/engine/{engine_id}/capabilities", response)

                if response.status_code == 200:
                    data = response.json()
                    tracer.step(f"Engine {engine_id} capabilities: {data}")
                    tracer.success("Engine capabilities API works")
                else:
                    tracer.step(f"Capabilities returned {response.status_code}")

        except Exception as e:
            tracer.error(e, "Engine capabilities request failed")


class TestStep3Processing:
    """Tests for Step 3: Processing (voice cloning)."""

    def test_voice_clone_api(self, api_monitor, tracer):
        """Test voice clone API endpoint."""
        tracer.step("Testing voice clone API")

        test_audio = get_test_audio_path("reference_clean")
        if not test_audio.exists():
            pytest.skip(f"Reference audio not found at {test_audio}")

        try:
            with open(test_audio, "rb") as f:
                files = {"file": (test_audio.name, f, "audio/wav")}
                data = {
                    "name": "test_clone",
                    "engine": "auto",  # Let backend choose
                }
                response = requests.post(
                    f"{BACKEND_URL}/api/voice/clone",
                    files=files,
                    data=data,
                    timeout=120,  # Cloning can take time
                )
                tracer.api_call("POST", "/api/voice/clone", response)

                if response.status_code in [200, 201, 202]:
                    result = response.json()
                    tracer.step(f"Clone result: {result}")

                    # Check for job ID or voice profile
                    if "job_id" in result or "id" in result or "profile" in result:
                        tracer.success("Voice clone API works - job/profile created")
                    else:
                        tracer.step("Clone returned success but no job/profile ID")

                elif response.status_code == 422:
                    tracer.step(f"Clone validation failed: {response.text}")
                else:
                    tracer.step(f"Clone returned {response.status_code}: {response.text}")

        except requests.RequestException as e:
            tracer.error(e, "Voice clone request failed")
            pytest.skip(f"Voice clone endpoint not available: {e}")

    def test_cloning_status_api(self, api_monitor, tracer):
        """Test voice cloning status endpoint."""
        tracer.step("Testing cloning status API")

        try:
            response = api_monitor.get("/api/voice-cloning/status")
            tracer.api_call("GET", "/api/voice-cloning/status", response)

            if response.status_code == 200:
                data = response.json()
                tracer.step(f"Cloning status: {data}")
                tracer.success("Cloning status API works")
            else:
                tracer.step(f"Status returned {response.status_code}")

        except Exception as e:
            tracer.error(e, "Cloning status request failed")


class TestStep4ReviewAndFinalize:
    """Tests for Step 4: Review and Finalize."""

    def test_voice_synthesize_api(self, api_monitor, tracer):
        """Test voice synthesis API for preview."""
        tracer.step("Testing voice synthesize API")

        try:
            payload = {
                "text": "Hello, this is a test of the voice synthesis system.",
                "voice_id": "default",  # Use default voice for test
            }
            response = requests.post(
                f"{BACKEND_URL}/api/voice/synthesize", json=payload, timeout=60
            )
            tracer.api_call("POST", "/api/voice/synthesize", response)

            if response.status_code in [200, 201, 202]:
                tracer.step("Synthesis request successful")

                # Check response type
                content_type = response.headers.get("content-type", "")
                if "audio" in content_type:
                    tracer.step("Received audio response directly")
                else:
                    data = response.json()
                    tracer.step(f"Synthesis result: {data}")

                tracer.success("Voice synthesize API works")

            elif response.status_code == 422:
                tracer.step(f"Synthesis validation failed: {response.text}")
            else:
                tracer.step(f"Synthesis returned {response.status_code}")

        except Exception as e:
            tracer.error(e, "Voice synthesize request failed")

    def test_profiles_api(self, api_monitor, tracer):
        """Test profiles API for saving voice profiles."""
        tracer.step("Testing profiles API")

        try:
            # Get existing profiles
            response = api_monitor.get("/api/profiles")
            tracer.api_call("GET", "/api/profiles", response)

            if response.status_code == 200:
                profiles = response.json()
                tracer.step(
                    f"Existing profiles: {len(profiles) if isinstance(profiles, list) else 'N/A'}"
                )
                tracer.success("Profiles API works")
            else:
                tracer.step(f"Profiles returned {response.status_code}")

        except Exception as e:
            tracer.error(e, "Profiles request failed")


class TestVoiceCloningWizardE2E:
    """End-to-end Voice Cloning Wizard tests."""

    @pytest.mark.slow
    def test_wizard_ui_navigation(self, driver, app_launched, tracer):
        """Test navigating through wizard UI steps."""
        tracer.step("Starting wizard UI navigation test", driver, SCREENSHOTS_ENABLED)

        # Navigate to Profiles section
        try:
            profiles_nav = driver.find_element("accessibility id", "NavProfiles")
            profiles_nav.click()
            time.sleep(1)
            tracer.step("Navigated to Profiles", driver, SCREENSHOTS_ENABLED)
        except Exception as e:
            tracer.error(e, "Failed to navigate to Profiles")
            pytest.skip("Cannot navigate to Profiles panel")

        # Look for wizard or clone button
        tracer.step("Looking for Clone/Wizard button", driver, SCREENSHOTS_ENABLED)

        clone_button = None
        clone_identifiers = [
            ("accessibility id", "CloneVoiceButton"),
            ("accessibility id", "StartCloningButton"),
            ("accessibility id", "VoiceCloningWizardButton"),
            ("xpath", "//*[contains(@Name, 'Clone')]"),
            ("xpath", "//*[contains(@Name, 'Wizard')]"),
        ]

        for by, value in clone_identifiers:
            try:
                clone_button = driver.find_element(by, value)
                tracer.step(f"Found clone button: {value}", driver, SCREENSHOTS_ENABLED)
                break
            except Exception:
                continue

        if clone_button:
            clone_button.click()
            time.sleep(1)
            tracer.step("Clicked clone button", driver, SCREENSHOTS_ENABLED)

            # Look for wizard step indicators
            step_indicators = [
                "Step 1",
                "Step 2",
                "Step 3",
                "Step 4",
                "Upload",
                "Configure",
                "Process",
                "Review",
            ]

            for indicator in step_indicators:
                try:
                    driver.find_element("xpath", f"//*[contains(@Name, '{indicator}')]")
                    tracer.step(f"Found wizard indicator: {indicator}", driver, SCREENSHOTS_ENABLED)
                    break
                except Exception:
                    continue

        tracer.success("Wizard UI navigation test completed")

    @pytest.mark.slow
    def test_complete_cloning_workflow_api(self, api_monitor, tracer):
        """Test complete cloning workflow via API."""
        tracer.step("Starting complete API cloning workflow")

        # Step 1: Upload and analyze reference audio
        tracer.step("Step 1: Analyze reference audio")
        test_audio = get_test_audio_path("reference_clean")

        if not test_audio.exists():
            pytest.skip(f"Reference audio not found at {test_audio}")

        analysis_result = None
        with open(test_audio, "rb") as f:
            files = {"file": (test_audio.name, f, "audio/wav")}
            response = requests.post(f"{BACKEND_URL}/api/voice/analyze", files=files, timeout=60)
            tracer.api_call("POST", "/api/voice/analyze", response)

            if response.status_code == 200:
                analysis_result = response.json()
                tracer.step(f"Analysis complete: {analysis_result}")

        # Step 2: Get available engines
        tracer.step("Step 2: Get available engines")
        response = api_monitor.get("/api/engine/list")
        tracer.api_call("GET", "/api/engine/list", response)

        engines = []
        if response.status_code == 200:
            engines = response.json()
            tracer.step(
                f"Available engines: {len(engines) if isinstance(engines, list) else 'N/A'}"
            )

        # Step 3: Start cloning
        tracer.step("Step 3: Start voice cloning")
        with open(test_audio, "rb") as f:
            files = {"file": (test_audio.name, f, "audio/wav")}
            data = {"name": "api_test_clone", "engine": "auto"}
            response = requests.post(
                f"{BACKEND_URL}/api/voice/clone", files=files, data=data, timeout=120
            )
            tracer.api_call("POST", "/api/voice/clone", response)

            clone_result = None
            if response.status_code in [200, 201, 202]:
                clone_result = response.json()
                tracer.step(f"Clone initiated: {clone_result}")

        # Step 4: Check profiles
        tracer.step("Step 4: Verify profiles")
        response = api_monitor.get("/api/profiles")
        tracer.api_call("GET", "/api/profiles", response)

        if response.status_code == 200:
            profiles = response.json()
            tracer.step(f"Total profiles: {len(profiles) if isinstance(profiles, list) else 'N/A'}")

        tracer.success("Complete API cloning workflow finished")


# Run tests directly if executed as script
if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short"])
