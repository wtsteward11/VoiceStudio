"""Tests for baseline dependency validation at startup."""

from unittest.mock import patch

from backend.api.lifecycle import BASELINE_DEPS, validate_baseline_deps


class TestValidateBaselineDeps:
    """Tests for the validate_baseline_deps function."""

    def test_all_baseline_deps_importable_in_current_env(self):
        """All baseline deps must import in the dev/test environment."""
        valid, failures = validate_baseline_deps()
        assert valid is True, f"Baseline deps failed in test env: {failures}"
        assert failures == []

    def test_baseline_deps_list_is_not_empty(self):
        """BASELINE_DEPS must contain entries — never silently empty."""
        assert len(BASELINE_DEPS) >= 4

    def test_reports_failure_for_missing_module(self):
        """If a baseline dep cannot be imported, it is reported."""
        with patch(
            "backend.api.lifecycle.BASELINE_DEPS",
            [("nonexistent_module_xyz", "Test missing")],
        ):
            valid, failures = validate_baseline_deps()
            assert valid is False
            assert len(failures) == 1
            assert failures[0]["name"] == "nonexistent_module_xyz"
            assert "error" in failures[0]

    def test_partial_failure_reports_only_missing(self):
        """Mixed list: only the missing dep should appear in failures."""
        with patch(
            "backend.api.lifecycle.BASELINE_DEPS",
            [
                ("fastapi", "HTTP framework"),
                ("nonexistent_module_abc", "Does not exist"),
            ],
        ):
            valid, failures = validate_baseline_deps()
            assert valid is False
            assert len(failures) == 1
            assert failures[0]["name"] == "nonexistent_module_abc"

    def test_all_pass_returns_empty_failures(self):
        """When every dep imports, failures list is empty."""
        with patch(
            "backend.api.lifecycle.BASELINE_DEPS",
            [("sys", "Built-in"), ("os", "Built-in")],
        ):
            valid, failures = validate_baseline_deps()
            assert valid is True
            assert failures == []
