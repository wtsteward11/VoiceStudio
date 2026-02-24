"""
Gateway Endpoint Aliases

GAP-CRIT-001: Provides backward-compatible endpoint aliases for frontend gateways.

This module creates aliases for endpoints that the frontend expects but which have
different paths in the backend implementation. This allows the frontend to use
its expected paths while the backend maintains its organized route structure.

Aliases provided:
    - /api/voice/voices -> /api/voice-browser/voices (VoiceGateway)
    - /api/projects/{id}/timeline/* -> various timeline endpoints (TimelineGateway)
"""

from __future__ import annotations

import logging
import asyncio
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..deps import (
    ArtifactRefCounterDep,
    TrackStoreDep,
)

logger = logging.getLogger(__name__)

# ============================================================================
# Voice Aliases Router
# ============================================================================

voice_alias_router = APIRouter(prefix="/api/voice", tags=["voice-aliases"])


# Import the voice browser functionality
def _get_voice_browser_data():
    """Import voice browser data lazily to avoid circular imports."""
    try:
        from . import voice_browser

        return voice_browser
    except ImportError as e:
        logger.error(f"Failed to import voice_browser: {e}")
        raise HTTPException(status_code=500, detail="Voice browser module unavailable")


class VoiceInfoAlias(BaseModel):
    """Voice info model matching VoiceGateway expectations."""

    id: str
    name: str
    engine_id: str | None = None
    language: str | None = None
    gender: str | None = None
    description: str | None = None
    preview_url: str | None = None
    tags: list[str] = []


class VoicesListResponse(BaseModel):
    """Response model for voices list."""

    voices: list[VoiceInfoAlias]
    total: int


@voice_alias_router.get("/voices", response_model=list[VoiceInfoAlias])
async def get_available_voices(
    engine_id: str | None = Query(None, description="Filter by engine ID"),
) -> list[VoiceInfoAlias]:
    """
    Alias for /api/voice-browser/voices.

    Returns available voices, optionally filtered by engine.
    This endpoint exists for VoiceGateway compatibility.
    """
    try:
        vb = _get_voice_browser_data()

        # Get all voices from the catalog
        catalog = getattr(vb, "_voice_catalog", {})

        if not catalog:
            # Try to load if empty
            load_fn = getattr(vb, "_load_catalog", None)
            if load_fn:
                load_fn()
            catalog = getattr(vb, "_voice_catalog", {})

        voices = []
        for voice_id, voice_data in catalog.items():
            # Filter by engine if specified
            if engine_id and voice_data.get("engine_id") != engine_id:
                continue

            voices.append(
                VoiceInfoAlias(
                    id=voice_id,
                    name=voice_data.get("name", voice_id),
                    engine_id=voice_data.get("engine_id"),
                    language=voice_data.get("language"),
                    gender=voice_data.get("gender"),
                    description=voice_data.get("description"),
                    preview_url=voice_data.get("preview_url"),
                    tags=voice_data.get("tags", []),
                )
            )

        logger.info(f"Returned {len(voices)} voices (engine_id filter: {engine_id})")
        return voices
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting voices: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get voices: {e!s}")


# ============================================================================
# Timeline Aliases Router (for /api/projects/{project_id}/timeline/*)
# ============================================================================

timeline_alias_router = APIRouter(
    prefix="/api/projects/{project_id}/timeline",
    tags=["timeline-aliases"],
)


class TimelineDetailAlias(BaseModel):
    """Complete timeline detail matching TimelineGateway expectations."""

    project_id: str
    duration_seconds: float = 0.0
    tracks: list[dict] = []
    markers: list[dict] = []


class TrackInfoAlias(BaseModel):
    """Track info matching TimelineGateway.TrackInfo expectations."""

    id: str
    name: str
    project_id: str
    track_number: int
    clips: list[dict] = []
    engine: str | None = None


class TrackCreateRequestAlias(BaseModel):
    """Request to create a track."""

    name: str
    engine: str | None = None


class ClipInfoAlias(BaseModel):
    """Clip info matching TimelineGateway.ClipInfo expectations."""

    id: str
    name: str
    profile_id: str
    audio_id: str
    audio_url: str
    duration_seconds: float
    start_time: float
    engine: str | None = None
    quality_score: float | None = None


class ClipCreateRequestAlias(BaseModel):
    """Request to create a clip."""

    name: str
    profile_id: str
    audio_id: str
    audio_url: str
    duration_seconds: float
    start_time: float
    engine: str | None = None
    quality_score: float | None = None


class ClipUpdateRequestAlias(BaseModel):
    """Request to update a clip."""

    name: str | None = None
    start_time: float | None = None


class MarkerInfoAlias(BaseModel):
    """Marker info matching TimelineGateway.MarkerInfo expectations."""

    id: str
    name: str
    time_seconds: float
    color: str | None = None
    description: str | None = None


class MarkerCreateRequestAlias(BaseModel):
    """Request to create a marker."""

    name: str
    time_seconds: float
    color: str | None = None
    description: str | None = None


# In-memory marker storage (per project) - can be moved to persistent store later
_project_markers: dict[str, list[dict]] = {}
_state_lock = asyncio.Lock()


@timeline_alias_router.get("", response_model=TimelineDetailAlias)
async def get_timeline(
    project_id: str,
    track_store: TrackStoreDep,
) -> TimelineDetailAlias:
    """
    Get complete timeline for a project.

    Returns the timeline with all tracks and markers (GAP-CRIT-003).
    This is the composite endpoint that TimelineGateway expects.
    """
    try:
        # Get tracks from the track store
        track_data_list = track_store.list_tracks(project_id)

        # Get markers for this project
        markers = _project_markers.get(project_id, [])

        # Calculate duration from tracks and clips
        duration = 0.0
        for track in track_data_list:
            for clip in track.get("clips", []):
                clip_end = clip.get("start_time", 0.0) + clip.get("duration_seconds", 0.0)
                duration = max(duration, clip_end)

        return TimelineDetailAlias(
            project_id=project_id,
            duration_seconds=duration,
            tracks=track_data_list,
            markers=markers,
        )
    except Exception as e:
        logger.error(f"Error getting timeline for project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get timeline: {e!s}")


@timeline_alias_router.post("/tracks", response_model=TrackInfoAlias)
async def add_track(
    project_id: str,
    request: TrackCreateRequestAlias,
    track_store: TrackStoreDep,
) -> TrackInfoAlias:
    """
    Add a track to the project timeline.

    Alias for POST /api/projects/{project_id}/tracks
    """
    try:
        from . import tracks as tracks_module

        # Create the track request
        track_request = tracks_module.TrackCreateRequest(
            name=request.name,
            engine=request.engine,
        )

        # Use the existing create_track function
        result = tracks_module.create_track(project_id, track_request, track_store)

        return TrackInfoAlias(
            id=result.id,
            name=result.name,
            project_id=result.project_id,
            track_number=result.track_number,
            clips=[c.model_dump() for c in result.clips],
            engine=result.engine,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error adding track to project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to add track: {e!s}")


@timeline_alias_router.delete("/tracks/{track_id}")
async def remove_track(
    project_id: str,
    track_id: str,
    track_store: TrackStoreDep,
    ref_counter: ArtifactRefCounterDep,
) -> dict:
    """
    Remove a track from the project timeline.

    Alias for DELETE /api/projects/{project_id}/tracks/{track_id}
    """
    try:
        from . import tracks as tracks_module

        # Use the existing delete_track function
        tracks_module.delete_track(project_id, track_id, track_store, ref_counter)

        return {"ok": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error removing track {track_id} from project {project_id}: {e}", exc_info=True
        )
        raise HTTPException(status_code=500, detail=f"Failed to remove track: {e!s}")


@timeline_alias_router.post("/tracks/{track_id}/clips", response_model=ClipInfoAlias)
async def add_clip(
    project_id: str,
    track_id: str,
    request: ClipCreateRequestAlias,
    track_store: TrackStoreDep,
    ref_counter: ArtifactRefCounterDep,
) -> ClipInfoAlias:
    """
    Add a clip to a track.

    Alias for POST /api/projects/{project_id}/tracks/{track_id}/clips
    """
    try:
        from . import tracks as tracks_module

        clip_request = tracks_module.ClipCreateRequest(
            name=request.name,
            profile_id=request.profile_id,
            audio_id=request.audio_id,
            audio_url=request.audio_url,
            duration_seconds=request.duration_seconds,
            start_time=request.start_time,
            engine=request.engine,
            quality_score=request.quality_score,
        )

        result = tracks_module.create_clip(
            project_id, track_id, clip_request, track_store, ref_counter
        )

        return ClipInfoAlias(
            id=result.id,
            name=result.name,
            profile_id=result.profile_id,
            audio_id=result.audio_id,
            audio_url=result.audio_url,
            duration_seconds=result.duration_seconds,
            start_time=result.start_time,
            engine=result.engine,
            quality_score=result.quality_score,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error adding clip to track {track_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to add clip: {e!s}")


@timeline_alias_router.put("/tracks/{track_id}/clips/{clip_id}", response_model=ClipInfoAlias)
async def update_clip(
    project_id: str,
    track_id: str,
    clip_id: str,
    request: ClipUpdateRequestAlias,
    track_store: TrackStoreDep,
) -> ClipInfoAlias:
    """
    Update a clip in a track.

    Alias for PUT /api/projects/{project_id}/tracks/{track_id}/clips/{clip_id}
    """
    try:
        from . import tracks as tracks_module

        clip_request = tracks_module.ClipUpdateRequest(
            name=request.name,
            start_time=request.start_time,
        )

        result = tracks_module.update_clip(project_id, track_id, clip_id, clip_request, track_store)

        return ClipInfoAlias(
            id=result.id,
            name=result.name,
            profile_id=result.profile_id,
            audio_id=result.audio_id,
            audio_url=result.audio_url,
            duration_seconds=result.duration_seconds,
            start_time=result.start_time,
            engine=result.engine,
            quality_score=result.quality_score,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating clip {clip_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update clip: {e!s}")


@timeline_alias_router.delete("/tracks/{track_id}/clips/{clip_id}")
async def remove_clip(
    project_id: str,
    track_id: str,
    clip_id: str,
    track_store: TrackStoreDep,
    ref_counter: ArtifactRefCounterDep,
) -> dict:
    """
    Remove a clip from a track.

    Alias for DELETE /api/projects/{project_id}/tracks/{track_id}/clips/{clip_id}
    """
    try:
        from . import tracks as tracks_module

        tracks_module.delete_clip(project_id, track_id, clip_id, track_store, ref_counter)

        return {"ok": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error removing clip {clip_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to remove clip: {e!s}")


@timeline_alias_router.post("/markers", response_model=MarkerInfoAlias)
async def add_marker(
    project_id: str,
    request: MarkerCreateRequestAlias,
) -> MarkerInfoAlias:
    """
    Add a marker to the project timeline.
    """
    try:
        import uuid

        marker: dict[str, Any] = {
            "id": str(uuid.uuid4()),
            "name": request.name,
            "time_seconds": request.time_seconds,
            "color": request.color,
            "description": request.description,
        }

        if project_id not in _project_markers:
            _project_markers[project_id] = []
        _project_markers[project_id].append(marker)

        logger.info(f"Added marker {marker['id']} to project {project_id}")

        return MarkerInfoAlias(**marker)
    except Exception as e:
        logger.error(f"Error adding marker to project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to add marker: {e!s}")


@timeline_alias_router.delete("/markers/{marker_id}")
async def remove_marker(
    project_id: str,
    marker_id: str,
) -> dict:
    """
    Remove a marker from the project timeline.
    """
    try:
        markers = _project_markers.get(project_id, [])
        original_count = len(markers)

        _project_markers[project_id] = [m for m in markers if m.get("id") != marker_id]

        if len(_project_markers[project_id]) == original_count:
            raise HTTPException(status_code=404, detail="Marker not found")

        logger.info(f"Removed marker {marker_id} from project {project_id}")

        return {"ok": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error removing marker {marker_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to remove marker: {e!s}")
