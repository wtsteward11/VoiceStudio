"""
SLO Monitoring Service — Phase 5.2

Provides comprehensive Service Level Objective (SLO) monitoring and alerting.

Features:
- Configurable SLO definitions
- Rolling window metric tracking
- Alert generation on SLO breach
- Historical SLO status tracking
- Budget burn rate calculation

Local-first: All data stored locally, no external dependencies.
"""

from __future__ import annotations

import json
import logging
import threading
import time
from collections import deque
from collections.abc import Callable
from dataclasses import asdict, dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path

logger = logging.getLogger(__name__)

# Default SLO storage path
DEFAULT_SLO_DATA_DIR = Path(".buildlogs/slo")


# =============================================================================
# SLO Configuration
# =============================================================================


class SLOType(str, Enum):
    """Types of SLOs."""

    LATENCY = "latency"
    AVAILABILITY = "availability"
    ERROR_RATE = "error_rate"
    THROUGHPUT = "throughput"
    QUALITY = "quality"


class AlertSeverity(str, Enum):
    """Alert severity levels."""

    INFO = "info"
    WARNING = "warning"
    CRITICAL = "critical"


@dataclass
class SLODefinition:
    """Defines an SLO target."""

    id: str
    name: str
    description: str
    slo_type: SLOType
    target: float
    warning_threshold: float  # Trigger warning at this level
    critical_threshold: float  # Trigger critical at this level
    window_hours: int = 24  # Rolling window for measurement
    metric_name: str = ""  # Telemetry metric to track
    is_higher_better: bool = False  # True for availability, false for latency

    def check_status(self, current_value: float) -> tuple[bool, AlertSeverity | None]:
        """
        Check if SLO is met and return alert severity if applicable.

        Returns:
            Tuple of (is_met, alert_severity)
        """
        if self.is_higher_better:
            is_met = current_value >= self.target
            if current_value < self.critical_threshold:
                return False, AlertSeverity.CRITICAL
            elif current_value < self.warning_threshold:
                return False, AlertSeverity.WARNING
            return is_met, None
        else:
            is_met = current_value <= self.target
            if current_value > self.critical_threshold:
                return False, AlertSeverity.CRITICAL
            elif current_value > self.warning_threshold:
                return False, AlertSeverity.WARNING
            return is_met, None


@dataclass
class SLOStatus:
    """Current status of an SLO."""

    slo_id: str
    slo_name: str
    target: float
    current_value: float
    is_met: bool
    alert_severity: str | None = None
    window_hours: int = 24
    sample_count: int = 0
    last_updated: str = ""
    burn_rate: float = 0.0  # Budget burn rate (1.0 = burning at expected rate)
    error_budget_remaining: float = 100.0  # Percentage of error budget remaining


@dataclass
class SLOAlert:
    """An SLO alert event."""

    alert_id: str
    slo_id: str
    slo_name: str
    severity: AlertSeverity
    message: str
    current_value: float
    target: float
    timestamp: str
    acknowledged: bool = False
    acknowledged_by: str | None = None
    acknowledged_at: str | None = None
    resolved: bool = False
    resolved_at: str | None = None


@dataclass
class MetricSample:
    """A single metric sample."""

    timestamp: float
    value: float
    labels: dict[str, str] = field(default_factory=dict)


# =============================================================================
# Default SLO Definitions
# =============================================================================

DEFAULT_SLOS = [
    SLODefinition(
        id="synthesis_latency_p95",
        name="Voice Synthesis Latency (P95)",
        description="95th percentile latency for voice synthesis should be under 2 seconds",
        slo_type=SLOType.LATENCY,
        target=2.0,
        warning_threshold=1.5,
        critical_threshold=2.5,
        window_hours=24,
        metric_name="voice_synthesis_latency_seconds",
        is_higher_better=False,
    ),
    SLODefinition(
        id="api_availability",
        name="API Availability",
        description="API should be available 99.5% of the time",
        slo_type=SLOType.AVAILABILITY,
        target=0.995,
        warning_threshold=0.99,
        critical_threshold=0.98,
        window_hours=24,
        metric_name="http_request_success_rate",
        is_higher_better=True,
    ),
    SLODefinition(
        id="transcription_accuracy",
        name="Transcription Accuracy",
        description="Transcription accuracy should be above 95%",
        slo_type=SLOType.QUALITY,
        target=0.95,
        warning_threshold=0.93,
        critical_threshold=0.90,
        window_hours=24,
        metric_name="transcription_accuracy",
        is_higher_better=True,
    ),
    SLODefinition(
        id="engine_error_rate",
        name="Engine Error Rate",
        description="Engine error rate should be below 1%",
        slo_type=SLOType.ERROR_RATE,
        target=0.01,
        warning_threshold=0.02,
        critical_threshold=0.05,
        window_hours=24,
        metric_name="engine_error_rate",
        is_higher_better=False,
    ),
    SLODefinition(
        id="request_throughput",
        name="Request Throughput",
        description="API should handle at least 10 requests per second",
        slo_type=SLOType.THROUGHPUT,
        target=10.0,
        warning_threshold=8.0,
        critical_threshold=5.0,
        window_hours=1,
        metric_name="http_requests_per_second",
        is_higher_better=True,
    ),
]


# =============================================================================
# SLO Monitor Service
# =============================================================================


class SLOMonitor:
    """
    Monitors Service Level Objectives and generates alerts.

    Usage:
        monitor = SLOMonitor()
        monitor.record_metric("voice_synthesis_latency_seconds", 1.5)
        status = monitor.get_slo_status("synthesis_latency_p95")
        alerts = monitor.get_active_alerts()
    """

    def __init__(
        self,
        slos: list[SLODefinition] | None = None,
        data_dir: Path | None = None,
        alert_callback: Callable[[SLOAlert], None] | None = None,
    ):
        """
        Initialize SLO monitor.

        Args:
            slos: List of SLO definitions (defaults to DEFAULT_SLOS)
            data_dir: Directory for persisting SLO data
            alert_callback: Callback function for new alerts
        """
        self.slos: dict[str, SLODefinition] = {}
        self._metric_samples: dict[str, deque] = {}
        self._alerts: list[SLOAlert] = []
        self._alert_history: list[SLOAlert] = []
        self._lock = threading.Lock()
        self._alert_callback = alert_callback
        self._data_dir = data_dir or DEFAULT_SLO_DATA_DIR
        self._data_dir.mkdir(parents=True, exist_ok=True)
        self._alert_counter = 0

        # Register SLOs
        for slo in slos or DEFAULT_SLOS:
            self.register_slo(slo)

        logger.info(f"SLOMonitor initialized with {len(self.slos)} SLOs")

    def register_slo(self, slo: SLODefinition) -> None:
        """Register an SLO definition."""
        self.slos[slo.id] = slo
        # Initialize metric buffer with max samples for window
        max_samples = slo.window_hours * 3600  # 1 sample per second max
        self._metric_samples[slo.metric_name] = deque(maxlen=min(max_samples, 100000))
        logger.debug(f"Registered SLO: {slo.id}")

    def record_metric(
        self,
        metric_name: str,
        value: float,
        labels: dict[str, str] | None = None,
        timestamp: float | None = None,
    ) -> None:
        """
        Record a metric sample.

        Args:
            metric_name: Name of the metric
            value: Metric value
            labels: Optional labels for the sample
            timestamp: Optional timestamp (defaults to now)
        """
        ts = timestamp or time.time()
        sample = MetricSample(timestamp=ts, value=value, labels=labels or {})

        with self._lock:
            if metric_name in self._metric_samples:
                self._metric_samples[metric_name].append(sample)

            # Check SLOs that use this metric
            for slo in self.slos.values():
                if slo.metric_name == metric_name:
                    self._check_slo(slo)

    def _check_slo(self, slo: SLODefinition) -> None:
        """Check an SLO and generate alerts if needed."""
        samples = list(self._metric_samples.get(slo.metric_name, []))
        if not samples:
            return

        # Calculate current value based on SLO type
        current_value = self._calculate_slo_value(slo, samples)
        is_met, severity = slo.check_status(current_value)

        if severity and not self._has_active_alert(slo.id, severity):
            self._create_alert(slo, current_value, severity)
        elif is_met:
            self._resolve_alerts(slo.id)

    def _calculate_slo_value(
        self,
        slo: SLODefinition,
        samples: list[MetricSample],
    ) -> float:
        """Calculate the current SLO value from samples."""
        if not samples:
            return slo.target  # Assume target if no data

        # Filter to window
        cutoff = time.time() - (slo.window_hours * 3600)
        window_samples = [s for s in samples if s.timestamp >= cutoff]

        if not window_samples:
            return slo.target

        values = [s.value for s in window_samples]

        if slo.slo_type == SLOType.LATENCY:
            # Calculate P95
            sorted_vals = sorted(values)
            idx = int(len(sorted_vals) * 0.95)
            return sorted_vals[min(idx, len(sorted_vals) - 1)]
        elif slo.slo_type in (SLOType.AVAILABILITY, SLOType.ERROR_RATE):
            # Calculate rate
            return sum(values) / len(values)
        else:
            # Average for other types
            return sum(values) / len(values)

    def _has_active_alert(self, slo_id: str, severity: AlertSeverity) -> bool:
        """Check if there's an active alert for this SLO."""
        return any(
            a.slo_id == slo_id and a.severity == severity and not a.resolved for a in self._alerts
        )

    def _create_alert(
        self,
        slo: SLODefinition,
        current_value: float,
        severity: AlertSeverity,
    ) -> SLOAlert:
        """Create a new alert."""
        self._alert_counter += 1
        alert = SLOAlert(
            alert_id=f"alert-{self._alert_counter:05d}",
            slo_id=slo.id,
            slo_name=slo.name,
            severity=severity,
            message=self._format_alert_message(slo, current_value, severity),
            current_value=current_value,
            target=slo.target,
            timestamp=datetime.utcnow().isoformat() + "Z",
        )

        self._alerts.append(alert)
        logger.warning(f"SLO Alert: {alert.message}")

        if self._alert_callback:
            try:
                self._alert_callback(alert)
            except Exception as e:
                logger.error(f"Alert callback failed: {e}")

        return alert

    def _format_alert_message(
        self,
        slo: SLODefinition,
        current_value: float,
        severity: AlertSeverity,
    ) -> str:
        """Format an alert message."""
        direction = "above" if not slo.is_higher_better else "below"
        return (
            f"[{severity.value.upper()}] {slo.name}: "
            f"Current value {current_value:.3f} is {direction} target {slo.target:.3f}"
        )

    def _resolve_alerts(self, slo_id: str) -> None:
        """Resolve all active alerts for an SLO."""
        now = datetime.utcnow().isoformat() + "Z"
        for alert in self._alerts:
            if alert.slo_id == slo_id and not alert.resolved:
                alert.resolved = True
                alert.resolved_at = now
                self._alert_history.append(alert)
                logger.info(f"SLO Alert resolved: {alert.slo_name}")

        self._alerts = [a for a in self._alerts if not a.resolved]

    def get_slo_status(self, slo_id: str) -> SLOStatus | None:
        """Get the current status of an SLO."""
        slo = self.slos.get(slo_id)
        if not slo:
            return None

        with self._lock:
            samples = list(self._metric_samples.get(slo.metric_name, []))

        # Filter to window
        cutoff = time.time() - (slo.window_hours * 3600)
        window_samples = [s for s in samples if s.timestamp >= cutoff]

        current_value = self._calculate_slo_value(slo, window_samples)
        is_met, severity = slo.check_status(current_value)

        # Calculate burn rate (ratio of actual error rate to allowed error rate)
        error_budget = 1.0 - slo.target if slo.is_higher_better else slo.target
        actual_error = 1.0 - current_value if slo.is_higher_better else current_value
        burn_rate = actual_error / error_budget if error_budget > 0 else 0.0

        # Calculate remaining budget
        window_fraction = min(
            (time.time() - window_samples[0].timestamp if window_samples else 0)
            / (slo.window_hours * 3600),
            1.0,
        )
        expected_burn = window_fraction
        budget_remaining = max(0, (1.0 - (burn_rate * expected_burn)) * 100)

        return SLOStatus(
            slo_id=slo.id,
            slo_name=slo.name,
            target=slo.target,
            current_value=current_value,
            is_met=is_met,
            alert_severity=severity.value if severity else None,
            window_hours=slo.window_hours,
            sample_count=len(window_samples),
            last_updated=datetime.utcnow().isoformat() + "Z",
            burn_rate=burn_rate,
            error_budget_remaining=budget_remaining,
        )

    def get_all_slo_statuses(self) -> list[SLOStatus]:
        """Get status of all SLOs."""
        statuses = []
        for slo_id in self.slos:
            status = self.get_slo_status(slo_id)
            if status:
                statuses.append(status)
        return statuses

    def get_active_alerts(self) -> list[SLOAlert]:
        """Get all active (unresolved) alerts."""
        return [a for a in self._alerts if not a.resolved]

    def get_alert_history(
        self,
        limit: int = 100,
        slo_id: str | None = None,
    ) -> list[SLOAlert]:
        """Get alert history."""
        history = self._alert_history + self._alerts
        if slo_id:
            history = [a for a in history if a.slo_id == slo_id]
        return sorted(history, key=lambda a: a.timestamp, reverse=True)[:limit]

    def acknowledge_alert(
        self,
        alert_id: str,
        acknowledged_by: str = "system",
    ) -> bool:
        """Acknowledge an alert."""
        for alert in self._alerts:
            if alert.alert_id == alert_id and not alert.acknowledged:
                alert.acknowledged = True
                alert.acknowledged_by = acknowledged_by
                alert.acknowledged_at = datetime.utcnow().isoformat() + "Z"
                logger.info(f"Alert {alert_id} acknowledged by {acknowledged_by}")
                return True
        return False

    def get_overall_health(self) -> str:
        """
        Get overall SLO health.

        Returns:
            "healthy", "degraded", or "unhealthy"
        """
        statuses = self.get_all_slo_statuses()
        if not statuses:
            return "healthy"

        met_count = sum(1 for s in statuses if s.is_met)
        total = len(statuses)

        critical_alerts = [
            a for a in self._alerts if a.severity == AlertSeverity.CRITICAL and not a.resolved
        ]

        if critical_alerts:
            return "unhealthy"
        elif met_count == total:
            return "healthy"
        elif met_count >= total * 0.5:
            return "degraded"
        else:
            return "unhealthy"

    def export_status(self, filepath: Path | None = None) -> Path:
        """Export current SLO status to JSON file."""
        filepath = filepath or (
            self._data_dir / f"slo_status_{datetime.utcnow().strftime('%Y%m%d_%H%M%S')}.json"
        )

        data = {
            "exported_at": datetime.utcnow().isoformat() + "Z",
            "overall_health": self.get_overall_health(),
            "slos": [asdict(s) for s in self.get_all_slo_statuses()],
            "active_alerts": [asdict(a) for a in self.get_active_alerts()],
        }

        with open(filepath, "w") as f:
            json.dump(data, f, indent=2, default=str)

        return filepath


# =============================================================================
# Global Instance
# =============================================================================

_slo_monitor: SLOMonitor | None = None


def get_slo_monitor() -> SLOMonitor:
    """Get the global SLO monitor instance."""
    global _slo_monitor
    if _slo_monitor is None:
        _slo_monitor = SLOMonitor()
    return _slo_monitor


def reset_slo_monitor() -> None:
    """Reset the global SLO monitor (for testing)."""
    global _slo_monitor
    _slo_monitor = None


# =============================================================================
# Integration Helpers
# =============================================================================


def record_latency(metric_name: str, latency_seconds: float) -> None:
    """Convenience function to record latency metrics."""
    get_slo_monitor().record_metric(metric_name, latency_seconds)


def record_success(metric_name: str, success: bool) -> None:
    """Convenience function to record success/failure metrics."""
    get_slo_monitor().record_metric(metric_name, 1.0 if success else 0.0)


def record_error_rate(metric_name: str, error_count: int, total_count: int) -> None:
    """Convenience function to record error rate metrics."""
    if total_count > 0:
        rate = error_count / total_count
        get_slo_monitor().record_metric(metric_name, rate)
