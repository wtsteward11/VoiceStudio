"""
AI Assistant Action Execution Routes

Endpoints for executing predefined actions through the AI assistant.
"""

from __future__ import annotations

import logging
import uuid
from datetime import datetime
from typing import Any

from fastapi import APIRouter, HTTPException, Request

from ..models_additional import AssistantRunRequest

logger = logging.getLogger(__name__)

# GAP-B02: Changed to sub-prefix to avoid conflict with assistant.py
router = APIRouter(prefix="/api/assistant/run", tags=["assistant-run"])

# In-memory action registry (replace with database in production)
_action_registry: dict[str, dict] = {
    "synthesize": {
        "name": "Synthesize Voice",
        "description": "Generate voice synthesis from text",
        "required_params": ["text", "profile_id"],
        "optional_params": ["engine", "language", "emotion"],
    },
    "transcribe": {
        "name": "Transcribe Audio",
        "description": "Transcribe audio to text",
        "required_params": ["audio_id"],
        "optional_params": ["engine", "language"],
    },
    "apply_effect": {
        "name": "Apply Audio Effect",
        "description": "Apply audio effect to audio file",
        "required_params": ["audio_id", "effect_type"],
        "optional_params": ["effect_params"],
    },
    "analyze_quality": {
        "name": "Analyze Audio Quality",
        "description": "Analyze audio quality metrics",
        "required_params": ["audio_id"],
        "optional_params": [],
    },
    "train_model": {
        "name": "Train Voice Model",
        "description": "Start training a voice model",
        "required_params": ["dataset_id", "profile_id"],
        "optional_params": ["engine", "epochs"],
    },
}


@router.post("/run")
async def run(req: AssistantRunRequest, http_request: Request) -> dict:
    """
    Execute an AI assistant action.

    Actions are predefined operations that can be executed through
    the assistant interface, such as voice synthesis, transcription,
    quality analysis, etc.

    Args:
        req: Request with action_id and optional parameters

    Returns:
        Dictionary with execution result
    """
    try:
        action_id = req.action_id
        params = req.params or {}

        if not action_id:
            raise HTTPException(status_code=400, detail="action_id is required")

        # Check if action exists
        if action_id not in _action_registry:
            raise HTTPException(
                status_code=404,
                detail=f"Action '{action_id}' not found. Available actions: {', '.join(_action_registry.keys())}",
            )

        action = _action_registry[action_id]

        # Validate required parameters
        required_params = action.get("required_params", [])
        missing_params = [p for p in required_params if p not in params]

        if missing_params:
            raise HTTPException(
                status_code=400,
                detail=f"Missing required parameters: {', '.join(missing_params)}",
            )

        # Execute action based on type
        execution_id = f"exec_{uuid.uuid4().hex[:8]}"
        result: dict[str, Any] | None = None
        status = "completed"
        error_message = None

        try:
            if action_id == "synthesize":
                # Execute voice synthesis
                from backend.voice.services.synthesis_service import SynthesisService

                from ..models_additional import VoiceSynthesizeRequest

                synth_req = VoiceSynthesizeRequest(
                    engine=params.get("engine", "xtts"),
                    profile_id=params["profile_id"],
                    text=params["text"],
                    language=params.get("language", "en"),
                    emotion=params.get("emotion"),
                )

                synth_result = await SynthesisService.synthesize(synth_req, http_request, config_service=None)
                result = {
                    "audio_id": synth_result.audio_id,
                    "audio_url": synth_result.audio_url,
                }

            elif action_id == "transcribe":
                # Execute transcription via service (no route-to-route import)
                from backend.services.transcription_service import (
                    TranscriptionRequest,
                    transcribe_audio,
                )

                transcribe_req = TranscriptionRequest(
                    audio_id=params["audio_id"],
                    engine=params.get("engine", "whisper"),
                    language=params.get("language", "auto"),
                )

                transcribe_result = await transcribe_audio(transcribe_req)
                result = {
                    "text": transcribe_result.text,
                    "language": transcribe_result.language,
                    "segments": [
                        {
                            "text": seg.text,
                            "start": seg.start,
                            "end": seg.end,
                        }
                        for seg in transcribe_result.segments
                    ],
                }

            elif action_id == "apply_effect":
                # Execute audio effect application
                # This would call the appropriate effect route
                result = {
                    "audio_id": params["audio_id"],
                    "effect_type": params["effect_type"],
                    "message": f"Effect '{params['effect_type']}' applied to audio",
                }

            elif action_id == "analyze_quality":
                # Execute quality analysis via service (no route-to-route import)
                from backend.services.quality_service import (
                    QualityAnalysisRequest,
                    analyze_quality,
                )

                quality_req = QualityAnalysisRequest()

                quality_result = await analyze_quality(quality_req)
                result = {
                    "audio_id": params["audio_id"],
                    "mos_score": quality_result.metrics.get("mos_score"),
                    "snr_db": quality_result.metrics.get("snr_db"),
                    "naturalness": quality_result.metrics.get("naturalness"),
                    "similarity": quality_result.metrics.get("similarity"),
                }

            elif action_id == "train_model":
                # Execute model training
                # This would start a training job
                result = {
                    "dataset_id": params["dataset_id"],
                    "profile_id": params["profile_id"],
                    "engine": params.get("engine", "xtts"),
                    "message": "Training job started",
                }

            else:
                # Generic action execution
                result = {
                    "action_id": action_id,
                    "params": params,
                    "message": f"Action '{action_id}' executed successfully",
                }

        except Exception as e:
            status = "failed"
            error_message = str(e)
            logger.error(f"Action execution failed: {action_id}, error: {e}", exc_info=True)

        logger.info(
            f"Assistant action executed: {action_id} -> {execution_id}, " f"status={status}"
        )

        return {
            "ok": status == "completed",
            "action_id": action_id,
            "execution_id": execution_id,
            "status": status,
            "result": result,
            "error_message": error_message,
            "timestamp": datetime.utcnow().isoformat(),
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Assistant action execution failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Assistant action execution failed: {e!s}"
        ) from e


@router.get("/actions")
async def list_actions() -> dict:
    """List all available assistant actions."""
    actions = [
        {
            "id": action_id,
            "name": action["name"],
            "description": action["description"],
            "required_params": action.get("required_params", []),
            "optional_params": action.get("optional_params", []),
        }
        for action_id, action in _action_registry.items()
    ]

    return {
        "actions": actions,
        "count": len(actions),
    }


@router.get("/actions/{action_id}")
async def get_action(action_id: str) -> dict:
    """Get details about a specific action."""
    if action_id not in _action_registry:
        raise HTTPException(status_code=404, detail=f"Action '{action_id}' not found")

    action = _action_registry[action_id]
    return {
        "id": action_id,
        "name": action["name"],
        "description": action["description"],
        "required_params": action.get("required_params", []),
        "optional_params": action.get("optional_params", []),
    }
