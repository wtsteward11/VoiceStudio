"""
Pytest configuration and fixtures for UI tests.

Enhanced with:
- Screenshot capture on test failure
- Environment variable support for application path
- Retry utilities for flaky element lookups
- Structured output directories
"""

from __future__ import annotations

import contextlib
import os
import subprocess
import time
from datetime import datetime
from pathlib import Path

import pytest
import requests
from selenium.common.exceptions import NoSuchElementException, TimeoutException
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait

# =============================================================================
# Configuration
# =============================================================================

WINAPPDRIVER_URL = os.getenv("WINAPPDRIVER_URL", "http://127.0.0.1:4723")
WINAPPDRIVER_PATH = os.getenv(
    "WINAPPDRIVER_PATH", r"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"
)
IMPLICIT_WAIT = int(os.getenv("UI_TEST_IMPLICIT_WAIT", "10"))  # seconds
EXPLICIT_WAIT = int(os.getenv("UI_TEST_EXPLICIT_WAIT", "30"))  # seconds

# Application path - check environment variable first
PROJECT_ROOT = Path(__file__).parent.parent.parent
OUTPUT_DIR = PROJECT_ROOT / ".buildlogs" / "ui_tests"
SCREENSHOT_DIR = OUTPUT_DIR / "screenshots"

# Try environment variable, then fallback to build paths
APP_PATH_ENV = os.getenv("VS_APP_PATH")
if APP_PATH_ENV:
    APP_PATH = Path(APP_PATH_ENV)
else:
    # Try multiple possible locations
    POSSIBLE_PATHS = [
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
        / "Debug"
        / "net8.0-windows10.0.19041.0"
        / "VoiceStudio.App.exe",
        PROJECT_ROOT
        / "src"
        / "VoiceStudio.App"
        / "bin"
        / "Release"
        / "net8.0-windows10.0.19041.0"
        / "win-x64"
        / "publish"
        / "VoiceStudio.App.exe",
    ]
    APP_PATH = next((p for p in POSSIBLE_PATHS if p.exists()), POSSIBLE_PATHS[0])


# =============================================================================
# Setup
# =============================================================================


def pytest_configure(config):
    """Configure pytest with custom markers and output directories."""
    # Register custom markers
    config.addinivalue_line(
        "markers", "slow: marks tests as slow (deselect with '-m \"not slow\"')"
    )
    config.addinivalue_line("markers", "flaky: marks tests as potentially flaky")
    config.addinivalue_line("markers", "smoke: marks tests as smoke tests")
    config.addinivalue_line("markers", "navigation: marks tests as navigation tests")
    config.addinivalue_line("markers", "panel: marks tests as panel functionality tests")
    # Phase 4B: Additional markers for UI test organization
    config.addinivalue_line("markers", "synthesis: marks tests as voice synthesis tests")
    config.addinivalue_line("markers", "rendering: marks tests as UI rendering tests")
    config.addinivalue_line("markers", "matrix: marks tests as panel matrix tests")
    config.addinivalue_line("markers", "workflow: marks tests as workflow tests")
    config.addinivalue_line("markers", "e2e: marks tests as end-to-end lifecycle tests")
    config.addinivalue_line("markers", "audio: marks tests that require audio files")

    # Ensure output directories exist
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)


def pytest_addoption(parser):
    """Add custom command-line options."""
    parser.addoption(
        "--generate-audio",
        action="store_true",
        default=False,
        help="Force regeneration of test audio files even if they exist",
    )
    parser.addoption(
        "--skip-audio-gen",
        action="store_true",
        default=False,
        help="Skip automatic audio generation (fail fast if audio missing)",
    )


# =============================================================================
# Utility Functions
# =============================================================================


def is_winappdriver_running() -> bool:
    """Check if WinAppDriver is running."""
    try:
        import requests

        response = requests.get(f"{WINAPPDRIVER_URL}/status", timeout=2)
        return response.status_code == 200
    except Exception:
        return False


def start_winappdriver() -> bool:
    """Start WinAppDriver service."""
    if is_winappdriver_running():
        return True

    try:
        if os.path.exists(WINAPPDRIVER_PATH):
            subprocess.Popen(
                [WINAPPDRIVER_PATH],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            # Wait for WinAppDriver to start
            for _ in range(10):
                time.sleep(1)
                if is_winappdriver_running():
                    return True
        return False
    except Exception as e:
        print(f"Failed to start WinAppDriver: {e}")
        return False


def capture_screenshot(driver, name: str) -> Path | None:
    """Capture a screenshot and save to the screenshots directory."""
    try:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"{name}_{timestamp}.png"
        filepath = SCREENSHOT_DIR / filename
        driver.save_screenshot(str(filepath))
        print(f"Screenshot saved: {filepath}")
        return filepath
    except Exception as e:
        print(f"Failed to capture screenshot: {e}")
        return None


def find_element_with_retry(driver, by: str, value: str, retries: int = 3, delay: float = 1.0):
    """Find an element with retry logic for flaky lookups."""
    last_exception = None
    for attempt in range(retries):
        try:
            return driver.find_element(by, value)
        except NoSuchElementException as e:
            last_exception = e
            if attempt < retries - 1:
                time.sleep(delay)
    raise last_exception


def wait_for_element(driver, automation_id: str, timeout: int = EXPLICIT_WAIT):
    """Wait for an element to be present and visible."""
    try:
        wait = WebDriverWait(driver, timeout)
        element = wait.until(EC.presence_of_element_located(("accessibility id", automation_id)))
        return element
    except TimeoutException:
        raise TimeoutException(f"Element '{automation_id}' not found within {timeout}s")


def wait_for_panel_load(driver, panel_id: str, timeout: int = EXPLICIT_WAIT):
    """Wait for a panel to fully load."""
    element = wait_for_element(driver, panel_id, timeout)
    # Additional wait for content to render
    time.sleep(0.5)
    return element


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="session")
def winappdriver_service():
    """Ensure WinAppDriver is running before tests."""
    if not is_winappdriver_running() and not start_winappdriver():
        pytest.skip(
            "WinAppDriver is not running and could not be started. "
            "Please start WinAppDriver manually."
        )
    yield
    # Cleanup if needed


@pytest.fixture(scope="session", autouse=True)
def ensure_test_audio(request):
    """
    Session-scoped autouse fixture that ensures test audio files are available.

    This fixture:
    1. Checks if canonical audio files exist
    2. If missing, generates synthetic fallback audio
    3. Sets VOICESTUDIO_TEST_AUDIO environment variable to the audio path

    Can be controlled via pytest options:
    - --generate-audio: Force regeneration even if files exist
    - --skip-audio-gen: Skip generation entirely (fail fast if missing)
    """
    skip_gen = request.config.getoption("--skip-audio-gen", default=False)
    force_gen = request.config.getoption("--generate-audio", default=False)

    if skip_gen:
        # Check if audio exists without generating
        canonical_dir = PROJECT_ROOT / "tests" / "assets" / "canonical" / "standard"
        audio_path = canonical_dir / "allan_watts_15s.wav"
        if not audio_path.exists():
            audio_path = canonical_dir / "allan_watts.wav"
        if not audio_path.exists():
            # Check UI fixtures
            fixtures_dir = Path(__file__).parent / "fixtures"
            audio_path = fixtures_dir / "test_audio_short.wav"

        if audio_path.exists():
            os.environ.setdefault("VOICESTUDIO_TEST_AUDIO", str(audio_path))
        yield
        return

    # Import and use the generator
    try:
        from tests.ui.fixtures.generate_test_audio import (
            ensure_test_audio_available,
            generate_canonical_audio,
        )

        if force_gen:
            print("\n[conftest] Force-generating test audio files...")
            generate_canonical_audio(force=True)

        audio_path = ensure_test_audio_available()

        if audio_path:
            os.environ.setdefault("VOICESTUDIO_TEST_AUDIO", str(audio_path))
            print(f"\n[conftest] Test audio available: {audio_path}")
        else:
            print("\n[conftest] WARNING: Could not ensure test audio availability")

    except ImportError as e:
        print(f"\n[conftest] WARNING: Could not import audio generator: {e}")
    except Exception as e:
        print(f"\n[conftest] WARNING: Error ensuring test audio: {e}")

    yield


@pytest.fixture(scope="session")
def canonical_audio_path(ensure_test_audio):
    """
    Provide the path to canonical test audio.

    Depends on ensure_test_audio to guarantee files exist.
    Returns the path to the best available test audio file.
    """
    # Check for canonical audio
    canonical_dir = PROJECT_ROOT / "tests" / "assets" / "canonical" / "standard"

    # Prefer the 15s segment for faster tests
    segment_path = canonical_dir / "allan_watts_15s.wav"
    if segment_path.exists():
        return segment_path

    # Fall back to full audio
    full_path = canonical_dir / "allan_watts.wav"
    if full_path.exists():
        return full_path

    # Fall back to UI fixtures
    fixtures_dir = Path(__file__).parent / "fixtures"
    for name in ["test_audio_short.wav", "test_audio_long.wav", "voice_reference_clean.wav"]:
        path = fixtures_dir / name
        if path.exists():
            return path

    # Last resort: check environment variable
    env_path = os.getenv("VOICESTUDIO_TEST_AUDIO")
    if env_path and Path(env_path).exists():
        return Path(env_path)

    pytest.skip(
        "No test audio available. Run with --generate-audio or provide VOICESTUDIO_TEST_AUDIO"
    )


class WinAppDriverSession:
    """
    Custom WinAppDriver session using JSON Wire Protocol.

    WinAppDriver (1.2) doesn't support W3C capabilities format used by
    Selenium 4.x, so we use raw HTTP requests for all operations.
    """

    def __init__(self, app_path: str, base_url: str = "http://127.0.0.1:4723"):
        self.base_url = base_url
        self.session_id = None
        self._implicit_wait = 10

        # Kill any existing VoiceStudio processes before starting
        try:
            import subprocess

            subprocess.run(
                ["taskkill", "/F", "/IM", "VoiceStudio.App.exe"], capture_output=True, timeout=5
            )
            time.sleep(1)
        # ALLOWED: bare except - best effort, failure acceptable
        except Exception:
            pass

        # Create session with retry
        payload = {
            "desiredCapabilities": {
                "app": app_path,
                "platformName": "Windows",
                "deviceName": "WindowsPC",
            }
        }

        last_error = None
        for _attempt in range(3):
            try:
                response = requests.post(
                    f"{base_url}/session",
                    json=payload,
                    headers={"Content-Type": "application/json"},
                    timeout=60,
                )

                if response.status_code == 200:
                    data = response.json()
                    self.session_id = data.get("sessionId")
                    if self.session_id:
                        return  # Success

                last_error = response.text
            except Exception as e:
                last_error = str(e)

            # Wait before retry
            time.sleep(2)

        raise RuntimeError(f"Failed to create session after 3 attempts: {last_error}")

    def _session_url(self, path: str = "") -> str:
        return f"{self.base_url}/session/{self.session_id}{path}"

    def _request(self, method: str, path: str, data: dict | None = None) -> dict:
        url = self._session_url(path)
        response = requests.request(
            method, url, json=data, headers={"Content-Type": "application/json"}, timeout=30
        )
        if response.status_code >= 400:
            raise RuntimeError(f"Request failed: {response.text}")
        return response.json() if response.text else {}

    def implicitly_wait(self, seconds: float):
        """Set implicit wait timeout."""
        self._implicit_wait = seconds
        self._request("POST", "/timeouts", {"implicit": int(seconds * 1000)})

    def find_element(self, by: str, value: str):
        """Find element by locator strategy."""
        # Map common strategies
        strategy_map = {
            "accessibility id": "accessibility id",
            "name": "name",
            "class name": "class name",
            "xpath": "xpath",
            "id": "id",
        }
        using = strategy_map.get(by, by)
        result = self._request("POST", "/element", {"using": using, "value": value})
        element_id = result.get("value", {}).get("ELEMENT") or next(
            iter(result.get("value", {}).values())
        )
        return WinAppDriverElement(self, element_id)

    def find_elements(self, by: str, value: str):
        """Find elements by locator strategy."""
        strategy_map = {
            "accessibility id": "accessibility id",
            "name": "name",
            "class name": "class name",
            "xpath": "xpath",
        }
        using = strategy_map.get(by, by)
        result = self._request("POST", "/elements", {"using": using, "value": value})
        elements = []
        for elem in result.get("value", []):
            element_id = elem.get("ELEMENT") or next(iter(elem.values()))
            elements.append(WinAppDriverElement(self, element_id))
        return elements

    def save_screenshot(self, filepath: str) -> bool:
        """Save screenshot to file."""
        try:
            result = self._request("GET", "/screenshot")
            import base64

            with open(filepath, "wb") as f:
                f.write(base64.b64decode(result.get("value", "")))
            return True
        except Exception:
            return False

    @property
    def title(self) -> str:
        """Get window title."""
        result = self._request("GET", "/title")
        return result.get("value", "")

    def quit(self):
        """Close session and app."""
        with contextlib.suppress(Exception):
            self._request("DELETE", "")

        # Give time for app to fully close
        time.sleep(1.5)

        # Force kill any remaining VoiceStudio processes
        try:
            import subprocess

            subprocess.run(
                ["taskkill", "/F", "/IM", "VoiceStudio.App.exe"], capture_output=True, timeout=5
            )
        # ALLOWED: bare except - best effort, failure acceptable
        except Exception:
            pass

        # Ensure process is fully terminated before next test
        time.sleep(1.5)

    # -------------------------------------------------------------------------
    # Keyboard Action Chains
    # -------------------------------------------------------------------------

    # Special key codes for WinAppDriver (Unicode Private Use Area)
    KEYS = {
        "NULL": "\ue000",
        "CANCEL": "\ue001",
        "HELP": "\ue002",
        "BACKSPACE": "\ue003",
        "TAB": "\ue004",
        "CLEAR": "\ue005",
        "RETURN": "\ue006",
        "ENTER": "\ue007",
        "SHIFT": "\ue008",
        "CONTROL": "\ue009",
        "ALT": "\ue00a",
        "PAUSE": "\ue00b",
        "ESCAPE": "\ue00c",
        "SPACE": "\ue00d",
        "PAGE_UP": "\ue00e",
        "PAGE_DOWN": "\ue00f",
        "END": "\ue010",
        "HOME": "\ue011",
        "LEFT": "\ue012",
        "UP": "\ue013",
        "RIGHT": "\ue014",
        "DOWN": "\ue015",
        "INSERT": "\ue016",
        "DELETE": "\ue017",
        "SEMICOLON": "\ue018",
        "EQUALS": "\ue019",
        "NUMPAD0": "\ue01a",
        "NUMPAD1": "\ue01b",
        "NUMPAD2": "\ue01c",
        "NUMPAD3": "\ue01d",
        "NUMPAD4": "\ue01e",
        "NUMPAD5": "\ue01f",
        "NUMPAD6": "\ue020",
        "NUMPAD7": "\ue021",
        "NUMPAD8": "\ue022",
        "NUMPAD9": "\ue023",
        "MULTIPLY": "\ue024",
        "ADD": "\ue025",
        "SEPARATOR": "\ue026",
        "SUBTRACT": "\ue027",
        "DECIMAL": "\ue028",
        "DIVIDE": "\ue029",
        "F1": "\ue031",
        "F2": "\ue032",
        "F3": "\ue033",
        "F4": "\ue034",
        "F5": "\ue035",
        "F6": "\ue036",
        "F7": "\ue037",
        "F8": "\ue038",
        "F9": "\ue039",
        "F10": "\ue03a",
        "F11": "\ue03b",
        "F12": "\ue03c",
        "META": "\ue03d",
        "COMMAND": "\ue03d",
        "WIN": "\ue03d",
    }

    def send_keys(self, *keys: str):
        """
        Send key sequence to the active window.

        Args:
            *keys: Key strings or special key names (e.g., "CONTROL", "a").
        """
        key_sequence = []
        for key in keys:
            if key.upper() in self.KEYS:
                key_sequence.append(self.KEYS[key.upper()])
            else:
                key_sequence.extend(list(key))

        self._request("POST", "/keys", {"value": key_sequence})

    def send_key_combination(self, *modifiers_and_key: str):
        """
        Send a key combination (e.g., Ctrl+S, Alt+F4).

        Args:
            *modifiers_and_key: Modifier keys followed by the final key.
                               e.g., ("CONTROL", "s") for Ctrl+S

        Example:
            driver.send_key_combination("CONTROL", "SHIFT", "s")  # Ctrl+Shift+S
            driver.send_key_combination("ALT", "F4")  # Alt+F4
        """
        if not modifiers_and_key:
            return

        key_sequence = []
        # Press modifiers
        modifiers = modifiers_and_key[:-1]
        final_key = modifiers_and_key[-1]

        for mod in modifiers:
            mod_key = self.KEYS.get(mod.upper())
            if mod_key:
                key_sequence.append(mod_key)

        # Press final key
        if final_key.upper() in self.KEYS:
            key_sequence.append(self.KEYS[final_key.upper()])
        else:
            key_sequence.extend(list(final_key))

        # Release modifiers (by pressing them again - this releases them)
        for mod in reversed(modifiers):
            mod_key = self.KEYS.get(mod.upper())
            if mod_key:
                key_sequence.append(mod_key)

        self._request("POST", "/keys", {"value": key_sequence})

    def press_shortcut(self, shortcut: str):
        """
        Press a keyboard shortcut in string format.

        Args:
            shortcut: Shortcut string like "Ctrl+S", "Alt+F4", "Ctrl+Shift+N".

        Example:
            driver.press_shortcut("Ctrl+S")
            driver.press_shortcut("Ctrl+Shift+P")
            driver.press_shortcut("F5")
        """
        # Parse shortcut string
        parts = [p.strip() for p in shortcut.split("+")]

        # Map common shortcut names to key codes
        shortcut_map = {
            "ctrl": "CONTROL",
            "alt": "ALT",
            "shift": "SHIFT",
            "win": "META",
            "cmd": "META",
            "meta": "META",
            "esc": "ESCAPE",
            "enter": "ENTER",
            "return": "RETURN",
            "tab": "TAB",
            "space": "SPACE",
            "backspace": "BACKSPACE",
            "delete": "DELETE",
            "del": "DELETE",
            "home": "HOME",
            "end": "END",
            "pageup": "PAGE_UP",
            "pagedown": "PAGE_DOWN",
            "up": "UP",
            "down": "DOWN",
            "left": "LEFT",
            "right": "RIGHT",
        }

        mapped_parts = []
        for part in parts:
            lower_part = part.lower()
            if lower_part in shortcut_map:
                mapped_parts.append(shortcut_map[lower_part])
            elif part.upper() in self.KEYS:
                mapped_parts.append(part.upper())
            else:
                mapped_parts.append(part.lower())

        self.send_key_combination(*mapped_parts)

    def press_escape(self):
        """Press Escape key."""
        self.send_keys("ESCAPE")

    def press_enter(self):
        """Press Enter key."""
        self.send_keys("ENTER")

    def press_tab(self, shift: bool = False):
        """
        Press Tab key, optionally with Shift.

        Args:
            shift: If True, press Shift+Tab (reverse tab).
        """
        if shift:
            self.send_key_combination("SHIFT", "TAB")
        else:
            self.send_keys("TAB")

    # -------------------------------------------------------------------------
    # Switch To (for active element, window, frame operations)
    # -------------------------------------------------------------------------

    @property
    def switch_to(self):
        """Return a SwitchTo object for accessing active element, windows, etc."""
        return self._SwitchTo(self)

    class _SwitchTo:
        """Provides switch_to functionality for WinAppDriver sessions."""

        def __init__(self, session: WinAppDriverSession):
            self._session = session

        @property
        def active_element(self):
            """Get the currently active/focused element."""
            result = self._session._request("GET", "/element/active")
            value = result.get("value", {})
            element_id = value.get("ELEMENT") or next(iter(value.values()), None)
            if not element_id:
                raise RuntimeError("No active element found")
            return WinAppDriverElement(self._session, element_id)

        def window(self, window_handle: str):
            """Switch to a different window by handle."""
            self._session._request("POST", "/window", {"handle": window_handle})

        def default_content(self):
            """Switch to the default content (main window)."""
            pass  # WinAppDriver operates on a single window typically


class WinAppDriverElement:
    """Element wrapper for WinAppDriver."""

    def __init__(self, session: WinAppDriverSession, element_id: str):
        self._session = session
        self._element_id = element_id

    def _request(self, method: str, path: str, data: dict | None = None) -> dict:
        return self._session._request(method, f"/element/{self._element_id}{path}", data)

    def click(self):
        """Click the element."""
        self._request("POST", "/click")

    def double_click(self):
        """Double-click the element by clicking twice quickly."""
        import time

        # Simple double-click: click twice with minimal delay
        self._request("POST", "/click")
        time.sleep(0.05)  # 50ms between clicks for double-click
        self._request("POST", "/click")

    def send_keys(self, text: str):
        """Send keys to element."""
        self._request("POST", "/value", {"value": list(text)})

    def clear(self):
        """Clear element text."""
        self._request("POST", "/clear")

    @property
    def text(self) -> str:
        """Get element text."""
        result = self._request("GET", "/text")
        return result.get("value", "")

    def get_attribute(self, name: str) -> str:
        """Get element attribute."""
        result = self._request("GET", f"/attribute/{name}")
        return result.get("value", "")

    def is_displayed(self) -> bool:
        """Check if element is displayed."""
        result = self._request("GET", "/displayed")
        return result.get("value", False)

    def is_enabled(self) -> bool:
        """Check if element is enabled."""
        result = self._request("GET", "/enabled")
        return result.get("value", False)

    @property
    def rect(self) -> dict:
        """Get element rectangle (position and size)."""
        result = self._request("GET", "/rect")
        return result.get("value", {})

    @property
    def location(self) -> dict:
        """Get element location (x, y coordinates)."""
        result = self._request("GET", "/location")
        return result.get("value", {})

    @property
    def size(self) -> dict:
        """Get element size (width, height)."""
        result = self._request("GET", "/size")
        return result.get("value", {})

    def find_element(self, by: str, value: str):
        """Find child element."""
        strategy_map = {"accessibility id": "accessibility id", "name": "name", "xpath": "xpath"}
        using = strategy_map.get(by, by)
        result = self._request("POST", "/element", {"using": using, "value": value})
        element_id = result.get("value", {}).get("ELEMENT") or next(
            iter(result.get("value", {}).values())
        )
        return WinAppDriverElement(self._session, element_id)

    def find_elements(self, by: str, value: str):
        """Find child elements."""
        strategy_map = {"accessibility id": "accessibility id", "name": "name", "xpath": "xpath"}
        using = strategy_map.get(by, by)
        result = self._request("POST", "/elements", {"using": using, "value": value})
        elements = []
        for elem in result.get("value", []):
            element_id = elem.get("ELEMENT") or next(iter(elem.values()))
            elements.append(WinAppDriverElement(self._session, element_id))
        return elements


@pytest.fixture(scope="function")
def driver(winappdriver_service, request):
    """Create and configure WinAppDriver session for Windows application."""
    if not APP_PATH.exists():
        pytest.skip(
            f"Application not found at {APP_PATH}. Please build the application first. "
            f"Set VS_APP_PATH environment variable to override."
        )

    # Create WinAppDriver session with custom JSON Wire Protocol client
    session = WinAppDriverSession(str(APP_PATH), WINAPPDRIVER_URL)

    # Set implicit wait
    session.implicitly_wait(IMPLICIT_WAIT)

    yield session

    # Capture screenshot on test failure and attach to pytest-html report
    if hasattr(request.node, "rep_call") and request.node.rep_call.failed:
        test_name = request.node.name.replace("::", "_").replace("[", "_").replace("]", "_")
        screenshot_path = capture_screenshot(session, f"FAILED_{test_name}")

        # Attach screenshot to pytest-html report if available
        if screenshot_path:
            _attach_screenshot_to_html_report(request, screenshot_path)

    try:
        session.quit()
    # ALLOWED: bare except - Best effort cleanup, failure is acceptable
    except Exception:
        pass


def _attach_screenshot_to_html_report(request, screenshot_path: Path):
    """Attach screenshot to pytest-html report if the plugin is available."""
    try:
        # Check if pytest-html is available
        if hasattr(request.config, "_html"):
            import base64

            # Read screenshot and encode as base64
            with open(screenshot_path, "rb") as f:
                screenshot_data = base64.b64encode(f.read()).decode("utf-8")

            # Get or create extras list for this test
            extra = getattr(request.node, "extra", [])

            # Add screenshot as embedded image
            from pytest_html import extras

            extra.append(extras.image(f"data:image/png;base64,{screenshot_data}"))

            request.node.extra = extra
    except ImportError:
        # pytest-html not installed, skip attachment
        pass
    except Exception as e:
        print(f"Warning: Failed to attach screenshot to HTML report: {e}")


@pytest.hookimpl(tryfirst=True, hookwrapper=True)
def pytest_runtest_makereport(item, call):
    """Hook to capture test result for screenshot on failure."""
    outcome = yield
    rep = outcome.get_result()
    setattr(item, f"rep_{rep.when}", rep)


@pytest.fixture(scope="function")
def app_launched(driver):
    """Ensure application is launched and ready."""
    # Wait for app to load by checking for navigation elements
    # The navigation rail with elements like NavStudio, NavProfiles, etc.
    max_wait = 30
    start = time.time()

    # First, check for and dismiss any welcome dialog
    try:
        # Look for welcome dialog - try Skip button first (faster dismissal)
        time.sleep(1.5)  # Wait for dialog to fully appear

        # Try to find Skip button by name
        try:
            skip_btn = driver.find_element("name", "Skip")
            skip_btn.click()
            time.sleep(0.5)
            print("Dismissed welcome dialog via Skip button")
        except (RuntimeError, NoSuchElementException):
            # Try CloseButton automation ID
            try:
                close_btn = driver.find_element("accessibility id", "CloseButton")
                close_btn.click()
                time.sleep(0.5)
                print("Dismissed welcome dialog via CloseButton")
            except (RuntimeError, NoSuchElementException):
                # Try PrimaryButton as last resort
                try:
                    primary_btn = driver.find_element("accessibility id", "PrimaryButton")
                    primary_btn.click()
                    time.sleep(0.5)
                    print("Dismissed welcome dialog via PrimaryButton")
                except (RuntimeError, NoSuchElementException):
                    pass  # No dialog to close
    except Exception as e:
        print(f"Dialog dismissal error (non-fatal): {e}")
        pass  # Continue if any error during dialog dismissal

    while time.time() - start < max_wait:
        try:
            # Try to find a navigation element that should always be visible
            nav_element = driver.find_element("accessibility id", "NavStudio")
            return nav_element  # App is ready
        except (RuntimeError, NoSuchElementException):
            time.sleep(0.5)

    pytest.fail("Application failed to launch - NavStudio element not found after 30s")


@pytest.fixture
def screenshot(driver):
    """Fixture to capture screenshots on demand."""

    def _capture(name: str):
        return capture_screenshot(driver, name)

    return _capture


@pytest.fixture
def navigate_to_panel(driver, app_launched):
    """Fixture to navigate to a specific panel."""

    def _navigate(panel_button_id: str, panel_view_id: str, timeout: int = EXPLICIT_WAIT):
        try:
            button = find_element_with_retry(driver, "accessibility id", panel_button_id)
            button.click()
            time.sleep(0.5)
            return wait_for_panel_load(driver, panel_view_id, timeout)
        except Exception as e:
            raise AssertionError(
                f"Failed to navigate to panel. Button: {panel_button_id}, "
                f"View: {panel_view_id}. Error: {e}"
            )

    return _navigate


@pytest.fixture
def retry_action():
    """Fixture for retrying flaky actions."""

    def _retry(action, retries: int = 3, delay: float = 1.0, on_fail=None):
        last_exception = None
        for attempt in range(retries):
            try:
                return action()
            except Exception as e:
                last_exception = e
                if on_fail:
                    on_fail(attempt, e)
                if attempt < retries - 1:
                    time.sleep(delay)
        raise last_exception

    return _retry


# =============================================================================
# Tracing Fixtures
# =============================================================================

BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")


@pytest.fixture(scope="session")
def session_tracer():
    """
    Session-scoped WorkflowTracer for capturing traces across multiple tests.

    The tracer is initialized once per test session and can be used to track
    the full workflow across related tests. Individual tests can start new
    phases within the same session tracer.

    Yields:
        WorkflowTracer: Configured tracer instance.
    """
    from tracing.workflow_tracer import WorkflowTracer

    output_dir = OUTPUT_DIR / "traces"
    output_dir.mkdir(parents=True, exist_ok=True)

    tracer = WorkflowTracer(
        workflow_name="session_workflow",
        output_dir=output_dir,
    )
    tracer.start("Session Test Workflow")

    yield tracer

    # Export reports at session end
    try:
        tracer.complete()
        tracer.export_json()
        tracer.export_html()
    except Exception as e:
        print(f"[conftest] Error exporting session tracer reports: {e}")


@pytest.fixture(scope="function")
def tracer(request):
    """
    Function-scoped WorkflowTracer for per-test tracing.

    Creates a fresh tracer for each test, allowing isolated trace capture.
    Reports are exported automatically at test completion.

    Yields:
        WorkflowTracer: Configured tracer instance for the current test.
    """
    from tracing.workflow_tracer import WorkflowTracer

    test_name = request.node.name.replace("::", "_").replace("[", "_").replace("]", "_")
    output_dir = OUTPUT_DIR / "traces" / test_name
    output_dir.mkdir(parents=True, exist_ok=True)

    tracer = WorkflowTracer(
        workflow_name=test_name,
        output_dir=output_dir,
    )
    tracer.start(f"Test: {test_name}")

    yield tracer

    # Export reports at test end
    try:
        tracer.complete()
        tracer.export_json()
    except Exception as e:
        print(f"[conftest] Error exporting test tracer reports: {e}")


@pytest.fixture(scope="session")
def session_api_monitor(session_tracer):
    """
    Session-scoped APIMonitor for monitoring backend calls across tests.

    Integrated with session_tracer for correlated logging.

    Yields:
        APIMonitor: Configured monitor instance.
    """
    from tracing.api_monitor import APIMonitor

    monitor = APIMonitor(
        base_url=BACKEND_URL,
        timeout=30.0,
        tracer=session_tracer,
    )

    yield monitor

    # Export call log at session end
    try:
        log_path = OUTPUT_DIR / "traces" / "api_calls_session.json"
        monitor.export_log(log_path)
    except Exception as e:
        print(f"[conftest] Error exporting session API log: {e}")


@pytest.fixture(scope="function")
def api_monitor(tracer):
    """
    Function-scoped APIMonitor for per-test backend call monitoring.

    Integrated with the function-scoped tracer for correlated logging.

    Yields:
        APIMonitor: Configured monitor instance for the current test.
    """
    from tracing.api_monitor import APIMonitor

    monitor = APIMonitor(
        base_url=BACKEND_URL,
        timeout=30.0,
        tracer=tracer,
    )

    yield monitor


@pytest.fixture(scope="function")
def uploaded_asset(api_monitor, canonical_audio_path):
    """
    Upload test audio to the backend and return asset metadata.

    This fixture dynamically uploads the canonical test audio file to the
    backend's library, eliminating the need for hardcoded asset UUIDs.
    The uploaded asset is available for the duration of the test.

    Yields:
        dict: Asset metadata including 'id', 'filename', 'path', etc.
              Returns None if upload fails.
    """
    if not canonical_audio_path.exists():
        pytest.skip(f"Test audio not found: {canonical_audio_path}")

    try:
        with open(canonical_audio_path, "rb") as f:
            files = {"file": (canonical_audio_path.name, f, "audio/wav")}
            response = api_monitor.post("/api/library/assets/upload", files=files)

        if response.status_code in (200, 201):
            data = response.json()
            asset_id = data.get("id") or data.get("asset_id")
            if asset_id:
                yield {
                    "id": asset_id,
                    "filename": canonical_audio_path.name,
                    "path": canonical_audio_path,
                    "response": data,
                }
                return

        print(f"[conftest] Upload failed: {response.status_code}")
        yield None

    except Exception as e:
        print(f"[conftest] Error uploading test audio: {e}")
        yield None


@pytest.fixture(scope="session")
def session_uploaded_asset(session_api_monitor, ensure_test_audio):
    """
    Session-scoped uploaded asset for tests that share the same asset.

    Uploads the canonical test audio once per session and provides
    the asset metadata to all tests that need it.

    Yields:
        dict: Asset metadata or None if upload fails.
    """
    canonical_dir = PROJECT_ROOT / "tests" / "assets" / "canonical" / "standard"
    audio_path = canonical_dir / "allan_watts_15s.wav"
    if not audio_path.exists():
        audio_path = canonical_dir / "allan_watts.wav"
    if not audio_path.exists():
        # Fall back to environment variable
        env_path = os.getenv("VOICESTUDIO_TEST_AUDIO")
        if env_path:
            audio_path = Path(env_path)

    if not audio_path.exists():
        print("[conftest] No test audio available for session upload")
        yield None
        return

    try:
        with open(audio_path, "rb") as f:
            files = {"file": (audio_path.name, f, "audio/wav")}
            response = session_api_monitor.post("/api/library/assets/upload", files=files)

        if response.status_code in (200, 201):
            data = response.json()
            asset_id = data.get("id") or data.get("asset_id")
            if asset_id:
                print(f"[conftest] Session asset uploaded: {asset_id}")
                yield {
                    "id": asset_id,
                    "filename": audio_path.name,
                    "path": audio_path,
                    "response": data,
                }
                return

        print(f"[conftest] Session upload failed: {response.status_code}")
        yield None

    except Exception as e:
        print(f"[conftest] Error uploading session audio: {e}")
        yield None


# =============================================================================
# Session Info
# =============================================================================


def pytest_report_header(config):
    """Add custom header info to test report."""
    return [
        "VoiceStudio UI Tests",
        f"  App Path: {APP_PATH}",
        f"  WinAppDriver: {WINAPPDRIVER_URL}",
        f"  Backend URL: {BACKEND_URL}",
        f"  Screenshots: {SCREENSHOT_DIR}",
    ]
