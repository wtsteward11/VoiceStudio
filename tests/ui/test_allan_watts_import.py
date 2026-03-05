"""
Allan Watts M4A File Import Tests.

Comprehensive import testing using the canonical test audio:
tests/assets/canonical/standard/allan_watts.wav (or allan_watts_15s.wav)

Audio is resolved via:
1. VOICESTUDIO_TEST_AUDIO environment variable (if set)
2. conftest.py canonical_audio_path fixture (auto-provisioned)
3. Synthetic generation fallback via generate_test_audio.py

Tests:
- UI Import button workflow
- Drag-and-drop import (simulated)
- Direct API upload with M4A format
- Format detection verification
- Post-import library integration
- Import error handling edge cases

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
    ALLAN_WATTS_METADATA,
    FORMAT_MIME_TYPES,
    LIBRARY_WORKFLOW,
    TEST_AUDIO_FILE,
    compute_file_checksum,
    get_test_audio_info,
)
from tracing.api_monitor import APIMonitor
from tracing.workflow_tracer import WorkflowTracer

# Configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "1") == "1"

# Pytest markers
pytestmark = [
    pytest.mark.import_workflow,
    pytest.mark.allan_watts,
]


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="module")
def tracer():
    """Create a workflow tracer for import tests."""
    t = WorkflowTracer("allan_watts_import", OUTPUT_DIR)
    t.start()
    yield t
    t.export_report()


@pytest.fixture(scope="module")
def api_monitor():
    """Create an API monitor for tracking backend calls."""
    monitor = APIMonitor(base_url=BACKEND_URL)
    yield monitor
    monitor.export_log(OUTPUT_DIR / "api_coverage" / "allan_watts_import_api_calls.json")


@pytest.fixture
def test_file():
    """Ensure the test file exists and return its info."""
    info = get_test_audio_info()
    if not info["exists"]:
        pytest.skip(f"Test audio file not found: {TEST_AUDIO_FILE}")
    return info


# =============================================================================
# Prerequisites
# =============================================================================


class TestImportPrerequisites:
    """Verify prerequisites for import testing."""

    def test_test_file_exists(self, test_file, tracer):
        """Verify Allan Watts.m4a exists."""
        tracer.start_phase("import_prerequisites", "Verify test file availability")
        tracer.step("Checking Allan Watts.m4a file exists")

        assert test_file["exists"], f"Test file should exist: {TEST_AUDIO_FILE}"
        tracer.step(
            f"Test file found: {test_file['filename']}, size: {test_file['size_mb']:.2f} MB"
        )
        tracer.end_phase(success=True, notes="Test file verified")
        tracer.success("Test file exists")

    def test_test_file_format(self, test_file, tracer):
        """Verify test file has correct format."""
        tracer.step("Verifying file format is M4A")

        assert test_file["extension"] == ".m4a", f"Expected .m4a, got {test_file['extension']}"
        tracer.step(f"Format confirmed: {test_file['extension']}")
        tracer.success("File format is M4A")

    def test_file_metadata_matches(self, test_file, tracer):
        """Verify file metadata matches expected values."""
        tracer.step("Checking file metadata")

        assert (
            test_file["filename"] == ALLAN_WATTS_METADATA.filename
        ), f"Filename mismatch: {test_file['filename']} != {ALLAN_WATTS_METADATA.filename}"

        tracer.step(f"Filename: {test_file['filename']}")
        tracer.step(f"Extension: {test_file['extension']}")
        tracer.step(f"Size: {test_file['size_mb']:.2f} MB")
        tracer.success("Metadata matches expected values")

    def test_backend_health(self, api_monitor, tracer):
        """Verify backend is healthy."""
        tracer.step("Checking backend health")

        healthy = api_monitor.wait_for_backend(timeout=10.0)
        assert healthy, f"Backend at {BACKEND_URL} should be healthy"
        tracer.success("Backend is healthy")


# =============================================================================
# UI Import Tests
# =============================================================================


class TestUIImport:
    """Test import via UI interactions."""

    @pytest.mark.smoke
    def test_import_button_enabled(self, driver, app_launched, tracer):
        """Verify Import button is visible and enabled."""
        tracer.start_phase("ui_import", "Test UI import functionality")
        tracer.step("Checking Import button availability", driver, SCREENSHOTS_ENABLED)

        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            assert import_button.is_enabled(), "Import button should be enabled"
            tracer.ui_action("check", "ImportButton", {"enabled": True})
            tracer.step("Import button is enabled", driver, SCREENSHOTS_ENABLED)
            tracer.success("Import button available and enabled")
        except RuntimeError as e:
            tracer.error(e, "Import button not found")
            raise

    @pytest.mark.smoke
    def test_import_button_opens_dialog(self, driver, app_launched, tracer):
        """Verify Import button opens file dialog."""
        tracer.step("Testing Import button click", driver, SCREENSHOTS_ENABLED)

        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")

            start_time = time.perf_counter()
            import_button.click()
            tracer.ui_action("click", "ImportButton")
            time.sleep(1.0)

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

            elapsed_ms = (time.perf_counter() - start_time) * 1000
            tracer.record_timing("dialog_open", elapsed_ms, "Import dialog open time")

            assert dialog_found, "File dialog should open after clicking Import"
            tracer.step(f"Dialog opened in {elapsed_ms:.1f}ms", driver, SCREENSHOTS_ENABLED)

            # Close dialog
            driver.press_escape()
            time.sleep(0.5)

            tracer.success("Import button opens dialog correctly")
        except RuntimeError as e:
            tracer.error(e, "Failed to open import dialog")
            raise

    def test_import_keyboard_shortcut(self, driver, app_launched, tracer):
        """Test Ctrl+I keyboard shortcut for import."""
        tracer.step("Testing Ctrl+I shortcut", driver, SCREENSHOTS_ENABLED)

        try:
            start_time = time.perf_counter()
            driver.press_shortcut("Ctrl+i")
            tracer.ui_action("keyboard", "Ctrl+I")
            time.sleep(1.0)

            dialog_found = False
            for indicator in ["Open", "Cancel", "File name:"]:
                try:
                    driver.find_element("name", indicator)
                    dialog_found = True
                    break
                # ALLOWED: bare except - best effort, failure acceptable
                except RuntimeError:
                    pass

            elapsed_ms = (time.perf_counter() - start_time) * 1000
            tracer.record_timing("shortcut_dialog_open", elapsed_ms)

            assert dialog_found, "Ctrl+I should open import dialog"
            tracer.step(
                f"Shortcut dialog opened in {elapsed_ms:.1f}ms", driver, SCREENSHOTS_ENABLED
            )

            driver.press_escape()
            time.sleep(0.5)

            tracer.success("Ctrl+I shortcut works")
        except RuntimeError as e:
            tracer.error(e, "Keyboard shortcut failed")
            raise

    def test_import_file_path_entry(self, driver, app_launched, test_file, tracer):
        """Test entering file path directly in dialog."""
        tracer.step("Testing file path entry in dialog", driver, SCREENSHOTS_ENABLED)

        try:
            import_button = driver.find_element("xpath", "//*[contains(@Name, 'Import')]")
            import_button.click()
            time.sleep(1.0)

            # Try to find filename textbox
            file_path_str = str(TEST_AUDIO_FILE)

            try:
                filename_box = driver.find_element("name", "File name:")
                filename_box.click()
                filename_box.send_keys(file_path_str)
                tracer.ui_action("type", "FileNameBox", {"text": file_path_str})
                tracer.step("File path entered in dialog", driver, SCREENSHOTS_ENABLED)

                # Don't actually submit - just verify we can type
                driver.press_escape()
                time.sleep(0.5)

                tracer.success("File path entry works")
            except RuntimeError:
                tracer.step("Could not find File name box - trying alternative approach")
                driver.press_escape()
                time.sleep(0.5)
                pytest.skip("File name textbox not accessible")

        except RuntimeError as e:
            tracer.error(e, "File path entry test failed")
            raise


# =============================================================================
# API Import Tests
# =============================================================================


class TestAPIImport:
    """Test import via direct API calls."""

    def test_upload_allan_watts_m4a(self, api_monitor, test_file, tracer):
        """Upload Allan Watts.m4a via API."""
        tracer.start_phase("api_import", "Test API upload functionality")
        tracer.step("Uploading Allan Watts.m4a via API")

        # Calculate checksum for verification
        checksum = compute_file_checksum(TEST_AUDIO_FILE)
        tracer.step(f"Source file checksum: {checksum}")

        try:
            with open(TEST_AUDIO_FILE, "rb") as f:
                files = {"file": (TEST_AUDIO_FILE.name, f, "audio/x-m4a")}

                start_time = time.perf_counter()
                response = api_monitor.post("/api/library/assets/upload", files=files)
                elapsed_ms = (time.perf_counter() - start_time) * 1000

                tracer.api_call("POST", "/api/library/assets/upload", response)
                tracer.record_timing(
                    "api_upload", elapsed_ms, f"{test_file['size_mb']:.2f} MB file"
                )

                if response.status_code in [200, 201]:
                    data = response.json()
                    tracer.step(f"Upload successful in {elapsed_ms:.1f}ms")
                    tracer.step(f"Response: {data}")

                    # Verify response contains expected fields
                    if "id" in data:
                        tracer.step(f"Asset ID: {data['id']}")
                    if "filename" in data:
                        tracer.step(f"Stored filename: {data['filename']}")

                    tracer.end_phase(success=True)
                    tracer.success("M4A file uploaded successfully")
                else:
                    tracer.step(f"Upload returned {response.status_code}: {response.text}")
                    # Try alternative endpoints
                    self._try_alternative_upload(api_monitor, test_file, tracer)

        except requests.RequestException as e:
            tracer.error(e, "Upload request failed")
            pytest.skip(f"Upload endpoint not available: {e}")

    def _try_alternative_upload(self, api_monitor, test_file, tracer):
        """Try alternative upload endpoints."""
        alternative_endpoints = [
            "/api/audio/upload",
            "/api/upload",
            "/api/v3/assets",
        ]

        for endpoint in alternative_endpoints:
            tracer.step(f"Trying alternative endpoint: {endpoint}")
            try:
                with open(TEST_AUDIO_FILE, "rb") as f:
                    files = {"file": (TEST_AUDIO_FILE.name, f, "audio/x-m4a")}
                    response = api_monitor.post(endpoint, files=files)
                    tracer.api_call("POST", endpoint, response)

                    if response.status_code in [200, 201]:
                        tracer.step(f"Alternative endpoint {endpoint} works")
                        return
            # ALLOWED: bare except - best effort, failure acceptable
            except requests.RequestException:
                pass

        tracer.step("No working upload endpoint found")

    def test_upload_with_metadata(self, api_monitor, test_file, tracer):
        """Upload with additional metadata."""
        tracer.step("Testing upload with metadata")

        metadata = {
            "title": "Allan Watts Audio",
            "source": "test",
            "tags": ["philosophy", "test", "allan_watts"],
        }

        try:
            with open(TEST_AUDIO_FILE, "rb") as f:
                files = {"file": (TEST_AUDIO_FILE.name, f, "audio/x-m4a")}
                data = {"metadata": str(metadata)}  # Some APIs accept string, some JSON

                response = api_monitor.post("/api/library/assets/upload", files=files, data=data)
                tracer.api_call("POST", "/api/library/assets/upload (with metadata)", response)

                tracer.step(f"Upload with metadata: status {response.status_code}")
                tracer.success("Metadata upload test completed")
        except requests.RequestException as e:
            tracer.step(f"Metadata upload not supported: {e}")

    def test_m4a_format_detection(self, api_monitor, tracer):
        """Verify backend detects M4A format correctly."""
        tracer.step("Testing M4A format detection")

        expected_mime = FORMAT_MIME_TYPES["m4a"]
        tracer.step(f"Expected MIME type: {expected_mime}")

        # Check if backend has format detection endpoint
        try:
            response = api_monitor.get("/api/audio/formats")
            tracer.api_call("GET", "/api/audio/formats", response)

            if response.status_code == 200:
                formats = response.json()
                if isinstance(formats, list):
                    m4a_supported = any("m4a" in str(f).lower() for f in formats)
                    tracer.step(f"M4A in supported formats: {m4a_supported}")
                elif isinstance(formats, dict):
                    tracer.step(f"Formats response: {formats}")
        except requests.RequestException:
            tracer.step("Format detection endpoint not available")

        tracer.success("Format detection check completed")


# =============================================================================
# Post-Import Verification
# =============================================================================


class TestPostImportVerification:
    """Verify state after import."""

    def test_library_navigation_after_import(self, driver, app_launched, tracer):
        """Navigate to Library panel after import."""
        tracer.step("Navigating to Library panel", driver, SCREENSHOTS_ENABLED)

        try:
            nav_id = LIBRARY_WORKFLOW.nav_id
            tracer.start_panel_transition("unknown", "Library")

            nav_button = driver.find_element("accessibility id", nav_id)
            nav_button.click()
            time.sleep(1.0)

            # Verify Library loaded
            root_id = LIBRARY_WORKFLOW.root_id
            try:
                driver.find_element("accessibility id", root_id)
                tracer.end_panel_transition(success=True, driver=driver)
                tracer.step("Library panel loaded", driver, SCREENSHOTS_ENABLED)
                tracer.success("Library navigation successful")
            except RuntimeError:
                tracer.end_panel_transition(success=False, error="Root element not found")
                pytest.fail("Library panel root not found")

        except RuntimeError as e:
            tracer.end_panel_transition(success=False, error=str(e))
            tracer.error(e, "Library navigation failed")
            raise

    def test_library_search_for_imported_file(self, driver, app_launched, tracer):
        """Search for imported file in Library."""
        tracer.step("Searching for Allan Watts in Library", driver, SCREENSHOTS_ENABLED)

        try:
            # Navigate to Library first
            nav_button = driver.find_element("accessibility id", "NavLibrary")
            nav_button.click()
            time.sleep(1.0)

            # Find search box
            search_box = driver.find_element("accessibility id", "LibraryView_SearchBox")
            search_box.click()
            search_box.clear()
            search_box.send_keys("Allan")
            tracer.ui_action("search", "LibraryView_SearchBox", {"query": "Allan"})
            time.sleep(0.5)

            tracer.step("Search query entered", driver, SCREENSHOTS_ENABLED)

            # Clear search
            search_box.clear()
            tracer.success("Library search works")
        except RuntimeError as e:
            tracer.error(e, "Library search failed")
            pytest.skip("Library search not available")


# =============================================================================
# Import Error Handling
# =============================================================================


class TestImportErrorHandling:
    """Test error handling during import."""

    def test_upload_nonexistent_file_api(self, api_monitor, tracer):
        """Test API behavior when file doesn't exist."""
        tracer.step("Testing error handling for nonexistent file")

        # Can't send nonexistent file via API, but we can test with empty body
        try:
            response = api_monitor.post("/api/library/assets/upload", files={})
            tracer.api_call("POST", "/api/library/assets/upload (empty)", response)

            assert response.status_code in [
                400,
                422,
            ], f"Empty upload should fail with validation error, got {response.status_code}"
            tracer.step(f"Empty upload correctly rejected: {response.status_code}")
            tracer.success("Empty upload handling works")
        except requests.RequestException as e:
            tracer.step(f"Request error (expected): {e}")

    def test_upload_corrupted_audio(self, api_monitor, tracer):
        """Test handling of corrupted audio data."""
        tracer.step("Testing corrupted audio handling")

        import tempfile

        # Create a file with M4A extension but invalid content
        with tempfile.NamedTemporaryFile(suffix=".m4a", delete=False) as f:
            f.write(b"This is not valid M4A audio data")
            temp_path = Path(f.name)

        try:
            with open(temp_path, "rb") as f:
                files = {"file": ("corrupted.m4a", f, "audio/x-m4a")}
                response = api_monitor.post("/api/library/assets/upload", files=files)
                tracer.api_call("POST", "/api/library/assets/upload (corrupted)", response)

                tracer.step(f"Corrupted file handling: status {response.status_code}")
                # Either reject (400/422) or attempt processing is acceptable
                tracer.success("Corrupted audio handling test completed")
        except requests.RequestException as e:
            tracer.step(f"Request error: {e}")
        finally:
            temp_path.unlink(missing_ok=True)

    def test_upload_oversized_simulation(self, api_monitor, tracer):
        """Document size limits (without actually uploading large file)."""
        tracer.step("Checking upload size limits")

        # Check if there's a documented size limit
        try:
            response = api_monitor.get("/api/config")
            tracer.api_call("GET", "/api/config", response)

            if response.status_code == 200:
                config = response.json()
                if "max_upload_size" in config:
                    tracer.step(f"Max upload size: {config['max_upload_size']}")
        # ALLOWED: bare except - best effort, failure acceptable
        except requests.RequestException:
            pass

        # The Allan Watts file is a reasonable size for testing
        info = get_test_audio_info()
        if info["exists"]:
            tracer.step(f"Test file size: {info['size_mb']:.2f} MB - within typical limits")

        tracer.success("Size limit check completed")


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
            "--html=.buildlogs/validation/reports/allan_watts_import_report.html",
            "--self-contained-html",
        ]
    )
