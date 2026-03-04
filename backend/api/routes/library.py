"""
Asset Library Routes

Endpoints for managing and browsing the asset library.
Supports audio files, voice profiles, presets, templates, and other assets.

Panel Workflow Integration - Library Persistence.
Now uses database-backed repositories instead of in-memory storage.
"""

from __future__ import annotations

import contextlib
import logging
import os
import uuid
from datetime import datetime
from pathlib import Path
from typing import Callable

from fastapi import APIRouter, File, HTTPException, Query, UploadFile
from pydantic import BaseModel

from backend.config.path_config import get_path
from backend.data.repositories.library_repository import (
    LibraryAssetEntity,
    LibraryFolderEntity,
    get_library_asset_repository,
    get_library_folder_repository,
)

try:
    from ..optimization import cache_response
except ImportError:

    def cache_response(ttl: int = 300, key_func: Callable | None = None):
        def decorator(func: Callable) -> Callable:
            return func

        return decorator


logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/library", tags=["library"])


class AssetType:
    """Asset type constants."""

    AUDIO = "audio"
    VOICE_PROFILE = "voice_profile"
    PRESET = "preset"
    TEMPLATE = "template"
    EFFECT = "effect"
    MACRO = "macro"
    IMAGE = "image"
    VIDEO = "video"
    OTHER = "other"


class LibraryAsset(BaseModel):
    """An asset in the library."""

    id: str
    name: str
    type: str  # AssetType
    path: str
    folder_id: str | None = None
    tags: list[str] = []
    metadata: dict = {}
    created: datetime
    modified: datetime
    size: int = 0
    duration: float | None = None  # For audio/video
    thumbnail_url: str | None = None


class LibraryFolder(BaseModel):
    """A folder in the library."""

    id: str
    name: str
    parent_id: str | None = None
    path: str
    created: datetime
    modified: datetime
    asset_count: int = 0


class AssetSearchRequest(BaseModel):
    """Request to search assets."""

    query: str | None = None
    asset_type: str | None = None
    tags: list[str] | None = None
    folder_id: str | None = None
    limit: int = 100
    offset: int = 0


class AssetSearchResponse(BaseModel):
    """Response from asset search."""

    assets: list[LibraryAsset]
    total: int
    limit: int
    offset: int


def _entity_to_asset(entity: LibraryAssetEntity) -> LibraryAsset:
    """Convert LibraryAssetEntity to LibraryAsset response model."""
    import json

    tags: list[str] = []
    try:
        tags = json.loads(entity.tags) if entity.tags else []
    except (json.JSONDecodeError, TypeError) as e:
        logger.debug(f"Failed to parse tags JSON for entity {entity.id}: {e}")
        tags = []

    metadata: dict[str, object] = {}
    try:
        metadata = json.loads(entity.metadata) if entity.metadata else {}
    except (json.JSONDecodeError, TypeError) as e:
        # GAP-PY-001: Best effort - corrupted metadata JSON
        logger.debug(f"Failed to parse metadata JSON for entity {entity.id}: {e}")
        metadata = {}

    return LibraryAsset(
        id=entity.id,
        name=entity.name,
        type=entity.type,
        path=entity.path,
        folder_id=entity.folder_id,
        tags=tags,
        metadata=metadata,
        created=entity.created_at if isinstance(entity.created_at, datetime) else datetime.now(),
        modified=entity.modified_at if isinstance(entity.modified_at, datetime) else datetime.now(),
        size=entity.size or 0,
        duration=entity.duration,
        thumbnail_url=entity.thumbnail_url,
    )


def _entity_to_folder(entity: LibraryFolderEntity, asset_count: int = 0) -> LibraryFolder:
    """Convert LibraryFolderEntity to LibraryFolder response model."""
    return LibraryFolder(
        id=entity.id,
        name=entity.name,
        parent_id=entity.parent_id,
        path=entity.path,
        created=entity.created_at if isinstance(entity.created_at, datetime) else datetime.now(),
        modified=entity.modified_at if isinstance(entity.modified_at, datetime) else datetime.now(),
        asset_count=asset_count,
    )


@router.get("/folders", response_model=list[LibraryFolder])
@cache_response(ttl=60)  # Cache for 60 seconds (folders change moderately)
async def get_folders(parent_id: str | None = Query(None, description="Parent folder ID")):
    """Get all folders, optionally filtered by parent."""
    folder_repo = get_library_folder_repository()
    asset_repo = get_library_asset_repository()

    if parent_id is None:
        folder_entities = await folder_repo.get_root_folders()
    else:
        folder_entities = await folder_repo.get_children(parent_id)

    # Get asset counts for each folder
    folders = []
    for entity in folder_entities:
        count = await asset_repo.count({"folder_id": entity.id})
        folders.append(_entity_to_folder(entity, asset_count=count))

    return sorted(folders, key=lambda x: x.name)


@router.post("/folders", response_model=LibraryFolder)
async def create_folder(name: str, parent_id: str | None = None, path: str | None = None):
    """Create a new folder."""
    folder_repo = get_library_folder_repository()

    folder_id = str(uuid.uuid4())

    if not path:
        library_base = get_path("data") / "library"
        if parent_id:
            parent = await folder_repo.get_by_id(parent_id)
            if parent:
                path = str(Path(parent.path) / name)
            else:
                path = str(library_base / name)
        else:
            path = str(library_base / name)

    # Ensure directory exists
    os.makedirs(path, exist_ok=True)

    now = datetime.now()
    folder_entity = LibraryFolderEntity(
        id=folder_id,
        name=name,
        parent_id=parent_id,
        path=path,
        created_at=now,
        updated_at=now,
        modified_at=now,
    )

    await folder_repo.create(folder_entity)
    logger.info(f"Created folder {folder_id}: {name}")

    return _entity_to_folder(folder_entity, asset_count=0)


@router.get("/assets", response_model=AssetSearchResponse)
@cache_response(ttl=30)  # Cache for 30 seconds (asset searches change moderately)
async def search_assets(
    query: str | None = Query(None),
    asset_type: str | None = Query(None),
    tags: str | None = Query(None),  # Comma-separated
    folder_id: str | None = Query(None),
    limit: int = Query(100, ge=1, le=1000),
    offset: int = Query(0, ge=0),
):
    """Search and filter assets."""
    asset_repo = get_library_asset_repository()

    # Build filters
    filters = {}
    if folder_id:
        filters["folder_id"] = folder_id
    if asset_type:
        filters["type"] = asset_type

    # Get entities based on filters
    if query:
        # Use search by name
        entities = await asset_repo.search_by_name(query, limit=1000)
        # Apply additional filters
        if folder_id:
            entities = [e for e in entities if e.folder_id == folder_id]
        if asset_type:
            entities = [e for e in entities if e.type == asset_type]
    elif tags:
        # Use search by tags
        tag_list = [t.strip() for t in tags.split(",")]
        entities = await asset_repo.search_by_tags(tag_list, limit=1000)
        # Apply additional filters
        if folder_id:
            entities = [e for e in entities if e.folder_id == folder_id]
        if asset_type:
            entities = [e for e in entities if e.type == asset_type]
    elif filters:
        entities = await asset_repo.find(filters)
    else:
        entities = await asset_repo.get_all()

    # Apply tag filter if not already done
    if tags and not query:
        import json

        tag_list = [t.strip() for t in tags.split(",")]
        filtered = []
        for e in entities:
            try:
                entity_tags = json.loads(e.tags) if e.tags else []
                if any(tag in entity_tags for tag in tag_list):
                    filtered.append(e)
            except (json.JSONDecodeError, TypeError) as ex:
                # GAP-PY-001: Invalid tags JSON, skip entity in tag filter
                logger.debug(f"Failed to parse tags for entity {getattr(e, 'id', 'unknown')}: {ex}")
        entities = filtered

    # Sort by modified date (newest first) - already done by repository
    total = len(entities)
    paginated = entities[offset : offset + limit]

    return AssetSearchResponse(
        assets=[_entity_to_asset(entity) for entity in paginated],
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get("/assets/{asset_id}", response_model=LibraryAsset)
@cache_response(ttl=300)  # Cache for 5 minutes (individual assets change less frequently)
async def get_asset(asset_id: str):
    """Get a specific asset."""
    asset_repo = get_library_asset_repository()
    entity = await asset_repo.get_by_id(asset_id)

    if not entity:
        raise HTTPException(status_code=404, detail="Asset not found")

    return _entity_to_asset(entity)


@router.post("/assets", response_model=LibraryAsset)
async def create_asset(
    name: str,
    asset_type: str,
    path: str,
    folder_id: str | None = None,
    tags: list[str] | None = None,
    metadata: dict | None = None,
):
    """Create or register a new asset."""
    import json

    asset_repo = get_library_asset_repository()
    asset_id = str(uuid.uuid4())

    # Get file size if path exists
    size = 0
    if os.path.exists(path):
        size = os.path.getsize(path)

    now = datetime.now()
    entity = LibraryAssetEntity(
        id=asset_id,
        name=name,
        type=asset_type,
        path=path,
        folder_id=folder_id,
        tags=json.dumps(tags or []),
        metadata=json.dumps(metadata or {}),
        size=size,
        duration=None,
        thumbnail_url=None,
        created_at=now,
        updated_at=now,
        modified_at=now,
    )

    await asset_repo.create(entity)
    logger.info(f"Created asset {asset_id}: {name}")

    return _entity_to_asset(entity)


@router.post("/assets/upload", response_model=LibraryAsset, status_code=201)
async def upload_asset(
    file: UploadFile = File(...),
    folder_id: str | None = None,
    tags: str | None = None,
):
    """
    Upload an audio file directly into the library.

    Validates the file, saves it to the audio uploads directory,
    creates a library asset entry, and returns the new asset.
    Tags should be comma-separated if provided.
    """
    import json

    asset_repo = get_library_asset_repository()

    # Validate file has a name
    if not file.filename:
        raise HTTPException(status_code=400, detail="File must have a filename")

    # Read file content
    content = await file.read()
    if len(content) == 0:
        raise HTTPException(status_code=400, detail="Uploaded file is empty")

    # Validate media file (accepts audio + video for audio extraction)
    is_video_source = False
    detected_format = None
    try:
        from backend.core.security.file_validation import (
            FileCategory,
            validate_media_for_audio_extraction,
        )

        file_info = validate_media_for_audio_extraction(content, filename=file.filename)
        detected_format = file_info.extension
        is_video_source = file_info.category == FileCategory.VIDEO
        if is_video_source:
            logger.info(
                "Video file '%s' accepted for audio extraction (will convert to WAV)",
                file.filename,
            )
    except ImportError:
        logger.warning("file_validation module not available; skipping validation")
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Invalid audio file: {e!s}") from e

    # Save file to audio uploads directory (outside repo)
    upload_dir = str(get_path("audio_uploads"))
    os.makedirs(upload_dir, exist_ok=True)

    file_id = str(uuid.uuid4())
    original_ext = os.path.splitext(file.filename)[1] or ".wav"
    safe_filename = f"{file_id}{original_ext}"
    dest_path = os.path.join(upload_dir, safe_filename)

    try:
        from pathlib import Path

        Path(dest_path).write_bytes(content)
    except Exception as e:
        if os.path.exists(dest_path):
            os.remove(dest_path)
        raise HTTPException(status_code=500, detail=f"Failed to save uploaded file: {e!s}") from e

    # Convert to WAV if needed (video files or non-WAV audio)
    final_path = dest_path
    is_wav = original_ext.lower() in (".wav", ".wave")
    converted = False

    if not is_wav:
        # Convert to WAV for cloning compatibility
        wav_filename = f"{file_id}.wav"
        wav_path = os.path.join(upload_dir, wav_filename)

        try:
            from pathlib import Path

            from backend.core.audio.conversion import get_conversion_service

            conversion_service = get_conversion_service()
            # Run async conversion in sync context
            import asyncio

            loop = asyncio.get_event_loop()
            result = loop.run_until_complete(
                conversion_service.convert_to_wav(
                    input_path=Path(dest_path),
                    output_path=Path(wav_path),
                    sample_rate=44100,
                    channels=2,
                    bit_depth=16,
                )
            )

            if result.success:
                final_path = wav_path
                converted = True
                logger.info(
                    "Converted %s (%s) to WAV for library import",
                    file.filename,
                    detected_format or original_ext,
                )
            else:
                logger.warning(
                    "Audio conversion failed for %s: %s (keeping original)",
                    file.filename,
                    result.error,
                )
        except ImportError:
            logger.warning("AudioConversionService not available; keeping original format")
        except Exception as conv_error:
            logger.warning(
                "Conversion failed for %s, keeping original: %s",
                file.filename,
                conv_error,
            )

    # Parse tags
    tag_list = []
    if tags:
        tag_list = [t.strip() for t in tags.split(",") if t.strip()]

    # Determine asset name from filename (without extension)
    asset_name = os.path.splitext(file.filename)[0]

    # Create library asset entry
    asset_id = str(uuid.uuid4())
    now = datetime.now()

    metadata_dict = {
        "original_filename": file.filename,
        "original_path": dest_path if converted else None,
        "content_type": file.content_type,
        "upload_id": file_id,
        "source": "upload",
        "converted_to_wav": converted,
        "source_format": detected_format or original_ext.lstrip("."),
        "is_video_source": is_video_source,
    }

    entity = LibraryAssetEntity(
        id=asset_id,
        name=asset_name,
        type="audio",
        path=final_path,
        folder_id=folder_id,
        tags=json.dumps(tag_list),
        metadata=json.dumps(metadata_dict),
        size=len(content),
        duration=None,
        thumbnail_url=None,
        created_at=now,
        updated_at=now,
        modified_at=now,
    )

    await asset_repo.create(entity)

    logger.info(
        "Uploaded and created library asset %s: %s (%d bytes)",
        asset_id,
        asset_name,
        len(content),
    )

    return _entity_to_asset(entity)


@router.put("/assets/{asset_id}", response_model=LibraryAsset)
async def update_asset(
    asset_id: str,
    name: str | None = None,
    tags: list[str] | None = None,
    folder_id: str | None = None,
    metadata: dict | None = None,
):
    """Update an asset."""
    import json

    asset_repo = get_library_asset_repository()
    entity = await asset_repo.get_by_id(asset_id)

    if not entity:
        raise HTTPException(status_code=404, detail="Asset not found")

    # Build update data
    update_data = {}
    if name is not None:
        update_data["name"] = name
    if tags is not None:
        update_data["tags"] = json.dumps(tags)
    if folder_id is not None:
        update_data["folder_id"] = folder_id
    if metadata is not None:
        # Merge with existing metadata
        existing_metadata: dict[str, object] = {}
        with contextlib.suppress(json.JSONDecodeError, TypeError):
            existing_metadata = json.loads(entity.metadata) if entity.metadata else {}
        existing_metadata.update(metadata)
        update_data["metadata"] = json.dumps(existing_metadata)

    update_data["modified_at"] = datetime.now().isoformat()

    updated = await asset_repo.update(asset_id, update_data)

    if not updated:
        raise HTTPException(status_code=404, detail="Asset not found")

    logger.info(f"Updated asset {asset_id}")

    return _entity_to_asset(updated)


@router.delete("/assets/{asset_id}")
async def delete_asset(asset_id: str):
    """Delete an asset."""
    asset_repo = get_library_asset_repository()
    entity = await asset_repo.get_by_id(asset_id)

    if not entity:
        raise HTTPException(status_code=404, detail="Asset not found")

    # Soft delete
    await asset_repo.delete(asset_id, soft=True)

    logger.info(f"Deleted asset {asset_id}")

    return {"success": True, "message": "Asset deleted"}


@router.get("/types")
async def get_asset_types():
    """Get list of available asset types."""
    return {
        "types": [
            {"id": AssetType.AUDIO, "name": "Audio"},
            {"id": AssetType.VOICE_PROFILE, "name": "Voice Profile"},
            {"id": AssetType.PRESET, "name": "Preset"},
            {"id": AssetType.TEMPLATE, "name": "Template"},
            {"id": AssetType.EFFECT, "name": "Effect"},
            {"id": AssetType.MACRO, "name": "Macro"},
            {"id": AssetType.IMAGE, "name": "Image"},
            {"id": AssetType.VIDEO, "name": "Video"},
            {"id": AssetType.OTHER, "name": "Other"},
        ]
    }


@router.get("/summary")
async def get_library_summary():
    """Get library summary statistics."""
    asset_repo = get_library_asset_repository()
    folder_repo = get_library_folder_repository()

    asset_summary = await asset_repo.get_summary()
    folder_count = len(await folder_repo.get_all())

    return {
        **asset_summary,
        "folders": folder_count,
    }
