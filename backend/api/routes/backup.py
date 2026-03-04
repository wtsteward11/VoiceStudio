"""
Backup and Restore Routes

Endpoints for backing up and restoring application data.
Supports full backups, selective backups, and restore operations.
"""

from __future__ import annotations

import asyncio
import logging
import os
import shutil
import zipfile
from datetime import datetime
from pathlib import Path

try:
    import psutil

    PSUTIL_AVAILABLE = True
except ImportError:
    PSUTIL_AVAILABLE = False
    # Import warning logged via the logger instance defined below (line 46+)

from fastapi import APIRouter, Depends, File, HTTPException, Query, UploadFile
from fastapi.responses import FileResponse
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_archive_file,
)

from ..auth import require_auth_if_enabled

try:
    from ..optimization import cache_response
except ImportError:
    from typing import Callable

    def cache_response(ttl: int = 300, key_func: Callable | None = None):
        def decorator(func):
            return func

        return decorator


logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/backup", tags=["backup"])

# Backup storage directory (outside repo to avoid Cursor brick)
from backend.config.path_config import get_path

BACKUP_DIR = get_path("backups")


class BackupInfo(BaseModel):
    """Information about a backup."""

    id: str
    name: str
    created: str  # ISO datetime string
    size_bytes: int
    includes_profiles: bool
    includes_projects: bool
    includes_settings: bool
    includes_models: bool
    description: str | None = None


class BackupCreateRequest(BaseModel):
    """Request to create a backup."""

    name: str
    includes_profiles: bool = True
    includes_projects: bool = True
    includes_settings: bool = True
    includes_models: bool = False
    description: str | None = None


class RestoreRequest(BaseModel):
    """Request to restore from backup."""

    backup_id: str
    restore_profiles: bool = True
    restore_projects: bool = True
    restore_settings: bool = True
    restore_models: bool = False


from backend.services.persistent_store import PersistentStore

_backups: PersistentStore = PersistentStore("backups")
_state_lock = asyncio.Lock()

# Backup limits for memory management
_MAX_BACKUP_SIZE_MB = 5000  # 5GB max backup size
_MAX_BACKUP_COUNT = 100  # Maximum number of backups
_MAX_UPLOAD_SIZE_MB = 5000  # 5GB max upload size


def _get_backup_path(backup_id: str) -> Path:
    """Get the file path for a backup."""
    return BACKUP_DIR / f"{backup_id}.zip"


def _check_disk_space(required_bytes: float) -> bool:
    """Check if there's enough disk space."""
    if not PSUTIL_AVAILABLE:
        return True  # Assume enough space if psutil unavailable
    try:
        disk_usage = psutil.disk_usage(BACKUP_DIR)
        free_space = disk_usage.free
        return bool(free_space > int(required_bytes * 1.1))  # 10% buffer
    except Exception as e:
        logger.warning(f"Could not check disk space: {e}")
        return True  # Assume enough space if check fails


def _cleanup_old_backups():
    """Remove oldest backups if limit is exceeded."""
    if len(_backups) <= _MAX_BACKUP_COUNT:
        return

    # Sort backups by creation date (oldest first)
    sorted_backups = sorted(
        _backups.items(),
        key=lambda x: x[1].get("created", ""),
    )

    # Remove oldest backups
    excess_count = len(_backups) - _MAX_BACKUP_COUNT
    for backup_id, _ in sorted_backups[:excess_count]:
        backup_path = _get_backup_path(backup_id)
        if backup_path.exists():
            try:
                backup_path.unlink()
                logger.info(f"Cleaned up old backup: {backup_id}")
            except Exception as e:
                logger.warning(f"Failed to delete old backup {backup_id}: {e}")
        _backups.pop(backup_id, None)


@router.get("", response_model=list[BackupInfo])
@cache_response(ttl=60)  # Cache for 60 seconds (backup list changes moderately)
async def list_backups():
    """List all available backups."""
    backups = []
    for backup_id, backup_info in _backups.items():
        backup_path = _get_backup_path(backup_id)
        if backup_path.exists():
            size = backup_path.stat().st_size
            backups.append(
                BackupInfo(
                    id=backup_id,
                    name=backup_info.get("name", ""),
                    created=backup_info.get("created", ""),
                    size_bytes=size,
                    includes_profiles=backup_info.get("includes_profiles", False),
                    includes_projects=backup_info.get("includes_projects", False),
                    includes_settings=backup_info.get("includes_settings", False),
                    includes_models=backup_info.get("includes_models", False),
                    description=backup_info.get("description"),
                )
            )

    # Sort by creation date (newest first)
    backups.sort(key=lambda b: b.created, reverse=True)
    return backups


@router.get("/{backup_id}", response_model=BackupInfo)
@cache_response(ttl=300)  # Cache for 5 minutes (backup info is static)
async def get_backup_info(backup_id: str):
    """Get information about a specific backup."""
    if backup_id not in _backups:
        raise HTTPException(status_code=404, detail="Backup not found")

    backup_info = _backups[backup_id]
    backup_path = _get_backup_path(backup_id)

    if not backup_path.exists():
        raise HTTPException(status_code=404, detail="Backup file not found")

    size = backup_path.stat().st_size

    return BackupInfo(
        id=backup_id,
        name=backup_info.get("name", ""),
        created=backup_info.get("created", ""),
        size_bytes=size,
        includes_profiles=backup_info.get("includes_profiles", False),
        includes_projects=backup_info.get("includes_projects", False),
        includes_settings=backup_info.get("includes_settings", False),
        includes_models=backup_info.get("includes_models", False),
        description=backup_info.get("description"),
    )


@router.post("", response_model=BackupInfo)
async def create_backup(
    request: BackupCreateRequest,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Create a new backup."""
    import uuid

    # Validate request
    if not request.name or not request.name.strip():
        raise HTTPException(
            status_code=400,
            detail="Backup name is required. Please provide a name for your backup.",
        )
    if len(request.name) > 200:
        raise HTTPException(
            status_code=400,
            detail="Backup name cannot exceed 200 characters. Please use a shorter name.",
        )

    # Check if at least one component is selected
    if not any(
        [
            request.includes_profiles,
            request.includes_projects,
            request.includes_settings,
            request.includes_models,
        ]
    ):
        raise HTTPException(
            status_code=400,
            detail="Please select at least one component to backup (profiles, projects, settings, or models).",
        )

    # Clean up old backups before creating new one
    _cleanup_old_backups()

    backup_id = f"backup-{uuid.uuid4().hex[:8]}"
    backup_path = _get_backup_path(backup_id)
    now = datetime.utcnow().isoformat()

    try:
        # Create temporary directory for backup contents (outside repo)
        temp_dir = get_path("temp") / f"temp_backup_{backup_id}"
        temp_dir.mkdir(parents=True, exist_ok=True)

        try:
            # Backup profiles
            if request.includes_profiles:
                profiles_dir = get_path("data") / "profiles"
                if profiles_dir.exists():
                    shutil.copytree(
                        profiles_dir,
                        temp_dir / "profiles",
                        dirs_exist_ok=True,
                    )

            # Backup projects
            if request.includes_projects:
                projects_dir = get_path("data") / "projects"
                if projects_dir.exists():
                    shutil.copytree(
                        projects_dir,
                        temp_dir / "projects",
                        dirs_exist_ok=True,
                    )

            # Backup settings
            if request.includes_settings:
                settings_file = get_path("data") / "settings.json"
                if settings_file.exists():
                    shutil.copy2(settings_file, temp_dir / "settings.json")

            # Backup models (optional, can be large)
            if request.includes_models:
                models_dir = get_path("models")
                if models_dir.exists():
                    # Check size before copying
                    total_size = sum(f.stat().st_size for f in models_dir.rglob("*") if f.is_file())
                    size_mb = total_size / (1024 * 1024)
                    if size_mb > _MAX_BACKUP_SIZE_MB:
                        raise HTTPException(
                            status_code=413,
                            detail=(
                                f"Models directory is too large ({size_mb:.1f}MB). "
                                f"Maximum backup size is {_MAX_BACKUP_SIZE_MB}MB. "
                                f"Please exclude models or reduce the models directory size."
                            ),
                        )
                    # Check disk space (1.5x for ZIP compression)
                    required_space = total_size * 1.5
                    if not _check_disk_space(required_space):
                        raise HTTPException(
                            status_code=507,
                            detail=(
                                f"Insufficient disk space to create backup. "
                                f"Required: {required_space / (1024**3):.2f}GB. "
                                f"Please free up disk space and try again."
                            ),
                        )
                    shutil.copytree(models_dir, temp_dir / "models", dirs_exist_ok=True)

            # Create metadata file
            metadata = {
                "id": backup_id,
                "name": request.name,
                "created": now,
                "includes_profiles": request.includes_profiles,
                "includes_projects": request.includes_projects,
                "includes_settings": request.includes_settings,
                "includes_models": request.includes_models,
                "description": request.description,
            }

            import json

            with open(temp_dir / "metadata.json", "w") as f:
                json.dump(metadata, f, indent=2)

            # Create ZIP archive with compression
            try:
                with zipfile.ZipFile(backup_path, "w", zipfile.ZIP_DEFLATED) as zipf:
                    for root, _dirs, files in os.walk(temp_dir):
                        for file in files:
                            file_path = Path(root) / file
                            # Validate path to prevent path traversal
                            try:
                                arcname = file_path.relative_to(temp_dir)
                                # Ensure arcname doesn't contain parent refs
                                if ".." in str(arcname):
                                    raise ValueError(f"Invalid path in backup: {arcname}")
                                zipf.write(file_path, arcname)
                            except Exception as e:
                                logger.warning(f"Skipping invalid file path: " f"{file_path}: {e}")
                                continue

                # Check final backup size
                final_size_mb = backup_path.stat().st_size / (1024 * 1024)
                if final_size_mb > _MAX_BACKUP_SIZE_MB:
                    backup_path.unlink()
                    raise HTTPException(
                        status_code=413,
                        detail=(
                            f"Backup size ({final_size_mb:.1f}MB) exceeds maximum "
                            f"allowed size ({_MAX_BACKUP_SIZE_MB}MB). "
                            f"Please exclude some components or reduce data size."
                        ),
                    )
            except zipfile.BadZipFile as e:
                logger.error(
                    f"Failed to create ZIP archive for backup {backup_id}: {e}",
                    exc_info=True,
                )
                raise HTTPException(
                    status_code=500,
                    detail=(
                        "Failed to create backup archive. "
                        "Please try again or contact support if the issue persists."
                    ),
                ) from e

            # Store metadata
            _backups[backup_id] = metadata

            size = backup_path.stat().st_size

            return BackupInfo(
                id=backup_id,
                name=request.name,
                created=now,
                size_bytes=size,
                includes_profiles=request.includes_profiles,
                includes_projects=request.includes_projects,
                includes_settings=request.includes_settings,
                includes_models=request.includes_models,
                description=request.description,
            )

        finally:
            # Clean up temporary directory
            if temp_dir.exists():
                shutil.rmtree(temp_dir)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create backup '{request.name}': {e}", exc_info=True)
        if backup_path.exists():
            try:
                backup_path.unlink()
            except Exception:
                pass  # Ignore cleanup errors
        raise HTTPException(
            status_code=500,
            detail="Failed to create backup. Please check disk space and permissions, then try again.",
        ) from e


@router.get("/{backup_id}/download")
async def download_backup(backup_id: str):
    """Download a backup file."""
    if backup_id not in _backups:
        raise HTTPException(status_code=404, detail="Backup not found")

    backup_path = _get_backup_path(backup_id)
    if not backup_path.exists():
        raise HTTPException(status_code=404, detail="Backup file not found")

    backup_info = _backups[backup_id]
    filename = f"{backup_info.get('name', backup_id)}.zip"

    return FileResponse(
        path=str(backup_path),
        filename=filename,
        media_type="application/zip",
    )


@router.post("/{backup_id}/restore")
async def restore_backup(
    backup_id: str,
    request: RestoreRequest,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Restore from a backup."""
    if backup_id not in _backups:
        raise HTTPException(status_code=404, detail="Backup not found")

    backup_path = _get_backup_path(backup_id)
    if not backup_path.exists():
        raise HTTPException(status_code=404, detail="Backup file not found")

    try:
        # Create temporary directory for extraction (outside repo)
        temp_dir = get_path("temp") / f"temp_restore_{backup_id}"
        temp_dir.mkdir(parents=True, exist_ok=True)

        try:
            # Extract ZIP archive with validation
            try:
                with zipfile.ZipFile(backup_path, "r") as zipf:
                    # Test ZIP file integrity before restore
                    bad_file = zipf.testzip()
                    if bad_file:
                        raise HTTPException(
                            status_code=400,
                            detail=f"Backup file is corrupted: {bad_file}",
                        )
                    zipf.extractall(temp_dir)
            except zipfile.BadZipFile as e:
                raise HTTPException(status_code=400, detail=f"Invalid backup file: {e}") from e

            # Verify metadata
            metadata_file = temp_dir / "metadata.json"
            if not metadata_file.exists():
                raise HTTPException(status_code=400, detail="Invalid backup: missing metadata")

            import json

            with open(metadata_file) as f:
                metadata = json.load(f)

            # Restore profiles
            if request.restore_profiles and metadata.get("includes_profiles"):
                profiles_backup = temp_dir / "profiles"
                if profiles_backup.exists():
                    profiles_dir = get_path("data") / "profiles"
                    profiles_dir.mkdir(parents=True, exist_ok=True)
                    shutil.copytree(profiles_backup, profiles_dir, dirs_exist_ok=True)

            # Restore projects
            if request.restore_projects and metadata.get("includes_projects"):
                projects_backup = temp_dir / "projects"
                if projects_backup.exists():
                    projects_dir = get_path("data") / "projects"
                    projects_dir.mkdir(parents=True, exist_ok=True)
                    shutil.copytree(projects_backup, projects_dir, dirs_exist_ok=True)

            # Restore settings
            if request.restore_settings and metadata.get("includes_settings"):
                settings_backup = temp_dir / "settings.json"
                if settings_backup.exists():
                    settings_file = get_path("data") / "settings.json"
                    settings_file.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(settings_backup, settings_file)

            # Restore models
            if request.restore_models and metadata.get("includes_models"):
                models_backup = temp_dir / "models"
                if models_backup.exists():
                    models_dir = get_path("models")
                    models_dir.mkdir(parents=True, exist_ok=True)
                    shutil.copytree(models_backup, models_dir, dirs_exist_ok=True)

            return {"success": True, "message": "Backup restored successfully"}

        finally:
            # Clean up temporary directory
            if temp_dir.exists():
                shutil.rmtree(temp_dir)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to restore backup: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to restore backup: {e!s}") from e


@router.post("/upload")
async def upload_backup(
    file: UploadFile = File(...),
    name: str | None = Query(None),
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Upload a backup file."""
    import uuid

    # Validate file size
    if hasattr(file, "size") and file.size:
        size_mb = file.size / (1024 * 1024)
        if size_mb > _MAX_UPLOAD_SIZE_MB:
            raise HTTPException(
                status_code=413,
                detail=(
                    f"Upload size ({size_mb:.1f}MB) exceeds " f"maximum ({_MAX_UPLOAD_SIZE_MB}MB)"
                ),
            )

    # Clean up old backups before uploading new one
    _cleanup_old_backups()

    backup_id = f"backup-{uuid.uuid4().hex[:8]}"
    backup_path = _get_backup_path(backup_id)
    now = datetime.utcnow().isoformat()

    try:
        # Check disk space before upload
        if hasattr(file, "size") and file.size and not _check_disk_space(float(file.size)):
            raise HTTPException(
                status_code=507,
                detail="Insufficient disk space",
            )

        # Read and validate archive file
        content = await file.read()
        try:
            validate_archive_file(content, filename=file.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid archive file: {e.message}",
            ) from e

        # Check size
        max_size_bytes = _MAX_UPLOAD_SIZE_MB * 1024 * 1024
        if len(content) > max_size_bytes:
            raise HTTPException(
                status_code=413,
                detail=f"Upload size exceeds maximum ({_MAX_UPLOAD_SIZE_MB}MB)",
            )

        # Save validated file
        backup_path.write_bytes(content)

        # Extract and read metadata
        temp_dir = get_path("temp") / f"temp_upload_{backup_id}"
        temp_dir.mkdir(parents=True, exist_ok=True)

        try:
            # Validate ZIP file before extraction
            try:
                with zipfile.ZipFile(backup_path, "r") as zipf:
                    # Test ZIP file integrity
                    zipf.testzip()
                    zipf.extractall(temp_dir)
            except zipfile.BadZipFile as e:
                backup_path.unlink()
                raise HTTPException(status_code=400, detail=f"Invalid ZIP file: {e}") from e

            metadata_file = temp_dir / "metadata.json"
            if not metadata_file.exists():
                raise HTTPException(status_code=400, detail="Invalid backup: missing metadata")

            import json

            with open(metadata_file) as f:
                metadata = json.load(f)

            # Update metadata with upload info
            metadata["id"] = backup_id
            metadata["created"] = now
            if name:
                metadata["name"] = name

            _backups[backup_id] = metadata

            size = backup_path.stat().st_size

            return BackupInfo(
                id=backup_id,
                name=metadata.get("name", name or "Uploaded Backup"),
                created=now,
                size_bytes=size,
                includes_profiles=metadata.get("includes_profiles", False),
                includes_projects=metadata.get("includes_projects", False),
                includes_settings=metadata.get("includes_settings", False),
                includes_models=metadata.get("includes_models", False),
                description=metadata.get("description"),
            )

        finally:
            if temp_dir.exists():
                shutil.rmtree(temp_dir)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to upload backup: {e}", exc_info=True)
        if backup_path.exists():
            try:
                backup_path.unlink()
            except Exception:
                pass  # Ignore cleanup errors
        raise HTTPException(status_code=500, detail=f"Failed to upload backup: {e!s}") from e


@router.delete("/{backup_id}")
async def delete_backup(
    backup_id: str,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Delete a backup."""
    if backup_id not in _backups:
        raise HTTPException(status_code=404, detail="Backup not found")

    backup_path = _get_backup_path(backup_id)
    if backup_path.exists():
        backup_path.unlink()

    del _backups[backup_id]
    return {"success": True}
