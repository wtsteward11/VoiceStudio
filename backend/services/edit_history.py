"""
Undo/Redo Edit History for VoiceStudio (Phase 13.4.2)

Command pattern implementation for timeline edit operations.
Supports undo/redo with configurable history depth.
"""

from __future__ import annotations

import logging
import time
from abc import ABC, abstractmethod
from collections import deque
from dataclasses import dataclass
from typing import Any

logger = logging.getLogger(__name__)


class EditCommand(ABC):
    """
    Abstract base for edit commands.

    Each command implements execute() and undo() for
    reversible timeline operations.
    """

    @abstractmethod
    def execute(self) -> Any:
        """Execute the edit operation."""
        ...

    @abstractmethod
    def undo(self) -> Any:
        """Reverse the edit operation."""
        ...

    @property
    @abstractmethod
    def description(self) -> str:
        """Human-readable description of the edit."""
        ...


@dataclass
class EditRecord:
    """Record of an executed edit command."""

    command: EditCommand
    timestamp: float
    result: Any = None


class EditHistory:
    """
    Undo/redo stack for timeline editing operations.

    Uses command pattern to enable reversible edits.
    """

    def __init__(self, max_history: int = 100):
        self._undo_stack: deque[EditRecord] = deque(maxlen=max_history)
        self._redo_stack: deque[EditRecord] = deque(maxlen=max_history)
        self._max_history = max_history

    def execute(self, command: EditCommand) -> Any:
        """Execute a command and push it onto the undo stack."""
        result = command.execute()
        record = EditRecord(
            command=command,
            timestamp=time.time(),
            result=result,
        )
        self._undo_stack.append(record)
        self._redo_stack.clear()  # Clear redo stack on new action
        logger.debug(f"Edit executed: {command.description}")
        return result

    def undo(self) -> str | None:
        """Undo the most recent edit."""
        if not self._undo_stack:
            return None

        record = self._undo_stack.pop()
        record.command.undo()
        self._redo_stack.append(record)
        logger.debug(f"Undo: {record.command.description}")
        return record.command.description

    def redo(self) -> str | None:
        """Redo the most recently undone edit."""
        if not self._redo_stack:
            return None

        record = self._redo_stack.pop()
        record.command.execute()
        self._undo_stack.append(record)
        logger.debug(f"Redo: {record.command.description}")
        return record.command.description

    def can_undo(self) -> bool:
        return len(self._undo_stack) > 0

    def can_redo(self) -> bool:
        return len(self._redo_stack) > 0

    def get_undo_history(self, count: int = 10) -> list[str]:
        """Get descriptions of undoable edits."""
        return [r.command.description for r in list(self._undo_stack)[-count:]]

    def get_redo_history(self, count: int = 10) -> list[str]:
        """Get descriptions of redoable edits."""
        return [r.command.description for r in list(self._redo_stack)[-count:]]

    def clear(self) -> None:
        """Clear all history."""
        self._undo_stack.clear()
        self._redo_stack.clear()


# --- Concrete edit commands ---


class AddClipCommand(EditCommand):
    """Add a clip to a track."""

    def __init__(self, track_store, project_id: str, track_id: str, clip_data: dict[str, Any]):
        self._store = track_store
        self._project_id = project_id
        self._track_id = track_id
        self._clip_data = clip_data
        self._clip_id = clip_data.get("id", "")

    def execute(self) -> Any:
        track = self._store.get_track(self._project_id, self._track_id)
        if track:
            clips = track.get("clips", [])
            clips.append(self._clip_data)
            self._store.update_track(self._project_id, self._track_id, {"clips": clips})
        return self._clip_id

    def undo(self) -> Any:
        track = self._store.get_track(self._project_id, self._track_id)
        if track:
            clips = [c for c in track.get("clips", []) if c.get("id") != self._clip_id]
            self._store.update_track(self._project_id, self._track_id, {"clips": clips})

    @property
    def description(self) -> str:
        return f"Add clip to track {self._track_id}"


class RemoveClipCommand(EditCommand):
    """Remove a clip from a track."""

    def __init__(self, track_store, project_id: str, track_id: str, clip_id: str):
        self._store = track_store
        self._project_id = project_id
        self._track_id = track_id
        self._clip_id = clip_id
        self._removed_clip: dict[str, Any] | None = None

    def execute(self) -> Any:
        track = self._store.get_track(self._project_id, self._track_id)
        if track:
            clips = track.get("clips", [])
            self._removed_clip = next((c for c in clips if c.get("id") == self._clip_id), None)
            clips = [c for c in clips if c.get("id") != self._clip_id]
            self._store.update_track(self._project_id, self._track_id, {"clips": clips})
        return self._clip_id

    def undo(self) -> Any:
        if self._removed_clip:
            track = self._store.get_track(self._project_id, self._track_id)
            if track:
                clips = track.get("clips", [])
                clips.append(self._removed_clip)
                self._store.update_track(self._project_id, self._track_id, {"clips": clips})

    @property
    def description(self) -> str:
        return f"Remove clip {self._clip_id} from track {self._track_id}"


class MoveClipCommand(EditCommand):
    """Move a clip to a new position."""

    def __init__(self, track_store, project_id: str, track_id: str, clip_id: str, new_start: float):
        self._store = track_store
        self._project_id = project_id
        self._track_id = track_id
        self._clip_id = clip_id
        self._new_start = new_start
        self._old_start: float | None = None

    def execute(self) -> Any:
        track = self._store.get_track(self._project_id, self._track_id)
        if track:
            for clip in track.get("clips", []):
                if clip.get("id") == self._clip_id:
                    self._old_start = clip.get("start_time", 0.0)
                    clip["start_time"] = self._new_start
                    break
            self._store.save_track(self._project_id, track)

    def undo(self) -> Any:
        if self._old_start is not None:
            track = self._store.get_track(self._project_id, self._track_id)
            if track:
                for clip in track.get("clips", []):
                    if clip.get("id") == self._clip_id:
                        clip["start_time"] = self._old_start
                        break
                self._store.save_track(self._project_id, track)

    @property
    def description(self) -> str:
        return f"Move clip {self._clip_id} to position {self._new_start}"
