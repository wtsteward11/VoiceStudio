"""
Global Search API Routes
Implements IDEA 5: Global Search with Panel Context.
Implements IDEA 36: Advanced Search with Natural Language.
"""

from __future__ import annotations

import logging
import threading
from datetime import datetime, timedelta
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel, Field

from backend.services.audio_artifacts import AudioRegistry

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/search", tags=["search"])

# Use backend services (no route-to-route imports)
from backend.services.marker_store import get_marker_store
from backend.services.profile_search_service import get_profiles_for_search
from backend.services.project_search_service import get_projects_for_search
from backend.services.script_store import get_scripts_for_search

_markers: Any = None
_profiles: Any = None
_projects: Any = None
_scripts: Any = None
STORAGE_AVAILABLE = False
_storage_lock = threading.Lock()


def _load_search_storage() -> None:
    """
    Lazy-initialize search storage backends on first route request.

    GAP-069 slice 5: Avoid import-time DB / store access so unit tests can collect
    without a connected database. Assign globals only after all getters succeed.
    """
    global _markers, _profiles, _projects, _scripts, STORAGE_AVAILABLE
    if STORAGE_AVAILABLE:
        return
    with _storage_lock:
        if STORAGE_AVAILABLE:
            return
        markers = get_marker_store()
        profiles = get_profiles_for_search()
        projects = get_projects_for_search()
        scripts = get_scripts_for_search()
        _markers = markers
        _profiles = profiles
        _projects = projects
        _scripts = scripts
        STORAGE_AVAILABLE = True


class SearchResultItem(BaseModel):
    """Individual search result item."""

    id: str = Field(..., description="Item identifier")
    type: str = Field(..., description="Item type (profile, project, audio, marker, script)")
    title: str = Field(..., description="Item title/name")
    description: str | None = Field(None, description="Item description")
    panel_id: str = Field(..., description="Panel ID to navigate to")
    preview: str | None = Field(None, description="Preview text snippet")
    metadata: dict[str, Any] = Field(default_factory=dict, description="Additional metadata")


class ParsedQuery(BaseModel):
    """Parsed natural language query with extracted filters."""

    original_query: str = Field(..., description="Original query")
    search_terms: list[str] = Field(default_factory=list, description="Search terms")
    filters: dict[str, Any] = Field(default_factory=dict, description="Extracted filters")
    types: list[str] | None = Field(None, description="Content types to search")


class SearchResponse(BaseModel):
    """Response from global search."""

    query: str = Field(..., description="Search query")
    results: list[SearchResultItem] = Field(default_factory=list, description="Search results")
    total_results: int = Field(..., description="Total number of results")
    results_by_type: dict[str, int] = Field(
        default_factory=dict, description="Result count by type"
    )
    parsed_query: ParsedQuery | None = Field(
        None, description="Parsed natural language query (if applicable)"
    )


def _search_profiles(query: str, limit: int = 10) -> list[SearchResultItem]:
    """Search voice profiles."""
    if not STORAGE_AVAILABLE:
        return []

    results = []
    query_lower = query.lower()

    for profile_id, profile in _profiles.items():
        name = profile.get("name", "")
        description = profile.get("description", "")
        tags = profile.get("tags", [])

        # Search in name, description, tags
        if (
            query_lower in name.lower()
            or query_lower in description.lower()
            or any(query_lower in tag.lower() for tag in tags)
        ):

            # Create preview snippet
            preview_parts = []
            if query_lower in name.lower():
                preview_parts.append(f"Name: {name}")
            if description and query_lower in description.lower():
                preview_parts.append(description[:100])

            results.append(
                SearchResultItem(
                    id=profile_id,
                    type="profile",
                    title=name,
                    description=description,
                    panel_id="profiles",
                    preview=" | ".join(preview_parts) if preview_parts else None,
                    metadata={"language": profile.get("language"), "tags": tags},
                )
            )

    return results[:limit]


def _search_projects(query: str, limit: int = 10) -> list[SearchResultItem]:
    """Search projects."""
    if not STORAGE_AVAILABLE:
        return []

    results = []
    query_lower = query.lower()

    for project_id, project in _projects.items():
        name = project.get("name", "")
        description = project.get("description", "")

        if query_lower in name.lower() or query_lower in description.lower():
            preview_parts = []
            if query_lower in name.lower():
                preview_parts.append(f"Name: {name}")
            if description and query_lower in description.lower():
                preview_parts.append(description[:100])

            results.append(
                SearchResultItem(
                    id=project_id,
                    type="project",
                    title=name,
                    description=description,
                    panel_id="timeline",
                    preview=" | ".join(preview_parts) if preview_parts else None,
                    metadata={},
                )
            )

    return results[:limit]


def _search_audio_files(query: str, limit: int = 10) -> list[SearchResultItem]:
    """Search audio files."""
    if not STORAGE_AVAILABLE:
        return []

    results = []
    query_lower = query.lower()

    # Search in audio storage
    for audio_id, audio_path in AudioRegistry.items():
        # Extract filename from path
        import os

        filename = os.path.basename(audio_path)

        if query_lower in filename.lower():
            results.append(
                SearchResultItem(
                    id=audio_id,
                    type="audio",
                    title=filename,
                    description=None,
                    panel_id="library",
                    preview=f"Audio file: {filename}",
                    metadata={"path": audio_path},
                )
            )

    return results[:limit]


def _search_markers(query: str, limit: int = 10) -> list[SearchResultItem]:
    """Search timeline markers (flat store: marker_id -> marker)."""
    if not STORAGE_AVAILABLE:
        return []

    results = []
    query_lower = query.lower()

    for marker_id, marker in _markers.items():
        name = marker.get("name", "")
        description = marker.get("description", "")

        if query_lower in name.lower() or query_lower in description.lower():
            preview_parts = []
            if query_lower in name.lower():
                preview_parts.append(f"Name: {name}")
            if description and query_lower in description.lower():
                preview_parts.append(description[:100])

            results.append(
                SearchResultItem(
                    id=marker_id,
                    type="marker",
                    title=name,
                    description=description,
                    panel_id="timeline",
                    preview=" | ".join(preview_parts) if preview_parts else None,
                    metadata={"project_id": marker.get("project_id"), "time": marker.get("time")},
                )
            )

    return results[:limit]


def _search_scripts(query: str, limit: int = 10) -> list[SearchResultItem]:
    """Search scripts."""
    if not STORAGE_AVAILABLE:
        return []

    results = []
    query_lower = query.lower()

    for script_id, script in _scripts.items():
        name = script.get("name", "")
        text = script.get("text", "")

        if query_lower in name.lower() or query_lower in text.lower():
            # Create preview from text
            text_lower = text.lower()
            query_pos = text_lower.find(query_lower)
            if query_pos >= 0:
                start = max(0, query_pos - 50)
                end = min(len(text), query_pos + len(query) + 50)
                preview = text[start:end]
                if start > 0:
                    preview = "..." + preview
                if end < len(text):
                    preview = preview + "..."
            else:
                preview = text[:100] if text else None

            results.append(
                SearchResultItem(
                    id=script_id,
                    type="script",
                    title=name,
                    description=None,
                    panel_id="script_editor",
                    preview=preview,
                    metadata={"text_length": len(text)},
                )
            )

    return results[:limit]


def _parse_natural_language_query(query: str) -> ParsedQuery:
    """
    Parse natural language query and extract filters.

    Supports:
    - Time filters: "last week", "today", "recent", "from last week"
    - Quality filters: "high quality", "low quality", "good quality", "poor quality"
    - Type filters: "profile", "profiles", "audio", "clip", "preset"
    - Emotion filters: "sad", "happy", "angry", etc.
    - Date filters: "created today", "from last week"
    """
    parsed = ParsedQuery(original_query=query, types=None)
    query_lower = query.lower()

    # Extract search terms (remove filter keywords)
    search_terms = []
    words = query.split()
    filter_keywords = {
        "last",
        "week",
        "today",
        "recent",
        "from",
        "created",
        "high",
        "low",
        "good",
        "poor",
        "quality",
        "profile",
        "profiles",
        "audio",
        "clip",
        "preset",
        "sad",
        "happy",
        "angry",
        "neutral",
        "excited",
    }

    for word in words:
        if word.lower() not in filter_keywords:
            search_terms.append(word)

    parsed.search_terms = search_terms if search_terms else [query]

    # Extract time filters
    if "last week" in query_lower or "from last week" in query_lower:
        parsed.filters["date"] = "last_week"
        parsed.filters["date_from"] = (datetime.now() - timedelta(days=7)).isoformat()
    elif "today" in query_lower or "created today" in query_lower:
        parsed.filters["date"] = "today"
        parsed.filters["date_from"] = datetime.now().replace(hour=0, minute=0, second=0).isoformat()
    elif "recent" in query_lower:
        parsed.filters["date"] = "recent"
        parsed.filters["date_from"] = (datetime.now() - timedelta(days=7)).isoformat()

    # Extract quality filters
    if "high quality" in query_lower or "good quality" in query_lower:
        parsed.filters["quality"] = "high"
        parsed.filters["quality_min"] = 4.0
    elif "low quality" in query_lower or "poor quality" in query_lower:
        parsed.filters["quality"] = "low"
        parsed.filters["quality_max"] = 3.0

    # Extract type filters
    types_to_search = []
    if "profile" in query_lower or "profiles" in query_lower:
        types_to_search.append("profile")
    if "audio" in query_lower or "clip" in query_lower:
        types_to_search.append("audio")
    if "preset" in query_lower:
        types_to_search.append("preset")
    if "project" in query_lower or "projects" in query_lower:
        types_to_search.append("project")

    if types_to_search:
        parsed.types = types_to_search

    # Extract emotion filters
    emotions = ["sad", "happy", "angry", "neutral", "excited", "calm", "energetic"]
    for emotion in emotions:
        if emotion in query_lower:
            parsed.filters["emotion"] = emotion
            break

    return parsed


def _apply_quality_filter(
    results: list[SearchResultItem],
    quality_min: float | None = None,
    quality_max: float | None = None,
) -> list[SearchResultItem]:
    """Filter results by quality score if available in metadata."""
    if not quality_min and not quality_max:
        return results

    filtered = []
    for result in results:
        quality = result.metadata.get("quality_score")
        if quality is None:
            # If no quality info, include it (don't filter out)
            filtered.append(result)
        else:
            quality_value = float(quality) if isinstance(quality, (int, float, str)) else None
            if quality_value is not None:
                if quality_min and quality_value < quality_min:
                    continue
                if quality_max and quality_value > quality_max:
                    continue
            filtered.append(result)

    return filtered


@router.get("", response_model=SearchResponse)
@cache_response(ttl=30)  # Cache for 30 seconds (search results may change)
async def search(
    q: str = Query(..., description="Search query", min_length=2),
    types: str | None = Query(
        None,
        description="Comma-separated list of types to search (profile,project,audio,marker,script)",
    ),
    limit: int = Query(50, description="Maximum number of results per type", ge=1, le=100),
) -> SearchResponse:
    """
    Global search across all panels and content types.

    Implements IDEA 5: Global Search with Panel Context.

    Searches across:
    - Voice profiles (name, description, tags)
    - Projects (name, description)
    - Audio files (filename)
    - Timeline markers (name, description)
    - Scripts (name, text content)

    Args:
        q: Search query (minimum 2 characters)
        types: Optional filter by content types
        limit: Maximum results per type

    Returns:
        Search results grouped by type with preview snippets
    """
    if len(q) < 2:
        raise HTTPException(status_code=400, detail="Search query must be at least 2 characters")

    _load_search_storage()

    # Check storage availability - return proper error instead of empty results
    if not STORAGE_AVAILABLE:
        raise HTTPException(
            status_code=503,
            detail="Search service unavailable. Storage modules not loaded. Check backend configuration.",
        )

    try:
        # Parse natural language query
        parsed_query = _parse_natural_language_query(q)

        # Determine types to search
        type_filter = None
        if types:
            type_filter = [t.strip().lower() for t in types.split(",")]
        elif parsed_query.types:
            type_filter = parsed_query.types

        # Use parsed search terms or original query
        search_query = " ".join(parsed_query.search_terms) if parsed_query.search_terms else q

        all_results = []
        results_by_type = {}

        # Search profiles
        if not type_filter or "profile" in type_filter:
            profile_results = _search_profiles(search_query, limit)
            all_results.extend(profile_results)
            results_by_type["profile"] = len(profile_results)

        # Search projects
        if not type_filter or "project" in type_filter:
            project_results = _search_projects(search_query, limit)
            all_results.extend(project_results)
            results_by_type["project"] = len(project_results)

        # Search audio files
        if not type_filter or "audio" in type_filter:
            audio_results = _search_audio_files(search_query, limit)
            all_results.extend(audio_results)
            results_by_type["audio"] = len(audio_results)

        # Search markers
        if not type_filter or "marker" in type_filter:
            marker_results = _search_markers(search_query, limit)
            all_results.extend(marker_results)
            results_by_type["marker"] = len(marker_results)

        # Search scripts
        if not type_filter or "script" in type_filter:
            script_results = _search_scripts(search_query, limit)
            all_results.extend(script_results)
            results_by_type["script"] = len(script_results)

        # Apply quality filters if present
        if "quality_min" in parsed_query.filters or "quality_max" in parsed_query.filters:
            all_results = _apply_quality_filter(
                all_results,
                quality_min=parsed_query.filters.get("quality_min"),
                quality_max=parsed_query.filters.get("quality_max"),
            )

        return SearchResponse(
            query=q,
            results=all_results,
            total_results=len(all_results),
            results_by_type=results_by_type,
            parsed_query=parsed_query,
        )

    except Exception as e:
        logger.error(f"Search failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Search failed: {e!s}")
