"""
Backend Telemetry Service — TASK-CP-003

Provides structured logging, request tracing, and metrics collection for the
VoiceStudio backend. Designed to be local-first and free (no external deps).

Architecture:
- Structured JSON logging via Python's logging module
- Span-like context managers for operation tracing
- In-memory metrics with periodic summary
- Compatible with OpenTelemetry concepts for future migration

See ADR-013 for tracing architecture decisions.
"""

from __future__ import annotations

import json
import logging
import threading
import time
import uuid
from collections import defaultdict
from collections.abc import Generator
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)


# ============================================================================
# Structured Logging
# ============================================================================


class JSONFormatter(logging.Formatter):
    """JSON log formatter for structured logging."""

    def format(self, record: logging.LogRecord) -> str:
        log_data = {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "module": record.module,
            "function": record.funcName,
            "line": record.lineno,
        }

        # Add exception info if present
        if record.exc_info:
            log_data["exception"] = self.formatException(record.exc_info)

        # Add extra fields (span context, etc.)
        for key in ("trace_id", "span_id", "operation", "duration_ms", "status"):
            if hasattr(record, key):
                log_data[key] = getattr(record, key)

        return json.dumps(log_data, default=str)


def setup_json_logging(level: int = logging.INFO) -> None:
    """Configure root logger with JSON formatter."""
    handler = logging.StreamHandler()
    handler.setFormatter(JSONFormatter())
    root = logging.getLogger()
    root.handlers = [handler]
    root.setLevel(level)


# ============================================================================
# Span / Tracing
# ============================================================================


class SpanStatus(str, Enum):
    """Span completion status."""

    OK = "ok"
    ERROR = "error"
    CANCELLED = "cancelled"


@dataclass
class Span:
    """Represents a traced operation span."""

    trace_id: str
    span_id: str
    name: str
    start_time: float = field(default_factory=time.time)  # Unix timestamp
    end_time: float | None = None
    status: SpanStatus = SpanStatus.OK
    attributes: dict[str, Any] = field(default_factory=dict)
    error: str | None = None
    parent_span_id: str | None = None
    # Internal perf counter for accurate duration measurement
    _perf_start: float = field(default_factory=time.perf_counter, repr=False)
    _perf_end: float | None = field(default=None, repr=False)

    @property
    def duration_ms(self) -> float:
        if self._perf_end is None:
            return (time.perf_counter() - self._perf_start) * 1000
        return (self._perf_end - self._perf_start) * 1000

    def set_attribute(self, key: str, value: Any) -> None:
        self.attributes[key] = value

    def set_status(self, status: SpanStatus, error: str | None = None) -> None:
        self.status = status
        self.error = error

    def end(self) -> None:
        self._perf_end = time.perf_counter()
        self.end_time = time.time()

    def to_dict(self) -> dict[str, Any]:
        return {
            "trace_id": self.trace_id,
            "span_id": self.span_id,
            "parent_span_id": self.parent_span_id,
            "name": self.name,
            "start_time": self.start_time,
            "end_time": self.end_time,
            "duration_ms": self.duration_ms,
            "status": self.status.value,
            "attributes": self.attributes,
            "error": self.error,
        }


# Thread-local storage for current span context
_span_context = threading.local()


def get_current_span() -> Span | None:
    """Get the current active span (if any)."""
    return getattr(_span_context, "span", None)


def _set_current_span(span: Span | None) -> None:
    _span_context.span = span


# ============================================================================
# Metrics
# ============================================================================


class MetricType(str, Enum):
    """Metric types."""

    COUNTER = "counter"
    HISTOGRAM = "histogram"
    GAUGE = "gauge"


@dataclass
class MetricValue:
    """Container for metric values."""

    count: int = 0
    sum: float = 0.0
    min: float = float("inf")
    max: float = float("-inf")
    values: list[float] = field(default_factory=list)  # For histogram buckets

    def record(self, value: float = 1.0) -> None:
        self.count += 1
        self.sum += value
        self.min = min(self.min, value)
        self.max = max(self.max, value)
        # Keep last 1000 values for percentile calculation
        if len(self.values) < 1000:
            self.values.append(value)
        else:
            self.values.pop(0)
            self.values.append(value)

    @property
    def avg(self) -> float:
        return self.sum / self.count if self.count > 0 else 0.0

    def percentile(self, p: float) -> float:
        if not self.values:
            return 0.0
        sorted_vals = sorted(self.values)
        idx = int(len(sorted_vals) * p / 100)
        return sorted_vals[min(idx, len(sorted_vals) - 1)]

    def to_dict(self) -> dict[str, Any]:
        return {
            "count": self.count,
            "sum": self.sum,
            "min": self.min if self.min != float("inf") else None,
            "max": self.max if self.max != float("-inf") else None,
            "avg": self.avg,
            "p50": self.percentile(50),
            "p95": self.percentile(95),
            "p99": self.percentile(99),
        }


# ============================================================================
# Telemetry Service
# ============================================================================


class TelemetryService:
    """
    Centralized telemetry for VoiceStudio backend.

    Provides:
    - Structured JSON logging
    - Span-based operation tracing
    - In-memory metrics collection
    - Request/response instrumentation

    Local-first: No external dependencies; all data stored in-memory.
    """

    def __init__(self, service_name: str = "voicestudio.backend"):
        self.service_name = service_name
        self._metrics: dict[str, dict[str, MetricValue]] = defaultdict(
            lambda: defaultdict(MetricValue)
        )
        self._spans: list[Span] = []
        self._max_spans = 1000  # Keep last N spans
        self._lock = threading.Lock()

        # Pre-defined metrics
        self._request_counter = "http_requests_total"
        self._request_duration = "http_request_duration_seconds"
        self._engine_operations = "engine_operations_total"
        self._errors_total = "errors_total"

        logger.info(f"TelemetryService initialized: {service_name}")

    # ----- Tracing -----

    @contextmanager
    def trace(
        self,
        name: str,
        attributes: dict[str, Any] | None = None,
    ) -> Generator[Span, None, None]:
        """
        Context manager for tracing an operation.

        Example:
            with telemetry.trace("synthesize", {"engine": "xtts"}) as span:
                result = engine.synthesize(text)
                span.set_attribute("output_size", len(result))
        """
        parent = get_current_span()
        trace_id = parent.trace_id if parent else uuid.uuid4().hex[:16]
        span_id = uuid.uuid4().hex[:8]

        span = Span(
            trace_id=trace_id,
            span_id=span_id,
            name=name,
            parent_span_id=parent.span_id if parent else None,
            attributes=attributes or {},
        )

        _set_current_span(span)
        try:
            yield span
        except Exception as e:
            span.set_status(SpanStatus.ERROR, str(e))
            raise
        finally:
            span.end()
            _set_current_span(parent)
            self._record_span(span)

    def _record_span(self, span: Span) -> None:
        with self._lock:
            self._spans.append(span)
            if len(self._spans) > self._max_spans:
                self._spans.pop(0)

        # Log span completion
        extra = {
            "trace_id": span.trace_id,
            "span_id": span.span_id,
            "operation": span.name,
            "duration_ms": span.duration_ms,
            "status": span.status.value,
        }
        log_record = logging.LogRecord(
            name=__name__,
            level=logging.DEBUG,
            pathname=__file__,
            lineno=0,
            msg=f"Span completed: {span.name}",
            args=(),
            exc_info=None,
        )
        for k, v in extra.items():
            setattr(log_record, k, v)
        logger.handle(log_record)

    def get_recent_spans(self, limit: int = 100) -> list[Span]:
        """Get recent spans as Span objects."""
        with self._lock:
            return list(self._spans[-limit:])

    def get_recent_spans_dict(self, limit: int = 100) -> list[dict[str, Any]]:
        """Get recent spans as dictionaries for debugging/API."""
        with self._lock:
            return [s.to_dict() for s in self._spans[-limit:]]

    # ----- Metrics -----

    def increment(
        self,
        name: str,
        value: float = 1.0,
        labels: dict[str, str] | None = None,
    ) -> None:
        """Increment a counter metric."""
        label_key = self._label_key(labels)
        with self._lock:
            self._metrics[name][label_key].record(value)

    def observe(
        self,
        name: str,
        value: float,
        labels: dict[str, str] | None = None,
    ) -> None:
        """Record a histogram observation."""
        label_key = self._label_key(labels)
        with self._lock:
            self._metrics[name][label_key].record(value)

    def _label_key(self, labels: dict[str, str] | None) -> str:
        if not labels:
            return ""
        return ",".join(f"{k}={v}" for k, v in sorted(labels.items()))

    def get_metrics(self) -> dict[str, Any]:
        """Get all metrics as a dictionary."""
        with self._lock:
            result = {}
            for name, label_values in self._metrics.items():
                result[name] = {}
                for label_key, value in label_values.items():
                    result[name][label_key or "default"] = value.to_dict()
            return result

    # ----- Convenience Methods -----

    def record_request(
        self,
        method: str,
        path: str,
        status_code: int,
        duration_seconds: float,
    ) -> None:
        """Record an HTTP request for metrics."""
        labels = {"method": method, "path": path, "status": str(status_code)}
        self.increment(self._request_counter, labels=labels)
        self.observe(self._request_duration, duration_seconds, labels=labels)

        if status_code >= 400:
            error_type = "client_error" if status_code < 500 else "server_error"
            self.increment(self._errors_total, labels={"type": error_type, "path": path})

    def record_engine_operation(
        self,
        engine_id: str,
        operation: str,
        duration_seconds: float,
        success: bool,
    ) -> None:
        """Record an engine operation for metrics."""
        labels = {
            "engine": engine_id,
            "operation": operation,
            "status": "success" if success else "error",
        }
        self.increment(self._engine_operations, labels=labels)
        self.observe(
            "engine_operation_duration_seconds",
            duration_seconds,
            labels={"engine": engine_id, "operation": operation},
        )

    def record_error(self, error_type: str, path: str | None = None) -> None:
        """Record an error occurrence."""
        labels = {"type": error_type}
        if path:
            labels["path"] = path
        self.increment(self._errors_total, labels=labels)

    # ----- Summary -----

    def get_summary(self) -> dict[str, Any]:
        """Get a summary of telemetry data."""
        metrics = self.get_metrics()
        spans = self.get_recent_spans(10)

        return {
            "service": self.service_name,
            "metrics_count": sum(len(v) for v in metrics.values()),
            "spans_count": len(self._spans),
            "recent_spans": spans,
            "metrics": metrics,
        }

    def reset(self) -> None:
        """Reset all metrics and spans (for testing)."""
        with self._lock:
            self._metrics.clear()
            self._spans.clear()


# ============================================================================
# Global Instance
# ============================================================================

_telemetry_service: TelemetryService | None = None


def get_telemetry_service() -> TelemetryService:
    """Get the global telemetry service instance."""
    global _telemetry_service
    if _telemetry_service is None:
        _telemetry_service = TelemetryService()
    return _telemetry_service


def reset_telemetry_service() -> None:
    """Reset the global telemetry service (for testing)."""
    global _telemetry_service
    if _telemetry_service:
        _telemetry_service.reset()
    _telemetry_service = None
