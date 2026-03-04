"""
Phase 7: Telemetry System
Task 7.4: Privacy-respecting telemetry for diagnostics.
"""

from __future__ import annotations

import asyncio
import json
import logging
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class TelemetryLevel(Enum):
    """Telemetry collection levels."""

    OFF = "off"
    MINIMAL = "minimal"  # Only crash reports
    STANDARD = "standard"  # + Usage statistics
    FULL = "full"  # + Performance metrics


class EventType(Enum):
    """Types of telemetry events."""

    APP_START = "app_start"
    APP_CLOSE = "app_close"
    FEATURE_USE = "feature_use"
    ERROR = "error"
    CRASH = "crash"
    PERFORMANCE = "performance"
    SYNTHESIS = "synthesis"
    FEEDBACK = "feedback"


@dataclass
class TelemetryEvent:
    """A telemetry event."""

    event_type: EventType
    timestamp: datetime
    session_id: str
    event_id: str = field(default_factory=lambda: str(uuid.uuid4()))
    properties: dict[str, Any] = field(default_factory=dict)
    metrics: dict[str, float] = field(default_factory=dict)


@dataclass
class TelemetrySettings:
    """Telemetry settings."""

    level: TelemetryLevel = TelemetryLevel.OFF
    consent_given: bool = False
    consent_date: datetime | None = None
    anonymous_id: str = field(default_factory=lambda: str(uuid.uuid4()))
    batch_size: int = 50
    flush_interval_seconds: int = 300


class TelemetryService:
    """Service for privacy-respecting telemetry collection."""

    def __init__(self, settings_path: Path | None = None, endpoint_url: str | None = None):
        self._settings_path = settings_path or Path.home() / ".voicestudio/telemetry.json"
        self._endpoint_url = endpoint_url
        self._settings = TelemetrySettings()
        self._events: list[TelemetryEvent] = []
        self._session_id = str(uuid.uuid4())
        self._flush_task: asyncio.Task | None = None
        self._load_settings()

    @property
    def settings(self) -> TelemetrySettings:
        """Get telemetry settings."""
        return self._settings

    @property
    def is_enabled(self) -> bool:
        """Check if telemetry is enabled."""
        return self._settings.consent_given and self._settings.level != TelemetryLevel.OFF

    def set_consent(self, consent: bool, level: TelemetryLevel = TelemetryLevel.STANDARD) -> None:
        """Set user consent for telemetry."""
        self._settings.consent_given = consent
        self._settings.consent_date = datetime.now() if consent else None
        self._settings.level = level if consent else TelemetryLevel.OFF
        self._save_settings()

    def track_event(
        self,
        event_type: EventType,
        properties: dict[str, Any] | None = None,
        metrics: dict[str, float] | None = None,
    ) -> None:
        """Track a telemetry event."""
        if not self.is_enabled:
            return

        # Check if event type is allowed at current level
        if not self._is_event_allowed(event_type):
            return

        event = TelemetryEvent(
            event_type=event_type,
            timestamp=datetime.now(),
            session_id=self._session_id,
            properties=self._sanitize_properties(properties or {}),
            metrics=metrics or {},
        )

        self._events.append(event)

        # Flush if batch size reached
        if len(self._events) >= self._settings.batch_size:
            asyncio.create_task(self.flush())

    def track_app_start(self) -> None:
        """Track application start."""
        self.track_event(
            EventType.APP_START,
            properties={
                "os_version": self._get_os_version(),
                "app_version": self._get_app_version(),
            },
        )

    def track_app_close(self, session_duration_seconds: float) -> None:
        """Track application close."""
        self.track_event(
            EventType.APP_CLOSE, metrics={"session_duration": session_duration_seconds}
        )

    def track_feature_use(self, feature_name: str, details: dict[str, Any] | None = None) -> None:
        """Track feature usage."""
        self.track_event(
            EventType.FEATURE_USE,
            properties={
                "feature": feature_name,
                **(details or {}),
            },
        )

    def track_error(
        self, error_type: str, error_message: str, stack_trace: str | None = None
    ) -> None:
        """Track an error."""
        self.track_event(
            EventType.ERROR,
            properties={
                "error_type": error_type,
                "error_message": self._sanitize_message(error_message),
                "stack_trace": self._sanitize_stack_trace(stack_trace) if stack_trace else None,
            },
        )

    def track_crash(self, exception: Exception, context: dict[str, Any] | None = None) -> None:
        """Track a crash (always sent if consent given)."""
        if not self._settings.consent_given:
            return

        self.track_event(
            EventType.CRASH,
            properties={
                "exception_type": type(exception).__name__,
                "exception_message": self._sanitize_message(str(exception)),
                **(self._sanitize_properties(context or {})),
            },
        )

        # Immediately flush crash events
        asyncio.create_task(self.flush())

    def track_synthesis(self, engine: str, duration_seconds: float, success: bool) -> None:
        """Track synthesis operation."""
        self.track_event(
            EventType.SYNTHESIS,
            properties={
                "engine": engine,
                "success": success,
            },
            metrics={
                "duration": duration_seconds,
            },
        )
        if success and duration_seconds > 0:
            try:
                from backend.services.usage_stats import record_synthesis_minutes
                record_synthesis_minutes(duration_seconds / 60.0)
            except Exception as e:
                logger.debug("Usage stats record_synthesis_minutes skip: %s", e)

    def track_performance(self, operation: str, duration_ms: float, success: bool = True) -> None:
        """Track performance metrics."""
        self.track_event(
            EventType.PERFORMANCE,
            properties={
                "operation": operation,
                "success": success,
            },
            metrics={
                "duration_ms": duration_ms,
            },
        )

    async def flush(self) -> bool:
        """Flush pending events to the server."""
        if not self._events:
            return True

        if not self._endpoint_url:
            # No endpoint, just clear events
            self._events.clear()
            return True

        events_to_send = self._events.copy()
        self._events.clear()

        try:
            # Prepare payload
            {
                "anonymous_id": self._settings.anonymous_id,
                "events": [
                    {
                        "id": e.event_id,
                        "type": e.event_type.value,
                        "timestamp": e.timestamp.isoformat(),
                        "session_id": e.session_id,
                        "properties": e.properties,
                        "metrics": e.metrics,
                    }
                    for e in events_to_send
                ],
            }

            # In production, send to endpoint
            # For now, just log
            logger.debug(f"Would send {len(events_to_send)} telemetry events")

            return True

        except Exception as e:
            logger.error(f"Failed to flush telemetry: {e}")
            # Put events back
            self._events = events_to_send + self._events
            return False

    def start_background_flush(self) -> None:
        """Start background flush task."""

        async def flush_loop():
            while True:
                await asyncio.sleep(self._settings.flush_interval_seconds)
                await self.flush()

        self._flush_task = asyncio.create_task(flush_loop())

    def stop_background_flush(self) -> None:
        """Stop background flush task."""
        if self._flush_task:
            self._flush_task.cancel()
            self._flush_task = None

    def _is_event_allowed(self, event_type: EventType) -> bool:
        """Check if event type is allowed at current level."""
        level = self._settings.level

        if level == TelemetryLevel.OFF:
            return False

        if level == TelemetryLevel.MINIMAL:
            return event_type in (EventType.CRASH,)

        if level == TelemetryLevel.STANDARD:
            return event_type in (
                EventType.CRASH,
                EventType.ERROR,
                EventType.APP_START,
                EventType.APP_CLOSE,
                EventType.FEATURE_USE,
            )

        # FULL level allows all
        return True

    def _sanitize_properties(self, properties: dict[str, Any]) -> dict[str, Any]:
        """Sanitize properties to remove PII."""
        sanitized = {}

        pii_keys = {"email", "name", "username", "path", "file", "directory"}

        for key, value in properties.items():
            if key.lower() in pii_keys:
                continue

            if isinstance(value, str):
                value = self._sanitize_message(value)

            sanitized[key] = value

        return sanitized

    def _sanitize_message(self, message: str) -> str:
        """Sanitize message to remove potential PII."""
        import re

        # Remove file paths
        message = re.sub(r"[A-Za-z]:\\[^\s]+", "[PATH]", message)
        message = re.sub(r"/[^\s]+", "[PATH]", message)

        # Remove email addresses
        message = re.sub(r"\S+@\S+\.\S+", "[EMAIL]", message)

        # Remove IP addresses
        message = re.sub(r"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}", "[IP]", message)

        return message

    def _sanitize_stack_trace(self, stack_trace: str) -> str:
        """Sanitize stack trace to remove file paths."""
        import re

        # Replace file paths
        sanitized = re.sub(r'File "[^"]+', 'File "[PATH]', stack_trace)

        return sanitized

    def _get_os_version(self) -> str:
        """Get OS version."""
        import platform

        return platform.platform()

    def _get_app_version(self) -> str:
        """Get application version."""
        return "1.0.0"

    def _load_settings(self) -> None:
        """Load settings from disk."""
        try:
            if self._settings_path.exists():
                data = json.loads(self._settings_path.read_text())

                self._settings = TelemetrySettings(
                    level=TelemetryLevel(data.get("level", "off")),
                    consent_given=data.get("consent_given", False),
                    consent_date=(
                        datetime.fromisoformat(data["consent_date"])
                        if data.get("consent_date")
                        else None
                    ),
                    anonymous_id=data.get("anonymous_id", str(uuid.uuid4())),
                )
        except Exception as e:
            logger.warning(f"Failed to load telemetry settings: {e}")

    def _save_settings(self) -> None:
        """Save settings to disk."""
        try:
            self._settings_path.parent.mkdir(parents=True, exist_ok=True)

            data = {
                "level": self._settings.level.value,
                "consent_given": self._settings.consent_given,
                "consent_date": (
                    self._settings.consent_date.isoformat() if self._settings.consent_date else None
                ),
                "anonymous_id": self._settings.anonymous_id,
            }

            self._settings_path.write_text(json.dumps(data, indent=2))

        except Exception as e:
            logger.error(f"Failed to save telemetry settings: {e}")
