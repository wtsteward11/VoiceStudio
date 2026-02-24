"""
Unit tests for the ErrorTracker service.

Tests cover:
- Error tracking and categorization
- Error aggregation and deduplication
- Summary statistics
- Error resolution
- Export functionality
"""

import pytest

from backend.services.error_tracker import (
    ErrorCategory,
    ErrorContext,
    ErrorSeverity,
    ErrorTracker,
    get_error_tracker,
    reset_error_tracker,
    track_error,
    track_request,
)


class TestErrorSeverity:
    """Tests for ErrorSeverity enum."""

    def test_severity_values(self):
        """Test all severity values exist."""
        assert ErrorSeverity.DEBUG.value == "debug"
        assert ErrorSeverity.INFO.value == "info"
        assert ErrorSeverity.WARNING.value == "warning"
        assert ErrorSeverity.ERROR.value == "error"
        assert ErrorSeverity.CRITICAL.value == "critical"


class TestErrorCategory:
    """Tests for ErrorCategory enum."""

    def test_category_values(self):
        """Test all category values exist."""
        assert ErrorCategory.VALIDATION.value == "validation"
        assert ErrorCategory.AUTHENTICATION.value == "authentication"
        assert ErrorCategory.NOT_FOUND.value == "not_found"
        assert ErrorCategory.SERVER.value == "server"
        assert ErrorCategory.ENGINE.value == "engine"


class TestErrorContext:
    """Tests for ErrorContext dataclass."""

    def test_create_context(self):
        """Test creating error context."""
        context = ErrorContext(
            request_id="req-123",
            endpoint="/api/voice",
            method="POST",
        )

        assert context.request_id == "req-123"
        assert context.endpoint == "/api/voice"
        assert context.method == "POST"

    def test_default_context(self):
        """Test default context values."""
        context = ErrorContext()

        assert context.request_id is None
        assert context.user_id is None
        assert context.endpoint is None
        assert context.method is None
        assert context.params == {}


class TestErrorTracker:
    """Tests for ErrorTracker class."""

    @pytest.fixture
    def tracker(self):
        """Create a fresh tracker for each test."""
        return ErrorTracker(max_errors=100)

    def test_track_simple_error(self, tracker):
        """Test tracking a simple error."""
        try:
            raise ValueError("Test error")
        except ValueError as e:
            tracked = tracker.track_error(e)

        assert tracked.error_id.startswith("err-")
        assert tracked.exception_type == "ValueError"
        assert tracked.message == "Test error"
        assert tracked.severity == ErrorSeverity.ERROR

    def test_track_error_with_severity(self, tracker):
        """Test tracking error with custom severity."""
        try:
            raise RuntimeError("Warning condition")
        except RuntimeError as e:
            tracked = tracker.track_error(e, severity=ErrorSeverity.WARNING)

        assert tracked.severity == ErrorSeverity.WARNING

    def test_track_error_with_category(self, tracker):
        """Test tracking error with explicit category."""
        try:
            raise KeyError("missing_key")
        except KeyError as e:
            tracked = tracker.track_error(e, category=ErrorCategory.VALIDATION)

        assert tracked.category == ErrorCategory.VALIDATION

    def test_track_error_with_context(self, tracker):
        """Test tracking error with context."""
        context = ErrorContext(
            request_id="req-456",
            endpoint="/api/synthesize",
            method="POST",
        )

        try:
            raise Exception("Processing failed")
        except Exception as e:
            tracked = tracker.track_error(e, context=context)

        assert tracked.context is not None
        assert tracked.context.request_id == "req-456"
        assert tracked.context.endpoint == "/api/synthesize"

    def test_track_error_with_tags(self, tracker):
        """Test tracking error with tags."""
        try:
            raise Exception("Tagged error")
        except Exception as e:
            tracked = tracker.track_error(e, tags=["critical", "urgent"])

        assert "critical" in tracked.tags
        assert "urgent" in tracked.tags

    def test_auto_category_detection_validation(self, tracker):
        """Test auto-detection of validation category."""

        class ValidationError(Exception):
            pass

        try:
            raise ValidationError("Invalid input")
        except ValidationError as e:
            tracked = tracker.track_error(e)

        assert tracked.category == ErrorCategory.VALIDATION

    def test_auto_category_detection_not_found(self, tracker):
        """Test auto-detection of not_found category."""
        try:
            raise FileNotFoundError("File not found")
        except FileNotFoundError as e:
            tracked = tracker.track_error(e)

        assert tracked.category == ErrorCategory.NOT_FOUND

    def test_auto_category_detection_timeout(self, tracker):
        """Test auto-detection of timeout category."""
        try:
            raise TimeoutError("Connection timed out")
        except TimeoutError as e:
            tracked = tracker.track_error(e)

        assert tracked.category == ErrorCategory.TIMEOUT

    def test_fingerprint_deduplication(self, tracker):
        """Test that same errors get same fingerprint."""
        fp1 = fp2 = None

        try:
            raise ValueError("Same error")
        except ValueError as e:
            fp1 = tracker.track_error(e).fingerprint

        try:
            raise ValueError("Same error")
        except ValueError as e:
            fp2 = tracker.track_error(e).fingerprint

        assert fp1 == fp2

    def test_different_errors_different_fingerprints(self, tracker):
        """Test that different errors get different fingerprints."""
        fp1 = fp2 = None

        try:
            raise ValueError("Error A")
        except ValueError as e:
            fp1 = tracker.track_error(e).fingerprint

        try:
            raise ValueError("Error B")
        except ValueError as e:
            fp2 = tracker.track_error(e).fingerprint

        assert fp1 != fp2


class TestErrorAggregation:
    """Tests for error aggregation."""

    @pytest.fixture
    def tracker(self):
        """Create a fresh tracker for each test."""
        return ErrorTracker(max_errors=100)

    def test_aggregate_count(self, tracker):
        """Test that aggregate count increases for same error."""
        for _ in range(5):
            try:
                raise ValueError("Repeated error")
            except ValueError as e:
                tracker.track_error(e)

        aggregates = tracker.get_aggregates()
        assert len(aggregates) == 1
        assert aggregates[0].count == 5

    def test_aggregate_timestamps(self, tracker):
        """Test that aggregate timestamps are updated."""
        first_tracked = None
        last_tracked = None

        try:
            raise ValueError("First occurrence")
        except ValueError as e:
            first_tracked = tracker.track_error(e)

        try:
            raise ValueError("First occurrence")
        except ValueError as e:
            last_tracked = tracker.track_error(e)

        aggregates = tracker.get_aggregates()
        assert aggregates[0].first_seen == first_tracked.timestamp
        assert aggregates[0].last_seen == last_tracked.timestamp

    def test_aggregate_affected_endpoints(self, tracker):
        """Test that affected endpoints are tracked."""
        try:
            raise ValueError("Multi-endpoint error")
        except ValueError as e:
            tracker.track_error(e, context=ErrorContext(endpoint="/api/a"))

        try:
            raise ValueError("Multi-endpoint error")
        except ValueError as e:
            tracker.track_error(e, context=ErrorContext(endpoint="/api/b"))

        aggregates = tracker.get_aggregates()
        assert "/api/a" in aggregates[0].affected_endpoints
        assert "/api/b" in aggregates[0].affected_endpoints

    def test_aggregate_sort_by_count(self, tracker):
        """Test sorting aggregates by count."""
        # Create error A with 3 occurrences
        for _ in range(3):
            try:
                raise ValueError("Error A")
            except ValueError as e:
                tracker.track_error(e)

        # Create error B with 5 occurrences
        for _ in range(5):
            try:
                raise ValueError("Error B")
            except ValueError as e:
                tracker.track_error(e)

        aggregates = tracker.get_aggregates(sort_by="count")
        assert aggregates[0].count == 5  # Error B first
        assert aggregates[1].count == 3  # Error A second


class TestErrorSummary:
    """Tests for error summary."""

    @pytest.fixture
    def tracker(self):
        """Create a fresh tracker for each test."""
        return ErrorTracker(max_errors=100)

    def test_summary_counts(self, tracker):
        """Test summary counts."""
        # Track 3 unique errors
        for i in range(3):
            try:
                raise ValueError(f"Error {i}")
            except ValueError as e:
                tracker.track_error(e)

        summary = tracker.get_summary()
        assert summary.total_errors == 3
        assert summary.unique_errors == 3

    def test_summary_by_severity(self, tracker):
        """Test summary by severity."""
        try:
            raise ValueError("Error")
        except ValueError as e:
            tracker.track_error(e, severity=ErrorSeverity.ERROR)

        try:
            raise ValueError("Warning")
        except ValueError as e:
            tracker.track_error(e, severity=ErrorSeverity.WARNING)

        summary = tracker.get_summary()
        assert summary.errors_by_severity.get("error") == 1
        assert summary.errors_by_severity.get("warning") == 1

    def test_summary_by_category(self, tracker):
        """Test summary by category."""
        try:
            raise ValueError("Validation issue")
        except ValueError as e:
            tracker.track_error(e, category=ErrorCategory.VALIDATION)

        try:
            raise ValueError("Server issue")
        except ValueError as e:
            tracker.track_error(e, category=ErrorCategory.SERVER)

        summary = tracker.get_summary()
        assert summary.errors_by_category.get("validation") == 1
        assert summary.errors_by_category.get("server") == 1

    def test_error_rate_calculation(self, tracker):
        """Test error rate calculation."""
        # Track 10 requests
        for _ in range(10):
            tracker.track_request()

        # Track 2 errors
        for _ in range(2):
            try:
                raise ValueError("Test")
            except ValueError as e:
                tracker.track_error(e)

        summary = tracker.get_summary()
        assert summary.error_rate == 20.0  # 2/10 * 100


class TestErrorResolution:
    """Tests for error resolution."""

    @pytest.fixture
    def tracker(self):
        """Create a fresh tracker for each test."""
        return ErrorTracker(max_errors=100)

    def test_resolve_error(self, tracker):
        """Test resolving an error."""
        try:
            raise ValueError("Resolvable error")
        except ValueError as e:
            tracked = tracker.track_error(e)

        success = tracker.resolve_error(tracked.error_id, "Fixed by update")
        assert success is True

        errors = tracker.get_errors()
        assert errors[0].resolved is True
        assert errors[0].resolution_notes == "Fixed by update"

    def test_resolve_nonexistent_error(self, tracker):
        """Test resolving non-existent error."""
        success = tracker.resolve_error("err-999999")
        assert success is False

    def test_clear_resolved(self, tracker):
        """Test clearing resolved errors."""
        # Track 3 errors
        for i in range(3):
            try:
                raise ValueError(f"Error {i}")
            except ValueError as e:
                tracked = tracker.track_error(e)
                if i < 2:  # Resolve first 2
                    tracker.resolve_error(tracked.error_id)

        removed = tracker.clear_resolved()
        assert removed == 2

        errors = tracker.get_errors()
        assert len(errors) == 1


class TestGlobalFunctions:
    """Tests for global functions."""

    def setup_method(self):
        """Reset tracker before each test."""
        reset_error_tracker()

    def teardown_method(self):
        """Reset tracker after each test."""
        reset_error_tracker()

    def test_get_error_tracker(self):
        """Test getting global tracker."""
        tracker = get_error_tracker()
        assert tracker is not None

        # Same instance
        tracker2 = get_error_tracker()
        assert tracker is tracker2

    def test_track_error_function(self):
        """Test track_error convenience function."""
        try:
            raise ValueError("Global tracking")
        except ValueError as e:
            tracked = track_error(e)

        assert tracked.error_id.startswith("err-")

    def test_track_request_function(self):
        """Test track_request convenience function."""
        track_request()

        tracker = get_error_tracker()
        summary = tracker.get_summary()
        # Error rate should be 0 if no errors tracked
        assert summary.error_rate == 0.0


class TestMaxErrors:
    """Tests for max errors limit."""

    def test_trim_on_overflow(self):
        """Test that old errors are trimmed when limit is reached."""
        tracker = ErrorTracker(max_errors=5)

        # Track 10 errors
        for i in range(10):
            try:
                raise ValueError(f"Error {i}")
            except ValueError as e:
                tracker.track_error(e)

        errors = tracker.get_errors(limit=100)
        assert len(errors) == 5

        # Should have kept most recent
        messages = [e.message for e in errors]
        assert "Error 5" in messages
        assert "Error 9" in messages
        assert "Error 0" not in messages


class TestExport:
    """Tests for error export."""

    @pytest.fixture
    def tracker(self, tmp_path):
        """Create a fresh tracker with temp directory."""
        return ErrorTracker(max_errors=100, output_dir=tmp_path)

    def test_export_report(self, tracker, tmp_path):
        """Test exporting error report."""
        try:
            raise ValueError("Export test")
        except ValueError as e:
            tracker.track_error(e)

        filepath = tracker.export_report()

        assert filepath.exists()
        assert filepath.suffix == ".json"

    def test_export_with_custom_filename(self, tracker, tmp_path):
        """Test exporting with custom filename."""
        try:
            raise ValueError("Export test")
        except ValueError as e:
            tracker.track_error(e)

        filepath = tracker.export_report(filename="custom_report.json")

        assert filepath.name == "custom_report.json"
        assert filepath.exists()
