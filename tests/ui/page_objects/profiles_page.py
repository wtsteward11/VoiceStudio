"""
Profiles Page Object for VoiceStudio UI Tests.

Provides automation for the Profiles panel including:
- Voice profile listing
- Profile creation
- Profile import/export
"""

from __future__ import annotations

import time
from typing import TYPE_CHECKING, Optional

from .base_page import BasePage

if TYPE_CHECKING:
    from tests.ui.conftest import WinAppDriverElement


class ProfilesPage(BasePage):
    """
    Page object for the Profiles panel.

    Handles voice profile management.
    """

    # Element automation IDs (from automation_ids.py)
    PROFILE_LIST = "ProfilesView_ProfileList"
    ADD_PROFILE_BUTTON = "ProfilesView_AddProfileButton"
    IMPORT_BUTTON = "ProfilesView_ImportButton"

    @property
    def root_automation_id(self) -> str:
        return "ProfilesView_Root"

    @property
    def nav_automation_id(self) -> str:
        return "NavProfiles"

    # =========================================================================
    # Profile Listing
    # =========================================================================

    def get_profile_list_items(self) -> list[WinAppDriverElement]:
        """Get list of profile elements."""
        try:
            profile_list = self.find_element(self.PROFILE_LIST)
            return profile_list.find_elements("class name", "ListViewItem")
        except RuntimeError:
            return []

    def get_profile_count(self) -> int:
        """Get the number of profiles in the list."""
        return len(self.get_profile_list_items())

    def select_profile_by_index(self, index: int) -> bool:
        """
        Select a profile by its index in the list.

        Args:
            index: Zero-based index of the profile.

        Returns:
            True if profile was selected.
        """
        profiles = self.get_profile_list_items()
        if 0 <= index < len(profiles):
            try:
                profiles[index].click()
                time.sleep(0.2)
                return True
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass
        return False

    def select_profile_by_name(self, name: str) -> bool:
        """
        Select a profile by name.

        Args:
            name: Name of the profile to select.

        Returns:
            True if profile was found and selected.
        """
        profiles = self.get_profile_list_items()
        for profile in profiles:
            try:
                if name.lower() in profile.text.lower():
                    profile.click()
                    time.sleep(0.2)
                    return True
            except RuntimeError:
                continue
        return False

    def get_profile_names(self) -> list[str]:
        """Get list of all profile names."""
        profiles = self.get_profile_list_items()
        names = []
        for profile in profiles:
            try:
                names.append(profile.text)
            except RuntimeError:
                continue
        return names

    # =========================================================================
    # Profile Actions
    # =========================================================================

    def click_add_profile(self) -> bool:
        """Click the add profile button."""
        return self.click_with_retry(self.ADD_PROFILE_BUTTON)

    def click_import(self) -> bool:
        """Click the import button."""
        return self.click_with_retry(self.IMPORT_BUTTON)

    # =========================================================================
    # Workflow Convenience
    # =========================================================================

    def profile_exists(self, name: str) -> bool:
        """
        Check if a profile with the given name exists.

        Args:
            name: Profile name to search for.

        Returns:
            True if profile exists.
        """
        names = self.get_profile_names()
        return any(name.lower() in n.lower() for n in names)

    def wait_for_profile(self, name: str, timeout: float = 10.0) -> bool:
        """
        Wait for a profile to appear in the list.

        Args:
            name: Profile name to wait for.
            timeout: Maximum time to wait.

        Returns:
            True if profile appeared within timeout.
        """
        start = time.time()
        while time.time() - start < timeout:
            if self.profile_exists(name):
                return True
            time.sleep(0.5)
        return False
