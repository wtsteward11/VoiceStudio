"""
Voice AI Pipeline API Endpoints (Phase 9.2.5)

REST and WebSocket endpoints for the STT → LLM → TTS pipeline.

WebSocket Protocol (GAP-INT-002):
    Use standardized protocol from backend.api.ws.protocol for new messages.
    See backend/api/ws/protocol.py for specification.
"""

from __future__ import annotations

import asyncio
import json
import logging
import uuid

from fastapi import APIRouter, Depends, HTTPException, WebSocket, WebSocketDisconnect
from pydantic import BaseModel

from ..middleware.auth_middleware import require_auth_if_enabled

# WebSocket protocol for standardized messaging (GAP-CRIT-002)
from ..ws.protocol import (
    ErrorCode,
    MessageType,
    create_error,
    create_message,
    create_pong,
)

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/pipeline",
    tags=["pipeline"],
    dependencies=[Depends(require_auth_if_enabled)],
)

# Active pipeline sessions
_sessions: dict[str, PipelineSession] = {}
_state_lock = asyncio.Lock()


class PipelineRequest(BaseModel):
    """Request for pipeline processing."""

    text: str
    mode: str = "batch"  # "streaming" or "batch"
    stt_engine: str = "whisper"
    llm_provider: str = "ollama"
    tts_engine: str = "xtts_v2"
    language: str = "en"
    llm_model: str | None = None
    tts_voice: str | None = None
    synthesize: bool = True


class PipelineResponse(BaseModel):
    """Response from pipeline processing."""

    session_id: str
    response_text: str
    audio_available: bool = False
    audio_id: str | None = None
    metrics: dict | None = None


class PipelineSession:
    """Tracks an active pipeline session."""

    def __init__(self, session_id: str, orchestrator):
        self.session_id = session_id
        self.orchestrator = orchestrator
        self.turns: list[dict] = []


@router.post("/process", response_model=PipelineResponse)
async def process_pipeline(request: PipelineRequest):
    """
    Process text through the Voice AI pipeline.

    Supports batch mode (complete response) and returns
    the LLM response with optional TTS audio.
    """
    from backend.pipeline.facade import PipelineConfig, PipelineMode, PipelineOrchestrator

    session_id = f"sess-{uuid.uuid4().hex[:8]}"

    config = PipelineConfig(
        mode=PipelineMode.BATCH if request.mode == "batch" else PipelineMode.STREAMING,
        stt_engine=request.stt_engine,
        llm_provider=request.llm_provider,
        tts_engine=request.tts_engine,
        language=request.language,
        llm_model=request.llm_model,
        tts_voice=request.tts_voice,
    )

    orchestrator = PipelineOrchestrator(config)
    initialized = await orchestrator.initialize()

    if not initialized:
        raise HTTPException(
            status_code=503,
            detail="Pipeline initialization failed. Check that LLM provider is available.",
        )

    try:
        result = await orchestrator.process_text(request.text)

        # Store audio if generated (real storage + provenance per trust audit)
        audio_id = None
        audio_bytes = result.get("audio")
        if audio_bytes and isinstance(audio_bytes, bytes) and request.synthesize:
            try:
                import tempfile

                from backend.services.audio_artifacts import create_audio_artifact_from_file

                with tempfile.NamedTemporaryFile(
                    suffix=".wav", delete=False
                ) as tmp:
                    tmp.write(audio_bytes)
                    output_path = tmp.name
                try:
                    audio_id, _, _ = create_audio_artifact_from_file(
                        output_path,
                        created_by="pipeline",
                        audio_id=f"pipe-{uuid.uuid4().hex[:8]}",
                        delete_source=True,
                    )
                finally:
                    import os

                    if os.path.exists(output_path):
                        try:
                            os.unlink(output_path)
                        # ALLOWED: bare except - best effort, failure acceptable
                        except OSError:
                            pass
            except Exception as exc:
                logger.warning("Pipeline audio storage failed: %s", exc)
                audio_id = None

        return PipelineResponse(
            session_id=session_id,
            response_text=result.get("response", ""),
            audio_available=audio_id is not None,
            audio_id=audio_id,
            metrics=result.get("metrics"),
        )

    except Exception as exc:
        logger.error(f"Pipeline processing failed: {exc}")
        raise HTTPException(
            status_code=500,
            detail=f"Pipeline processing failed: {exc!s}",
        ) from exc
    finally:
        await orchestrator.cleanup()


@router.websocket("/stream")
async def pipeline_stream(websocket: WebSocket):
    """
    WebSocket endpoint for streaming pipeline interaction.

    Client sends text messages, server streams back tokens and audio chunks.

    Protocol:
        Client → {"type": "text", "content": "Hello"}
        Server → {"type": "ttft", "time_to_first_token_ms": 123}
        Server → {"type": "token", "content": "Hi"}
        Server → {"type": "token", "content": " there"}
        Server → {"type": "complete", "content": "Hi there!"}
    """
    await websocket.accept()
    session_id = f"ws-{uuid.uuid4().hex[:8]}"
    logger.info(f"Pipeline WebSocket connected: {session_id}")

    from backend.pipeline.facade import PipelineConfig, PipelineOrchestrator

    orchestrator = PipelineOrchestrator(PipelineConfig())
    await orchestrator.initialize()

    try:
        while True:
            data = await websocket.receive_text()

            try:
                msg = json.loads(data)
            except json.JSONDecodeError:
                msg = {"type": "text", "content": data}

            if msg.get("type") == "text":
                text = msg.get("content", "")
                if text.strip():
                    async for chunk in orchestrator.stream_text(text):
                        # Wrap orchestrator chunks in standardized protocol
                        await websocket.send_json(create_message(MessageType.DATA, chunk))

            elif msg.get("type") == "reset":
                orchestrator.reset()
                await websocket.send_json(create_message(MessageType.ACK, {"action": "reset"}))

            elif msg.get("type") == "ping":
                await websocket.send_json(create_pong())

    except WebSocketDisconnect:
        logger.info(f"Pipeline WebSocket disconnected: {session_id}")
    except Exception as exc:
        logger.error(f"Pipeline WebSocket error: {exc}")
        try:
            await websocket.send_json(create_error(str(exc), code=ErrorCode.INTERNAL_ERROR))
        except Exception as send_err:
            logger.debug(f"Failed to send error response to WebSocket client: {send_err}")
    finally:
        await orchestrator.cleanup()


@router.get("/providers")
async def list_pipeline_providers():
    """List available providers for each pipeline stage."""
    from backend.ml.models.llm_provider_service import get_llm_provider_service

    provider_service = get_llm_provider_service()
    llm_providers = [
        {"name": p.name, "local": p.local, "available": p.available}
        for p in provider_service.get_available_providers()
    ]

    return {
        "stt": {
            "available": ["whisper", "whisper_cpp"],
            "default": "whisper",
        },
        "llm": {
            "available": llm_providers,
            "default": "ollama",
        },
        "tts": {
            "available": ["xtts_v2", "piper", "openai_tts"],
            "default": "xtts_v2",
        },
    }


@router.get("/metrics")
async def get_pipeline_metrics():
    """Get pipeline performance metrics."""
    return {
        "active_sessions": len(_sessions),
        "total_sessions": 0,
        "avg_latency_ms": 0.0,
        "message": "Pipeline metrics endpoint active",
    }
