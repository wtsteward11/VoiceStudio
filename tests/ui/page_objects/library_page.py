"""
Library Page Object.

Provides methods for interacting with the audio library panel.
"""

from __future__ import annotations

import time
from typing import TYPE_CHECKING

from .base_page import BasePage

if TYPE_CHECKING:
    pass


class LibraryPage(BasePage):
    """Page object for the Library panel."""

    # =========================================================================
    # Automation IDs
    # =========================================================================

    @property
    def root_automation_id(self) -> str:
        return "LibraryView_Root"

    @property
    def nav_automation_id(self) -> str:
        return "NavLibrary"

    # Main elements
    FOLDERS_LIST = "LibraryView_FoldersListView"
    FILES_LIST = "LibraryView_FilesListView"
    DRAG_DROP_CANVAS = "LibraryView_DragDropCanvas"

    # Search and filter
    SEARCH_BOX = "LibraryView_SearchBox"
    FILTER_COMBO = "LibraryView_FilterComboBox"
    SORT_COMBO = "LibraryView_SortComboBox"

    # Actions
    ADD_FOLDER_BUTTON = "LibraryView_AddFolderButton"
    REMOVE_FOLDER_BUTTON = "LibraryView_RemoveFolderButton"
    REFRESH_BUTTON = "LibraryView_RefreshButton"
    PLAY_BUTTON = "LibraryView_PlayButton"
    STOP_BUTTON = "LibraryView_StopButton"

    # Details panel
    DETAILS_PANEL = "LibraryView_DetailsPanel"
    FILE_NAME_TEXT = "LibraryView_FileNameText"
    FILE_DURATION_TEXT = "LibraryView_DurationText"
    FILE_FORMAT_TEXT = "LibraryView_FormatText"

    # Status
    STATUS_TEXT = "LibraryView_StatusText"
    FILE_COUNT_TEXT = "LibraryView_FileCountText"

    # =========================================================================
    # Properties
    # =========================================================================

    @property
    def search_text(self) -> str | None:
        """Get the current search box value."""
        return self.get_text(self.SEARCH_BOX)

    @property
    def status_text(self) -> str | None:
        """Get the current status text."""
        return self.get_text(self.STATUS_TEXT)

    @property
    def file_count(self) -> str | None:
        """Get the file count display text."""
        return self.get_text(self.FILE_COUNT_TEXT)

    @property
    def selected_file_name(self) -> str | None:
        """Get the name of the selected file."""
        return self.get_text(self.FILE_NAME_TEXT)

    # =========================================================================
    # Actions
    # =========================================================================

    def search(self, query: str) -> bool:
        """
        Enter a search query.

        Args:
            query: The search query.

        Returns:
            True if successful.
        """
        return self.type_text(self.SEARCH_BOX, query)

    def clear_search(self) -> bool:
        """
        Clear the search box.

        Returns:
            True if successful.
        """
        return self.type_text(self.SEARCH_BOX, "")

    def select_filter(self, filter_name: str) -> bool:
        """
        Select a file filter.

        Args:
            filter_name: Name of the filter to select.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.FILTER_COMBO, filter_name)

    def select_sort(self, sort_option: str) -> bool:
        """
        Select a sort option.

        Args:
            sort_option: Name of the sort option.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.SORT_COMBO, sort_option)

    def click_add_folder(self) -> bool:
        """
        Click the add folder button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.ADD_FOLDER_BUTTON)

    def click_remove_folder(self) -> bool:
        """
        Click the remove folder button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.REMOVE_FOLDER_BUTTON)

    def click_refresh(self) -> bool:
        """
        Click the refresh button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.REFRESH_BUTTON)

    def click_play(self) -> bool:
        """
        Click the play button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.PLAY_BUTTON)

    def click_stop(self) -> bool:
        """
        Click the stop button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.STOP_BUTTON)

    # =========================================================================
    # Workflows
    # =========================================================================

    def search_and_select_first(self, query: str, timeout: float = 5.0) -> bool:
        """
        Search for files and select the first result.

        Args:
            query: Search query.
            timeout: Maximum time to wait for results.

        Returns:
            True if a file was found and selected.
        """
        # Enter search query
        if not self.search(query):
            return False

        # Wait for results
        time.sleep(0.5)

        # Try to find files list
        try:
            files_list = self.find_element(self.FILES_LIST, timeout)
            # Get first item and click it
            items = files_list.find_elements("xpath", ".//ListItem")
            if items:
                items[0].click()
                return True
        # ALLOWED: bare except - best effort, failure acceptable
        except (RuntimeError, AttributeError):
            pass

        return False

    def get_folder_count(self) -> int:
        """
        Get the number of folders in the library.

        Returns:
            Number of folders or 0 if cannot determine.
        """
        try:
            folders_list = self.find_element(self.FOLDERS_LIST, timeout=2)
            items = folders_list.find_elements("xpath", ".//ListItem")
            return len(items)
        except (RuntimeError, AttributeError):
            return 0

    def verify_elements_present(self) -> dict:
        """
        Verify all critical elements are present.

        Returns:
            Dictionary with element names and their presence status.
        """
        elements = {
            "root": self.root_automation_id,
            "folders_list": self.FOLDERS_LIST,
            "search_box": self.SEARCH_BOX,
            "add_folder_button": self.ADD_FOLDER_BUTTON,
            "refresh_button": self.REFRESH_BUTTON,
        }

        return {name: self.element_exists(auto_id) for name, auto_id in elements.items()}

    def play_selected_file(self, wait_for_playback: bool = True) -> bool:
        """
        Play the currently selected file.

        Args:
            wait_for_playback: Whether to wait for playback to start.

        Returns:
            True if playback started.
        """
        if not self.click_play():
            return False

        if wait_for_playback:
            # Wait for stop button to appear (indicates playing)
            time.sleep(0.3)
            return self.element_exists(self.STOP_BUTTON)

        return True
