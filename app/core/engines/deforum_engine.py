"""
Deforum Engine for VoiceStudio
Keyframed Stable Diffusion animations using Deforum

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

logger = logging.getLogger(__name__)

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Optional imports (lazy to avoid crashing when dependencies are missing)
HAS_DIFFUSERS = False
_StableDiffusionPipeline = None

try:
    import cv2

    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False
    logger.warning("opencv-python not installed. Install with: pip install opencv-python")


def _load_diffusers_pipeline():
    """Import StableDiffusionPipeline only when needed."""
    try:
        from diffusers import StableDiffusionPipeline

        return StableDiffusionPipeline
    except Exception as exc:
        raise RuntimeError(
            "DeforumEngine requires diffusers plus a compatible transformers/torch "
            "stack. Install or repair the Deforum environment (recommended: a "
            "dedicated virtual environment for this engine)."
        ) from exc


def _require_diffusers_pipeline():
    """Ensure diffusers is available, raising with a clear message otherwise."""
    global HAS_DIFFUSERS, _StableDiffusionPipeline

    if HAS_DIFFUSERS and _StableDiffusionPipeline is not None:
        return _StableDiffusionPipeline

    pipeline_cls = _load_diffusers_pipeline()
    HAS_DIFFUSERS = True
    _StableDiffusionPipeline = pipeline_cls
    return pipeline_cls


class DeforumEngine(EngineProtocol):
    """
    Deforum Engine for keyframed Stable Diffusion animations.

    Supports:
    - Keyframed animations with motion parameters
    - Camera movement (zoom, pan, rotate)
    - Prompt interpolation
    - Frame-by-frame control
    """

    # Class-level model cache (shared across instances)
    _model_cache: OrderedDict[str, object] = OrderedDict()
    _max_cache_size = 4  # Cache up to 4 models (increased from 2)

    def __init__(
        self,
        model_id: str = "runwayml/stable-diffusion-v1-5",
        device: str | None = None,
        gpu: bool = True,
        width: int = 512,
        height: int = 512,
        num_inference_steps: int = 50,
        lazy_load: bool = True,
        enable_model_cache: bool = True,
        batch_size: int = 4,  # Increased default batch size
        enable_response_cache: bool = True,
        response_cache_size: int = 50,  # Smaller cache for animations
    ):
        """
        Initialize Deforum engine.

        Args:
            model_id: HuggingFace model identifier
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            width: Output video width
            height: Output video height
            num_inference_steps: Number of denoising steps
            lazy_load: Load models only when needed (default: True)
            enable_model_cache: Enable LRU model cache (default: True)
            batch_size: Default batch size for batch processing
            enable_response_cache: Enable LRU response cache (default: True)
            response_cache_size: Maximum response cache size
        """
        if not HAS_DIFFUSERS:
            # Try to load lazily with a clearer error message
            _require_diffusers_pipeline()

        super().__init__(device=device, gpu=gpu)

        self.model_id = model_id
        self.width = width
        self.height = height
        self.num_inference_steps = num_inference_steps
        self.lazy_load = lazy_load
        self.enable_model_cache = enable_model_cache
        self.batch_size = batch_size
        self.enable_response_cache = enable_response_cache
        self.response_cache_size = response_cache_size

        self.pipeline: Any = None
        self._model_key: str | None = None

        # LRU response cache for generated animations (stores output paths)
        self._response_cache: OrderedDict[str, str] = OrderedDict()
        self._cache_stats = {"hits": 0, "misses": 0}

        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _get_model_key(self) -> str:
        """Generate cache key for model."""
        key_data = {
            "model_id": self.model_id,
            "device": self.device,
            "width": self.width,
            "height": self.height,
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
            logger.debug("Loaded Deforum model from cache")
            return True
        return False

    def _save_model_to_cache(self):
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
            logger.debug(f"Evicted Deforum model from cache: {oldest_key[:8]}")

        self._model_cache[self._model_key] = self.pipeline
        # LRU update
        self._model_cache.move_to_end(self._model_key)

    def initialize(self) -> bool:
        """Initialize the Deforum model."""
        try:
            if self._initialized:
                return True

            # Try to load from cache first
            if self._load_model_from_cache():
                self._initialized = True
                logger.info("Deforum engine initialized from cache")
                return True

            logger.info(f"Loading Deforum model: {self.model_id}")

            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                )
            os.makedirs(model_cache_dir, exist_ok=True)

            pipeline_cls = _require_diffusers_pipeline()

            self.pipeline = pipeline_cls.from_pretrained(
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
                if hasattr(self.pipeline, "enable_attention_slicing"):
                    self.pipeline.enable_attention_slicing()

            # Save to cache
            self._model_key = self._get_model_key()
            self._save_model_to_cache()

            self._initialized = True
            logger.info(f"Deforum model loaded successfully (device: {self.device})")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Deforum model: {e}")
            self._initialized = False
            return False

    def cleanup(self):
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
            logger.info("Deforum engine cleaned up")

        except Exception as e:
            logger.error(f"Error during Deforum cleanup: {e}")

    @classmethod
    def clear_model_cache(cls):
        """Clear the shared model cache."""
        for _key, pipeline in cls._model_cache.items():
            del pipeline
        cls._model_cache.clear()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        logger.info("Deforum model cache cleared")

    def _generate_cache_key(
        self,
        prompts: str | list[str] | dict[int, str],
        num_frames: int,
        fps: int,
        seed: int | None,
        keyframes: dict[int, dict] | None,
        camera_motion: dict | None,
        **kwargs,
    ) -> str:
        """Generate cache key from generation parameters."""
        # Normalize prompts to string representation
        if isinstance(prompts, str):
            prompts_str = prompts
        elif isinstance(prompts, list):
            prompts_str = json.dumps(prompts, sort_keys=True)
        else:
            prompts_str = json.dumps(prompts, sort_keys=True)

        cache_data = {
            "prompts": prompts_str,
            "num_frames": num_frames,
            "fps": fps,
            "seed": seed if seed is not None else -1,
            "keyframes": json.dumps(keyframes, sort_keys=True) if keyframes else None,
            "camera_motion": json.dumps(camera_motion, sort_keys=True) if camera_motion else None,
            "width": self.width,
            "height": self.height,
            "num_inference_steps": self.num_inference_steps,
            "kwargs": dict(kwargs.items()),
        }
        cache_str = json.dumps(cache_data, sort_keys=True)
        return hashlib.sha256(cache_str.encode()).hexdigest()

    def generate_animation(
        self,
        prompts: str | list[str] | dict[int, str],
        output_path: str | Path | None = None,
        num_frames: int = 120,
        fps: int = 24,
        seed: int | None = None,
        keyframes: dict[int, dict] | None = None,
        camera_motion: dict | None = None,
        **kwargs,
    ) -> str:
        """
        Generate keyframed animation.

        Args:
            prompts: Prompt string, list of prompts, or dict of {frame: prompt}
            output_path: Path to save output video
            num_frames: Number of frames to generate
            fps: Frames per second
            seed: Random seed
            keyframes: Dict of {frame_number: {prompt, strength, ...}}
            camera_motion: Camera motion parameters (zoom, pan_x, pan_y, rotate)
            **kwargs: Additional generation parameters

        Returns:
            Path to generated video
        """
        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            raise RuntimeError("Failed to initialize Deforum engine.")

        # Record start time for metrics
        start_time = time.perf_counter()

        # Check response cache
        cache_key = None
        if self.enable_response_cache:
            cache_key = self._generate_cache_key(
                prompts,
                num_frames,
                fps,
                seed,
                keyframes,
                camera_motion,
                **kwargs,
            )
            if cache_key in self._response_cache:
                cached_path = self._response_cache[cache_key]
                # LRU update
                self._response_cache.move_to_end(cache_key)
                self._cache_stats["hits"] += 1
                logger.debug("Using cached Deforum generation result")
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
                    metrics.record_synthesis_time("deforum", duration, cached=True)
                except Exception:
                    ...
                return cached_path
            else:
                self._cache_stats["misses"] += 1

        try:
            # Parse prompts
            prompt_dict = self._parse_prompts(prompts, num_frames)

            # Parse keyframes
            if keyframes is None:
                keyframes = {}

            # Parse camera motion
            if camera_motion is None:
                camera_motion = {"zoom": 1.0, "pan_x": 0.0, "pan_y": 0.0, "rotate": 0.0}

            # Set seed
            generator = None
            if seed is not None:
                generator = torch.Generator(device=self.device).manual_seed(seed)

            logger.info(f"Generating Deforum animation: {num_frames} frames")

            frames: list[Image.Image] = []
            for frame_idx in range(num_frames):
                # Interpolate prompt
                prompt = self._interpolate_prompt(prompt_dict, frame_idx, num_frames)

                # Interpolate camera motion
                cam_params = self._interpolate_camera(camera_motion, frame_idx, num_frames)

                # Get keyframe parameters if available
                frame_params = keyframes.get(frame_idx, {})
                strength = frame_params.get("strength", 0.75)
                guidance_scale = frame_params.get("guidance_scale", 7.5)

                # Generate frame
                if frame_idx == 0:
                    # First frame - no previous frame
                    image = self.pipeline(
                        prompt=prompt,
                        width=self.width,
                        height=self.height,
                        num_inference_steps=self.num_inference_steps,
                        guidance_scale=guidance_scale,
                        generator=generator,
                        **kwargs,
                    ).images[0]
                else:
                    # Subsequent frames - use previous frame as init
                    prev_image = frames[-1]
                    # Apply camera transform
                    transformed_image = self._apply_camera_transform(prev_image, cam_params)

                    image = self.pipeline(
                        prompt=prompt,
                        image=transformed_image,
                        strength=strength,
                        width=self.width,
                        height=self.height,
                        num_inference_steps=self.num_inference_steps,
                        guidance_scale=guidance_scale,
                        generator=generator,
                        **kwargs,
                    ).images[0]

                frames.append(image)

                # Clear GPU cache periodically
                if (frame_idx + 1) % 10 == 0:
                    logger.info(f"Generated {frame_idx + 1}/{num_frames} frames")
                    if self.device == "cuda" and torch.cuda.is_available():
                        torch.cuda.empty_cache()

            # Clear GPU cache after generation
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            # Generate output path
            if output_path is None:
                output_dir = os.path.join(
                    os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "deforum_output"
                )
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, f"deforum_anim_{seed or 0}.mp4")

            output_path = Path(output_path)
            output_path.parent.mkdir(parents=True, exist_ok=True)

            # Save video
            self._save_video(frames, str(output_path), fps)

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
                metrics.record_synthesis_time("deforum", duration, cached=False)
            except Exception:
                ...

            logger.info(f"Animation generated successfully: {output_path}")
            return str(output_path)

        except Exception as e:
            logger.error(f"Error generating animation: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("deforum", "generation_error")
            except Exception:
                ...
            raise RuntimeError(f"Failed to generate animation: {e}")

    def batch_generate_animations(
        self, animations_config: list[dict], batch_size: int | None = None, **kwargs
    ) -> list[str | None]:
        """
        Generate multiple animations using batch processing.

        Args:
            animations_config: List of animation config dicts, each containing:
                - prompts: Prompt string, list, or dict
                - output_path: Optional output path
                - num_frames: Number of frames
                - fps: Frames per second
                - seed: Random seed
                - keyframes: Keyframe dict
                - camera_motion: Camera motion dict
            batch_size: Batch size for processing
            **kwargs: Additional generation parameters

        Returns:
            List of paths to generated videos or None for failed generations
        """
        if not animations_config:
            return []

        # Lazy loading: initialize only when needed
        if not self._initialized and not self.initialize():
            return [None] * len(animations_config)

        # Record start time for metrics
        start_time = time.perf_counter()

        try:
            actual_batch_size = batch_size if batch_size is not None else self.batch_size

            # Process animations in batches with ThreadPoolExecutor for better
            # parallelization
            def generate_single(args):
                idx, config = args
                try:
                    return self.generate_animation(
                        prompts=config.get("prompts", ""),
                        output_path=config.get("output_path"),
                        num_frames=config.get("num_frames", 120),
                        fps=config.get("fps", 24),
                        seed=config.get("seed"),
                        keyframes=config.get("keyframes"),
                        camera_motion=config.get("camera_motion"),
                        **kwargs,
                    )
                except Exception as e:
                    logger.error(f"Batch animation generation failed for {idx}: {e}")
                    # Record error metrics
                    try:
                        from .performance_metrics import get_engine_metrics

                        metrics = get_engine_metrics()
                        metrics.record_error("deforum", "batch_generation_error")
                    except Exception:
                        ...
                    return None

            # Prepare arguments
            args_list = [(i, config) for i, config in enumerate(animations_config)]

            # Process in batches with ThreadPoolExecutor
            all_outputs = []
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
                metrics.record_synthesis_time("deforum", duration, cached=False)
            except Exception:
                ...

            return all_outputs

        except Exception as e:
            logger.error(f"Deforum batch generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("deforum", "batch_generation_error")
            except Exception:
                ...
            return [None] * len(animations_config)

    def _parse_prompts(
        self,
        prompts: str | list[str] | dict[int, str],
        num_frames: int,
    ) -> dict[int, str]:
        """Parse prompts into frame-based dictionary."""
        if isinstance(prompts, str):
            return {0: prompts, num_frames - 1: prompts}
        elif isinstance(prompts, list):
            if len(prompts) == 1:
                return {0: prompts[0], num_frames - 1: prompts[0]}
            else:
                step = num_frames // (len(prompts) - 1)
                return {i * step: prompts[i] for i in range(len(prompts))}
        else:
            return prompts

    def _interpolate_prompt(self, prompt_dict: dict[int, str], frame: int, num_frames: int) -> str:
        """Interpolate prompt between keyframes."""
        if frame in prompt_dict:
            return prompt_dict[frame]

        # Find surrounding keyframes
        prev_frame = max([f for f in prompt_dict if f <= frame], default=0)
        next_frame = min([f for f in prompt_dict if f > frame], default=num_frames - 1)

        if prev_frame == next_frame:
            return prompt_dict[prev_frame]

        # Linear interpolation (simple - just use previous prompt)
        return prompt_dict[prev_frame]

    def _interpolate_camera(self, camera_motion: dict, frame: int, num_frames: int) -> dict:
        """Interpolate camera motion parameters."""
        zoom_start = camera_motion.get("zoom_start", camera_motion.get("zoom", 1.0))
        zoom_end = camera_motion.get("zoom_end", zoom_start)
        pan_x_start = camera_motion.get("pan_x_start", camera_motion.get("pan_x", 0.0))
        pan_x_end = camera_motion.get("pan_x_end", pan_x_start)
        pan_y_start = camera_motion.get("pan_y_start", camera_motion.get("pan_y", 0.0))
        pan_y_end = camera_motion.get("pan_y_end", pan_y_start)
        rotate_start = camera_motion.get("rotate_start", camera_motion.get("rotate", 0.0))
        rotate_end = camera_motion.get("rotate_end", rotate_start)

        t = frame / (num_frames - 1) if num_frames > 1 else 0

        return {
            "zoom": zoom_start + (zoom_end - zoom_start) * t,
            "pan_x": pan_x_start + (pan_x_end - pan_x_start) * t,
            "pan_y": pan_y_start + (pan_y_end - pan_y_start) * t,
            "rotate": rotate_start + (rotate_end - rotate_start) * t,
        }

    def _apply_camera_transform(self, image: Image.Image, cam_params: dict) -> Image.Image:
        """Apply camera transform to image."""
        if not HAS_CV2:
            return image

        img_array = np.array(image)
        h, w = img_array.shape[:2]
        center = (w // 2, h // 2)

        # Zoom
        zoom = cam_params.get("zoom", 1.0)
        if zoom != 1.0:
            scale = zoom
            M = cv2.getRotationMatrix2D(center, 0, scale)
            img_array = cv2.warpAffine(img_array, M, (w, h))

        # Rotate
        rotate = cam_params.get("rotate", 0.0)
        if rotate != 0.0:
            M = cv2.getRotationMatrix2D(center, rotate, 1.0)
            img_array = cv2.warpAffine(img_array, M, (w, h))

        # Pan
        pan_x = cam_params.get("pan_x", 0.0)
        pan_y = cam_params.get("pan_y", 0.0)
        if pan_x != 0.0 or pan_y != 0.0:
            M = np.array([[1, 0, pan_x * w], [0, 1, pan_y * h]], dtype=np.float32)
            img_array = cv2.warpAffine(img_array, M, (w, h))

        return Image.fromarray(img_array)

    def _save_video(self, frames: list[Image.Image], output_path: str, fps: int):
        """Save frames as video."""
        if HAS_CV2:
            self._save_video_cv2(frames, output_path, fps)
        else:
            self._save_video_pil(frames, output_path, fps)

    def _save_video_cv2(self, frames: list[Image.Image], output_path: str, fps: int):
        """Save frames as video using OpenCV."""
        if not HAS_CV2:
            raise ImportError("opencv-python required for video saving")

        height, width = frames[0].size[1], frames[0].size[0]
        fourcc_fn = cv2.VideoWriter_fourcc
        fourcc = fourcc_fn(*"mp4v")
        out = cv2.VideoWriter(output_path, fourcc, fps, (width, height))

        try:
            for frame in frames:
                frame_cv = cv2.cvtColor(np.array(frame), cv2.COLOR_RGB2BGR)
                out.write(frame_cv)
        finally:
            out.release()

    def _save_video_pil(self, frames: list[Image.Image], output_path: str, fps: int):
        """Save frames as video using PIL/imageio fallback."""
        try:
            import imageio
        except ImportError:
            raise ImportError("imageio required for video saving")

        frame_arrays: list[Any] = [np.array(frame) for frame in frames]
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

    def clear_response_cache(self):
        """Clear the response cache."""
        if self.enable_response_cache:
            self._response_cache.clear()
            self._cache_stats = {"hits": 0, "misses": 0}
            logger.info("Deforum response cache cleared")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "model_id": self.model_id,
                "width": self.width,
                "height": self.height,
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


def create_deforum_engine(
    model_id: str = "runwayml/stable-diffusion-v1-5",
    device: str | None = None,
    gpu: bool = True,
    width: int = 512,
    height: int = 512,
    num_inference_steps: int = 50,
    lazy_load: bool = True,
) -> DeforumEngine:
    """
    Create and initialize Deforum engine.

    Args:
        model_id: HuggingFace model identifier
        device: Device to use
        gpu: Whether to use GPU
        width: Output video width
        height: Output video height
        num_inference_steps: Number of denoising steps
        lazy_load: Load models only when needed (default: True)

    Returns:
        Initialized DeforumEngine instance
    """
    engine = DeforumEngine(
        model_id=model_id,
        device=device,
        gpu=gpu,
        width=width,
        height=height,
        num_inference_steps=num_inference_steps,
        lazy_load=lazy_load,
    )
    if not lazy_load:
        engine.initialize()
    return engine
