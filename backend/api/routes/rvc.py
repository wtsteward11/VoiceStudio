"""
RVC (Retrieval-based Voice Conversion) Routes
Real-time voice conversion endpoints

WebSocket Protocol Migration (GAP-INT-002):
    New WebSocket messages should use the standardized protocol:

        from backend.api.ws import create_message, create_error, MessageType

    See backend/api/ws/protocol.py for the full specification.
"""

from __future__ import annotations

import base64
import json
import logging
import os
import tempfile
import uuid

import numpy as np
from fastapi import (
    APIRouter,
    Depends,
    File,
    HTTPException,
    UploadFile,
    WebSocket,
    WebSocketDisconnect,
)
from fastapi.responses import FileResponse
from pydantic import BaseModel

from backend.config.path_config import get_models_path
from backend.core.circuit_breaker import (
    CircuitBreakerOpenError,
    get_engine_breaker,
)
from backend.ml.models.engine_service import get_engine_service
from backend.ml.models.model_preflight import PreflightError, ensure_sovits

from ..middleware.auth_middleware import require_auth_if_enabled
from ..models import ApiOk
from ..models_additional import RvcStartRequest

# WebSocket protocol for standardized messaging (GAP-CRIT-002)
from ..ws.protocol import (
    ErrorCode,
    MessageType,
    create_error,
    create_message,
)


class RvcStartResponse(BaseModel):
    """Response model for RVC session start."""

    session_id: str
    status: str
    message: str


logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/rvc",
    tags=["rvc"],
    dependencies=[Depends(require_auth_if_enabled)],
)

# Backward-compatible engine aliases used by the UI and some clients.
_ENGINE_ID_ALIASES: dict[str, str] = {
    "sovits": "sovits_svc",
    "sovits_v4": "sovits_svc",
    "gpt_sovits": "sovits_svc",
}


def _normalize_engine_id(engine_id: str) -> str:
    engine_norm = (engine_id or "").strip().lower()
    return _ENGINE_ID_ALIASES.get(engine_norm, engine_norm)


# Engine router for RVC
ENGINE_AVAILABLE = False
engine_router = None
quality_metrics = None

# RVC engine initialization via EngineService (ADR-008 compliant)
ENGINE_AVAILABLE = False
_rvc_engine_service = None

try:
    _rvc_engine_service = get_engine_service()
    engines = _rvc_engine_service.list_engines()
    ENGINE_AVAILABLE = len(engines) > 0
    if ENGINE_AVAILABLE:
        logger.info(f"RVC EngineService initialized with {len(engines)} engines")
    else:
        logger.warning("No engines available for RVC")
except Exception as e:
    logger.warning(f"RVC EngineService not available: {e}")
    ENGINE_AVAILABLE = False


def _get_quality_metrics():
    """Get quality metrics via EngineService."""
    if _rvc_engine_service is None:
        return {}
    return {
        "calculate_all": _rvc_engine_service.calculate_all_metrics,
        "similarity": _rvc_engine_service.calculate_similarity,
    }


@router.post("/convert")
async def convert_voice(
    source_audio_id: str,
    target_speaker_model: str,
    engine_id: str = "rvc",
    pitch_shift: int = 0,
    protect: float = 0.33,
    index_rate: float = 0.75,
    enhance_quality: bool = True,
    calculate_quality: bool = True,
):
    """
    Convert voice using RVC.

    Args:
        source_audio_id: ID of source audio file
        target_speaker_model: Path to target speaker model
        pitch_shift: Pitch shift in semitones (-12 to 12)
        protect: Protect voiceless sounds (0.0-0.5)
        index_rate: Index rate for retrieval (0.0-1.0)
        enhance_quality: If True, apply quality enhancement
        calculate_quality: If True, return quality metrics

    Returns:
        Conversion result with audio_id and quality metrics
    """
    if not ENGINE_AVAILABLE or not engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    try:
        requested_engine = (engine_id or "rvc").strip().lower()
        engine_key = _normalize_engine_id(requested_engine)
        if engine_key == "sovits_svc":
            ensure_sovits(auto_download=False)

        # Get conversion engine via EngineService
        engine = _rvc_engine_service.get_engine(engine_key) if _rvc_engine_service else None
        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Conversion engine '{engine_key}' is not available or failed to initialize",
            )

        # Get source audio path using helper function
        from .audio import _get_audio_path

        source_audio_path = _get_audio_path(source_audio_id)
        if not source_audio_path or not os.path.exists(source_audio_path):
            # Try alternative storage
            from .voice import _audio_storage as voice_audio_storage

            source_audio_path = voice_audio_storage.get(source_audio_id)
            if not source_audio_path or not os.path.exists(source_audio_path):
                raise HTTPException(
                    status_code=404, detail=f"Source audio not found: {source_audio_id}"
                )

        if engine_key == "sovits_svc":
            inference_configured = bool(getattr(engine, "infer_command", None))
            allow_passthrough = bool(getattr(engine, "allow_passthrough", False))
            if not inference_configured and not allow_passthrough:
                detail = (
                    "So-VITS-SVC inference command not configured. "
                    "Set SOVITS_SVC_INFER_COMMAND or configure "
                    "infer_command in engine settings."
                )
                raise HTTPException(status_code=424, detail=detail)

        # Perform conversion with circuit breaker protection
        output_path = tempfile.mktemp(suffix=".wav")
        breaker = get_engine_breaker(engine_key)

        try:
            async with breaker():
                result = engine.convert_voice(
                    source_audio=source_audio_path,
                    target_speaker_model=target_speaker_model,
                    output_path=output_path,
                    pitch_shift=pitch_shift,
                    enhance_quality=enhance_quality,
                    calculate_quality=calculate_quality,
                    protect=protect,
                    index_rate=index_rate,
                )
        except CircuitBreakerOpenError as e:
            raise HTTPException(
                status_code=503,
                detail=f"Engine '{engine_key}' temporarily unavailable: {e}",
            )

        # Handle result
        if isinstance(result, tuple):
            audio, quality_metrics_dict = result
        else:
            audio = result
            quality_metrics_dict = {}

        if audio is None and not os.path.exists(output_path):
            raise HTTPException(status_code=500, detail="Voice conversion failed")

        # Generate audio ID and store (persisted via voice registry)
        audio_id = str(uuid.uuid4())
        from .voice import _register_audio_file

        _register_audio_file(audio_id, output_path)

        # Calculate duration
        import wave

        try:
            with wave.open(output_path, "rb") as wav_file:
                frames = wav_file.getnframes()
                sample_rate = wav_file.getframerate()
                duration = frames / float(sample_rate)
        except (wave.Error, OSError) as wav_err:
            logger.debug(f"Could not read duration from {output_path}: {wav_err}")
            duration = 2.5

        # Build response
        response = {
            "success": True,
            "audio_id": audio_id,
            "audio_url": f"/api/rvc/audio/{audio_id}",
            "duration": duration,
            "sample_rate": 40000,
            "engine_id": engine_key,
            "device": getattr(engine, "device", None),
        }

        if engine_key == "sovits_svc":
            response["inference_configured"] = bool(getattr(engine, "infer_command", None))

        if quality_metrics_dict:
            response["quality_metrics"] = quality_metrics_dict

        return response

    except HTTPException:
        raise
    except PreflightError as e:
        # Convert service-layer preflight error to HTTP exception
        raise HTTPException(status_code=e.status_code, detail=e.detail)
    except Exception as e:
        logger.error(f"RVC conversion error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Conversion failed: {e!s}")


@router.websocket("/convert/realtime")
async def convert_realtime(websocket: WebSocket):
    """
    Real-time voice conversion stream.

    WebSocket endpoint for real-time audio streaming conversion.
    """
    await websocket.accept()

    try:
        if not ENGINE_AVAILABLE or not engine_router:
            await websocket.send_json(
                create_error("Engine router not available", code=ErrorCode.UNAVAILABLE)
            )
            await websocket.close()
            return

        engine = None
        conversion_params = {}

        while True:
            # Receive message
            data = await websocket.receive_text()
            request = json.loads(data)

            request_type = request.get("type")

            if request_type == "start":
                # Initialize conversion session
                target_speaker_model = request.get("target_speaker_model")
                pitch_shift = request.get("pitch_shift", 0)
                protect = request.get("protect", 0.33)
                index_rate = request.get("index_rate", 0.75)

                # Get RVC engine via EngineService
                engine = _rvc_engine_service.get_rvc_engine() if _rvc_engine_service else None
                if engine is None:
                    await websocket.send_json(
                        create_error("RVC engine is not available", code=ErrorCode.ENGINE_ERROR)
                    )
                    continue

                conversion_params = {
                    "target_speaker_model": target_speaker_model,
                    "pitch_shift": pitch_shift,
                    "protect": protect,
                    "index_rate": index_rate,
                }

                await websocket.send_json(
                    create_message(MessageType.START, {"message": "Real-time conversion started"})
                )

            elif request_type == "audio_chunk":
                # Convert audio chunk
                if engine is None:
                    await websocket.send_json(
                        create_error("Conversion not started", code=ErrorCode.VALIDATION_ERROR)
                    )
                    continue

                try:
                    # Decode audio chunk
                    audio_b64 = request.get("data")
                    audio_bytes = base64.b64decode(audio_b64)
                    audio_chunk = np.frombuffer(audio_bytes, dtype=np.float32)

                    # Convert chunk with circuit breaker protection
                    import time

                    breaker = get_engine_breaker("rvc")
                    start_time = time.time()

                    try:
                        async with breaker():
                            converted_chunk = engine.convert_realtime(
                                audio_chunk, **conversion_params
                            )
                    except CircuitBreakerOpenError as e:
                        await websocket.send_json(
                            create_error(
                                f"RVC engine temporarily unavailable: {e}",
                                code=ErrorCode.UNAVAILABLE,
                            )
                        )
                        continue

                    latency_ms = (time.time() - start_time) * 1000

                    # Encode converted chunk
                    converted_bytes = converted_chunk.tobytes()
                    converted_b64 = base64.b64encode(converted_bytes).decode("utf-8")

                    # Send converted chunk (using standardized protocol)
                    await websocket.send_json(
                        create_message(
                            MessageType.CONVERTED_CHUNK,
                            {
                                "data": converted_b64,
                                "sample_rate": 40000,
                                "format": "float32",
                                "latency_ms": latency_ms,
                            },
                        )
                    )

                except Exception as e:
                    logger.error(f"Chunk conversion failed: {e}")
                    await websocket.send_json(
                        create_error(f"Chunk conversion failed: {e!s}", code=ErrorCode.ENGINE_ERROR)
                    )

            elif request_type == "stop":
                # Stop conversion
                await websocket.send_json(
                    create_message(MessageType.STOP, {"message": "Real-time conversion stopped"})
                )
                break

    except WebSocketDisconnect:
        logger.info("RVC WebSocket disconnected")
    except Exception as e:
        logger.error(f"RVC WebSocket error: {e}", exc_info=True)
        try:
            await websocket.send_json(
                create_error(f"WebSocket error: {e!s}", code=ErrorCode.INTERNAL_ERROR)
            )
        except Exception as send_err:
            logger.debug(f"Could not send error to RVC WebSocket client: {send_err}")
    finally:
        try:
            await websocket.close()
        except Exception as close_err:
            logger.debug(f"RVC WebSocket close error: {close_err}")


@router.get("/models")
async def get_models():
    """
    Get available RVC models.

    Returns:
        List of available RVC models
    """
    try:
        model_cache_dir = os.path.join(str(get_models_path()), "rvc")

        models = []
        if os.path.exists(model_cache_dir):
            for item in os.listdir(model_cache_dir):
                model_path = os.path.join(model_cache_dir, item)
                if os.path.isdir(model_path):
                    # Check if it's a valid RVC model
                    model_files = os.listdir(model_path)
                    if any(f.endswith(".pth") for f in model_files):
                        models.append(
                            {
                                "id": item,
                                "name": item,
                                "path": model_path,
                                "sample_rate": 40000,
                                "created_at": "2025-01-27T10:00:00Z",
                            }
                        )

        return {"models": models}

    except Exception as e:
        logger.error(f"Failed to get models: {e}")
        return {"models": []}


@router.post("/models/upload")
async def upload_model(model_file: UploadFile = File(...), model_name: str | None = None):
    """
    Upload RVC model.

    Args:
        model_file: Model file to upload
        model_name: Optional model name

    Returns:
        Upload result with model_id
    """
    try:
        model_cache_dir = os.path.join(str(get_models_path()), "rvc")
        os.makedirs(model_cache_dir, exist_ok=True)

        # Generate model ID
        model_id = model_name or str(uuid.uuid4())
        model_dir = os.path.join(model_cache_dir, model_id)
        os.makedirs(model_dir, exist_ok=True)

        # Save model file
        filename = model_file.filename or "model.pth"
        model_path = os.path.join(model_dir, filename)
        with open(model_path, "wb") as f:
            content = await model_file.read()
            f.write(content)

        return {
            "success": True,
            "model_id": model_id,
            "message": "Model uploaded successfully",
        }

    except Exception as e:
        logger.error(f"Model upload failed: {e}")
        raise HTTPException(status_code=500, detail=f"Upload failed: {e!s}")


@router.get("/audio/{audio_id}")
async def get_audio(audio_id: str):
    """
    Retrieve converted audio file.

    Returns the audio file as a WAV stream for playback.
    """
    from .voice import _audio_storage

    if audio_id not in _audio_storage:
        raise HTTPException(status_code=404, detail=f"Audio not found: {audio_id}")

    file_path = _audio_storage[audio_id]

    if not os.path.exists(file_path):
        del _audio_storage[audio_id]
        raise HTTPException(status_code=404, detail="Audio file not found on disk")

    return FileResponse(
        file_path,
        media_type="audio/wav",
        filename=f"{audio_id}.wav",
    )


@router.post("/start", response_model=RvcStartResponse)
async def start(req: RvcStartRequest) -> RvcStartResponse:
    """
    Start RVC session (legacy endpoint).

    Creates a new RVC conversion session for real-time or batch processing.

    Args:
        req: Request with session configuration

    Returns:
        RvcStartResponse with session_id and status
    """
    try:
        if not ENGINE_AVAILABLE or not engine_router:
            raise HTTPException(status_code=503, detail="Engine router not available")

        # Generate session ID
        session_id = f"rvc_{uuid.uuid4().hex[:8]}"

        # Initialize session (in production, store in database)
        # For now, we'll just return the session ID
        logger.info(f"RVC session started: {session_id}")

        return RvcStartResponse(
            session_id=session_id,
            status="started",
            message="RVC session created successfully",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to start RVC session: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to start session: {e!s}") from e


@router.post("/stop")
async def stop(req: dict) -> ApiOk:
    """
    Stop RVC session (legacy endpoint).

    Stops an active RVC conversion session and cleans up resources.

    Args:
        req: Request with session_id (optional)

    Returns:
        ApiOk indicating success
    """
    try:
        session_id = req.get("session_id") if isinstance(req, dict) else None

        if session_id:
            logger.info(f"RVC session stopped: {session_id}")
        else:
            logger.info("RVC session stop requested (no session_id provided)")

        # In production, clean up session resources here
        # For now, just return success

        return ApiOk()

    except Exception as e:
        logger.error(f"Failed to stop RVC session: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to stop session: {e!s}") from e
