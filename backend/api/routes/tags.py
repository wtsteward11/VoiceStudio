"""
Tag Management Routes

Endpoints for managing tags across the application.
Supports CRUD operations, tag usage tracking, and categorization.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, field_validator

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/tags", tags=["tags"])

# In-memory tags storage (replace with database in production)
_tags: dict[str, dict] = {}
_MAX_TAGS = 10000  # Maximum number of tags to prevent memory issues


class Tag(BaseModel):
    """A tag definition."""

    id: str
    name: str
    category: str | None = None
    color: str | None = None  # Hex color code
    description: str | None = None
    usage_count: int = 0  # How many resources use this tag
    created: str  # ISO datetime string
    modified: str  # ISO datetime string


class TagCreateRequest(BaseModel):
    """Request to create a tag."""

    name: str
    category: str | None = None
    color: str | None = None
    description: str | None = None

    @field_validator("name")
    @classmethod
    def validate_name(cls, v: str) -> str:
        """Validate tag name."""
        if not v or not v.strip():
            raise ValueError("Tag name cannot be empty")
        if len(v) > 100:
            raise ValueError("Tag name cannot exceed 100 characters")
        return v.strip()

    @field_validator("color")
    @classmethod
    def validate_color(cls, v: str | None) -> str | None:
        """Validate color format."""
        if v and not v.startswith("#"):
            raise ValueError("Color must be a hex code starting with #")
        if v and len(v) != 7:
            raise ValueError("Color must be a 6-digit hex code (e.g., #FF0000)")
        return v

    @field_validator("description")
    @classmethod
    def validate_description(cls, v: str | None) -> str | None:
        """Validate description length."""
        if v and len(v) > 500:
            raise ValueError("Description cannot exceed 500 characters")
        return v


class TagUpdateRequest(BaseModel):
    """Request to update a tag."""

    name: str | None = None
    category: str | None = None
    color: str | None = None
    description: str | None = None

    @field_validator("name")
    @classmethod
    def validate_name(cls, v: str | None) -> str | None:
        """Validate tag name."""
        if v is not None:
            if not v or not v.strip():
                raise ValueError("Tag name cannot be empty")
            if len(v) > 100:
                raise ValueError("Tag name cannot exceed 100 characters")
            return v.strip()
        return v

    @field_validator("color")
    @classmethod
    def validate_color(cls, v: str | None) -> str | None:
        """Validate color format."""
        if v:
            if not v.startswith("#"):
                raise ValueError("Color must be a hex code starting with #")
            if len(v) != 7:
                raise ValueError("Color must be a 6-digit hex code (e.g., #FF0000)")
        return v

    @field_validator("description")
    @classmethod
    def validate_description(cls, v: str | None) -> str | None:
        """Validate description length."""
        if v and len(v) > 500:
            raise ValueError("Description cannot exceed 500 characters")
        return v


class TagUsageResponse(BaseModel):
    """Response showing tag usage."""

    tag_id: str
    tag_name: str
    resources: list[dict]  # List of resources using this tag


# Initialize default tags
def _initialize_default_tags():
    """Initialize default tags."""
    from datetime import datetime

    now = datetime.utcnow().isoformat()
    default_tags = [
        {
            "id": "tag-voice-male",
            "name": "male",
            "category": "voice",
            "color": "#3B82F6",  # Blue
            "description": "Male voice",
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
        {
            "id": "tag-voice-female",
            "name": "female",
            "category": "voice",
            "color": "#EC4899",  # Pink
            "description": "Female voice",
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
        {
            "id": "tag-quality-high",
            "name": "high-quality",
            "category": "quality",
            "color": "#10B981",  # Green
            "description": "High quality audio",
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
        {
            "id": "tag-language-en",
            "name": "english",
            "category": "language",
            "color": "#8B5CF6",  # Purple
            "description": "English language",
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
    ]

    for tag in default_tags:
        _tags[tag["id"]] = tag


# Initialize on module load
_initialize_default_tags()


@router.get("", response_model=list[Tag])
@cache_response(ttl=60)  # Cache for 60 seconds (tags may change)
async def get_tags(
    category: str | None = Query(None),
    search: str | None = Query(None),
    limit: int = Query(100, ge=1, le=1000),
):
    """Get all tags, optionally filtered."""
    try:
        tags = list(_tags.values())

        # Filter by category if provided
        if category:
            tags = [t for t in tags if t.get("category") == category]

        # Filter by search term if provided (optimized for large lists)
        if search:
            search_lower = search.lower()
            # Pre-compile search terms for better performance
            tags = [
                t
                for t in tags
                if search_lower in t.get("name", "").lower()
                or (t.get("description") and search_lower in t.get("description", "").lower())
            ]

        # Sort by usage count (descending), then by name
        tags.sort(key=lambda t: (-t.get("usage_count", 0), t.get("name", "")))

        # Apply limit and convert to Tag models
        return [
            Tag(
                id=str(tag.get("id", "")),
                name=str(tag.get("name", "")),
                category=tag.get("category"),
                color=tag.get("color"),
                description=tag.get("description"),
                usage_count=int(tag.get("usage_count", 0)),
                created=str(tag.get("created", "")),
                modified=str(tag.get("modified", "")),
            )
            for tag in tags[:limit]
        ]
    except Exception as e:
        logger.error(f"Failed to get tags: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail="Unable to retrieve tags. Please try again later.",
        ) from e


@router.get("/{tag_id}", response_model=Tag)
@cache_response(ttl=300)  # Cache for 5 minutes (tag info is relatively static)
async def get_tag(tag_id: str):
    """Get a specific tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID."),
        )

    return Tag(**_tags[tag_id])


@router.post("", response_model=Tag)
async def create_tag(request: TagCreateRequest):
    """Create a new tag."""
    import uuid
    from datetime import datetime

    # Check storage limit
    if len(_tags) >= _MAX_TAGS:
        raise HTTPException(
            status_code=503,
            detail=(
                f"Maximum number of tags ({_MAX_TAGS}) reached. "
                "Please delete unused tags before creating new ones."
            ),
        )

    # Check if tag name already exists (case-insensitive)
    existing = next(
        (t for t in _tags.values() if t.get("name", "").lower() == request.name.lower()),
        None,
    )
    if existing:
        raise HTTPException(
            status_code=409,
            detail=(
                f"Tag '{request.name}' already exists. "
                "Please use a different name or update the existing tag."
            ),
        )

    try:
        tag_id = f"tag-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        tag = {
            "id": tag_id,
            "name": request.name,
            "category": request.category,
            "color": request.color,
            "description": request.description,
            "usage_count": 0,
            "created": now,
            "modified": now,
        }

        _tags[tag_id] = tag
        logger.info(f"Created tag: {tag_id} ({request.name})")
        return Tag(
            id=tag_id,
            name=request.name,
            category=request.category,
            color=request.color,
            description=request.description,
            usage_count=0,
            created=now,
            modified=now,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Failed to create tag '{request.name}': {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=("Unable to create tag. " "Please check your input and try again."),
        ) from e


@router.put("/{tag_id}", response_model=Tag)
async def update_tag(tag_id: str, request: TagUpdateRequest):
    """Update a tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID and try again."),
        )

    tag = _tags[tag_id].copy()
    from datetime import datetime

    # Check if new name conflicts with existing tag
    if request.name and request.name.lower() != tag.get("name", "").lower():
        existing = next(
            (
                t
                for t in _tags.values()
                if t.get("id") != tag_id and t.get("name", "").lower() == request.name.lower()
            ),
            None,
        )
        if existing:
            raise HTTPException(
                status_code=409,
                detail=(f"Tag '{request.name}' already exists. " "Please use a different name."),
            )

    if request.name is not None:
        tag["name"] = request.name
    if request.category is not None:
        tag["category"] = request.category
    if request.color is not None:
        tag["color"] = request.color
    if request.description is not None:
        tag["description"] = request.description

    tag["modified"] = datetime.utcnow().isoformat()
    _tags[tag_id] = tag

    return Tag(
        id=str(tag.get("id", "")),
        name=str(tag.get("name", "")),
        category=tag.get("category"),
        color=tag.get("color"),
        description=tag.get("description"),
        usage_count=int(tag.get("usage_count", 0)),
        created=str(tag.get("created", "")),
        modified=str(tag.get("modified", "")),
    )


@router.delete("/{tag_id}")
async def delete_tag(tag_id: str):
    """Delete a tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID and try again."),
        )

    try:
        tag = _tags[tag_id]
        usage_count = tag.get("usage_count", 0)
        if usage_count > 0:
            raise HTTPException(
                status_code=409,
                detail=(
                    f"Cannot delete tag '{tag_id}' because it is in use "
                    f"by {usage_count} resource(s). "
                    "Please remove the tag from all resources before deleting."
                ),
            )

        # Prevent deletion of default tags
        is_default = (
            tag_id.startswith("tag-voice-")
            or tag_id.startswith("tag-quality-")
            or tag_id.startswith("tag-language-")
        )
        if is_default:
            raise HTTPException(
                status_code=403,
                detail=(
                    "Cannot delete default system tags. "
                    "These tags are required by the application."
                ),
            )

        del _tags[tag_id]
        logger.info(f"Deleted tag: {tag_id}")
        return {"success": True}
    except HTTPException:
        raise
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete tag {tag_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=(f"Unable to delete tag '{tag_id}'. " "Please try again later."),
        ) from e


@router.get("/{tag_id}/usage", response_model=TagUsageResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (usage stats may update)
async def get_tag_usage(tag_id: str):
    """Get usage information for a tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID."),
        )

    tag = _tags[tag_id]
    tag_name = tag.get("name", "")
    resources: list[dict] = []

    # Query profiles that use this tag
    from .profiles import _profiles

    for profile_id, profile in _profiles.items():
        if tag_id in profile.tags or tag_name in profile.tags:
            resources.append(
                {
                    "type": "profile",
                    "id": profile_id,
                    "name": profile.name,
                    "url": f"/api/profiles/{profile_id}",
                }
            )

    # Query projects that might use this tag (via profiles)
    _projects_ref: Any = getattr(
        __import__("backend.api.routes.projects", fromlist=["_projects"]), "_projects", {}
    )

    for project_id, project in _projects_ref.items():
        # Check if any profile in the project uses this tag
        for profile_id in project.voice_profile_ids:
            if profile_id in _profiles:
                profile = _profiles[profile_id]
                if tag_id in profile.tags or tag_name in profile.tags:
                    resources.append(
                        {
                            "type": "project",
                            "id": project_id,
                            "name": project.name,
                            "url": (f"/api/projects/{project_id}"),
                        }
                    )
                    break  # Only add project once

    # Query audio files that might be tagged (via projects)
    # Audio files in projects could be tagged, but we don't have direct
    # tagging yet. This would require extending the audio file model to
    # include tags

    return TagUsageResponse(tag_id=tag_id, tag_name=tag_name, resources=resources)


@router.post("/{tag_id}/increment-usage")
async def increment_tag_usage(tag_id: str):
    """Increment the usage count for a tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID."),
        )

    tag = _tags[tag_id]
    tag["usage_count"] = tag.get("usage_count", 0) + 1
    from datetime import datetime

    tag["modified"] = datetime.utcnow().isoformat()
    _tags[tag_id] = tag

    return {"success": True, "usage_count": tag["usage_count"]}


@router.post("/{tag_id}/decrement-usage")
async def decrement_tag_usage(tag_id: str):
    """Decrement the usage count for a tag."""
    if tag_id not in _tags:
        raise HTTPException(
            status_code=404,
            detail=(f"Tag '{tag_id}' not found. " "Please check the tag ID."),
        )

    tag = _tags[tag_id]
    current_count = tag.get("usage_count", 0)
    if current_count > 0:
        tag["usage_count"] = current_count - 1
        from datetime import datetime

        tag["modified"] = datetime.utcnow().isoformat()
        _tags[tag_id] = tag

    return {"success": True, "usage_count": tag.get("usage_count", 0)}


@router.get("/categories/list")
@cache_response(ttl=600)  # Cache for 10 minutes (categories are static)
async def get_tag_categories():
    """Get list of tag categories."""
    categories = set()
    for tag in _tags.values():
        cat = tag.get("category")
        if cat:
            categories.add(cat)

    return {"categories": sorted(categories)}


@router.post("/merge")
async def merge_tags(source_tag_id: str, target_tag_id: str):
    """Merge two tags (move all usage from source to target)."""
    if source_tag_id not in _tags:
        raise HTTPException(status_code=404, detail="Source tag not found")
    if target_tag_id not in _tags:
        raise HTTPException(status_code=404, detail="Target tag not found")

    source_tag = _tags[source_tag_id]
    target_tag = _tags[target_tag_id]

    # Transfer usage count
    target_usage = target_tag.get("usage_count", 0)
    source_usage = source_tag.get("usage_count", 0)
    target_tag["usage_count"] = target_usage + source_usage
    from datetime import datetime

    target_tag["modified"] = datetime.utcnow().isoformat()
    _tags[target_tag_id] = target_tag

    # Delete source tag
    del _tags[source_tag_id]

    return {
        "success": True,
        "merged_into": target_tag_id,
        "target_usage_count": target_tag["usage_count"],
    }
