"""
Local Alerting Service — Phase 8 WS4

Rule engine for threshold-based alerts. Local-first, no external dependencies.
Channels: backend log, optional UI toast via WebSocket.
"""

from __future__ import annotations

import json
import logging
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)


@dataclass
class AlertRule:
    """A single alert rule."""

    id: str
    name: str
    condition: str  # error_rate | latency_p95 | circuit_open
    threshold: float
    window_seconds: int = 300
    severity: str = "warning"  # info | warning | critical
    enabled: bool = True

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> AlertRule:
        return cls(
            id=data.get("id", ""),
            name=data.get("name", ""),
            condition=data.get("condition", "error_rate"),
            threshold=data.get("threshold", 0.05),
            window_seconds=data.get("window_seconds", 300),
            severity=data.get("severity", "warning"),
            enabled=data.get("enabled", True),
        )


@dataclass
class Alert:
    """A fired alert."""

    rule_id: str
    message: str
    severity: str
    value: float
    threshold: float
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class AlertingService:
    """
    Local alerting with configurable rules and persistence.

    - Rules: error_rate > 5%, latency_p95 > 2000ms, circuit_open
    - Alerts persisted to data/alerts.json
    - Optional WebSocket callback for UI toast
    """

    def __init__(
        self,
        data_dir: Path | None = None,
        config_path: Path | None = None,
    ):
        self._data_dir = data_dir or get_path("data")
        self._alerts_path = self._data_dir / "alerts.json"
        self._config_path = config_path or Path("config/alert_rules.json")
        self._rules: dict[str, AlertRule] = {}
        self._alerts: list[dict[str, Any]] = []
        self._ws_callback: Callable[[Alert], None] | None = None
        self._load_rules()
        self._load_alerts()

    def _load_rules(self) -> None:
        """Load rules from config."""
        if self._config_path.exists():
            try:
                with open(self._config_path, encoding="utf-8") as f:
                    data = json.load(f)
                self._rules = {}
                for r in data.get("rules", []):
                    rule = AlertRule.from_dict(r)
                    self._rules[rule.id] = rule
            except (json.JSONDecodeError, OSError) as e:
                logger.warning(f"Failed to load alert rules: {e}")
                self._default_rules()
        else:
            self._default_rules()

    def _default_rules(self) -> None:
        """Default rules when no config exists."""
        self._rules = {
            "error_rate_5pct": AlertRule(
                id="error_rate_5pct",
                name="Error rate > 5%",
                condition="error_rate",
                threshold=0.05,
                window_seconds=300,
                severity="warning",
            ),
            "latency_p95_2s": AlertRule(
                id="latency_p95_2s",
                name="Latency p95 > 2s",
                condition="latency_p95",
                threshold=2000.0,
                window_seconds=300,
                severity="warning",
            ),
            "circuit_open": AlertRule(
                id="circuit_open",
                name="Circuit breaker open",
                condition="circuit_open",
                threshold=1.0,
                severity="critical",
            ),
        }

    def _load_alerts(self) -> None:
        """Load recent alerts from disk."""
        if self._alerts_path.exists():
            try:
                with open(self._alerts_path, encoding="utf-8") as f:
                    data = json.load(f)
                self._alerts = data.get("alerts", [])[-500:]
            except (json.JSONDecodeError, OSError) as e:
                logger.warning(f"Failed to load alerts: {e}")
                self._alerts = []
        else:
            self._alerts = []

    def _save_alerts(self) -> None:
        """Persist alerts to disk."""
        self._alerts_path.parent.mkdir(parents=True, exist_ok=True)
        try:
            data = {
                "version": "1.0",
                "updated_at": datetime.now(timezone.utc).isoformat(),
                "alerts": self._alerts[-500:],
            }
            with open(self._alerts_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
        except OSError as e:
            logger.error(f"Failed to save alerts: {e}")

    def set_ws_callback(self, callback: Callable[[Alert], None] | None) -> None:
        """Set optional WebSocket callback for UI toast."""
        self._ws_callback = callback

    def evaluate(
        self,
        condition: str,
        value: float,
        metadata: dict[str, Any] | None = None,
    ) -> list[Alert]:
        """
        Evaluate value against rules. Fire alerts when threshold exceeded.

        Returns:
            List of fired alerts.
        """
        fired: list[Alert] = []
        for rule in self._rules.values():
            if not rule.enabled or rule.condition != condition:
                continue
            if value <= rule.threshold:
                continue
            alert = Alert(
                rule_id=rule.id,
                message=f"{rule.name}: {value} >= {rule.threshold}",
                severity=rule.severity,
                value=value,
                threshold=rule.threshold,
                metadata=metadata or {},
            )
            fired.append(alert)
            self._alerts.append(alert.to_dict())
            logger.warning(
                "Alert fired: %s (value=%.2f, threshold=%.2f)",
                rule.name,
                value,
                rule.threshold,
            )
            if self._ws_callback:
                try:
                    self._ws_callback(alert)
                except Exception as e:
                    logger.debug(f"WS callback failed: {e}")
        if fired:
            self._save_alerts()
        return fired

    def get_alerts(self, limit: int = 50) -> list[dict[str, Any]]:
        """Get recent alerts."""
        return list(reversed(self._alerts[-limit:]))

    def get_rules(self) -> list[dict[str, Any]]:
        """Get configured rules."""
        return [r.to_dict() for r in self._rules.values()]


_alerting_instance: AlertingService | None = None


def get_alerting_service(
    data_dir: Path | None = None,
    config_path: Path | None = None,
) -> AlertingService:
    """Get or create the alerting service singleton."""
    global _alerting_instance
    if _alerting_instance is None:
        _alerting_instance = AlertingService(
            data_dir=data_dir,
            config_path=config_path,
        )
    return _alerting_instance
