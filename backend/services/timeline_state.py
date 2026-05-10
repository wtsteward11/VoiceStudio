"""Timeline session state models and persistence helpers (service layer).

Extracted from ``backend.api.routes.timeline`` so backend services can load,
mutate, and persist timeline state without importing the HTTP route module
(``scripts/ci/check_service_boundaries.py`` gate).
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional
from uuid import uuid4

from pydantic import BaseModel, Field

from backend.project.timeline.session_repository import DEFAULT_SESSION_ID


class Clip(BaseModel):
    """A clip within a track."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    track_id: str = ""
    start_time: float = 0.0  # seconds
    end_time: float = 1.0  # seconds
    source_path: Optional[str] = None
    source_start: float = 0.0  # source offset
    fade_in_seconds: float = 0.0
    fade_out_seconds: float = 0.0
    name: str = "Untitled Clip"
    color: Optional[str] = None
    volume: float = 1.0
    muted: bool = False
    locked: bool = False
    metadata: Dict[str, Any] = Field(default_factory=dict)


class Track(BaseModel):
    """A track in the timeline."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    name: str = "Track"
    type: str = "audio"  # audio, video, subtitle
    order: int = 0
    color: Optional[str] = None
    volume: float = 1.0
    pan: float = 0.0
    muted: bool = False
    solo: bool = False
    locked: bool = False
    clips: List[Clip] = Field(default_factory=list)
    metadata: Dict[str, Any] = Field(default_factory=dict)


class TimelineState(BaseModel):
    """Complete timeline state."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    name: str = "Untitled Timeline"
    duration: float = 0.0  # seconds
    sample_rate: int = 48000
    tracks: list[Track] = Field(default_factory=list)
    playhead_position: float = 0.0
    loop_start: float | None = None
    loop_end: float | None = None
    zoom_level: float = 1.0
    scroll_offset: float = 0.0
    created_at: str = Field(default_factory=lambda: datetime.now().isoformat())
    updated_at: str = Field(default_factory=lambda: datetime.now().isoformat())
    revision: int = 0


async def _hydrate(
    session_id: str = DEFAULT_SESSION_ID,
) -> tuple[TimelineState, list[TimelineState], list[TimelineState], int]:
    """Load timeline + undo/redo stacks from SQLite (or empty defaults).

    Returns ``base_revision`` from the database row (``0`` when no row exists yet).
    """
    from backend.project.timeline.session_repository import load_session_timeline_raw

    raw = await load_session_timeline_raw(session_id)
    if raw is None:
        return TimelineState(), [], [], 0
    base_revision = int(raw.get("revision", 0))
    state = TimelineState.model_validate(raw["timeline"])
    state.revision = base_revision
    undo = [TimelineState.model_validate(x) for x in raw["undo"]]
    redo = [TimelineState.model_validate(x) for x in raw["redo"]]
    return state, undo, redo, base_revision


async def persist_timeline(
    state: TimelineState,
    undo_stack: list[TimelineState],
    redo_stack: list[TimelineState],
    session_id: str,
    expected_revision: int,
) -> int:
    """Write timeline + stacks to SQLite with optimistic concurrency.

    Raises ``TimelineConflictError`` from the session repository on revision mismatch.
    """
    from backend.project.timeline.session_repository import save_session_timeline_raw

    state.updated_at = datetime.now().isoformat()
    new_rev = await save_session_timeline_raw(
        state.model_dump(mode="json"),
        [x.model_dump(mode="json") for x in undo_stack],
        [x.model_dump(mode="json") for x in redo_stack],
        session_id=session_id,
        expected_revision=expected_revision,
    )
    state.revision = new_rev
    return new_rev


def _push_undo_before_mutate(
    current: TimelineState,
    undo_stack: list[TimelineState],
    redo_stack: list[TimelineState],
) -> None:
    """Snapshot current timeline before a mutating operation."""
    undo_stack.append(current.model_copy(deep=True))
    redo_stack.clear()
    if len(undo_stack) > 50:
        undo_stack.pop(0)


def _update_timeline_duration(timeline: TimelineState) -> None:
    """Update timeline duration based on clips."""
    max_end = 0.0
    for track in timeline.tracks:
        for clip in track.clips:
            if clip.end_time > max_end:
                max_end = clip.end_time
    timeline.duration = max_end
