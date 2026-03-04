"""
Voice Browser Routes

Endpoints for browsing and discovering voice profiles, samples, and models.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

try:
    from ..optimization import cache_response
except ImportError:
    from typing import Callable

    def cache_response(ttl: int = 300, key_func: Callable | None = None):
        def decorator(func):
            return func

        return decorator


# Profiles will be imported dynamically in _sync_catalog_from_profiles to avoid circular imports

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/voice-browser", tags=["voice-browser"])

# Voice catalog with persistence
_voice_catalog: dict[str, dict] = {}
_state_lock = asyncio.Lock()
_catalog_file_path: str | None = None


def _get_catalog_path() -> str:
    """Get path to catalog persistence file."""
    global _catalog_file_path
    if _catalog_file_path is None:
        cache_dir = os.getenv(
            "VOICESTUDIO_CACHE_DIR", os.path.join(os.path.expanduser("~"), ".voicestudio", "cache")
        )
        os.makedirs(cache_dir, exist_ok=True)
        _catalog_file_path = os.path.join(cache_dir, "voice_catalog.json")
    return _catalog_file_path


def _load_catalog():
    """Load voice catalog from disk."""
    global _voice_catalog
    catalog_path = _get_catalog_path()
    if os.path.exists(catalog_path):
        try:
            with open(catalog_path, encoding="utf-8") as f:
                _voice_catalog = json.load(f)
            logger.info(f"Loaded {len(_voice_catalog)} voices from catalog")
        except Exception as e:
            logger.warning(f"Failed to load voice catalog: {e}")
            _voice_catalog = {}


def _save_catalog():
    """Save voice catalog to disk."""
    catalog_path = _get_catalog_path()
    try:
        with open(catalog_path, "w", encoding="utf-8") as f:
            json.dump(_voice_catalog, f, indent=2, ensure_ascii=False)
    except Exception as e:
        logger.warning(f"Failed to save voice catalog: {e}")


def _sync_catalog_from_profiles():
    """Sync catalog with profiles from profiles route."""
    global _voice_catalog
    updated = False

    # Import profiles from profile service (no route-to-route import)
    try:
        from backend.services.profile_search_service import (
            get_profile_timestamps_store,
            get_profiles_proxy,
        )

        _profile_timestamps = get_profile_timestamps_store()
        profile_storage = get_profiles_proxy()
        for profile_id, profile in profile_storage.items():
            # Handle both Pydantic model and dict
            if hasattr(profile, "name"):
                # Pydantic model
                name = profile.name
                description = getattr(profile, "description", None)
                language = getattr(profile, "language", "en")
                quality_score = getattr(profile, "quality_score", 0.0)
                tags = getattr(profile, "tags", [])
                reference_audio_url = getattr(profile, "reference_audio_url", None)
            else:
                # Dict
                name = profile.get("name", "")
                description = profile.get("description")
                language = profile.get("language", "en")
                quality_score = profile.get("quality_score", 0.0)
                tags = profile.get("tags", [])
                reference_audio_url = profile.get("reference_audio_url")

            # Extract preview audio ID from reference_audio_url if available
            preview_audio_id = None
            if reference_audio_url:
                # Extract audio_id from URL like /api/profiles/{id}/reference or /api/audio/{audio_id}
                if "/audio/" in reference_audio_url:
                    preview_audio_id = reference_audio_url.split("/audio/")[-1].split("/")[0]
                elif "/profiles/" in reference_audio_url and "/reference" in reference_audio_url:
                    # Could extract from profile reference audio
                    pass

            # Get creation time
            created_timestamp = _profile_timestamps.get(profile_id, 0.0)
            from datetime import datetime

            created_str = (
                datetime.fromtimestamp(created_timestamp).isoformat()
                if created_timestamp > 0
                else datetime.utcnow().isoformat()
            )

            # Convert profile to catalog entry
            catalog_entry = {
                "id": profile_id,
                "name": name,
                "description": description,
                "language": language,
                "gender": None,  # Not in profile model
                "age_range": None,  # Not in profile model
                "quality_score": quality_score,
                "sample_count": 1 if reference_audio_url else 0,  # Count reference audio as sample
                "tags": tags if isinstance(tags, list) else [],
                "preview_audio_id": preview_audio_id,
                "created": created_str,
            }

            # Update catalog if changed
            if profile_id not in _voice_catalog or _voice_catalog[profile_id] != catalog_entry:
                _voice_catalog[profile_id] = catalog_entry
                updated = True

        # Remove profiles that no longer exist
        profile_ids = set(profile_storage.keys())
        catalog_ids = set(_voice_catalog.keys())
        for removed_id in catalog_ids - profile_ids:
            del _voice_catalog[removed_id]
            updated = True

        if updated:
            _save_catalog()
            logger.info(f"Synced voice catalog: {len(_voice_catalog)} voices")
    except Exception as e:
        logger.warning(f"Failed to sync catalog from profiles: {e}")


# Initialize catalog on module load
_load_catalog()
_sync_catalog_from_profiles()


class VoiceProfileSummary(BaseModel):
    """Summary of a voice profile for browsing."""

    id: str
    name: str
    description: str | None = None
    language: str
    gender: str | None = None
    age_range: str | None = None
    quality_score: float
    sample_count: int
    tags: list[str] = []
    preview_audio_id: str | None = None
    created: str  # ISO datetime string


class VoiceSearchRequest(BaseModel):
    """Request for voice search."""

    query: str | None = None
    language: str | None = None
    gender: str | None = None
    min_quality_score: float | None = None
    tags: list[str] = []
    limit: int = 50
    offset: int = 0


class VoiceSearchResponse(BaseModel):
    """Response from voice search."""

    voices: list[VoiceProfileSummary]
    total: int
    limit: int
    offset: int


@router.get("/voices", response_model=VoiceSearchResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (voice searches change moderately)
async def search_voices(
    query: str | None = None,
    language: str | None = None,
    gender: str | None = None,
    min_quality_score: float | None = None,
    tags: str | None = None,  # Comma-separated
    limit: int = 50,
    offset: int = 0,
):
    """Search and browse voice profiles."""
    # Sync catalog before search to ensure it's up to date
    _sync_catalog_from_profiles()

    voices = list(_voice_catalog.values())

    # Apply filters
    if query:
        query_lower = query.lower()
        voices = [
            v
            for v in voices
            if query_lower in v.get("name", "").lower()
            or query_lower in v.get("description", "").lower()
        ]

    if language:
        voices = [v for v in voices if v.get("language") == language]

    if gender:
        voices = [v for v in voices if v.get("gender") == gender]

    if min_quality_score is not None:
        voices = [v for v in voices if v.get("quality_score", 0.0) >= min_quality_score]

    if tags:
        tag_list = [t.strip() for t in tags.split(",")]
        voices = [v for v in voices if any(tag in v.get("tags", []) for tag in tag_list)]

    # Sort by quality score (descending)
    voices.sort(key=lambda v: v.get("quality_score", 0.0), reverse=True)

    # Paginate
    total = len(voices)
    voices = voices[offset : offset + limit]

    return VoiceSearchResponse(
        voices=[
            VoiceProfileSummary(
                id=str(v.get("id", "")),
                name=str(v.get("name", "")),
                description=v.get("description"),
                language=str(v.get("language", "")),
                gender=v.get("gender"),
                age_range=v.get("age_range"),
                quality_score=v.get("quality_score", 0.0),
                sample_count=v.get("sample_count", 0),
                tags=v.get("tags", []),
                preview_audio_id=v.get("preview_audio_id"),
                created=str(v.get("created", "")),
            )
            for v in voices
        ],
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get("/voices/{voice_id}", response_model=VoiceProfileSummary)
@cache_response(ttl=300)  # Cache for 5 minutes (individual voices change less frequently)
async def get_voice_summary(voice_id: str):
    """Get detailed summary of a voice profile."""
    # Sync catalog before lookup
    _sync_catalog_from_profiles()

    if voice_id not in _voice_catalog:
        raise HTTPException(status_code=404, detail="Voice not found")

    v = _voice_catalog[voice_id]
    return VoiceProfileSummary(
        id=str(v.get("id", "")),
        name=str(v.get("name", "")),
        description=v.get("description"),
        language=str(v.get("language", "")),
        gender=v.get("gender"),
        age_range=v.get("age_range"),
        quality_score=v.get("quality_score", 0.0),
        sample_count=v.get("sample_count", 0),
        tags=v.get("tags", []),
        preview_audio_id=v.get("preview_audio_id"),
        created=str(v.get("created", "")),
    )


@router.get("/languages")
@cache_response(ttl=600)  # Cache for 10 minutes (languages are relatively static)
async def get_available_languages():
    """Get list of available languages in voice catalog."""
    _sync_catalog_from_profiles()

    languages = set()
    for v in _voice_catalog.values():
        lang = v.get("language")
        if lang:
            languages.add(lang)

    return {"languages": sorted(languages)}


@router.get("/tags")
async def get_available_tags():
    """Get list of available tags in voice catalog."""
    _sync_catalog_from_profiles()

    tags = set()
    for v in _voice_catalog.values():
        for tag in v.get("tags", []):
            tags.add(tag)

    return {"tags": sorted(tags)}


@router.post("/refresh")
async def refresh_catalog():
    """Manually refresh the voice catalog from profiles."""
    _sync_catalog_from_profiles()
    return {"message": "Catalog refreshed", "voice_count": len(_voice_catalog)}
