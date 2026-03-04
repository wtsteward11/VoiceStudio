"""
Automation Curves Routes

Endpoints for managing automation curves for track parameters.
Supports CRUD operations, point editing, and curve interpolation.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/automation", tags=["automation"])

# In-memory automation curves storage (replace with database in production)
from backend.services.persistent_store import PersistentStore

_automation_curves: PersistentStore = PersistentStore("automation_curves")
_state_lock = asyncio.Lock()


class AutomationPoint(BaseModel):
    """A point on an automation curve."""

    time: float  # Time in seconds
    value: float  # Parameter value
    bezier_handle_in_x: float | None = None
    bezier_handle_in_y: float | None = None
    bezier_handle_out_x: float | None = None
    bezier_handle_out_y: float | None = None


class AutomationCurve(BaseModel):
    """An automation curve for a parameter."""

    id: str
    name: str
    parameter_id: str  # e.g., "volume", "pitch", "speed"
    track_id: str
    points: list[AutomationPoint] = []
    interpolation: str = "linear"  # "linear", "bezier", "step"
    created: str  # ISO datetime string
    modified: str  # ISO datetime string


class AutomationCurveCreateRequest(BaseModel):
    """Request to create an automation curve."""

    name: str
    parameter_id: str
    track_id: str
    interpolation: str = "linear"


class AutomationCurveUpdateRequest(BaseModel):
    """Request to update an automation curve."""

    name: str | None = None
    points: list[AutomationPoint] | None = None
    interpolation: str | None = None


@router.get("", response_model=list[AutomationCurve])
@cache_response(ttl=30)  # Cache for 30 seconds (automation curves may change)
async def get_automation_curves(
    track_id: str | None = Query(None),
    parameter_id: str | None = Query(None),
):
    """Get all automation curves, optionally filtered."""
    curves = list(_automation_curves.values())

    if track_id:
        curves = [c for c in curves if c.get("track_id") == track_id]

    if parameter_id:
        curves = [c for c in curves if c.get("parameter_id") == parameter_id]

    # Sort by track_id, then parameter_id
    curves.sort(key=lambda c: (c.get("track_id", ""), c.get("parameter_id", "")))

    return [
        AutomationCurve(
            id=str(c.get("id", "")),
            name=str(c.get("name", "")),
            parameter_id=str(c.get("parameter_id", "")),
            track_id=str(c.get("track_id", "")),
            points=[
                AutomationPoint(
                    time=p.get("time", 0.0),
                    value=p.get("value", 0.0),
                    bezier_handle_in_x=p.get("bezier_handle_in_x"),
                    bezier_handle_in_y=p.get("bezier_handle_in_y"),
                    bezier_handle_out_x=p.get("bezier_handle_out_x"),
                    bezier_handle_out_y=p.get("bezier_handle_out_y"),
                )
                for p in c.get("points", [])
            ],
            interpolation=str(c.get("interpolation", "linear")),
            created=str(c.get("created", "")),
            modified=str(c.get("modified", "")),
        )
        for c in curves
    ]


@router.get("/tracks")
async def list_automation_tracks():
    """
    List available automation tracks.

    Returns unique track IDs from all stored automation curves.
    In a production system, this would query from a database of tracks.
    """
    # Extract unique track IDs from stored automation curves
    track_ids = set()
    for curve in _automation_curves.values():
        if "track_id" in curve:
            track_ids.add(curve["track_id"])

    if not track_ids:
        # No automation curves exist yet - return empty list
        # This is expected when no curves have been created
        return []

    # Return basic track info from existing curves
    return [{"id": track_id, "has_automation": True} for track_id in sorted(track_ids)]


@router.get("/{curve_id}", response_model=AutomationCurve)
@cache_response(ttl=60)  # Cache for 60 seconds (curve info is relatively static)
async def get_automation_curve(curve_id: str):
    """Get a specific automation curve."""
    if curve_id not in _automation_curves:
        raise HTTPException(status_code=404, detail="Automation curve not found")

    curve = _automation_curves[curve_id]
    return AutomationCurve(
        id=str(curve.get("id", "")),
        name=str(curve.get("name", "")),
        parameter_id=str(curve.get("parameter_id", "")),
        track_id=str(curve.get("track_id", "")),
        points=[
            AutomationPoint(
                time=p.get("time", 0.0),
                value=p.get("value", 0.0),
                bezier_handle_in_x=p.get("bezier_handle_in_x"),
                bezier_handle_in_y=p.get("bezier_handle_in_y"),
                bezier_handle_out_x=p.get("bezier_handle_out_x"),
                bezier_handle_out_y=p.get("bezier_handle_out_y"),
            )
            for p in curve.get("points", [])
        ],
        interpolation=str(curve.get("interpolation", "linear")),
        created=str(curve.get("created", "")),
        modified=str(curve.get("modified", "")),
    )


@router.post("", response_model=AutomationCurve)
async def create_automation_curve(request: AutomationCurveCreateRequest):
    """Create a new automation curve."""
    import uuid

    curve_id = f"automation-{uuid.uuid4().hex[:8]}"
    now = datetime.utcnow().isoformat()

    curve = {
        "id": curve_id,
        "name": request.name,
        "parameter_id": request.parameter_id,
        "track_id": request.track_id,
        "points": [],
        "interpolation": request.interpolation,
        "created": now,
        "modified": now,
    }

    _automation_curves[curve_id] = curve
    return AutomationCurve(
        id=curve_id,
        name=request.name,
        parameter_id=request.parameter_id,
        track_id=request.track_id,
        points=[],
        interpolation=request.interpolation,
        created=now,
        modified=now,
    )


@router.put("/{curve_id}", response_model=AutomationCurve)
async def update_automation_curve(curve_id: str, request: AutomationCurveUpdateRequest):
    """Update an automation curve."""
    if curve_id not in _automation_curves:
        raise HTTPException(status_code=404, detail="Automation curve not found")

    curve = _automation_curves[curve_id].copy()

    if request.name is not None:
        curve["name"] = request.name
    if request.points is not None:
        curve["points"] = [
            {
                "time": p.time,
                "value": p.value,
                "bezier_handle_in_x": p.bezier_handle_in_x,
                "bezier_handle_in_y": p.bezier_handle_in_y,
                "bezier_handle_out_x": p.bezier_handle_out_x,
                "bezier_handle_out_y": p.bezier_handle_out_y,
            }
            for p in request.points
        ]
        # Sort points by time
        curve["points"].sort(key=lambda p: p.get("time", 0.0))
    if request.interpolation is not None:
        curve["interpolation"] = request.interpolation

    curve["modified"] = datetime.utcnow().isoformat()
    _automation_curves[curve_id] = curve

    return AutomationCurve(
        id=str(curve.get("id", "")),
        name=str(curve.get("name", "")),
        parameter_id=str(curve.get("parameter_id", "")),
        track_id=str(curve.get("track_id", "")),
        points=[
            AutomationPoint(
                time=p.get("time", 0.0),
                value=p.get("value", 0.0),
                bezier_handle_in_x=p.get("bezier_handle_in_x"),
                bezier_handle_in_y=p.get("bezier_handle_in_y"),
                bezier_handle_out_x=p.get("bezier_handle_out_x"),
                bezier_handle_out_y=p.get("bezier_handle_out_y"),
            )
            for p in curve.get("points", [])
        ],
        interpolation=str(curve.get("interpolation", "linear")),
        created=str(curve.get("created", "")),
        modified=str(curve.get("modified", "")),
    )


@router.delete("/{curve_id}")
async def delete_automation_curve(curve_id: str):
    """Delete an automation curve."""
    if curve_id not in _automation_curves:
        raise HTTPException(status_code=404, detail="Automation curve not found")

    del _automation_curves[curve_id]
    return {"success": True}


@router.post("/{curve_id}/points", response_model=AutomationCurve)
async def add_automation_point(curve_id: str, point: AutomationPoint):
    """Add a point to an automation curve."""
    if curve_id not in _automation_curves:
        raise HTTPException(status_code=404, detail="Automation curve not found")

    curve = _automation_curves[curve_id].copy()
    from datetime import datetime

    point_dict = {
        "time": point.time,
        "value": point.value,
        "bezier_handle_in_x": point.bezier_handle_in_x,
        "bezier_handle_in_y": point.bezier_handle_in_y,
        "bezier_handle_out_x": point.bezier_handle_out_x,
        "bezier_handle_out_y": point.bezier_handle_out_y,
    }

    curve["points"].append(point_dict)
    # Sort points by time
    curve["points"].sort(key=lambda p: p.get("time", 0.0))

    curve["modified"] = datetime.utcnow().isoformat()
    _automation_curves[curve_id] = curve

    return AutomationCurve(
        id=str(curve.get("id", "")),
        name=str(curve.get("name", "")),
        parameter_id=str(curve.get("parameter_id", "")),
        track_id=str(curve.get("track_id", "")),
        points=[
            AutomationPoint(
                time=p.get("time", 0.0),
                value=p.get("value", 0.0),
                bezier_handle_in_x=p.get("bezier_handle_in_x"),
                bezier_handle_in_y=p.get("bezier_handle_in_y"),
                bezier_handle_out_x=p.get("bezier_handle_out_x"),
                bezier_handle_out_y=p.get("bezier_handle_out_y"),
            )
            for p in curve.get("points", [])
        ],
        interpolation=str(curve.get("interpolation", "linear")),
        created=str(curve.get("created", "")),
        modified=str(curve.get("modified", "")),
    )


@router.delete("/{curve_id}/points/{point_index}")
async def delete_automation_point(curve_id: str, point_index: int):
    """Delete a point from an automation curve."""
    if curve_id not in _automation_curves:
        raise HTTPException(status_code=404, detail="Automation curve not found")

    curve = _automation_curves[curve_id].copy()
    from datetime import datetime

    if point_index < 0 or point_index >= len(curve.get("points", [])):
        raise HTTPException(status_code=400, detail="Invalid point index")

    curve["points"].pop(point_index)
    curve["modified"] = datetime.utcnow().isoformat()
    _automation_curves[curve_id] = curve

    return {"success": True}


@router.get("/tracks/{track_id}/parameters")
@cache_response(ttl=300)  # Cache for 5 minutes (track parameters are relatively static)
async def get_track_parameters(track_id: str):
    """Get available parameters for a track."""
    # Return common automation parameters available for tracks
    return {
        "parameters": [
            {"id": "volume", "name": "Volume", "min": 0.0, "max": 1.0},
            {"id": "pan", "name": "Pan", "min": -1.0, "max": 1.0},
            {"id": "pitch", "name": "Pitch", "min": -12.0, "max": 12.0},
            {"id": "speed", "name": "Speed", "min": 0.5, "max": 2.0},
            {"id": "reverb", "name": "Reverb", "min": 0.0, "max": 1.0},
            {"id": "delay", "name": "Delay", "min": 0.0, "max": 1.0},
        ]
    }

    # Endpoint moved above parametric routes
