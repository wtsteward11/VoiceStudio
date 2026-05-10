"""
Orchestrator API Routes — Phase X-A

REST and WebSocket endpoints for the intelligent orchestration system.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, WebSocket, WebSocketDisconnect

from backend.orchestrator.presets import get_presets_service
from backend.orchestrator.scheduler import get_job_scheduler
from backend.orchestrator.schemas import (
    GpuStatusResponse,
    OrchestrationRequest,
    OrchestrationResponse,
    OrchestrationStatus,
    StrategyPreset,
)
from backend.orchestrator.service import get_orchestration_service

from ..middleware.auth_middleware import require_auth_if_enabled

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/orchestrator",
    tags=["orchestrator"],
    dependencies=[Depends(require_auth_if_enabled)],
)


@router.post("/run", response_model=OrchestrationResponse)
async def run_orchestration(request: OrchestrationRequest) -> OrchestrationResponse:
    """
    Execute an orchestration pipeline.

    If async_mode=true (default), returns immediately with job_id and status=queued.
    The job is submitted to the scheduler which handles priority, GPU awareness,
    and concurrency limits.
    If async_mode=false, blocks until the pipeline completes or fails.
    """
    service = get_orchestration_service()

    if request.async_mode:
        response = service.submit_async(request)
        scheduler = get_job_scheduler()
        scheduler.submit(response.job_id, request)
        _try_run_next(service, scheduler)
        return response
    else:
        return service.run_sync(request)


def _try_run_next(service: Any, scheduler: Any) -> None:
    """Attempt to schedule and execute the next queued job in a background thread."""
    job = scheduler.try_schedule_next()
    if job is None:
        return

    import concurrent.futures

    def _run(job_id: str) -> None:
        try:
            result = service.execute_job(job_id)
            duration_ms = result.total_execution_time_ms or 0
            engine = result.engine_used
            scheduler.mark_completed(job_id, duration_ms=duration_ms, engine_id=engine)
        except Exception as exc:
            logger.exception("Scheduled job %s failed", job_id)
            scheduler.mark_failed(job_id)
        finally:
            _try_run_next(service, scheduler)

    executor = concurrent.futures.ThreadPoolExecutor(
        max_workers=1, thread_name_prefix="orchestrator-job"
    )
    executor.submit(_run, job.job_id)
    # ``shutdown(wait=False)`` lets the in-flight ``_run`` finish, then signals
    # the worker thread to exit. Without this, CPython's non-daemon executor
    # workers can survive interpreter shutdown long enough to delay process
    # exit, contributing to PR #49's post-pytest hang on CI.
    executor.shutdown(wait=False)


@router.get("/status/{job_id}")
async def get_status(job_id: str) -> dict[str, Any]:
    """Poll orchestration job status."""
    service = get_orchestration_service()
    status = service.get_status(job_id)
    if status is None:
        raise HTTPException(status_code=404, detail=f"Job {job_id} not found")
    return status.model_dump(exclude_none=True)


@router.post("/cancel/{job_id}")
async def cancel_job(job_id: str) -> dict[str, Any]:
    """Cancel a running orchestration job."""
    service = get_orchestration_service()
    success = service.cancel(job_id)
    if not success:
        raise HTTPException(status_code=404, detail=f"Job {job_id} not found")
    return {"job_id": job_id, "status": "cancelled"}


@router.get("/presets")
async def list_presets() -> list[dict[str, Any]]:
    """List all available strategy presets (built-in and user-saved)."""
    service = get_presets_service()
    return [p.model_dump(exclude_none=True) for p in service.list_all()]


@router.post("/presets", response_model=dict)
async def save_preset(preset: StrategyPreset) -> dict[str, Any]:
    """Save a custom strategy preset."""
    service = get_presets_service()
    saved = service.save_user_preset(preset)
    return saved.model_dump(exclude_none=True)


@router.delete("/presets/{preset_id}")
async def delete_preset(preset_id: str) -> dict[str, Any]:
    """Delete a user-saved preset. Built-in presets cannot be deleted."""
    service = get_presets_service()
    success = service.delete_user_preset(preset_id)
    if not success:
        raise HTTPException(
            status_code=404,
            detail=f"Preset {preset_id} not found or is built-in",
        )
    return {"preset_id": preset_id, "deleted": True}


@router.get("/strategies")
async def list_strategies() -> list[dict[str, str]]:
    """List available orchestration strategies."""
    return [
        {"id": "auto", "name": "Auto", "description": "Intelligent engine and parameter selection"},
        {"id": "quality_first", "name": "Quality First", "description": "Maximize audio quality, allow more retries"},
        {"id": "speed_first", "name": "Speed First", "description": "Fastest synthesis, minimal post-processing"},
        {"id": "deterministic", "name": "Deterministic", "description": "Reproducible output with fixed parameters"},
    ]


@router.get("/debug/{job_id}")
async def get_debug_info(job_id: str) -> dict[str, Any]:
    """Return the full execution plan for a completed or failed job."""
    service = get_orchestration_service()
    plan = service.get_debug_info(job_id)
    if plan is None:
        raise HTTPException(status_code=404, detail=f"Job {job_id} not found")
    return plan.model_dump(exclude_none=True)


@router.get("/scheduler-status")
async def get_scheduler_status() -> dict[str, Any]:
    """Return current scheduler queue depth, running count, and GPU availability."""
    scheduler = get_job_scheduler()
    return scheduler.get_status()


@router.get("/gpu-status", response_model=GpuStatusResponse)
async def get_gpu_status() -> GpuStatusResponse:
    """Return current GPU utilization status."""
    try:
        import torch

        if torch.cuda.is_available():
            total = torch.cuda.get_device_properties(0).total_mem / (1024 * 1024)
            used = torch.cuda.memory_allocated(0) / (1024 * 1024)
            return GpuStatusResponse(
                gpu_available=True,
                total_vram_mb=round(total, 1),
                used_vram_mb=round(used, 1),
                free_vram_mb=round(total - used, 1),
                utilization_percent=round(used / total * 100, 1) if total > 0 else 0.0,
                can_schedule=used / total < 0.85 if total > 0 else True,
            )
    except ImportError:
        logger.debug("torch not available for GPU status")
    except Exception:
        logger.debug("GPU status query failed")

    return GpuStatusResponse(gpu_available=False)


@router.websocket("/events/{job_id}")
async def orchestration_events(websocket: WebSocket, job_id: str) -> None:
    """
    WebSocket stream of orchestration events for a specific job.

    Sends OrchestrationEvent JSON messages as they occur.
    Closes when the job completes, fails, or is cancelled.
    """
    await websocket.accept()

    service = get_orchestration_service()
    event_queue: asyncio.Queue[dict[str, Any]] = asyncio.Queue()

    def on_event(event: Any) -> None:
        if event.job_id == job_id:
            try:
                event_queue.put_nowait(event.model_dump(exclude_none=True))
            except Exception:
                logger.debug("Failed to enqueue orchestration event")

    service.emitter.add_listener(on_event)

    try:
        while True:
            try:
                event_data = await asyncio.wait_for(event_queue.get(), timeout=30.0)
                await websocket.send_json(event_data)

                event_type = event_data.get("event_type", "")
                if event_type in ("job_completed", "job_failed"):
                    break
            except asyncio.TimeoutError:
                await websocket.send_json({"type": "ping"})
            except WebSocketDisconnect:
                break
    finally:
        service.emitter.remove_listener(on_event)
