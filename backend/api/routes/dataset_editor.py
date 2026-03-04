"""
Training Dataset Editor Routes

Advanced endpoints for detailed dataset editing including audio file management,
transcript editing, and dataset validation.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/dataset-editor", tags=["dataset-editor"])

# In-memory dataset details (replace with database in production)
_dataset_details: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class DatasetAudioFile(BaseModel):
    """An audio file in a dataset."""

    id: str
    audio_id: str
    transcript: str | None = None
    duration: float | None = None
    sample_rate: int | None = None
    order: int  # Order in dataset
    created: str  # ISO datetime string


class DatasetDetail(BaseModel):
    """Detailed dataset information."""

    id: str
    name: str
    description: str | None = None
    audio_files: list[DatasetAudioFile]
    total_duration: float
    total_files: int
    created: str
    modified: str


class DatasetAddAudioRequest(BaseModel):
    """Request to add audio to dataset."""

    audio_id: str
    transcript: str | None = None
    order: int | None = None


class DatasetUpdateAudioRequest(BaseModel):
    """Request to update audio in dataset."""

    transcript: str | None = None
    order: int | None = None


class DatasetValidateResponse(BaseModel):
    """Response from dataset validation."""

    valid: bool
    errors: list[str] = []
    warnings: list[str] = []
    total_duration: float
    total_files: int


@router.get("/{dataset_id}", response_model=DatasetDetail)
async def get_dataset_detail(dataset_id: str):
    """Get detailed dataset information."""
    if dataset_id not in _dataset_details:
        # Try to load from training datasets
        # In a real implementation, this would query the database
        raise HTTPException(status_code=404, detail="Dataset not found")

    detail = _dataset_details[dataset_id]
    return DatasetDetail(
        id=detail["id"],
        name=detail["name"],
        description=detail.get("description"),
        audio_files=[DatasetAudioFile(**af) for af in detail.get("audio_files", [])],
        total_duration=detail.get("total_duration", 0.0),
        total_files=detail.get("total_files", 0),
        created=detail["created"],
        modified=detail["modified"],
    )


@router.post("/{dataset_id}/audio", response_model=DatasetDetail)
async def add_audio_to_dataset(dataset_id: str, request: DatasetAddAudioRequest):
    """Add an audio file to a dataset."""
    import uuid

    if dataset_id not in _dataset_details:
        raise HTTPException(status_code=404, detail="Dataset not found")

    try:
        detail = _dataset_details[dataset_id].copy()
        audio_files = detail.get("audio_files", [])

        # Determine order
        order = request.order
        if order is None:
            order = len(audio_files)

        audio_file = {
            "id": f"audio-{uuid.uuid4().hex[:8]}",
            "audio_id": request.audio_id,
            "transcript": request.transcript,
            "duration": None,  # Would be fetched from audio metadata
            "sample_rate": None,
            "order": order,
            "created": datetime.utcnow().isoformat(),
        }

        audio_files.append(audio_file)
        audio_files.sort(key=lambda x: x.get("order", 0))

        detail["audio_files"] = audio_files
        detail["total_files"] = len(audio_files)
        detail["modified"] = datetime.utcnow().isoformat()

        _dataset_details[dataset_id] = detail

        logger.info(f"Added audio {request.audio_id} to dataset {dataset_id}")

        return DatasetDetail(
            id=detail["id"],
            name=detail["name"],
            description=detail.get("description"),
            audio_files=[DatasetAudioFile(**af) for af in audio_files],
            total_duration=detail.get("total_duration", 0.0),
            total_files=len(audio_files),
            created=detail["created"],
            modified=detail["modified"],
        )
    except Exception as e:
        logger.error(f"Failed to add audio to dataset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to add audio: {e!s}",
        ) from e


@router.put(
    "/{dataset_id}/audio/{audio_file_id}",
    response_model=DatasetDetail,
)
async def update_audio_in_dataset(
    dataset_id: str,
    audio_file_id: str,
    request: DatasetUpdateAudioRequest,
):
    """Update an audio file in a dataset."""
    if dataset_id not in _dataset_details:
        raise HTTPException(status_code=404, detail="Dataset not found")

    try:
        detail = _dataset_details[dataset_id].copy()
        audio_files = detail.get("audio_files", [])

        audio_file = next(
            (af for af in audio_files if af.get("id") == audio_file_id),
            None,
        )
        if not audio_file:
            raise HTTPException(status_code=404, detail="Audio file not found")

        if request.transcript is not None:
            audio_file["transcript"] = request.transcript
        if request.order is not None:
            audio_file["order"] = request.order
            audio_files.sort(key=lambda x: x.get("order", 0))

        detail["audio_files"] = audio_files
        detail["modified"] = datetime.utcnow().isoformat()

        _dataset_details[dataset_id] = detail

        logger.debug(f"Updated audio {audio_file_id} in dataset {dataset_id}")

        return DatasetDetail(
            id=detail["id"],
            name=detail["name"],
            description=detail.get("description"),
            audio_files=[DatasetAudioFile(**af) for af in audio_files],
            total_duration=detail.get("total_duration", 0.0),
            total_files=len(audio_files),
            created=detail["created"],
            modified=detail["modified"],
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update audio in dataset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update audio: {e!s}",
        ) from e


@router.delete(
    "/{dataset_id}/audio/{audio_file_id}",
    response_model=DatasetDetail,
)
async def remove_audio_from_dataset(dataset_id: str, audio_file_id: str):
    """Remove an audio file from a dataset."""
    if dataset_id not in _dataset_details:
        raise HTTPException(status_code=404, detail="Dataset not found")

    try:
        detail = _dataset_details[dataset_id].copy()
        audio_files = detail.get("audio_files", [])

        audio_files = [af for af in audio_files if af.get("id") != audio_file_id]

        detail["audio_files"] = audio_files
        detail["total_files"] = len(audio_files)
        detail["modified"] = datetime.utcnow().isoformat()

        _dataset_details[dataset_id] = detail

        logger.info(f"Removed audio {audio_file_id} from dataset {dataset_id}")

        return DatasetDetail(
            id=detail["id"],
            name=detail["name"],
            description=detail.get("description"),
            audio_files=[DatasetAudioFile(**af) for af in audio_files],
            total_duration=detail.get("total_duration", 0.0),
            total_files=len(audio_files),
            created=detail["created"],
            modified=detail["modified"],
        )
    except Exception as e:
        logger.error(f"Failed to remove audio from dataset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to remove audio: {e!s}",
        ) from e


@router.post("/{dataset_id}/validate", response_model=DatasetValidateResponse)
async def validate_dataset(dataset_id: str):
    """Validate a dataset."""
    if dataset_id not in _dataset_details:
        raise HTTPException(status_code=404, detail="Dataset not found")

    detail = _dataset_details[dataset_id]
    audio_files = detail.get("audio_files", [])

    errors = []
    warnings = []

    if len(audio_files) == 0:
        errors.append("Dataset must contain at least one audio file")

    if len(audio_files) < 10:
        warnings.append(
            "Dataset has fewer than 10 audio files. " "More files may improve training quality."
        )

    transcripts_missing = sum(1 for af in audio_files if not af.get("transcript"))
    if transcripts_missing > 0:
        warnings.append(f"{transcripts_missing} audio files are missing transcripts")

    total_duration = sum(af.get("duration", 0.0) for af in audio_files)

    return DatasetValidateResponse(
        valid=len(errors) == 0,
        errors=errors,
        warnings=warnings,
        total_duration=total_duration,
        total_files=len(audio_files),
    )
