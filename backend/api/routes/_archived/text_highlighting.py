"""
Text Highlighting Routes

Endpoints for text highlighting and synchronization with audio.
"""

from __future__ import annotations

import logging
import asyncio

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/text-highlighting", tags=["text-highlighting"])

# In-memory highlighting sessions (replace with database in production)
_highlighting_sessions: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class TextSegment(BaseModel):
    """A text segment with timing information."""

    id: str
    text: str
    start_time: float  # Start time in seconds
    end_time: float  # End time in seconds
    word_timings: list[dict[str, float]] | None = None
    # word_timings format: [{"word": "hello", "start": 0.0, "end": 0.5}]


class HighlightingSession(BaseModel):
    """A text highlighting session."""

    id: str
    audio_id: str
    text: str
    segments: list[TextSegment]
    current_time: float
    created: str  # ISO datetime string


class HighlightingCreateRequest(BaseModel):
    """Request to create a highlighting session."""

    audio_id: str
    text: str
    segments: list[TextSegment] | None = None


class HighlightingUpdateRequest(BaseModel):
    """Request to update highlighting."""

    current_time: float | None = None
    segments: list[TextSegment] | None = None


class HighlightingSyncRequest(BaseModel):
    """Request to sync highlighting with audio time."""

    audio_id: str
    current_time: float


class HighlightingSyncResponse(BaseModel):
    """Response from highlighting sync."""

    active_segment_id: str | None = None
    active_word_index: int | None = None
    segments: list[TextSegment]


@router.post("", response_model=HighlightingSession)
async def create_highlighting_session(request: HighlightingCreateRequest):
    """Create a new text highlighting session."""
    import uuid
    from datetime import datetime

    try:
        session_id = f"highlight-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        # In a real implementation, this would:
        # 1. Load audio and extract word timings
        # 2. Align text with audio using forced alignment
        # 3. Create segments with timing information

        segments = request.segments or []

        session = {
            "id": session_id,
            "audio_id": request.audio_id,
            "text": request.text,
            "segments": [s.model_dump() for s in segments],
            "current_time": 0.0,
            "created": now,
        }

        _highlighting_sessions[session_id] = session
        logger.info(f"Created highlighting session: {session_id}")

        return HighlightingSession(
            id=session_id,
            audio_id=request.audio_id,
            text=request.text,
            segments=segments,
            current_time=0.0,
            created=now,
        )
    except Exception as e:
        logger.error(f"Failed to create highlighting session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create session: {e!s}",
        ) from e


@router.get("/{session_id}", response_model=HighlightingSession)
async def get_highlighting_session(session_id: str):
    """Get a highlighting session."""
    if session_id not in _highlighting_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    session = _highlighting_sessions[session_id]
    return HighlightingSession(
        id=session["id"],
        audio_id=session["audio_id"],
        text=session["text"],
        segments=[TextSegment(**s) for s in session.get("segments", [])],
        current_time=session.get("current_time", 0.0),
        created=session["created"],
    )


@router.put("/{session_id}", response_model=HighlightingSession)
async def update_highlighting_session(session_id: str, request: HighlightingUpdateRequest):
    """Update a highlighting session."""
    if session_id not in _highlighting_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    try:
        session = _highlighting_sessions[session_id].copy()

        if request.current_time is not None:
            session["current_time"] = request.current_time

        if request.segments is not None:
            session["segments"] = [s.model_dump() for s in request.segments]

        _highlighting_sessions[session_id] = session

        return HighlightingSession(
            id=session["id"],
            audio_id=session["audio_id"],
            text=session["text"],
            segments=[TextSegment(**s) for s in session.get("segments", [])],
            current_time=session.get("current_time", 0.0),
            created=session["created"],
        )
    except Exception as e:
        logger.error(f"Failed to update highlighting session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update session: {e!s}",
        ) from e


@router.post("/sync", response_model=HighlightingSyncResponse)
async def sync_highlighting(request: HighlightingSyncRequest):
    """Sync highlighting with current audio playback time."""
    # Find session by audio_id
    session = None
    for s in _highlighting_sessions.values():
        if s.get("audio_id") == request.audio_id:
            session = s
            break

    if not session:
        raise HTTPException(status_code=404, detail="Session not found")

    # Find active segment and word
    active_segment_id = None
    active_word_index = None
    segments = [TextSegment(**s) for s in session.get("segments", [])]

    for segment in segments:
        if segment.start_time <= request.current_time <= segment.end_time:
            active_segment_id = segment.id

            # Find active word if word timings available
            if segment.word_timings:
                for idx, word_timing in enumerate(segment.word_timings):
                    word_start = word_timing.get("start", 0.0)
                    word_end = word_timing.get("end", 0.0)
                    if word_start <= request.current_time <= word_end:
                        active_word_index = idx
                        break
            break

    return HighlightingSyncResponse(
        active_segment_id=active_segment_id,
        active_word_index=active_word_index,
        segments=segments,
    )


@router.delete("/{session_id}")
async def delete_highlighting_session(session_id: str):
    """Delete a highlighting session."""
    if session_id not in _highlighting_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    del _highlighting_sessions[session_id]
    logger.info(f"Deleted highlighting session: {session_id}")
    return {"success": True}


@router.get("/sessions", response_model=list[HighlightingSession])
async def list_highlighting_sessions():
    """List all highlighting sessions."""
    sessions = list(_highlighting_sessions.values())

    # Sort by created date (newest first)
    sessions.sort(key=lambda s: s.get("created", ""), reverse=True)

    return [
        HighlightingSession(
            id=session["id"],
            audio_id=session["audio_id"],
            text=session["text"],
            segments=[TextSegment(**s) for s in session.get("segments", [])],
            current_time=session.get("current_time", 0.0),
            created=session["created"],
        )
        for session in sessions
    ]


class HighlightingPersistRequest(BaseModel):
    """Request to persist a highlighting session."""

    session_id: str | None = None  # Optional since it's in path
    audio_id: str
    text: str
    segments: list[dict]  # Accept dict format from ViewModel
    created: str


@router.post("/{session_id}/persist")
async def persist_highlighting_session(session_id: str, request: HighlightingPersistRequest):
    """Persist a highlighting session to storage."""
    if session_id not in _highlighting_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    try:
        # Update session with persisted data
        session = _highlighting_sessions[session_id]
        session["audio_id"] = request.audio_id
        session["text"] = request.text

        # Convert dict segments to TextSegment objects, then to dict format
        segments_list = []
        for seg_dict in request.segments:
            if isinstance(seg_dict, dict):
                # Convert dict to TextSegment, then back to dict for storage
                segment = TextSegment(**seg_dict)
                segments_list.append(segment.model_dump())
            else:
                segments_list.append(seg_dict)

        session["segments"] = segments_list
        session["created"] = request.created

        _highlighting_sessions[session_id] = session
        logger.info(f"Persisted highlighting session: {session_id}")

        return {"success": True, "message": "Session persisted"}
    except Exception as e:
        logger.error(f"Failed to persist highlighting session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to persist session: {e!s}",
        ) from e
