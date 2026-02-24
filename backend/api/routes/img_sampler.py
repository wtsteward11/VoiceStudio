"""
Image Sampler Routes

Endpoints for image sampling/rendering with different samplers
for diffusion models (DDIM, Euler, etc.).
"""

from __future__ import annotations

import base64
import logging
import asyncio
import os

from fastapi import APIRouter, HTTPException

from ..models_additional import ImgSamplerRequest

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/img/sampler", tags=["img", "sampler"])

# In-memory storage for sampled images
_image_storage: dict[str, str] = {}  # image_id -> file_path
_state_lock = asyncio.Lock()

# Supported samplers
SUPPORTED_SAMPLERS = [
    "ddim",
    "ddpm",
    "euler",
    "euler_a",
    "heun",
    "dpm_2",
    "dpm_2_a",
    "lms",
    "plms",
    "dpm_fast",
    "dpm_adaptive",
    "dpmpp_2s_a",
    "dpmpp_2m",
    "dpmpp_sde",
    "uni_pc",
]


@router.post("/render")
async def render(req: ImgSamplerRequest) -> dict:
    """
    Render/sample an image using a diffusion model sampler.

    This endpoint uses image generation engines to create images
    with different sampling methods (DDIM, Euler, etc.).

    Args:
        req: Request with prompt and sampler type

    Returns:
        Dictionary with generated image as base64 data URL
    """
    try:
        prompt = req.prompt
        sampler = req.sampler.lower()

        if not prompt or not prompt.strip():
            raise HTTPException(status_code=400, detail="prompt is required")

        # Validate sampler
        if sampler not in SUPPORTED_SAMPLERS:
            raise HTTPException(
                status_code=400,
                detail=(
                    f"Unsupported sampler '{sampler}'. "
                    f"Supported: {', '.join(SUPPORTED_SAMPLERS)}"
                ),
            )

        # Try to use image generation engine
        try:
            # Import image generation route
            from ..models_additional import ImageGenerateRequest
            from .image_gen import generate_image

            # Create image generation request
            gen_req = ImageGenerateRequest(
                prompt=prompt,
                engine="sdxl_comfy",
                width=512,
                height=512,
                steps=20,
                cfg_scale=7.5,
                sampler=sampler,
            )

            # Generate image
            gen_result = await generate_image(gen_req)

            # Get image path from result
            image_id = gen_result.image_id
            image_path = gen_result.image_url

            if not image_path:
                raise HTTPException(
                    status_code=502,
                    detail=(
                        "Image generation returned no output path. "
                        "Ensure the image generation engine is configured and running."
                    ),
                )
            if not os.path.exists(image_path):
                raise HTTPException(
                    status_code=502,
                    detail=(
                        "Image generation output file was not found on disk. "
                        "Check engine logs and storage permissions."
                    ),
                )

            # Read image and convert to base64
            with open(image_path, "rb") as f:
                image_data = f.read()
                image_base64 = base64.b64encode(image_data).decode("utf-8")
                mime_type = "image/png"
                if image_path.lower().endswith(".jpg") or image_path.lower().endswith(".jpeg"):
                    mime_type = "image/jpeg"
                elif image_path.lower().endswith(".webp"):
                    mime_type = "image/webp"

                data_url = f"data:{mime_type};base64,{image_base64}"

                logger.info(
                    f"Image sampled: prompt='{prompt[:50]}...', "
                    f"sampler={sampler}, image_id={image_id}"
                )

                return {
                    "image": data_url,
                    "image_id": image_id,
                    "sampler": sampler,
                    "prompt": prompt,
                }

        except HTTPException:
            raise
        except ImportError as exc:
            # Image generation not available
            logger.warning(
                "Image generation engine not available: %s",
                exc,
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    "Image generation engine not available. "
                    "Install image engine dependencies and enable image generation routes."
                ),
            ) from exc
        except Exception as e:
            logger.error(
                f"Image generation failed: {e}",
                exc_info=True,
            )
            raise HTTPException(
                status_code=502,
                detail=("Image generation failed. " "Check the image engine logs for details."),
            ) from e

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Image sampling failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Image sampling failed: {e!s}") from e


@router.get("/samplers")
async def list_samplers() -> dict:
    """List all supported image samplers."""
    return {
        "samplers": SUPPORTED_SAMPLERS,
        "count": len(SUPPORTED_SAMPLERS),
        "default": "ddim",
    }


@router.get("/samplers/{sampler_name}")
async def get_sampler_info(sampler_name: str) -> dict:
    """Get information about a specific sampler."""
    sampler = sampler_name.lower()

    if sampler not in SUPPORTED_SAMPLERS:
        raise HTTPException(
            status_code=404,
            detail=f"Sampler '{sampler_name}' not found",
        )

    # Sampler descriptions
    descriptions = {
        "ddim": "Denoising Diffusion Implicit Models - Fast, deterministic",
        "ddpm": "Denoising Diffusion Probabilistic Models - Original method",
        "euler": "Euler method - Fast, good quality",
        "euler_a": "Euler Ancestral - Stochastic variant",
        "heun": "Heun's method - More accurate than Euler",
        "dpm_2": "DPM-Solver-2 - Fast and accurate",
        "dpm_2_a": "DPM-Solver-2 Ancestral - Stochastic variant",
        "lms": "Linear Multi-Step - Stable, good quality",
        "plms": "Pseudo Linear Multi-Step - Fast variant",
        "dpm_fast": "DPM Fast - Very fast, lower quality",
        "dpm_adaptive": "DPM Adaptive - Adaptive step size",
        "dpmpp_2s_a": "DPM++ 2S Ancestral - High quality",
        "dpmpp_2m": "DPM++ 2M - Fast and high quality",
        "dpmpp_sde": "DPM++ SDE - Stochastic Differential Equation",
        "uni_pc": "UniPC - Unified Predictor-Corrector",
    }

    return {
        "name": sampler,
        "description": descriptions.get(sampler, "No description available"),
        "type": "deterministic" if "a" not in sampler else "stochastic",
    }
