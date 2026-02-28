"""
Error Tracking Service — Phase 5.4

Provides comprehensive error tracking and aggregation for the backend.

Features:
- Error categorization and classification
- Error aggregation and deduplication
- Trend analysis
- Error context capture
- Report generation

Local-first: All data stored locally, no external dependencies.
"""

from __future__ import annotations

import hashlib
import json
import logging
import threading
import traceback
from collections import defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path

logger = logging.getLogger(__name__)

# Default error storage path
DEFAULT_ERROR_DIR = Path(".buildlogs/errors")


# =============================================================================
# Error Models
# =============================================================================


class ErrorSeverity(str, Enum):
    """Error severity levels."""

    DEBUG = "debug"
    INFO = "info"
    WARNING = "warning"
    ERROR = "error"
    CRITICAL = "critical"


class ErrorCategory(str, Enum):
    """Error categories."""

    VALIDATION = "validation"
    AUTHENTICATION = "authentication"
    AUTHORIZATION = "authorization"
    NOT_FOUND = "not_found"
    RATE_LIMIT = "rate_limit"
    SERVER = "server"
    DATABASE = "database"
    EXTERNAL_SERVICE = "external_service"
    ENGINE = "engine"
    TIMEOUT = "timeout"
    UNKNOWN = "unknown"


@dataclass
class ErrorContext:
    """Context information for an error."""

    request_id: str | None = None
    user_id: str | None = None
    endpoint: str | None = None
    method: str | None = None
    params: dict = field(default_factory=dict)
    headers: dict = field(default_factory=dict)


@dataclass
class TrackedError:
    """A tracked error instance."""

    error_id: str
    fingerprint: str  # For deduplication
    timestamp: str
    severity: ErrorSeverity
    category: ErrorCategory
    message: str
    exception_type: str
    stacktrace: str | None = None
    context: ErrorContext | None = None
    tags: list[str] = field(default_factory=list)
    resolved: bool = False
    resolution_notes: str | None = None


@dataclass
class ErrorAggregate:
    """Aggregated error statistics."""

    fingerprint: str
    first_seen: str
    last_seen: str
    count: int
    severity: ErrorSeverity
    category: ErrorCategory
    message: str
    exception_type: str
    sample_stacktrace: str | None = None
    affected_endpoints: list[str] = field(default_factory=list)


@dataclass
class ErrorSummary:
    """Summary of error statistics."""

    total_errors: int = 0
    unique_errors: int = 0
    errors_by_severity: dict[str, int] = field(default_factory=dict)
    errors_by_category: dict[str, int] = field(default_factory=dict)
    top_errors: list[ErrorAggregate] = field(default_factory=list)
    error_rate: float = 0.0


# =============================================================================
# Error Tracker Service
# =============================================================================


class ErrorTracker:
    """
    Tracks and aggregates errors for analysis.

    Usage:
        tracker = ErrorTracker()
        tracker.track_error(exception, context=ErrorContext(endpoint="/api/voice"))
        summary = tracker.get_summary()
    """

    def __init__(
        self,
        max_errors: int = 10000,
        output_dir: Path | None = None,
    ):
        """
        Initialize error tracker.

        Args:
            max_errors: Maximum errors to keep in memory
            output_dir: Directory for error reports
        """
        self.max_errors = max_errors
        self.output_dir = output_dir or DEFAULT_ERROR_DIR
        self.output_dir.mkdir(parents=True, exist_ok=True)

        self._errors: list[TrackedError] = []
        self._aggregates: dict[str, ErrorAggregate] = {}
        self._error_counter = 0
        self._lock = threading.Lock()

        # Metrics
        self._total_requests = 0
        self._error_count = 0

    def track_error(
        self,
        exception: Exception,
        severity: ErrorSeverity = ErrorSeverity.ERROR,
        category: ErrorCategory | None = None,
        context: ErrorContext | None = None,
        tags: list[str] | None = None,
    ) -> TrackedError:
        """
        Track an error occurrence.

        Args:
            exception: The exception to track
            severity: Error severity level
            category: Error category (auto-detected if not provided)
            context: Error context information
            tags: Optional tags for categorization

        Returns:
            The tracked error
        """
        with self._lock:
            self._error_counter += 1
            self._error_count += 1

            # Auto-detect category if not provided
            if category is None:
                category = self._detect_category(exception)

            # Generate fingerprint for deduplication
            fingerprint = self._generate_fingerprint(exception)

            # Get stacktrace
            stacktrace = "".join(
                traceback.format_exception(type(exception), exception, exception.__traceback__)
            )

            # Create tracked error
            error = TrackedError(
                error_id=f"err-{self._error_counter:06d}",
                fingerprint=fingerprint,
                timestamp=datetime.utcnow().isoformat() + "Z",
                severity=severity,
                category=category,
                message=str(exception),
                exception_type=type(exception).__name__,
                stacktrace=stacktrace,
                context=context,
                tags=tags or [],
            )

            # Add to list
            self._errors.append(error)

            # Trim if needed
            if len(self._errors) > self.max_errors:
                self._errors = self._errors[-self.max_errors :]

            # Update aggregate
            self._update_aggregate(error)

            logger.debug(f"Tracked error: {error.error_id} ({error.exception_type})")

            return error

    def track_request(self) -> None:
        """Track a request for error rate calculation."""
        with self._lock:
            self._total_requests += 1

    def _detect_category(self, exception: Exception) -> ErrorCategory:
        """Auto-detect error category from exception type."""
        exc_type = type(exception).__name__.lower()
        exc_message = str(exception).lower()

        if "validation" in exc_type or "validation" in exc_message:
            return ErrorCategory.VALIDATION
        elif "auth" in exc_type or "permission" in exc_type:
            return ErrorCategory.AUTHORIZATION
        elif "notfound" in exc_type or "not found" in exc_message:
            return ErrorCategory.NOT_FOUND
        elif "timeout" in exc_type or "timed out" in exc_message:
            return ErrorCategory.TIMEOUT
        elif "database" in exc_type or "sql" in exc_type:
            return ErrorCategory.DATABASE
        elif "engine" in exc_type:
            return ErrorCategory.ENGINE
        elif "connection" in exc_type or "http" in exc_type:
            return ErrorCategory.EXTERNAL_SERVICE
        else:
            return ErrorCategory.UNKNOWN

    def _generate_fingerprint(self, exception: Exception) -> str:
        """Generate a fingerprint for error deduplication."""
        # Use exception type + message hash
        content = f"{type(exception).__name__}:{exception!s}"
        return hashlib.md5(content.encode()).hexdigest()[:12]

    def _update_aggregate(self, error: TrackedError) -> None:
        """Update error aggregate."""
        fp = error.fingerprint

        if fp in self._aggregates:
            agg = self._aggregates[fp]
            agg.last_seen = error.timestamp
            agg.count += 1

            # Track affected endpoints
            if error.context and error.context.endpoint:
                if error.context.endpoint not in agg.affected_endpoints:
                    agg.affected_endpoints.append(error.context.endpoint)
        else:
            self._aggregates[fp] = ErrorAggregate(
                fingerprint=fp,
                first_seen=error.timestamp,
                last_seen=error.timestamp,
                count=1,
                severity=error.severity,
                category=error.category,
                message=error.message,
                exception_type=error.exception_type,
                sample_stacktrace=error.stacktrace,
                affected_endpoints=(
                    [error.context.endpoint] if error.context and error.context.endpoint else []
                ),
            )

    def get_errors(
        self,
        limit: int = 100,
        severity: ErrorSeverity | None = None,
        category: ErrorCategory | None = None,
    ) -> list[TrackedError]:
        """Get recent errors with optional filtering."""
        with self._lock:
            errors = list(self._errors)

        if severity:
            errors = [e for e in errors if e.severity == severity]

        if category:
            errors = [e for e in errors if e.category == category]

        return sorted(errors, key=lambda e: e.timestamp, reverse=True)[:limit]

    def get_aggregates(
        self,
        limit: int = 50,
        sort_by: str = "count",
    ) -> list[ErrorAggregate]:
        """Get error aggregates sorted by frequency."""
        with self._lock:
            aggregates = list(self._aggregates.values())

        if sort_by == "count":
            aggregates.sort(key=lambda a: a.count, reverse=True)
        elif sort_by == "recent":
            aggregates.sort(key=lambda a: a.last_seen, reverse=True)

        return aggregates[:limit]

    def get_summary(self) -> ErrorSummary:
        """Get error summary statistics."""
        with self._lock:
            errors = list(self._errors)
            aggregates = list(self._aggregates.values())
            total_requests = self._total_requests
            error_count = self._error_count

        # Count by severity
        by_severity: dict[str, int] = defaultdict(int)
        for error in errors:
            by_severity[error.severity.value] += 1

        # Count by category
        by_category: dict[str, int] = defaultdict(int)
        for error in errors:
            by_category[error.category.value] += 1

        # Top errors
        top_errors = sorted(aggregates, key=lambda a: a.count, reverse=True)[:10]

        # Error rate
        error_rate = (error_count / total_requests * 100) if total_requests > 0 else 0.0

        return ErrorSummary(
            total_errors=len(errors),
            unique_errors=len(aggregates),
            errors_by_severity=dict(by_severity),
            errors_by_category=dict(by_category),
            top_errors=top_errors,
            error_rate=error_rate,
        )

    def resolve_error(
        self,
        error_id: str,
        resolution_notes: str = "",
    ) -> bool:
        """Mark an error as resolved."""
        with self._lock:
            for error in self._errors:
                if error.error_id == error_id:
                    error.resolved = True
                    error.resolution_notes = resolution_notes
                    return True
        return False

    def export_report(
        self,
        filename: str | None = None,
        include_stacktraces: bool = False,
    ) -> Path:
        """Export error report to JSON file."""
        timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
        filename = filename or f"error_report_{timestamp}.json"
        filepath = self.output_dir / filename

        summary = self.get_summary()
        recent_errors = self.get_errors(limit=100)

        report = {
            "generated_at": datetime.utcnow().isoformat() + "Z",
            "summary": asdict(summary),
            "recent_errors": [
                {
                    **asdict(e),
                    "stacktrace": (e.stacktrace if include_stacktraces else "[redacted]"),
                    "context": asdict(e.context) if e.context else None,
                }
                for e in recent_errors
            ],
        }

        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, default=str)

        logger.info(f"Error report saved to {filepath}")
        return filepath

    def clear_resolved(self) -> int:
        """Clear resolved errors from tracking."""
        with self._lock:
            original_count = len(self._errors)
            self._errors = [e for e in self._errors if not e.resolved]
            removed = original_count - len(self._errors)
        return removed

    def reset(self) -> None:
        """Reset all error tracking data."""
        with self._lock:
            self._errors.clear()
            self._aggregates.clear()
            self._error_counter = 0
            self._total_requests = 0
            self._error_count = 0


# =============================================================================
# Global Instance
# =============================================================================

_error_tracker: ErrorTracker | None = None


def get_error_tracker() -> ErrorTracker:
    """Get the global error tracker instance."""
    global _error_tracker
    if _error_tracker is None:
        _error_tracker = ErrorTracker()
    return _error_tracker


def reset_error_tracker() -> None:
    """Reset the global error tracker."""
    global _error_tracker
    _error_tracker = None


# =============================================================================
# Convenience Functions
# =============================================================================


def track_error(
    exception: Exception,
    severity: ErrorSeverity = ErrorSeverity.ERROR,
    category: ErrorCategory | None = None,
    context: ErrorContext | None = None,
    tags: list[str] | None = None,
) -> TrackedError:
    """Convenience function to track an error."""
    return get_error_tracker().track_error(exception, severity, category, context, tags)


def track_request() -> None:
    """Convenience function to track a request."""
    get_error_tracker().track_request()
