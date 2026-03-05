"""
Base Page Object for VoiceStudio UI Tests.

Provides common utilities for all page objects including:
- Element finding with retry logic
- Screenshot capture
- Wait utilities
- Navigation helpers
"""

from __future__ import annotations

import time
from abc import ABC, abstractmethod
from datetime import datetime
from pathlib import Path
from typing import TYPE_CHECKING, Optional

if TYPE_CHECKING:
    from tests.ui.conftest import WinAppDriverElement, WinAppDriverSession


class BasePage(ABC):
    """
    Abstract base class for all page objects.

    Provides common utilities for interacting with UI elements.
    """

    # Default timeouts (seconds)
    DEFAULT_WAIT_TIMEOUT = 10
    DEFAULT_RETRY_COUNT = 3
    DEFAULT_RETRY_DELAY = 0.5

    # Screenshot directory
    SCREENSHOT_DIR = (
        Path(__file__).parent.parent.parent.parent / ".buildlogs" / "ui_tests" / "screenshots"
    )

    def __init__(self, driver: WinAppDriverSession):
        """
        Initialize the page object.

        Args:
            driver: WinAppDriverSession instance.
        """
        self.driver = driver
        self.SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)

    @property
    @abstractmethod
    def root_automation_id(self) -> str:
        """Return the automation ID of the page's root element."""
        pass

    @property
    @abstractmethod
    def nav_automation_id(self) -> str:
        """Return the automation ID of the navigation button for this page."""
        pass

    # =========================================================================
    # Navigation
    # =========================================================================

    def navigate(self, wait_time: float = 0.5) -> bool:
        """
        Navigate to this page using the navigation button.

        Args:
            wait_time: Time to wait after clicking navigation button.

        Returns:
            True if navigation was successful.
        """
        try:
            nav_button = self.find_element(self.nav_automation_id)
            nav_button.click()
            time.sleep(wait_time)
            return self.is_loaded()
        except RuntimeError:
            return False

    def is_loaded(self) -> bool:
        """
        Check if this page is currently loaded.

        Returns:
            True if the root element is visible.
        """
        try:
            root = self.find_element(self.root_automation_id, timeout=2)
            return root.is_displayed()
        except RuntimeError:
            return False

    def wait_until_loaded(self, timeout: float = DEFAULT_WAIT_TIMEOUT) -> bool:
        """
        Wait until this page is loaded.

        Args:
            timeout: Maximum time to wait in seconds.

        Returns:
            True if page loaded within timeout.
        """
        start = time.time()
        while time.time() - start < timeout:
            if self.is_loaded():
                return True
            time.sleep(0.2)
        return False

    # =========================================================================
    # Element Finding
    # =========================================================================

    def find_element(
        self,
        automation_id: str,
        timeout: float = DEFAULT_WAIT_TIMEOUT,
        by: str = "accessibility id",
    ) -> WinAppDriverElement:
        """
        Find an element by automation ID with retry logic.

        Args:
            automation_id: The AutomationId to search for.
            timeout: Maximum time to wait for element.
            by: Locator strategy (default: "accessibility id").

        Returns:
            WinAppDriverElement if found.

        Raises:
            RuntimeError: If element not found within timeout.
        """
        start = time.time()
        last_error = None

        while time.time() - start < timeout:
            try:
                return self.driver.find_element(by, automation_id)
            except RuntimeError as e:
                last_error = e
                time.sleep(0.2)

        raise RuntimeError(f"Element '{automation_id}' not found within {timeout}s") from last_error

    def find_elements(
        self, automation_id: str, by: str = "accessibility id"
    ) -> list[WinAppDriverElement]:
        """
        Find all elements matching the automation ID.

        Args:
            automation_id: The AutomationId to search for.
            by: Locator strategy.

        Returns:
            List of matching elements (may be empty).
        """
        try:
            return self.driver.find_elements(by, automation_id)
        except RuntimeError:
            return []

    def element_exists(self, automation_id: str, by: str = "accessibility id") -> bool:
        """
        Check if an element exists.

        Args:
            automation_id: The AutomationId to search for.
            by: Locator strategy.

        Returns:
            True if element exists.
        """
        try:
            self.driver.find_element(by, automation_id)
            return True
        except RuntimeError:
            return False

    # =========================================================================
    # Wait Utilities
    # =========================================================================

    def wait_for_element(
        self, automation_id: str, timeout: float = DEFAULT_WAIT_TIMEOUT
    ) -> Optional[WinAppDriverElement]:
        """
        Wait for an element to appear.

        Args:
            automation_id: The AutomationId to wait for.
            timeout: Maximum time to wait.

        Returns:
            Element if found, None if timeout.
        """
        try:
            return self.find_element(automation_id, timeout)
        except RuntimeError:
            return None

    def wait_for_element_gone(
        self, automation_id: str, timeout: float = DEFAULT_WAIT_TIMEOUT
    ) -> bool:
        """
        Wait for an element to disappear.

        Args:
            automation_id: The AutomationId to wait for.
            timeout: Maximum time to wait.

        Returns:
            True if element disappeared within timeout.
        """
        start = time.time()
        while time.time() - start < timeout:
            if not self.element_exists(automation_id):
                return True
            time.sleep(0.2)
        return False

    def wait_for_enabled(self, automation_id: str, timeout: float = DEFAULT_WAIT_TIMEOUT) -> bool:
        """
        Wait for an element to become enabled.

        Args:
            automation_id: The AutomationId to wait for.
            timeout: Maximum time to wait.

        Returns:
            True if element became enabled within timeout.
        """
        start = time.time()
        while time.time() - start < timeout:
            try:
                element = self.driver.find_element("accessibility id", automation_id)
                if element.is_enabled():
                    return True
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass
            time.sleep(0.2)
        return False

    # =========================================================================
    # Actions with Retry
    # =========================================================================

    def click_with_retry(
        self,
        automation_id: str,
        retries: int = DEFAULT_RETRY_COUNT,
        delay: float = DEFAULT_RETRY_DELAY,
    ) -> bool:
        """
        Click an element with retry logic.

        Args:
            automation_id: The AutomationId of element to click.
            retries: Number of retry attempts.
            delay: Delay between retries in seconds.

        Returns:
            True if click was successful.
        """
        for attempt in range(retries):
            try:
                element = self.driver.find_element("accessibility id", automation_id)
                element.click()
                return True
            except RuntimeError:
                if attempt < retries - 1:
                    time.sleep(delay)
        return False

    def type_text(self, automation_id: str, text: str, clear_first: bool = True) -> bool:
        """
        Type text into an element.

        Args:
            automation_id: The AutomationId of text input.
            text: Text to type.
            clear_first: Whether to clear existing text first.

        Returns:
            True if successful.
        """
        try:
            element = self.find_element(automation_id)
            if clear_first:
                element.clear()
            element.send_keys(text)
            return True
        except RuntimeError:
            return False

    def get_text(self, automation_id: str) -> str | None:
        """
        Get text from an element.

        Args:
            automation_id: The AutomationId of element.

        Returns:
            Element text or None if not found.
        """
        try:
            element = self.find_element(automation_id)
            return element.text
        except RuntimeError:
            return None

    # =========================================================================
    # Screenshots
    # =========================================================================

    def capture_screenshot(self, name: str | None = None) -> Path | None:
        """
        Capture a screenshot.

        Args:
            name: Optional name for the screenshot file.

        Returns:
            Path to saved screenshot or None if failed.
        """
        try:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            name = name or self.__class__.__name__
            filename = f"{name}_{timestamp}.png"
            filepath = self.SCREENSHOT_DIR / filename

            if self.driver.save_screenshot(str(filepath)):
                return filepath
            return None
        except Exception:
            return None

    # =========================================================================
    # ComboBox Utilities
    # =========================================================================

    def select_combobox_item(
        self, combobox_id: str, item_text: str, timeout: float = DEFAULT_WAIT_TIMEOUT
    ) -> bool:
        """
        Select an item from a ComboBox.

        Args:
            combobox_id: The AutomationId of the ComboBox.
            item_text: The text of the item to select.
            timeout: Maximum time to wait.

        Returns:
            True if selection was successful.
        """
        try:
            # Click to open combobox
            combobox = self.find_element(combobox_id, timeout)
            combobox.click()
            time.sleep(0.3)

            # Find and click the item
            items = self.driver.find_elements("name", item_text)
            for item in items:
                try:
                    item.click()
                    time.sleep(0.2)
                    return True
                except RuntimeError:
                    continue

            # If item not found by name, try pressing Escape to close
            self.driver.press_escape()
            return False

        except RuntimeError:
            return False
