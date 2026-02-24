"""
FastSD CPU Engine for VoiceStudio
Fast CPU-optimized Stable Diffusion integration

FastSD CPU uses ONNX runtime for faster CPU inference.

Compatible with:
- Python 3.10+
- diffusers library with ONNX support
- onnxruntime
- PyTorch 2.0+ (CPU)
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import time
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import torch
from PIL import Image

logger = logging.getLogger(__name__)

HAS_DIFFUSERS = False
HAS_DIFFUSERS_ONNX = False

try:
    from diffusers import OnnxStableDiffusionPipeline

    HAS_DIFFUSERS_ONNX = True
except ImportError:
    HAS_DIFFUSERS_ONNX = False

try:
    from diffusers import StableDiffusionPipeline

    HAS_DIFFUSERS = True
except ImportError:
    HAS_DIFFUSERS = False
    if not HAS_DIFFUSERS_ONNX:
        logger.warning("diffusers not installed. Install with: pip install diffusers>=0.21.0")

try:
    import onnxruntime as ort

    HAS_ONNX = True
except ImportError:
    HAS_ONNX = False
    logger.warning("onnxruntime not installed. Install with: pip install onnxruntime>=1.15.0")

# Import base protocol from canonical source
from .base import EngineProtocol


class FastSDCPUEngine(EngineProtocol):
    """FastSD CPU Engine for fast CPU-optimized Stable Diffusion generation."""

    SUPPORTED_FORMATS = ["png", "jpg", "webp"]

    # Class-level model cache (shared across instances)
    _model_cache: OrderedDict[str, Any] = OrderedDict()
    _max_cache_size = 4  # Cache up to 4 models (increased from 2)

    def __init__(
        self,
        model_id: str = "runwayml/stable-diffusion-v1-5",
        use_onnx: bool = True,
        num_threads: int = 4,
        device: str | None = None,
        gpu: bool = False,  # Force CPU
        lazy_load: bool = True,
        enable_model_cache: bool = True,
        batch_size: int = 4,  # Increased default batch size
        enable_response_cache: bool = True,
        response_cache_size: int = 100,
    ):
        """Initialize FastSD CPU engine."""
        super().__init__(device="cpu", gpu=False)  # Force CPU

        if not HAS_DIFFUSERS and not HAS_DIFFUSERS_ONNX:
            raise ImportError("diffusers library not installed")

        self.model_id = model_id
        self.use_onnx = use_onnx and HAS_ONNX and HAS_DIFFUSERS_ONNX
        self.num_threads = num_threads
        self.lazy_load = lazy_load
        self.enable_model_cache = enable_model_cache
        self.batch_size = batch_size
        self.enable_response_cache = enable_response_cache
        self.response_cache_size = response_cache_size

        self.pipe: Any = None
        self._model_key: str | None = None

        # LRU response cache for generated images
        self._response_cache: OrderedDict[str, Image.Image] = OrderedDict()
        self._cache_stats = {"hits": 0, "misses": 0}

        # Configure ONNX runtime if available
        if self.use_onnx and HAS_ONNX:
            ort.set_default_logger_severity(3)  # Suppress warnings

    def _get_model_key(self) -> str:
        """Generate cache key for model."""
        key_data = {
            "model_id": self.model_id,
            "use_onnx": self.use_onnx,
            "num_threads": self.num_threads,
            "device": "cpu",
        }
        key_str = json.dumps(key_data, sort_keys=True)
        return hashlib.sha256(key_str.encode()).hexdigest()

    def _load_model_from_cache(self) -> bool:
        """Load model from cache if available."""
        if not self.enable_model_cache:
            return False

        self._model_key = self._get_model_key()
        if self._model_key in self._model_cache:
            self.pipe = self._model_cache[self._model_key]
            # LRU update
            self._model_cache.move_to_end(self._model_key)
            logger.debug("Loaded FastSD CPU model from cache")
            return True
        return False

    def _save_model_to_cache(self) -> None:
        """Save model to cache."""
        if not self.enable_model_cache or self._model_key is None:
            return

        # Evict oldest if cache full
        if len(self._model_cache) >= self._max_cache_size:
            oldest_key, oldest_pipe = self._model_cache.popitem(last=False)
            # Cleanup evicted model
            del oldest_pipe
            logger.debug(f"Evicted FastSD CPU model from cache: {oldest_key[:8]}")

        self._model_cache[self._model_key] = self.pipe
        # LRU update
        self._model_cache.move_to_end(self._model_key)

    def initialize(self) -> bool:
        """Initialize the FastSD CPU model."""
        try:
            if self._initialized:
                return True

            # Try to load from cache first
            if self._load_model_from_cache():
                self._initialized = True
                logger.info("FastSD CPU engine initialized from cache")
                return True

            logger.info(
                f"Loading FastSD CPU model: {self.model_id} "
                f"(device: cpu, ONNX: {self.use_onnx})"
            )

            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                    "fastsd_cpu",
                )
            os.makedirs(model_cache_dir, exist_ok=True)

            try:
                if self.use_onnx:
                    # Use ONNX pipeline for faster inference
                    self.pipe = OnnxStableDiffusionPipeline.from_pretrained(
                        self.model_id, cache_dir=model_cache_dir
                    )
                    # Configure ONNX session options
                    if hasattr(self.pipe, "scheduler") and HAS_ONNX:
                        ort.get_available_providers()
                        session_options = ort.SessionOptions()
                        session_options.intra_op_num_threads = self.num_threads
                        session_options.inter_op_num_threads = self.num_threads
                else:
                    # Fallback to regular pipeline
                    from diffusers import StableDiffusionPipeline

                    self.pipe = StableDiffusionPipeline.from_pretrained(
                        self.model_id,
                        torch_dtype=torch.float32,
                        cache_dir=model_cache_dir,
                    )
                    # CPU memory optimization
                    self.pipe.enable_attention_slicing()
                    self.pipe.enable_sequential_cpu_offload()

                # Save to cache
                self._model_key = self._get_model_key()
                self._save_model_to_cache()

                self._initialized = True
                logger.info("FastSD CPU engine initialized successfully")
                return True
            except Exception as e:
                logger.error(f"Failed to load FastSD CPU model: {e}")
                return False

        except Exception as e:
            logger.error(f"Failed to initialize FastSD CPU engine: {e}")
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
            "use_onnx": self.use_onnx,
            "kwargs": {k: v for k, v in kwargs.items() if k != "image"},
        }
        cache_str = json.dumps(cache_data, sort_keys=True)
        return hashlib.sha256(cache_str.encode()).hexdigest()

    def generate(
        self,
        prompt: str,
        negative_prompt: str = "",
        width: int = 512,
        height: int = 512,
        steps: int = 20,
        cfg_scale: float = 7.0,
        sampler: str | None = None,
        seed: int | None = None,
        output_path: str | Path | None = None,
        **kwargs: Any,
    ) -> Image.Image | None | tuple[Image.Image | None, dict[str, Any]]:
        """Generate image using fast CPU-optimized Stable Diffusion."""
        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return None

        # Record start time for metrics
        start_time = time.perf_counter()

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
                logger.debug("Using cached FastSD CPU generation result")
                if output_path:
                    cached_image.save(output_path)
                # Record metrics
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    duration = time.perf_counter() - start_time
                    metrics.record_synthesis_time("fastsd_cpu", duration, cached=True)
                except Exception:
                    ...
                return cached_image
            else:
                self._cache_stats["misses"] += 1

        try:
            generator: Any = None
            if seed is not None:
                if self.use_onnx:
                    import numpy as np

                    generator = np.random.RandomState(seed)
                else:
                    generator = torch.Generator(device="cpu").manual_seed(seed)

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

            if not images:
                return None

            image: Image.Image = images[0]

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
                metrics.record_synthesis_time("fastsd_cpu", duration, cached=False)
            except Exception:
                ...

            if output_path:
                image.save(output_path)
                logger.info(f"Image saved to: {output_path}")
                return image

            return image

        except Exception as e:
            logger.error(f"FastSD CPU generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("fastsd_cpu", "generation_error")
            except Exception:
                ...
            return None

    def batch_generate(
        self,
        prompts: list[str],
        negative_prompts: list[str] | None = None,
        width: int = 512,
        height: int = 512,
        steps: int = 20,
        cfg_scale: float = 7.0,
        seeds: list[int | None] | None = None,
        output_paths: list[str | Path | None] | None = None,
        batch_size: int | None = None,
        **kwargs: Any,
    ) -> list[Image.Image | None]:
        """
        Generate multiple images using batch processing.

        Args:
            prompts: List of prompts to generate
            negative_prompts: Optional list of negative prompts
            width: Image width
            height: Image height
            steps: Number of inference steps
            cfg_scale: Guidance scale
            seeds: Optional list of random seeds
            output_paths: Optional list of output paths
            batch_size: Batch size for processing
            **kwargs: Additional generation parameters

        Returns:
            List of generated images or None for failed generations
        """
        if not prompts:
            return []

        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return [None] * len(prompts)

        # Record start time for metrics
        start_time = time.perf_counter()

        try:
            actual_batch_size = batch_size if batch_size is not None else self.batch_size

            # Process prompts in batches with ThreadPoolExecutor for better
            # parallelization
            def generate_single(args: tuple[Any, ...]) -> Any:
                idx, prompt, neg_prompt, seed, out_path = args
                try:
                    return self.generate(
                        prompt=prompt,
                        negative_prompt=neg_prompt,
                        width=width,
                        height=height,
                        steps=steps,
                        cfg_scale=cfg_scale,
                        seed=seed,
                        output_path=out_path,
                        **kwargs,
                    )
                except Exception as e:
                    logger.error(f"Batch generation failed for {idx}: {e}")
                    # Record error metrics
                    try:
                        from .performance_metrics import get_engine_metrics

                        metrics = get_engine_metrics()
                        metrics.record_error("fastsd_cpu", "batch_generation_error")
                    except Exception:
                        ...
                    return None

            # Prepare arguments
            if negative_prompts is None:
                negative_prompts = [""] * len(prompts)
            if seeds is None:
                seeds = [None] * len(prompts)
            if output_paths is None:
                output_paths = [None] * len(prompts)

            args_list = [
                (i, prompt, neg_prompt, seed, out_path)
                for i, (prompt, neg_prompt, seed, out_path) in enumerate(
                    zip(prompts, negative_prompts, seeds, output_paths)
                )
            ]

            # Process in batches with ThreadPoolExecutor
            all_images = []
            for i in range(0, len(args_list), actual_batch_size):
                batch_args = args_list[i : i + actual_batch_size]

                with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
                    batch_results = list(executor.map(generate_single, batch_args))
                all_images.extend(batch_results)

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("fastsd_cpu", duration, cached=False)
            except Exception:
                ...

            return all_images

        except Exception as e:
            logger.error(f"FastSD CPU batch generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("fastsd_cpu", "batch_generation_error")
            except Exception:
                ...
            return [None] * len(prompts)

    def cleanup(self) -> None:
        """Clean up resources (enhanced)."""
        try:
            # Don't delete if in cache (other instances might be using it)
            if (
                not self.enable_model_cache
                or (self._model_key is not None and self._model_key not in self._model_cache)
            ) and self.pipe is not None:
                del self.pipe
                self.pipe = None

            # Clear response cache
            if self.enable_response_cache:
                self._response_cache.clear()
                self._cache_stats = {"hits": 0, "misses": 0}

            self._initialized = False
            logger.info("FastSD CPU engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    @classmethod
    def clear_model_cache(cls) -> None:
        """Clear the shared model cache."""
        for _key, pipe in cls._model_cache.items():
            del pipe
        cls._model_cache.clear()
        logger.info("FastSD CPU model cache cleared")

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
            logger.info("FastSD CPU response cache cleared")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "model_id": self.model_id,
                "use_onnx": self.use_onnx,
                "num_threads": self.num_threads,
                "device": "cpu",
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


def create_fastsd_cpu_engine(
    model_id: str = "runwayml/stable-diffusion-v1-5",
    use_onnx: bool = True,
    num_threads: int = 4,
    device: str | None = None,
    gpu: bool = False,
    lazy_load: bool = True,
) -> FastSDCPUEngine:
    """Factory function to create a FastSD CPU engine instance."""
    engine = FastSDCPUEngine(
        model_id=model_id,
        use_onnx=use_onnx,
        num_threads=num_threads,
        device=device,
        gpu=gpu,
        lazy_load=lazy_load,
    )
    if not lazy_load:
        engine.initialize()
    return engine
