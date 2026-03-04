"""
Batch Processing Routes

Queue-based batch processing for voice synthesis operations.
"""

from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import uuid
from datetime import datetime
from enum import Enum
from typing import Any

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from backend.api.dependencies import require_synthesis_clearance
from backend.infrastructure.adapters.job_state_store import get_job_state_store
from backend.ml.models.engine_service import get_engine_service

from ..models import ApiOk
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/batch", tags=["batch"])

# Disk-backed job state store for persistence across restarts
_batch_store = get_job_state_store("batch_jobs")

# Enhanced job queue integration
_enhanced_job_queue = None
_resource_manager = None

from backend.services.persistent_store import PersistentStore

_batch_jobs: PersistentStore = PersistentStore("batch_jobs")
_job_queue: list[str] = []  # Queue of job IDs
_processing_jobs: set[str] = set()  # Jobs currently being processed
_state_lock = asyncio.Lock()


def _persist_batch_job(job_id: str, job_data: dict) -> None:
    """Persist batch job state to disk."""
    try:
        _batch_store.upsert(job_id, job_data)
    except Exception as e:
        logger.debug(f"Failed to persist batch job {job_id}: {e}")


def _load_persisted_batch_jobs() -> None:
    """Load persisted batch jobs from disk on startup."""
    global _batch_jobs
    try:
        for job_id, payload in _batch_store.load_all().items():
            try:
                # Mark incomplete jobs as failed on restart
                if payload.get("status") in ("pending", "processing", "running"):
                    payload["status"] = "failed"
                    payload["error"] = "Backend restarted during processing"
                    _batch_store.upsert(job_id, payload)
                _batch_jobs[job_id] = payload
            except Exception as e:
                logger.warning(f"Failed to restore batch job {job_id}: {e}")
    except Exception as e:
        logger.warning(f"Failed to load persisted batch jobs: {e}")


def _websocket_notifier(event_type: str, data: dict[str, Any]) -> None:
    """WebSocket notification callback for job events."""
    if HAS_WEBSOCKET:
        try:
            asyncio.ensure_future(
                realtime.broadcast_general_event(event_type, {"type": event_type, **data})
            )
        except Exception as e:
            logger.debug(f"WebSocket notification failed: {e}")


def _get_enhanced_job_queue():
    """Get or create enhanced job queue instance."""
    global _enhanced_job_queue, _resource_manager
    if _enhanced_job_queue is None:
        try:
            from backend.services.runtime_facade import (
                create_enhanced_job_queue,
                get_resource_manager,
            )

            _resource_manager = get_resource_manager()
            _enhanced_job_queue = create_enhanced_job_queue(
                resource_manager=_resource_manager,
                max_retries=3,
                enable_batching=True,
                websocket_notifier=_websocket_notifier,
            )
            logger.info("Enhanced job queue initialized with WebSocket support")
        except Exception as e:
            logger.warning(
                f"Failed to initialize enhanced job queue: {e}. " f"Falling back to simple queue."
            )
            _enhanced_job_queue = None
    return _enhanced_job_queue


# Try to import engine router for batch processing
# Engine availability checked via EngineService (ADR-008 compliant)
ENGINE_AVAILABLE = False

try:
    _engine_service = get_engine_service()
    engines = _engine_service.list_engines()
    ENGINE_AVAILABLE = len(engines) > 0
    if ENGINE_AVAILABLE:
        logger.info("EngineService available for batch processing")
    else:
        logger.warning("No engines available for batch processing")
except Exception as e:
    logger.warning(f"EngineService not available for batch processing: {e}")
    ENGINE_AVAILABLE = False

# Try to import WebSocket broadcasting
try:
    from ..ws import realtime

    HAS_WEBSOCKET = True
except ImportError:
    HAS_WEBSOCKET = False
    logger.warning("WebSocket realtime module not available for batch progress updates")

# Try importing joblib for parallel processing
try:
    from joblib import Parallel, delayed

    HAS_JOBLIB = True
except ImportError:
    HAS_JOBLIB = False
    Parallel = None
    delayed = None
    logger.debug("joblib not available. Parallel batch processing will be limited.")

# Try importing dask for distributed processing
try:
    import dask
    from dask import delayed as dask_delayed
    from dask.distributed import Client
    from dask.distributed import as_completed as dask_as_completed

    HAS_DASK = True
except ImportError:
    HAS_DASK = False
    dask = None
    dask_delayed = None
    Client = None
    dask_as_completed = None
    logger.debug("dask not available. Distributed batch processing will be limited.")


class JobStatus(str, Enum):
    """Status of a batch job."""

    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


class BatchJob(BaseModel):
    """A batch processing job."""

    id: str
    name: str
    project_id: str
    voice_profile_id: str
    engine_id: str
    text: str
    language: str = "en"
    output_path: str | None = None
    status: JobStatus = JobStatus.PENDING
    progress: float = 0.0  # 0.0 to 1.0
    error_message: str | None = None
    result_audio_id: str | None = None
    created: datetime
    started: datetime | None = None
    completed: datetime | None = None
    # Quality-based batch processing (IDEA 57)
    quality_metrics: dict | None = None  # Quality metrics dict
    quality_score: float | None = None  # Overall quality score (0.0-1.0)
    quality_threshold: float | None = None  # Minimum quality threshold
    quality_status: str | None = None  # "pass", "fail", "warning"


class BatchQueueStatus(BaseModel):
    """Batch queue status response."""

    queue_length: int
    pending: int
    running: int
    completed: int
    failed: int
    total: int


class BatchJobRequest(BaseModel):
    """Request to create a batch job."""

    name: str
    project_id: str
    voice_profile_id: str
    engine_id: str
    text: str
    language: str = "en"
    output_path: str | None = None
    # Quality-based batch processing (IDEA 57)
    quality_threshold: float | None = None  # Minimum quality threshold (0.0-1.0)
    enhance_quality: bool = False  # Enable quality enhancement


# Load persisted batch jobs on module import
_load_persisted_batch_jobs()


@router.post("/jobs", response_model=BatchJob)
async def create_batch_job(
    job_request: BatchJobRequest,
    _policy: None = Depends(require_synthesis_clearance),
):
    """Create a new batch processing job."""
    try:
        if not job_request.name or not job_request.name.strip():
            raise HTTPException(status_code=400, detail="Job name is required")
        if not job_request.project_id or not job_request.project_id.strip():
            raise HTTPException(status_code=400, detail="Project ID is required")
        if not job_request.voice_profile_id or not job_request.voice_profile_id.strip():
            raise HTTPException(status_code=400, detail="Voice profile ID is required")
        if not job_request.engine_id or not job_request.engine_id.strip():
            raise HTTPException(status_code=400, detail="Engine ID is required")
        if not job_request.text or not job_request.text.strip():
            raise HTTPException(status_code=400, detail="Text is required")

        job_id = str(uuid.uuid4())

        job = BatchJob(
            id=job_id,
            name=job_request.name.strip(),
            project_id=job_request.project_id,
            voice_profile_id=job_request.voice_profile_id,
            engine_id=job_request.engine_id,
            text=job_request.text.strip(),
            language=job_request.language,
            output_path=job_request.output_path,
            status=JobStatus.PENDING,
            created=datetime.utcnow(),
            quality_threshold=job_request.quality_threshold,
        )

        # Store enhance_quality flag in job_data for processing
        job_data = job.model_dump()
        job_data["enhance_quality"] = job_request.enhance_quality
        _batch_jobs[job_id] = job_data
        _persist_batch_job(job_id, job_data)
        _job_queue.append(job_id)

        logger.info(f"Created batch job {job_id}: {job_request.name}")

        return BatchJob(**job_data)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating batch job: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create batch job: {e!s}")


@router.get("/jobs", response_model=list[BatchJob])
@cache_response(ttl=10)  # Cache for 10 seconds (job list may change frequently)
async def list_batch_jobs(project_id: str | None = None, status: JobStatus | None = None):
    """List all batch jobs, optionally filtered by project or status."""
    try:
        jobs = [BatchJob(**job_data) for job_data in _batch_jobs.values()]

        if project_id:
            jobs = [j for j in jobs if j.project_id == project_id]

        if status:
            jobs = [j for j in jobs if j.status == status]

        # Sort by created time (newest first)
        jobs.sort(key=lambda x: x.created, reverse=True)

        logger.debug(f"Listed {len(jobs)} batch jobs (project_id={project_id}, status={status})")
        return jobs
    except Exception as e:
        logger.error(f"Error listing batch jobs: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list batch jobs: {e!s}")


@router.get("/jobs/{job_id}", response_model=BatchJob)
@cache_response(ttl=5)  # Cache for 5 seconds (job status changes frequently during processing)
async def get_batch_job(job_id: str):
    """Get a specific batch job."""
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        job_data = _batch_jobs.get(job_id)
        if not job_data:
            logger.warning(f"Batch job not found: {job_id}")
            raise HTTPException(status_code=404, detail="Batch job not found")

        logger.debug(f"Retrieved batch job: {job_id}")
        return BatchJob(**job_data)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting batch job {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get batch job: {e!s}")


@router.delete("/jobs/{job_id}", response_model=ApiOk)
async def delete_batch_job(job_id: str):
    """Delete a batch job."""
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        if job_id not in _batch_jobs:
            logger.warning(f"Batch job not found for deletion: {job_id}")
            raise HTTPException(status_code=404, detail="Batch job not found")

        # Remove from queue if present
        if job_id in _job_queue:
            _job_queue.remove(job_id)

        # Remove from processing set if present
        if job_id in _processing_jobs:
            _processing_jobs.remove(job_id)

        del _batch_jobs[job_id]
        _batch_store.delete(job_id)
        logger.info(f"Deleted batch job {job_id}")

        return ApiOk(ok=True)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting batch job {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete batch job: {e!s}")


@router.post("/jobs/{job_id}/start", response_model=BatchJob)
async def start_batch_job(
    job_id: str,
    _policy: None = Depends(require_synthesis_clearance),
):
    """Start processing a batch job."""
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        job_data = _batch_jobs.get(job_id)
        if not job_data:
            logger.warning(f"Batch job not found for start: {job_id}")
            raise HTTPException(status_code=404, detail="Batch job not found")

        job = BatchJob(**job_data)

        if job.status != JobStatus.PENDING:
            raise HTTPException(
                status_code=400, detail=f"Job is not pending (status: {job.status})"
            )

        # Check if job is already being processed
        if job_id in _processing_jobs:
            raise HTTPException(status_code=400, detail="Job is already being processed")

        # Update job status
        job.status = JobStatus.RUNNING
        job.started = datetime.utcnow()
        job.progress = 0.0

        job_data = job.model_dump()
        _batch_jobs[job_id] = job_data
        _persist_batch_job(job_id, job_data)

        # Remove from queue if present
        if job_id in _job_queue:
            _job_queue.remove(job_id)

        logger.info(f"Started batch job {job_id}")

        # Start processing asynchronously
        asyncio.create_task(_process_batch_job(job_id))

        return BatchJob(**job_data)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error starting batch job {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to start batch job: {e!s}")


@router.post("/jobs/{job_id}/cancel", response_model=BatchJob)
async def cancel_batch_job(job_id: str):
    """Cancel a batch job."""
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        job_data = _batch_jobs.get(job_id)
        if not job_data:
            logger.warning(f"Batch job not found for cancellation: {job_id}")
            raise HTTPException(status_code=404, detail="Batch job not found")

        job = BatchJob(**job_data)

        if job.status not in [JobStatus.PENDING, JobStatus.RUNNING]:
            raise HTTPException(
                status_code=400, detail=f"Cannot cancel job with status: {job.status}"
            )

        job.status = JobStatus.CANCELLED
        job.completed = datetime.utcnow()

        # Remove from queue if present
        if job_id in _job_queue:
            _job_queue.remove(job_id)

        # Remove from processing set if present
        if job_id in _processing_jobs:
            _processing_jobs.remove(job_id)

        job_data = job.model_dump()
        _batch_jobs[job_id] = job_data
        _persist_batch_job(job_id, job_data)

        logger.info(f"Cancelled batch job {job_id}")

        return BatchJob(**job_data)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error cancelling batch job {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to cancel batch job: {e!s}")


@router.get("/queue/status", response_model=BatchQueueStatus)
@cache_response(ttl=5)  # Cache for 5 seconds (queue status changes frequently)
async def get_queue_status():
    """Get the status of the batch processing queue."""
    try:
        pending = len(
            [j for j in _batch_jobs.values() if j.get("status") == JobStatus.PENDING.value]
        )
        running = len(
            [j for j in _batch_jobs.values() if j.get("status") == JobStatus.RUNNING.value]
        )
        completed = len(
            [j for j in _batch_jobs.values() if j.get("status") == JobStatus.COMPLETED.value]
        )
        failed = len([j for j in _batch_jobs.values() if j.get("status") == JobStatus.FAILED.value])

        status = BatchQueueStatus(
            queue_length=len(_job_queue),
            pending=pending,
            running=running,
            completed=completed,
            failed=failed,
            total=len(_batch_jobs),
        )

        logger.debug(f"Retrieved batch queue status: {status.model_dump()}")
        return status
    except Exception as e:
        logger.error(f"Error getting batch queue status: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get queue status: {e!s}")


async def _process_batch_job(job_id: str):
    """
    Process a batch job by performing voice synthesis.

    This function:
    1. Gets the engine instance
    2. Loads the voice profile
    3. Performs synthesis
    4. Saves the result
    5. Updates job status and broadcasts progress
    """
    job_data = _batch_jobs.get(job_id)
    if not job_data:
        logger.error(f"Job data not found for job {job_id}")
        return

    job = BatchJob(**job_data)

    # Check if job was cancelled
    if job.status == JobStatus.CANCELLED:
        logger.info(f"Job {job_id} was cancelled, skipping processing")
        _processing_jobs.discard(job_id)
        return

    # Mark as processing
    _processing_jobs.add(job_id)

    try:
        # Broadcast initial progress
        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "running",
                    "progress": 0.0,
                    "message": "Starting synthesis...",
                },
            )

        # Validate engine availability
        if not ENGINE_AVAILABLE:
            error_msg = "Voice synthesis engines are not available. Please ensure engines are properly installed and configured."
            logger.error(f"Batch job {job_id} failed: {error_msg}")

            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.0,
                        "error_message": error_msg,
                    },
                )
            return

        # Get engine instance via EngineService (ADR-008 compliant)
        try:
            if not ENGINE_AVAILABLE:
                raise HTTPException(status_code=503, detail="EngineService not available. Engines may not be loaded properly.")

            engine_service = get_engine_service()
            engine = engine_service.get_engine(job.engine_id)
            if engine is None:
                raise HTTPException(
                    status_code=503,
                    detail=f"Engine '{job.engine_id}' is not available. "
                    f"Please check that the engine is installed and configured correctly.",
                )
        except AttributeError as e:
            error_msg = (
                f"EngineService error: {e!s}. Engine system may not be properly initialized."
            )
            logger.error(f"Batch job {job_id} failed: {error_msg}", exc_info=True)
            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data
            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.0,
                        "error_message": error_msg,
                    },
                )
            return
        except Exception as e:
            error_msg = f"Failed to initialize engine '{job.engine_id}': {e!s}. Please check engine configuration and dependencies."
            logger.error(f"Batch job {job_id} failed: {error_msg}", exc_info=True)

            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.0,
                        "error_message": error_msg,
                    },
                )
            return

        # Update progress: Engine loaded
        job_data["progress"] = 0.1
        _batch_jobs[job_id] = job_data

        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "running",
                    "progress": 0.1,
                    "message": f"Engine '{job.engine_id}' loaded",
                },
            )

        # Get profile audio path
        from backend.services.profile_service import resolve_reference_audio_path

        resolved = resolve_reference_audio_path(job.voice_profile_id)
        profile_audio_path = str(resolved) if resolved.exists() else None

        # Fallback: try profile store audio_path if canonical path not found
        if not profile_audio_path or not os.path.exists(profile_audio_path):
            try:
                from backend.project.management.profile_store import get_profile_store

                _profile_store = get_profile_store()
                profile_data = _profile_store.get(job.voice_profile_id)
                if profile_data and isinstance(profile_data, dict):
                    store_path = profile_data.get("audio_path")
                    if store_path and os.path.exists(store_path):
                        profile_audio_path = store_path
            except Exception as e:
                logger.debug(f"Could not get profile audio path from profile store: {e}")

        if not profile_audio_path or not os.path.exists(profile_audio_path):
            error_msg = f"Profile audio not found for profile '{job.voice_profile_id}'. Please ensure the profile has a reference audio file."
            logger.error(f"Batch job {job_id} failed: {error_msg}")

            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.2,
                        "error_message": error_msg,
                    },
                )
            return

        # Update progress: Profile loaded
        job_data["progress"] = 0.2
        _batch_jobs[job_id] = job_data

        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "running",
                    "progress": 0.2,
                    "message": "Profile loaded, starting synthesis...",
                },
            )

        # Prepare output path
        try:
            if job.output_path:
                output_path = job.output_path
                # Validate output path
                if ".." in output_path or output_path.startswith("/") or ":" in output_path:
                    raise ValueError(
                        f"Invalid output path: {output_path}. Path contains invalid characters."
                    )

                output_dir = os.path.dirname(output_path)
                if output_dir:  # Only create directory if path has a directory component
                    try:
                        os.makedirs(output_dir, exist_ok=True)
                    except PermissionError:
                        raise PermissionError(
                            f"Permission denied when creating output directory '{output_dir}'. Please check directory permissions."
                        )
                    except OSError as e:
                        if "No space left" in str(e) or "disk full" in str(e).lower():
                            raise OSError(
                                f"Disk full. Cannot create output directory '{output_dir}'. Please free up space."
                            )
                        raise
            else:
                # Use temporary file or project directory
                if job.project_id:
                    from backend.services.path_service import PathService

                    project_audio_dir = PathService.get_projects_dir() / job.project_id / "audio"
                    try:
                        project_audio_dir.mkdir(parents=True, exist_ok=True)
                    except (PermissionError, OSError) as e:
                        error_msg = (
                            f"Failed to create project audio directory '{project_audio_dir}': {e!s}"
                        )
                        logger.error(f"Batch job {job_id} failed: {error_msg}")
                        job_data["status"] = JobStatus.FAILED.value
                        job_data["error_message"] = error_msg
                        job_data["completed"] = datetime.utcnow().isoformat()
                        _batch_jobs[job_id] = job_data
                        if HAS_WEBSOCKET:
                            await realtime.broadcast_batch_progress(
                                batch_id=job_id,
                                progress_data={
                                    "status": "failed",
                                    "progress": 0.25,
                                    "error_message": error_msg,
                                },
                            )
                        return
                    output_filename = f"batch_{job_id}.wav"
                    output_path = str(project_audio_dir / output_filename)
                else:
                    try:
                        with tempfile.NamedTemporaryFile(
                            delete=False, suffix=".wav"
                        ) as tmp:
                            output_path = tmp.name
                    except OSError as e:
                        error_msg = f"Failed to create temporary file: {e!s}"
                        logger.error(f"Batch job {job_id} failed: {error_msg}")
                        job_data["status"] = JobStatus.FAILED.value
                        job_data["error_message"] = error_msg
                        job_data["completed"] = datetime.utcnow().isoformat()
                        _batch_jobs[job_id] = job_data
                        if HAS_WEBSOCKET:
                            await realtime.broadcast_batch_progress(
                                batch_id=job_id,
                                progress_data={
                                    "status": "failed",
                                    "progress": 0.25,
                                    "error_message": error_msg,
                                },
                            )
                        return
        except (ValueError, PermissionError, OSError) as e:
            error_msg = f"Output path preparation failed: {e!s}"
            logger.error(f"Batch job {job_id} failed: {error_msg}", exc_info=True)
            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data
            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.25,
                        "error_message": error_msg,
                    },
                )
            return
        except Exception as e:
            error_msg = f"Unexpected error preparing output path: {e!s}"
            logger.error(f"Batch job {job_id} failed: {error_msg}", exc_info=True)
            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data
            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.25,
                        "error_message": error_msg,
                    },
                )
            return

        # Update progress: Starting synthesis
        job_data["progress"] = 0.3
        _batch_jobs[job_id] = job_data

        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "running",
                    "progress": 0.3,
                    "message": "Synthesizing audio...",
                },
            )

        # Perform synthesis
        if not hasattr(engine, "synthesize"):
            error_msg = (
                f"Engine '{job.engine_id}' does not support synthesis. "
                f"Please use a different engine that supports voice synthesis."
            )
            logger.error(f"Batch job {job_id} failed: {error_msg}")

            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": 0.3,
                        "error_message": error_msg,
                    },
                )
            return

        # Ensure engine is initialized
        if not engine.is_initialized():
            engine.initialize()

        # Prepare synthesis parameters
        # Check if job has quality threshold from request
        enhance_quality = job_data.get("enhance_quality", False)
        synthesis_kwargs = {
            "text": job.text,
            "speaker_wav": (profile_audio_path if os.path.exists(profile_audio_path) else None),
            "language": job.language,
            "output_path": output_path,
            "calculate_quality": True,
            "enhance_quality": enhance_quality,
        }

        # Update progress during synthesis
        job_data["progress"] = 0.5
        _batch_jobs[job_id] = job_data

        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "running",
                    "progress": 0.5,
                    "message": "Processing synthesis...",
                },
            )

        # Perform synthesis with error handling
        try:
            result = engine.synthesize(**synthesis_kwargs)

            # Handle both single return and tuple (audio, metrics)
            if isinstance(result, tuple):
                audio, quality_metrics = result
            else:
                audio = result
                quality_metrics = {}

            if audio is None:
                raise HTTPException(status_code=500, detail="Engine returned None - synthesis may have failed")

            # Calculate quality score from metrics (IDEA 57)
            quality_score = None
            if quality_metrics:
                # Extract quality score from metrics
                if isinstance(quality_metrics, dict):
                    quality_score = quality_metrics.get("quality_score")
                    if quality_score is None:
                        # Calculate from individual metrics if available
                        mos = quality_metrics.get("mos_score", 0)
                        similarity = quality_metrics.get("similarity", 0)
                        naturalness = quality_metrics.get("naturalness", 0)
                        if mos > 0 or similarity > 0 or naturalness > 0:
                            # Normalize and average
                            mos_norm = (mos - 1.0) / 4.0 if mos > 0 else 0  # 1-5 -> 0-1
                            quality_score = (mos_norm + similarity + naturalness) / 3.0

            # Validate quality threshold if set (IDEA 57)
            quality_status = None
            if job.quality_threshold is not None and quality_score is not None:
                if quality_score >= job.quality_threshold:
                    quality_status = "pass"
                elif quality_score >= job.quality_threshold * 0.9:  # 10% tolerance
                    quality_status = "warning"
                else:
                    quality_status = "fail"
                    # Optionally fail the job if quality is too low
                    # Mark quality status but continue processing

            # Store quality metrics in job data (IDEA 57)
            job_data["quality_metrics"] = quality_metrics if quality_metrics else None
            job_data["quality_score"] = quality_score
            job_data["quality_status"] = quality_status

            # Update progress: Synthesis complete
            job_data["progress"] = 0.9
            _batch_jobs[job_id] = job_data

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "running",
                        "progress": 0.9,
                        "message": "Synthesis complete, finalizing...",
                    },
                )

            # Generate audio ID for result
            audio_id = f"batch_{job_id}_{uuid.uuid4().hex[:8]}"

            # Register audio file via artifact spine if it exists
            if os.path.exists(output_path):
                try:
                    from backend.services.audio_artifacts import create_audio_artifact_from_file

                    audio_id, _, _ = create_audio_artifact_from_file(
                        output_path,
                        created_by="batch",
                        audio_id=audio_id,
                        delete_source=False,
                    )
                except Exception as e:
                    logger.warning(f"Could not register audio file: {e}")

            # Mark job as completed
            job_data["status"] = JobStatus.COMPLETED.value
            job_data["progress"] = 1.0
            job_data["result_audio_id"] = audio_id
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data
            _persist_batch_job(job_id, job_data)

            logger.info(f"Batch job {job_id} completed successfully. Audio ID: {audio_id}")

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "completed",
                        "progress": 1.0,
                        "message": "Batch job completed successfully",
                        "result_audio_id": audio_id,
                    },
                )

        except Exception as synthesis_error:
            error_msg = f"Synthesis failed: {synthesis_error!s}"
            logger.error(f"Batch job {job_id} synthesis error: {error_msg}", exc_info=True)

            job_data["status"] = JobStatus.FAILED.value
            job_data["error_message"] = error_msg
            job_data["completed"] = datetime.utcnow().isoformat()
            _batch_jobs[job_id] = job_data
            _persist_batch_job(job_id, job_data)

            if HAS_WEBSOCKET:
                await realtime.broadcast_batch_progress(
                    batch_id=job_id,
                    progress_data={
                        "status": "failed",
                        "progress": job_data.get("progress", 0.5),
                        "error_message": error_msg,
                    },
                )

    except Exception as e:
        error_msg = f"Batch job processing failed: {e!s}"
        logger.error(f"Batch job {job_id} processing error: {error_msg}", exc_info=True)

        job_data = _batch_jobs.get(job_id, {})
        job_data["status"] = JobStatus.FAILED.value
        job_data["error_message"] = error_msg
        job_data["completed"] = datetime.utcnow().isoformat()
        _batch_jobs[job_id] = job_data
        _persist_batch_job(job_id, job_data)

        if HAS_WEBSOCKET:
            await realtime.broadcast_batch_progress(
                batch_id=job_id,
                progress_data={
                    "status": "failed",
                    "progress": job_data.get("progress", 0.0),
                    "error_message": error_msg,
                },
            )

    finally:
        # Remove from processing set
        _processing_jobs.discard(job_id)


# Quality-Based Batch Processing endpoints (IDEA 57)


class BatchQualityReport(BaseModel):
    """Quality report for a batch job (IDEA 57)."""

    job_id: str
    job_name: str
    quality_score: float | None = None
    quality_status: str | None = None
    quality_threshold: float | None = None
    metrics: dict[str, Any] = {}
    summary: dict[str, Any] = {}
    comparison: dict[str, Any] | None = None


class BatchQualityStatistics(BaseModel):
    """Quality statistics for a batch of jobs (IDEA 57)."""

    total_jobs: int
    completed_jobs: int
    jobs_with_quality: int
    average_quality: float | None = None
    min_quality: float | None = None
    max_quality: float | None = None
    quality_distribution: dict[str, int] = {}
    status_distribution: dict[str, int] = {}


class BatchRetryWithQualityRequest(BaseModel):
    """Request to retry a batch job with quality settings (IDEA 57)."""

    quality_threshold: float | None = None
    enhance_quality: bool = False
    quality_mode: str | None = None


@router.get("/jobs/{job_id}/quality", response_model=BatchQualityReport)
@cache_response(ttl=10)  # Cache for 10 seconds (quality metrics may update during processing)
async def get_batch_job_quality(job_id: str):
    """
    Get quality metrics for a batch job (IDEA 57).
    """
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        job_data = _batch_jobs.get(job_id)
        if not job_data:
            raise HTTPException(status_code=404, detail="Batch job not found")

        # Generate quality report
        report = generate_batch_quality_report(job_data)

        return BatchQualityReport(**report)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting batch job quality {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get batch job quality: {e!s}")


@router.get("/jobs/{job_id}/quality-report", response_model=BatchQualityReport)
@cache_response(ttl=10)  # Cache for 10 seconds (quality report may update during processing)
async def get_batch_quality_report(job_id: str):
    """
    Get detailed quality report for a batch job (IDEA 57).
    Includes comparison with other completed jobs.
    """
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        job_data = _batch_jobs.get(job_id)
        if not job_data:
            raise HTTPException(status_code=404, detail="Batch job not found")

        # Get all jobs for comparison
        all_jobs = list(_batch_jobs.values())

        # Generate comprehensive quality report
        report = generate_batch_quality_report(job_data, all_jobs=all_jobs)

        return BatchQualityReport(**report)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting batch quality report {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get batch quality report: {e!s}")


@router.get("/quality/statistics", response_model=BatchQualityStatistics)
@cache_response(ttl=30)  # Cache for 30 seconds (statistics aggregate data)
async def get_batch_quality_statistics(
    project_id: str | None = None, status: JobStatus | None = None
):
    """
    Get quality statistics for batch jobs (IDEA 57).
    Optionally filtered by project or status.
    """
    try:
        jobs = list(_batch_jobs.values())

        # Filter by project
        if project_id:
            jobs = [j for j in jobs if j.get("project_id") == project_id]

        # Filter by status
        if status:
            jobs = [j for j in jobs if j.get("status") == status.value]

        # Calculate statistics
        stats = calculate_batch_statistics(jobs)

        return BatchQualityStatistics(**stats)
    except Exception as e:
        logger.error(f"Error getting batch quality statistics: {e!s}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Failed to get batch quality statistics: {e!s}"
        )


@router.post("/jobs/{job_id}/retry-with-quality", response_model=BatchJob)
async def retry_batch_job_with_quality(
    job_id: str,
    request: BatchRetryWithQualityRequest,
    _policy: None = Depends(require_synthesis_clearance),
):
    """
    Retry a failed batch job with quality settings (IDEA 57).
    Creates a new job based on the failed one with updated quality settings.
    """
    try:
        if not job_id or not job_id.strip():
            raise HTTPException(status_code=400, detail="Job ID is required")

        original_job_data = _batch_jobs.get(job_id)
        if not original_job_data:
            raise HTTPException(status_code=404, detail="Batch job not found")

        original_job = BatchJob(**original_job_data)

        # Create new job request based on original
        retry_request = BatchJobRequest(
            name=f"{original_job.name} (Retry)",
            project_id=original_job.project_id,
            voice_profile_id=original_job.voice_profile_id,
            engine_id=original_job.engine_id,
            text=original_job.text,
            language=original_job.language,
            output_path=original_job.output_path,
            quality_threshold=request.quality_threshold or original_job.quality_threshold,
            enhance_quality=request.enhance_quality,
        )

        # Create new job
        return await create_batch_job(retry_request)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error retrying batch job with quality {job_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to retry batch job: {e!s}")


def process_batch_jobs_parallel(
    job_ids: list[str], max_workers: int | None = None, use_dask: bool = False
) -> dict[str, Any]:
    """
    Process multiple batch jobs in parallel using joblib or dask.

    Args:
        job_ids: List of job IDs to process
        max_workers: Maximum number of parallel workers (None = auto)
        use_dask: If True, use dask for distributed processing

    Returns:
        Dictionary with results for each job
    """
    if use_dask and HAS_DASK:
        # Use dask for distributed processing
        try:
            # Create dask client (local cluster)
            with Client(processes=False, n_workers=max_workers or 4):
                # Create delayed tasks
                tasks = []
                for job_id in job_ids:
                    task = dask_delayed(_process_batch_job_sync)(job_id)
                    tasks.append((job_id, task))

                # Compute all tasks
                results = dask.compute(*[task for _, task in tasks])

                # Map results back to job IDs
                return {job_id: result for (job_id, _), result in zip(tasks, results)}
        except Exception as e:
            logger.warning(f"Dask parallel processing failed: {e}. Falling back to joblib.")
            use_dask = False

    if HAS_JOBLIB:
        # Use joblib for parallel processing
        try:
            results = Parallel(n_jobs=max_workers, backend="threading")(
                delayed(_process_batch_job_sync)(job_id) for job_id in job_ids
            )
            return dict(zip(job_ids, results))
        except Exception as e:
            logger.warning(f"Joblib parallel processing failed: {e}. Falling back to sequential.")

    # Fallback to sequential processing
    results = {}
    for job_id in job_ids:
        try:
            results[job_id] = _process_batch_job_sync(job_id)
        except Exception as e:
            logger.error(f"Failed to process job {job_id}: {e}")
            results[job_id] = {"error": str(e)}

    return results


def _process_batch_job_sync(job_id: str) -> dict[str, Any]:
    """
    Synchronous wrapper for batch job processing (for parallel execution).

    Args:
        job_id: Job ID to process

    Returns:
        Dictionary with processing results
    """
    import asyncio

    # Run async function in sync context
    try:
        loop = asyncio.get_event_loop()
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)

    try:
        loop.run_until_complete(_process_batch_job(job_id))
        job_data = _batch_jobs.get(job_id, {})
        return {
            "job_id": job_id,
            "status": job_data.get("status"),
            "success": job_data.get("status") == JobStatus.COMPLETED.value,
        }
    except Exception as e:
        logger.error(f"Error in sync batch processing for {job_id}: {e}")
        return {"job_id": job_id, "status": "failed", "success": False, "error": str(e)}


def generate_batch_quality_report(
    job_data: dict[str, Any], all_jobs: list[dict[str, Any]] | None = None
) -> dict[str, Any]:
    """
    Generate quality report for a batch job.

    Args:
        job_data: Job data dictionary
        all_jobs: Optional list of all jobs for comparison

    Returns:
        Dictionary with quality report
    """
    quality_score = job_data.get("quality_score")
    quality_status = job_data.get("quality_status")
    quality_threshold = job_data.get("quality_threshold")

    report: dict[str, Any] = {
        "job_id": job_data.get("id"),
        "job_name": job_data.get("name"),
        "quality_score": quality_score,
        "quality_status": quality_status,
        "quality_threshold": quality_threshold,
        "metrics": job_data.get("quality_metrics", {}),
        "summary": {},
    }

    if quality_score is not None:
        report["summary"] = {
            "score": quality_score,
            "status": quality_status,
            "meets_threshold": (
                quality_score >= quality_threshold if quality_threshold is not None else None
            ),
        }

    if all_jobs:
        completed_jobs = [
            j
            for j in all_jobs
            if j.get("status") == JobStatus.COMPLETED.value and j.get("quality_score") is not None
        ]
        if completed_jobs:
            scores: list[float] = [float(j["quality_score"]) for j in completed_jobs]
            report["comparison"] = {
                "average": sum(scores) / len(scores) if scores else None,
                "min": min(scores) if scores else None,
                "max": max(scores) if scores else None,
                "percentile": (
                    sum(1 for s in scores if s < quality_score) / len(scores) * 100
                    if quality_score is not None and scores
                    else None
                ),
            }

    return report


def calculate_batch_statistics(jobs: list[dict[str, Any]]) -> dict[str, Any]:
    """
    Calculate quality statistics for a batch of jobs.

    Args:
        jobs: List of job data dictionaries

    Returns:
        Dictionary with statistics
    """
    total_jobs = len(jobs)
    completed_jobs = [j for j in jobs if j.get("status") == JobStatus.COMPLETED.value]
    jobs_with_quality = [j for j in completed_jobs if j.get("quality_score") is not None]

    quality_distribution: dict[str, int] = {}
    status_distribution: dict[str, int] = {}

    stats: dict[str, Any] = {
        "total_jobs": total_jobs,
        "completed_jobs": len(completed_jobs),
        "jobs_with_quality": len(jobs_with_quality),
        "average_quality": None,
        "min_quality": None,
        "max_quality": None,
        "quality_distribution": quality_distribution,
        "status_distribution": status_distribution,
    }

    if jobs_with_quality:
        scores: list[float] = [float(j["quality_score"]) for j in jobs_with_quality]
        stats["average_quality"] = sum(scores) / len(scores)
        stats["min_quality"] = min(scores)
        stats["max_quality"] = max(scores)

        for job in jobs_with_quality:
            score = float(job.get("quality_score", 0))
            bucket = (
                "excellent"
                if score >= 0.9
                else "good" if score >= 0.7 else "fair" if score >= 0.5 else "poor"
            )
            quality_distribution[bucket] = quality_distribution.get(bucket, 0) + 1

    for job in jobs:
        job_status = str(job.get("status", "unknown"))
        status_distribution[job_status] = status_distribution.get(job_status, 0) + 1

    return stats
