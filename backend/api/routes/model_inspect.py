"""
Model Inspection Routes

Endpoints for inspecting model internals, layers, and activations.
"""

from __future__ import annotations

import base64
import logging

import numpy as np
from fastapi import APIRouter, HTTPException

from ..models_additional import ModelInspectRequest

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/model", tags=["model", "inspect"])


@router.post("/inspect")
async def inspect(req: ModelInspectRequest) -> dict:
    """
    Inspect model layer activations and internals.

    Provides insights into model behavior by inspecting
    specific layers and their activations.

    Args:
        req: Request with layer number to inspect

    Returns:
        Dictionary with layer information and activation maps
    """
    try:
        layer = req.layer

        if layer < 0:
            raise HTTPException(status_code=400, detail="Layer number must be non-negative")

        # Try to get model information from engine router
        try:
            import os

            # Try to get model cache to inspect models (app on path via main.py bootstrap)
            from backend.services.model_facade import get_model_cache

            model_cache = get_model_cache()
            cache_stats = model_cache.get_stats()

            # Get cached models
            cached_models = cache_stats.get("cached_models", [])

            if not cached_models:
                logger.warning("No models cached for inspection")
                return _build_inspection_status(
                    layer=layer,
                    model_name="unknown",
                    model_type="unknown",
                    total_layers=0,
                    heads=0,
                    model_available=False,
                    activations_available=False,
                    message=(
                        "No cached models available for inspection. "
                        "Load a model via an engine first."
                    ),
                )

            # Get first cached model for inspection
            model_info = cached_models[0] if cached_models else None

            if model_info:
                # Generate inspection data based on model info
                model_name = model_info.get("name", "unknown")
                model_type = model_info.get("type", "unknown")

                # Estimate layer count based on model type
                if "xtts" in model_name.lower() or "tts" in model_type.lower():
                    total_layers = 24  # Typical TTS model
                    heads = 8  # Attention heads
                elif "whisper" in model_name.lower():
                    total_layers = 12  # Whisper base model
                    heads = 6
                elif "transformer" in model_type.lower():
                    total_layers = 12  # Default transformer
                    heads = 12
                else:
                    total_layers = 12
                    heads = 12

                # Validate layer number
                if layer >= total_layers:
                    raise HTTPException(
                        status_code=400,
                        detail=(
                            f"Layer {layer} out of range. "
                            f"Model has {total_layers} layers (0-{total_layers-1})"
                        ),
                    )

                # Try to extract real activations from model if available
                activation_map = None
                activation_reason = None
                try:
                    # Try to get actual model instance from cache
                    if hasattr(model_cache, "get") and model_info.get("cache_key"):
                        cached_model = model_cache.get(
                            model_info.get("engine", "unknown"),
                            model_info.get("cache_key"),
                            device=model_info.get("device", "cpu"),
                        )

                        if cached_model is None:
                            activation_reason = "Model instance is not loaded in the cache."
                        else:
                            # Try to extract activations from PyTorch model
                            try:
                                import torch

                                if isinstance(cached_model, torch.nn.Module):
                                    # Create dummy input for activation extraction
                                    # For TTS models, use text/audio features
                                    # For now, create a simple feature map
                                    with torch.no_grad():
                                        # Try to access layer activations via hooks
                                        activations = []

                                        def hook_fn(module, input, output):
                                            activations.append(output.detach().cpu().numpy())

                                        modules_list = list(cached_model.modules())
                                        if not modules_list:
                                            activation_reason = (
                                                "Model has no modules available for inspection."
                                            )
                                        else:
                                            # Register hook on specified layer
                                            layer_idx = min(layer, len(modules_list) - 1)
                                            if layer_idx < len(modules_list):
                                                target_layer = modules_list[layer_idx]
                                                handle = target_layer.register_forward_hook(hook_fn)

                                                # Forward pass with dummy input
                                                try:
                                                    # Create appropriate dummy input based on model type
                                                    if (
                                                        "tts" in model_type.lower()
                                                        or "xtts" in model_name.lower()
                                                    ):
                                                        # TTS model: text tokens + speaker embedding
                                                        dummy_input = torch.randint(
                                                            0, 1000, (1, 10)
                                                        ).to(model_info.get("device", "cpu"))
                                                    else:
                                                        # Generic: use model's expected input shape if available
                                                        dummy_input = torch.randn(1, 64).to(
                                                            model_info.get("device", "cpu")
                                                        )

                                                    _ = cached_model(dummy_input)
                                                    handle.remove()

                                                    if activations:
                                                        # Use first activation map
                                                        act_data = activations[0]
                                                        # Reshape to 2D for visualization
                                                        if len(act_data.shape) > 2:
                                                            act_data = act_data.reshape(
                                                                -1, act_data.shape[-1]
                                                            )
                                                        if len(act_data.shape) == 2:
                                                            # Average over batch dimension if present
                                                            if act_data.shape[0] > 1:
                                                                act_data = np.mean(act_data, axis=0)
                                                            # Resize to 64x64 for visualization
                                                            try:
                                                                from scipy.ndimage import zoom

                                                                target_size = 64
                                                                current_size = act_data.shape[0]
                                                                zoom_factor = (
                                                                    target_size / current_size
                                                                )
                                                                act_data = zoom(
                                                                    act_data, zoom_factor
                                                                )
                                                            except ImportError:
                                                                # Fallback: simple interpolation using numpy
                                                                target_size = 64
                                                                current_size = act_data.shape[0]
                                                                indices = np.linspace(
                                                                    0, current_size - 1, target_size
                                                                )
                                                                act_data = np.interp(
                                                                    indices,
                                                                    np.arange(current_size),
                                                                    act_data,
                                                                )
                                                            activation_map = act_data
                                                    else:
                                                        activation_reason = "No activation data was produced for the requested layer."
                                                except Exception as hook_error:
                                                    logger.debug(
                                                        f"Failed to extract activations via hook: {hook_error}"
                                                    )
                                                    handle.remove()
                                else:
                                    activation_reason = "Cached model is not a torch module."
                            except ImportError as torch_error:
                                activation_reason = "PyTorch is not available for model inspection."
                                logger.debug(
                                    "PyTorch not available for model inspection: %s",
                                    torch_error,
                                )
                            except Exception as extract_error:
                                activation_reason = (
                                    "Failed to extract activation data from the model."
                                )
                                logger.debug(
                                    "Failed to extract real activations: %s",
                                    extract_error,
                                )
                    else:
                        activation_reason = (
                            "Model cache does not expose a retrievable model instance."
                        )
                except Exception as e:
                    activation_reason = "Model activation extraction failed."
                    logger.debug(f"Model activation extraction failed: {e}")

                if activation_map is None:
                    if not activation_reason:
                        activation_reason = "Activation data is unavailable for the selected layer."
                    logger.warning(
                        "Activation data unavailable for model %s layer %s: %s",
                        model_name,
                        layer,
                        activation_reason,
                    )
                    return _build_inspection_status(
                        layer=layer,
                        model_name=model_name,
                        model_type=model_type,
                        total_layers=total_layers,
                        heads=heads,
                        model_available=True,
                        activations_available=False,
                        message=activation_reason,
                    )

                # Convert to uint8 for image
                activation_map = (activation_map * 255).astype(np.uint8)
                if activation_map.ndim == 1:
                    activation_map = np.tile(activation_map, (activation_map.shape[0], 1))
                activation_shape = list(activation_map.shape)

                # Convert to base64
                from io import BytesIO

                from PIL import Image

                img = Image.fromarray(activation_map, mode="L")
                buffer = BytesIO()
                img.save(buffer, format="PNG")
                image_data = buffer.getvalue()
                activation_base64 = base64.b64encode(image_data).decode("utf-8")

                logger.info(
                    f"Model inspection: model={model_name}, " f"layer={layer}, heads={heads}"
                )

                return {
                    "heads": heads,
                    "maps": f"data:image/png;base64,{activation_base64}",
                    "layer": layer,
                    "total_layers": total_layers,
                    "model_name": model_name,
                    "model_type": model_type,
                    "activation_shape": activation_shape,
                    "model_available": True,
                    "activations_available": True,
                }
            else:
                return _build_inspection_status(
                    layer=layer,
                    model_name="unknown",
                    model_type="unknown",
                    total_layers=0,
                    heads=0,
                    model_available=False,
                    activations_available=False,
                    message=(
                        "No cached models available for inspection. "
                        "Load a model via an engine first."
                    ),
                )

        except HTTPException:
            raise
        except ImportError as exc:
            logger.warning(
                "Model cache not available: %s",
                exc,
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    "Model cache not available. "
                    "Install backend engine dependencies and load a model to enable inspection."
                ),
            ) from exc
        except Exception as e:
            logger.error(f"Model inspection failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=500,
                detail="Model inspection failed. Check backend logs for details.",
            ) from e

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Model inspection failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Model inspection failed: {e!s}") from e


def _build_inspection_status(
    *,
    layer: int,
    message: str,
    model_available: bool,
    activations_available: bool,
    model_name: str = "unknown",
    model_type: str = "unknown",
    total_layers: int = 0,
    heads: int = 0,
) -> dict:
    return {
        "model_available": model_available,
        "activations_available": activations_available,
        "layer": layer,
        "total_layers": total_layers,
        "model_name": model_name,
        "model_type": model_type,
        "heads": heads,
        "maps": None,
        "activation_shape": None,
        "message": message,
    }


@router.get("/inspect/layers")
async def list_layers(
    model_name: str | None = None,
) -> dict:
    """
    List all layers in a model.

    Args:
        model_name: Optional model name to inspect

    Returns:
        Dictionary with layer information
    """
    try:
        # Try to get model information
        try:
            from backend.services.model_facade import get_model_cache

            model_cache = get_model_cache()
            cache_stats = model_cache.get_stats()
            cached_models = cache_stats.get("cached_models", [])

            if cached_models:
                model_info = (
                    next(
                        (m for m in cached_models if m.get("name") == model_name),
                        cached_models[0],
                    )
                    if model_name
                    else cached_models[0]
                )

                model_name_actual = model_info.get("name", "unknown")
                model_type = model_info.get("type", "unknown")

                # Estimate layer count
                if "xtts" in model_name_actual.lower():
                    total_layers = 24
                elif "whisper" in model_name_actual.lower():
                    total_layers = 12
                else:
                    total_layers = 12

                layers = [
                    {
                        "index": i,
                        "name": f"layer_{i}",
                        "type": "transformer" if i < total_layers - 2 else "output",
                    }
                    for i in range(total_layers)
                ]

                return {
                    "model_name": model_name_actual,
                    "model_type": model_type,
                    "layers": layers,
                    "total_layers": total_layers,
                }
            else:
                # No models available
                return {
                    "model_name": model_name or "unknown",
                    "layers": [],
                    "total_layers": 0,
                    "message": "No models available for inspection",
                }

        except ImportError:
            return {
                "model_name": model_name or "unknown",
                "layers": [],
                "total_layers": 0,
                "message": "Model cache not available",
            }

    except Exception as e:
        logger.error(f"Failed to list layers: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list layers: {e!s}") from e
