"""
Project Management Routes

CRUD operations for projects.
"""

from __future__ import annotations

import logging
import os
from datetime import datetime
from pathlib import Path
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field

from backend.project.management.project_store_service import (
    ProjectRecord,
    get_project_store_service,
)

from ..middleware.auth_middleware import require_auth_if_enabled
from ..models import ApiOk
from ..optimization import cache_response, get_pagination_params

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/projects",
    tags=["projects"],
    dependencies=[Depends(require_auth_if_enabled)],
)


class Project(BaseModel):
    id: str
    name: str
    description: Optional[str] = None
    created_at: str
    updated_at: str
    voice_profile_ids: List[str] = Field(default_factory=list)


class ProjectCreateRequest(BaseModel):
    name: str
    description: Optional[str] = None


class ProjectUpdateRequest(BaseModel):
    name: Optional[str] = None
    description: Optional[str] = None
    voice_profile_ids: Optional[List[str]] = None


def _store():
    return get_project_store_service()


def _to_api_project(record: ProjectRecord) -> Project:
    return Project(
        id=record.id,
        name=record.name,
        description=record.description,
        created_at=record.created_at,
        updated_at=record.updated_at,
        voice_profile_ids=record.voice_profile_ids,
    )


def _projects_root_dir() -> str:
    return str(_store().projects_dir)


def _ensure_project_dir(project_id: str) -> str:
    """Ensure project directory exists and return its path."""
    project_dir = os.path.join(_projects_root_dir(), project_id)
    os.makedirs(project_dir, exist_ok=True)
    os.makedirs(os.path.join(project_dir, "audio"), exist_ok=True)
    return project_dir


@router.get("")
@cache_response(ttl=60)  # Cache for 60 seconds
def list_projects(request: Request) -> dict:
    """
    List all projects with pagination.

    Query parameters:
    - page: Page number (default: 1)
    - page_size: Items per page (default: 50, max: 1000)
    """
    try:
        # Get pagination parameters
        pagination = get_pagination_params(request, default_page_size=50)

        # Get all projects
        all_projects = [_to_api_project(p) for p in _store().list_projects()]

        # Paginate
        result = pagination.paginate(all_projects)

        return result
    except Exception as e:
        logger.error(f"Failed to list projects: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list projects: {e!s}") from e


@router.get("/{project_id}", response_model=Project)
@cache_response(ttl=300)  # Cache for 5 minutes
def get_project(project_id: str) -> Project:
    """Get a specific project."""
    try:
        try:
            record = _store().get_project(project_id)
        except KeyError:
            raise HTTPException(status_code=404, detail="Project not found")
        return _to_api_project(record)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get project: {e!s}") from e


@router.post("", response_model=Project)
def create_project(req: ProjectCreateRequest) -> Project:
    """Create a new project."""
    try:
        # Validate input
        if not req.name or not req.name.strip():
            raise HTTPException(
                status_code=400, detail="Project name is required and cannot be empty"
            )
        name = req.name.strip()
        description = req.description.strip() if req.description else None

        record = _store().create_project(name=name, description=description)
        logger.info(f"Created project: {record.id} - {record.name}")
        return _to_api_project(record)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create project: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create project: {e!s}") from e


@router.put("/{project_id}", response_model=Project)
def update_project(project_id: str, req: ProjectUpdateRequest) -> Project:
    """Update an existing project."""
    try:
        # Validate input
        if req.name is not None and (not req.name or not req.name.strip()):
            raise HTTPException(status_code=400, detail="Project name cannot be empty")
        name = req.name.strip() if req.name is not None else None
        description_provided = req.description is not None
        description = req.description.strip() if req.description else None

        try:
            record = _store().update_project(
                project_id=project_id,
                name=name,
                description=description,
                voice_profile_ids=req.voice_profile_ids,
                description_provided=description_provided,
            )
        except KeyError:
            raise HTTPException(status_code=404, detail="Project not found")

        logger.info(f"Updated project: {project_id} - {record.name}")
        return _to_api_project(record)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update project: {e!s}") from e


@router.delete("/{project_id}", response_model=ApiOk)
def delete_project(project_id: str) -> ApiOk:
    """Delete a project."""
    try:
        if not _store().exists(project_id):
            raise HTTPException(status_code=404, detail="Project not found")

        _store().delete_project(project_id)
        logger.info(f"Deleted project: {project_id}")
        return ApiOk()
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete project: {e!s}") from e


class ProjectAudioFileResponse(BaseModel):
    filename: str
    url: str
    size: int
    modified: str


class SaveAudioRequest(BaseModel):
    audio_id: str
    filename: Optional[str] = None


@router.post("/{project_id}/audio/save", response_model=ProjectAudioFileResponse)
def save_audio_to_project(project_id: str, req: SaveAudioRequest) -> ProjectAudioFileResponse:
    """
    Save an audio file to a project directory.

    Args:
        project_id: Project ID
        req: Request body with audio_id and optional filename

    Returns:
        ProjectAudioFileResponse with file information
    """
    try:
        _store().get_project(project_id)
    except KeyError:
        raise HTTPException(
            status_code=404,
            detail=f"Project '{project_id}' not found. Please check the project ID and try again.",
        )

    audio_id = req.audio_id
    filename = req.filename

    # Import audio storage from voice routes
    from backend.services.audio_artifacts import AudioRegistry

    source_path = AudioRegistry.get_path(audio_id)
    if not source_path:
        raise HTTPException(
            status_code=404,
            detail=f"Audio file with ID '{audio_id}' not found. The audio may have expired or been removed.",
        )
    if not os.path.exists(source_path):
        raise HTTPException(
            status_code=404,
            detail=f"Audio file at path '{source_path}' no longer exists on disk. The file may have been deleted.",
        )

    try:
        try:
            dest_path = _store().save_audio_file(
                project_id,
                source_path,
                audio_id=audio_id,
                filename=filename,
            )
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))
        except PermissionError:
            raise HTTPException(
                status_code=403,
                detail=(
                    "Permission denied when saving audio to the project. "
                    "Please check directory permissions."
                ),
            )
        except OSError as e:
            if "No space left" in str(e) or "disk full" in str(e).lower():
                raise HTTPException(
                    status_code=507,
                    detail="Disk full. Please free up space and try again.",
                )
            raise HTTPException(status_code=500, detail=f"Failed to save audio file: {e!s}")

        # Get file stats
        try:
            file_stat = os.stat(dest_path)
        except OSError as e:
            logger.error(f"Failed to get file stats for {dest_path}: {e}")
            raise HTTPException(
                status_code=500,
                detail="Failed to retrieve file information after save.",
            )

        final_name = Path(dest_path).name
        logger.info(f"Saved audio {audio_id} to project {project_id}: {dest_path}")

        return ProjectAudioFileResponse(
            filename=final_name,
            url=f"/api/projects/{project_id}/audio/{final_name}",
            size=file_stat.st_size,
            modified=datetime.fromtimestamp(file_stat.st_mtime).isoformat(),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Unexpected error saving audio to project {project_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"An unexpected error occurred while saving the audio file: {e!s}",
        )


class ProjectAudioFile(BaseModel):
    filename: str
    url: str
    size: int
    modified: str


@router.get("/{project_id}/audio", response_model=List[ProjectAudioFile])
@cache_response(ttl=60)  # Cache for 60 seconds (audio file lists change moderately)
def list_project_audio(project_id: str) -> List[ProjectAudioFile]:
    """List all audio files in a project."""
    try:
        try:
            _store().get_project(project_id)
        except KeyError:
            raise HTTPException(
                status_code=404,
                detail=f"Project '{project_id}' not found. Please check the project ID and try again.",
            )

        project_dir = _ensure_project_dir(project_id)
        audio_dir = os.path.join(project_dir, "audio")

        if not os.path.exists(audio_dir):
            return []

        audio_files = []
        try:
            for filename in os.listdir(audio_dir):
                if filename.endswith((".wav", ".mp3", ".flac")):
                    file_path = os.path.join(audio_dir, filename)
                    try:
                        file_stat = os.stat(file_path)
                        audio_files.append(
                            ProjectAudioFile(
                                filename=filename,
                                url=f"/api/projects/{project_id}/audio/{filename}",
                                size=file_stat.st_size,
                                modified=datetime.fromtimestamp(file_stat.st_mtime).isoformat(),
                            )
                        )
                    except (OSError, PermissionError) as e:
                        logger.warning(f"Failed to get stats for file {file_path}: {e}")
                        # Continue with other files instead of failing completely
                        continue
        except PermissionError:
            raise HTTPException(
                status_code=403,
                detail=f"Permission denied when accessing audio directory '{audio_dir}'. Please check directory permissions.",
            )
        except OSError as e:
            logger.error(f"Failed to list audio files in {audio_dir}: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to list audio files: {e!s}")

        return audio_files
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Unexpected error listing audio files for project {project_id}: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"An unexpected error occurred while listing audio files: {e!s}",
        )


@router.get("/{project_id}/audio/{filename}")
def get_project_audio(project_id: str, filename: str):
    """Get an audio file from a project."""
    try:
        try:
            _store().get_project(project_id)
        except KeyError:
            raise HTTPException(
                status_code=404,
                detail=f"Project '{project_id}' not found. Please check the project ID and try again.",
            )

        # Validate filename to prevent directory traversal
        if ".." in filename or "/" in filename or "\\" in filename:
            raise HTTPException(
                status_code=400,
                detail="Invalid filename. Directory traversal is not allowed.",
            )
        project_dir = _ensure_project_dir(project_id)
        audio_dir = os.path.join(project_dir, "audio")
        file_path = os.path.join(audio_dir, filename)

        if not os.path.exists(file_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file '{filename}' not found in project '{project_id}'. The file may have been deleted.",
            )

        if not os.path.isfile(file_path):
            raise HTTPException(status_code=400, detail=f"Path '{filename}' is not a file.")

        try:
            return FileResponse(file_path, media_type="audio/wav")
        except Exception as e:
            logger.error(f"Failed to serve file {file_path}: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to serve audio file: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Unexpected error getting audio file {filename} from project {project_id}: {e}",
            exc_info=True,
        )
        raise HTTPException(
            status_code=500,
            detail=f"An unexpected error occurred while retrieving the audio file: {e!s}",
        )
