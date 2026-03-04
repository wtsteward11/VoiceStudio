"""
Preset Library Routes

Unified endpoint for managing all types of presets:
- Mixer presets
- Effect presets
- Voice presets
- Engine presets
- Template presets
"""

from __future__ import annotations

import asyncio
import logging
import uuid
from datetime import datetime
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/presets", tags=["presets"])

from backend.services.persistent_store import PersistentStore

_presets: PersistentStore = PersistentStore("presets")
_MAX_PRESETS = 1000
_preset_timestamps: PersistentStore = PersistentStore("preset_timestamps")
_state_lock = asyncio.Lock()


def _cleanup_old_presets():
    """
    Clean up old presets from storage.

    Removes presets beyond MAX_PRESETS (oldest first based on creation time).
    """
    if len(_presets) > _MAX_PRESETS:
        sorted_presets = sorted(
            _preset_timestamps.items(),
            key=lambda x: x[1],
        )
        excess = len(_presets) - _MAX_PRESETS
        for preset_id, _ in sorted_presets[:excess]:
            _presets.pop(preset_id, None)
            _preset_timestamps.pop(preset_id, None)
        logger.info(f"Cleaned up {excess} old presets from storage")


class PresetType:
    """Preset type constants."""

    MIXER = "mixer"
    EFFECT = "effect"
    VOICE = "voice"
    ENGINE = "engine"
    TEMPLATE = "template"
    MACRO = "macro"


class Preset(BaseModel):
    """A preset in the library."""

    id: str
    name: str
    type: str  # PresetType
    category: str | None = None
    description: str | None = None
    data: dict = {}  # Preset-specific data
    tags: list[str] = []
    created: datetime
    modified: datetime
    author: str | None = None
    version: str = "1.0"
    is_public: bool = False
    usage_count: int = 0


class PresetSearchRequest(BaseModel):
    """Request to search presets."""

    query: str | None = None
    preset_type: str | None = None
    category: str | None = None
    tags: list[str] | None = None
    limit: int = 100
    offset: int = 0


class PresetCreateRequest(BaseModel):
    """Request to create a preset."""

    name: str
    preset_type: str
    category: str | None = None
    description: str | None = None
    data: dict | None = None
    tags: list[str] | None = None
    author: str | None = None
    is_public: bool = False


class PresetSearchResponse(BaseModel):
    """Response from preset search."""

    presets: list[Preset]
    total: int
    limit: int
    offset: int


@router.get("", response_model=PresetSearchResponse)
@cache_response(ttl=30)  # Cache for 30 seconds (preset search results may change)
async def search_presets(
    query: str | None = Query(None),
    preset_type: str | None = Query(None),
    category: str | None = Query(None),
    tags: str | None = Query(None),  # Comma-separated
    limit: int = Query(100, ge=1, le=1000),
    offset: int = Query(0, ge=0),
):
    """Search and filter presets."""
    filtered_presets = list(_presets.values())

    # Filter by type
    if preset_type:
        filtered_presets = [p for p in filtered_presets if p.get("type") == preset_type]

    # Filter by category
    if category:
        filtered_presets = [p for p in filtered_presets if p.get("category") == category]

    # Filter by tags
    if tags:
        tag_list = [t.strip() for t in tags.split(",")]
        filtered_presets = [
            p for p in filtered_presets if any(tag in p.get("tags", []) for tag in tag_list)
        ]

    # Filter by query (name/description search)
    if query:
        query_lower = query.lower()
        filtered_presets = [
            p
            for p in filtered_presets
            if (
                query_lower in p.get("name", "").lower()
                or query_lower in p.get("description", "").lower()
            )
        ]

    # Sort by usage count and modified date
    filtered_presets.sort(
        key=lambda x: (x.get("usage_count", 0), x.get("modified", "")),
        reverse=True,
    )

    total = len(filtered_presets)
    paginated = filtered_presets[offset : offset + limit]

    # Convert preset dicts to Preset models, ensuring datetime strings
    preset_models = []
    for preset in paginated:
        preset_dict = dict(preset)
        # Ensure created/modified are ISO strings
        if isinstance(preset_dict.get("created"), datetime):
            preset_dict["created"] = preset_dict["created"].isoformat()
        elif not isinstance(preset_dict.get("created"), str):
            preset_dict["created"] = datetime.utcnow().isoformat()
        if isinstance(preset_dict.get("modified"), datetime):
            preset_dict["modified"] = preset_dict["modified"].isoformat()
        elif not isinstance(preset_dict.get("modified"), str):
            preset_dict["modified"] = datetime.utcnow().isoformat()
        preset_models.append(Preset(**preset_dict))

    return PresetSearchResponse(
        presets=preset_models,
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get("/{preset_id}", response_model=Preset)
@cache_response(ttl=300)  # Cache for 5 minutes (preset info is relatively static)
async def get_preset(preset_id: str):
    """Get a specific preset."""
    if preset_id not in _presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    return Preset(**_presets[preset_id])


@router.post("", response_model=Preset)
async def create_preset(request: PresetCreateRequest):
    """Create a new preset."""
    import time

    preset_id = str(uuid.uuid4())
    now = datetime.utcnow()

    preset_data = {
        "id": preset_id,
        "name": request.name,
        "type": request.preset_type,
        "category": request.category,
        "description": request.description,
        "data": request.data or {},
        "tags": request.tags or [],
        "created": now.isoformat(),
        "modified": now.isoformat(),
        "author": request.author,
        "version": "1.0",
        "is_public": request.is_public,
        "usage_count": 0,
    }

    _presets[preset_id] = preset_data
    _preset_timestamps[preset_id] = time.time()

    # Clean up old presets if needed
    if len(_presets) > _MAX_PRESETS:
        _cleanup_old_presets()

    logger.info(f"Created preset {preset_id}: {request.name}")

    return Preset(
        id=preset_id,
        name=request.name,
        type=request.preset_type,
        category=request.category,
        description=request.description,
        data=request.data or {},
        tags=request.tags or [],
        created=now,
        modified=now,
        author=request.author,
        version="1.0",
        is_public=request.is_public,
        usage_count=0,
    )


class PresetUpdateRequest(BaseModel):
    """Request to update a preset."""

    name: str | None = None
    category: str | None = None
    description: str | None = None
    data: dict | None = None
    tags: list[str] | None = None
    is_public: bool | None = None


@router.put("/{preset_id}", response_model=Preset)
async def update_preset(preset_id: str, request: PresetUpdateRequest):
    """Update a preset."""
    if preset_id not in _presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    preset = _presets[preset_id]

    # Update fields
    if request.name is not None:
        preset["name"] = request.name
    if request.category is not None:
        preset["category"] = request.category
    if request.description is not None:
        preset["description"] = request.description
    if request.data is not None:
        preset["data"].update(request.data)
    if request.tags is not None:
        preset["tags"] = request.tags
    if request.is_public is not None:
        preset["is_public"] = request.is_public

    preset["modified"] = datetime.utcnow().isoformat()

    logger.info(f"Updated preset {preset_id}")

    # Ensure created/modified are ISO strings
    preset_dict = dict(preset)
    if isinstance(preset_dict.get("created"), datetime):
        preset_dict["created"] = preset_dict["created"].isoformat()
    if isinstance(preset_dict.get("modified"), datetime):
        preset_dict["modified"] = preset_dict["modified"].isoformat()

    return Preset(**preset_dict)


@router.delete("/{preset_id}")
async def delete_preset(preset_id: str):
    """Delete a preset."""
    if preset_id not in _presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    del _presets[preset_id]

    logger.info(f"Deleted preset {preset_id}")

    return {"success": True, "message": "Preset deleted"}


class PresetApplyRequest(BaseModel):
    """Request to apply a preset."""

    target_id: str | None = None  # Project ID, track ID, etc.


@router.post("/{preset_id}/apply")
async def apply_preset(preset_id: str, request: PresetApplyRequest):
    """Apply a preset to a target."""
    if preset_id not in _presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    preset = _presets[preset_id]

    # Increment usage count
    preset["usage_count"] = preset.get("usage_count", 0) + 1
    preset["modified"] = datetime.utcnow().isoformat()

    logger.info(f"Applied preset {preset_id} to {request.target_id}")

    return {
        "success": True,
        "preset_id": preset_id,
        "target_id": request.target_id,
        "data": preset.get("data", {}),
    }


class PresetTypeInfo(BaseModel):
    """Preset type information."""

    id: str
    name: str


class PresetTypesResponse(BaseModel):
    """Response with preset types."""

    types: list[PresetTypeInfo]


@router.get("/types", response_model=PresetTypesResponse)
@cache_response(ttl=600)  # Cache for 10 minutes (preset types are static)
async def get_preset_types():
    """Get list of available preset types."""
    return PresetTypesResponse(
        types=[
            PresetTypeInfo(id=PresetType.MIXER, name="Mixer"),
            PresetTypeInfo(id=PresetType.EFFECT, name="Effect"),
            PresetTypeInfo(id=PresetType.VOICE, name="Voice"),
            PresetTypeInfo(id=PresetType.ENGINE, name="Engine"),
            PresetTypeInfo(id=PresetType.TEMPLATE, name="Template"),
            PresetTypeInfo(id=PresetType.MACRO, name="Macro"),
        ]
    )


@router.get("/categories/{preset_type}", response_model=list[str])
@cache_response(ttl=600)  # Cache for 10 minutes (categories are static)
async def get_categories(preset_type: str):
    """Get categories for a specific preset type."""
    categories_map = {
        PresetType.MIXER: ["vocal", "instrumental", "podcast", "music"],
        PresetType.EFFECT: ["reverb", "delay", "compression", "eq", "distortion"],
        PresetType.VOICE: ["narrative", "character", "commercial", "audiobook"],
        PresetType.ENGINE: ["xtts", "tortoise", "chatterbox"],
        PresetType.TEMPLATE: ["project", "track", "scene"],
        PresetType.MACRO: ["automation", "batch", "workflow"],
    }

    return categories_map.get(preset_type, [])
