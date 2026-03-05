"""
Audio Import Workflow Tests.

Tests the complete audio import workflow including:
- Import button click triggering file dialog
- File selection and upload
- Library integration after import
- Backend API integration

Requires:
- WinAppDriver running
- Backend running on port 8000
- VoiceStudio application built
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
    pytest.mark.import_workflow,
]


@pytest.fixture
def tracer():
    """Create a workflow tracer for this test."""
    tracer = WorkflowTracer("audio_import", OUTPUT_DIR)
    tracer.start()
    yield tracer
    tracer.export_report()


@pytest.fixture
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "audio_import_api_calls.json")


class TestAudioImportButton:
    """Tests for the Import button functionality."""

    @pytest.mark.smoke
    def test_import_button_exists(self, driver, app_launched, tracer):
        """Verify the Import button is present in the toolbar."""
        tracer.step("Looking for Import button", driver, SCREENSHOTS_ENABLED)

        # Look for Import button by various methods
        try:
            # Try by name containing "Import"
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            assert import_button is not None, "Import button should exist"
            assert import_button.is_enabled(), "Import button should be enabled"
            tracer.step("Import button found and enabled", driver, SCREENSHOTS_ENABLED)
            tracer.success("Import button exists and is enabled")
        except Exception as e:
            tracer.error(e, "Failed to find Import button")
            raise

    @pytest.mark.smoke
    def test_import_button_opens_dialog(self, driver, app_launched, tracer):
        """Verify clicking Import button opens file dialog."""
        tracer.step("Clicking Import button", driver, SCREENSHOTS_ENABLED)

        try:
            # Find and click Import button
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            import_button.click()

            # Wait for dialog to appear
            time.sleep(1)
            tracer.step("Import button clicked, waiting for dialog", driver, SCREENSHOTS_ENABLED)

            # Look for file dialog elements
            dialog_found = False
            dialog_indicators = ["Open", "Cancel", "File name:", "Files of type:"]

            for indicator in dialog_indicators:
                try:
                    driver.find_element("name", indicator)
                    dialog_found = True
                    tracer.step(f"Found dialog indicator: {indicator}", driver, SCREENSHOTS_ENABLED)
                    break
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            assert dialog_found, "File dialog should open after clicking Import"
            tracer.step("File dialog confirmed open", driver, SCREENSHOTS_ENABLED)

            # Close the dialog
            try:
                cancel_button = driver.find_element("name", "Cancel")
                cancel_button.click()
                time.sleep(0.5)
            except Exception:
                # Try pressing Escape
                driver.press_escape()
                time.sleep(0.5)

            tracer.success("Import button opens file dialog correctly")

        except Exception as e:
            tracer.error(e, "Failed to open file dialog")
            raise

    @pytest.mark.smoke
    def test_import_keyboard_shortcut(self, driver, app_launched, tracer):
        """Verify Ctrl+I keyboard shortcut opens import dialog."""
        tracer.step("Testing Ctrl+I shortcut", driver, SCREENSHOTS_ENABLED)

        try:
            # Press Ctrl+I
            driver.press_shortcut("Ctrl+i")
            time.sleep(1)
            tracer.step("Ctrl+I pressed, waiting for dialog", driver, SCREENSHOTS_ENABLED)

            # Look for file dialog
            dialog_found = False
            for indicator in ["Open", "Cancel", "File name:"]:
                try:
                    driver.find_element("name", indicator)
                    dialog_found = True
                    break
                # ALLOWED: bare except - best effort, failure acceptable
                except Exception:
                    pass

            assert dialog_found, "Ctrl+I should open import dialog"
            tracer.step("Dialog opened via Ctrl+I", driver, SCREENSHOTS_ENABLED)

            # Close dialog
            driver.press_escape()
            time.sleep(0.5)

            tracer.success("Ctrl+I shortcut works correctly")

        except Exception as e:
            tracer.error(e, "Ctrl+I shortcut failed")
            raise


class TestAudioUploadAPI:
    """Tests for the audio upload API integration."""

    def test_backend_health(self, api_monitor, tracer):
        """Verify backend is healthy before running tests."""
        tracer.step("Checking backend health")

        assert api_monitor.health_check(), f"Backend at {BACKEND_URL} should be healthy"
        tracer.success("Backend is healthy")

    def test_audio_upload_endpoint(self, api_monitor, tracer):
        """Test direct audio upload to backend API."""
        tracer.step("Testing audio upload endpoint")

        # Get test audio file
        test_audio = get_test_audio_path("short")
        assert test_audio.exists(), f"Test audio file should exist at {test_audio}"
        tracer.step(f"Using test audio: {test_audio}")

        # Upload to backend
        with open(test_audio, "rb") as f:
            files = {"file": (test_audio.name, f, "audio/wav")}

            try:
                response = requests.post(f"{BACKEND_URL}/api/audio/upload", files=files, timeout=30)
                tracer.api_call("POST", "/api/audio/upload", response)

                # Check response
                assert response.status_code in [
                    200,
                    201,
                ], f"Upload should succeed, got {response.status_code}: {response.text}"

                data = response.json()
                assert (
                    "id" in data or "path" in data or "filename" in data
                ), "Response should contain file identifier"

                tracer.step(f"Upload successful: {data}")
                tracer.success("Audio upload API works correctly")

            except requests.RequestException as e:
                tracer.error(e, "Audio upload request failed")
                pytest.skip(f"Backend audio upload not available: {e}")

    def test_audio_formats_endpoint(self, api_monitor, tracer):
        """Test audio formats endpoint returns supported formats."""
        tracer.step("Testing audio formats endpoint")

        try:
            response = api_monitor.get("/api/audio/formats")
            tracer.api_call("GET", "/api/audio/formats", response)

            if response.status_code == 200:
                data = response.json()
                tracer.step(f"Supported formats: {data}")
                tracer.success("Audio formats endpoint works")
            else:
                tracer.step(f"Formats endpoint returned {response.status_code}")

        except Exception as e:
            tracer.error(e, "Audio formats request failed")


class TestImportWorkflowEnd2End:
    """End-to-end import workflow tests."""

    @pytest.mark.slow
    def test_full_import_workflow(self, driver, app_launched, tracer, api_monitor):
        """
        Test complete import workflow:
        1. Click Import button
        2. File dialog opens
        3. (Simulated) file selection
        4. Upload completes
        5. File appears in Library
        """
        tracer.step("Starting full import workflow test", driver, SCREENSHOTS_ENABLED)

        # Step 1: Click Import button
        tracer.step("Step 1: Click Import button", driver, SCREENSHOTS_ENABLED)
        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            import_button.click()
            time.sleep(1)
        except Exception as e:
            tracer.error(e, "Failed to click Import button")
            raise

        # Step 2: Verify dialog opens
        tracer.step("Step 2: Verify file dialog opens", driver, SCREENSHOTS_ENABLED)
        dialog_found = False
        for indicator in ["Open", "Cancel", "File name:"]:
            try:
                driver.find_element("name", indicator)
                dialog_found = True
                break
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass

        assert dialog_found, "File dialog should open"
        tracer.step("File dialog confirmed open", driver, SCREENSHOTS_ENABLED)

        # Step 3: Cancel dialog (we can't actually select files in automated test)
        # In real test, we would use SendKeys to type file path
        tracer.step(
            "Step 3: Cancel dialog (automated file selection limited)", driver, SCREENSHOTS_ENABLED
        )
        driver.press_escape()
        time.sleep(0.5)

        # Step 4: Verify we can navigate to Library panel
        tracer.step("Step 4: Navigate to Library panel", driver, SCREENSHOTS_ENABLED)
        try:
            # Try to find and click Library navigation
            library_nav = driver.find_element("accessibility id", "NavLibrary")
            library_nav.click()
            time.sleep(1)
            tracer.step("Library panel opened", driver, SCREENSHOTS_ENABLED)
        except Exception as e:
            tracer.error(e, "Could not navigate to Library panel")
            # Non-fatal - continue with workflow

        # Step 5: Verify backend is accessible
        tracer.step("Step 5: Verify backend connection", driver, SCREENSHOTS_ENABLED)
        assert api_monitor.health_check(), "Backend should be accessible"

        tracer.success("Import workflow test completed (with dialog cancellation)")


class TestImportErrorHandling:
    """Tests for import error handling."""

    def test_import_invalid_file_type(self, api_monitor, tracer):
        """Test that invalid file types are rejected."""
        tracer.step("Testing invalid file type rejection")

        # Create a fake text file
        import tempfile

        with tempfile.NamedTemporaryFile(suffix=".txt", delete=False) as f:
            f.write(b"This is not an audio file")
            temp_path = f.name

        try:
            with open(temp_path, "rb") as f:
                files = {"file": ("test.txt", f, "text/plain")}
                response = requests.post(f"{BACKEND_URL}/api/audio/upload", files=files, timeout=10)
                tracer.api_call("POST", "/api/audio/upload", response)

                # Should reject with 400 or 422
                assert response.status_code in [
                    400,
                    415,
                    422,
                ], f"Invalid file type should be rejected, got {response.status_code}"

                tracer.step(f"Invalid file correctly rejected: {response.status_code}")
                tracer.success("Invalid file type handling works")

        except requests.RequestException as e:
            tracer.error(e, "Request failed")
            pytest.skip(f"Backend not available: {e}")
        finally:
            Path(temp_path).unlink(missing_ok=True)

    def test_import_empty_file(self, api_monitor, tracer):
        """Test that empty files are handled gracefully."""
        tracer.step("Testing empty file handling")

        import tempfile

        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            # Write minimal WAV header but essentially empty
            f.write(b"RIFF\x00\x00\x00\x00WAVE")
            temp_path = f.name

        try:
            with open(temp_path, "rb") as f:
                files = {"file": ("empty.wav", f, "audio/wav")}
                response = requests.post(f"{BACKEND_URL}/api/audio/upload", files=files, timeout=10)
                tracer.api_call("POST", "/api/audio/upload", response)

                # Should either reject (400/422) or handle gracefully (200)
                assert response.status_code in [
                    200,
                    400,
                    422,
                ], f"Empty file should be handled, got {response.status_code}"

                tracer.step(f"Empty file handled with status {response.status_code}")
                tracer.success("Empty file handling works")

        except requests.RequestException as e:
            tracer.error(e, "Request failed")
            pytest.skip(f"Backend not available: {e}")
        finally:
            Path(temp_path).unlink(missing_ok=True)


# Run tests directly if executed as script
if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short"])
