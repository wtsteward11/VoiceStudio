"""
Timeline Page Object for VoiceStudio UI Tests.

Provides automation for the Timeline panel including:
- Track management
- Playback controls
- Timeline navigation
"""

from __future__ import annotations

import time
from typing import TYPE_CHECKING, Optional

from .base_page import BasePage

if TYPE_CHECKING:
    from tests.ui.conftest import WinAppDriverElement


class TimelinePage(BasePage):
    """
    Page object for the Timeline panel.

    Handles audio timeline editing and arrangement.
    """

    # Element automation IDs (from automation_ids.py)
    TRACK_LIST = "TimelineView_TrackList"
    PLAY_BUTTON = "TimelineView_PlayButton"
    STOP_BUTTON = "TimelineView_StopButton"

    @property
    def root_automation_id(self) -> str:
        return "TimelineView_Root"

    @property
    def nav_automation_id(self) -> str:
        return "NavTimeline"

    # =========================================================================
    # Track Management
    # =========================================================================

    def get_tracks(self) -> list[WinAppDriverElement]:
        """Get list of track elements."""
        try:
            track_list = self.find_element(self.TRACK_LIST)
            return track_list.find_elements("class name", "ListViewItem")
        except RuntimeError:
            return []

    def get_track_count(self) -> int:
        """Get the number of tracks."""
        return len(self.get_tracks())

    def has_tracks(self) -> bool:
        """Check if timeline has any tracks."""
        return self.get_track_count() > 0

    def select_track_by_index(self, index: int) -> bool:
        """
        Select a track by its index.

        Args:
            index: Zero-based track index.

        Returns:
            True if track was selected.
        """
        tracks = self.get_tracks()
        if 0 <= index < len(tracks):
            try:
                tracks[index].click()
                time.sleep(0.2)
                return True
            # ALLOWED: bare except - best effort, failure acceptable
            except RuntimeError:
                pass
        return False

    # =========================================================================
    # Playback Controls
    # =========================================================================

    def play(self) -> bool:
        """Click the play button to start playback."""
        return self.click_with_retry(self.PLAY_BUTTON)

    def stop(self) -> bool:
        """Click the stop button to stop playback."""
        return self.click_with_retry(self.STOP_BUTTON)

    def is_playing(self) -> bool:
        """
        Check if timeline is currently playing.

        Returns:
            True if playback is active (stop button visible/enabled).
        """
        try:
            stop_button = self.find_element(self.STOP_BUTTON, timeout=1)
            return stop_button.is_enabled()
        except RuntimeError:
            return False

    # =========================================================================
    # Workflow Convenience
    # =========================================================================

    def play_and_wait(self, duration: float = 5.0) -> bool:
        """
        Start playback and wait for specified duration.

        Args:
            duration: Time to play in seconds.

        Returns:
            True if playback started.
        """
        if self.play():
            time.sleep(duration)
            self.stop()
            return True
        return False

    def wait_for_track_added(self, initial_count: int, timeout: float = 10.0) -> bool:
        """
        Wait for a new track to be added.

        Args:
            initial_count: Track count before add operation.
            timeout: Maximum time to wait.

        Returns:
            True if track count increased.
        """
        start = time.time()
        while time.time() - start < timeout:
            if self.get_track_count() > initial_count:
                return True
            time.sleep(0.5)
        return False

    def clear_all_tracks(self) -> int:
        """
        Attempt to clear all tracks from timeline.

        Returns:
            Number of tracks that were cleared.
        """
        initial_count = self.get_track_count()
        cleared = 0

        for _ in range(initial_count):
            if self.select_track_by_index(0):
                self.driver.press_key("DELETE")
                time.sleep(0.3)
                cleared += 1

        return cleared
