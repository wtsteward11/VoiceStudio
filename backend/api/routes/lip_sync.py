"""
Lip Sync Routes

Phase 10.1: Expose LipSyncService via REST API.
Provides endpoints for automatic lip sync generation with
multiple engines (Wav2Lip, SadTalker, FOMM).
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/lip-sync", tags=["lip-sync"])


# --- Request/Response Models ---


class LipSyncRequest(BaseModel):
    """Request for lip sync generation."""

    video_id: str = Field(..., description="Video file ID")
    audio_id: str = Field(..., description="Audio file ID to sync")
    engine: str = Field("wav2lip", description="Engine: wav2lip, sadtalker, fomm")
    quality: str = Field("balanced", description="Quality: fast, balanced, high")


class LipSyncResponse(BaseModel):
    """Response for lip sync generation."""

    output_video_id: str
    engine: str
    processing_time_seconds: float
    frame_count: int


class LipSyncPreviewRequest(BaseModel):
    """Request for lip sync preview on timeline."""

    video_id: str
    audio_id: str
    start_time: float = Field(0.0, description="Start time in seconds")
    duration: float = Field(5.0, description="Preview duration in seconds")
    engine: str = Field("wav2lip")


class LipSyncPreviewResponse(BaseModel):
    """Response for lip sync preview."""

    preview_video_id: str
    start_time: float
    end_time: float
    frames_processed: int


class PhonemeExtractionRequest(BaseModel):
    """Request for phoneme extraction from audio."""

    audio_id: str


class PhonemeExtractionResponse(BaseModel):
    """Response for phoneme extraction."""

    phonemes: list[dict[str, Any]]
    total_duration: float


class LipSyncEngine(BaseModel):
    """Lip sync engine information."""

    id: str
    name: str
    description: str
    available: bool
    quality_level: str
    speed_rating: str


# --- API Endpoints ---


@router.post("/generate", response_model=LipSyncResponse)
async def generate_lip_sync(request: LipSyncRequest):
    """
    Generate lip-synced video from audio.

    Phase 10.1.1: Automatic lip sync with selected engine.

    Args:
        request: Lip sync parameters

    Returns:
        Output video ID with sync metadata
    """
    try:
        from backend.media.lip_sync.lip_sync_service import (
            LipSyncServiceUnavailable,
            get_lip_sync_service,
        )

        service = get_lip_sync_service()
        result = await service.generate(
            video_id=request.video_id,
            audio_id=request.audio_id,
            engine=request.engine,
            quality=request.quality,
        )

        if not result.get("success", False):
            raise HTTPException(
                status_code=500, detail=result.get("error", "Lip sync generation failed")
            )

        return LipSyncResponse(
            output_video_id=result["output_video_id"],
            engine=request.engine,
            processing_time_seconds=result.get("processing_time_seconds", 0.0),
            frame_count=result.get("frame_count", 0),
        )

    except HTTPException:
        raise
    except LipSyncServiceUnavailable as e:
        logger.warning(f"Lip sync service unavailable: {e.message}")
        raise HTTPException(
            status_code=503,
            detail=e.to_dict(),
        ) from e
    except Exception as e:
        logger.error(f"Lip sync generation failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Lip sync generation failed: {e!s}") from e


@router.post("/preview", response_model=LipSyncPreviewResponse)
async def preview_lip_sync(request: LipSyncPreviewRequest):
    """
    Generate lip sync preview for timeline.

    Phase 10.1.2: Timeline preview.

    Args:
        request: Preview parameters with time range

    Returns:
        Preview video segment
    """
    try:
        from backend.media.lip_sync.lip_sync_service import get_lip_sync_service

        service = get_lip_sync_service()
        result = await service.generate_preview(
            video_id=request.video_id,
            audio_id=request.audio_id,
            start_time=request.start_time,
            duration=request.duration,
            engine=request.engine,
        )

        if not result.get("success", False):
            raise HTTPException(
                status_code=500, detail=result.get("error", "Lip sync preview failed")
            )

        return LipSyncPreviewResponse(
            preview_video_id=result["preview_video_id"],
            start_time=request.start_time,
            end_time=request.start_time + request.duration,
            frames_processed=result.get("frames_processed", 0),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Lip sync preview failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Lip sync preview failed: {e!s}") from e


@router.post("/extract-phonemes", response_model=PhonemeExtractionResponse)
async def extract_phonemes(request: PhonemeExtractionRequest):
    """
    Extract phonemes from audio for manual adjustment.

    Phase 10.1.3: Phoneme extraction for visualization.

    Args:
        request: Audio ID to process

    Returns:
        Phoneme sequence with timing
    """
    try:
        from backend.media.lip_sync.lip_sync_service import get_lip_sync_service

        service = get_lip_sync_service()
        result = await service.extract_phonemes(request.audio_id)

        if not result.get("success", False):
            raise HTTPException(
                status_code=500, detail=result.get("error", "Phoneme extraction failed")
            )

        return PhonemeExtractionResponse(
            phonemes=result.get("phonemes", []),
            total_duration=result.get("total_duration", 0.0),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Phoneme extraction failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Phoneme extraction failed: {e!s}") from e


@router.get("/engines", response_model=list[LipSyncEngine])
async def list_engines():
    """List available lip sync engines."""
    try:
        from backend.media.lip_sync.lip_sync_service import get_lip_sync_service

        service = get_lip_sync_service()
        engines = service.list_engines()

        return [
            LipSyncEngine(
                id=e["id"],
                name=e["name"],
                description=e.get("description", ""),
                available=e.get("available", False),
                quality_level=e.get("quality_level", "medium"),
                speed_rating=e.get("speed_rating", "medium"),
            )
            for e in engines
        ]

    except Exception as e:
        logger.error(f"Failed to list engines: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list engines: {e!s}") from e


@router.get("/engines/{engine_id}/status")
async def get_engine_status(engine_id: str):
    """Get status and capabilities of a specific engine."""
    try:
        from backend.media.lip_sync.lip_sync_service import get_lip_sync_service

        service = get_lip_sync_service()
        status = await service.get_engine_status(engine_id)

        if not status:
            raise HTTPException(status_code=404, detail=f"Engine '{engine_id}' not found")

        return status

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get engine status: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get engine status: {e!s}") from e


@router.get("/quality-settings")
async def list_quality_settings():
    """List available quality settings and their tradeoffs."""
    return {
        "settings": [
            {
                "id": "fast",
                "name": "Fast",
                "description": "Quick processing, suitable for previews",
                "resolution": "256x256",
                "fps": 25,
            },
            {
                "id": "balanced",
                "name": "Balanced",
                "description": "Good quality with reasonable speed",
                "resolution": "512x512",
                "fps": 30,
            },
            {
                "id": "high",
                "name": "High Quality",
                "description": "Best quality, slower processing",
                "resolution": "1024x1024",
                "fps": 60,
            },
        ]
    }
