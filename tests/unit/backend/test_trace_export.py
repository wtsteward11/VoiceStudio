"""
Unit Tests for Trace Export Service — Phase 5.1

Tests for trace export, analysis, and aggregation functionality.
"""

import json
import sys
import time
from pathlib import Path

import pytest

# Add project paths
PROJECT_ROOT = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))
sys.path.insert(0, str(PROJECT_ROOT / "backend"))

from backend.services.telemetry import (
    Span,
    SpanStatus,
    TelemetryService,
    reset_telemetry_service,
)
from backend.services.trace_export import (
    TraceAnalyzer,
    TraceExport,
    TraceExporter,
    TraceSummary,
    get_trace_analyzer,
    get_trace_exporter,
)


@pytest.fixture
def telemetry():
    """Create a fresh TelemetryService instance."""
    reset_telemetry_service()
    service = TelemetryService("test.service")
    yield service
    reset_telemetry_service()


@pytest.fixture
def sample_spans():
    """Create sample spans for testing."""
    now = time.time()
    return [
        Span(
            trace_id="trace1",
            span_id="span1",
            name="http_request",
            start_time=now - 1.0,
            end_time=now - 0.5,
            status=SpanStatus.OK,
            _perf_start=0,
            _perf_end=0.5,
        ),
        Span(
            trace_id="trace1",
            span_id="span2",
            name="db_query",
            start_time=now - 0.8,
            end_time=now - 0.6,
            status=SpanStatus.OK,
            parent_span_id="span1",
            _perf_start=0,
            _perf_end=0.2,
        ),
        Span(
            trace_id="trace2",
            span_id="span3",
            name="http_request",
            start_time=now - 0.3,
            end_time=now - 0.1,
            status=SpanStatus.ERROR,
            error="Connection timeout",
            _perf_start=0,
            _perf_end=0.2,
        ),
    ]


class TestSpan:
    """Test Span dataclass functionality."""

    def test_span_creation(self):
        """Test basic span creation."""
        span = Span(
            trace_id="test-trace",
            span_id="test-span",
            name="test_operation",
        )
        assert span.trace_id == "test-trace"
        assert span.span_id == "test-span"
        assert span.name == "test_operation"
        assert span.status == SpanStatus.OK
        assert span.start_time > 0

    def test_span_duration(self):
        """Test span duration calculation."""
        span = Span(
            trace_id="t1",
            span_id="s1",
            name="test",
            _perf_start=0,
            _perf_end=0.5,
        )
        # Duration should be approximately 500ms
        assert abs(span.duration_ms - 500) < 1

    def test_span_set_attribute(self):
        """Test setting span attributes."""
        span = Span(trace_id="t1", span_id="s1", name="test")
        span.set_attribute("key", "value")
        span.set_attribute("count", 42)
        assert span.attributes["key"] == "value"
        assert span.attributes["count"] == 42

    def test_span_set_status(self):
        """Test setting span status."""
        span = Span(trace_id="t1", span_id="s1", name="test")
        span.set_status(SpanStatus.ERROR, "Something failed")
        assert span.status == SpanStatus.ERROR
        assert span.error == "Something failed"

    def test_span_end(self):
        """Test ending a span."""
        span = Span(trace_id="t1", span_id="s1", name="test")
        assert span.end_time is None
        assert span._perf_end is None
        span.end()
        assert span.end_time is not None
        assert span._perf_end is not None

    def test_span_to_dict(self):
        """Test span serialization."""
        span = Span(
            trace_id="t1",
            span_id="s1",
            name="test",
            parent_span_id="p1",
            status=SpanStatus.ERROR,
            error="Test error",
        )
        span.set_attribute("key", "value")

        d = span.to_dict()
        assert d["trace_id"] == "t1"
        assert d["span_id"] == "s1"
        assert d["parent_span_id"] == "p1"
        assert d["name"] == "test"
        assert d["status"] == "error"
        assert d["error"] == "Test error"
        assert d["attributes"] == {"key": "value"}
        assert "start_time" in d
        assert "duration_ms" in d


class TestTraceExport:
    """Test TraceExport dataclass."""

    def test_trace_export_creation(self):
        """Test TraceExport dataclass."""
        export = TraceExport(
            trace_id="test-trace",
            service_name="test-service",
            spans=[{"name": "span1"}],
            start_time="2026-02-04T12:00:00Z",
            end_time="2026-02-04T12:01:00Z",
            duration_ms=60000,
            span_count=1,
        )
        assert export.trace_id == "test-trace"
        assert export.service_name == "test-service"
        assert len(export.spans) == 1
        assert export.duration_ms == 60000
        assert export.status == "ok"
        assert export.error_count == 0


class TestTraceSummary:
    """Test TraceSummary dataclass."""

    def test_trace_summary_defaults(self):
        """Test TraceSummary default values."""
        summary = TraceSummary()
        assert summary.total_traces == 0
        assert summary.total_spans == 0
        assert summary.avg_duration_ms == 0.0
        assert summary.error_rate == 0.0
        assert summary.operations == {}
        assert summary.status_counts == {}


class TestTraceExporter:
    """Test TraceExporter class."""

    def test_exporter_init(self, telemetry, tmp_path):
        """Test exporter initialization."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        assert exporter.export_dir == tmp_path
        assert exporter.telemetry == telemetry

    def test_get_traces_empty(self, telemetry, tmp_path):
        """Test getting traces when empty."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        spans = exporter.get_traces()
        assert spans == []

    def test_get_traces_with_data(self, telemetry, tmp_path):
        """Test getting traces after recording spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        # Record some spans
        with telemetry.trace("test_operation"):
            pass

        spans = exporter.get_traces()
        assert len(spans) == 1
        assert spans[0].name == "test_operation"

    def test_group_by_trace(self, telemetry, tmp_path, sample_spans):
        """Test grouping spans by trace_id."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        grouped = exporter.group_by_trace(sample_spans)

        assert len(grouped) == 2
        assert "trace1" in grouped
        assert "trace2" in grouped
        assert len(grouped["trace1"]) == 2
        assert len(grouped["trace2"]) == 1

    def test_create_trace_export(self, telemetry, tmp_path, sample_spans):
        """Test creating a TraceExport from spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        # Use spans from trace1
        trace1_spans = [s for s in sample_spans if s.trace_id == "trace1"]
        export = exporter.create_trace_export("trace1", trace1_spans)

        assert export.trace_id == "trace1"
        assert export.span_count == 2
        assert export.status == "ok"
        assert export.error_count == 0

    def test_create_trace_export_with_errors(self, telemetry, tmp_path, sample_spans):
        """Test creating TraceExport with error spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        # Use span from trace2 (which has an error)
        trace2_spans = [s for s in sample_spans if s.trace_id == "trace2"]
        export = exporter.create_trace_export("trace2", trace2_spans)

        assert export.trace_id == "trace2"
        assert export.status == "error"
        assert export.error_count == 1

    def test_export_to_json(self, telemetry, tmp_path):
        """Test exporting traces to JSON file."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        # Record some spans
        with telemetry.trace("test_op1"), telemetry.trace("test_op2"):
            pass

        filepath = exporter.export_to_json("test_export.json")

        assert filepath.exists()
        assert filepath.name == "test_export.json"

        with open(filepath) as f:
            data = json.load(f)

        assert "exported_at" in data
        assert "service_name" in data
        assert "traces" in data
        assert "summary" in data

    def test_calculate_summary(self, telemetry, tmp_path, sample_spans):
        """Test calculating summary statistics."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        summary = exporter.calculate_summary(sample_spans)

        assert summary.total_traces == 2
        assert summary.total_spans == 3
        assert summary.avg_duration_ms > 0
        assert summary.error_rate > 0  # One span has error

    def test_calculate_summary_empty(self, telemetry, tmp_path):
        """Test calculating summary with no spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        summary = exporter.calculate_summary([])

        assert summary.total_traces == 0
        assert summary.total_spans == 0
        assert summary.avg_duration_ms == 0.0


class TestTraceAnalyzer:
    """Test TraceAnalyzer class."""

    def test_analyzer_init(self, telemetry, tmp_path):
        """Test analyzer initialization."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        analyzer = TraceAnalyzer(exporter)
        assert analyzer.exporter == exporter

    def test_find_slow_spans(self, telemetry, tmp_path):
        """Test finding slow spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        analyzer = TraceAnalyzer(exporter)

        # Record spans with different durations
        with telemetry.trace("fast_op"):
            pass  # Very fast

        with telemetry.trace("slow_op"):
            time.sleep(0.1)  # 100ms+

        slow = analyzer.find_slow_spans(threshold_ms=50)
        assert len(slow) >= 1
        assert any(s.name == "slow_op" for s in slow)

    def test_find_errors(self, telemetry, tmp_path):
        """Test finding error spans."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        analyzer = TraceAnalyzer(exporter)

        # Record a successful span
        with telemetry.trace("success_op"):
            pass

        # Record an error span
        try:
            with telemetry.trace("error_op"):
                raise ValueError("Test error")
        # ALLOWED: bare except - Intentional error for test case
        except ValueError:
            pass

        errors = analyzer.find_errors()
        assert len(errors) == 1
        assert errors[0].name == "error_op"
        assert errors[0].status == SpanStatus.ERROR

    def test_get_operation_stats(self, telemetry, tmp_path):
        """Test getting operation statistics."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        analyzer = TraceAnalyzer(exporter)

        # Record multiple operations
        for _ in range(3):
            with telemetry.trace("op_a"):
                pass

        for _ in range(2):
            with telemetry.trace("op_b"):
                pass

        stats = analyzer.get_operation_stats()

        assert "op_a" in stats
        assert "op_b" in stats
        assert stats["op_a"]["count"] == 3
        assert stats["op_b"]["count"] == 2

    def test_get_trace_tree(self, telemetry, tmp_path):
        """Test building trace tree."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)
        analyzer = TraceAnalyzer(exporter)

        # Record nested spans
        with telemetry.trace("root") as root_span:
            trace_id = root_span.trace_id
            with telemetry.trace("child1"):
                pass
            with telemetry.trace("child2"):
                pass

        tree = analyzer.get_trace_tree(trace_id)

        assert tree["name"] == "root"
        assert len(tree["children"]) == 2


class TestGlobalFunctions:
    """Test global factory functions."""

    def test_get_trace_exporter(self):
        """Test getting global exporter."""
        exporter1 = get_trace_exporter()
        exporter2 = get_trace_exporter()
        assert exporter1 is exporter2

    def test_get_trace_analyzer(self):
        """Test getting global analyzer."""
        analyzer1 = get_trace_analyzer()
        analyzer2 = get_trace_analyzer()
        assert analyzer1 is analyzer2


class TestFilterFunctions:
    """Test filter functionality in TraceExporter."""

    def test_filter_by_operation(self, telemetry, tmp_path):
        """Test filtering traces by operation."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        with telemetry.trace("http_request"):
            pass
        with telemetry.trace("db_query"):
            pass
        with telemetry.trace("http_request"):
            pass

        # Filter for http_request only
        http_spans = exporter.get_traces(filter_fn=lambda s: s.name == "http_request")

        assert len(http_spans) == 2
        assert all(s.name == "http_request" for s in http_spans)

    def test_filter_by_status(self, telemetry, tmp_path):
        """Test filtering traces by status."""
        exporter = TraceExporter(telemetry, export_dir=tmp_path)

        with telemetry.trace("success"):
            pass

        try:
            with telemetry.trace("failure"):
                raise RuntimeError("Test")
        # ALLOWED: bare except - Intentional error for test case
        except RuntimeError:
            pass

        # Filter for errors only
        error_spans = exporter.get_traces(filter_fn=lambda s: s.status == SpanStatus.ERROR)

        assert len(error_spans) == 1
        assert error_spans[0].name == "failure"
