"""
Multi-Voice Generator Routes

Endpoints for generating multiple voice synthesis jobs simultaneously.
Essential for batch processing and A/B testing.
"""

from __future__ import annotations

import asyncio
import csv
import io
import logging
import uuid
from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from backend.api.dependencies import require_synthesis_clearance

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/voice/multi", tags=["multi-voice-generator"])

# In-memory storage for multi-voice jobs (replace with database in production)
_multi_voice_jobs: dict[str, MultiVoiceJob] = {}


class VoiceGenerationItem(BaseModel):
    """Single voice generation item in the queue."""

    item_id: str
    profile_id: str
    text: str
    engine: str = "xtts"
    quality_mode: str = "standard"
    language: str = "en"
    emotion: str | None = None
    status: str = "pending"  # pending, processing, completed, failed
    progress: float = 0.0
    audio_id: str | None = None
    audio_url: str | None = None
    quality_score: float | None = None
    quality_metrics: dict | None = None
    error_message: str | None = None


class MultiVoiceJob(BaseModel):
    """Multi-voice generation job."""

    job_id: str
    name: str
    items: list[VoiceGenerationItem]
    status: str = "pending"  # pending, processing, completed, failed
    progress: float = 0.0
    created_at: str
    updated_at: str
    completed_count: int = 0
    failed_count: int = 0


class MultiVoiceGenerateRequest(BaseModel):
    """Request to start multi-voice generation."""

    name: str
    items: list[dict]  # List of voice generation items


class MultiVoiceGenerateResponse(BaseModel):
    """Multi-voice generation response."""

    job_id: str
    name: str
    total_items: int
    status: str


class MultiVoiceJobStatusResponse(BaseModel):
    """Multi-voice job status response."""

    job_id: str
    name: str
    status: str
    progress: float
    total_items: int
    completed_count: int
    failed_count: int
    items: list[dict]


class MultiVoiceResultsResponse(BaseModel):
    """Multi-voice results response."""

    job_id: str
    items: list[dict]  # Completed items with results


class MultiVoiceCompareRequest(BaseModel):
    """Request to compare voice generation results."""

    audio_ids: list[str]
    comparison_type: str = "quality"  # quality, similarity, naturalness


class MultiVoiceCompareResponse(BaseModel):
    """Voice comparison response."""

    comparisons: list[dict]
    best_audio_id: str | None = None
    best_score: float | None = None


@router.post("/generate", response_model=MultiVoiceGenerateResponse, status_code=201)
async def generate_multi_voice(
    request: MultiVoiceGenerateRequest,
    _policy: None = Depends(require_synthesis_clearance),
):
    """Start multi-voice generation job."""
    try:
        if len(request.items) > 20:
            raise HTTPException(
                status_code=400,
                detail="Maximum 20 voice items allowed per job",
            )

        job_id = f"multi-voice-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        # Create voice generation items
        items = []
        for i, item_data in enumerate(request.items):
            item_id = f"{job_id}-item-{i}"
            item = VoiceGenerationItem(
                item_id=item_id,
                profile_id=item_data.get("profile_id", ""),
                text=item_data.get("text", ""),
                engine=item_data.get("engine", "xtts"),
                quality_mode=item_data.get("quality_mode", "standard"),
                language=item_data.get("language", "en"),
                emotion=item_data.get("emotion"),
                status="pending",
            )
            items.append(item)

        job = MultiVoiceJob(
            job_id=job_id,
            name=request.name,
            items=items,
            status="pending",
            progress=0.0,
            created_at=now,
            updated_at=now,
        )

        _multi_voice_jobs[job_id] = job

        # Start processing in background
        import asyncio

        asyncio.create_task(process_multi_voice_job(job_id))

        logger.info(f"Started multi-voice generation: {job_id} - {len(items)} items")

        return MultiVoiceGenerateResponse(
            job_id=job_id,
            name=job.name,
            total_items=len(items),
            status=job.status,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to start multi-voice generation: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to start multi-voice generation: {e!s}",
        ) from e


async def process_multi_voice_job(job_id: str):
    """Process a multi-voice generation job."""
    if job_id not in _multi_voice_jobs:
        return

    job = _multi_voice_jobs[job_id]
    job.status = "processing"
    job.updated_at = datetime.utcnow().isoformat()

    total_items = len(job.items)
    completed = 0

    # In a real implementation, this would:
    # 1. Process each item sequentially or in parallel
    # 2. Call the voice synthesis endpoint for each item
    # 3. Update progress and status
    # 4. Store results

    # Simulate processing
    for item in job.items:
        item.status = "processing"
        job.updated_at = datetime.utcnow().isoformat()
        _multi_voice_jobs[job_id] = job

        # Simulate processing time
        import asyncio

        await asyncio.sleep(0.1)

        # Simulate completion
        item.status = "completed"
        item.progress = 1.0
        item.audio_id = f"audio-{uuid.uuid4().hex[:8]}"
        item.audio_url = f"/api/voice/audio/{item.audio_id}"
        item.quality_score = 0.85
        item.quality_metrics = {
            "mos_score": 4.2,
            "similarity": 0.87,
            "naturalness": 0.82,
        }

        completed += 1
        job.completed_count = completed
        job.progress = completed / total_items
        job.updated_at = datetime.utcnow().isoformat()
        _multi_voice_jobs[job_id] = job

    job.status = "completed"
    job.progress = 1.0
    job.updated_at = datetime.utcnow().isoformat()
    _multi_voice_jobs[job_id] = job

    logger.info(f"Completed multi-voice generation: {job_id}")


@router.get("/{job_id}/status", response_model=MultiVoiceJobStatusResponse)
async def get_multi_voice_status(job_id: str):
    """Get multi-voice job status."""
    try:
        if job_id not in _multi_voice_jobs:
            raise HTTPException(status_code=404, detail=f"Multi-voice job '{job_id}' not found")

        job = _multi_voice_jobs[job_id]

        # Convert items to dicts
        items_dict = []
        for item in job.items:
            items_dict.append(
                {
                    "item_id": item.item_id,
                    "profile_id": item.profile_id,
                    "text": item.text,
                    "engine": item.engine,
                    "quality_mode": item.quality_mode,
                    "language": item.language,
                    "emotion": item.emotion,
                    "status": item.status,
                    "progress": item.progress,
                    "audio_id": item.audio_id,
                    "audio_url": item.audio_url,
                    "quality_score": item.quality_score,
                    "quality_metrics": item.quality_metrics,
                    "error_message": item.error_message,
                }
            )

        return MultiVoiceJobStatusResponse(
            job_id=job.job_id,
            name=job.name,
            status=job.status,
            progress=job.progress,
            total_items=len(job.items),
            completed_count=job.completed_count,
            failed_count=job.failed_count,
            items=items_dict,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get multi-voice status: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get multi-voice status: {e!s}",
        ) from e


@router.get("/{job_id}/results", response_model=MultiVoiceResultsResponse)
async def get_multi_voice_results(job_id: str):
    """Get multi-voice generation results."""
    try:
        if job_id not in _multi_voice_jobs:
            raise HTTPException(status_code=404, detail=f"Multi-voice job '{job_id}' not found")

        job = _multi_voice_jobs[job_id]

        # Return only completed items
        completed_items = []
        for item in job.items:
            if item.status == "completed":
                completed_items.append(
                    {
                        "item_id": item.item_id,
                        "profile_id": item.profile_id,
                        "text": item.text,
                        "engine": item.engine,
                        "quality_mode": item.quality_mode,
                        "language": item.language,
                        "emotion": item.emotion,
                        "audio_id": item.audio_id,
                        "audio_url": item.audio_url,
                        "quality_score": item.quality_score,
                        "quality_metrics": item.quality_metrics,
                    }
                )

        return MultiVoiceResultsResponse(
            job_id=job_id,
            items=completed_items,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get multi-voice results: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get multi-voice results: {e!s}",
        ) from e


@router.post("/export")
async def export_multi_voice(job_id: str = ""):
    """Export multi-voice results as CSV."""
    try:
        if job_id not in _multi_voice_jobs:
            raise HTTPException(status_code=404, detail=f"Multi-voice job '{job_id}' not found")

        job = _multi_voice_jobs[job_id]

        # Create CSV
        output = io.StringIO()
        writer = csv.writer(output)

        # Header
        writer.writerow(
            [
                "Item ID",
                "Profile ID",
                "Text",
                "Engine",
                "Quality Mode",
                "Language",
                "Emotion",
                "Status",
                "Audio ID",
                "Audio URL",
                "Quality Score",
            ]
        )

        # Rows
        for item in job.items:
            writer.writerow(
                [
                    item.item_id,
                    item.profile_id,
                    item.text,
                    item.engine,
                    item.quality_mode,
                    item.language,
                    item.emotion or "",
                    item.status,
                    item.audio_id or "",
                    item.audio_url or "",
                    item.quality_score or "",
                ]
            )

        csv_content = output.getvalue()
        output.close()

        return {
            "job_id": job_id,
            "csv_content": csv_content,
            "filename": f"{job.name}_{job_id}.csv",
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to export multi-voice results: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to export multi-voice results: {e!s}",
        ) from e


@router.post("/compare", response_model=MultiVoiceCompareResponse)
async def compare_voices(request: MultiVoiceCompareRequest):
    """Compare multiple voice generation results."""
    try:
        if len(request.audio_ids) < 2:
            raise HTTPException(
                status_code=400,
                detail="At least 2 audio IDs required for comparison",
            )

        # In a real implementation, this would:
        # 1. Load audio files
        # 2. Calculate quality metrics for each
        # 3. Compare metrics based on comparison_type
        # 4. Return comparison results

        # Simulate comparison
        comparisons = []
        best_audio_id = None
        best_score = 0.0

        for audio_id in request.audio_ids:
            # Simulate quality score
            quality_score = 0.75 + (hash(audio_id) % 25) / 100.0

            comparisons.append(
                {
                    "audio_id": audio_id,
                    "quality_score": quality_score,
                    "mos_score": 3.5 + quality_score * 1.5,
                    "similarity": 0.7 + quality_score * 0.3,
                    "naturalness": 0.7 + quality_score * 0.3,
                }
            )

            if quality_score > best_score:
                best_score = quality_score
                best_audio_id = audio_id

        return MultiVoiceCompareResponse(
            comparisons=comparisons,
            best_audio_id=best_audio_id,
            best_score=best_score,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to compare voices: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to compare voices: {e!s}",
        ) from e


class CSVImportRequest(BaseModel):
    """Request to import CSV."""

    csv_content: str


@router.post("/import")
async def import_multi_voice(request: CSVImportRequest):
    """Import voice generation items from CSV."""
    try:
        if not request.csv_content:
            raise HTTPException(status_code=400, detail="CSV content is required")

        # Parse CSV
        reader = csv.DictReader(io.StringIO(request.csv_content))

        items = []
        for row in reader:
            items.append(
                {
                    "profile_id": row.get("Profile ID", ""),
                    "text": row.get("Text", ""),
                    "engine": row.get("Engine", "xtts"),
                    "quality_mode": row.get("Quality Mode", "standard"),
                    "language": row.get("Language", "en"),
                    "emotion": row.get("Emotion") or None,
                }
            )

        if len(items) > 20:
            raise HTTPException(
                status_code=400,
                detail="Maximum 20 voice items allowed per job",
            )

        return {
            "items": items,
            "count": len(items),
        }
    except Exception as e:
        logger.error(f"Failed to import multi-voice CSV: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to import CSV: {e!s}",
        ) from e
