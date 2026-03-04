"""
Quality Pipeline Management Routes (IDEA 58).

Provides endpoints for engine-specific quality enhancement pipelines.
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..utils.engine_quality_pipelines import (
    apply_engine_pipeline,
    compare_enhancement,
    get_engine_pipeline,
    get_pipeline_description,
    list_engine_presets,
    preview_engine_pipeline,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/quality/pipelines", tags=["quality-pipelines"])

# In-memory storage for custom pipelines (replace with database in production)
_custom_pipelines: dict[str, dict[str, Any]] = {}
_state_lock = asyncio.Lock()


class PipelineConfiguration(BaseModel):
    """Pipeline configuration request/response (IDEA 58)."""

    engine_id: str
    preset_name: str | None = "default"
    steps: list[str] = []
    settings: dict[str, Any] = {}
    description: str | None = None


class PipelinePreviewRequest(BaseModel):
    """Request to preview a pipeline (IDEA 58)."""

    audio_id: str
    engine_id: str
    pipeline_config: PipelineConfiguration | None = None
    preset_name: str | None = "default"


class PipelineComparisonResponse(BaseModel):
    """Response from pipeline comparison (IDEA 58)."""

    before_metrics: dict[str, Any] = {}
    after_metrics: dict[str, Any] = {}
    improvements: dict[str, Any] = {}


class PipelinePreviewResponse(BaseModel):
    """Response from pipeline preview (IDEA 58)."""

    enhanced_audio_id: str
    before_metrics: dict[str, Any] = {}
    after_metrics: dict[str, Any] = {}
    comparison: PipelineComparisonResponse | None = None


@router.get("/engines/{engine_id}/presets", response_model=list[str])
async def list_presets(engine_id: str):
    """
    List available presets for an engine (IDEA 58).
    """
    try:
        presets = list_engine_presets(engine_id)
        return presets
    except Exception as e:
        logger.error(f"Error listing presets for {engine_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list presets: {e!s}")


@router.get("/engines/{engine_id}/presets/{preset_name}", response_model=PipelineConfiguration)
async def get_preset(engine_id: str, preset_name: str = "default"):
    """
    Get pipeline configuration for an engine preset (IDEA 58).
    """
    try:
        pipeline = get_engine_pipeline(engine_id, preset_name)
        description = get_pipeline_description(engine_id, preset_name)

        return PipelineConfiguration(
            engine_id=engine_id,
            preset_name=preset_name,
            steps=pipeline.get("steps", []),
            settings=pipeline,
            description=description,
        )
    except Exception as e:
        logger.error(
            f"Error getting preset {preset_name} for {engine_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to get preset: {e!s}")


@router.post("/engines/{engine_id}/apply")
async def apply_pipeline(
    engine_id: str,
    audio_id: str = Query(..., description="Audio ID to enhance"),
    preset_name: str = Query("default", description="Preset name"),
    pipeline_config: PipelineConfiguration | None = None,
    reference_audio_id: str | None = Query(None, description="Optional reference audio ID"),
):
    """
    Apply quality enhancement pipeline to audio (IDEA 58).
    """
    try:
        # Get audio file from storage
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio not found: {audio_id}")

        # Load audio
        try:
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            audio = audio.astype(np.float32)
        except ImportError:
            raise HTTPException(status_code=500, detail="soundfile not available")

        # Get reference audio if provided
        reference_audio_path = None
        if reference_audio_id:
            reference_audio_path = AudioRegistry.get_path(reference_audio_id)
            if not reference_audio_path or not os.path.exists(reference_audio_path):
                logger.warning(f"Reference audio not found: {reference_audio_id}")

        # Get pipeline configuration
        config = None
        if pipeline_config:
            config = pipeline_config.settings or pipeline_config.dict()
        else:
            config = get_engine_pipeline(engine_id, preset_name)

        # Apply pipeline
        audio_array = np.array(audio)

        enhanced_audio, quality_metrics = apply_engine_pipeline(
            audio=audio_array,
            sample_rate=sample_rate,
            engine_id=engine_id,
            pipeline_config=config,
            preset_name=preset_name if not pipeline_config else None,
            reference_audio=reference_audio_path,
        )

        # Save and register via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        enhanced_audio_id, _, _ = create_audio_artifact_from_wav_array(
            enhanced_audio,
            sample_rate,
            created_by="quality_pipelines",
            audio_id=f"{audio_id}_enhanced_{uuid.uuid4().hex[:8]}",
        )

        return {"audio_id": enhanced_audio_id, "quality_metrics": quality_metrics}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error applying pipeline: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to apply pipeline: {e!s}")


@router.post("/engines/{engine_id}/preview", response_model=PipelinePreviewResponse)
async def preview_pipeline(engine_id: str, request: PipelinePreviewRequest):
    """
    Preview quality enhancement pipeline effects (IDEA 58).
    Returns enhanced audio and before/after metrics.
    """
    try:
        # Get audio file
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(request.audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio not found: {request.audio_id}")

        # Load audio
        try:
            import numpy as np
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            audio_array = np.array(audio, dtype=np.float32)
        except ImportError:
            raise HTTPException(status_code=500, detail="soundfile not available")

        # Get pipeline configuration
        config = None
        if request.pipeline_config:
            config = request.pipeline_config.settings or request.pipeline_config.dict()
        else:
            config = get_engine_pipeline(engine_id, request.preset_name or "default")

        # Preview pipeline
        enhanced_audio, before_metrics, after_metrics = preview_engine_pipeline(
            audio=audio_array,
            sample_rate=sample_rate,
            engine_id=engine_id,
            pipeline_config=config,
            preset_name=request.preset_name or "default",
            reference_audio=None,
        )

        # Save and register via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        enhanced_audio_id, _, _ = create_audio_artifact_from_wav_array(
            enhanced_audio,
            sample_rate,
            created_by="quality_pipelines_preview",
            audio_id=f"{request.audio_id}_preview_{uuid.uuid4().hex[:8]}",
        )

        # Create comparison
        comparison_data: PipelineComparisonResponse | None = None
        if before_metrics and after_metrics:
            improvements = {}
            for key in ["mos_score", "similarity", "naturalness", "snr_db"]:
                before_val = before_metrics.get(key)
                after_val = after_metrics.get(key)

                if before_val is not None and after_val is not None:
                    improvement = after_val - before_val
                    improvement_percent = (
                        (improvement / before_val) * 100 if before_val > 0 else 0.0
                    )

                    improvements[key] = {
                        "before": before_val,
                        "after": after_val,
                        "improvement": improvement,
                        "improvement_percent": improvement_percent,
                    }

            comparison_data = PipelineComparisonResponse(
                before_metrics=before_metrics,
                after_metrics=after_metrics,
                improvements=improvements,
            )

        return PipelinePreviewResponse(
            enhanced_audio_id=enhanced_audio_id,
            before_metrics=before_metrics or {},
            after_metrics=after_metrics or {},
            comparison=comparison_data,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error previewing pipeline: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to preview pipeline: {e!s}")


@router.post("/engines/{engine_id}/compare", response_model=PipelineComparisonResponse)
async def compare_pipeline(
    engine_id: str,
    audio_id: str = Query(..., description="Audio ID to compare"),
    preset_name: str = Query("default", description="Preset name"),
    pipeline_config: PipelineConfiguration | None = None,
    reference_audio_id: str | None = Query(None, description="Optional reference audio ID"),
):
    """
    Compare audio before and after enhancement (IDEA 58).
    Returns improvement metrics.
    """
    try:
        # Get audio file
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio not found: {audio_id}")

        # Load audio
        try:
            import numpy as np
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            audio_array = np.array(audio, dtype=np.float32)
        except ImportError:
            raise HTTPException(status_code=500, detail="soundfile not available")

        # Get reference audio if provided
        reference_audio_path = None
        if reference_audio_id:
            reference_audio_path = AudioRegistry.get_path(reference_audio_id)

        # Get pipeline configuration
        config = None
        if pipeline_config:
            config = pipeline_config.settings or pipeline_config.dict()
        else:
            config = get_engine_pipeline(engine_id, preset_name)

        # Compare enhancement
        comparison = compare_enhancement(
            audio=audio_array,
            sample_rate=sample_rate,
            engine_id=engine_id,
            pipeline_config=config,
            preset_name=preset_name if not pipeline_config else None,
            reference_audio=reference_audio_path,
        )

        return PipelineComparisonResponse(
            before_metrics=comparison.get("before_metrics", {}),
            after_metrics=comparison.get("after_metrics", {}),
            improvements=comparison.get("improvements", {}),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error comparing pipeline: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to compare pipeline: {e!s}")
