"""
Error Analysis Service — Phase 5.4.3

Provides error trend analysis and pattern detection.
Aggregates error data for insights into system health and failure patterns.

Features:
- Error rate calculation over time windows
- Error categorization and grouping
- Trend detection (increasing, stable, decreasing)
- Pattern identification for recurring errors
- Impact scoring

All operations are local-first with file-based storage.
"""

from __future__ import annotations

import json
import logging
import threading
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Default storage location
ERROR_DATA_DIR = Path(".voicestudio/error_analysis")

# Time windows for analysis
TIME_WINDOWS = {
    "1h": timedelta(hours=1),
    "6h": timedelta(hours=6),
    "24h": timedelta(hours=24),
    "7d": timedelta(days=7),
    "30d": timedelta(days=30),
}


class TrendDirection(Enum):
    """Direction of error trend."""

    INCREASING = "increasing"
    STABLE = "stable"
    DECREASING = "decreasing"
    UNKNOWN = "unknown"


class ErrorSeverity(Enum):
    """Error severity levels."""

    CRITICAL = 4
    HIGH = 3
    MEDIUM = 2
    LOW = 1
    INFO = 0


@dataclass
class ErrorEntry:
    """Represents a single error occurrence."""

    timestamp: datetime
    error_type: str
    message: str
    severity: ErrorSeverity = ErrorSeverity.MEDIUM
    source: str = ""
    correlation_id: str | None = None
    stack_trace: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for serialization."""
        return {
            "timestamp": self.timestamp.isoformat(),
            "error_type": self.error_type,
            "message": self.message,
            "severity": self.severity.name,
            "source": self.source,
            "correlation_id": self.correlation_id,
            "stack_trace": self.stack_trace,
            "metadata": self.metadata,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> ErrorEntry:
        """Create from dictionary."""
        return cls(
            timestamp=datetime.fromisoformat(data["timestamp"]),
            error_type=data["error_type"],
            message=data["message"],
            severity=ErrorSeverity[data.get("severity", "MEDIUM")],
            source=data.get("source", ""),
            correlation_id=data.get("correlation_id"),
            stack_trace=data.get("stack_trace"),
            metadata=data.get("metadata", {}),
        )


@dataclass
class ErrorTrend:
    """Error trend analysis result."""

    error_type: str
    direction: TrendDirection
    current_rate: float
    previous_rate: float
    change_percent: float
    total_count: int
    first_seen: datetime | None = None
    last_seen: datetime | None = None


@dataclass
class ErrorPattern:
    """Detected error pattern."""

    error_type: str
    pattern_id: str
    occurrences: int
    first_occurrence: datetime
    last_occurrence: datetime
    message_template: str
    affected_sources: list[str] = field(default_factory=list)
    severity: ErrorSeverity = ErrorSeverity.MEDIUM


@dataclass
class ErrorSummary:
    """Summary of error analysis."""

    window: str
    total_errors: int
    error_rate_per_hour: float
    unique_error_types: int
    top_errors: list[dict[str, Any]]
    trends: list[ErrorTrend]
    patterns: list[ErrorPattern]
    severity_distribution: dict[str, int]
    source_distribution: dict[str, int]


class ErrorAnalysisService:
    """
    Service for analyzing error trends and patterns.

    Thread-safe singleton for consistent error tracking.
    """

    _instance: ErrorAnalysisService | None = None
    _lock = threading.Lock()

    def __new__(cls) -> ErrorAnalysisService:
        if cls._instance is None:
            with cls._lock:
                if cls._instance is None:
                    instance = super().__new__(cls)
                    instance._errors: list[ErrorEntry] = []
                    instance._error_lock = threading.Lock()
                    instance._data_dir = ERROR_DATA_DIR
                    instance._init_storage()
                    cls._instance = instance
        return cls._instance

    def _init_storage(self) -> None:
        """Initialize storage directory."""
        self._data_dir.mkdir(parents=True, exist_ok=True)
        self._load_errors()

    def _load_errors(self) -> None:
        """Load errors from storage."""
        errors_file = self._data_dir / "errors.jsonl"
        if not errors_file.exists():
            return

        try:
            with open(errors_file, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        data = json.loads(line)
                        self._errors.append(ErrorEntry.from_dict(data))
        except (json.JSONDecodeError, KeyError) as e:
            logger.warning("Failed to load some error entries: %s", e)

    def _save_error(self, error: ErrorEntry) -> None:
        """Append error to storage."""
        errors_file = self._data_dir / "errors.jsonl"
        try:
            with open(errors_file, "a", encoding="utf-8") as f:
                f.write(json.dumps(error.to_dict()) + "\n")
        except OSError as e:
            logger.warning("Failed to save error: %s", e)

    def record_error(
        self,
        error_type: str,
        message: str,
        severity: ErrorSeverity = ErrorSeverity.MEDIUM,
        source: str = "",
        correlation_id: str | None = None,
        stack_trace: str | None = None,
        metadata: dict[str, Any] | None = None,
    ) -> ErrorEntry:
        """
        Record a new error occurrence.

        Args:
            error_type: Type/category of error.
            message: Error message.
            severity: Error severity level.
            source: Source component/module.
            correlation_id: Request correlation ID.
            stack_trace: Stack trace if available.
            metadata: Additional metadata.

        Returns:
            The recorded ErrorEntry.
        """
        error = ErrorEntry(
            timestamp=datetime.now(timezone.utc),
            error_type=error_type,
            message=message,
            severity=severity,
            source=source,
            correlation_id=correlation_id,
            stack_trace=stack_trace,
            metadata=metadata or {},
        )

        with self._error_lock:
            self._errors.append(error)
            self._save_error(error)

        return error

    def get_errors(
        self,
        window: str = "24h",
        error_type: str | None = None,
        severity: ErrorSeverity | None = None,
        source: str | None = None,
    ) -> list[ErrorEntry]:
        """
        Get errors matching filters within time window.

        Args:
            window: Time window (1h, 6h, 24h, 7d, 30d).
            error_type: Filter by error type.
            severity: Filter by minimum severity.
            source: Filter by source.

        Returns:
            List of matching errors.
        """
        duration = TIME_WINDOWS.get(window, timedelta(hours=24))
        cutoff = datetime.now(timezone.utc) - duration

        with self._error_lock:
            errors = [e for e in self._errors if e.timestamp >= cutoff]

        if error_type:
            errors = [e for e in errors if e.error_type == error_type]

        if severity:
            errors = [e for e in errors if e.severity.value >= severity.value]

        if source:
            errors = [e for e in errors if e.source == source]

        return errors

    def get_error_rate(
        self,
        window: str = "1h",
        error_type: str | None = None,
    ) -> float:
        """
        Get error rate (errors per hour) for window.

        Args:
            window: Time window.
            error_type: Optional filter by type.

        Returns:
            Errors per hour.
        """
        errors = self.get_errors(window, error_type=error_type)
        duration = TIME_WINDOWS.get(window, timedelta(hours=1))
        hours = duration.total_seconds() / 3600

        return len(errors) / hours if hours > 0 else 0.0

    def calculate_trend(
        self,
        error_type: str,
        window: str = "24h",
    ) -> ErrorTrend:
        """
        Calculate error trend by comparing current window to previous.

        Args:
            error_type: Error type to analyze.
            window: Current time window.

        Returns:
            ErrorTrend with direction and change percentage.
        """
        duration = TIME_WINDOWS.get(window, timedelta(hours=24))
        now = datetime.now(timezone.utc)
        current_start = now - duration
        previous_start = current_start - duration

        with self._error_lock:
            current_errors = [
                e
                for e in self._errors
                if e.error_type == error_type and e.timestamp >= current_start
            ]
            previous_errors = [
                e
                for e in self._errors
                if e.error_type == error_type and previous_start <= e.timestamp < current_start
            ]

        hours = duration.total_seconds() / 3600
        current_rate = len(current_errors) / hours if hours > 0 else 0.0
        previous_rate = len(previous_errors) / hours if hours > 0 else 0.0

        # Calculate change percentage
        if previous_rate > 0:
            change = ((current_rate - previous_rate) / previous_rate) * 100
        elif current_rate > 0:
            change = 100.0
        else:
            change = 0.0

        # Determine direction
        if abs(change) < 10:
            direction = TrendDirection.STABLE
        elif change > 0:
            direction = TrendDirection.INCREASING
        else:
            direction = TrendDirection.DECREASING

        first_seen = min(
            (e.timestamp for e in current_errors + previous_errors),
            default=None,
        )
        last_seen = max(
            (e.timestamp for e in current_errors + previous_errors),
            default=None,
        )

        return ErrorTrend(
            error_type=error_type,
            direction=direction,
            current_rate=current_rate,
            previous_rate=previous_rate,
            change_percent=change,
            total_count=len(current_errors) + len(previous_errors),
            first_seen=first_seen,
            last_seen=last_seen,
        )

    def detect_patterns(
        self,
        window: str = "24h",
        min_occurrences: int = 3,
    ) -> list[ErrorPattern]:
        """
        Detect recurring error patterns.

        Args:
            window: Time window to analyze.
            min_occurrences: Minimum occurrences to consider a pattern.

        Returns:
            List of detected patterns.
        """
        errors = self.get_errors(window)

        # Group by error type
        by_type: dict[str, list[ErrorEntry]] = defaultdict(list)
        for error in errors:
            by_type[error.error_type].append(error)

        patterns = []
        for error_type, type_errors in by_type.items():
            if len(type_errors) < min_occurrences:
                continue

            sources = list({e.source for e in type_errors if e.source})
            severities = [e.severity for e in type_errors]
            most_common_severity = max(
                set(severities),
                key=severities.count,
            )

            pattern = ErrorPattern(
                error_type=error_type,
                pattern_id=f"pattern_{error_type}_{len(type_errors)}",
                occurrences=len(type_errors),
                first_occurrence=min(e.timestamp for e in type_errors),
                last_occurrence=max(e.timestamp for e in type_errors),
                message_template=type_errors[0].message[:100],
                affected_sources=sources[:10],
                severity=most_common_severity,
            )
            patterns.append(pattern)

        # Sort by occurrences
        patterns.sort(key=lambda p: p.occurrences, reverse=True)
        return patterns

    def get_summary(self, window: str = "24h") -> ErrorSummary:
        """
        Get comprehensive error summary.

        Args:
            window: Time window to analyze.

        Returns:
            ErrorSummary with all analysis data.
        """
        errors = self.get_errors(window)
        duration = TIME_WINDOWS.get(window, timedelta(hours=24))
        hours = duration.total_seconds() / 3600

        # Count by type
        by_type: dict[str, int] = defaultdict(int)
        for error in errors:
            by_type[error.error_type] += 1

        # Top errors
        top_errors = [
            {"error_type": k, "count": v}
            for k, v in sorted(by_type.items(), key=lambda x: -x[1])[:10]
        ]

        # Severity distribution
        severity_dist: dict[str, int] = defaultdict(int)
        for error in errors:
            severity_dist[error.severity.name] += 1

        # Source distribution
        source_dist: dict[str, int] = defaultdict(int)
        for error in errors:
            if error.source:
                source_dist[error.source] += 1

        # Calculate trends for top error types
        trends = []
        for error_type in list(by_type.keys())[:5]:
            trend = self.calculate_trend(error_type, window)
            trends.append(trend)

        # Detect patterns
        patterns = self.detect_patterns(window)

        return ErrorSummary(
            window=window,
            total_errors=len(errors),
            error_rate_per_hour=len(errors) / hours if hours > 0 else 0.0,
            unique_error_types=len(by_type),
            top_errors=top_errors,
            trends=trends,
            patterns=patterns,
            severity_distribution=dict(severity_dist),
            source_distribution=dict(source_dist),
        )

    def get_impact_score(self, window: str = "24h") -> dict[str, Any]:
        """
        Calculate impact score based on error severity and frequency.

        Args:
            window: Time window to analyze.

        Returns:
            Impact score and breakdown.
        """
        errors = self.get_errors(window)

        # Weight by severity
        weights = {
            ErrorSeverity.CRITICAL: 10,
            ErrorSeverity.HIGH: 5,
            ErrorSeverity.MEDIUM: 2,
            ErrorSeverity.LOW: 1,
            ErrorSeverity.INFO: 0,
        }

        total_weight = sum(weights.get(e.severity, 1) for e in errors)
        max_possible = len(errors) * 10 if errors else 1

        # Normalize to 0-100 scale
        impact_score = min(100, (total_weight / max_possible) * 100)

        # Breakdown by severity
        breakdown: dict[str, dict[str, Any]] = {}
        for severity in ErrorSeverity:
            count = sum(1 for e in errors if e.severity == severity)
            weight = count * weights[severity]
            breakdown[severity.name] = {
                "count": count,
                "weight": weight,
            }

        return {
            "score": round(impact_score, 2),
            "level": self._get_impact_level(impact_score),
            "total_errors": len(errors),
            "weighted_total": total_weight,
            "breakdown": breakdown,
        }

    @staticmethod
    def _get_impact_level(score: float) -> str:
        """Get human-readable impact level."""
        if score >= 80:
            return "critical"
        elif score >= 60:
            return "high"
        elif score >= 40:
            return "medium"
        elif score >= 20:
            return "low"
        else:
            return "minimal"

    def cleanup_old_errors(self, days: int = 30) -> int:
        """
        Remove errors older than specified days.

        Args:
            days: Number of days to keep.

        Returns:
            Number of errors removed.
        """
        cutoff = datetime.now(timezone.utc) - timedelta(days=days)

        with self._error_lock:
            original_count = len(self._errors)
            self._errors = [e for e in self._errors if e.timestamp >= cutoff]
            removed = original_count - len(self._errors)

            # Rewrite storage file
            if removed > 0:
                errors_file = self._data_dir / "errors.jsonl"
                with open(errors_file, "w", encoding="utf-8") as f:
                    for error in self._errors:
                        f.write(json.dumps(error.to_dict()) + "\n")

        return removed


def get_error_analysis_service() -> ErrorAnalysisService:
    """Get the global error analysis service instance."""
    return ErrorAnalysisService()
