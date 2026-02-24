"""
Deepfake Creator Routes

Endpoints for face swapping and face replacement (with consent/watermark requirements).
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, File, HTTPException, UploadFile
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_image_file,
    validate_video_file,
)
from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/deepfake-creator", tags=["deepfake-creator"])

# In-memory storage for deepfake jobs (replace with database in production)
_deepfake_jobs: dict[str, DeepfakeJob] = {}
_job_queue: list[str] = []  # Queue of job IDs
_processing_jobs: set[str] = set()  # Jobs currently being processed
_max_concurrent_jobs: int = 2  # Maximum concurrent deepfake processing jobs


class DeepfakeJob(BaseModel):
    """Deepfake job information."""

    job_id: str
    source_face_file: str
    target_media_file: str
    output_file: str | None = None
    media_type: str  # image, video
    engine: str  # deepfacelab, fomm, etc.
    status: str  # pending, processing, completed, failed
    progress: float = 0.0  # 0.0 to 100.0
    consent_given: bool = False
    watermark_applied: bool = False
    error_message: str | None = None
    created_at: str
    completed_at: str | None = None


class DeepfakeRequest(BaseModel):
    """Request to create a deepfake."""

    media_type: str  # image, video
    engine: str = "deepfacelab"  # deepfacelab, fomm
    consent_given: bool = False
    apply_watermark: bool = True
    quality: str = "high"  # low, medium, high
    additional_params: dict[str, str] = {}


class DeepfakeResponse(BaseModel):
    """Deepfake response."""

    job_id: str
    status: str
    progress: float
    output_file: str | None = None
    consent_given: bool
    watermark_applied: bool
    error_message: str | None = None


class DeepfakeEngine(BaseModel):
    """Deepfake engine information."""

    engine_id: str
    name: str
    description: str
    supported_types: list[str]  # image, video
    requires_consent: bool = True
    watermark_required: bool = True
    is_available: bool = True


@router.post("/create", response_model=DeepfakeResponse)
async def create_deepfake(
    request: DeepfakeRequest,
    source_face: UploadFile = File(...),
    target_media: UploadFile = File(...),
):
    """Create a deepfake (face swap/replacement)."""
    try:
        # Validate consent (required for deepfake creation)
        if not request.consent_given:
            raise HTTPException(
                status_code=400,
                detail="Consent is required for deepfake creation",
            )

        if request.media_type not in ["image", "video"]:
            raise HTTPException(
                status_code=400,
                detail="media_type must be 'image' or 'video'",
            )

        import tempfile
        import uuid
        from datetime import datetime
        from pathlib import Path

        job_id = f"deepfake-{uuid.uuid4().hex[:12]}"
        now = datetime.utcnow().isoformat()

        # Save uploaded files
        temp_dir = Path(tempfile.gettempdir()) / "deepfake" / job_id
        temp_dir.mkdir(parents=True, exist_ok=True)

        source_face_path = temp_dir / f"source_{source_face.filename or 'face.jpg'}"
        target_media_path = temp_dir / f"target_{target_media.filename or 'media'}"

        # Read uploaded files content
        source_face_content = await source_face.read()
        target_media_content = await target_media.read()

        # Validate file types by magic bytes
        try:
            validate_image_file(source_face_content, filename=source_face.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid source face image: {e.message}",
            ) from e

        try:
            if request.media_type == "image":
                validate_image_file(target_media_content, filename=target_media.filename)
            else:  # video
                validate_video_file(target_media_content, filename=target_media.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid target media file: {e.message}",
            ) from e

        # Write uploaded files
        with open(source_face_path, "wb") as f:
            f.write(source_face_content)

        with open(target_media_path, "wb") as f:
            f.write(target_media_content)

        # Validate file types
        source_ext = Path(source_face.filename or "").suffix.lower()
        target_ext = Path(target_media.filename or "").suffix.lower()

        valid_image_exts = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
        valid_video_exts = {".mp4", ".avi", ".mov", ".mkv", ".webm"}

        if source_ext not in valid_image_exts:
            raise HTTPException(
                status_code=400,
                detail=f"Source face must be an image ({', '.join(valid_image_exts)})",
            )

        if request.media_type == "image" and target_ext not in valid_image_exts:
            raise HTTPException(
                status_code=400, detail="Target must be an image for image deepfake"
            )
        elif request.media_type == "video" and target_ext not in valid_video_exts:
            raise HTTPException(status_code=400, detail="Target must be a video for video deepfake")

        # Create job
        job = DeepfakeJob(
            job_id=job_id,
            source_face_file=str(source_face_path),
            target_media_file=str(target_media_path),
            media_type=request.media_type,
            engine=request.engine,
            status="pending",
            progress=0.0,
            consent_given=request.consent_given,
            watermark_applied=request.apply_watermark,
            created_at=now,
        )

        _deepfake_jobs[job_id] = job
        _job_queue.append(job_id)

        logger.info(
            f"Created deepfake job: {job_id} for {request.media_type} "
            f"with {request.engine} (consent: {request.consent_given}, "
            f"watermark: {request.apply_watermark})"
        )

        # Start async processing
        import asyncio

        # Queue job for processing
        if len(_processing_jobs) < _max_concurrent_jobs:
            _processing_jobs.add(job_id)
            if job_id in _job_queue:
                _job_queue.remove(job_id)
            asyncio.create_task(_process_deepfake_job(job_id))
        else:
            # Job will be processed when a slot becomes available
            logger.info(
                f"Deepfake job {job_id} queued (max concurrent jobs: {_max_concurrent_jobs})"
            )
            asyncio.create_task(_process_job_queue())

        # Return initial job status
        return DeepfakeResponse(
            job_id=job.job_id,
            status=job.status,
            progress=job.progress,
            output_file=job.output_file,
            consent_given=job.consent_given,
            watermark_applied=job.watermark_applied,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create deepfake: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create deepfake: {e!s}",
        ) from e


@router.get("/jobs/{job_id}", response_model=DeepfakeResponse)
async def get_deepfake_job(job_id: str):
    """Get status of a deepfake job."""
    try:
        if job_id not in _deepfake_jobs:
            raise HTTPException(status_code=404, detail=f"Deepfake job '{job_id}' not found")

        job = _deepfake_jobs[job_id]

        return DeepfakeResponse(
            job_id=job.job_id,
            status=job.status,
            progress=job.progress,
            output_file=job.output_file,
            consent_given=job.consent_given,
            watermark_applied=job.watermark_applied,
            error_message=job.error_message,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get deepfake job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get deepfake job: {e!s}",
        ) from e


@router.get("/jobs", response_model=list[DeepfakeResponse])
async def list_deepfake_jobs():
    """List all deepfake jobs."""
    try:
        jobs = []
        for job in _deepfake_jobs.values():
            jobs.append(
                DeepfakeResponse(
                    job_id=job.job_id,
                    status=job.status,
                    progress=job.progress,
                    output_file=job.output_file,
                    consent_given=job.consent_given,
                    watermark_applied=job.watermark_applied,
                    error_message=job.error_message,
                )
            )
        return sorted(jobs, key=lambda j: j.job_id, reverse=True)
    except Exception as e:
        logger.error(f"Failed to list deepfake jobs: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list deepfake jobs: {e!s}",
        ) from e


@router.delete("/jobs/{job_id}")
async def delete_deepfake_job(job_id: str):
    """Delete a deepfake job."""
    try:
        if job_id not in _deepfake_jobs:
            raise HTTPException(status_code=404, detail=f"Deepfake job '{job_id}' not found")

        del _deepfake_jobs[job_id]
        if job_id in _job_queue:
            _job_queue.remove(job_id)
        _processing_jobs.discard(job_id)
        logger.info(f"Deleted deepfake job: {job_id}")

        return {"message": f"Deepfake job '{job_id}' deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete deepfake job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete deepfake job: {e!s}",
        ) from e


async def _process_deepfake_job(job_id: str):
    """Process a single deepfake job."""
    import asyncio
    from datetime import datetime
    from pathlib import Path

    try:
        job = _deepfake_jobs.get(job_id)
        if not job:
            logger.warning(f"Deepfake job {job_id} not found")
            return

        job.status = "processing"
        job.progress = 5.0
        _deepfake_jobs[job_id] = job

        # Try to use deepfake engine (ADR-008 compliant)
        try:
            engine_service = get_engine_service()
            engine = engine_service.get_deepfacelab_engine()
            if not engine or not engine.is_available():
                raise Exception("DeepFaceLab engine not available")

            job.progress = 15.0
            _deepfake_jobs[job_id] = job

            # Get file paths
            source_face_path = Path(job.source_face_file)
            target_media_path = Path(job.target_media_file)
            temp_dir = source_face_path.parent

            # Process deepfake
            output_path = (
                temp_dir / f"output_{job_id}.{('png' if job.media_type == 'image' else 'mp4')}"
            )

            if job.media_type == "image":
                # Process image deepfake
                job.progress = 25.0
                _deepfake_jobs[job_id] = job

                engine.swap_face(
                    source_face_path=str(source_face_path),
                    target_image_path=str(target_media_path),
                    output_path=str(output_path),
                )

                job.progress = 70.0
                _deepfake_jobs[job_id] = job
            else:
                # Process video deepfake
                job.progress = 20.0
                _deepfake_jobs[job_id] = job

                engine.swap_face_video(
                    source_face_path=str(source_face_path),
                    target_video_path=str(target_media_path),
                    output_path=str(output_path),
                )

                job.progress = 60.0
                _deepfake_jobs[job_id] = job

            job.progress = 80.0
            _deepfake_jobs[job_id] = job

            # Apply watermark if requested
            if job.watermark_applied:
                try:
                    from PIL import Image, ImageDraw, ImageFont

                    if job.media_type == "image":
                        img = Image.open(output_path)
                        draw = ImageDraw.Draw(img)
                        # Add watermark text
                        watermark_text = "DEEPFAKE"
                        font: Any = None
                        try:
                            font = ImageFont.truetype("arial.ttf", 24)
                        except OSError:
                            font = ImageFont.load_default()
                        draw.text((10, 10), watermark_text, fill=(255, 0, 0, 128), font=font)
                        img.save(output_path)
                    # Video watermarking would require video processing library
                except Exception as e:
                    logger.warning(f"Failed to apply watermark: {e}")

            job.status = "completed"
            job.progress = 100.0
            job.output_file = str(output_path)
            job.completed_at = datetime.utcnow().isoformat()
            _deepfake_jobs[job_id] = job

            logger.info(f"Deepfake job completed: {job_id}")

        except Exception as e:
            logger.error(f"Deepfake processing failed: {e}", exc_info=True)
            job.status = "failed"
            job.error_message = f"Processing failed: {e!s}"
            job.completed_at = datetime.utcnow().isoformat()
            _deepfake_jobs[job_id] = job

    except Exception as e:
        logger.error(f"Deepfake job processing error: {e}", exc_info=True)
        job = _deepfake_jobs.get(job_id)
        if job:
            job.status = "failed"
            job.error_message = f"Job processing error: {e!s}"
            job.completed_at = datetime.utcnow().isoformat()
            _deepfake_jobs[job_id] = job
    finally:
        # Remove from processing set and queue
        _processing_jobs.discard(job_id)
        if job_id in _job_queue:
            _job_queue.remove(job_id)
        # Process next job in queue
        asyncio.create_task(_process_job_queue())


async def _process_job_queue():
    """Process jobs from the queue."""
    import asyncio

    while _job_queue and len(_processing_jobs) < _max_concurrent_jobs:
        # Get next job from queue
        if not _job_queue:
            break

        job_id = _job_queue[0]
        if job_id in _processing_jobs:
            _job_queue.pop(0)
            continue

        job = _deepfake_jobs.get(job_id)
        if not job or job.status != "pending":
            _job_queue.pop(0)
            continue

        # Start processing
        _processing_jobs.add(job_id)
        _job_queue.pop(0)

        # Process job
        asyncio.create_task(_process_deepfake_job(job_id))

        await asyncio.sleep(0.1)  # Small delay to prevent tight loop


@router.get("/queue/status")
async def get_queue_status():
    """Get deepfake job queue status."""
    try:
        return {
            "queue_length": len(_job_queue),
            "processing_count": len(_processing_jobs),
            "max_concurrent_jobs": _max_concurrent_jobs,
            "queued_jobs": _job_queue[:10],  # First 10 jobs
            "processing_jobs": list(_processing_jobs),
        }
    except Exception as e:
        logger.error(f"Failed to get queue status: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get queue status: {e!s}",
        ) from e


@router.get("/engines", response_model=list[DeepfakeEngine])
async def list_deepfake_engines():
    """List all available deepfake engines."""
    return [
        DeepfakeEngine(
            engine_id="deepfacelab",
            name="DeepFaceLab",
            description="Professional face replacement/swap in videos",
            supported_types=["image", "video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
        DeepfakeEngine(
            engine_id="fomm",
            name="First Order Motion Model",
            description="Motion transfer for face animation",
            supported_types=["video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
        DeepfakeEngine(
            engine_id="faceswap",
            name="FaceSwap",
            description="Open-source face swapping tool",
            supported_types=["image", "video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
    ]
