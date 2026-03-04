"""
Training progress broadcaster interface.

Services call the broadcaster to push training progress; the API layer wires
a real implementation (WebSocket) at startup. Services must not import from
backend.api.routes or backend.api.ws.
"""

from __future__ import annotations

from typing import Protocol, runtime_checkable


@runtime_checkable
class TrainingProgressBroadcaster(Protocol):
    """Protocol for broadcasting training progress to clients."""

    async def broadcast_training_progress(
        self, training_id: str, progress_data: dict, batch: bool = True
    ) -> None:
        """Broadcast training progress. No-op if no clients connected."""
        ...


class NoOpBroadcaster:
    """Default no-op implementation when WebSocket is unavailable."""

    async def broadcast_training_progress(
        self, training_id: str, progress_data: dict, batch: bool = True
    ) -> None:
        pass


_broadcaster: TrainingProgressBroadcaster | None = None
_noop = NoOpBroadcaster()


def set_broadcaster(broadcaster: TrainingProgressBroadcaster | None) -> None:
    """Register the broadcaster implementation. Called at app startup."""
    global _broadcaster
    _broadcaster = broadcaster


def get_broadcaster() -> TrainingProgressBroadcaster:
    """Return the registered broadcaster, or NoOpBroadcaster if none set."""
    return _broadcaster if _broadcaster is not None else _noop
