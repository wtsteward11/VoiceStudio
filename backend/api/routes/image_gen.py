"""
Image Generation Routes

High-quality image generation endpoints with support for multiple engines.
"""

from __future__ import annotations

import base64
import logging
import os
import tempfile
import uuid
from io import BytesIO

import numpy as np
from fastapi import APIRouter, File, HTTPException, UploadFile
from PIL import Image

from backend.core.circuit_breaker import (
    CircuitBreakerOpenError,
    get_engine_breaker,
)
from backend.core.security.file_validation import (
    FileValidationError,
    validate_image_file,
)
from backend.ml.models.engine_service import get_engine_service

from ..models_additional import (
    FaceEnhancementRequest,
    FaceEnhancementResponse,
    FaceQualityAnalysis,
    ImageGenerateRequest,
    ImageGenerateResponse,
    ImageUpscaleRequest,
    ImageUpscaleResponse,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/image", tags=["image", "generation"])

# In-memory storage for generated images
# (replace with database/storage in production)
_image_storage: dict[str, str] = {}  # image_id -> file_path


def _analyze_image_artifacts(img_array: np.ndarray) -> float:
    """
    Analyze image artifacts (compression artifacts, noise, blockiness).

    Returns artifact score (0.0 = no artifacts, 10.0 = severe artifacts).
    Lower is better.
    """

    # Check for blockiness (compression artifacts)
    # Analyze local variance - compression creates uniform blocks
    h, w = img_array.shape[:2]
    block_size = 8
    variance_scores = []

    for y in range(0, h - block_size, block_size):
        for x in range(0, w - block_size, block_size):
            block = img_array[y : y + block_size, x : x + block_size]
            # Convert to grayscale for variance calculation
            block_gray = np.mean(block, axis=2) if len(block.shape) == 3 else block
            variance = np.var(block_gray)
            variance_scores.append(variance)

    # Low variance indicates compression artifacts
    if variance_scores:
        avg_variance = np.mean(variance_scores)
        # Normalize to 0-10 scale (higher variance = fewer artifacts)
        artifact_score = max(0.0, min(10.0, 10.0 - (avg_variance / 100.0)))
    else:
        artifact_score = 5.0  # Default middle score

    # Check for noise (high frequency content)
    # Calculate Laplacian variance for sharpness/noise detection
    if len(img_array.shape) == 3:
        gray = np.mean(img_array, axis=2).astype(np.float32)
    else:
        gray = img_array.astype(np.float32)

    # Simple edge detection (Laplacian-like)
    kernel = np.array([[0, -1, 0], [-1, 4, -1], [0, -1, 0]], dtype=np.float32)
    edges = np.abs(np.convolve(gray.flatten(), kernel.flatten(), mode="valid"))
    edge_variance = np.var(edges)

    # High edge variance can indicate noise
    if edge_variance > 1000:  # Threshold for excessive noise
        artifact_score = min(10.0, artifact_score + 2.0)

    return float(artifact_score)


def _analyze_image_alignment(img_array: np.ndarray) -> float:
    """
    Analyze image alignment and symmetry.

    Returns alignment score (0.0 = poor, 10.0 = excellent).
    """
    import numpy as np

    h, w = img_array.shape[:2]

    # Check center composition (assuming face/important content should be centered)
    # Analyze luminance distribution
    if len(img_array.shape) == 3:
        gray = np.mean(img_array, axis=2).astype(np.float32)
    else:
        gray = img_array.astype(np.float32)

    # Calculate center of mass
    y_coords, x_coords = np.mgrid[0:h, 0:w]
    total_luminance = np.sum(gray)

    if total_luminance > 0:
        center_y = np.sum(y_coords * gray) / total_luminance
        center_x = np.sum(x_coords * gray) / total_luminance

        # Check how close center of mass is to image center
        image_center_y = h / 2.0
        image_center_x = w / 2.0

        distance_from_center = np.sqrt(
            (center_y - image_center_y) ** 2 + (center_x - image_center_x) ** 2
        )
        max_distance = np.sqrt((h / 2) ** 2 + (w / 2) ** 2)

        # Normalize to 0-10 scale (closer to center = better alignment)
        alignment_score = max(0.0, 10.0 * (1.0 - (distance_from_center / max_distance)))
    else:
        alignment_score = 5.0  # Default middle score

    # Check horizontal symmetry
    mid_x = w // 2
    left_half = gray[:, :mid_x]
    right_half = np.flip(gray[:, mid_x:], axis=1)

    if left_half.shape == right_half.shape:
        symmetry_diff = np.mean(np.abs(left_half - right_half))
        symmetry_score = max(0.0, 10.0 - (symmetry_diff / 10.0))
        alignment_score = (alignment_score + symmetry_score) / 2.0

    return float(alignment_score)


def _analyze_image_realism(img_array: np.ndarray) -> float:
    """
    Analyze image realism (color balance, contrast, sharpness).

    Returns realism score (0.0 = unrealistic, 10.0 = realistic).
    """
    import numpy as np

    if len(img_array.shape) == 3:
        # Color balance - check if RGB channels are balanced
        r_channel = img_array[:, :, 0].astype(np.float32)
        g_channel = img_array[:, :, 1].astype(np.float32)
        b_channel = img_array[:, :, 2].astype(np.float32)

        r_mean = np.mean(r_channel)
        g_mean = np.mean(g_channel)
        b_mean = np.mean(b_channel)

        # Balanced color channels indicate good color balance
        color_variance = np.var([r_mean, g_mean, b_mean])
        color_balance_score = max(0.0, 10.0 - (color_variance / 100.0))

        # Contrast analysis
        gray = np.mean(img_array, axis=2).astype(np.float32)
    else:
        gray = img_array.astype(np.float32)
        color_balance_score = 7.0  # Default for grayscale

    # Contrast (standard deviation of luminance)
    contrast = np.std(gray)
    # Good contrast is typically 30-80 for 8-bit images
    if 30 <= contrast <= 80:
        contrast_score = 10.0
    elif contrast < 30:
        contrast_score = max(0.0, 5.0 * (contrast / 30.0))
    else:
        contrast_score = max(0.0, 10.0 - ((contrast - 80) / 20.0))

    # Sharpness (edge detection)
    # Calculate gradient magnitude
    grad_y = np.diff(gray, axis=0)
    grad_x = np.diff(gray, axis=1)
    # Pad to match dimensions
    grad_y = np.pad(grad_y, ((0, 1), (0, 0)), mode="edge")
    grad_x = np.pad(grad_x, ((0, 0), (0, 1)), mode="edge")

    gradient_magnitude = np.sqrt(grad_x**2 + grad_y**2)
    sharpness = np.mean(gradient_magnitude)

    # Good sharpness is typically 5-15 for 8-bit images
    if 5 <= sharpness <= 15:
        sharpness_score = 10.0
    elif sharpness < 5:
        sharpness_score = max(0.0, 5.0 * (sharpness / 5.0))
    else:
        sharpness_score = max(0.0, 10.0 - ((sharpness - 15) / 10.0))

    # Combine scores
    realism_score = color_balance_score * 0.3 + contrast_score * 0.4 + sharpness_score * 0.3

    return float(realism_score)


# Engine router for image generation
# Image engine initialization via EngineService (ADR-008 compliant)
ENGINE_AVAILABLE = False
_image_engine_service = None

try:
    _image_engine_service = get_engine_service()
    engines = _image_engine_service.list_engines()
    ENGINE_AVAILABLE = len(engines) > 0
    if ENGINE_AVAILABLE:
        engine_names = [e.get("id", e.get("name", "")) for e in engines]
        logger.info(
            f"Image EngineService initialized with {len(engines)} engines: {', '.join(engine_names[:5])}..."
        )
    else:
        logger.warning("No engines available for image generation")
except Exception as e:
    logger.warning(f"Image EngineService not available: {e}")
    ENGINE_AVAILABLE = False


@router.post("/generate", response_model=ImageGenerateResponse)
async def generate_image(req: ImageGenerateRequest) -> ImageGenerateResponse:
    """
    Generate image from text prompt using specified engine.

    Engines are dynamically discovered from engine manifests.
    Any engine with an engine.manifest.json file in engines/ will be available.
    """
    try:
        # Dynamically discover available engines via EngineService
        valid_engines: list[str] = []
        if ENGINE_AVAILABLE and _image_engine_service:
            engine_list = _image_engine_service.list_engines()
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

        # Generate image if engines available
        if ENGINE_AVAILABLE and _image_engine_service:
            try:
                # Get engine instance
                engine = _image_engine_service.get_engine(req.engine)
                if engine is None:
                    raise HTTPException(
                        status_code=503,
                        detail=f"Engine '{req.engine}' is not available or failed to initialize",
                    )

                # Create temporary output file
                image_id = f"img_{uuid.uuid4().hex[:12]}"
                output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_images")
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, f"{image_id}.png")

                # Prepare generation parameters
                gen_kwargs = {
                    "prompt": req.prompt,
                    "negative_prompt": req.negative_prompt or "",
                    "width": req.width or 512,
                    "height": req.height or 512,
                    "steps": req.steps or 20,
                    "cfg_scale": req.cfg_scale or 7.0,
                    "sampler": req.sampler,
                    "seed": req.seed,
                    "output_path": output_path,
                }

                # Add additional parameters from request
                if req.additional_params:
                    gen_kwargs.update(req.additional_params)

                # Generate image with circuit breaker protection
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
                        detail=f"Image generation failed for engine '{req.engine}'",
                    )

                # Handle tuple result (image, metadata)
                if isinstance(result, tuple):
                    image, metadata = result
                else:
                    image = result
                    metadata = {}

                # Ensure image is saved
                if not os.path.exists(output_path):
                    image.save(output_path)

                # Store image path
                _image_storage[image_id] = output_path

                # Convert image to base64 for response
                buffer = BytesIO()
                image.save(buffer, format="PNG")
                image_base64 = base64.b64encode(buffer.getvalue()).decode("utf-8")

                return ImageGenerateResponse(
                    image_id=image_id,
                    image_url=f"/api/image/{image_id}",
                    image_base64=f"data:image/png;base64,{image_base64}",
                    width=image.width,
                    height=image.height,
                    format="png",
                    metadata=metadata,
                )

            except Exception as e:
                logger.error(f"Image generation error: {e}", exc_info=True)
                raise HTTPException(status_code=500, detail=f"Image generation failed: {e!s}")
        else:
            # Engines not available - return proper error
            raise HTTPException(
                status_code=503,
                detail=(
                    "Image generation engines are not available. "
                    "Please ensure engines are properly installed and configured. "
                    "Check engine installation and ensure engine manifests are loaded."
                ),
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Unexpected error in generate_image: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Internal server error: {e!s}")


@router.post("/upscale", response_model=ImageUpscaleResponse)
async def upscale_image(
    req: ImageUpscaleRequest, image_file: UploadFile | None = File(None)
) -> ImageUpscaleResponse:
    """
    Upscale image using Real-ESRGAN or other upscaling engines.
    """
    try:
        if not ENGINE_AVAILABLE or not _image_engine_service:
            raise HTTPException(status_code=503, detail="Upscaling engines are not available")

        # Get upscaling engine (default: realesrgan)
        engine_name = req.engine or "realesrgan"
        engine = _image_engine_service.get_engine(engine_name)

        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Upscaling engine '{engine_name}' is not available",
            )

        # Load input image
        if image_file:
            image_data = await image_file.read()
            # Validate image file type by magic bytes
            try:
                validate_image_file(image_data, filename=image_file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid image file: {e.message}",
                ) from e
            input_image = Image.open(BytesIO(image_data))
        elif req.image_id:
            # Load from stored image
            if req.image_id not in _image_storage:
                raise HTTPException(status_code=404, detail=f"Image '{req.image_id}' not found")
            input_image = Image.open(_image_storage[req.image_id])
        else:
            raise HTTPException(
                status_code=400, detail="Either image_file or image_id must be provided"
            )

        # Create output path
        image_id = f"upscaled_{uuid.uuid4().hex[:12]}"
        output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_images")
        os.makedirs(output_dir, exist_ok=True)
        output_path = os.path.join(output_dir, f"{image_id}.png")

        # Upscale image with circuit breaker protection
        breaker = get_engine_breaker(engine_name)
        try:
            async with breaker():
                if hasattr(engine, "upscale"):
                    upscaled_image = engine.upscale(
                        input_image, output_path=output_path, **req.additional_params or {}
                    )
                else:
                    # Fallback: use generate method with image parameter
                    upscaled_image = engine.generate(
                        prompt="",
                        image=input_image,
                        output_path=output_path,
                        **req.additional_params or {},
                    )
        except CircuitBreakerOpenError as e:
            raise HTTPException(
                status_code=503,
                detail=f"Upscaling engine '{engine_name}' temporarily unavailable: {e}",
            )

        if upscaled_image is None:
            raise HTTPException(status_code=500, detail="Image upscaling failed")

        # Store upscaled image
        _image_storage[image_id] = output_path

        # Convert to base64
        buffer = BytesIO()
        upscaled_image.save(buffer, format="PNG")
        image_base64 = base64.b64encode(buffer.getvalue()).decode("utf-8")

        return ImageUpscaleResponse(
            image_id=image_id,
            image_url=f"/api/image/{image_id}",
            image_base64=f"data:image/png;base64,{image_base64}",
            width=upscaled_image.width,
            height=upscaled_image.height,
            scale=req.scale or 4,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Upscaling error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Image upscaling failed: {e!s}")


@router.post("/enhance-face", response_model=FaceEnhancementResponse)
async def enhance_face(req: FaceEnhancementRequest) -> FaceEnhancementResponse:
    """
    Advanced deepfake face quality enhancement (IDEA 66).

    Analyzes face quality and applies face-specific enhancement
    for better deepfake realism.
    """
    try:
        if not req.image_id and not req.video_id:
            raise HTTPException(
                status_code=400, detail="Either image_id or video_id must be provided"
            )

        # For images, analyze and enhance
        if req.image_id:
            if req.image_id not in _image_storage:
                raise HTTPException(status_code=404, detail=f"Image '{req.image_id}' not found")

            image_path = _image_storage[req.image_id]
            input_image = Image.open(image_path)

            # Analyze image quality metrics
            width, height = input_image.size
            resolution_score = min(10.0, (width * height) / 10000.0)  # Normalize

            # Convert to numpy array for analysis
            import numpy as np

            img_array = np.array(input_image.convert("RGB"))

            # Analyze artifacts (compression artifacts, noise)
            artifact_score = _analyze_image_artifacts(img_array)

            # Analyze alignment/symmetry (centering, symmetry)
            alignment_score = _analyze_image_alignment(img_array)

            # Analyze realism (color balance, contrast, sharpness)
            realism_score = _analyze_image_realism(img_array)

            overall_quality = (
                resolution_score + (10 - artifact_score) + alignment_score + realism_score
            ) / 4.0

            original_analysis = FaceQualityAnalysis(
                resolution_score=resolution_score,
                artifact_score=artifact_score,
                alignment_score=alignment_score,
                realism_score=realism_score,
                overall_quality=overall_quality,
                recommendations=[],
            )

            # Apply enhancement if requested
            enhanced_image_id = None
            enhanced_image_url = None
            enhanced_analysis = None
            quality_improvement = 0.0

            if req.multi_stage or req.face_specific:
                # Apply upscaling for face enhancement
                if ENGINE_AVAILABLE and _image_engine_service:
                    try:
                        # Use face-specific upscaling if available
                        engine_name = "realesrgan"  # Default
                        engine = _image_engine_service.get_engine(engine_name)

                        if engine and hasattr(engine, "upscale"):
                            enhanced_image_id = (
                                f"face_enhanced_{req.image_id}_{uuid.uuid4().hex[:8]}"
                            )
                            output_dir = os.path.join(tempfile.gettempdir(), "voicestudio_images")
                            os.makedirs(output_dir, exist_ok=True)
                            output_path = os.path.join(output_dir, f"{enhanced_image_id}.png")

                            enhanced_image = engine.upscale(input_image, output_path=output_path)
                            if enhanced_image:
                                _image_storage[enhanced_image_id] = output_path
                                enhanced_image_url = f"/api/image/{enhanced_image_id}"

                                # Re-analyze enhanced image
                                enhanced_width, enhanced_height = enhanced_image.size
                                enhanced_resolution = min(
                                    10.0, (enhanced_width * enhanced_height) / 10000.0
                                )

                                enhanced_img_array = np.array(enhanced_image.convert("RGB"))
                                enhanced_artifact = _analyze_image_artifacts(enhanced_img_array)
                                enhanced_alignment = _analyze_image_alignment(enhanced_img_array)
                                enhanced_realism = _analyze_image_realism(enhanced_img_array)

                                enhanced_overall = (
                                    enhanced_resolution
                                    + (10 - enhanced_artifact)
                                    + enhanced_alignment
                                    + enhanced_realism
                                ) / 4.0

                                enhanced_analysis = FaceQualityAnalysis(
                                    resolution_score=enhanced_resolution,
                                    artifact_score=enhanced_artifact,
                                    alignment_score=enhanced_alignment,
                                    realism_score=enhanced_realism,
                                    overall_quality=enhanced_overall,
                                    recommendations=[],
                                )

                                quality_improvement = min(
                                    1.0, (enhanced_overall - overall_quality) / 10.0
                                )
                    except Exception as e:
                        logger.warning(f"Face enhancement failed: {e}")

            return FaceEnhancementResponse(
                image_id=req.image_id,
                video_id=None,
                enhanced_image_id=enhanced_image_id,
                enhanced_video_id=None,
                enhanced_image_url=enhanced_image_url,
                enhanced_video_url=None,
                original_analysis=original_analysis,
                enhanced_analysis=enhanced_analysis,
                quality_improvement=quality_improvement,
            )

        # For videos, delegate to video_face_enhancer service
        elif req.video_id:
            try:
                from backend.media.video.video_face_enhancer import enhance_video_faces

                enhanced_video_id = f"enhanced_{req.video_id}"
                result = await enhance_video_faces(
                    video_id=req.video_id,
                    model=req.model or "realesrgan",
                    strength=req.strength or 0.8,
                )
                return FaceEnhancementResponse(
                    image_id=None,
                    video_id=req.video_id,
                    enhanced_image_id=None,
                    enhanced_video_id=result.get("enhanced_video_id", enhanced_video_id),
                    enhanced_image_url=None,
                    enhanced_video_url=result.get("enhanced_video_url"),
                    original_analysis=result.get("original_analysis", {}),
                    enhanced_analysis=result.get("enhanced_analysis", {}),
                    quality_improvement=result.get("quality_improvement", {}),
                )
            except ImportError:
                # Graceful fallback if service not available
                raise HTTPException(
                    status_code=400,
                    detail=(
                        "Video face enhancement requires additional dependencies. "
                        "Install with: pip install opencv-python realesrgan. "
                        "Alternatively, extract frames and use image face enhancement."
                    ),
                )
            except Exception as ve:
                raise HTTPException(
                    status_code=500,
                    detail=f"Video face enhancement failed: {ve!s}",
                ) from ve

        raise HTTPException(
            status_code=400,
            detail="Either image_id or video_id must be provided",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Face enhancement error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Face enhancement failed: {e!s}") from e


@router.get("/{image_id}")
async def get_image(image_id: str):
    """Retrieve generated image by ID."""
    if image_id not in _image_storage:
        raise HTTPException(status_code=404, detail="Image not found")

    image_path = _image_storage[image_id]
    if not os.path.exists(image_path):
        raise HTTPException(status_code=404, detail="Image file not found")

    from fastapi.responses import FileResponse

    return FileResponse(image_path, media_type="image/png")


@router.get("/engines/list")
async def list_engines() -> dict:
    """List all available image generation engines."""
    if not ENGINE_AVAILABLE or not _image_engine_service:
        return {"engines": [], "available": False}

    try:
        engines = _image_engine_service.list_engines() if _image_engine_service else []
        return {"engines": engines, "available": True, "count": len(engines)}
    except Exception as e:
        logger.error(f"Error listing engines: {e}")
        return {"engines": [], "available": False, "error": str(e)}
