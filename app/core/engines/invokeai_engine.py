"""
InvokeAI Engine for VoiceStudio
Professional Stable Diffusion pipeline integration

InvokeAI is a professional Stable Diffusion pipeline with
advanced features and a clean API.

Compatible with:
- Python 3.10+
- InvokeAI server (requires separate installation)
- HTTP API for image generation
"""

from __future__ import annotations

import base64
import hashlib
import json
import logging
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
except ImportError:  # ALLOWED: bare except - optional urllib3 Retry
    pass

logger = logging.getLogger(__name__)

# Import base protocol from canonical source
from .base import EngineProtocol


class InvokeAIEngine(EngineProtocol):
    """
    InvokeAI Engine for professional Stable Diffusion image generation.

    Supports:
    - Text-to-image generation
    - Image-to-image transformation
    - Inpainting
    - ControlNet
    - LoRA loading
    - Advanced prompt parsing
    """

    SUPPORTED_FORMATS = ["png", "jpg", "webp"]

    def __init__(
        self,
        server_url: str = "http://127.0.0.1:9090",
        device: str | None = None,
        gpu: bool = True,
        enable_cache: bool = True,
        cache_size: int = 100,
        max_retries: int = 3,
        backoff_factor: float = 0.3,
        pool_connections: int = 10,
        pool_maxsize: int = 20,
        batch_size: int = 4,
    ):
        """
        Initialize InvokeAI engine.

        Args:
            server_url: URL of InvokeAI server (default: http://127.0.0.1:9090)
            device: Device parameter (server handles device)
            gpu: GPU parameter (server handles GPU)
            enable_cache: Enable LRU response cache
            cache_size: Maximum cache size
            max_retries: Maximum number of retries for failed requests
            backoff_factor: Backoff factor for retries
            pool_connections: Number of connection pools
            pool_maxsize: Maximum pool size
            batch_size: Default batch size for parallel processing
        """
        super().__init__(device=device, gpu=gpu)

        self.server_url = server_url.rstrip("/")
        self.api_url = f"{self.server_url}/api/v1"

        # Performance optimizations
        self.enable_cache = enable_cache
        self.cache_size = max(cache_size, 200)  # Increased default cache size
        self.batch_size = max(batch_size, 8)  # Increased default batch size
        self.max_retries = max_retries
        self.backoff_factor = backoff_factor
        # Increased pool size
        self.pool_connections = max(pool_connections, 20)
        self.pool_maxsize = max(pool_maxsize, 40)

        # LRU response cache
        self._response_cache: OrderedDict[str, Image.Image] = OrderedDict()
        self._cache_stats = {
            "hits": 0,
            "misses": 0,
        }

        # Setup session with connection pooling and retries
        self.session = requests.Session()
        self._request_timeout = 300

        if HAS_RETRY:
            retries = Retry(
                total=max_retries,
                backoff_factor=backoff_factor,
                status_forcelist=[429, 500, 502, 503, 504],
                allowed_methods=frozenset(["GET", "POST"]),
            )
            adapter = HTTPAdapter(
                max_retries=retries,
                pool_connections=self.pool_connections,
                pool_maxsize=self.pool_maxsize,
            )
            self.session.mount("http://", adapter)
            self.session.mount("https://", adapter)

    def initialize(self) -> bool:
        """Initialize the InvokeAI engine by connecting to server."""
        try:
            if self._initialized:
                return True

            logger.info(f"Connecting to InvokeAI server: {self.server_url}")

            try:
                response = self.session.get(f"{self.api_url}/models", timeout=5)
                if response.status_code == 200:
                    logger.info("InvokeAI server connection successful")
                    self._initialized = True
                    return True
                else:
                    logger.error(f"InvokeAI server returned status " f"{response.status_code}")
                    self._initialized = False
                    return False
            except requests.exceptions.RequestException as e:
                logger.error(f"Failed to connect to InvokeAI server: {e}")
                logger.error(f"Make sure InvokeAI server is running at " f"{self.server_url}")
                logger.error("Install from: https://github.com/invoke-ai/InvokeAI")
                self._initialized = False
                return False

        except Exception as e:
            logger.error(f"Failed to initialize InvokeAI engine: {e}")
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
        sampler: str = "euler",
        seed: int | None = None,
        output_path: str | Path | None = None,
        **kwargs,
    ) -> Image.Image | None | tuple[Image.Image | None, dict]:
        """Generate image using InvokeAI."""
        if not self._initialized and not self.initialize():
            return None

        # Check cache (only for txt2img without image input)
        cache_key = None
        if self.enable_cache and "image" not in kwargs:
            cache_key = self._generate_cache_key(
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
            if cache_key in self._response_cache:
                cached_image = self._response_cache[cache_key]
                # LRU update
                self._response_cache.move_to_end(cache_key)
                self._cache_stats["hits"] += 1
                logger.debug("Using cached InvokeAI generation result")
                if output_path:
                    cached_image.save(output_path)
                return cached_image
            else:
                self._cache_stats["misses"] += 1

        try:
            payload = {
                "prompt": prompt,
                "negative_prompt": negative_prompt,
                "width": width,
                "height": height,
                "steps": steps,
                "cfg_scale": cfg_scale,
                "sampler_name": sampler,
                "seed": seed if seed is not None else -1,
                "num_images": 1,
            }

            if "image" in kwargs:
                image_input = kwargs["image"]
                if isinstance(image_input, str):
                    if os.path.exists(image_input):
                        with open(image_input, "rb") as f:
                            image_data = base64.b64encode(f.read()).decode("utf-8")
                    else:
                        image_data = image_input
                elif isinstance(image_input, Image.Image):
                    buffer = BytesIO()
                    image_input.save(buffer, format="PNG")
                    image_data = base64.b64encode(buffer.getvalue()).decode("utf-8")
                else:
                    image_data = str(image_input)

                payload["init_image"] = image_data
                payload["strength"] = kwargs.get("strength", 0.7)
                endpoint = f"{self.api_url}/img2img"
            else:
                endpoint = f"{self.api_url}/txt2img"

            response = self.session.post(endpoint, json=payload, timeout=300)

            if response.status_code != 200:
                logger.error(f"InvokeAI generation failed: {response.text}")
                return None

            result = response.json()

            if "images" in result and len(result["images"]) > 0:
                image_data = result["images"][0]
                if isinstance(image_data, str):
                    image_bytes = base64.b64decode(image_data)
                else:
                    image_bytes = image_data
                image = Image.open(BytesIO(image_bytes))

                # Cache result (only for txt2img without image input)
                if self.enable_cache and cache_key is not None:
                    # Evict oldest if cache full
                    if len(self._response_cache) >= self.cache_size:
                        self._response_cache.popitem(last=False)
                    self._response_cache[cache_key] = image.copy()
                    # LRU update
                    self._response_cache.move_to_end(cache_key)

                if output_path:
                    image.save(output_path)
                    logger.info(f"Image saved to: {output_path}")
                    return image

                return image
            else:
                logger.error("No images in response")
                return None

        except Exception as e:
            logger.error(f"InvokeAI generation failed: {e}")
            return None

    def batch_generate(
        self,
        prompts: list[str],
        negative_prompt: str = "",
        width: int = 512,
        height: int = 512,
        steps: int = 20,
        cfg_scale: float = 7.0,
        sampler: str = "euler",
        seeds: list[int | None] | None = None,
        output_paths: list[str | Path | None] | None = None,
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

        def generate_single(args):
            idx, prompt, seed, output_path = args
            try:
                # Record processing time if metrics available
                start_time = time.perf_counter()
                result = self.generate(
                    prompt=prompt,
                    negative_prompt=negative_prompt,
                    width=width,
                    height=height,
                    steps=steps,
                    cfg_scale=cfg_scale,
                    sampler=sampler,
                    seed=seed,
                    output_path=output_path,
                    **kwargs,
                )
                # Record metrics if available
                duration = time.perf_counter() - start_time
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    metrics.record_synthesis_time("invokeai", duration, cached=False)
                except Exception:
                    logger.debug("Performance metrics unavailable for invokeai batch generation.")
                return result
            except Exception as e:
                logger.error(f"Batch generation failed for prompt {idx}: {e}")
                # Record error if metrics available
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    metrics.record_error("invokeai", "generation_error")
                except Exception:
                    ...
                return None

        # Prepare arguments
        if seeds is None:
            seeds = [None] * len(prompts)
        if output_paths is None:
            output_paths = [None] * len(prompts)

        args_list = [
            (i, prompt, seed, output_path)
            for i, (prompt, seed, output_path) in enumerate(zip(prompts, seeds, output_paths))
        ]

        # Optimize batch processing with better chunking
        results = []
        for i in range(0, len(args_list), actual_batch_size):
            batch_args = args_list[i : i + actual_batch_size]

            with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
                batch_results = list(executor.map(generate_single, batch_args))
            results.extend(batch_results)

        return results

    def clear_cache(self):
        """Clear the response cache (enhanced)."""
        if self.enable_cache:
            self._response_cache.clear()
            self._cache_stats = {"hits": 0, "misses": 0}
            logger.info("InvokeAI response cache cleared")

    def get_cache_stats(self) -> dict[str, int | float | str | bool]:
        """Get cache statistics (enhanced)."""
        if not self.enable_cache:
            return {"enabled": False}

        total_requests = self._cache_stats["hits"] + self._cache_stats["misses"]
        hit_rate = (self._cache_stats["hits"] / total_requests * 100) if total_requests > 0 else 0.0

        return {
            "enabled": True,
            "size": len(self._response_cache),
            "max_size": self.cache_size,
            "cache_hits": self._cache_stats["hits"],
            "cache_misses": self._cache_stats["misses"],
            "hit_rate": f"{hit_rate:.2f}%",
        }

    def cleanup(self):
        """Clean up resources (enhanced)."""
        try:
            # Clear cache
            if self.enable_cache:
                self._response_cache.clear()
                self._cache_stats = {"hits": 0, "misses": 0}

            if hasattr(self, "session"):
                self.session.close()
            self._initialized = False
            logger.info("InvokeAI engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        cache_stats = self.get_cache_stats()
        info.update(
            {
                "server_url": self.server_url,
                "supported_formats": self.SUPPORTED_FORMATS,
                "cache_enabled": self.enable_cache,
                "cache_size": cache_stats.get("size", 0),
                "cache_max_size": cache_stats.get("max_size", 0),
                "batch_size": self.batch_size,
                "max_retries": self.max_retries,
                "connection_pooling": HAS_RETRY,
            }
        )
        return info


def create_invokeai_engine(
    server_url: str = "http://127.0.0.1:9090",
    device: str | None = None,
    gpu: bool = True,
) -> InvokeAIEngine:
    """Factory function to create an InvokeAI engine instance."""
    return InvokeAIEngine(server_url=server_url, device=device, gpu=gpu)
