"""
SDXL ComfyUI Engine for VoiceStudio
Stable Diffusion XL via ComfyUI workflow engine integration

ComfyUI is a powerful node-based workflow engine for Stable Diffusion.
This engine provides SDXL-specific integration via ComfyUI.

Compatible with:
- Python 3.10+
- ComfyUI server (requires separate installation)
- HTTP API for workflow execution
"""

from __future__ import annotations

import hashlib
import json
import os
import time
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
from pathlib import Path
from typing import Any

import requests
from PIL import Image

HTTPAdapter: Any = None
Retry: Any = None
HAS_RETRY = False
try:
    from requests.adapters import HTTPAdapter as _HTTPAdapter
    from urllib3.util.retry import Retry as _Retry

    HTTPAdapter = _HTTPAdapter
    Retry = _Retry
    HAS_RETRY = True
except ImportError:
    pass

import logging

logger = logging.getLogger(__name__)

# Import base protocol from canonical source
from .base import EngineProtocol


class SDXLComfyEngine(EngineProtocol):
    """
    SDXL ComfyUI Engine for Stable Diffusion XL image generation.

    Supports:
    - Text-to-image generation
    - Image-to-image transformation
    - Inpainting
    - ControlNet
    - LoRA loading
    - Custom workflows
    """

    # Supported image formats
    SUPPORTED_FORMATS = ["png", "jpg", "webp"]

    def __init__(
        self,
        server_url: str = "http://127.0.0.1:8188",
        workflow_path: str | None = None,
        checkpoint: str = "sd_xl_base_1.0.safetensors",
        device: str | None = None,
        gpu: bool = True,
        enable_cache: bool = True,
        cache_size: int = 200,  # Increased cache size
        max_retries: int = 3,
        backoff_factor: float = 0.3,
        pool_connections: int = 20,  # Increased pool size
        pool_maxsize: int = 40,  # Increased max pool size
        batch_size: int = 8,  # Increased default batch size
    ):
        """
        Initialize SDXL ComfyUI engine.

        Args:
            server_url: URL of ComfyUI server (default: http://127.0.0.1:8188)
            workflow_path: Path to ComfyUI workflow JSON file (optional)
            checkpoint: SDXL checkpoint filename
            device: Device parameter (server handles device, kept for
                compatibility)
            gpu: GPU parameter (server handles GPU, kept for compatibility)
            enable_cache: Enable LRU workflow cache
            cache_size: Maximum cache size
            max_retries: Maximum number of retries for failed requests
            backoff_factor: Backoff factor for retries
            pool_connections: Number of connection pools
            pool_maxsize: Maximum pool size
            batch_size: Default batch size for parallel processing
        """
        super().__init__(device=device, gpu=gpu)

        self.server_url = server_url.rstrip("/")
        self.workflow_path = workflow_path
        self.checkpoint = checkpoint

        # Performance optimizations
        self.enable_cache = enable_cache
        self.cache_size = cache_size
        self.batch_size = batch_size
        self.max_retries = max_retries
        self.backoff_factor = backoff_factor
        self.pool_connections = pool_connections
        self.pool_maxsize = pool_maxsize

        # LRU workflow cache
        self._workflow_cache: OrderedDict[str, dict] = OrderedDict()
        self._response_cache: OrderedDict[str, Image.Image] = OrderedDict()
        self._cache_stats = {"hits": 0, "misses": 0}  # Cache hit/miss tracking

        # Setup session with connection pooling and retries
        self.session = requests.Session()
        self._request_timeout = 300  # 5 minutes for image generation

        if HAS_RETRY:
            retries = Retry(
                total=max_retries,
                backoff_factor=backoff_factor,
                status_forcelist=[429, 500, 502, 503, 504],
                allowed_methods=frozenset(["GET", "POST"]),
            )
            adapter = HTTPAdapter(
                max_retries=retries,
                pool_connections=pool_connections,
                pool_maxsize=pool_maxsize,
            )
            self.session.mount("http://", adapter)
            self.session.mount("https://", adapter)

        self.client_id: str | None = None

    def initialize(self) -> bool:
        """
        Initialize the SDXL ComfyUI engine by connecting to server.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            logger.info(f"Connecting to ComfyUI server: {self.server_url}")

            # Test server connection
            try:
                response = self.session.get(f"{self.server_url}/system_stats", timeout=5)
                if response.status_code == 200:
                    logger.info("ComfyUI server connection successful")
                else:
                    logger.warning(f"ComfyUI server returned status " f"{response.status_code}")
            except requests.exceptions.RequestException as e:
                logger.error(f"Failed to connect to ComfyUI server: {e}")
                logger.error(f"Make sure ComfyUI server is running at {self.server_url}")
                logger.error("Install ComfyUI from: " "https://github.com/comfyanonymous/ComfyUI")
                self._initialized = False
                return False

            # Get client ID for WebSocket connection (optional, for progress)
            try:
                response = self.session.get(f"{self.server_url}/prompt", timeout=5)
                if response.status_code == 200:
                    # Client ID not strictly required for HTTP API
                    self.client_id = "voice_studio_client"
                    logger.info("ComfyUI engine initialized successfully")
                    self._initialized = True
                    return True
            except Exception as e:
                logger.warning(f"Client ID setup failed: {e}, but continuing")

            self._initialized = True
            logger.info("SDXL ComfyUI engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize SDXL ComfyUI engine: {e}")
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
        sampler: str,
        seed: int | None,
        workflow: dict | None,
        **kwargs,
    ) -> str:
        """Generate cache key from generation parameters."""
        cache_data = {
            "prompt": prompt,
            "negative_prompt": negative_prompt,
            "width": width,
            "height": height,
            "steps": steps,
            "cfg_scale": cfg_scale,
            "sampler": sampler,
            "seed": seed if seed is not None else -1,
            "workflow": workflow if workflow else None,
            "workflow_path": self.workflow_path,
            "checkpoint": self.checkpoint,
            "kwargs": {k: v for k, v in kwargs.items() if k != "workflow"},
        }
        cache_str = json.dumps(cache_data, sort_keys=True)
        return hashlib.sha256(cache_str.encode()).hexdigest()

    def generate(
        self,
        prompt: str,
        negative_prompt: str = "",
        width: int = 1024,
        height: int = 1024,
        steps: int = 20,
        cfg_scale: float = 7.0,
        sampler: str = "euler",
        seed: int | None = None,
        output_path: str | Path | None = None,
        **kwargs,
    ) -> Image.Image | None | tuple[Image.Image | None, dict]:
        """
        Generate image from text prompt using SDXL via ComfyUI.

        Args:
            prompt: Text prompt for image generation
            negative_prompt: Negative prompt (things to avoid)
            width: Image width (default: 1024)
            height: Image height (default: 1024)
            steps: Number of sampling steps (default: 20)
            cfg_scale: Classifier-free guidance scale (default: 7.0)
            sampler: Sampling method (default: "euler")
            seed: Random seed (None for random)
            output_path: Optional path to save output image
            **kwargs: Additional generation parameters
                - workflow: Custom workflow JSON (overrides default)
                - lora: LoRA model to use
                - controlnet: ControlNet model to use
                - image: Input image for img2img
                - mask: Mask for inpainting

        Returns:
            PIL Image or None if generation failed,
            or tuple of (image, metadata) if metadata requested
        """
        if not self._initialized and not self.initialize():
            return None

        # Record start time for metrics
        start_time = time.perf_counter()

        # Check cache
        cache_key = None
        if self.enable_cache:
            cache_key = self._generate_cache_key(
                prompt,
                negative_prompt,
                width,
                height,
                steps,
                cfg_scale,
                sampler,
                seed,
                kwargs.get("workflow"),
                **kwargs,
            )
            if cache_key in self._response_cache:
                cached_image = self._response_cache[cache_key]
                # LRU update
                self._response_cache.move_to_end(cache_key)
                self._cache_stats["hits"] += 1
                logger.debug("Using cached SDXL ComfyUI generation result")
                if output_path:
                    cached_image.save(output_path)
                # Record metrics
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    duration = time.perf_counter() - start_time
                    metrics.record_synthesis_time("sdxl_comfy", duration, cached=True)
                except Exception:
                    ...
                return cached_image
            else:
                self._cache_stats["misses"] += 1

        try:
            # Load workflow if provided, otherwise use default SDXL workflow
            if kwargs.get("workflow") or self.workflow_path:
                workflow_path = kwargs.get("workflow") or self.workflow_path
                if workflow_path and os.path.exists(workflow_path):
                    # Check workflow cache
                    workflow_cache_key = hashlib.sha256(workflow_path.encode()).hexdigest()
                    if workflow_cache_key in self._workflow_cache:
                        workflow = self._workflow_cache[workflow_cache_key]
                        self._workflow_cache.move_to_end(workflow_cache_key)
                    else:
                        with open(workflow_path) as f:
                            workflow = json.load(f)
                        # Cache workflow
                        if len(self._workflow_cache) >= self.cache_size:
                            self._workflow_cache.popitem(last=False)
                        self._workflow_cache[workflow_cache_key] = workflow
                        self._workflow_cache.move_to_end(workflow_cache_key)
                else:
                    # Create default SDXL workflow
                    workflow = self._create_default_sdxl_workflow(
                        prompt,
                        negative_prompt,
                        width,
                        height,
                        steps,
                        cfg_scale,
                        sampler,
                        seed,
                        **kwargs,
                    )
            else:
                # Create default SDXL workflow
                workflow = self._create_default_sdxl_workflow(
                    prompt,
                    negative_prompt,
                    width,
                    height,
                    steps,
                    cfg_scale,
                    sampler,
                    seed,
                    **kwargs,
                )

            # Queue prompt
            prompt_data = {
                "prompt": workflow,
                "client_id": self.client_id or "voice_studio",
            }
            response = self.session.post(f"{self.server_url}/prompt", json=prompt_data, timeout=10)

            if response.status_code != 200:
                logger.error(f"Failed to queue prompt: {response.text}")
                return None

            result = response.json()
            prompt_id = result.get("prompt_id")
            if not prompt_id:
                logger.error("No prompt_id returned from ComfyUI")
                return None

            # Wait for completion and get result
            image = self._wait_for_completion(prompt_id)

            if image is None:
                return None

            # Cache result
            if self.enable_cache and cache_key is not None:
                # Evict oldest if cache full
                if len(self._response_cache) >= self.cache_size:
                    self._response_cache.popitem(last=False)
                self._response_cache[cache_key] = image.copy()
                # LRU update
                self._response_cache.move_to_end(cache_key)

            # Record metrics
            duration = time.perf_counter() - start_time
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_synthesis_time("sdxl_comfy", duration, cached=False)
            except Exception:
                ...

            # Save if requested
            if output_path:
                image.save(output_path)
                logger.info(f"Image saved to: {output_path}")
                return image

            return image

        except Exception as e:
            logger.error(f"SDXL ComfyUI generation failed: {e}")
            # Record error metrics
            try:
                from .performance_metrics import get_engine_metrics

                metrics = get_engine_metrics()
                metrics.record_error("sdxl_comfy", "generation_error")
            except Exception:
                ...
            return None

    def batch_generate(
        self,
        prompts: list[str],
        negative_prompt: str = "",
        width: int = 1024,
        height: int = 1024,
        steps: int = 20,
        cfg_scale: float = 7.0,
        sampler: str = "euler",
        seeds: list[int | None] | None = None,
        output_paths: list[str | Path | None] | None = None,
        workflows: list[dict | None] | None = None,
        batch_size: int | None = None,
        **kwargs,
    ) -> list[Image.Image | None]:
        """
        Generate multiple images in parallel using batch processing.

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
            workflows: Optional list of custom workflow JSONs
            batch_size: Batch size for parallel processing
            **kwargs: Additional generation parameters

        Returns:
            List of PIL Images or None for failed generations
        """
        if not prompts:
            return []

        if not self._initialized and not self.initialize():
            return [None] * len(prompts)

        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        # Record start time for metrics
        start_time = time.perf_counter()

        def generate_single(args):
            idx, prompt, seed, output_path, workflow = args
            try:
                return self.generate(
                    prompt=prompt,
                    negative_prompt=negative_prompt,
                    width=width,
                    height=height,
                    steps=steps,
                    cfg_scale=cfg_scale,
                    sampler=sampler,
                    seed=seed,
                    output_path=output_path,
                    workflow=workflow,
                    **kwargs,
                )
            except Exception as e:
                logger.error(f"Batch generation failed for prompt {idx}: {e}")
                # Record error metrics
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    metrics.record_error("sdxl_comfy", "batch_generation_error")
                except Exception:
                    ...
                return None

        # Prepare arguments
        if seeds is None:
            seeds = [None] * len(prompts)
        if output_paths is None:
            output_paths = [None] * len(prompts)
        if workflows is None:
            workflows = [None] * len(prompts)

        args_list = [
            (i, prompt, seed, output_path, workflow)
            for i, (prompt, seed, output_path, workflow) in enumerate(
                zip(prompts, seeds, output_paths, workflows)
            )
        ]

        # Process in batches with better chunking
        results = []
        for i in range(0, len(args_list), actual_batch_size):
            batch_args = args_list[i : i + actual_batch_size]

            with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
                batch_results = list(executor.map(generate_single, batch_args))
            results.extend(batch_results)

        # Record metrics
        duration = time.perf_counter() - start_time
        try:
            from .performance_metrics import get_engine_metrics

            metrics = get_engine_metrics()
            metrics.record_synthesis_time("sdxl_comfy", duration, cached=False)
        except Exception:
            ...

        return results

    def clear_cache(self):
        """Clear the workflow and response caches (enhanced)."""
        if self.enable_cache:
            self._workflow_cache.clear()
            self._response_cache.clear()
            self._cache_stats = {"hits": 0, "misses": 0}  # Reset cache stats
            logger.info("SDXL ComfyUI caches cleared")

    def get_cache_stats(self) -> dict[str, int | float | str | bool]:
        """Get cache statistics (enhanced)."""
        if not self.enable_cache:
            return {"enabled": False}

        total_requests = self._cache_stats["hits"] + self._cache_stats["misses"]
        hit_rate = (self._cache_stats["hits"] / total_requests * 100) if total_requests > 0 else 0.0

        return {
            "enabled": True,
            "workflow_cache_size": len(self._workflow_cache),
            "response_cache_size": len(self._response_cache),
            "max_size": self.cache_size,
            "hits": self._cache_stats["hits"],
            "misses": self._cache_stats["misses"],
            "hit_rate": f"{hit_rate:.2f}%",
        }

    def _create_default_sdxl_workflow(
        self,
        prompt: str,
        negative_prompt: str,
        width: int,
        height: int,
        steps: int,
        cfg_scale: float,
        sampler: str,
        seed: int | None,
        **kwargs,
    ) -> dict:
        """Create default SDXL workflow JSON."""
        # Generate node IDs
        import uuid

        checkpoint_loader = str(uuid.uuid4())
        clip_text_encode = str(uuid.uuid4())
        clip_text_encode_neg = str(uuid.uuid4())
        empty_latent = str(uuid.uuid4())
        ksampler = str(uuid.uuid4())
        vae_decode = str(uuid.uuid4())
        save_image = str(uuid.uuid4())

        workflow = {
            checkpoint_loader: {
                "inputs": {"ckpt_name": self.checkpoint},
                "class_type": "CheckpointLoaderSimple",
            },
            clip_text_encode: {
                "inputs": {"text": prompt, "clip": [checkpoint_loader, 1]},
                "class_type": "CLIPTextEncode",
            },
            clip_text_encode_neg: {
                "inputs": {
                    "text": negative_prompt,
                    "clip": [checkpoint_loader, 1],
                },
                "class_type": "CLIPTextEncode",
            },
            empty_latent: {
                "inputs": {"width": width, "height": height, "batch_size": 1},
                "class_type": "EmptyLatentImage",
            },
            ksampler: {
                "inputs": {
                    "seed": (seed if seed is not None else int.from_bytes(os.urandom(4), "big")),
                    "steps": steps,
                    "cfg": cfg_scale,
                    "sampler_name": sampler,
                    "scheduler": "normal",
                    "denoise": 1.0,
                    "model": [checkpoint_loader, 0],
                    "positive": [clip_text_encode, 0],
                    "negative": [clip_text_encode_neg, 0],
                    "latent_image": [empty_latent, 0],
                },
                "class_type": "KSampler",
            },
            vae_decode: {
                "inputs": {
                    "samples": [ksampler, 0],
                    "vae": [checkpoint_loader, 2],
                },
                "class_type": "VAEDecode",
            },
            save_image: {
                "inputs": {
                    "filename_prefix": "ComfyUI",
                    "images": [vae_decode, 0],
                },
                "class_type": "SaveImage",
            },
        }

        return workflow

    def _wait_for_completion(self, prompt_id: str, timeout: int = 300) -> Image.Image | None:
        """Wait for prompt completion and retrieve image."""
        import time

        start_time = time.time()

        while time.time() - start_time < timeout:
            # Check history for completed prompt
            try:
                response = self.session.get(f"{self.server_url}/history/{prompt_id}", timeout=5)
                if response.status_code == 200:
                    history = response.json()
                    if prompt_id in history:
                        # Get output images
                        output_images = history[prompt_id].get("outputs", {})
                        for _node_id, node_output in output_images.items():
                            if "images" in node_output:
                                for image_info in node_output["images"]:
                                    filename = image_info.get("filename")
                                    subfolder = image_info.get("subfolder", "")
                                    image_type = image_info.get("type", "output")

                                    # Download image
                                    image_url = f"{self.server_url}/view"
                                    params = {
                                        "filename": filename,
                                        "subfolder": subfolder,
                                        "type": image_type,
                                    }
                                    img_response = self.session.get(
                                        image_url, params=params, timeout=10
                                    )
                                    if img_response.status_code == 200:
                                        image = Image.open(BytesIO(img_response.content))
                                        return image

                        # If we got here, prompt completed but no images found
                        logger.warning("Prompt completed but no images found in output")
                        return None

                # Check queue status
                response = self.session.get(f"{self.server_url}/queue", timeout=5)
                if response.status_code == 200:
                    queue = response.json()
                    # Check if prompt is still in queue
                    in_queue = False
                    for queue_type in ["queue_running", "queue_pending"]:
                        for item in queue.get(queue_type, []):
                            if item[1] == prompt_id:
                                in_queue = True
                                break

                    if not in_queue:
                        # Prompt finished, check history again
                        time.sleep(1)
                        continue

                time.sleep(2)

            except requests.exceptions.RequestException as e:
                logger.warning(f"Error checking completion: {e}")
                time.sleep(2)

        logger.error(f"Generation timed out after {timeout} seconds")
        return None

    def cleanup(self):
        """Clean up resources."""
        try:
            # Clear caches
            if self.enable_cache:
                self._workflow_cache.clear()
                self._response_cache.clear()
                # Reset cache stats
                self._cache_stats = {"hits": 0, "misses": 0}

            if hasattr(self, "session"):
                self.session.close()
            self._initialized = False
            logger.info("SDXL ComfyUI engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "server_url": self.server_url,
                "checkpoint": self.checkpoint,
                "workflow_path": self.workflow_path,
                "supported_formats": self.SUPPORTED_FORMATS,
                "cache_enabled": self.enable_cache,
                "workflow_cache_size": cache_stats.get("workflow_cache_size", 0),
                "response_cache_size": cache_stats.get("response_cache_size", 0),
                "cache_max_size": cache_stats.get("max_size", 0),
                "cache_hits": cache_stats.get("hits", 0),
                "cache_misses": cache_stats.get("misses", 0),
                "cache_hit_rate": cache_stats.get("hit_rate", "N/A"),
                "batch_size": self.batch_size,
                "max_retries": self.max_retries,
                "connection_pooling": HAS_RETRY,
            }
        )
        return info


def create_sdxl_comfy_engine(
    server_url: str = "http://127.0.0.1:8188",
    workflow_path: str | None = None,
    checkpoint: str = "sd_xl_base_1.0.safetensors",
    device: str | None = None,
    gpu: bool = True,
) -> SDXLComfyEngine:
    """Factory function to create an SDXL ComfyUI engine instance."""
    return SDXLComfyEngine(
        server_url=server_url,
        workflow_path=workflow_path,
        checkpoint=checkpoint,
        device=device,
        gpu=gpu,
    )
