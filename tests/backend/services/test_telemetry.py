"""Tests for TelemetryService — TASK-CP-003."""

import pytest

from backend.services.telemetry import (
    SpanStatus,
    get_telemetry_service,
    reset_telemetry_service,
)


class TestTelemetryService:
    """Test TelemetryService functionality."""

    def setup_method(self):
        """Reset telemetry before each test."""
        reset_telemetry_service()

    def test_get_telemetry_service_singleton(self):
        """Test that get_telemetry_service returns a singleton."""
        t1 = get_telemetry_service()
        t2 = get_telemetry_service()
        assert t1 is t2

    def test_trace_creates_span(self):
        """Test that trace creates a span with correct attributes."""
        telemetry = get_telemetry_service()

        with telemetry.trace("test_operation", {"key": "value"}) as span:
            assert span.name == "test_operation"
            assert span.attributes["key"] == "value"
            assert span.trace_id is not None
            assert span.span_id is not None

        # Span should be ended
        assert span.end_time is not None
        assert span.duration_ms >= 0

    def test_nested_spans_share_trace_id(self):
        """Test that nested spans share the same trace_id."""
        telemetry = get_telemetry_service()

        with telemetry.trace("parent") as parent:
            parent_trace_id = parent.trace_id
            with telemetry.trace("child") as child:
                assert child.trace_id == parent_trace_id
                assert child.parent_span_id == parent.span_id

    def test_span_error_handling(self):
        """Test that span captures errors correctly."""
        telemetry = get_telemetry_service()

        with pytest.raises(ValueError), telemetry.trace("error_op") as span:
            raise ValueError("test error")

        assert span.status == SpanStatus.ERROR
        assert "test error" in span.error

    def test_increment_counter(self):
        """Test counter increment."""
        telemetry = get_telemetry_service()

        telemetry.increment("test_counter", labels={"type": "test"})
        telemetry.increment("test_counter", labels={"type": "test"})
        telemetry.increment("test_counter", 5.0, labels={"type": "test"})

        metrics = telemetry.get_metrics()
        assert "test_counter" in metrics
        assert metrics["test_counter"]["type=test"]["count"] == 3
        assert metrics["test_counter"]["type=test"]["sum"] == 7.0

    def test_observe_histogram(self):
        """Test histogram observation."""
        telemetry = get_telemetry_service()

        values = [0.1, 0.2, 0.3, 0.5, 1.0]
        for v in values:
            telemetry.observe("test_histogram", v)

        metrics = telemetry.get_metrics()
        assert "test_histogram" in metrics
        hist = metrics["test_histogram"]["default"]
        assert hist["count"] == 5
        assert hist["min"] == 0.1
        assert hist["max"] == 1.0

    def test_record_request(self):
        """Test request recording."""
        telemetry = get_telemetry_service()

        telemetry.record_request("GET", "/api/test", 200, 0.05)
        telemetry.record_request("POST", "/api/test", 500, 0.1)

        metrics = telemetry.get_metrics()
        assert "http_requests_total" in metrics
        assert "errors_total" in metrics

    def test_record_engine_operation(self):
        """Test engine operation recording."""
        telemetry = get_telemetry_service()

        telemetry.record_engine_operation("xtts", "synthesize", 1.5, success=True)
        telemetry.record_engine_operation("xtts", "synthesize", 2.0, success=False)

        metrics = telemetry.get_metrics()
        assert "engine_operations_total" in metrics

    def test_get_summary(self):
        """Test summary generation."""
        telemetry = get_telemetry_service()

        with telemetry.trace("op1"):
            pass

        telemetry.increment("counter1")

        summary = telemetry.get_summary()
        assert summary["service"] == "voicestudio.backend"
        assert summary["spans_count"] >= 1
        assert summary["metrics_count"] >= 1

    def test_get_recent_spans(self):
        """Test recent spans retrieval."""
        telemetry = get_telemetry_service()

        for i in range(5):
            with telemetry.trace(f"op_{i}"):
                pass

        spans = telemetry.get_recent_spans(3)
        assert len(spans) == 3
        # Most recent spans should be returned
        assert spans[-1]["name"] == "op_4"

    def test_reset(self):
        """Test reset clears all data."""
        telemetry = get_telemetry_service()

        with telemetry.trace("op"):
            pass
        telemetry.increment("counter")

        telemetry.reset()

        assert telemetry.get_metrics() == {}
        assert telemetry.get_recent_spans() == []
