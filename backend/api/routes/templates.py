"""
Template Management Routes

Endpoints for managing project templates.
Supports CRUD operations, template categories, and template application.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/templates", tags=["templates"])

from backend.services.persistent_store import PersistentStore

_templates: PersistentStore = PersistentStore("templates")
_state_lock = asyncio.Lock()


class Template(BaseModel):
    """A project template definition."""

    id: str
    name: str
    category: str
    description: str | None = None
    thumbnail_url: str | None = None
    project_data: dict = {}  # Template project structure
    tags: list[str] = []
    author: str | None = None
    version: str = "1.0"
    is_public: bool = False
    usage_count: int = 0
    created: str  # ISO datetime string
    modified: str  # ISO datetime string


class TemplateCreateRequest(BaseModel):
    """Request to create a template."""

    name: str
    category: str
    description: str | None = None
    thumbnail_url: str | None = None
    project_data: dict = {}
    tags: list[str] = []
    author: str | None = None
    is_public: bool = False


class TemplateUpdateRequest(BaseModel):
    """Request to update a template."""

    name: str | None = None
    category: str | None = None
    description: str | None = None
    thumbnail_url: str | None = None
    project_data: dict | None = None
    tags: list[str] | None = None
    is_public: bool | None = None


class TemplateApplyRequest(BaseModel):
    """Request to apply a template."""

    project_id: str | None = None  # Apply to existing project
    project_name: str | None = None  # Name for new project


# Initialize default templates
def _initialize_default_templates():
    """Initialize default templates."""
    now = datetime.utcnow().isoformat()
    default_templates = [
        {
            "id": "template-audiobook",
            "name": "Audiobook Production",
            "category": "production",
            "description": "Template for audiobook narration projects",
            "project_data": {
                "tracks": [],
                "settings": {"sample_rate": 44100, "channels": 2},
            },
            "tags": ["audiobook", "narration", "production"],
            "author": "VoiceStudio",
            "version": "1.0",
            "is_public": True,
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
        {
            "id": "template-podcast",
            "name": "Podcast Episode",
            "category": "production",
            "description": "Template for podcast episode production",
            "project_data": {
                "tracks": [],
                "settings": {"sample_rate": 48000, "channels": 2},
            },
            "tags": ["podcast", "episode", "production"],
            "author": "VoiceStudio",
            "version": "1.0",
            "is_public": True,
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
        {
            "id": "template-voiceover",
            "name": "Voiceover Project",
            "category": "production",
            "description": "Template for voiceover projects",
            "project_data": {
                "tracks": [],
                "settings": {"sample_rate": 48000, "channels": 1},
            },
            "tags": ["voiceover", "commercial", "production"],
            "author": "VoiceStudio",
            "version": "1.0",
            "is_public": True,
            "usage_count": 0,
            "created": now,
            "modified": now,
        },
    ]

    for template in default_templates:
        _templates[template["id"]] = template


# Initialize on module load
_initialize_default_templates()


@router.get("", response_model=list[Template])
@cache_response(ttl=60)  # Cache for 60 seconds (templates may change)
async def get_templates(
    category: str | None = Query(None),
    search: str | None = Query(None),
    is_public: bool | None = Query(None),
    limit: int = Query(100, ge=1, le=1000),
):
    """Get all templates, optionally filtered."""
    templates = list(_templates.values())

    if category:
        templates = [t for t in templates if t.get("category") == category]

    if is_public is not None:
        templates = [t for t in templates if t.get("is_public", False) == is_public]

    if search:
        search_lower = search.lower()
        templates = [
            t
            for t in templates
            if search_lower in t.get("name", "").lower()
            or search_lower in (t.get("description") or "").lower()
            or any(search_lower in tag.lower() for tag in t.get("tags", []))
        ]

    # Sort by usage count (descending), then by name
    templates.sort(key=lambda t: (-t.get("usage_count", 0), t.get("name", "")))

    return [
        Template(
            id=str(t.get("id", "")),
            name=str(t.get("name", "")),
            category=str(t.get("category", "")),
            description=t.get("description"),
            thumbnail_url=t.get("thumbnail_url"),
            project_data=t.get("project_data", {}),
            tags=t.get("tags", []),
            author=t.get("author"),
            version=str(t.get("version", "1.0")),
            is_public=t.get("is_public", False),
            usage_count=int(t.get("usage_count", 0)),
            created=str(t.get("created", "")),
            modified=str(t.get("modified", "")),
        )
        for t in templates[:limit]
    ]


@router.get("/{template_id}", response_model=Template)
@cache_response(ttl=300)  # Cache for 5 minutes (template info is relatively static)
async def get_template(template_id: str):
    """Get a specific template."""
    if template_id not in _templates:
        raise HTTPException(status_code=404, detail="Template not found")

    template = _templates[template_id]
    return Template(
        id=str(template.get("id", "")),
        name=str(template.get("name", "")),
        category=str(template.get("category", "")),
        description=template.get("description"),
        thumbnail_url=template.get("thumbnail_url"),
        project_data=template.get("project_data", {}),
        tags=template.get("tags", []),
        author=template.get("author"),
        version=str(template.get("version", "1.0")),
        is_public=template.get("is_public", False),
        usage_count=int(template.get("usage_count", 0)),
        created=str(template.get("created", "")),
        modified=str(template.get("modified", "")),
    )


@router.post("", response_model=Template)
async def create_template(request: TemplateCreateRequest):
    """Create a new template."""
    import uuid

    template_id = f"template-{uuid.uuid4().hex[:8]}"
    now = datetime.utcnow().isoformat()

    template = {
        "id": template_id,
        "name": request.name,
        "category": request.category,
        "description": request.description,
        "thumbnail_url": request.thumbnail_url,
        "project_data": request.project_data,
        "tags": request.tags,
        "author": request.author,
        "version": "1.0",
        "is_public": request.is_public,
        "usage_count": 0,
        "created": now,
        "modified": now,
    }

    _templates[template_id] = template
    return Template(
        id=template_id,
        name=request.name,
        category=request.category,
        description=request.description,
        thumbnail_url=request.thumbnail_url,
        project_data=request.project_data,
        tags=request.tags,
        author=request.author,
        version="1.0",
        is_public=request.is_public,
        usage_count=0,
        created=now,
        modified=now,
    )


@router.put("/{template_id}", response_model=Template)
async def update_template(template_id: str, request: TemplateUpdateRequest):
    """Update a template."""
    if template_id not in _templates:
        raise HTTPException(status_code=404, detail="Template not found")

    template = _templates[template_id].copy()
    from datetime import datetime

    if request.name is not None:
        template["name"] = request.name
    if request.category is not None:
        template["category"] = request.category
    if request.description is not None:
        template["description"] = request.description
    if request.thumbnail_url is not None:
        template["thumbnail_url"] = request.thumbnail_url
    if request.project_data is not None:
        template["project_data"] = request.project_data
    if request.tags is not None:
        template["tags"] = request.tags
    if request.is_public is not None:
        template["is_public"] = request.is_public

    template["modified"] = datetime.utcnow().isoformat()
    _templates[template_id] = template

    return Template(
        id=str(template.get("id", "")),
        name=str(template.get("name", "")),
        category=str(template.get("category", "")),
        description=template.get("description"),
        thumbnail_url=template.get("thumbnail_url"),
        project_data=template.get("project_data", {}),
        tags=template.get("tags", []),
        author=template.get("author"),
        version=str(template.get("version", "1.0")),
        is_public=template.get("is_public", False),
        usage_count=int(template.get("usage_count", 0)),
        created=str(template.get("created", "")),
        modified=str(template.get("modified", "")),
    )


@router.delete("/{template_id}")
async def delete_template(template_id: str):
    """Delete a template."""
    if template_id not in _templates:
        raise HTTPException(status_code=404, detail="Template not found")

    template = _templates[template_id]
    if template.get("is_public", False) and template.get("usage_count", 0) > 0:
        raise HTTPException(
            status_code=409,
            detail="Cannot delete public template that is in use",
        )

    del _templates[template_id]
    return {"success": True}


@router.post("/{template_id}/apply")
async def apply_template(template_id: str, request: TemplateApplyRequest):
    """Apply a template to create or update a project."""
    if template_id not in _templates:
        raise HTTPException(status_code=404, detail="Template not found")

    template = _templates[template_id]

    # Increment usage count
    template["usage_count"] = template.get("usage_count", 0) + 1
    from datetime import datetime

    template["modified"] = datetime.utcnow().isoformat()
    _templates[template_id] = template

    # In a real implementation, this would create or update a project
    # using the template's project_data
    project_id = request.project_id or f"project-{template_id}"

    return {
        "success": True,
        "project_id": project_id,
        "template_id": template_id,
        "message": "Template applied successfully",
    }


@router.get("/categories/list")
@cache_response(ttl=600)  # Cache for 10 minutes (categories are static)
async def get_template_categories():
    """Get list of template categories."""
    categories = set()
    for template in _templates.values():
        cat = template.get("category")
        if cat:
            categories.add(cat)

    return {"categories": sorted(categories)}
