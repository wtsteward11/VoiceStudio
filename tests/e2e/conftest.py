"""
E2E Test Configuration and Shared Fixtures.

Provides common setup for end-to-end workflow tests.
"""

from __future__ import annotations

import contextlib
import os
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

import pytest

# Add project roots to path
PROJECT_ROOT = Path(__file__).parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT / "tests" / "ui"))
sys.path.insert(0, str(PROJECT_ROOT))

try:
    import requests
except ImportError:
    requests = None

try:
    from selenium import webdriver
    from selenium.common.exceptions import TimeoutException
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.support.ui import WebDriverWait
except ImportError:
    webdriver = None


# Configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8001")
WINAPPDRIVER_URL = "http://127.0.0.1:4723"
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/e2e"))
SCREENSHOTS_ENABLED = os.getenv("VOICESTUDIO_SCREENSHOTS_ENABLED", "true").lower() == "true"


# Resolve app path - check VS_APP_PATH first, then VOICESTUDIO_APP_PATH, then search for it
def _find_app_path() -> str:
    """Find the VoiceStudio app executable."""
    # Check environment variables first
    env_path = os.getenv("VS_APP_PATH") or os.getenv("VOICESTUDIO_APP_PATH")
    if env_path and Path(env_path).exists():
        return env_path

    # Search in common build locations
    possible_paths = [
        PROJECT_ROOT
        / ".buildlogs"
        / "x64"
        / "Debug"
        / "net8.0-windows10.0.19041.0"
        / "VoiceStudio.App.exe",
        PROJECT_ROOT / ".buildlogs" / "publish" / "VoiceStudio.App.exe",
        PROJECT_ROOT
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "x64"
        / "Debug"
        / "net8.0-windows10.0.19041.0"
        / "VoiceStudio.App.exe",
        PROJECT_ROOT
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "x64"
        / "Debug"
        / "net8.0-windows10.0.22621.0"
        / "VoiceStudio.App.exe",
        PROJECT_ROOT
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "Debug"
        / "net8.0-windows10.0.19041.0"
        / "VoiceStudio.App.exe",
    ]

    for path in possible_paths:
        if path.exists():
            return str(path)

    # Return the most likely path for error messages
    return str(possible_paths[0])


APP_PATH = _find_app_path()

# Ensure output directory exists
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


@pytest.fixture(scope="session")
def backend_url():
    """Return backend URL."""
    return BACKEND_URL


@pytest.fixture(scope="session")
def api_client():
    """Create API client for backend interaction."""
    if requests is None:
        pytest.skip("requests not installed")

    class APIClient:
        def __init__(self, base_url: str):
            self.base_url = base_url
            self.session = requests.Session()

        def get(self, path: str, **kwargs) -> requests.Response:
            return self.session.get(f"{self.base_url}{path}", **kwargs)

        def post(self, path: str, **kwargs) -> requests.Response:
            return self.session.post(f"{self.base_url}{path}", **kwargs)

        def put(self, path: str, **kwargs) -> requests.Response:
            return self.session.put(f"{self.base_url}{path}", **kwargs)

        def delete(self, path: str, **kwargs) -> requests.Response:
            return self.session.delete(f"{self.base_url}{path}", **kwargs)

        def health_check(self) -> bool:
            try:
                resp = self.get("/api/health", timeout=5)
                return resp.status_code == 200
            except Exception:
                return False

    return APIClient(BACKEND_URL)


@pytest.fixture(scope="session")
def backend_available(api_client):
    """Check if backend is available and skip if not."""
    if not api_client.health_check():
        pytest.skip("Backend not available")
    return True


@pytest.fixture(scope="session")
def winappdriver_process():
    """Start WinAppDriver if not running."""
    WINAPPDRIVER_PATH = r"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"

    if not Path(WINAPPDRIVER_PATH).exists():
        pytest.skip("WinAppDriver not installed")

    # Check if already running
    try:
        if requests:
            resp = requests.get(f"{WINAPPDRIVER_URL}/status", timeout=2)
            if resp.status_code == 200:
                yield None  # Already running, no process to manage
                return
    # ALLOWED: bare except - best effort, failure acceptable
    except Exception:
        pass

    # Start WinAppDriver
    process = subprocess.Popen(
        [WINAPPDRIVER_PATH],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(2)

    yield process

    # Cleanup
    if process:
        process.terminate()
        try:
            process.wait(timeout=5)
        except Exception:
            process.kill()


@pytest.fixture(scope="function")
def driver(winappdriver_process):
    """Create WinAppDriver session for UI automation."""
    if not Path(APP_PATH).exists():
        pytest.skip(f"App not found at {APP_PATH}")

    # Import the custom WinAppDriverSession from UI tests
    # This bypasses Selenium 4.x W3C capabilities issue
    try:
        from conftest import WinAppDriverSession
    except ImportError:
        # Fallback: define inline if import fails
        WinAppDriverSession = None

    if WinAppDriverSession is None:
        pytest.skip("WinAppDriverSession not available - UI conftest not on path")

    session = WinAppDriverSession(APP_PATH, WINAPPDRIVER_URL)
    session.implicitly_wait(10)

    # Wait for app to load
    time.sleep(3)

    yield session

    # Cleanup
    with contextlib.suppress(Exception):
        session.quit()


@pytest.fixture(scope="function")
def app_session(driver):
    """Alias for driver fixture - provides app session for UI tests."""
    return driver


@pytest.fixture
def screenshot_capture(driver):
    """Capture screenshots during test execution."""
    screenshots = []

    def capture(name: str):
        if not SCREENSHOTS_ENABLED:
            return None

        timestamp = datetime.now().strftime("%H%M%S")
        filename = f"{name}_{timestamp}.png"
        filepath = OUTPUT_DIR / "screenshots" / filename
        filepath.parent.mkdir(parents=True, exist_ok=True)

        try:
            driver.save_screenshot(str(filepath))
            screenshots.append(filepath)
            return filepath
        except Exception:
            return None

    yield capture

    return screenshots


@pytest.fixture
def test_audio_file():
    """Provide path to test audio file."""
    audio_dir = PROJECT_ROOT / "tests" / "ui" / "fixtures" / "audio"
    test_file = audio_dir / "test_speech_short.wav"

    if not test_file.exists():
        # Try to generate it
        generator = PROJECT_ROOT / "tests" / "ui" / "fixtures" / "generate_test_audio.py"
        if generator.exists():
            subprocess.run([sys.executable, str(generator)], cwd=str(audio_dir.parent))

    if test_file.exists():
        return test_file

    pytest.skip("Test audio file not available")


@pytest.fixture
def workflow_state():
    """Track workflow state across steps."""
    state = {
        "steps": [],
        "start_time": datetime.now(),
        "artifacts": [],
        "errors": [],
    }

    def record_step(name: str, success: bool = True, data: dict | None = None):
        state["steps"].append(
            {
                "name": name,
                "success": success,
                "data": data,
                "timestamp": datetime.now().isoformat(),
            }
        )

    def record_artifact(path: str, type: str):
        state["artifacts"].append({"path": path, "type": type})

    def record_error(error: str, step: str):
        state["errors"].append({"error": error, "step": step})

    state["record_step"] = record_step
    state["record_artifact"] = record_artifact
    state["record_error"] = record_error

    yield state

    # Calculate duration
    state["duration_seconds"] = (datetime.now() - state["start_time"]).total_seconds()


def find_element_safe(driver, by, value, timeout: float = 5.0):
    """Safely find element with timeout."""
    try:
        wait = WebDriverWait(driver, timeout)
        return wait.until(EC.presence_of_element_located((by, value)))
    except TimeoutException:
        return None
    except Exception:
        return None


def click_element_safe(driver, by, value, timeout: float = 5.0) -> bool:
    """Safely click element."""
    element = find_element_safe(driver, by, value, timeout)
    if element:
        try:
            element.click()
            return True
        # ALLOWED: bare except - best effort, failure acceptable
        except Exception:
            pass
    return False


def send_keys_safe(driver, by, value, text: str, timeout: float = 5.0) -> bool:
    """Safely send keys to element."""
    element = find_element_safe(driver, by, value, timeout)
    if element:
        try:
            element.clear()
            element.send_keys(text)
            return True
        # ALLOWED: bare except - best effort, failure acceptable
        except Exception:
            pass
    return False
