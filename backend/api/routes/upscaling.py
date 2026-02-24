"""
Upscaling Routes

Endpoints for image and video upscaling.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

from fastapi import APIRouter, File, HTTPException, UploadFile
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_image_file,
    validate_video_file,
)
from backend.ml.models.engine_service import get_engine_service

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/upscaling", tags=["upscaling"])

# In-memory storage for upscaling jobs (replace with database in production)
_upscaling_jobs: dict[str, UpscalingJob] = {}


class UpscalingJob(BaseModel):
    """Upscaling job information."""

    job_id: str
    input_file: str
    output_file: str | None = None
    media_type: str  # image, video
    engine: str  # realesrgan, esrgan, waifu2x, etc.
    scale_factor: float  # 2.0, 4.0, etc.
    status: str  # pending, processing, completed, failed
    progress: float = 0.0  # 0.0 to 100.0
    original_width: int | None = None
    original_height: int | None = None
    upscaled_width: int | None = None
    upscaled_height: int | None = None
    error_message: str | None = None
    created_at: str
    completed_at: str | None = None


class UpscalingRequest(BaseModel):
    """Request to upscale media."""

    media_type: str  # image, video
    engine: str = "realesrgan"  # realesrgan, esrgan, waifu2x
    scale_factor: float = 2.0  # 2.0, 4.0, etc.
    output_format: str | None = None  # png, jpg, mp4, etc.
    additional_params: dict[str, str] = {}


class UpscalingResponse(BaseModel):
    """Upscaling response."""

    job_id: str
    status: str
    progress: float
    output_file: str | None = None
    original_width: int | None = None
    original_height: int | None = None
    upscaled_width: int | None = None
    upscaled_height: int | None = None
    error_message: str | None = None


class UpscalingEngine(BaseModel):
    """Upscaling engine information."""

    engine_id: str
    name: str
    description: str
    supported_types: list[str]  # image, video
    supported_scales: list[float]  # [2.0, 4.0]
    is_available: bool = True


@router.post("/upscale", response_model=UpscalingResponse)
async def upscale_media(
    request: UpscalingRequest,
    file: UploadFile = File(...),
):
    """Upscale an image or video file."""
    try:
        # Validate file type (image or video)
        if request.media_type not in ["image", "video"]:
            raise HTTPException(
                status_code=400,
                detail="media_type must be 'image' or 'video'",
            )

        if request.scale_factor not in [2.0, 4.0, 8.0]:
            raise HTTPException(
                status_code=400,
                detail="scale_factor must be 2.0, 4.0, or 8.0",
            )

        # Validate file type by magic bytes based on media_type
        file_content = await file.read()
        await file.seek(0)  # Reset file position for later read
        try:
            if request.media_type == "image":
                validate_image_file(file_content, filename=file.filename)
            else:  # video
                validate_video_file(file_content, filename=file.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid {request.media_type} file: {e.message}",
            ) from e

        import uuid
        from datetime import datetime

        job_id = f"upscale-{uuid.uuid4().hex[:12]}"
        now = datetime.utcnow().isoformat()

        # Create job and start async processing
        job = UpscalingJob(
            job_id=job_id,
            input_file=file.filename or "unknown",
            media_type=request.media_type,
            engine=request.engine,
            scale_factor=request.scale_factor,
            status="pending",
            progress=0.0,
            created_at=now,
        )

        _upscaling_jobs[job_id] = job

        logger.info(
            f"Created upscaling job: {job_id} for {request.media_type} "
            f"with {request.engine} at {request.scale_factor}x"
        )

        # Start async processing
        import asyncio

        asyncio.create_task(_process_upscaling_job(job_id, file, request))

        return UpscalingResponse(
            job_id=job.job_id,
            status=job.status,
            progress=job.progress,
            output_file=None,
            original_width=None,
            original_height=None,
            upscaled_width=None,
            upscaled_height=None,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to upscale media: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to upscale media: {e!s}",
        ) from e


@router.get("/jobs/{job_id}", response_model=UpscalingResponse)
@cache_response(ttl=5)  # Cache for 5 seconds (job status changes frequently)
async def get_upscaling_job(job_id: str):
    """Get status of an upscaling job."""
    try:
        if job_id not in _upscaling_jobs:
            raise HTTPException(status_code=404, detail=f"Upscaling job '{job_id}' not found")

        job = _upscaling_jobs[job_id]

        return UpscalingResponse(
            job_id=job.job_id,
            status=job.status,
            progress=job.progress,
            output_file=job.output_file,
            original_width=job.original_width,
            original_height=job.original_height,
            upscaled_width=job.upscaled_width,
            upscaled_height=job.upscaled_height,
            error_message=job.error_message,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get upscaling job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get upscaling job: {e!s}",
        ) from e


@router.get("/jobs", response_model=list[UpscalingResponse])
@cache_response(ttl=10)  # Cache for 10 seconds (job list may change frequently)
async def list_upscaling_jobs():
    """List all upscaling jobs."""
    try:
        jobs = []
        for job in _upscaling_jobs.values():
            jobs.append(
                UpscalingResponse(
                    job_id=job.job_id,
                    status=job.status,
                    progress=job.progress,
                    output_file=job.output_file,
                    original_width=job.original_width,
                    original_height=job.original_height,
                    upscaled_width=job.upscaled_width,
                    upscaled_height=job.upscaled_height,
                    error_message=job.error_message,
                )
            )
        return sorted(jobs, key=lambda j: j.job_id, reverse=True)
    except Exception as e:
        logger.error(f"Failed to list upscaling jobs: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list upscaling jobs: {e!s}",
        ) from e


@router.delete("/jobs/{job_id}")
async def delete_upscaling_job(job_id: str):
    """Delete an upscaling job."""
    try:
        if job_id not in _upscaling_jobs:
            raise HTTPException(status_code=404, detail=f"Upscaling job '{job_id}' not found")

        del _upscaling_jobs[job_id]
        logger.info(f"Deleted upscaling job: {job_id}")

        return {"message": f"Upscaling job '{job_id}' deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete upscaling job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete upscaling job: {e!s}",
        ) from e


@router.get("/export/{job_id}")
async def export_upscaled_media(job_id: str):
    """
    Export upscaled media file.

    Args:
        job_id: Upscaling job ID

    Returns:
        Upscaled media file for download
    """
    try:
        if job_id not in _upscaling_jobs:
            raise HTTPException(status_code=404, detail=f"Upscaling job '{job_id}' not found")

        job = _upscaling_jobs[job_id]

        if job.status != "completed":
            raise HTTPException(
                status_code=400,
                detail=(f"Job '{job_id}' is not completed " f"(status: {job.status})"),
            )

        if not job.output_file or not os.path.exists(job.output_file):
            raise HTTPException(
                status_code=404,
                detail=f"Output file for job '{job_id}' not found",
            )

        # Determine media type and content type
        output_path = Path(job.output_file)
        file_extension = output_path.suffix.lower()

        content_types = {
            ".png": "image/png",
            ".jpg": "image/jpeg",
            ".jpeg": "image/jpeg",
            ".webp": "image/webp",
            ".mp4": "video/mp4",
            ".avi": "video/x-msvideo",
            ".mov": "video/quicktime",
        }

        content_type = content_types.get(file_extension, "application/octet-stream")

        from fastapi.responses import FileResponse

        return FileResponse(
            job.output_file,
            media_type=content_type,
            filename=output_path.name,
            headers={"Content-Disposition": (f'attachment; filename="{output_path.name}"')},
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to export upscaled media: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to export upscaled media: {e!s}",
        ) from e


@router.get("/engines", response_model=list[UpscalingEngine])
@cache_response(ttl=300)  # Cache for 5 minutes (engine list is relatively static)
async def list_upscaling_engines():
    """List all available upscaling engines."""
    return [
        UpscalingEngine(
            engine_id="realesrgan",
            name="Real-ESRGAN",
            description="High-quality image and video upscaling",
            supported_types=["image", "video"],
            supported_scales=[2.0, 4.0],
            is_available=True,
        ),
        UpscalingEngine(
            engine_id="esrgan",
            name="ESRGAN",
            description="Enhanced Super-Resolution GAN for images",
            supported_types=["image"],
            supported_scales=[2.0, 4.0],
            is_available=True,
        ),
        UpscalingEngine(
            engine_id="waifu2x",
            name="Waifu2x",
            description="Anime-style image upscaling",
            supported_types=["image"],
            supported_scales=[2.0, 4.0],
            is_available=True,
        ),
        UpscalingEngine(
            engine_id="swinir",
            name="SwinIR",
            description="Swin Transformer for image restoration",
            supported_types=["image"],
            supported_scales=[2.0, 4.0, 8.0],
            is_available=True,
        ),
    ]


async def _process_upscaling_job(job_id: str, file: UploadFile, request: UpscalingRequest):
    """Process upscaling job asynchronously."""
    import os
    import tempfile
    from datetime import datetime
    from pathlib import Path

    try:
        job = _upscaling_jobs.get(job_id)
        if not job:
            logger.warning(f"Upscaling job {job_id} not found")
            return

        job.status = "processing"
        job.progress = 10.0
        _upscaling_jobs[job_id] = job

        # Save uploaded file
        temp_dir = Path(tempfile.gettempdir()) / "upscaling" / job_id
        temp_dir.mkdir(parents=True, exist_ok=True)

        input_file_path = temp_dir / (
            file.filename or f"input.{'png' if request.media_type == 'image' else 'mp4'}"
        )
        with open(input_file_path, "wb") as f:
            content = await file.read()
            f.write(content)

        # Get original dimensions
        original_width = None
        original_height = None
        try:
            if request.media_type == "image":
                from PIL import Image

                with Image.open(input_file_path) as img:
                    original_width, original_height = img.size
            else:
                # For video, would need ffmpeg or similar
                original_width = 1920
                original_height = 1080
        except Exception as e:
            logger.warning(f"Failed to get image dimensions: {e}")
            original_width = 1920
            original_height = 1080

        job.original_width = original_width
        job.original_height = original_height
        job.progress = 30.0
        _upscaling_jobs[job_id] = job

        # Try to use Real-ESRGAN engine (ADR-008 compliant)
        try:
            engine_service = get_engine_service()
            engine = engine_service.get_realesrgan_engine()
            if not engine:
                raise Exception("Real-ESRGAN engine not available")
            # Set scale factor if engine supports it
            if hasattr(engine, "scale"):
                engine.scale = int(request.scale_factor)
            if not engine.initialize():
                raise Exception("Real-ESRGAN engine initialization failed")

            job.progress = 50.0
            _upscaling_jobs[job_id] = job

            # Process upscaling
            if request.media_type == "image":
                output_file_path = temp_dir / f"upscaled_{job_id}.{request.output_format or 'png'}"

                # Upscale image
                upscaled_image = engine.upscale(
                    image=str(input_file_path),
                    output_path=str(output_file_path),
                )

                if upscaled_image is not None and os.path.exists(output_file_path):
                    # Get upscaled dimensions
                    try:
                        from PIL import Image

                        with Image.open(output_file_path) as img:
                            upscaled_width, upscaled_height = img.size
                    except Exception:
                        upscaled_width = int(original_width * request.scale_factor)
                        upscaled_height = int(original_height * request.scale_factor)

                    job.status = "completed"
                    job.progress = 100.0
                    job.output_file = str(output_file_path)
                    job.upscaled_width = upscaled_width
                    job.upscaled_height = upscaled_height
                    job.completed_at = datetime.utcnow().isoformat()
                    _upscaling_jobs[job_id] = job

                    logger.info(f"Upscaling completed: {job_id}, " f"output: {output_file_path}")
                    return
                else:
                    raise Exception("Upscaling returned no output")
            else:
                # Video upscaling using OpenCV
                import cv2

                cap = cv2.VideoCapture(str(input_file_path))
                fps = cap.get(cv2.CAP_PROP_FPS)
                width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

                output_file_path = temp_dir / f"upscaled_{job_id}.{request.output_format or 'mp4'}"
                _fourcc_fn = getattr(cv2, "VideoWriter_fourcc", None)
                fourcc = _fourcc_fn(*"mp4v") if _fourcc_fn else 0
                out = cv2.VideoWriter(
                    str(output_file_path),
                    fourcc,
                    fps,
                    (
                        int(width * request.scale_factor),
                        int(height * request.scale_factor),
                    ),
                )

                while cap.isOpened():
                    ret, frame = cap.read()
                    if not ret:
                        break
                    upscaled_frame = cv2.resize(
                        frame,
                        (
                            int(width * request.scale_factor),
                            int(height * request.scale_factor),
                        ),
                        interpolation=cv2.INTER_LANCZOS4,
                    )
                    out.write(upscaled_frame)

                cap.release()
                out.release()

                job.status = "completed"
                job.progress = 100.0
                job.output_file = str(output_file_path)
                job.upscaled_width = int(width * request.scale_factor)
                job.upscaled_height = int(height * request.scale_factor)
                job.completed_at = datetime.utcnow().isoformat()
                _upscaling_jobs[job_id] = job

                logger.info(f"Video upscaling completed: {job_id}, output: {output_file_path}")
                return

        except (ImportError, AttributeError, Exception) as e:
            logger.warning(f"Real-ESRGAN engine not available or failed: {e}")

            # Fallback: Use PIL for simple image upscaling
            if request.media_type == "image":
                try:
                    from PIL import Image

                    job.progress = 60.0
                    _upscaling_jobs[job_id] = job

                    with Image.open(input_file_path) as img:
                        original_size = img.size
                        new_size = (
                            int(original_size[0] * request.scale_factor),
                            int(original_size[1] * request.scale_factor),
                        )

                        # Use LANCZOS resampling for better quality
                        upscaled_img = img.resize(new_size, Image.Resampling.LANCZOS)

                        output_file_path = (
                            temp_dir / f"upscaled_{job_id}.{request.output_format or 'png'}"
                        )
                        upscaled_img.save(output_file_path)

                        job.status = "completed"
                        job.progress = 100.0
                        job.output_file = str(output_file_path)
                        job.upscaled_width = new_size[0]
                        job.upscaled_height = new_size[1]
                        job.completed_at = datetime.utcnow().isoformat()
                        _upscaling_jobs[job_id] = job

                        logger.info(f"Upscaling completed (fallback): {job_id}")
                        return
                except Exception as e2:
                    logger.error(f"Fallback upscaling failed: {e2}")
                    raise

            raise Exception(f"Upscaling failed: {e!s}")

    except Exception as e:
        logger.error(f"Upscaling job {job_id} failed: {e}", exc_info=True)
        if job_id in _upscaling_jobs:
            job = _upscaling_jobs[job_id]
            job.status = "failed"
            job.error_message = str(e)
            job.completed_at = datetime.utcnow().isoformat()
            _upscaling_jobs[job_id] = job
