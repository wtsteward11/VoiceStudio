"""Voice streaming routes - real-time streaming synthesis via WebSocket."""

from __future__ import annotations

import asyncio
import base64
import json
import logging
import os
from typing import Any

import numpy as np
from fastapi import HTTPException, WebSocket, WebSocketDisconnect

from ...ws.protocol import (
    ErrorCode,
    MessageType,
    create_complete,
    create_error,
    create_message,
)
from . import _shared
from ._shared import (
    STREAMING_ENGINES,
    router,
)

logger = logging.getLogger(__name__)


def _engine_supports_streaming(engine_instance: Any) -> bool:
    """Check if an engine supports streaming synthesis."""
    return hasattr(engine_instance, "synthesize_stream") and callable(
        getattr(engine_instance, "synthesize_stream", None)
    )


def _get_engine_sample_rate(engine_instance: Any, engine_id: str) -> int:
    """Get the sample rate for an engine."""
    # Engine-specific sample rates
    SAMPLE_RATES = {
        "openvoice": 24000,
        "xtts": 24000,
        "xtts_v2": 24000,
        "tacotron2": 22050,
        "piper": 22050,
        "bark": 24000,
        "tortoise": 24000,
    }
    return getattr(
        engine_instance,
        "DEFAULT_SAMPLE_RATE",
        SAMPLE_RATES.get(engine_id, 24000),
    )


async def _send_audio_chunk(
    websocket: WebSocket,
    audio_chunk: np.ndarray,
    chunk_index: int,
    sample_rate: int,
) -> None:
    """Send a single audio chunk over WebSocket."""
    # Ensure numpy array
    if not isinstance(audio_chunk, np.ndarray):
        audio_chunk = np.array(audio_chunk, dtype=np.float32)

    # Convert to float32 if needed
    if audio_chunk.dtype != np.float32:
        audio_chunk = audio_chunk.astype(np.float32)

    # Encode as base64
    audio_bytes = audio_chunk.tobytes()
    audio_b64 = base64.b64encode(audio_bytes).decode("utf-8")

    await websocket.send_json(
        {
            "type": "audio_chunk",
            "chunk_index": chunk_index,
            "data": audio_b64,
            "sample_rate": sample_rate,
            "format": "float32",
            "samples": len(audio_chunk),
        }
    )


async def _stream_synthesis_chunks(
    websocket: WebSocket,
    engine_instance: Any,
    engine_id: str,
    text: str,
    profile_audio_path: str | None,
    language: str,
    chunk_size: int,
    overlap: int,
    **kwargs: Any,
) -> None:
    """
    Stream audio chunks from an engine's synthesize_stream method.

    Handles both generator and async generator streaming modes.
    """
    sample_rate = _get_engine_sample_rate(engine_instance, engine_id)
    chunk_index = 0
    total_samples = 0

    # Build streaming kwargs
    stream_kwargs = {
        "text": text,
        "language": language,
        "chunk_size": chunk_size,
        "overlap": overlap,
    }

    # Add speaker_wav for voice cloning engines
    if profile_audio_path:
        stream_kwargs["speaker_wav"] = profile_audio_path

    # Merge additional kwargs
    stream_kwargs.update(kwargs)

    try:
        # Get the streaming generator
        stream_gen = engine_instance.synthesize_stream(**stream_kwargs)

        # Handle async generators
        if hasattr(stream_gen, "__anext__"):
            async for audio_chunk in stream_gen:
                await _send_audio_chunk(websocket, audio_chunk, chunk_index, sample_rate)
                chunk_index += 1
                total_samples += len(audio_chunk)
        else:
            # Handle sync generators
            for audio_chunk in stream_gen:
                await _send_audio_chunk(websocket, audio_chunk, chunk_index, sample_rate)
                chunk_index += 1
                total_samples += len(audio_chunk)
                # Yield control to allow other async tasks
                await asyncio.sleep(0)

        # Send completion message
        duration = total_samples / sample_rate
        await websocket.send_json(
            {
                "type": "complete",
                "total_chunks": chunk_index,
                "total_samples": total_samples,
                "duration": duration,
                "sample_rate": sample_rate,
                "engine": engine_id,
            }
        )

    except Exception as e:
        logger.error(f"Streaming error for {engine_id}: {e}", exc_info=True)
        await websocket.send_json({"type": "error", "message": f"Streaming failed: {e!s}"})


@router.get("/streaming/capabilities")
async def get_streaming_capabilities() -> dict[str, Any]:
    """
    Get streaming synthesis capabilities.

    Returns information about which engines support streaming synthesis,
    WebSocket endpoint URL, and streaming parameters.

    C.2 Enhancement: Streaming capability discovery endpoint.
    """
    available_streaming_engines = []

    if _shared.ENGINE_AVAILABLE and _shared.engine_router:
        for engine_id in STREAMING_ENGINES:
            try:
                engine_instance = _shared.engine_router.get_engine(engine_id)
                if engine_instance is not None and _engine_supports_streaming(engine_instance):
                    available_streaming_engines.append(
                        {
                            "engine_id": engine_id,
                            "supports_streaming": True,
                            "fallback_available": True,
                        }
                    )
                elif engine_instance is not None:
                    # Engine exists but doesn't have streaming method
                    available_streaming_engines.append(
                        {
                            "engine_id": engine_id,
                            "supports_streaming": False,
                            "fallback_available": hasattr(engine_instance, "synthesize"),
                        }
                    )
            except Exception as e:
                # Engine check failed - skip this engine silently as this is a capability probe
                logger.debug(f"Failed to check streaming capability for {engine_id}: {e}")

    return {
        "websocket_endpoint": "/api/voice/synthesize/stream",
        "streaming_engines": list(STREAMING_ENGINES),
        "available_engines": available_streaming_engines,
        "target_latency_ms": 200,
        "supported_formats": ["raw", "wav", "mp3"],
        "chunk_size_samples": 4800,  # 200ms at 24kHz
    }


@router.get("/streaming/capabilities/{engine_id}")
async def get_engine_streaming_capability(engine_id: str) -> dict[str, Any]:
    """
    Check if a specific engine supports streaming.

    C.2 Enhancement: Per-engine streaming capability check.
    """
    if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
        raise HTTPException(status_code=503, detail="Engine system not available")

    engine_instance = _shared.engine_router.get_engine(engine_id)
    if engine_instance is None:
        raise HTTPException(status_code=404, detail=f"Engine '{engine_id}' not found")

    supports_streaming = _engine_supports_streaming(engine_instance)
    supports_batch = hasattr(engine_instance, "synthesize") and callable(
        getattr(engine_instance, "synthesize", None)
    )

    return {
        "engine_id": engine_id,
        "supports_streaming": supports_streaming,
        "supports_batch": supports_batch,
        "fallback_mode": "batch" if not supports_streaming and supports_batch else None,
        "recommended_mode": "streaming" if supports_streaming else "batch",
    }


@router.websocket("/synthesize/stream")
async def synthesize_stream(websocket: WebSocket):
    """
    Stream synthesis in real-time chunks.

    WebSocket endpoint for real-time audio streaming.
    Supports multiple engines with streaming capability:
    - openvoice
    - xtts / xtts_v2
    - tacotron2
    - piper
    - bark
    - tortoise

    Protocol:
    1. Client sends {"type": "synthesize", "engine": "...", "text": "...", ...}
    2. Server sends {"type": "start", ...} when synthesis begins
    3. Server sends {"type": "audio_chunk", "data": "<base64>", ...} for each chunk
    4. Server sends {"type": "complete", "total_chunks": N, ...} when done
    5. Client can send {"type": "stop"} to cancel streaming
    """
    await websocket.accept()

    try:
        if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
            await websocket.send_json(
                create_error("Engine router not available", code=ErrorCode.UNAVAILABLE)
            )
            await websocket.close()
            return

        # Send capabilities on connect (using standardized protocol)
        await websocket.send_json(
            create_message(
                "capabilities",
                {
                    "streaming_engines": list(STREAMING_ENGINES),
                    "message": "Ready for streaming synthesis",
                },
            )
        )

        engine_instance = None

        while True:
            # Receive request
            data = await websocket.receive_text()
            request = json.loads(data)

            request_type = request.get("type")

            if request_type == "synthesize":
                # Initialize synthesis
                engine_id = request.get("engine", "openvoice")
                profile_id = request.get("profile_id")
                text = request.get("text")
                language = request.get("language", "en")
                chunk_size = request.get("chunk_size", 100)
                overlap = request.get("overlap", 20)
                # Additional engine-specific params
                extra_params = request.get("params", {})

                # Validate text
                if not text or not text.strip():
                    await websocket.send_json(
                        create_error("Text is required", code=ErrorCode.VALIDATION_ERROR)
                    )
                    continue

                # Get engine instance
                engine_instance = _shared.engine_router.get_engine(engine_id)
                if engine_instance is None:
                    await websocket.send_json(
                        create_error(
                            f"Engine '{engine_id}' is not available", code=ErrorCode.ENGINE_ERROR
                        )
                    )
                    continue

                # Check if engine supports streaming
                if not _engine_supports_streaming(engine_instance):
                    # Fall back to chunked non-streaming synthesis
                    if hasattr(engine_instance, "synthesize"):
                        await websocket.send_json(
                            create_message(
                                "warning",
                                {
                                    "message": f"Engine '{engine_id}' does not support streaming. Using chunked synthesis.",
                                },
                            )
                        )
                        # Perform regular synthesis and send as single chunk
                        try:
                            speaker_wav_val = None
                            if profile_id:
                                from backend.services.profile_service import (
                                    resolve_reference_audio_path,
                                )

                                resolved = resolve_reference_audio_path(profile_id)
                                if resolved.exists():
                                    speaker_wav_val = str(resolved)
                            result = engine_instance.synthesize(
                                text=text,
                                language=language,
                                speaker_wav=speaker_wav_val,
                                **extra_params,
                            )
                            if isinstance(result, np.ndarray):
                                sample_rate = _get_engine_sample_rate(engine_instance, engine_id)
                                await websocket.send_json(
                                    create_message(
                                        MessageType.START,
                                        {"message": "Synthesis started (non-streaming)"},
                                    )
                                )
                                await _send_audio_chunk(websocket, result, 0, sample_rate)
                                duration = len(result) / sample_rate
                                await websocket.send_json(
                                    create_complete(
                                        result={
                                            "total_chunks": 1,
                                            "duration": duration,
                                            "engine": engine_id,
                                        }
                                    )
                                )
                        except Exception as e:
                            logger.error(f"Synthesis error: {e}", exc_info=True)
                            await websocket.send_json(
                                create_error(
                                    f"Synthesis failed: {e!s}", code=ErrorCode.ENGINE_ERROR
                                )
                            )
                        continue
                    else:
                        await websocket.send_json(
                            create_error(
                                f"Engine '{engine_id}' does not support streaming or synthesis",
                                code=ErrorCode.ENGINE_ERROR,
                            )
                        )
                        continue

                # Get profile audio path if provided
                profile_audio_path = None
                if profile_id:
                    from backend.services.profile_service import resolve_reference_audio_path

                    resolved = resolve_reference_audio_path(profile_id)
                    if not resolved.exists():
                        await websocket.send_json(
                            create_error(
                                f"Profile audio not found: {profile_id}", code=ErrorCode.NOT_FOUND
                            )
                        )
                        continue
                    profile_audio_path = str(resolved)

                # Start streaming
                await websocket.send_json(
                    create_message(
                        MessageType.START,
                        {
                            "message": f"Streaming started with {engine_id}",
                            "engine": engine_id,
                        },
                    )
                )

                # Stream synthesis chunks
                await _stream_synthesis_chunks(
                    websocket=websocket,
                    engine_instance=engine_instance,
                    engine_id=engine_id,
                    text=text,
                    profile_audio_path=profile_audio_path,
                    language=language,
                    chunk_size=chunk_size,
                    overlap=overlap,
                    **extra_params,
                )

            elif request_type == "stop":
                # Stop streaming
                await websocket.send_json(
                    create_message(MessageType.STOP, {"message": "Streaming stopped"})
                )
                break

            elif request_type == "ping":
                # Keepalive ping (using standardized protocol)
                from ...ws.protocol import create_pong

                await websocket.send_json(create_pong())

    except WebSocketDisconnect:
        logger.info("WebSocket disconnected")
    except Exception as e:
        logger.error(f"WebSocket error: {e}", exc_info=True)
        try:
            await websocket.send_json(
                create_error(f"WebSocket error: {e!s}", code=ErrorCode.INTERNAL_ERROR)
            )
        except Exception as send_err:
            logger.debug(f"Could not send error to WebSocket client: {send_err}")
    finally:
        if engine_instance is not None:
            try:
                if hasattr(engine_instance, "cleanup"):
                    engine_instance.cleanup()
                    logger.debug("Streaming engine instance cleaned up")
            except Exception as cleanup_err:
                logger.warning(f"Engine cleanup failed after WebSocket disconnect: {cleanup_err}")
            engine_instance = None
        try:
            await websocket.close()
        except Exception as close_err:
            logger.debug(f"WebSocket close error (client may have disconnected): {close_err}")
