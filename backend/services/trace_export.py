"""
Trace Export Service — Phase 5.1

Provides trace export and analysis capabilities for the VoiceStudio backend.
Supports exporting traces to JSON files for analysis and debugging.

Features:
- Export traces to JSON files
- Trace analysis and aggregation
- Performance summary generation
- Trace filtering and search

Local-first: All data stored locally, no external dependencies.
"""

from __future__ import annotations

import json
import logging
from collections import defaultdict
from collections.abc import Callable
from dataclasses import asdict, dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from .telemetry import Span, SpanStatus, TelemetryService, get_telemetry_service

logger = logging.getLogger(__name__)

# Default export directory
DEFAULT_EXPORT_DIR = Path(".buildlogs/traces")


# =============================================================================
# Trace Export Models
# =============================================================================


@dataclass
class TraceExport:
    """Represents an exported trace."""

    trace_id: str
    service_name: str
    spans: list[dict[str, Any]]
    start_time: str
    end_time: str | None
    duration_ms: float
    span_count: int
    root_span: str | None = None
    status: str = "ok"
    error_count: int = 0


@dataclass
class TraceSummary:
    """Summary statistics for a collection of traces."""

    total_traces: int = 0
    total_spans: int = 0
    avg_duration_ms: float = 0.0
    min_duration_ms: float = 0.0
    max_duration_ms: float = 0.0
    p50_duration_ms: float = 0.0
    p95_duration_ms: float = 0.0
    p99_duration_ms: float = 0.0
    error_rate: float = 0.0
    operations: dict[str, int] = field(default_factory=dict)
    status_counts: dict[str, int] = field(default_factory=dict)


# =============================================================================
# Trace Exporter
# =============================================================================


class TraceExporter:
    """
    Exports traces from TelemetryService to various formats.

    Usage:
        exporter = TraceExporter(telemetry_service)
        exporter.export_to_json("traces_2026-02-04.json")
    """

    def __init__(
        self,
        telemetry: TelemetryService | None = None,
        export_dir: Path | None = None,
    ):
        """
        Initialize trace exporter.

        Args:
            telemetry: TelemetryService instance
            export_dir: Directory for exported files
        """
        self._telemetry = telemetry
        self.export_dir = export_dir or DEFAULT_EXPORT_DIR
        self.export_dir.mkdir(parents=True, exist_ok=True)

    @property
    def telemetry(self) -> TelemetryService:
        if self._telemetry is None:
            self._telemetry = get_telemetry_service()
        return self._telemetry

    def get_traces(
        self,
        limit: int | None = None,
        filter_fn: Callable[[Span], bool] | None = None,
    ) -> list[Span]:
        """
        Get traces from telemetry service.

        Args:
            limit: Maximum number of spans to retrieve
            filter_fn: Optional filter function

        Returns:
            List of spans
        """
        spans = self.telemetry.get_recent_spans(limit or 1000)

        if filter_fn:
            spans = [s for s in spans if filter_fn(s)]

        return spans

    def group_by_trace(self, spans: list[Span]) -> dict[str, list[Span]]:
        """Group spans by trace_id."""
        grouped: dict[str, list[Span]] = defaultdict(list)
        for span in spans:
            grouped[span.trace_id].append(span)
        return dict(grouped)

    def create_trace_export(
        self,
        trace_id: str,
        spans: list[Span],
    ) -> TraceExport:
        """Create a TraceExport from spans."""
        if not spans:
            return TraceExport(
                trace_id=trace_id,
                service_name=self.telemetry.service_name,
                spans=[],
                start_time=datetime.utcnow().isoformat(),
                end_time=None,
                duration_ms=0,
                span_count=0,
            )

        # Sort by start time
        sorted_spans = sorted(spans, key=lambda s: s.start_time)

        # Calculate total duration
        start_time = min(s.start_time for s in spans)
        end_times = [s.end_time for s in spans if s.end_time is not None]
        end_time = max(end_times) if end_times else None

        duration_ms = (end_time - start_time) * 1000 if end_time else 0

        # Find root span (no parent)
        root_span = next((s for s in spans if s.parent_span_id is None), None)

        # Count errors
        error_count = sum(1 for s in spans if s.status == SpanStatus.ERROR)

        # Determine overall status
        status = "error" if error_count > 0 else "ok"

        return TraceExport(
            trace_id=trace_id,
            service_name=self.telemetry.service_name,
            spans=[s.to_dict() for s in sorted_spans],
            start_time=datetime.fromtimestamp(start_time).isoformat() if start_time else "",
            end_time=datetime.fromtimestamp(end_time).isoformat() if end_time else None,
            duration_ms=duration_ms,
            span_count=len(spans),
            root_span=root_span.name if root_span else None,
            status=status,
            error_count=error_count,
        )

    def export_to_json(
        self,
        filename: str | None = None,
        limit: int | None = None,
        filter_fn: Callable[[Span], bool] | None = None,
        include_summary: bool = True,
    ) -> Path:
        """
        Export traces to JSON file.

        Args:
            filename: Output filename (auto-generated if None)
            limit: Maximum spans to export
            filter_fn: Optional filter function
            include_summary: Include summary statistics

        Returns:
            Path to exported file
        """
        spans = self.get_traces(limit, filter_fn)
        grouped = self.group_by_trace(spans)

        traces = []
        for trace_id, trace_spans in grouped.items():
            traces.append(asdict(self.create_trace_export(trace_id, trace_spans)))

        export_data = {
            "exported_at": datetime.utcnow().isoformat() + "Z",
            "service_name": self.telemetry.service_name,
            "trace_count": len(traces),
            "span_count": len(spans),
            "traces": traces,
        }

        if include_summary:
            summary = self.calculate_summary(spans)
            export_data["summary"] = asdict(summary)

        # Generate filename
        if filename is None:
            timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"traces_{timestamp}.json"

        filepath = self.export_dir / filename

        with open(filepath, "w") as f:
            json.dump(export_data, f, indent=2, default=str)

        logger.info(f"Exported {len(traces)} traces to {filepath}")
        return filepath

    def calculate_summary(self, spans: list[Span]) -> TraceSummary:
        """Calculate summary statistics for spans."""
        if not spans:
            return TraceSummary()

        # Group by trace for trace count
        grouped = self.group_by_trace(spans)

        # Calculate durations
        durations = [s.duration_ms for s in spans]
        sorted_durations = sorted(durations)

        # Operation counts
        operations: dict[str, int] = defaultdict(int)
        status_counts: dict[str, int] = defaultdict(int)

        for span in spans:
            operations[span.name] += 1
            status_counts[span.status.value] += 1

        error_count = status_counts.get("error", 0)

        return TraceSummary(
            total_traces=len(grouped),
            total_spans=len(spans),
            avg_duration_ms=sum(durations) / len(durations),
            min_duration_ms=min(durations),
            max_duration_ms=max(durations),
            p50_duration_ms=sorted_durations[len(sorted_durations) // 2],
            p95_duration_ms=sorted_durations[int(len(sorted_durations) * 0.95)],
            p99_duration_ms=sorted_durations[int(len(sorted_durations) * 0.99)],
            error_rate=error_count / len(spans) * 100 if spans else 0,
            operations=dict(operations),
            status_counts=dict(status_counts),
        )


# =============================================================================
# Trace Analyzer
# =============================================================================


class TraceAnalyzer:
    """
    Analyzes traces for performance insights and issues.

    Usage:
        analyzer = TraceAnalyzer(exporter)
        issues = analyzer.find_slow_spans(threshold_ms=1000)
    """

    def __init__(self, exporter: TraceExporter | None = None):
        self.exporter = exporter or TraceExporter()

    def find_slow_spans(
        self,
        threshold_ms: float = 1000,
        limit: int | None = None,
    ) -> list[Span]:
        """Find spans slower than threshold."""
        spans = self.exporter.get_traces(limit)
        return [s for s in spans if s.duration_ms > threshold_ms]

    def find_errors(self, limit: int | None = None) -> list[Span]:
        """Find spans with errors."""
        spans = self.exporter.get_traces(limit)
        return [s for s in spans if s.status == SpanStatus.ERROR]

    def get_operation_stats(
        self,
        limit: int | None = None,
    ) -> dict[str, dict[str, Any]]:
        """Get statistics per operation."""
        spans = self.exporter.get_traces(limit)

        stats: dict[str, dict[str, Any]] = defaultdict(
            lambda: {"count": 0, "durations": [], "errors": 0}
        )

        for span in spans:
            stats[span.name]["count"] += 1
            stats[span.name]["durations"].append(span.duration_ms)
            if span.status == SpanStatus.ERROR:
                stats[span.name]["errors"] += 1

        # Calculate aggregates
        result = {}
        for name, data in stats.items():
            durations = sorted(data["durations"])
            result[name] = {
                "count": data["count"],
                "avg_ms": sum(durations) / len(durations) if durations else 0,
                "p50_ms": durations[len(durations) // 2] if durations else 0,
                "p95_ms": durations[int(len(durations) * 0.95)] if durations else 0,
                "error_rate": data["errors"] / data["count"] * 100 if data["count"] else 0,
            }

        return result

    def get_trace_tree(self, trace_id: str) -> dict[str, Any]:
        """Build a tree structure for a trace."""
        spans = self.exporter.get_traces(filter_fn=lambda s: s.trace_id == trace_id)

        if not spans:
            return {}

        # Build span lookup
        {s.span_id: s for s in spans}

        # Find root span
        root = next((s for s in spans if s.parent_span_id is None), None)
        if not root:
            root = spans[0]

        def build_tree(span: Span) -> dict[str, Any]:
            children = [s for s in spans if s.parent_span_id == span.span_id]
            return {
                "span_id": span.span_id,
                "name": span.name,
                "duration_ms": span.duration_ms,
                "status": span.status.value,
                "children": [build_tree(c) for c in children],
            }

        return build_tree(root)


# =============================================================================
# Factory Functions
# =============================================================================

_exporter: TraceExporter | None = None
_analyzer: TraceAnalyzer | None = None


def get_trace_exporter() -> TraceExporter:
    """Get the global trace exporter."""
    global _exporter
    if _exporter is None:
        _exporter = TraceExporter()
    return _exporter


def get_trace_analyzer() -> TraceAnalyzer:
    """Get the global trace analyzer."""
    global _analyzer
    if _analyzer is None:
        _analyzer = TraceAnalyzer()
    return _analyzer


def export_traces(
    filename: str | None = None,
    limit: int | None = None,
) -> Path:
    """Convenience function to export traces."""
    return get_trace_exporter().export_to_json(filename, limit)
