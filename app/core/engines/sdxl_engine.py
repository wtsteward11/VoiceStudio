"""
SDXL Engine for VoiceStudio
Stable Diffusion XL standalone integration using diffusers

SDXL (Stable Diffusion XL) is a high-resolution image generation model
that produces 1024x1024+ images with high quality.

Compatible with:
- Python 3.10+
- diffusers library
- PyTorch 2.0+
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import time
from collections import OrderedDict
from pathlib import Path
from typing import Any

import torch
from PIL import Image

logger = logging.getLogger(__name__)

# Try to import diffusers
try:
    from diffusers import StableDiffusionXLImg2ImgPipeline, StableDiffusionXLPipeline

    HAS_DIFFUSERS = True
except ImportError:
    HAS_DIFFUSERS = False
    logger.warning("diffusers not installed. " "Install with: pip install diffusers>=0.21.0")

# Import base protocol from canonical source
from .base import EngineProtocol


class SDXLEngine(EngineProtocol):
    """
    SDXL Engine for high-resolution image generation.

    Supports:
    - Text-to-image generation (1024x1024+)
    - Image-to-image transformation
    - Inpainting
    - Refiner support for higher quality
    - Multiple resolution presets
    """

    SUPPORTED_FORMATS = ["png", "jpg", "webp"]
    RESOLUTION_PRESETS = {
        "1024x1024": (1024, 1024),
        "1152x896": (1152, 896),
        "896x1152": (896, 1152),
        "1216x832": (1216, 832),
        "832x1216": (832, 1216),
        "1344x768": (1344, 768),
        "768x1344": (768, 1344),
        "1536x640": (1536, 640),
        "640x1536": (640, 1536),
    }

    # Class-level model cache (shared across instances)
    _model_cache: OrderedDict[str, Any] = OrderedDict()
    _max_cache_size = 4  # Cache up to 4 models (increased from 2)

    def __init__(
        self,
        model_id: str = ("stabilityai/stable-diffusion-xl-base-1.0"),
        refiner_id: str | None = ("stabilityai/stable-diffusion-xl-refiner-1.0"),
        use_refiner: bool = True,
        resolution: str = "1024x1024",
        device: str | None = None,
        gpu: bool = True,
        lazy_load: bool = True,
        enable_model_cache: bool = True,
        batch_size: int = 4,  # Increased default batch size
        enable_response_cache: bool = True,
        response_cache_size: int = 100,
    ):
        """
        Initialize SDXL engine.

        Args:
            model_id: Model identifier for base SDXL model
            refiner_id: Model identifier for refiner model
            use_refiner: Whether to use refiner for higher quality
            resolution: Resolution preset
            device: Device to use (cuda/cpu)
            gpu: Whether to use GPU if available
            lazy_load: Load models only when needed (default: True)
            enable_model_cache: Enable LRU model cache (default: True)
            batch_size: Default batch size for batch processing
            enable_response_cache: Enable LRU response cache (default: True)
            response_cache_size: Maximum response cache size
        """
        super().__init__(device=device, gpu=gpu)

        if not HAS_DIFFUSERS:
            raise ImportError(
                "diffusers library not installed. " "Install with: pip install diffusers>=0.21.0"
            )

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

        self.model_id = model_id
        self.refiner_id = refiner_id
        self.use_refiner = use_refiner
        self.resolution = resolution
        self.lazy_load = lazy_load
        self.enable_model_cache = enable_model_cache
        self.batch_size = batch_size
        self.enable_response_cache = enable_response_cache
        self.response_cache_size = response_cache_size

        self.pipe: Any = None
        self.refiner: Any = None
        self._model_key: str | None = None

        # LRU response cache for generated images
        self._response_cache: OrderedDict[str, Image.Image] = OrderedDict()
        self._cache_stats = {"hits": 0, "misses": 0}

    def _get_model_key(self) -> str:
        """Generate cache key for model."""
        key_data = {
            "model_id": self.model_id,
            "refiner_id": self.refiner_id,
            "use_refiner": self.use_refiner,
            "device": self.device,
        }
        key_str = json.dumps(key_data, sort_keys=True)
        return hashlib.sha256(key_str.encode()).hexdigest()

    def _load_model_from_cache(self) -> bool:
        """Load model from cache if available."""
        if not self.enable_model_cache:
            return False

        self._model_key = self._get_model_key()
        if self._model_key in self._model_cache:
            cached_data = self._model_cache[self._model_key]
            self.pipe = cached_data.get("pipe")
            self.refiner = cached_data.get("refiner")
            # LRU update
            self._model_cache.move_to_end(self._model_key)
            logger.debug("Loaded SDXL model from cache")
            return True
        return False

    def _save_model_to_cache(self) -> None:
        """Save model to cache."""
        if not self.enable_model_cache or self._model_key is None:
            return

        # Evict oldest if cache full
        if len(self._model_cache) >= self._max_cache_size:
            oldest_key, oldest_data = self._model_cache.popitem(last=False)
            # Cleanup evicted model
            if "pipe" in oldest_data:
                del oldest_data["pipe"]
            if "refiner" in oldest_data:
                del oldest_data["refiner"]
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted SDXL model from cache: {oldest_key[:8]}")

        self._model_cache[self._model_key] = {
            "pipe": self.pipe,
            "refiner": self.refiner,
        }
        # LRU update
        self._model_cache.move_to_end(self._model_key)

    def initialize(self) -> bool:
        """Initialize the SDXL model."""
        try:
            if self._initialized:
                return True

            # Try to load from cache first
            if self._load_model_from_cache():
                self._initialized = True
                logger.info("SDXL engine initialized from cache")
                return True

            logger.info(f"Loading SDXL model: {self.model_id} " f"(device: {self.device})")

            # Use model cache directory if available
            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                    "sdxl",
                )
            os.makedirs(model_cache_dir, exist_ok=True)

            # Load base pipeline
            try:
                self.pipe = StableDiffusionXLPipeline.from_pretrained(
                    self.model_id,
                    torch_dtype=(torch.float16 if self.device == "cuda" else torch.float32),
                    cache_dir=model_cache_dir,
                )
                self.pipe = self.pipe.to(self.device)
                logger.info("SDXL base model loaded")
            except Exception as e:
                logger.error(f"Failed to load SDXL base model: {e}")
                return False

            # Load refiner if requested
            if self.use_refiner and self.refiner_id:
                try:
                    self.refiner = StableDiffusionXLImg2ImgPipeline.from_pretrained(
                        self.refiner_id,
                        torch_dtype=(torch.float16 if self.device == "cuda" else torch.float32),
                        cache_dir=model_cache_dir,
                    )
                    self.refiner = self.refiner.to(self.device)
                    logger.info("SDXL refiner model loaded")
                except Exception as e:
                    logger.warning(f"Failed to load SDXL refiner: {e}")
                    self.refiner = None

            # Save to cache
            self._model_key = self._get_model_key()
            self._save_model_to_cache()

            self._initialized = True
            logger.info("SDXL engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize SDXL engine: {e}")
            self._initialized = False
            return False

    def _generate_cache_key(
        self,
        prompt: str,
        negative_prompt: str,
        width: int,
        height: int,
        steps: int,
        cfg_scale: float,
        seed: int | None,
        **kwargs: Any,
    ) -> str:
        """Generate cache key from generation parameters."""
        cache_data = {
            "prompt": prompt,
            "negative_prompt": negative_prompt,
            "width": width,
            "height": height,
            "steps": steps,
            "cfg_scale": cfg_scale,
            "seed": seed if seed is not None else -1,
            "use_refiner": self.use_refiner,
            "kwargs": {k: v for k, v in kwargs.items() if k != "image"},
        }
        cache_str = json.dumps(cache_data, sort_keys=True)
        return hashlib.sha256(cache_str.encode()).hexdigest()

    def generate(
        self,
        prompt: str,
        negative_prompt: str = "",
        width: int | None = None,
        height: int | None = None,
        steps: int = 50,
        cfg_scale: float = 7.0,
        sampler: str | None = None,
        seed: int | None = None,
        output_path: str | Path | None = None,
        **kwargs: Any,
    ) -> Image.Image | None | tuple[Image.Image | None, dict[str, Any]]:
        """Generate image using SDXL."""
        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return None

        # Record start time for metrics
        start_time = time.perf_counter()

        # Get resolution
        if width is None or height is None:
            if self.resolution in self.RESOLUTION_PRESETS:
                width, height = self.RESOLUTION_PRESETS[self.resolution]
            else:
                width, height = 1024, 1024

        # Check response cache
        cache_key = None
        if self.enable_response_cache:
            cache_key = self._generate_cache_key(
                prompt,
                negative_prompt,
                width,
                height,
                steps,
                cfg_scale,
                seed,
                **kwargs,
            )
            if cache_key in self._response_cache:
                cached_image = self._response_cache[cache_key]
                # LRU update
                self._response_cache.move_to_end(cache_key)
                self._cache_stats["hits"] += 1
                logger.debug("Using cached SDXL generation result")
                if output_path:
                    cached_image.save(output_path)
                # Record metrics
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    duration = time.perf_counter() - start_time
                    metrics.record_synthesis_time("sdxl", duration, cached=True)
                except Exception:
                    ...
                return cached_image
            else:
                self._cache_stats["misses"] += 1

        try:
            # Set seed
            generator = None
            if seed is not None:
                generator = torch.Generator(device=self.device).manual_seed(seed)

            # Generate with base model
            # Use enable_model_cpu_offload for better GPU memory management
            if self.device == "cuda" and hasattr(self.pipe, "enable_model_cpu_offload"):
                self.pipe.enable_model_cpu_offload()

            images = self.pipe(
                prompt=prompt,
                negative_prompt=negative_prompt,
                width=width,
                height=height,
                num_inference_steps=steps,
                guidance_scale=cfg_scale,
                generator=generator,
                **kwargs,
            ).images

            # Clear GPU cache after generation
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            if not images:
                logger.error("No images generated")
                return None

            image: Image.Image = images[0]

            # Apply refiner if available
            if self.use_refiner and self.refiner is not None:
                try:
                    # Use enable_model_cpu_offload for better GPU memory
                    if self.device == "cuda" and hasattr(self.refiner, "enable_model_cpu_offload"):
                        self.refiner.enable_model_cpu_offload()

                    refined_images = self.refiner(
                        prompt=prompt,
                        negative_prompt=negative_prompt,
                        image=image,
                        num_inference_steps=kwargs.get("refiner_steps", 20),
                        strength=kwargs.get("refiner_strength", 0.3),
                        guidance_scale=cfg_scale,
                        generator=generator,
                    ).images
                    if refined_images:
                        image = refined_images[0]

                    # Clear GPU cache after refiner
                    if self.device == "cuda" and torch.cuda.is_available():
                        torch.cuda.empty_cache()
                except Exception as e:
                    logger.warning(f"Refiner failed: {e}, using base image")

            # Cache result
            if self.enable_response_cache and cache_key is not None:
                # Evict oldest if cache full
                if len(self._response_cache) >= self.response_cache_size:
                    self._response_cache.popitem(last=False)
                self._response_cache[cache_key] = image.copy()
                # LRU update
                self._response_cache.move_to_end(cache_key)

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("sdxl", duration, cached=False)
            except Exception:
                ...

            if output_path:
                image.save(output_path)
                logger.info(f"Image saved to: {output_path}")
                return image

            return image

        except Exception as e:
            logger.error(f"SDXL generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("sdxl", "generation_error")
            except Exception:
                ...
            return None

    def batch_generate(
        self,
        prompts: list[str],
        negative_prompt: str = "",
        width: int | None = None,
        height: int | None = None,
        steps: int = 50,
        cfg_scale: float = 7.0,
        sampler: str | None = None,
        seeds: list[int | None] | None = None,
        output_paths: list[str | Path | None] | None = None,
        batch_size: int | None = None,
        **kwargs: Any,
    ) -> list[Image.Image | None]:
        """
        Generate multiple images using batch processing.

        Args:
            prompts: List of text prompts for image generation
            negative_prompt: Negative prompt (applied to all)
            width: Image width
            height: Image height
            steps: Number of sampling steps
            cfg_scale: Classifier-free guidance scale
            sampler: Sampling method
            seeds: List of random seeds (None for random)
            output_paths: Optional list of paths to save output images
            batch_size: Batch size for processing (uses pipeline batch)
            **kwargs: Additional generation parameters

        Returns:
            List of PIL Images or None for failed generations
        """
        if not prompts:
            return []

        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return [None] * len(prompts)

        try:
            # Get resolution
            if width is None or height is None:
                if self.resolution in self.RESOLUTION_PRESETS:
                    width, height = self.RESOLUTION_PRESETS[self.resolution]
                else:
                    width, height = 1024, 1024

            actual_batch_size = batch_size if batch_size is not None else self.batch_size

            # Record start time for metrics
            start_time = time.perf_counter()

            # Process prompts in batches with ThreadPoolExecutor for better
            # parallelization
            all_images = []
            for i in range(0, len(prompts), actual_batch_size):
                batch_prompts = prompts[i : i + actual_batch_size]
                batch_seeds = (
                    seeds[i : i + actual_batch_size] if seeds else [None] * len(batch_prompts)
                )

                # Set generators for batch
                generators = []
                for seed in batch_seeds:
                    if seed is not None:
                        gen = torch.Generator(device=self.device).manual_seed(seed)
                    else:
                        gen = None
                    generators.append(gen)

                # Use enable_model_cpu_offload for better GPU memory
                if self.device == "cuda" and hasattr(self.pipe, "enable_model_cpu_offload"):
                    self.pipe.enable_model_cpu_offload()

                # Generate batch
                batch_images = self.pipe(
                    prompt=batch_prompts,
                    negative_prompt=[negative_prompt] * len(batch_prompts),
                    width=width,
                    height=height,
                    num_inference_steps=steps,
                    guidance_scale=cfg_scale,
                    generator=generators[0] if generators else None,
                    **kwargs,
                ).images

                all_images.extend(batch_images)

                # Clear GPU cache after batch
                if self.device == "cuda" and torch.cuda.is_available():
                    torch.cuda.empty_cache()

            # Apply refiner if available
            if self.use_refiner and self.refiner is not None:
                try:
                    if self.device == "cuda" and hasattr(self.refiner, "enable_model_cpu_offload"):
                        self.refiner.enable_model_cpu_offload()

                    refined_images = []
                    for image in all_images:
                        refined = self.refiner(
                            prompt=prompts[all_images.index(image)],
                            negative_prompt=negative_prompt,
                            image=image,
                            num_inference_steps=kwargs.get("refiner_steps", 20),
                            strength=kwargs.get("refiner_strength", 0.3),
                            guidance_scale=cfg_scale,
                        ).images
                        if refined:
                            refined_images.append(refined[0])
                        else:
                            refined_images.append(image)

                    all_images = refined_images

                    # Clear GPU cache after refiner
                    if self.device == "cuda" and torch.cuda.is_available():
                        torch.cuda.empty_cache()
                except Exception as e:
                    logger.warning(f"Refiner failed: {e}, using base images")

            # Save images if paths provided
            if output_paths:
                for image, path in zip(all_images, output_paths):
                    if path and image:
                        image.save(path)
                        logger.info(f"Image saved to: {path}")

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("sdxl", duration, cached=False)
            except Exception:
                ...

            return all_images

        except Exception as e:
            logger.error(f"SDXL batch generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("sdxl", "batch_generation_error")
            except Exception:
                ...
            return [None] * len(prompts)

    def cleanup(self) -> None:
        """Clean up resources (enhanced)."""
        try:
            # Don't delete if in cache (other instances might be using it)
            if not self.enable_model_cache or (
                self._model_key is not None and self._model_key not in self._model_cache
            ):
                if self.pipe is not None:
                    del self.pipe
                if self.refiner is not None:
                    del self.refiner

            # Clear response cache
            if self.enable_response_cache:
                self._response_cache.clear()
                self._cache_stats = {"hits": 0, "misses": 0}

            # Clear GPU cache
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("SDXL engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    @classmethod
    def clear_model_cache(cls) -> None:
        """Clear the shared model cache."""
        for _key, data in cls._model_cache.items():
            if "pipe" in data:
                del data["pipe"]
            if "refiner" in data:
                del data["refiner"]
        cls._model_cache.clear()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        logger.info("SDXL model cache cleared")

    def get_cache_stats(self) -> dict[str, int | float | str | bool]:
        """Get cache statistics (enhanced)."""
        if not self.enable_response_cache:
            return {"enabled": False}

        total_requests = self._cache_stats["hits"] + self._cache_stats["misses"]
        hit_rate = (self._cache_stats["hits"] / total_requests * 100) if total_requests > 0 else 0.0

        return {
            "enabled": True,
            "size": len(self._response_cache),
            "max_size": self.response_cache_size,
            "cache_hits": self._cache_stats["hits"],
            "cache_misses": self._cache_stats["misses"],
            "hit_rate": f"{hit_rate:.2f}%",
        }

    def clear_response_cache(self) -> None:
        """Clear the response cache."""
        if self.enable_response_cache:
            self._response_cache.clear()
            self._cache_stats = {"hits": 0, "misses": 0}
            logger.info("SDXL response cache cleared")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "model_id": self.model_id,
                "refiner_id": self.refiner_id,
                "use_refiner": self.use_refiner,
                "resolution": self.resolution,
                "supported_formats": self.SUPPORTED_FORMATS,
                "lazy_load": self.lazy_load,
                "model_cache_enabled": self.enable_model_cache,
                "model_cache_size": len(self._model_cache),
                "batch_size": self.batch_size,
                "response_cache_enabled": self.enable_response_cache,
                "response_cache_size": cache_stats.get("size", 0),
                "response_cache_max_size": cache_stats.get("max_size", 0),
                "response_cache_hits": cache_stats.get("cache_hits", 0),
                "response_cache_misses": cache_stats.get("cache_misses", 0),
                "response_cache_hit_rate": cache_stats.get("hit_rate", "N/A"),
            }
        )
        return info


def create_sdxl_engine(
    model_id: str = "stabilityai/stable-diffusion-xl-base-1.0",
    refiner_id: str | None = "stabilityai/stable-diffusion-xl-refiner-1.0",
    use_refiner: bool = True,
    resolution: str = "1024x1024",
    device: str | None = None,
    gpu: bool = True,
) -> SDXLEngine:
    """Factory function to create an SDXL engine instance."""
    return SDXLEngine(
        model_id=model_id,
        refiner_id=refiner_id,
        use_refiner=use_refiner,
        resolution=resolution,
        device=device,
        gpu=gpu,
    )
