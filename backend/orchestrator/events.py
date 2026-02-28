"""
Orchestration Event Emitter — Phase X-A

Wraps the existing WebSocket protocol to emit typed orchestration events.
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from typing import Any, Optional

from .schemas import OrchestrationEvent, OrchestrationEventType

logger = logging.getLogger(__name__)

EventCallback = Optional[Callable[[OrchestrationEvent], None]]


class OrchestrationEventEmitter:
    """Emits orchestration lifecycle events to registered listeners."""

    def __init__(self) -> None:
        self._listeners: list[Callable[[OrchestrationEvent], None]] = []

    def add_listener(self, callback: Callable[[OrchestrationEvent], None]) -> None:
        self._listeners.append(callback)

    def remove_listener(self, callback: Callable[[OrchestrationEvent], None]) -> None:
        self._listeners = [cb for cb in self._listeners if cb is not callback]

    def emit(
        self,
        event_type: OrchestrationEventType,
        job_id: str,
        data: dict[str, Any] | None = None,
    ) -> OrchestrationEvent:
        event = OrchestrationEvent(
            event_type=event_type,
            job_id=job_id,
            data=data or {},
        )
        for listener in self._listeners:
            try:
                listener(event)
            except Exception:
                logger.exception("Event listener error for %s", event_type.value)
        return event

    def job_queued(self, job_id: str, **data: Any) -> OrchestrationEvent:
        return self.emit(OrchestrationEventType.JOB_QUEUED, job_id, data)

    def engine_selected(
        self, job_id: str, engine: str, **data: Any
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.ENGINE_SELECTED,
            job_id,
            {"engine": engine, **data},
        )

    def synthesis_started(
        self, job_id: str, engine: str, **data: Any
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.SYNTHESIS_STARTED,
            job_id,
            {"engine": engine, **data},
        )

    def synthesis_completed(
        self, job_id: str, duration_ms: float, **data: Any
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.SYNTHESIS_COMPLETED,
            job_id,
            {"duration_ms": duration_ms, **data},
        )

    def quality_evaluated(
        self, job_id: str, metrics: dict[str, Any], passed: bool, **data: Any
    ) -> OrchestrationEvent:
        event_type = (
            OrchestrationEventType.QUALITY_EVALUATED
            if passed
            else OrchestrationEventType.QUALITY_BELOW_THRESHOLD
        )
        return self.emit(
            event_type, job_id, {"metrics": metrics, "passed": passed, **data}
        )

    def retry_triggered(
        self, job_id: str, attempt: int, reason: str, **data: Any
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.RETRY_TRIGGERED,
            job_id,
            {"attempt": attempt, "reason": reason, **data},
        )

    def enhancement_started(self, job_id: str, **data: Any) -> OrchestrationEvent:
        return self.emit(OrchestrationEventType.ENHANCEMENT_STARTED, job_id, data)

    def enhancement_completed(self, job_id: str, **data: Any) -> OrchestrationEvent:
        return self.emit(OrchestrationEventType.ENHANCEMENT_COMPLETED, job_id, data)

    def job_completed(
        self,
        job_id: str,
        audio_url: str,
        total_ms: float,
        **data: Any,
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.JOB_COMPLETED,
            job_id,
            {"audio_url": audio_url, "total_ms": total_ms, **data},
        )

    def job_failed(
        self, job_id: str, error: str, **data: Any
    ) -> OrchestrationEvent:
        return self.emit(
            OrchestrationEventType.JOB_FAILED,
            job_id,
            {"error": error, **data},
        )
