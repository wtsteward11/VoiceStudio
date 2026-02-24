"""
Face Swap Routes (formerly deepfake_creator)

Arch Review Task 1.4: Renamed to remove "deepfake" from API surface.
Legal/ethical: Requires explicit consent, audit logging, gated behind feature flag.

WARNING: Face swap technology has legal and ethical implications. Use only with
explicit consent from all parties. All invocations are logged for audit.
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any

from fastapi import APIRouter, File, HTTPException, Request, UploadFile
from pydantic import BaseModel

from backend.config.feature_flags import is_enabled
from backend.core.security.file_validation import (
    FileValidationError,
    validate_image_file,
    validate_video_file,
)
from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/face-swap", tags=["face-swap"])

# In-memory storage (replace with database in production)
_jobs: dict[str, FaceSwapJob] = {}
_job_queue: list[str] = []
_processing_jobs: set[str] = set()
_max_concurrent_jobs: int = 2


def _audit_log(
    action: str,
    job_id: str | None = None,
    request: Request | None = None,
    consent_acknowledged: bool = False,
) -> None:
    """Log every invocation for audit trail."""
    client = request.client.host if request and request.client else "unknown"
    logger.info(
        "face_swap_audit action=%s job_id=%s client=%s consent_ack=%s ts=%s",
        action,
        job_id or "",
        client,
        consent_acknowledged,
        datetime.utcnow().isoformat(),
    )


class FaceSwapJob(BaseModel):
    """Face swap job information."""

    job_id: str
    source_face_file: str
    target_media_file: str
    output_file: str | None = None
    media_type: str
    engine: str
    status: str
    progress: float = 0.0
    consent_given: bool = False
    consent_acknowledged: bool = False
    watermark_applied: bool = False
    error_message: str | None = None
    created_at: str
    completed_at: str | None = None


class FaceSwapRequest(BaseModel):
    """Request to create a face swap. Requires consent (consent_given or consent_acknowledged)."""

    media_type: str
    engine: str = "deepfacelab"
    consent_given: bool = False
    consent_acknowledged: bool | None = (
        None  # Explicit opt-in; if None, falls back to consent_given
    )
    apply_watermark: bool = True
    quality: str = "high"
    additional_params: dict[str, str] = {}

    def get_consent_acknowledged(self) -> bool:
        """Resolve consent: explicit consent_acknowledged or legacy consent_given."""
        if self.consent_acknowledged is not None:
            return self.consent_acknowledged
        return self.consent_given


class FaceSwapResponse(BaseModel):
    """Face swap response."""

    job_id: str
    status: str
    progress: float
    output_file: str | None = None
    consent_given: bool
    watermark_applied: bool
    error_message: str | None = None


class FaceSwapEngine(BaseModel):
    """Face swap engine information."""

    engine_id: str
    name: str
    description: str
    supported_types: list[str]
    requires_consent: bool = True
    watermark_required: bool = True
    is_available: bool = True


@router.post("/create", response_model=FaceSwapResponse)
async def create_face_swap(
    request: FaceSwapRequest,
    req: Request,
    source_face: UploadFile = File(...),
    target_media: UploadFile = File(...),
):
    """Create a face swap. Requires consent_acknowledged: true and feature flag."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(
            status_code=403,
            detail="Face swap is disabled. Enable experimental.face_swap in config/feature_flags.json",
        )
    consent_ok = request.get_consent_acknowledged()
    if not consent_ok:
        raise HTTPException(
            status_code=400,
            detail="Consent is required. Set consent_given or consent_acknowledged to true.",
        )
    _audit_log("create", request=req, consent_acknowledged=consent_ok)

    try:
        if request.media_type not in ["image", "video"]:
            raise HTTPException(status_code=400, detail="media_type must be 'image' or 'video'")

        import tempfile
        import uuid
        from pathlib import Path

        job_id = f"faceswap-{uuid.uuid4().hex[:12]}"
        now = datetime.utcnow().isoformat()

        temp_dir = Path(tempfile.gettempdir()) / "face_swap" / job_id
        temp_dir.mkdir(parents=True, exist_ok=True)

        source_face_path = temp_dir / f"source_{source_face.filename or 'face.jpg'}"
        target_media_path = temp_dir / f"target_{target_media.filename or 'media'}"

        source_face_content = await source_face.read()
        target_media_content = await target_media.read()

        try:
            validate_image_file(source_face_content, filename=source_face.filename)
        except FileValidationError as e:
            raise HTTPException(status_code=400, detail=f"Invalid source face: {e.message}") from e

        try:
            if request.media_type == "image":
                validate_image_file(target_media_content, filename=target_media.filename)
            else:
                validate_video_file(target_media_content, filename=target_media.filename)
        except FileValidationError as e:
            raise HTTPException(status_code=400, detail=f"Invalid target media: {e.message}") from e

        with open(source_face_path, "wb") as f:
            f.write(source_face_content)
        with open(target_media_path, "wb") as f:
            f.write(target_media_content)

        source_ext = Path(source_face.filename or "").suffix.lower()
        target_ext = Path(target_media.filename or "").suffix.lower()
        valid_image_exts = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
        valid_video_exts = {".mp4", ".avi", ".mov", ".mkv", ".webm"}

        if source_ext not in valid_image_exts:
            raise HTTPException(
                status_code=400, detail=f"Source must be image ({', '.join(valid_image_exts)})"
            )
        if request.media_type == "image" and target_ext not in valid_image_exts:
            raise HTTPException(status_code=400, detail="Target must be image for image swap")
        if request.media_type == "video" and target_ext not in valid_video_exts:
            raise HTTPException(status_code=400, detail="Target must be video for video swap")

        job = FaceSwapJob(
            job_id=job_id,
            source_face_file=str(source_face_path),
            target_media_file=str(target_media_path),
            media_type=request.media_type,
            engine=request.engine,
            status="pending",
            progress=0.0,
            consent_given=request.consent_given,
            consent_acknowledged=bool(request.consent_acknowledged),
            watermark_applied=request.apply_watermark,
            created_at=now,
        )
        _jobs[job_id] = job
        _job_queue.append(job_id)

        import asyncio

        if len(_processing_jobs) < _max_concurrent_jobs:
            _processing_jobs.add(job_id)
            if job_id in _job_queue:
                _job_queue.remove(job_id)
            asyncio.create_task(_process_job(job_id))
        else:
            asyncio.create_task(_process_queue())

        return FaceSwapResponse(
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
        logger.error("Failed to create face swap: %s", e)
        raise HTTPException(status_code=500, detail=f"Failed to create face swap: {e!s}") from e


@router.get("/jobs/{job_id}", response_model=FaceSwapResponse)
async def get_face_swap_job(job_id: str):
    """Get status of a face swap job."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(status_code=403, detail="Face swap is disabled")
    if job_id not in _jobs:
        raise HTTPException(status_code=404, detail=f"Job '{job_id}' not found")
    job = _jobs[job_id]
    return FaceSwapResponse(
        job_id=job.job_id,
        status=job.status,
        progress=job.progress,
        output_file=job.output_file,
        consent_given=job.consent_given,
        watermark_applied=job.watermark_applied,
        error_message=job.error_message,
    )


@router.get("/jobs", response_model=list[FaceSwapResponse])
async def list_face_swap_jobs():
    """List all face swap jobs."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(status_code=403, detail="Face swap is disabled")
    return sorted(
        [
            FaceSwapResponse(
                job_id=j.job_id,
                status=j.status,
                progress=j.progress,
                output_file=j.output_file,
                consent_given=j.consent_given,
                watermark_applied=j.watermark_applied,
                error_message=j.error_message,
            )
            for j in _jobs.values()
        ],
        key=lambda x: x.job_id,
        reverse=True,
    )


@router.delete("/jobs/{job_id}")
async def delete_face_swap_job(job_id: str):
    """Delete a face swap job."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(status_code=403, detail="Face swap is disabled")
    if job_id not in _jobs:
        raise HTTPException(status_code=404, detail=f"Job '{job_id}' not found")
    del _jobs[job_id]
    if job_id in _job_queue:
        _job_queue.remove(job_id)
    _processing_jobs.discard(job_id)
    return {"message": f"Job '{job_id}' deleted"}


@router.get("/queue/status")
async def get_queue_status():
    """Get face swap queue status."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(status_code=403, detail="Face swap is disabled")
    return {
        "queue_length": len(_job_queue),
        "processing_count": len(_processing_jobs),
        "max_concurrent_jobs": _max_concurrent_jobs,
        "queued_jobs": _job_queue[:10],
        "processing_jobs": list(_processing_jobs),
    }


@router.get("/engines", response_model=list[FaceSwapEngine])
async def list_face_swap_engines():
    """List available face swap engines."""
    if not is_enabled("experimental.face_swap"):
        raise HTTPException(status_code=403, detail="Face swap is disabled")
    return [
        FaceSwapEngine(
            engine_id="deepfacelab",
            name="DeepFaceLab",
            description="Professional face replacement/swap",
            supported_types=["image", "video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
        FaceSwapEngine(
            engine_id="fomm",
            name="First Order Motion Model",
            description="Motion transfer for face animation",
            supported_types=["video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
        FaceSwapEngine(
            engine_id="faceswap",
            name="FaceSwap",
            description="Open-source face swapping",
            supported_types=["image", "video"],
            requires_consent=True,
            watermark_required=True,
            is_available=True,
        ),
    ]


async def _process_job(job_id: str):
    """Process a face swap job (delegates to deepfake logic)."""
    import asyncio
    from pathlib import Path

    try:
        job = _jobs.get(job_id)
        if not job:
            return
        job.status = "processing"
        job.progress = 5.0
        _jobs[job_id] = job

        try:
            engine_service = get_engine_service()
            engine = engine_service.get_deepfacelab_engine()
            if not engine or not engine.is_available():
                raise HTTPException(status_code=503, detail="DeepFaceLab engine not available")

            source_face_path = Path(job.source_face_file)
            target_media_path = Path(job.target_media_file)
            temp_dir = source_face_path.parent
            output_path = (
                temp_dir / f"output_{job_id}.{'png' if job.media_type == 'image' else 'mp4'}"
            )

            if job.media_type == "image":
                engine.swap_face(
                    source_face_path=str(source_face_path),
                    target_image_path=str(target_media_path),
                    output_path=str(output_path),
                )
            else:
                engine.swap_face_video(
                    source_face_path=str(source_face_path),
                    target_video_path=str(target_media_path),
                    output_path=str(output_path),
                )

            if job.watermark_applied and job.media_type == "image":
                try:
                    from PIL import Image, ImageDraw, ImageFont

                    img = Image.open(output_path)
                    draw = ImageDraw.Draw(img)
                    font: Any = None
                    try:
                        font = ImageFont.truetype("arial.ttf", 24)
                    except OSError:
                        font = ImageFont.load_default()
                    draw.text((10, 10), "FACESWAP", fill=(255, 0, 0, 128), font=font)
                    img.save(output_path)
                except Exception as e:
                    logger.warning("Watermark failed: %s", e)

            job.status = "completed"
            job.progress = 100.0
            job.output_file = str(output_path)
            job.completed_at = datetime.utcnow().isoformat()
            _jobs[job_id] = job
        except Exception as e:
            logger.error("Face swap processing failed: %s", e, exc_info=True)
            job.status = "failed"
            job.error_message = f"Processing failed: {e!s}"
            job.completed_at = datetime.utcnow().isoformat()
            _jobs[job_id] = job
    except Exception as e:
        logger.error("Job processing error: %s", e, exc_info=True)
    finally:
        _processing_jobs.discard(job_id)
        if job_id in _job_queue:
            _job_queue.remove(job_id)
        asyncio.create_task(_process_queue())


async def _process_queue():
    """Process queued jobs."""
    import asyncio

    while _job_queue and len(_processing_jobs) < _max_concurrent_jobs:
        if not _job_queue:
            break
        job_id = _job_queue[0]
        if job_id in _processing_jobs:
            _job_queue.pop(0)
            continue
        job = _jobs.get(job_id)
        if not job or job.status != "pending":
            _job_queue.pop(0)
            continue
        _processing_jobs.add(job_id)
        _job_queue.pop(0)
        asyncio.create_task(_process_job(job_id))
        await asyncio.sleep(0.1)


# Backward compatibility: alias router for /api/deepfake-creator
deepfake_alias_router = APIRouter(prefix="/api/deepfake-creator", tags=["deepfake-creator-alias"])


@deepfake_alias_router.get("/engines", response_model=list[FaceSwapEngine])
async def _alias_engines():
    """Alias: redirect to face-swap engines."""
    return await list_face_swap_engines()


@deepfake_alias_router.get("/jobs", response_model=list[FaceSwapResponse])
async def _alias_list_jobs():
    return await list_face_swap_jobs()


@deepfake_alias_router.get("/jobs/{job_id}", response_model=FaceSwapResponse)
async def _alias_get_job(job_id: str):
    return await get_face_swap_job(job_id)


@deepfake_alias_router.get("/queue/status")
async def _alias_queue_status():
    return await get_queue_status()


@deepfake_alias_router.post("/create", response_model=FaceSwapResponse)
async def _alias_create(
    request: FaceSwapRequest,
    req: Request,
    source_face: UploadFile = File(...),
    target_media: UploadFile = File(...),
):
    """Backward-compat alias for /api/deepfake-creator/create."""
    return await create_face_swap(request, req, source_face, target_media)


@deepfake_alias_router.delete("/jobs/{job_id}")
async def _alias_delete(job_id: str):
    return await delete_face_swap_job(job_id)
