"""
Image Search Routes

Endpoints for searching images from various sources.
"""

from __future__ import annotations

import logging
import os
import uuid

import httpx
from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/image-search", tags=["image-search"])

# In-memory storage for search history (replace with database in production)
_search_history: list[ImageSearchResult] = []


class ImageSearchResult(BaseModel):
    """Image search result."""

    result_id: str
    image_url: str
    thumbnail_url: str | None = None
    title: str
    description: str | None = None
    source: str  # Unsplash, Pexels, Pixabay, etc.
    width: int
    height: int
    file_size: int | None = None  # bytes
    license: str | None = None
    author: str | None = None
    author_url: str | None = None
    tags: list[str] = []
    metadata: dict[str, str] = {}


class ImageSearchRequest(BaseModel):
    """Request to search for images."""

    query: str
    source: str | None = None  # unsplash, pexels, pixabay, all
    category: str | None = None  # nature, people, abstract, etc.
    orientation: str | None = None  # landscape, portrait, square
    color: str | None = None  # red, blue, green, etc.
    min_width: int | None = None
    min_height: int | None = None
    page: int = 1
    per_page: int = 20


class ImageSearchResponse(BaseModel):
    """Image search response."""

    results: list[ImageSearchResult]
    total: int
    page: int
    per_page: int
    total_pages: int
    query: str
    source: str | None = None


class ImageSource(BaseModel):
    """Image source information."""

    source_id: str
    name: str
    description: str
    requires_api_key: bool = False
    is_available: bool = True


@router.post("/search", response_model=ImageSearchResponse)
async def search_images(request: ImageSearchRequest):
    """Search for images from various sources."""
    try:
        if not request.query or len(request.query.strip()) == 0:
            raise HTTPException(status_code=400, detail="Search query is required")

        query = request.query.strip()
        source = request.source or "all"

        logger.info(f"Searching images for query '{query}' from source '{source}'")

        # Implement real image search
        results = []

        # Try to get API keys from API key store service
        api_keys: dict[str, str] = {}
        try:
            from backend.services.api_key_store_service import get_api_keys_store

            api_key_storage = get_api_keys_store()
            # Try to get keys for different sources
            for source_name in ["unsplash", "pexels", "pixabay"]:
                # Search for key by service name
                for key_obj in api_key_storage.values():
                    if key_obj.service_name.lower() == source_name.lower() and key_obj.is_active:
                        api_keys[source_name] = key_obj.key_value
                        break
        except ImportError:
            logger.warning("APIKeyManager not available, using fallback search")

        # Search based on source
        sources_to_search = []
        sources_to_search = ["unsplash", "pexels", "pixabay"] if source == "all" else [source]

        # Try to search each source
        for search_source in sources_to_search:
            try:
                source_results = await _search_source(
                    query, search_source, request, api_keys.get(search_source)
                )
                results.extend(source_results)
            except Exception as e:
                logger.warning(f"Failed to search {search_source}: {e}")

        # If no results from APIs, try local search
        if not results:
            results = await _search_local_images(query, request)

        # Apply filters
        if request.min_width:
            results = [r for r in results if r.width >= request.min_width]
        if request.min_height:
            results = [r for r in results if r.height >= request.min_height]
        if request.orientation:
            results = [r for r in results if _matches_orientation(r, request.orientation)]
        if request.color:
            results = [r for r in results if _matches_color(r, request.color)]

        # Rank results by relevance
        results = _rank_results(results, query, request)

        # Paginate
        total = len(results)
        start = (request.page - 1) * request.per_page
        end = start + request.per_page
        paginated_results = results[start:end]

        total_pages = (total + request.per_page - 1) // request.per_page

        response = ImageSearchResponse(
            results=paginated_results,
            total=total,
            page=request.page,
            per_page=request.per_page,
            total_pages=total_pages,
            query=query,
            source=source,
        )

        # Store in search history
        _search_history.extend(paginated_results)

        return response
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to search images: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to search images: {e!s}",
        ) from e


@router.get("/sources", response_model=list[ImageSource])
async def list_image_sources():
    """List all available image search sources."""
    return [
        ImageSource(
            source_id="unsplash",
            name="Unsplash",
            description="High-quality free photos",
            requires_api_key=True,
            is_available=True,
        ),
        ImageSource(
            source_id="pexels",
            name="Pexels",
            description="Free stock photos and videos",
            requires_api_key=True,
            is_available=True,
        ),
        ImageSource(
            source_id="pixabay",
            name="Pixabay",
            description="Free images, videos, and music",
            requires_api_key=True,
            is_available=True,
        ),
        ImageSource(
            source_id="local",
            name="Local Library",
            description="Search local image library",
            requires_api_key=False,
            is_available=True,
        ),
    ]


@router.get("/history", response_model=list[ImageSearchResult])
async def get_search_history(
    limit: int = Query(50, ge=1, le=500),
    source: str | None = Query(None),
):
    """Get recent search history."""
    try:
        history = _search_history.copy()

        if source:
            history = [r for r in history if r.source == source]

        return history[-limit:]
    except Exception as e:
        logger.error(f"Failed to get search history: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get search history: {e!s}",
        ) from e


@router.delete("/history")
async def clear_search_history():
    """Clear search history."""
    try:
        _search_history.clear()
        return {"message": "Search history cleared successfully"}
    except Exception as e:
        logger.error(f"Failed to clear search history: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to clear search history: {e!s}",
        ) from e


@router.get("/categories", response_model=list[str])
async def list_categories():
    """List available image categories."""
    return [
        "nature",
        "people",
        "abstract",
        "architecture",
        "animals",
        "business",
        "food",
        "technology",
        "travel",
        "sports",
        "art",
        "fashion",
        "music",
        "education",
        "health",
    ]


@router.get("/colors", response_model=list[str])
async def list_colors():
    """List available color filters."""
    return [
        "red",
        "orange",
        "yellow",
        "green",
        "blue",
        "purple",
        "pink",
        "brown",
        "black",
        "white",
        "gray",
    ]


async def _search_source(
    query: str, source: str, request: ImageSearchRequest, api_key: str | None = None
) -> list[ImageSearchResult]:
    """Search images from a specific source."""
    results = []

    try:
        if source == "unsplash":
            results = await _search_unsplash(query, request, api_key)
        elif source == "pexels":
            results = await _search_pexels(query, request, api_key)
        elif source == "pixabay":
            results = await _search_pixabay(query, request, api_key)
    except Exception as e:
        logger.warning(f"Failed to search {source}: {e}")

    return results


async def _search_unsplash(
    query: str, request: ImageSearchRequest, api_key: str | None = None
) -> list[ImageSearchResult]:
    """Search Unsplash API."""
    results: list[ImageSearchResult] = []

    if not api_key:
        logger.debug("Unsplash API key not available")
        return results

    try:
        async with httpx.AsyncClient(timeout=10.0) as client:
            url = "https://api.unsplash.com/search/photos"
            params: dict[str, str | int] = {
                "query": query,
                "page": request.page,
                "per_page": request.per_page,
            }

            if request.orientation:
                params["orientation"] = request.orientation

            if request.color:
                params["color"] = request.color

            headers = {"Authorization": f"Client-ID {api_key}"}

            response = await client.get(url, params=params, headers=headers)
            response.raise_for_status()

            data = response.json()

            for item in data.get("results", []):
                result = ImageSearchResult(
                    result_id=f"unsplash_{item.get('id', uuid.uuid4().hex)}",
                    image_url=item.get("urls", {}).get("regular", ""),
                    thumbnail_url=item.get("urls", {}).get("thumb", ""),
                    title=item.get("description") or item.get("alt_description", ""),
                    description=item.get("description", ""),
                    source="unsplash",
                    width=item.get("width", 0),
                    height=item.get("height", 0),
                    license="Unsplash License",
                    author=item.get("user", {}).get("name", ""),
                    author_url=item.get("user", {}).get("links", {}).get("html", ""),
                    tags=[tag.get("title", "") for tag in item.get("tags", [])],
                )
                results.append(result)
    except Exception as e:
        logger.error(f"Failed to search Unsplash: {e}")

    return results


async def _search_pexels(
    query: str, request: ImageSearchRequest, api_key: str | None = None
) -> list[ImageSearchResult]:
    """Search Pexels API."""
    results: list[ImageSearchResult] = []

    if not api_key:
        logger.debug("Pexels API key not available")
        return results

    try:
        async with httpx.AsyncClient(timeout=10.0) as client:
            url = "https://api.pexels.com/v1/search"
            params: dict[str, str | int] = {
                "query": query,
                "page": request.page,
                "per_page": request.per_page,
            }

            if request.orientation:
                params["orientation"] = request.orientation

            if request.color:
                params["color"] = request.color

            headers = {"Authorization": api_key}

            response = await client.get(url, params=params, headers=headers)
            response.raise_for_status()

            data = response.json()

            for item in data.get("photos", []):
                result = ImageSearchResult(
                    result_id=f"pexels_{item.get('id', uuid.uuid4().hex)}",
                    image_url=item.get("src", {}).get("large", ""),
                    thumbnail_url=item.get("src", {}).get("medium", ""),
                    title=item.get("alt", ""),
                    description="",
                    source="pexels",
                    width=item.get("width", 0),
                    height=item.get("height", 0),
                    license="Pexels License",
                    author=item.get("photographer", ""),
                    author_url=item.get("photographer_url", ""),
                    tags=[],
                )
                results.append(result)
    except Exception as e:
        logger.error(f"Failed to search Pexels: {e}")

    return results


async def _search_pixabay(
    query: str, request: ImageSearchRequest, api_key: str | None = None
) -> list[ImageSearchResult]:
    """Search Pixabay API."""
    results: list[ImageSearchResult] = []

    if not api_key:
        logger.debug("Pixabay API key not available")
        return results

    try:
        async with httpx.AsyncClient(timeout=10.0) as client:
            url = "https://pixabay.com/api/"
            params: dict[str, str | int] = {
                "key": api_key,
                "q": query,
                "page": request.page,
                "per_page": request.per_page,
                "image_type": "photo",
            }

            if request.orientation:
                params["orientation"] = request.orientation

            if request.category:
                params["category"] = request.category

            if request.color:
                params["colors"] = request.color

            if request.min_width:
                params["min_width"] = request.min_width

            if request.min_height:
                params["min_height"] = request.min_height

            response = await client.get(url, params=params)
            response.raise_for_status()

            data = response.json()

            for item in data.get("hits", []):
                result = ImageSearchResult(
                    result_id=f"pixabay_{item.get('id', uuid.uuid4().hex)}",
                    image_url=item.get("largeImageURL", ""),
                    thumbnail_url=item.get("previewURL", ""),
                    title=item.get("tags", ""),
                    description="",
                    source="pixabay",
                    width=item.get("imageWidth", 0),
                    height=item.get("imageHeight", 0),
                    license="Pixabay License",
                    author=item.get("user", ""),
                    author_url=f"https://pixabay.com/users/{item.get('user', '')}",
                    tags=item.get("tags", "").split(", ") if item.get("tags") else [],
                )
                results.append(result)
    except Exception as e:
        logger.error(f"Failed to search Pixabay: {e}")

    return results


async def _search_local_images(query: str, request: ImageSearchRequest) -> list[ImageSearchResult]:
    """Search local image library."""
    results: list[ImageSearchResult] = []

    try:
        # Search in common image directories
        search_dirs = [
            os.path.join(os.path.expanduser("~"), "Pictures"),
            os.path.join(os.path.expanduser("~"), "Downloads"),
            os.path.join(os.path.expanduser("~"), "VoiceStudio", "images"),
        ]

        image_extensions = {".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"}

        for search_dir in search_dirs:
            if not os.path.exists(search_dir):
                continue

            for root, _dirs, files in os.walk(search_dir):
                for file in files:
                    if not any(file.lower().endswith(ext) for ext in image_extensions):
                        continue

                    # Simple filename matching
                    if query.lower() in file.lower():
                        file_path = os.path.join(root, file)

                        try:
                            # Get image dimensions
                            from PIL import Image

                            with Image.open(file_path) as img:
                                width, height = img.size
                                file_size = os.path.getsize(file_path)

                            result = ImageSearchResult(
                                result_id=f"local_{uuid.uuid4().hex[:8]}",
                                image_url=f"file://{file_path}",
                                thumbnail_url=f"file://{file_path}",
                                title=file,
                                description=f"Local image: {file}",
                                source="local",
                                width=width,
                                height=height,
                                file_size=file_size,
                                license="Local file",
                                tags=[query],
                            )
                            results.append(result)
                        except Exception as e:
                            logger.debug(f"Failed to process local image {file_path}: {e}")
                            continue
    except Exception as e:
        logger.warning(f"Failed to search local images: {e}")

    return results


def _matches_orientation(result: ImageSearchResult, orientation: str) -> bool:
    """Check if result matches orientation filter."""
    if not orientation:
        return True

    aspect_ratio = result.width / result.height if result.height > 0 else 1.0

    if orientation == "landscape":
        return aspect_ratio > 1.0
    elif orientation == "portrait":
        return aspect_ratio < 1.0
    elif orientation == "square":
        return 0.9 <= aspect_ratio <= 1.1

    return True


def _matches_color(result: ImageSearchResult, color: str) -> bool:
    """Check if result matches color filter."""
    if not color:
        return True

    # Check if color is mentioned in tags or title
    color_lower = color.lower()
    searchable_text = " ".join(
        [result.title or "", result.description or "", " ".join(result.tags or [])]
    ).lower()

    # Basic color matching in text
    color_keywords = {
        "red": ["red", "crimson", "scarlet", "ruby"],
        "orange": ["orange", "tangerine", "amber"],
        "yellow": ["yellow", "gold", "amber", "lemon"],
        "green": ["green", "emerald", "lime", "forest"],
        "blue": ["blue", "azure", "navy", "sky", "ocean"],
        "purple": ["purple", "violet", "lavender", "plum"],
        "pink": ["pink", "rose", "magenta", "fuchsia"],
        "brown": ["brown", "tan", "chocolate", "coffee"],
        "black": ["black", "dark", "shadow", "ebony"],
        "white": ["white", "light", "snow", "ivory"],
        "gray": ["gray", "grey", "silver", "ash"],
    }

    if color_lower in color_keywords:
        keywords = color_keywords[color_lower]
        return any(keyword in searchable_text for keyword in keywords)

    # Fallback: check if color name appears in text
    return color_lower in searchable_text


def _rank_results(
    results: list[ImageSearchResult], query: str, request: ImageSearchRequest
) -> list[ImageSearchResult]:
    """Rank search results by relevance."""
    if not results:
        return results

    query_lower = query.lower()
    query_words = set(query_lower.split())

    def calculate_relevance_score(result: ImageSearchResult) -> float:
        """Calculate relevance score for a result."""
        score = 0.0

        # Title match (highest weight)
        title_lower = (result.title or "").lower()
        if query_lower in title_lower:
            score += 10.0
        else:
            # Partial word matches in title
            title_words = set(title_lower.split())
            matching_words = query_words.intersection(title_words)
            score += len(matching_words) * 3.0

        # Description match (medium weight)
        description_lower = (result.description or "").lower()
        if query_lower in description_lower:
            score += 5.0
        else:
            desc_words = set(description_lower.split())
            matching_words = query_words.intersection(desc_words)
            score += len(matching_words) * 1.5

        # Tag match (high weight)
        tags_lower = [tag.lower() for tag in (result.tags or [])]
        for tag in tags_lower:
            if query_lower in tag or tag in query_lower:
                score += 4.0
            else:
                tag_words = set(tag.split())
                matching_words = query_words.intersection(tag_words)
                score += len(matching_words) * 2.0

        # Source preference (slight boost for certain sources)
        source_weights = {
            "unsplash": 0.5,  # High quality
            "pexels": 0.3,
            "pixabay": 0.2,
            "local": 0.1,
        }
        score += source_weights.get(result.source, 0.0)

        # Image quality indicators (dimensions)
        if result.width > 0 and result.height > 0:
            # Prefer larger images (up to a point)
            area = result.width * result.height
            if area > 1000000:  # > 1MP
                score += 0.5
            elif area > 500000:  # > 0.5MP
                score += 0.3

        return score

    # Calculate scores and sort
    scored_results = [(result, calculate_relevance_score(result)) for result in results]
    scored_results.sort(key=lambda x: x[1], reverse=True)

    # Return sorted results
    return [result for result, _ in scored_results]
