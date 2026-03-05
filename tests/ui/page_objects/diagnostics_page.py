"""
Diagnostics Page Object for VoiceStudio UI Tests.

Provides automation for the Diagnostics panel including:
- System health monitoring
- Log inspection
- Connection testing
- Engine status
"""

from __future__ import annotations

import time
from typing import TYPE_CHECKING, Optional

from .base_page import BasePage

if TYPE_CHECKING:
    from tests.ui.conftest import WinAppDriverElement


class DiagnosticsPage(BasePage):
    """
    Page object for the Diagnostics panel.

    Handles system diagnostics and debugging features.
    """

    # Element automation IDs (from automation_ids.py)
    TAB_VIEW = "DiagnosticsView_TabView"
    ACTIVE_JOBS_LIST = "DiagnosticsView_ActiveJobsListView"
    LOG_LEVEL_FILTER = "DiagnosticsView_LogLevelFilter"
    LOG_SEARCH_BOX = "DiagnosticsView_LogSearchBox"
    LOGS_LIST = "DiagnosticsView_LogsListView"
    TEST_CONNECTION_BUTTON = "DiagnosticsView_TestConnectionButton"
    REFRESH_ENGINES_BUTTON = "DiagnosticsView_RefreshEnginesButton"

    @property
    def root_automation_id(self) -> str:
        return "DiagnosticsView_Root"

    @property
    def nav_automation_id(self) -> str:
        return "NavDiagnostics"

    # =========================================================================
    # Tab Navigation
    # =========================================================================

    def select_tab(self, tab_name: str) -> bool:
        """
        Select a diagnostics tab.

        Args:
            tab_name: Name of the tab to select.

        Returns:
            True if tab was selected.
        """
        try:
            tab_view = self.find_element(self.TAB_VIEW)
            tabs = tab_view.find_elements("class name", "TabViewItem")
            for tab in tabs:
                if tab_name.lower() in tab.text.lower():
                    tab.click()
                    time.sleep(0.3)
                    return True
        # ALLOWED: bare except - best effort, failure acceptable
        except RuntimeError:
            pass
        return False

    # =========================================================================
    # Active Jobs
    # =========================================================================

    def get_active_jobs(self) -> list[WinAppDriverElement]:
        """Get list of active job elements."""
        try:
            jobs_list = self.find_element(self.ACTIVE_JOBS_LIST)
            return jobs_list.find_elements("class name", "ListViewItem")
        except RuntimeError:
            return []

    def get_active_job_count(self) -> int:
        """Get the number of active jobs."""
        return len(self.get_active_jobs())

    def has_active_jobs(self) -> bool:
        """Check if there are any active jobs."""
        return self.get_active_job_count() > 0

    # =========================================================================
    # Logs
    # =========================================================================

    def set_log_level_filter(self, level: str) -> bool:
        """
        Set the log level filter.

        Args:
            level: Log level (e.g., "Error", "Warning", "Info", "Debug").

        Returns:
            True if filter was set.
        """
        return self.select_combobox_item(self.LOG_LEVEL_FILTER, level)

    def search_logs(self, query: str) -> bool:
        """
        Search logs with a query string.

        Args:
            query: Search query.

        Returns:
            True if search was executed.
        """
        return self.type_text(self.LOG_SEARCH_BOX, query)

    def get_log_entries(self) -> list[WinAppDriverElement]:
        """Get list of visible log entry elements."""
        try:
            logs_list = self.find_element(self.LOGS_LIST)
            return logs_list.find_elements("class name", "ListViewItem")
        except RuntimeError:
            return []

    def get_log_count(self) -> int:
        """Get the number of visible log entries."""
        return len(self.get_log_entries())

    # =========================================================================
    # Connection Testing
    # =========================================================================

    def test_connection(self) -> bool:
        """Click the test connection button."""
        return self.click_with_retry(self.TEST_CONNECTION_BUTTON)

    def refresh_engines(self) -> bool:
        """Click the refresh engines button."""
        return self.click_with_retry(self.REFRESH_ENGINES_BUTTON)

    # =========================================================================
    # Workflow Convenience
    # =========================================================================

    def wait_for_no_active_jobs(self, timeout: float = 60.0) -> bool:
        """
        Wait until there are no active jobs.

        Args:
            timeout: Maximum time to wait.

        Returns:
            True if no active jobs within timeout.
        """
        start = time.time()
        while time.time() - start < timeout:
            if not self.has_active_jobs():
                return True
            time.sleep(1.0)
        return False

    def check_system_health(self) -> dict:
        """
        Perform a system health check.

        Returns:
            Dictionary with health check results.
        """
        if not self.is_loaded():
            self.navigate()

        results = {
            "active_jobs": self.get_active_job_count(),
            "log_entries": self.get_log_count(),
            "connection_test": self.test_connection(),
            "engines_refreshed": self.refresh_engines(),
        }
        return results
