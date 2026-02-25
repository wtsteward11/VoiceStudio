"""
Timeline Markers Routes

Endpoints for managing timeline markers for navigation and organization.
"""

from __future__ import annotations

import logging
import asyncio
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, field_validator

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/markers", tags=["markers"])

# In-memory markers storage (replace with database in production)
from backend.api.routes._persistent_store import PersistentStore

_markers: PersistentStore = PersistentStore("markers")
_state_lock = asyncio.Lock()
_MAX_MARKERS = 10000  # Maximum number of markers to keep


class Marker(BaseModel):
    """A timeline marker."""

    id: str
    name: str
    time: float  # Time in seconds
    color: str = "#00FFFF"  # Hex color
    category: str | None = None
    description: str | None = None
    project_id: str
    created: str  # ISO datetime string
    modified: str  # ISO datetime string


class MarkerCreateRequest(BaseModel):
    """Request to create a marker."""

    name: str
    time: float
    color: str | None = None
    category: str | None = None
    description: str | None = None
    project_id: str

    @field_validator("name")
    @classmethod
    def validate_name(cls, v: str) -> str:
        """Validate marker name."""
        if not v or not v.strip():
            raise ValueError("Marker name cannot be empty")
        if len(v) > 100:
            raise ValueError("Marker name cannot exceed 100 characters")
        return v.strip()

    @field_validator("time")
    @classmethod
    def validate_time(cls, v: float) -> float:
        """Validate marker time."""
        if v < 0.0:
            raise ValueError("Marker time cannot be negative")
        return v

    @field_validator("color")
    @classmethod
    def validate_color(cls, v: str | None) -> str | None:
        """Validate color format."""
        if v and not v.startswith("#"):
            raise ValueError("Color must be a hex code starting with #")
        if v and len(v) != 7:
            raise ValueError("Color must be a 6-digit hex code (e.g., #00FFFF)")
        return v

    @field_validator("description")
    @classmethod
    def validate_description(cls, v: str | None) -> str | None:
        """Validate description length."""
        if v and len(v) > 500:
            raise ValueError("Description cannot exceed 500 characters")
        return v


class MarkerUpdateRequest(BaseModel):
    """Request to update a marker."""

    name: str | None = None
    time: float | None = None
    color: str | None = None
    category: str | None = None
    description: str | None = None

    @field_validator("name")
    @classmethod
    def validate_name(cls, v: str | None) -> str | None:
        """Validate marker name."""
        if v is not None:
            if not v or not v.strip():
                raise ValueError("Marker name cannot be empty")
            if len(v) > 100:
                raise ValueError("Marker name cannot exceed 100 characters")
            return v.strip()
        return v

    @field_validator("time")
    @classmethod
    def validate_time(cls, v: float | None) -> float | None:
        """Validate marker time."""
        if v is not None and v < 0.0:
            raise ValueError("Marker time cannot be negative")
        return v

    @field_validator("color")
    @classmethod
    def validate_color(cls, v: str | None) -> str | None:
        """Validate color format."""
        if v:
            if not v.startswith("#"):
                raise ValueError("Color must be a hex code starting with #")
            if len(v) != 7:
                raise ValueError("Color must be a 6-digit hex code (e.g., #00FFFF)")
        return v

    @field_validator("description")
    @classmethod
    def validate_description(cls, v: str | None) -> str | None:
        """Validate description length."""
        if v and len(v) > 500:
            raise ValueError("Description cannot exceed 500 characters")
        return v


@router.get("", response_model=list[Marker])
@cache_response(ttl=30)  # Cache for 30 seconds (markers may change frequently)
async def get_markers(
    project_id: str | None = Query(None),
    category: str | None = Query(None),
):
    """Get all markers, optionally filtered."""
    try:
        markers = list(_markers.values())

        # Filter by project if specified
        if project_id:
            markers = [m for m in markers if m.get("project_id") == project_id]

        # Filter by category if specified
        if category:
            markers = [m for m in markers if m.get("category") == category]

        # Sort by time
        markers.sort(key=lambda m: m.get("time", 0.0))

        return [
            Marker(
                id=str(m.get("id", "")),
                name=str(m.get("name", "")),
                time=m.get("time", 0.0),
                color=str(m.get("color", "#00FFFF")),
                category=m.get("category"),
                description=m.get("description"),
                project_id=str(m.get("project_id", "")),
                created=str(m.get("created", "")),
                modified=str(m.get("modified", "")),
            )
            for m in markers
        ]
    except Exception as e:
        logger.error(f"Failed to get markers: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to retrieve markers: {e!s}",
        ) from e


@router.get("/{marker_id}", response_model=Marker)
@cache_response(ttl=60)  # Cache for 60 seconds (marker info is relatively static)
async def get_marker(marker_id: str):
    """Get a specific marker."""
    try:
        if not marker_id or not marker_id.strip():
            raise HTTPException(status_code=400, detail="Marker ID is required")

        if marker_id not in _markers:
            logger.warning(f"Marker not found: {marker_id}")
            raise HTTPException(status_code=404, detail="Marker not found")

        marker = _markers[marker_id]
        logger.debug(f"Retrieved marker: {marker_id}")
        return Marker(
            id=str(marker.get("id", "")),
            name=str(marker.get("name", "")),
            time=marker.get("time", 0.0),
            color=str(marker.get("color", "#00FFFF")),
            category=marker.get("category"),
            description=marker.get("description"),
            project_id=str(marker.get("project_id", "")),
            created=str(marker.get("created", "")),
            modified=str(marker.get("modified", "")),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting marker {marker_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get marker: {e!s}")


@router.post("", response_model=Marker)
async def create_marker(request: MarkerCreateRequest):
    """Create a new marker."""
    import uuid

    # Check storage limit
    if len(_markers) >= _MAX_MARKERS:
        raise HTTPException(
            status_code=503,
            detail=(
                f"Maximum number of markers ({_MAX_MARKERS}) reached. "
                "Please delete unused markers first."
            ),
        )

    try:
        marker_id = f"marker-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        marker = {
            "id": marker_id,
            "name": request.name,
            "time": request.time,
            "color": request.color or "#00FFFF",
            "category": request.category,
            "description": request.description,
            "project_id": request.project_id,
            "created": now,
            "modified": now,
        }

        _markers[marker_id] = marker
        logger.info(f"Created marker: {marker_id} ({request.name})")
        return Marker(
            id=marker_id,
            name=request.name,
            time=request.time,
            color=request.color or "#00FFFF",
            category=request.category,
            description=request.description,
            project_id=request.project_id,
            created=now,
            modified=now,
        )
    except Exception as e:
        logger.error(f"Failed to create marker: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create marker: {e!s}",
        ) from e


@router.put("/{marker_id}", response_model=Marker)
async def update_marker(marker_id: str, request: MarkerUpdateRequest):
    """Update a marker."""
    try:
        if not marker_id or not marker_id.strip():
            raise HTTPException(status_code=400, detail="Marker ID is required")

        if marker_id not in _markers:
            logger.warning(f"Marker not found for update: {marker_id}")
            raise HTTPException(status_code=404, detail="Marker not found")
        marker = _markers[marker_id].copy()

        if request.name is not None:
            marker["name"] = request.name
        if request.time is not None:
            marker["time"] = request.time
        if request.color is not None:
            marker["color"] = request.color
        if request.category is not None:
            marker["category"] = request.category
        if request.description is not None:
            marker["description"] = request.description

        marker["modified"] = datetime.utcnow().isoformat()
        _markers[marker_id] = marker

        logger.info(f"Updated marker: {marker_id}")
        return Marker(
            id=str(marker.get("id", "")),
            name=str(marker.get("name", "")),
            time=marker.get("time", 0.0),
            color=str(marker.get("color", "#00FFFF")),
            category=marker.get("category"),
            description=marker.get("description"),
            project_id=str(marker.get("project_id", "")),
            created=str(marker.get("created", "")),
            modified=str(marker.get("modified", "")),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update marker {marker_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update marker: {e!s}",
        ) from e


@router.delete("/{marker_id}")
async def delete_marker(marker_id: str):
    """Delete a marker."""
    try:
        if not marker_id or not marker_id.strip():
            raise HTTPException(status_code=400, detail="Marker ID is required")

        if marker_id not in _markers:
            logger.warning(f"Marker not found for deletion: {marker_id}")
            raise HTTPException(status_code=404, detail="Marker not found")

        del _markers[marker_id]
        logger.info(f"Deleted marker: {marker_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete marker {marker_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete marker: {e!s}",
        ) from e


@router.get("/categories/list")
@cache_response(ttl=300)  # Cache for 5 minutes (categories are relatively static)
async def get_categories(project_id: str | None = Query(None)):
    """Get all marker categories for a project."""
    try:
        markers = list(_markers.values())

        if project_id:
            markers = [m for m in markers if m.get("project_id") == project_id]

        categories = set()
        for marker in markers:
            cat = marker.get("category")
            if cat:
                categories.add(cat)

        logger.debug(f"Retrieved {len(categories)} categories for project: {project_id or 'all'}")
        return {"categories": sorted(categories)}
    except Exception as e:
        logger.error(f"Error getting categories for project {project_id}: {e!s}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get categories: {e!s}",
        ) from e


# =============================================================================
# Project-scoped marker routes (for UI compatibility)
# =============================================================================

project_markers_router = APIRouter(prefix="/api/projects", tags=["project-markers"])


@project_markers_router.get("/{project_id}/markers", response_model=list[Marker])
@cache_response(ttl=30)
async def get_project_markers(project_id: str):
    """Get all markers for a specific project."""
    try:
        if not project_id or not project_id.strip():
            raise HTTPException(status_code=400, detail="Project ID is required")

        markers = [m for m in _markers.values() if m.get("project_id") == project_id]
        markers.sort(key=lambda m: m.get("time", 0))

        logger.debug(f"Retrieved {len(markers)} markers for project: {project_id}")
        return markers
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting markers for project {project_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get project markers: {e!s}",
        ) from e


@project_markers_router.get("/{project_id}/markers/{marker_id}", response_model=Marker)
@cache_response(ttl=60)
async def get_project_marker(project_id: str, marker_id: str):
    """Get a specific marker from a project."""
    try:
        if not project_id or not project_id.strip():
            raise HTTPException(status_code=400, detail="Project ID is required")
        if not marker_id or not marker_id.strip():
            raise HTTPException(status_code=400, detail="Marker ID is required")

        if marker_id not in _markers:
            raise HTTPException(status_code=404, detail="Marker not found")

        marker = _markers[marker_id]
        if marker.get("project_id") != project_id:
            raise HTTPException(status_code=404, detail="Marker not found in this project")

        return marker
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting marker {marker_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get marker: {e!s}",
        ) from e
