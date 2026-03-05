"""
Stable Video Diffusion (SVD) Engine for VoiceStudio
Image-to-video generation using Stability AI's Stable Video Diffusion model

Compatible with:
- Python 3.10+
- diffusers 0.21.0+
- PyTorch 2.0.0+
- transformers 4.20.0+
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

import numpy as np
import torch
from PIL import Image

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Optional imports for diffusers
try:
    from diffusers import StableVideoDiffusionPipeline

    HAS_DIFFUSERS = True
except ImportError:
    HAS_DIFFUSERS = False
    logger.warning("diffusers not installed. Install with: pip install diffusers>=0.21.0")

# load_image is not in diffusers type definitions; use getattr for mypy compat
_load_image: Any = None
if HAS_DIFFUSERS:
    try:
        import diffusers.utils as _diffusers_utils

        _load_image = getattr(_diffusers_utils, "load_image", None)
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass

# Optional image processing
try:
    import cv2

    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False
    logger.warning("opencv-python not installed. Install with: pip install opencv-python")


class SVDEngine(EngineProtocol):
    """
    Stable Video Diffusion (SVD) Engine for image-to-video generation.

    Supports:
    - Image-to-video generation
    - Video frame interpolation
    - Video length control
    - Motion control
    """

    # Class-level model cache (shared across instances)
    _model_cache: OrderedDict[str, Any] = OrderedDict()
    _max_cache_size = 4  # Cache up to 4 models (increased from 2)

    def __init__(
        self,
        model_id: str = "stabilityai/stable-video-diffusion-img2vid",
        device: str | None = None,
        gpu: bool = True,
        num_frames: int = 14,
        num_inference_steps: int = 25,
        lazy_load: bool = True,
        enable_model_cache: bool = True,
        batch_size: int = 4,  # Increased default batch size
        enable_response_cache: bool = True,
        response_cache_size: int = 50,  # Smaller cache for videos
    ):
        """
        Initialize SVD engine.

        Args:
            model_id: HuggingFace model identifier
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            num_frames: Number of frames to generate (default: 14)
            num_inference_steps: Number of denoising steps (default: 25)
            lazy_load: Load models only when needed (default: True)
            enable_model_cache: Enable LRU model cache (default: True)
            batch_size: Default batch size for batch processing
            enable_response_cache: Enable LRU response cache (default: True)
            response_cache_size: Maximum response cache size
        """
        if not HAS_DIFFUSERS:
            raise ImportError(
                "diffusers not installed. " "Install with: pip install diffusers>=0.21.0"
            )

        # Initialize base protocol
        super().__init__(device=device, gpu=gpu)

        self.model_id = model_id
        self.num_frames = num_frames
        self.num_inference_steps = num_inference_steps
        self.lazy_load = lazy_load
        self.enable_model_cache = enable_model_cache
        self.batch_size = batch_size
        self.enable_response_cache = enable_response_cache
        self.response_cache_size = response_cache_size

        self.pipeline: Any = None
        self._model_key: str | None = None

        # LRU response cache for generated videos (stores output paths)
        self._response_cache: OrderedDict[str, str] = OrderedDict()
        self._cache_stats = {"hits": 0, "misses": 0}

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _get_model_key(self) -> str:
        """Generate cache key for model."""
        key_data = {
            "model_id": self.model_id,
            "device": self.device,
            "num_frames": self.num_frames,
            "num_inference_steps": self.num_inference_steps,
        }
        key_str = json.dumps(key_data, sort_keys=True)
        return hashlib.sha256(key_str.encode()).hexdigest()

    def _load_model_from_cache(self) -> bool:
        """Load model from cache if available."""
        if not self.enable_model_cache:
            return False

        self._model_key = self._get_model_key()
        if self._model_key in self._model_cache:
            self.pipeline = self._model_cache[self._model_key]
            # LRU update
            self._model_cache.move_to_end(self._model_key)
            logger.debug("Loaded SVD model from cache")
            return True
        return False

    def _save_model_to_cache(self) -> None:
        """Save model to cache."""
        if not self.enable_model_cache or self._model_key is None:
            return

        # Evict oldest if cache full
        if len(self._model_cache) >= self._max_cache_size:
            oldest_key, oldest_pipeline = self._model_cache.popitem(last=False)
            # Cleanup evicted model
            del oldest_pipeline
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted SVD model from cache: {oldest_key[:8]}")

        self._model_cache[self._model_key] = self.pipeline
        # LRU update
        self._model_cache.move_to_end(self._model_key)

    def initialize(self) -> bool:
        """
        Initialize the SVD model.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            # Try to load from cache first
            if self._load_model_from_cache():
                self._initialized = True
                logger.info("SVD engine initialized from cache")
                return True

            logger.info(f"Loading SVD model: {self.model_id}")

            # Use model cache directory if available
            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                )

            # Ensure model cache directory exists
            os.makedirs(model_cache_dir, exist_ok=True)

            # Load pipeline
            self.pipeline = StableVideoDiffusionPipeline.from_pretrained(
                self.model_id,
                torch_dtype=(torch.float16 if self.device == "cuda" else torch.float32),
                cache_dir=model_cache_dir,
            )
            self.pipeline = self.pipeline.to(self.device)

            # GPU memory optimization
            if self.device == "cuda":
                if hasattr(self.pipeline, "enable_model_cpu_offload"):
                    self.pipeline.enable_model_cpu_offload()
                if hasattr(self.pipeline, "enable_vae_slicing"):
                    self.pipeline.enable_vae_slicing()

            # Save to cache
            self._model_key = self._get_model_key()
            self._save_model_to_cache()

            self._initialized = True

            logger.info(f"SVD model loaded successfully (device: {self.device})")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize SVD model: {e}")
            self._initialized = False
            return False

    def cleanup(self) -> None:
        """Clean up resources and free memory (enhanced)."""
        try:
            # Don't delete if in cache (other instances might be using it)
            if (
                not self.enable_model_cache
                or (self._model_key is not None and self._model_key not in self._model_cache)
            ) and self.pipeline is not None:
                del self.pipeline
                self.pipeline = None

            # Clear response cache
            if self.enable_response_cache:
                self._response_cache.clear()
                self._cache_stats = {"hits": 0, "misses": 0}

            # Clear GPU cache
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("SVD engine cleaned up")

        except Exception as e:
            logger.error(f"Error during SVD cleanup: {e}")

    @classmethod
    def clear_model_cache(cls) -> None:
        """Clear the shared model cache."""
        for _key, pipeline in cls._model_cache.items():
            del pipeline
        cls._model_cache.clear()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        logger.info("SVD model cache cleared")

    def _generate_cache_key(
        self,
        image_path: str | Path | Image.Image,
        num_frames: int,
        num_inference_steps: int,
        motion_bucket_id: int,
        seed: int | None,
        **kwargs: Any,
    ) -> str:
        """Generate cache key from generation parameters."""
        # Use image path hash for cache key
        if isinstance(image_path, (str, Path)):
            image_str = str(image_path)
        else:
            # For PIL Image, use a hash of the image data
            image_str = hashlib.sha256(
                image_path.tobytes() if hasattr(image_path, "tobytes") else str(image_path).encode()
            ).hexdigest()

        cache_data = {
            "image": image_str,
            "num_frames": num_frames,
            "num_inference_steps": num_inference_steps,
            "motion_bucket_id": motion_bucket_id,
            "seed": seed if seed is not None else -1,
            "kwargs": dict(kwargs.items()),
        }
        cache_str = json.dumps(cache_data, sort_keys=True)
        return hashlib.sha256(cache_str.encode()).hexdigest()

    def generate_video(
        self,
        image_path: str | Path | Image.Image,
        output_path: str | Path | None = None,
        num_frames: int | None = None,
        num_inference_steps: int | None = None,
        motion_bucket_id: int = 127,
        fps: int = 7,
        seed: int | None = None,
        **kwargs: Any,
    ) -> str | tuple[str, dict[str, Any]]:
        """
        Generate video from image.

        Args:
            image_path: Path to input image or PIL Image
            output_path: Path to save output video (optional)
            num_frames: Number of frames to generate (default: self.num_frames)
            num_inference_steps: Number of denoising steps (default: self.num_inference_steps)
            motion_bucket_id: Motion bucket ID (1-255, higher = more motion)
            fps: Frames per second for output video
            seed: Random seed for reproducibility
            **kwargs: Additional generation parameters

        Returns:
            Path to generated video, or tuple of (path, metadata) if return_metadata=True
        """
        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            raise RuntimeError("Failed to initialize SVD engine.")

        # Record start time for metrics
        start_time = time.perf_counter()

        # Use provided parameters or defaults
        num_frames = num_frames or self.num_frames
        num_inference_steps = num_inference_steps or self.num_inference_steps

        # Check response cache
        cache_key = None
        if self.enable_response_cache:
            cache_key = self._generate_cache_key(
                image_path,
                num_frames,
                num_inference_steps,
                motion_bucket_id,
                seed,
                **kwargs,
            )
            if cache_key in self._response_cache:
                cached_path = self._response_cache[cache_key]
                # LRU update
                self._response_cache.move_to_end(cache_key)
                self._cache_stats["hits"] += 1
                logger.debug("Using cached SVD generation result")
                # Copy to output path if provided
                if output_path and cached_path != str(output_path):
                    import shutil

                    shutil.copy2(cached_path, output_path)
                    return str(output_path)
                # Record metrics
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    duration = time.perf_counter() - start_time
                    metrics.record_synthesis_time("svd", duration, cached=True)
                except Exception:
                    ...
                return cached_path
            else:
                self._cache_stats["misses"] += 1

        try:
            # Load input image
            if isinstance(image_path, (str, Path)):
                if _load_image is not None:
                    image = _load_image(str(image_path))
                else:
                    image = Image.open(image_path).convert("RGB")
            else:
                image = image_path.convert("RGB") if hasattr(image_path, "convert") else image_path

            # Resize image to 1024x576 (SVD requirement)
            if image.size != (1024, 576):
                image = image.resize((1024, 576), Image.Resampling.LANCZOS)

            # Set random seed if provided
            generator = None
            if seed is not None:
                generator = torch.Generator(device=self.device).manual_seed(seed)

            logger.info(f"Generating video: {num_frames} frames, {num_inference_steps} steps")

            # Generate video frames
            frames = self.pipeline(
                image,
                decode_chunk_size=2,
                num_frames=num_frames,
                num_inference_steps=num_inference_steps,
                motion_bucket_id=motion_bucket_id,
                generator=generator,
                **kwargs,
            ).frames[0]

            # Clear GPU cache after generation
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            # Generate output path if not provided
            if output_path is None:
                output_dir = os.path.join(
                    os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "svd_output"
                )
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, f"svd_video_{hash(str(image_path))}.mp4")

            output_path = Path(output_path)
            output_path.parent.mkdir(parents=True, exist_ok=True)

            # Save frames as video
            if HAS_CV2:
                self._save_video_cv2(frames, str(output_path), fps)
            else:
                self._save_video_pil(frames, str(output_path), fps)

            # Cache result
            if self.enable_response_cache and cache_key is not None:
                # Evict oldest if cache full
                if len(self._response_cache) >= self.response_cache_size:
                    self._response_cache.popitem(last=False)
                self._response_cache[cache_key] = str(output_path)
                # LRU update
                self._response_cache.move_to_end(cache_key)

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("svd", duration, cached=False)
            except Exception:
                ...

            logger.info(f"Video generated successfully: {output_path}")
            return str(output_path)

        except Exception as e:
            logger.error(f"Error generating video: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("svd", "generation_error")
            except Exception:
                ...
            raise RuntimeError(f"Failed to generate video: {e}")

    def batch_generate_videos(
        self,
        image_paths: list[str | Path | Image.Image],
        output_paths: list[str | Path | None] | None = None,
        num_frames: int | None = None,
        num_inference_steps: int | None = None,
        motion_bucket_ids: list[int] | None = None,
        fps: int = 7,
        seeds: list[int | None] | None = None,
        batch_size: int | None = None,
        **kwargs: Any,
    ) -> list[str | None]:
        """
        Generate multiple videos from images using batch processing.

        Args:
            image_paths: List of paths to input images or PIL Images
            output_paths: Optional list of paths to save output videos
            num_frames: Number of frames to generate (default: self.num_frames)
            num_inference_steps: Number of denoising steps (default: self.num_inference_steps)
            motion_bucket_ids: List of motion bucket IDs (1-255, higher = more motion)
            fps: Frames per second for output videos
            seeds: List of random seeds for reproducibility
            batch_size: Batch size for processing
            **kwargs: Additional generation parameters

        Returns:
            List of paths to generated videos or None for failed generations
        """
        if not image_paths:
            return []

        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return [None] * len(image_paths)

        # Record start time for metrics
        start_time = time.perf_counter()

        try:
            actual_batch_size = batch_size if batch_size is not None else self.batch_size

            # Process images in batches with ThreadPoolExecutor for better
            # parallelization
            all_outputs = []

            def generate_single(args: tuple[Any, ...]) -> str | None:
                idx, img_path, out_path, motion_id, seed = args
                try:
                    result = self.generate_video(
                        image_path=img_path,
                        output_path=out_path,
                        num_frames=num_frames,
                        num_inference_steps=num_inference_steps,
                        motion_bucket_id=motion_id,
                        fps=fps,
                        seed=seed,
                        **kwargs,
                    )
                    return result if isinstance(result, str) else result[0]
                except Exception as e:
                    logger.error(f"Batch video generation failed for {idx}: {e}")
                    # Record error metrics
                    try:
                        from .performance_metrics import get_engine_metrics

                        metrics = get_engine_metrics()
                        metrics.record_error("svd", "batch_generation_error")
                    except Exception:
                        ...
                    return None

            # Prepare arguments
            if output_paths is None:
                output_paths = [None] * len(image_paths)
            if motion_bucket_ids is None:
                motion_bucket_ids = [127] * len(image_paths)
            if seeds is None:
                seeds = [None] * len(image_paths)

            args_list = [
                (i, img_path, out_path, motion_id, seed)
                for i, (img_path, out_path, motion_id, seed) in enumerate(
                    zip(image_paths, output_paths, motion_bucket_ids, seeds)
                )
            ]

            # Process in batches with ThreadPoolExecutor
            for i in range(0, len(args_list), actual_batch_size):
                batch_args = args_list[i : i + actual_batch_size]

                with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
                    batch_results = list(executor.map(generate_single, batch_args))
                all_outputs.extend(batch_results)

                # Clear GPU cache after batch
                if self.device == "cuda" and torch.cuda.is_available():
                    torch.cuda.empty_cache()

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("svd", duration, cached=False)
            except Exception:
                ...

            return all_outputs

        except Exception as e:
            logger.error(f"SVD batch generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("svd", "batch_generation_error")
            except Exception:
                ...
            return [None] * len(image_paths)

    def _save_video_cv2(self, frames: list[Image.Image], output_path: str, fps: int) -> None:
        """Save frames as video using OpenCV."""
        if not HAS_CV2:
            raise ImportError("opencv-python required for video saving")

        # Get frame dimensions
        height, width = frames[0].size[1], frames[0].size[0]

        # Create video writer
        _fourcc_fn: Any = getattr(cv2, "VideoWriter_fourcc", None)
        fourcc: int = _fourcc_fn(*"mp4v")
        out = cv2.VideoWriter(output_path, fourcc, fps, (width, height))

        try:
            for frame in frames:
                # Convert PIL to OpenCV format
                frame_cv = cv2.cvtColor(np.array(frame), cv2.COLOR_RGB2BGR)
                out.write(frame_cv)
        finally:
            out.release()

    def _save_video_pil(self, frames: list[Image.Image], output_path: str, fps: int) -> None:
        """Save frames as video using PIL/imageio fallback."""
        try:
            import imageio
            import imageio_ffmpeg
        except ImportError:
            raise ImportError("imageio and imageio-ffmpeg required for video saving")

        # Convert frames to numpy arrays
        frame_arrays: list[Any] = [np.array(frame) for frame in frames]

        # Save as video
        imageio.mimwrite(output_path, frame_arrays, fps=fps, codec="libx264", quality=8)

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
            logger.info("SVD response cache cleared")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "model_id": self.model_id,
                "num_frames": self.num_frames,
                "num_inference_steps": self.num_inference_steps,
                "engine_type": "video_generation",
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


def create_svd_engine(
    model_id: str = "stabilityai/stable-video-diffusion-img2vid",
    device: str | None = None,
    gpu: bool = True,
    num_frames: int = 14,
    num_inference_steps: int = 25,
    lazy_load: bool = True,
) -> SVDEngine:
    """
    Create and initialize SVD engine.

    Args:
        model_id: HuggingFace model identifier
        device: Device to use
        gpu: Whether to use GPU
        num_frames: Number of frames to generate
        num_inference_steps: Number of denoising steps
        lazy_load: Load models only when needed (default: True)

    Returns:
        Initialized SVDEngine instance
    """
    engine = SVDEngine(
        model_id=model_id,
        device=device,
        gpu=gpu,
        num_frames=num_frames,
        num_inference_steps=num_inference_steps,
        lazy_load=lazy_load,
    )
    if not lazy_load:
        engine.initialize()
    return engine
