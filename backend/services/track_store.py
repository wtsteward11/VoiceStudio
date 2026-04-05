"""Compatibility re-export — canonical implementation: `backend.project.tracks.track_store`."""

from backend.project.tracks.track_store import (
    TrackStore,
    get_track_store,
    reset_track_store,
)

__all__ = ["TrackStore", "get_track_store", "reset_track_store"]
