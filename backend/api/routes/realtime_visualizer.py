"""
Real-Time Audio Visualizer Routes

Endpoints for real-time audio visualization and streaming.

WebSocket Protocol (GAP-INT-002):
    Use standardized protocol from backend.api.ws.protocol for new messages.
    See backend/api/ws/protocol.py for specification.
"""

from __future__ import annotations

import asyncio
import logging
import os

from fastapi import APIRouter, Depends, HTTPException, WebSocket, WebSocketDisconnect
from pydantic import BaseModel

from ..middleware.auth_middleware import require_auth_if_enabled

# WebSocket protocol for standardized messaging (GAP-CRIT-002)
from ..ws.protocol import (
    ErrorCode,
    MessageType,
    create_error,
    create_message,
)

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/realtime-visualizer",
    tags=["realtime-visualizer"],
    dependencies=[Depends(require_auth_if_enabled)],
)

# In-memory visualizer sessions (replace with database in production)
_visualizer_sessions: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class VisualizerConfig(BaseModel):
    """Real-time visualizer configuration."""

    session_id: str
    visualization_type: str  # waveform, spectrogram, spectrum, both
    update_rate: float = 30.0  # Updates per second
    fft_size: int = 2048
    window_type: str = "hann"
    show_phase: bool = False
    color_scheme: str = "default"


class VisualizerFrame(BaseModel):
    """A single visualization frame."""

    timestamp: float
    samples: list[float] | None = None  # For waveform
    frequencies: list[float] | None = None  # For spectrum
    magnitudes: list[float] | None = None  # For spectrum/spectrogram
    spectrogram_frame: list[list[float]] | None = None  # For spectrogram


class VisualizerStartRequest(BaseModel):
    """Request to start a visualizer session."""

    visualization_type: str = "both"
    update_rate: float = 30.0
    fft_size: int = 2048
    window_type: str = "hann"
    show_phase: bool = False
    color_scheme: str = "default"


class VisualizerStartResponse(BaseModel):
    """Response from starting a visualizer session."""

    session_id: str
    message: str


@router.post("/start", response_model=VisualizerStartResponse)
async def start_visualizer_session(request: VisualizerStartRequest):
    """Start a new real-time visualizer session."""
    import uuid
    from datetime import datetime

    try:
        session_id = f"viz-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        session = {
            "session_id": session_id,
            "visualization_type": request.visualization_type,
            "update_rate": request.update_rate,
            "fft_size": request.fft_size,
            "window_type": request.window_type,
            "show_phase": request.show_phase,
            "color_scheme": request.color_scheme,
            "status": "active",
            "created": now,
        }

        _visualizer_sessions[session_id] = session
        logger.info(f"Started visualizer session: {session_id}")

        return VisualizerStartResponse(
            session_id=session_id,
            message="Visualizer session started",
        )
    except Exception as e:
        logger.error(f"Failed to start visualizer session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to start session: {e!s}",
        ) from e


@router.get("/{session_id}", response_model=VisualizerConfig)
async def get_visualizer_session(session_id: str):
    """Get visualizer session configuration."""
    if session_id not in _visualizer_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    session = _visualizer_sessions[session_id]
    return VisualizerConfig(
        session_id=session["session_id"],
        visualization_type=session["visualization_type"],
        update_rate=session["update_rate"],
        fft_size=session["fft_size"],
        window_type=session["window_type"],
        show_phase=session["show_phase"],
        color_scheme=session["color_scheme"],
    )


@router.post("/{session_id}/stop")
async def stop_visualizer_session(session_id: str):
    """Stop a visualizer session."""
    if session_id not in _visualizer_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    _visualizer_sessions[session_id]["status"] = "stopped"
    logger.info(f"Stopped visualizer session: {session_id}")
    return {"success": True}


@router.delete("/{session_id}")
async def delete_visualizer_session(session_id: str):
    """Delete a visualizer session."""
    if session_id not in _visualizer_sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    del _visualizer_sessions[session_id]
    logger.info(f"Deleted visualizer session: {session_id}")
    return {"success": True}


@router.websocket("/{session_id}/stream")
async def visualizer_stream(websocket: WebSocket, session_id: str):
    """WebSocket endpoint for real-time visualization data streaming."""
    if session_id not in _visualizer_sessions:
        await websocket.close(code=1008, reason="Session not found")
        return

    await websocket.accept()
    logger.info(f"WebSocket connection opened for visualizer: {session_id}")

    try:
        session = _visualizer_sessions[session_id]
        session_config = VisualizerConfig(**session)

        # Check if audio processing libraries are available
        try:
            import librosa
            import numpy as np
        except ImportError:
            await websocket.send_json(
                create_error(
                    "Audio processing libraries not available",
                    code=ErrorCode.UNAVAILABLE,
                    details={
                        "message": "Install librosa and numpy for real-time visualization. "
                        "Install with: pip install librosa==0.11.0 numpy"
                    },
                )
            )
            await websocket.close(code=1011, reason="Missing audio processing libraries")
            return

        while True:
            # Receive audio data from client
            # Expected format: {"audio": [sample1, sample2, ...], "sample_rate": 44100}
            try:
                data = await websocket.receive_json()

                if "error" in data:
                    logger.warning(f"Client error: {data.get('error')}")
                    continue

                # Process audio data if provided
                if "audio" in data and isinstance(data["audio"], list):
                    audio_samples = np.array(data["audio"], dtype=np.float32)
                    sample_rate = data.get("sample_rate", 44100)

                    # Generate visualization frame based on type
                    frame = {"timestamp": data.get("timestamp", 0.0)}

                    if session_config.visualization_type in ["waveform", "both"]:
                        # Downsample for waveform visualization
                        max_samples = 1024
                        if len(audio_samples) > max_samples:
                            step = len(audio_samples) // max_samples
                            audio_samples_downsampled = audio_samples[::step]
                        else:
                            audio_samples_downsampled = audio_samples
                        frame["samples"] = audio_samples_downsampled.tolist()

                    if session_config.visualization_type in [
                        "spectrum",
                        "spectrogram",
                        "both",
                    ]:
                        # Compute FFT
                        fft = np.fft.rfft(audio_samples, n=session_config.fft_size)
                        magnitude = np.abs(fft)
                        freqs = np.fft.rfftfreq(session_config.fft_size, 1.0 / sample_rate)

                        frame["frequencies"] = freqs.tolist()
                        frame["magnitudes"] = magnitude.tolist()

                    # Send visualization frame back (using standardized protocol)
                    await websocket.send_json(
                        create_message(MessageType.VISUALIZATION_FRAME, frame)
                    )
                else:
                    # Echo back acknowledgment for non-audio messages
                    await websocket.send_json(
                        create_message(
                            MessageType.ACK,
                            {
                                "status": "received",
                                "message": "Send audio data as {'audio': [...], 'sample_rate': 44100}",
                            },
                        )
                    )
            except Exception as msg_error:
                logger.error(f"Error processing audio data in session {session_id}: {msg_error}")
                await websocket.send_json(
                    create_error(
                        "Failed to process audio data",
                        code=ErrorCode.INTERNAL_ERROR,
                        details={"detail": str(msg_error)},
                    )
                )
    except WebSocketDisconnect:
        logger.info(f"WebSocket disconnected for visualizer: {session_id}")
    except Exception as e:
        logger.error(f"WebSocket error for visualizer {session_id}: {e}")
        await websocket.close(code=1011, reason=str(e))
