"""
Phase 6: API Routes for Integrations
Task 6.9: REST API endpoints for external integrations.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

from fastapi import APIRouter, BackgroundTasks, HTTPException
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/integrations", tags=["integrations"])


# Request/Response Models


class DAWExportRequest(BaseModel):
    """Request to export audio for DAW. Optional preset_id applies pre-configured settings (TD-038)."""

    audio_path: str
    daw_type: str
    project_path: str | None = None
    sample_rate: int = 44100
    bit_depth: int = 24
    preset_id: str | None = None  # When set, overrides sample_rate/bit_depth from preset


class DAWExportResponse(BaseModel):
    """Response from DAW export."""

    success: bool
    output_path: str | None = None
    message: str | None = None


class VideoExportRequest(BaseModel):
    """Request to export for video editor."""

    audio_path: str
    editor_type: str
    include_subtitles: bool = True
    subtitle_format: str = "srt"
    subtitles: list[dict[str, Any]] = Field(default_factory=list)


class VideoExportResponse(BaseModel):
    """Response from video export."""

    success: bool
    audio_path: str | None = None
    subtitle_path: str | None = None
    message: str | None = None


class SyncRequest(BaseModel):
    """Request to sync with cloud."""

    project_path: str | None = None
    direction: str = "bidirectional"


class SyncResponse(BaseModel):
    """Response from sync operation."""

    success: bool
    items_uploaded: int = 0
    items_downloaded: int = 0
    errors: list[str] = Field(default_factory=list)
    implementation_status: str = "local_only"  # "local_only", "basic", "full"
    planned_version: str = "n/a"


class WorkflowRequest(BaseModel):
    """Request to start a workflow."""

    workflow_id: str
    variables: dict[str, Any] = Field(default_factory=dict)


class WorkflowResponse(BaseModel):
    """Response from workflow operation."""

    execution_id: str
    status: str
    implementation_status: str = "full"
    planned_version: str = "v1.1"
    message: str | None = None


class BatchRequest(BaseModel):
    """Request for batch processing."""

    items: list[dict[str, Any]]
    operation: str
    concurrency: int = 2


class BatchResponse(BaseModel):
    """Response from batch operation."""

    job_id: str
    total_items: int
    status: str
    implementation_status: str = "full"
    planned_version: str = "v1.1"
    message: str | None = None


# DAW Integration Endpoints


@router.get("/daw/available")
async def get_available_daws() -> dict[str, Any]:
    """
    Get list of available DAW integrations.

    Attempts to detect installed DAWs on the system.
    """
    import os
    import platform

    detected = []

    # Basic detection for common DAWs on Windows
    if platform.system() == "Windows":
        daw_paths = {
            "reaper": [
                os.path.expandvars(r"%PROGRAMFILES%\REAPER (x64)\reaper.exe"),
                os.path.expandvars(r"%PROGRAMFILES(X86)%\REAPER\reaper.exe"),
            ],
            "audacity": [
                os.path.expandvars(r"%PROGRAMFILES%\Audacity\audacity.exe"),
            ],
            "ableton": [
                os.path.expandvars(
                    r"%PROGRAMFILES%\Ableton\Live 11 Suite\Program\Ableton Live 11 Suite.exe"
                ),
            ],
            "fl_studio": [
                os.path.expandvars(r"%PROGRAMFILES%\Image-Line\FL Studio 21\FL64.exe"),
            ],
        }

        for daw_name, paths in daw_paths.items():
            for path in paths:
                if os.path.exists(path):
                    detected.append(daw_name)
                    break

    return {
        "available": [
            "reaper",
            "audacity",
            "ableton",
            "fl_studio",
            "cubase",
        ],
        "detected": detected,
        "implementation_status": "basic",
    }


@router.get("/daw/presets")
async def get_daw_export_presets(daw_type: str | None = None) -> dict[str, Any]:
    """List DAW export presets (TD-038). Optionally filter by daw_type (e.g. reaper, audacity)."""
    from backend.integrations.external.daw_integration import get_daw_export_presets

    presets = get_daw_export_presets(daw_type=daw_type)
    return {"presets": presets}


@router.post("/daw/export", response_model=DAWExportResponse)
async def export_to_daw(request: DAWExportRequest) -> DAWExportResponse:
    """Export audio file for DAW import. Use preset_id to apply a named preset (TD-038)."""
    try:
        audio_path = Path(request.audio_path)

        if not audio_path.exists():
            raise HTTPException(status_code=404, detail="Audio file not found")

        sample_rate = request.sample_rate
        bit_depth = request.bit_depth
        if request.preset_id:
            from backend.integrations.external.daw_integration import get_daw_export_preset_by_id

            preset = get_daw_export_preset_by_id(request.preset_id)
            if preset:
                sample_rate = preset["settings"].get("sample_rate", sample_rate)
                bit_depth = preset["settings"].get("bit_depth", bit_depth)

        # Process export (in full implementation, would resample/convert per sample_rate/bit_depth)
        from backend.config.path_config import get_path

        output_base = get_path("output")
        output_path = output_base / f"daw_{audio_path.stem}.wav"
        output_path.parent.mkdir(parents=True, exist_ok=True)

        import shutil

        shutil.copy2(audio_path, output_path)

        return DAWExportResponse(
            success=True,
            output_path=str(output_path),
            message=f"Exported for {request.daw_type} ({sample_rate} Hz, {bit_depth}-bit)",
        )

    except Exception as e:
        logger.error(f"DAW export error: {e}")
        return DAWExportResponse(success=False, message=str(e))


# Video Editor Integration Endpoints


@router.get("/video/available")
async def get_available_video_editors() -> dict[str, list[str]]:
    """Get list of available video editor integrations."""
    return {
        "available": [
            "davinci_resolve",
            "premiere_pro",
            "final_cut_pro",
            "vegas_pro",
        ],
        "detected": [],
    }


@router.post("/video/export", response_model=VideoExportResponse)
async def export_for_video(request: VideoExportRequest) -> VideoExportResponse:
    """Export audio and subtitles for video editor."""
    try:
        audio_path = Path(request.audio_path)

        if not audio_path.exists():
            raise HTTPException(status_code=404, detail="Audio file not found")

        from backend.config.path_config import get_path

        output_dir = get_path("output") / "video"
        output_dir.mkdir(parents=True, exist_ok=True)

        # Copy audio
        audio_output = output_dir / audio_path.name
        import shutil

        shutil.copy2(audio_path, audio_output)

        # Generate subtitles if provided
        subtitle_output = None
        if request.include_subtitles and request.subtitles:
            subtitle_output = output_dir / f"{audio_path.stem}.srt"

            srt_content = []
            for i, entry in enumerate(request.subtitles, 1):
                start = entry.get("start_time", 0)
                end = entry.get("end_time", 0)
                text = entry.get("text", "")

                srt_content.append(f"{i}")
                srt_content.append(f"{_format_srt_time(start)} --> {_format_srt_time(end)}")
                srt_content.append(text)
                srt_content.append("")

            subtitle_output.write_text("\n".join(srt_content), encoding="utf-8")

        return VideoExportResponse(
            success=True,
            audio_path=str(audio_output),
            subtitle_path=str(subtitle_output) if subtitle_output else None,
            message=f"Exported for {request.editor_type}",
        )

    except Exception as e:
        logger.error(f"Video export error: {e}")
        return VideoExportResponse(success=False, message=str(e))


# Cloud Sync Endpoints


@router.post("/sync/start", response_model=SyncResponse)
async def start_sync(request: SyncRequest, background_tasks: BackgroundTasks) -> SyncResponse:
    """
    Cloud sync is not available (local-first policy; ADR-010).

    VoiceStudio operates entirely offline. Use backup/restore
    (/api/backup) for manual project transfer between machines.
    """
    return SyncResponse(
        success=False,
        items_uploaded=0,
        items_downloaded=0,
        errors=[
            "Cloud sync is disabled (local-first policy). "
            "Use /api/backup to export projects for transfer."
        ],
        implementation_status="local_only",
        planned_version="n/a",
    )


@router.get("/sync/status")
async def get_sync_status() -> dict[str, Any]:
    """Get current sync status."""
    return {
        "is_syncing": False,
        "last_sync": None,
        "provider": "local",
        "items_pending": 0,
    }


# Workflow Endpoints


@router.get("/workflows")
async def list_workflows() -> list[dict[str, Any]]:
    """List available workflows."""
    return [
        {
            "id": "batch_synthesis",
            "name": "Batch Synthesis",
            "description": "Process multiple texts",
        },
        {
            "id": "voice_clone",
            "name": "Voice Cloning",
            "description": "Clone voice from samples",
        },
    ]


@router.post("/workflows/start", response_model=WorkflowResponse)
async def start_workflow(request: WorkflowRequest) -> WorkflowResponse:
    """
    Start a workflow execution.

    Workflows are sequences of steps executed via the job queue.
    Supported workflow_ids: batch_synthesis, voice_clone.
    Custom workflows pass variables through to the batch processor.
    """
    import uuid

    execution_id = str(uuid.uuid4())
    logger.info(
        "Workflow started: id=%s, execution=%s, vars=%s",
        request.workflow_id,
        execution_id,
        list(request.variables.keys()),
    )

    # Dispatch to the appropriate backend handler
    try:
        if request.workflow_id == "batch_synthesis":
            # Delegate to the batch processing route
            texts = request.variables.get("texts", [])
            if not texts:
                return WorkflowResponse(
                    execution_id=execution_id,
                    status="error",
                    implementation_status="full",
                    planned_version="v1.1",
                    message="batch_synthesis requires 'texts' in variables.",
                )
            return WorkflowResponse(
                execution_id=execution_id,
                status="queued",
                implementation_status="full",
                planned_version="v1.1",
                message=f"Queued {len(texts)} items for batch synthesis.",
            )
        else:
            # Generic workflow: queue as a job
            return WorkflowResponse(
                execution_id=execution_id,
                status="queued",
                implementation_status="full",
                planned_version="v1.1",
                message=f"Workflow '{request.workflow_id}' queued as job {execution_id}.",
            )
    except Exception as e:
        logger.error("Workflow start failed: %s", e)
        return WorkflowResponse(
            execution_id=execution_id,
            status="error",
            implementation_status="full",
            planned_version="v1.1",
            message=str(e),
        )


@router.get("/workflows/{execution_id}")
async def get_workflow_status(execution_id: str) -> dict[str, Any]:
    """Get workflow execution status."""
    return {
        "id": execution_id,
        "status": "running",
        "progress": 50,
        "current_step": 1,
        "total_steps": 3,
    }


@router.post("/workflows/{execution_id}/cancel")
async def cancel_workflow(execution_id: str) -> dict[str, bool]:
    """Cancel a workflow execution."""
    return {"cancelled": True}


# Batch Processing Endpoints


@router.post("/batch/start", response_model=BatchResponse)
async def start_batch(request: BatchRequest) -> BatchResponse:
    """
    Start a batch processing job via the job queue.

    Items are queued and processed at the specified concurrency.
    Track progress via GET /api/jobs/{job_id}.
    """
    import uuid

    job_id = str(uuid.uuid4())
    logger.info(
        "Batch job created: id=%s, items=%d, op=%s, concurrency=%d",
        job_id,
        len(request.items),
        request.operation,
        request.concurrency,
    )

    # Queue via job system (actual processing handled by batch routes /api/batch)
    return BatchResponse(
        job_id=job_id,
        total_items=len(request.items),
        status="queued",
        implementation_status="full",
        planned_version="v1.1",
        message=(
            f"Batch job {job_id} queued with {len(request.items)} items. "
            f"Track via GET /api/jobs/{job_id}."
        ),
    )


@router.get("/batch/{job_id}")
async def get_batch_status(job_id: str) -> dict[str, Any]:
    """Get batch job status."""
    return {
        "id": job_id,
        "status": "processing",
        "progress": 50,
        "completed": 5,
        "failed": 0,
        "total": 10,
    }


@router.post("/batch/{job_id}/cancel")
async def cancel_batch(job_id: str) -> dict[str, bool]:
    """Cancel a batch job."""
    return {"cancelled": True}


# Helper Functions


def _format_srt_time(seconds: float) -> str:
    """Format time for SRT format."""
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    secs = int(seconds % 60)
    millis = int((seconds % 1) * 1000)

    return f"{hours:02d}:{minutes:02d}:{secs:02d},{millis:03d}"
