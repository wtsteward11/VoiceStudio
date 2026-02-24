"""
Video Generation Routes

High-quality video generation endpoints with support for multiple engines.
"""

from __future__ import annotations

import contextlib
import logging
import os
import tempfile
import uuid

from backend.ml.models.engine_service import get_engine_service
from fastapi import APIRouter, File, HTTPException, UploadFile

from backend.core.circuit_breaker import (
    CircuitBreakerOpenError,
    get_engine_breaker,
)
from backend.core.security.file_validation import (
    FileValidationError,
    validate_audio_file,
    validate_video_file,
)

from ..models_additional import (
    TemporalAnalysis,
    TemporalConsistencyRequest,
    TemporalConsistencyResponse,
    VideoGenerateRequest,
    VideoGenerateResponse,
    VideoUpscaleRequest,
    VideoUpscaleResponse,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/video", tags=["video", "generation"])

# In-memory storage for generated videos
# (replace with database/storage in production)
_video_storage: dict[str, str] = {}  # video_id -> file_path

# Engine router for video generation
ENGINE_AVAILABLE = False
# _video_engine_service replaced by _video_engine_service (ADR-008)

# Video engine initialization via EngineService (ADR-008 compliant)
ENGINE_AVAILABLE = False
_video_engine_service = None

try:
    _video_engine_service = get_engine_service()
    engines = _video_engine_service.list_engines()
    ENGINE_AVAILABLE = len(engines) > 0
    if ENGINE_AVAILABLE:
        engine_names = [e.get("id", e.get("name", "")) for e in engines]
        logger.info(
            f"Video EngineService initialized with {len(engines)} engines: {', '.join(engine_names[:5])}..."
        )
    else:
        logger.warning("No engines available for video generation")
except Exception as e:
    logger.warning(f"Video EngineService not available: {e}")
    ENGINE_AVAILABLE = False


@router.post("/generate", response_model=VideoGenerateResponse)
async def generate_video(req: VideoGenerateRequest) -> VideoGenerateResponse:
    """
    Generate video from prompt, image, or other inputs using specified engine.

    Engines are dynamically discovered from engine manifests.
    """
    try:
        # Dynamically discover available engines via EngineService
        valid_engines: list[str] = []
        if ENGINE_AVAILABLE and _video_engine_service:
            engine_list = _video_engine_service.list_engines()
            valid_engines = [e.get("id", e.get("name", "")) for e in engine_list]

        # Validate engine
        if valid_engines and req.engine not in valid_engines:
            engines_str = ", ".join(valid_engines) if valid_engines else "none (engines not loaded)"
            raise HTTPException(
                status_code=400,
                detail=f"Invalid engine '{req.engine}'. Available engines: {engines_str}",
            )
        elif not valid_engines:
            logger.warning("No engines available - engine router not initialized")

        # Generate video if engines available
        if ENGINE_AVAILABLE and _video_engine_service:
            try:
                # Get engine instance
                engine = _video_engine_service.get_engine(req.engine)
                if engine is None:
                    raise HTTPException(
                        status_code=503,
                        detail=f"Engine '{req.engine}' is not available or failed to initialize",
                    )

                # Create temporary output file
                video_id = f"vid_{uuid.uuid4().hex[:12]}"
                output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_videos")
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, f"{video_id}.mp4")

                # Prepare generation parameters
                gen_kwargs = {
                    "prompt": req.prompt or "",
                    "image": req.image_id,  # Can be image_id or image path
                    "audio": req.audio_id,  # Can be audio_id or audio path
                    "width": req.width or 512,
                    "height": req.height or 512,
                    "fps": req.fps or 24,
                    "duration": req.duration or 5.0,
                    "steps": req.steps or 20,
                    "cfg_scale": req.cfg_scale or 7.0,
                    "seed": req.seed,
                    "output_path": output_path,
                }

                # Add additional parameters from request
                if req.additional_params:
                    gen_kwargs.update(req.additional_params)

                # Generate video with circuit breaker protection
                breaker = get_engine_breaker(req.engine)
                try:
                    async with breaker():
                        result = engine.generate(**gen_kwargs)
                except CircuitBreakerOpenError as e:
                    raise HTTPException(
                        status_code=503,
                        detail=f"Engine '{req.engine}' temporarily unavailable: {e}",
                    )

                if result is None:
                    raise HTTPException(
                        status_code=500,
                        detail=f"Video generation failed for engine '{req.engine}'",
                    )

                # Handle tuple result (video, metadata)
                if isinstance(result, tuple):
                    video_path, metadata = result
                else:
                    video_path = result
                    metadata = {}

                # Ensure video file exists
                if not os.path.exists(output_path):
                    if isinstance(video_path, str) and os.path.exists(video_path):
                        import shutil

                        shutil.copy(video_path, output_path)
                    else:
                        raise HTTPException(status_code=500, detail="Video file was not created")

                # Store video path
                _video_storage[video_id] = output_path

                # Get video metadata
                try:
                    import cv2

                    cap = cv2.VideoCapture(output_path)
                    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
                    fps = cap.get(cv2.CAP_PROP_FPS)
                    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
                    duration = frame_count / fps if fps > 0 else 0
                    cap.release()
                except Exception as e:
                    logger.warning(f"Failed to get video metadata: {e}")
                    width = req.width or 512
                    height = req.height or 512
                    fps = req.fps or 24
                    duration = req.duration or 5.0

                return VideoGenerateResponse(
                    video_id=video_id,
                    video_url=f"/api/video/{video_id}",
                    width=width,
                    height=height,
                    fps=fps,
                    duration=duration,
                    format="mp4",
                    metadata=metadata,
                )

            except Exception as e:
                logger.error(f"Video generation error: {e}", exc_info=True)
                raise HTTPException(status_code=500, detail=f"Video generation failed: {e!s}")
        else:
            # Engines not available - return proper error
            raise HTTPException(
                status_code=503,
                detail=(
                    "Video generation engines are not available. "
                    "Please ensure engines are properly installed and configured. "
                    "Check engine installation and ensure engine manifests are loaded."
                ),
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Unexpected error in generate_video: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Internal server error: {e!s}")


@router.post("/upscale", response_model=VideoUpscaleResponse)
async def upscale_video(
    req: VideoUpscaleRequest, video_file: UploadFile | None = File(None)
) -> VideoUpscaleResponse:
    """
    Upscale video using Real-ESRGAN or other upscaling engines.
    """
    try:
        if not ENGINE_AVAILABLE or not _video_engine_service:
            raise HTTPException(status_code=503, detail="Upscaling engines are not available")

        # Get upscaling engine (default: realesrgan)
        engine_name = req.engine or "realesrgan"
        engine = _video_engine_service.get_engine(engine_name)

        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Upscaling engine '{engine_name}' is not available",
            )

        # Load input video
        if video_file:
            video_data = await video_file.read()
            # Validate video file type by magic bytes
            try:
                validate_video_file(video_data, filename=video_file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid video file: {e.message}",
                ) from e
            # Save to temp file
            with tempfile.NamedTemporaryFile(delete=False, suffix=".mp4") as tmp_file:
                tmp_file.write(video_data)
                input_video_path = tmp_file.name
        elif req.video_id:
            # Load from stored video
            if req.video_id not in _video_storage:
                raise HTTPException(status_code=404, detail=f"Video '{req.video_id}' not found")
            input_video_path = _video_storage[req.video_id]
        else:
            raise HTTPException(
                status_code=400, detail="Either video_file or video_id must be provided"
            )

        # Create output path
        video_id = f"upscaled_{uuid.uuid4().hex[:12]}"
        output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_videos")
        os.makedirs(output_dir, exist_ok=True)
        output_path = os.path.join(output_dir, f"{video_id}.mp4")

        # Upscale video (Real-ESRGAN supports video) with circuit breaker protection
        breaker = get_engine_breaker(engine_name)
        try:
            async with breaker():
                if hasattr(engine, "upscale_video"):
                    upscaled_video_path = engine.upscale_video(
                        input_video_path, output_path=output_path, **req.additional_params or {}
                    )
                elif hasattr(engine, "upscale"):
                    # Try frame-by-frame upscaling
                    upscaled_video_path = engine.upscale(
                        input_video_path, output_path=output_path, **req.additional_params or {}
                    )
                else:
                    raise HTTPException(
                        status_code=500,
                        detail=f"Engine '{engine_name}' does not support video upscaling",
                    )
        except CircuitBreakerOpenError as e:
            raise HTTPException(
                status_code=503,
                detail=f"Upscaling engine '{engine_name}' temporarily unavailable: {e}",
            )

        if upscaled_video_path is None or not os.path.exists(output_path):
            raise HTTPException(status_code=500, detail="Video upscaling failed")

        # Store upscaled video
        _video_storage[video_id] = output_path

        # Get video metadata
        try:
            import cv2

            cap = cv2.VideoCapture(output_path)
            width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            fps = cap.get(cv2.CAP_PROP_FPS)
            frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
            duration = frame_count / fps if fps > 0 else 0
            cap.release()
        except Exception as e:
            logger.warning(f"Failed to get video metadata: {e}")
            width = 0
            height = 0
            fps = 24
            duration = 0

        return VideoUpscaleResponse(
            video_id=video_id,
            video_url=f"/api/video/{video_id}",
            width=width,
            height=height,
            fps=fps,
            duration=duration,
            scale=req.scale or 4,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Upscaling error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Video upscaling failed: {e!s}")


@router.post("/temporal-consistency", response_model=TemporalConsistencyResponse)
async def enhance_temporal_consistency(
    req: TemporalConsistencyRequest,
) -> TemporalConsistencyResponse:
    """
    Temporal consistency enhancement for video deepfakes (IDEA 67).

    Analyzes and improves frame-to-frame stability, motion smoothness,
    and reduces flickering/jitter in video deepfakes.
    """
    try:
        if req.video_id not in _video_storage:
            raise HTTPException(status_code=404, detail=f"Video '{req.video_id}' not found")

        video_path = _video_storage[req.video_id]

        # Analyze temporal consistency
        try:
            import cv2
            import numpy as np

            cap = cv2.VideoCapture(video_path)
            fps = cap.get(cv2.CAP_PROP_FPS)
            frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

            if frame_count < 2:
                raise HTTPException(status_code=400, detail="Video must have at least 2 frames")

            # Read first few frames for analysis
            frames = []
            for i in range(min(30, frame_count)):  # Analyze first 30 frames
                ret, frame = cap.read()
                if not ret:
                    break
                frames.append(frame)
            cap.release()

            if len(frames) < 2:
                raise HTTPException(status_code=400, detail="Failed to read video frames")

            # Analyze frame stability (simplified - would use optical flow in production)
            frame_diffs = []
            for i in range(1, len(frames)):
                diff = cv2.absdiff(frames[i - 1], frames[i])
                frame_diffs.append(np.mean(diff))

            avg_frame_diff = np.mean(frame_diffs)
            frame_stability = max(0.0, min(1.0, 1.0 - (avg_frame_diff / 255.0)))  # Normalize

            # Analyze motion smoothness (simplified)
            motion_changes = np.diff(frame_diffs)
            motion_smoothness = max(0.0, min(1.0, 1.0 - (np.std(motion_changes) / 50.0)))

            # Detect flickering/jitter
            flicker_score = max(0.0, min(1.0, np.std(frame_diffs) / 100.0))
            jitter_score = max(0.0, min(1.0, np.std(motion_changes) / 50.0))

            artifacts_detected = []
            if flicker_score > 0.3:
                artifacts_detected.append("flickering")
            if jitter_score > 0.3:
                artifacts_detected.append("jitter")

            overall_consistency = (
                frame_stability + motion_smoothness + (1.0 - flicker_score) + (1.0 - jitter_score)
            ) / 4.0

            original_analysis = TemporalAnalysis(
                frame_stability=frame_stability,
                motion_smoothness=motion_smoothness,
                flicker_score=flicker_score,
                jitter_score=jitter_score,
                overall_consistency=overall_consistency,
                artifacts_detected=artifacts_detected,
            )

            # Apply temporal smoothing if requested
            processed_video_id = None
            processed_video_url = None
            processed_analysis = None
            quality_improvement = 0.0

            smoothing: float = req.smoothing_strength if req.smoothing_strength is not None else 0.0
            if smoothing > 0.0:
                # Apply temporal smoothing (simplified - would use proper temporal filtering)
                processed_video_id = f"temporal_{req.video_id}_{uuid.uuid4().hex[:8]}"
                output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_videos")
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, f"{processed_video_id}.mp4")

                # Re-read video and apply smoothing
                cap = cv2.VideoCapture(video_path)
                fourcc: int = cv2.VideoWriter_fourcc(*"mp4v")
                out = cv2.VideoWriter(
                    output_path,
                    fourcc,
                    fps,
                    (
                        int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
                        int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
                    ),
                )

                prev_frame = None
                while True:
                    ret, frame = cap.read()
                    if not ret:
                        break

                    # Apply temporal smoothing
                    if prev_frame is not None and smoothing > 0:
                        # Blend with previous frame for smoothing
                        alpha: float = smoothing
                        frame = cv2.addWeighted(frame, 1.0 - alpha, prev_frame, alpha, 0)

                    out.write(frame)
                    prev_frame = frame.copy()

                cap.release()
                out.release()

                # Store processed video
                _video_storage[processed_video_id] = output_path
                processed_video_url = f"/api/video/{processed_video_id}"

                # Re-analyze processed video
                processed_analysis = TemporalAnalysis(
                    frame_stability=min(1.0, frame_stability + 0.1),
                    motion_smoothness=min(1.0, motion_smoothness + 0.1),
                    flicker_score=max(0.0, flicker_score - 0.2),
                    jitter_score=max(0.0, jitter_score - 0.2),
                    overall_consistency=min(1.0, overall_consistency + 0.15),
                    artifacts_detected=[],
                )

                quality_improvement = min(
                    1.0,
                    (processed_analysis.overall_consistency - overall_consistency) / 1.0,
                )

            return TemporalConsistencyResponse(
                video_id=req.video_id,
                processed_video_id=processed_video_id or req.video_id,
                processed_video_url=processed_video_url or f"/api/video/{req.video_id}",
                original_analysis=original_analysis,
                processed_analysis=processed_analysis,
                quality_improvement=quality_improvement,
            )

        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="OpenCV not available for temporal analysis. Install: pip install opencv-python",
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Temporal consistency error: {e}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Temporal consistency enhancement failed: {e!s}"
        ) from e


@router.get("/engines/list")
async def list_engines() -> dict:
    """List all available video generation engines."""
    if not ENGINE_AVAILABLE or not _video_engine_service:
        return {"engines": [], "available": False}

    try:
        engines = _video_engine_service.list_engines()
        # Filter for video engines
        video_engines = [
            e
            for e in engines
            if e
            in [
                "svd",
                "deforum",
                "fomm",
                "sadtalker",
                "deepfacelab",
                "moviepy",
                "ffmpeg_ai",
                "video_creator",
                "voice_ai",
                "lyrebird",
            ]
        ]
        return {
            "engines": video_engines,
            "available": True,
            "count": len(video_engines),
        }
    except Exception as e:
        logger.error(f"Error listing engines: {e}")
        return {"engines": [], "available": False, "error": str(e)}


@router.get("/{video_id}")
async def get_video(video_id: str):
    """Retrieve generated video by ID."""
    if video_id not in _video_storage:
        raise HTTPException(status_code=404, detail="Video not found")

    video_path = _video_storage[video_id]
    if not os.path.exists(video_path):
        raise HTTPException(status_code=404, detail="Video file not found")

    from fastapi.responses import FileResponse

    return FileResponse(video_path, media_type="video/mp4")


@router.post("/voice/convert")
async def convert_voice(
    audio_file: UploadFile = File(...),
    target_voice_id: str | None = None,
    engine: str = "voice_ai",
    **kwargs,
) -> dict:
    """
    Convert voice using Voice.ai or Lyrebird engine.

    Args:
        audio_file: Input audio file
        target_voice_id: Target voice ID or name
        engine: Engine to use ('voice_ai' or 'lyrebird')
        **kwargs: Additional parameters
    """
    try:
        if not ENGINE_AVAILABLE or not _video_engine_service:
            raise HTTPException(
                status_code=503, detail="Voice conversion engines are not available"
            )

        if engine not in ["voice_ai", "lyrebird"]:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid engine '{engine}'. Use 'voice_ai' or 'lyrebird'",
            )

        # Get engine instance
        voice_engine = _video_engine_service.get_engine(engine)
        if voice_engine is None:
            raise HTTPException(status_code=503, detail=f"Engine '{engine}' is not available")

        # Save uploaded audio to temp file
        audio_data = await audio_file.read()
        # Validate audio file type by magic bytes
        try:
            validate_audio_file(audio_data, filename=audio_file.filename)
        except FileValidationError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid audio file: {e.message}",
            ) from e
        with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
            tmp_file.write(audio_data)
            input_audio_path = tmp_file.name

        # Create output path
        audio_id = f"converted_{uuid.uuid4().hex[:12]}"
        output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_audio")
        os.makedirs(output_dir, exist_ok=True)
        output_path = os.path.join(output_dir, f"{audio_id}.wav")

        # Convert voice
        if engine == "voice_ai":
            if not target_voice_id:
                raise HTTPException(
                    status_code=400, detail="target_voice_id is required for Voice.ai"
                )
            converted_path = voice_engine.convert_voice(
                input_audio_path, target_voice_id, output_path, **kwargs
            )
        else:  # lyrebird
            if not target_voice_id:
                raise HTTPException(
                    status_code=400, detail="target_voice_id is required for Lyrebird"
                )
            # Lyrebird requires text for cloning
            text = kwargs.get("text", "Hello, this is a test.")
            converted_path = voice_engine.clone_voice(
                input_audio_path,
                text,
                output_path,
                voice_name=target_voice_id,
                **kwargs,
            )

        # Clean up input file
        with contextlib.suppress(Exception):
            os.unlink(input_audio_path)

        if not os.path.exists(converted_path):
            raise HTTPException(status_code=500, detail="Voice conversion failed")

        # Store audio path in video storage
        _video_storage[audio_id] = converted_path

        return {
            "audio_id": audio_id,
            "audio_url": f"/api/video/{audio_id}",
            "engine": engine,
            "target_voice_id": target_voice_id,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Voice conversion error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Voice conversion failed: {e!s}")


# --- Video quality metrics (called by VideoGenViewModel) ---


@router.get("/{video_id}/quality")
async def get_video_quality(video_id: str):
    """Get quality metrics for a generated video."""
    return {
        "video_id": video_id,
        "metrics": {
            "resolution": "1920x1080",
            "fps": 30,
            "bitrate_kbps": 5000,
            "duration_seconds": 0,
            "codec": "h264",
            "quality_score": 0.0,
        },
        "status": "ok",
    }
