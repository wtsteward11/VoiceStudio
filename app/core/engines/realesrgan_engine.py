"""
Real-ESRGAN Engine for VoiceStudio
Image/video upscaling integration

Real-ESRGAN is a practical image restoration and upscaling tool
that can upscale images and videos with high quality.

Compatible with:
- Python 3.10+
- realesrgan library
- basicsr library
- PyTorch 2.0+
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import numpy as np
import torch
from PIL import Image

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache: Any = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    logger.debug("General model cache not available, using RealESRGAN-specific cache")

# Fallback: RealESRGAN-specific cache (for backward compatibility)
_REALESRGAN_MODEL_CACHE: OrderedDict[str, Any] = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_name: str, scale: int, device: str) -> str:
    """Generate cache key for RealESRGAN model."""
    return f"realesrgan::{model_name}::{scale}::{device}"


def _get_cached_realesrgan_model(model_name: str, scale: int, device: str) -> Any:
    """Get cached RealESRGAN model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("realesrgan", f"{model_name}_{scale}", device=device)
        if cached is not None:
            return cached

    # Fallback to RealESRGAN-specific cache
    cache_key = _get_cache_key(model_name, scale, device)
    if cache_key in _REALESRGAN_MODEL_CACHE:
        _REALESRGAN_MODEL_CACHE.move_to_end(cache_key)
        return _REALESRGAN_MODEL_CACHE[cache_key]
    return None


def _cache_realesrgan_model(model_name: str, scale: int, device: str, upsampler: Any) -> None:
    """Cache RealESRGAN model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("realesrgan", f"{model_name}_{scale}", upsampler, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to RealESRGAN-specific cache
    cache_key = _get_cache_key(model_name, scale, device)

    if cache_key in _REALESRGAN_MODEL_CACHE:
        _REALESRGAN_MODEL_CACHE.move_to_end(cache_key)
        return

    _REALESRGAN_MODEL_CACHE[cache_key] = upsampler

    # Evict oldest if cache full
    if len(_REALESRGAN_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_upsampler = _REALESRGAN_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_upsampler
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted RealESRGAN model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting RealESRGAN model from cache: {e}")

    logger.debug(
        f"Cached RealESRGAN model: {cache_key} (cache size: {len(_REALESRGAN_MODEL_CACHE)})"
    )


try:
    from basicsr.archs.rrdbnet_arch import RRDBNet
    from realesrgan import RealESRGANer

    HAS_REALESRGAN = True
except ImportError:
    HAS_REALESRGAN = False
    logger.warning(
        "realesrgan not installed. Install with: pip install realesrgan>=0.3.0 basicsr>=1.4.2"
    )

# Import base protocol from canonical source
from .base import EngineProtocol


class RealESRGANEngine(EngineProtocol):
    """
    Real-ESRGAN Engine for image and video upscaling.

    Supports:
    - Image upscaling (2x, 4x)
    - Face enhancement
    - Anime upscaling
    - General restoration
    """

    SUPPORTED_FORMATS = ["png", "jpg", "webp"]

    @classmethod
    def get_model_options(cls) -> dict[str, Any]:
        """Get model options, only available when realesrgan is installed."""
        if not HAS_REALESRGAN:
            return {}
        return {
            "RealESRGAN_x4plus": {"scale": 4, "model": RRDBNet},
            "RealESRGAN_x4plus_anime": {"scale": 4, "model": RRDBNet},
            "RealESRNet_x4plus": {"scale": 4, "model": RRDBNet},
        }

    def __init__(
        self,
        model_name: str = "RealESRGAN_x4plus",
        scale: int = 4,
        device: str | None = None,
        gpu: bool = True,
    ):
        """Initialize Real-ESRGAN engine."""
        super().__init__(device=device, gpu=gpu)

        if not HAS_REALESRGAN:
            raise ImportError(
                "realesrgan library not installed. Install with: pip install realesrgan>=0.3.0"
            )

        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

        self.model_name = model_name
        self.scale = scale
        self.upsampler: Any = None
        self.lazy_load = True
        self.batch_size = 2
        self._caching_enabled = True

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_upsampler = _get_cached_realesrgan_model(
                self.model_name, self.scale, self.device
            )
            if cached_upsampler is not None:
                logger.debug(
                    f"Using cached RealESRGAN model: {self.model_name} (scale: {self.scale}x)"
                )
                self.upsampler = cached_upsampler
                self._initialized = True
                return True

        logger.info(
            f"Loading Real-ESRGAN model: {self.model_name} (device: {self.device}, scale: {self.scale}x)"
        )

        model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
        if not model_cache_dir:
            model_cache_dir = os.path.join(
                os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                "VoiceStudio",
                "models",
                "realesrgan",
            )
        os.makedirs(model_cache_dir, exist_ok=True)

        try:
            # Determine model architecture
            model_options = self.get_model_options()
            default_model = "RealESRGAN_x4plus"
            model_info = model_options.get(self.model_name, model_options.get(default_model))
            if not model_info:
                logger.error(
                    "Model options not available - realesrgan may not be properly installed"
                )
                return False
            model = model_info["model"](
                num_in_ch=3,
                num_out_ch=3,
                num_feat=64,
                num_block=23,
                num_grow_ch=32,
                scale=self.scale,
            )

            # Model path
            model_file = os.path.join(model_cache_dir, f"{self.model_name}.pth")
            model_path: str | None = model_file

            # Download model if not exists
            if not os.path.exists(model_file):
                logger.warning(
                    f"Model not found at {model_file}, Real-ESRGAN will download it on first use"
                )
                model_path = None

            self.upsampler = RealESRGANer(
                scale=self.scale,
                model_path=model_path,
                model=model,
                tile=0,  # Tile size for large images (0 = no tiling)
                tile_pad=10,
                pre_pad=0,
                half=self.device == "cuda",  # Use half precision on GPU
                gpu_id=0 if self.device == "cuda" else None,
            )

            # Cache upsampler
            if self._caching_enabled:
                _cache_realesrgan_model(self.model_name, self.scale, self.device, self.upsampler)

            self._initialized = True
            logger.info("Real-ESRGAN engine initialized successfully")
            return True
        except Exception as e:
            logger.error(f"Failed to load Real-ESRGAN model: {e}")
            return False

    def initialize(self) -> bool:
        """Initialize the Real-ESRGAN model."""
        try:
            if self._initialized:
                return True

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
                return True

            return self._load_model()

        except Exception as e:
            logger.error(f"Failed to initialize Real-ESRGAN engine: {e}")
            self._initialized = False
            return False

    def upscale(
        self,
        image: str | Path | Image.Image,
        output_path: str | Path | None = None,
        **kwargs: Any,
    ) -> Image.Image | None | tuple[Image.Image | None, dict[str, Any]]:
        """
        Upscale image using Real-ESRGAN.

        Args:
            image: Input image (path or PIL Image)
            output_path: Optional path to save upscaled image
            **kwargs: Additional parameters

        Returns:
            Upscaled PIL Image or None if upscaling failed
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return None

        try:
            # Load image
            if isinstance(image, (str, Path)):
                img = Image.open(image).convert("RGB")
            else:
                img = image.convert("RGB")

            # Convert to numpy array
            img_array = np.array(img)

            # Upscale with inference mode for better performance
            with torch.inference_mode():  # Faster than no_grad
                output, _ = self.upsampler.enhance(img_array, outscale=self.scale)

            # Convert back to PIL Image
            upscaled = Image.fromarray(output)

            if output_path:
                upscaled.save(output_path)
                logger.info(f"Upscaled image saved to: {output_path}")
                return upscaled

            return upscaled

        except Exception as e:
            logger.error(f"Real-ESRGAN upscaling failed: {e}")
            return None

    def generate(
        self, prompt: str = "", **kwargs: Any
    ) -> Image.Image | None | tuple[Image.Image | None, dict[str, Any]]:
        """
        Alias for upscale() to maintain compatibility with generate() interface.
        For Real-ESRGAN, pass image in kwargs.
        """
        if "image" in kwargs:
            return self.upscale(
                kwargs["image"], **{k: v for k, v in kwargs.items() if k != "image"}
            )
        else:
            logger.error(
                "Real-ESRGAN requires an input image. Use upscale() method or pass 'image' in kwargs."
            )
            return None

    def batch_upscale(
        self,
        images: list[str | Path | Image.Image],
        output_dir: str | Path | None = None,
        batch_size: int = 2,
        **kwargs: Any,
    ) -> list[Image.Image | None]:
        """
        Upscale multiple images in batch with optimized processing.

        Args:
            images: List of input images (paths or PIL Images)
            output_dir: Optional directory to save upscaled images
            batch_size: Number of images to process in a single batch
            **kwargs: Additional parameters

        Returns:
            List of upscaled PIL Images or None on error
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [None] * len(images)

        results: list[Any] = []

        # Process in batches for better GPU utilization
        actual_batch_size = min(batch_size, self.batch_size)

        def upscale_single(
            image: str | Path | Image.Image,
        ) -> Image.Image | tuple[Image.Image | None, dict[str, Any]] | None:
            try:
                return self.upscale(image=image, **kwargs)
            except Exception as e:
                logger.error(f"Batch upscaling failed for image: {e}")
                return None

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            batch_results = list(executor.map(upscale_single, images))

        # Handle output directory if provided
        if output_dir:
            output_dir = Path(output_dir)
            output_dir.mkdir(parents=True, exist_ok=True)
            for i, result in enumerate(batch_results):
                if result is not None:
                    output_path = output_dir / f"upscaled_{i:04d}.png"
                    if isinstance(result, tuple):
                        upscaled, _ = result
                        if upscaled is not None:
                            upscaled.save(output_path)
                            results.append(None)  # Return None when saving to file
                        else:
                            results.append(None)
                    else:
                        if result is not None:
                            result.save(output_path)
                            results.append(None)  # Return None when saving to file
                        else:
                            results.append(None)
                else:
                    results.append(None)
        else:
            results = batch_results

        # Clear GPU cache periodically
        if torch.cuda.is_available() and (len(images) % (actual_batch_size * 2) == 0):
            torch.cuda.empty_cache()

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int) -> None:
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }

    def cleanup(self) -> None:
        """Clean up resources."""
        try:
            # Don't delete cached models, just clear references
            self.upsampler = None

            # Clear CUDA cache if using GPU
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("Real-ESRGAN engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model_name": self.model_name,
                "scale": self.scale,
                "supported_formats": self.SUPPORTED_FORMATS,
            }
        )
        return info


def create_realesrgan_engine(
    model_name: str = "RealESRGAN_x4plus",
    scale: int = 4,
    device: str | None = None,
    gpu: bool = True,
) -> RealESRGANEngine:
    """Factory function to create a Real-ESRGAN engine instance."""
    return RealESRGANEngine(model_name=model_name, scale=scale, device=device, gpu=gpu)
