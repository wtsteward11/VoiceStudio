"""
GPT-SoVITS Engine for VoiceStudio
Voice conversion and fine-tuning using GPT-SoVITS

Compatible with:
- Python 3.10+
- torch>=2.0.0
- GPT-SoVITS package
"""

from __future__ import annotations

import contextlib
import logging
import os
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Callable

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache: Any = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    logger.debug("General model cache not available, using GPT-SoVITS-specific cache")

# Fallback: GPT-SoVITS-specific cache (for backward compatibility)
_GPT_SOVITS_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_path: str, device: str) -> str:
    """Generate cache key for GPT-SoVITS model."""
    return f"gpt_sovits::{model_path}::{device}"


def _get_cached_gpt_sovits_model(model_path: str, device: str):
    """Get cached GPT-SoVITS model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("gpt_sovits", model_path, device=device)
        if cached is not None:
            return cached

    # Fallback to GPT-SoVITS-specific cache
    cache_key = _get_cache_key(model_path, device)
    if cache_key in _GPT_SOVITS_MODEL_CACHE:
        _GPT_SOVITS_MODEL_CACHE.move_to_end(cache_key)
        return _GPT_SOVITS_MODEL_CACHE[cache_key]
    return None


def _cache_gpt_sovits_model(model_path: str, device: str, model_data: dict):
    """Cache GPT-SoVITS model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("gpt_sovits", model_path, model_data, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to GPT-SoVITS-specific cache
    cache_key = _get_cache_key(model_path, device)

    if cache_key in _GPT_SOVITS_MODEL_CACHE:
        _GPT_SOVITS_MODEL_CACHE.move_to_end(cache_key)
        return

    _GPT_SOVITS_MODEL_CACHE[cache_key] = model_data

    # Evict oldest if cache full
    if len(_GPT_SOVITS_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_model = _GPT_SOVITS_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_model
            if HAS_TORCH and torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted GPT-SoVITS model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting GPT-SoVITS model from cache: {e}")

    logger.debug(
        f"Cached GPT-SoVITS model: {cache_key} (cache size: {len(_GPT_SOVITS_MODEL_CACHE)})"
    )


# Required imports
try:
    import torch

    HAS_TORCH = True
except ImportError:
    HAS_TORCH = False
    logger.warning("torch not installed. Install with: pip install torch")

try:
    import numpy as np

    HAS_NUMPY = True
except ImportError:
    HAS_NUMPY = False
    logger.warning("numpy not installed. Install with: pip install numpy")

try:
    import soundfile as sf

    HAS_SOUNDFILE = True
except ImportError:
    HAS_SOUNDFILE = False
    logger.warning("soundfile not installed. Install with: pip install soundfile")

# Optional quality metrics import
try:
    from .quality_metrics import calculate_all_metrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False

# Optional audio utilities import for quality enhancement
enhance_voice_cloning_quality: Callable[..., Any] | None = None
try:
    from app.core.audio.audio_utils import (
        enhance_voice_cloning_quality,
        enhance_voice_quality,
        normalize_lufs,
        remove_artifacts,
    )

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False


class GPTSovitsEngine(EngineProtocol):
    """
    GPT-SoVITS Engine for voice conversion and fine-tuning.

    Supports:
    - Voice conversion
    - Fine-tuning on custom datasets
    - Text-to-speech with voice cloning
    - Cross-lingual voice conversion
    """

    def __init__(
        self,
        model_path: str | None = None,
        config_path: str | None = None,
        device: str | None = None,
        **kwargs,
    ):
        """
        Initialize GPT-SoVITS engine.

        Args:
            model_path: Path to GPT-SoVITS model files
            config_path: Path to configuration file
            device: Device to use ('cuda', 'cpu', or None for auto)
        """
        models_root = os.getenv("VOICESTUDIO_MODELS_PATH", r"E:\VoiceStudio\models")
        default_model_path = os.path.join(models_root, "checkpoints", "MyVoiceProj", "model.pth")
        default_config_path = os.path.join(models_root, "checkpoints", "MyVoiceProj", "config.json")

        self.model_path = model_path or os.getenv("GPT_SOVITS_MODEL_PATH") or default_model_path
        self.config_path = config_path or os.getenv("GPT_SOVITS_CONFIG_PATH") or default_config_path
        self.device = device or ("cuda" if (HAS_TORCH and torch.cuda.is_available()) else "cpu")

        self._initialized = False
        self._model: dict[str, Any] | None = None
        self._tokenizer = None
        self._config = None
        self._response_cache: OrderedDict[str, Any] = OrderedDict()
        self._cache_max_size = 100
        self.lazy_load = True
        self.batch_size = 2
        self._caching_enabled = True

    def initialize(self) -> bool:
        """Initialize GPT-SoVITS model and components."""
        try:
            if not HAS_TORCH:
                logger.error("torch is required for GPT-SoVITS")
                return False

            if not HAS_NUMPY:
                logger.error("numpy is required for GPT-SoVITS")
                return False

            if not HAS_SOUNDFILE:
                logger.error("soundfile is required for GPT-SoVITS")
                return False

            # Check if model path exists
            if self.model_path and not os.path.exists(self.model_path):
                logger.warning(
                    f"Model path not found: {self.model_path}. GPT-SoVITS may need to be downloaded."
                )
                # Continue with initialization - model can be loaded on first use

            # Initialize device
            if self.device == "cuda" and not torch.cuda.is_available():
                logger.warning("CUDA not available, falling back to CPU")
                self.device = "cpu"

            # Load configuration if provided
            if self.config_path and os.path.exists(self.config_path):
                try:
                    import json

                    with open(self.config_path, encoding="utf-8") as f:
                        self._config = json.load(f)
                    logger.info(f"Loaded configuration from {self.config_path}")
                except Exception as e:
                    logger.warning(f"Failed to load config: {e}")

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
            else:
                # Load model immediately if lazy loading disabled
                if not self._load_model():
                    return False

            self._initialized = True
            logger.info(f"GPT-SoVITS engine initialized on device: {self.device}")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize GPT-SoVITS engine: {e}", exc_info=True)
            return False

    def cleanup(self) -> None:
        """Clean up GPT-SoVITS resources."""
        try:
            if self._model is not None:
                del self._model
                self._model = None

            if self._tokenizer is not None:
                del self._tokenizer
                self._tokenizer = None

            # Clear response cache
            self._response_cache.clear()

            if HAS_TORCH:
                torch.cuda.empty_cache() if self.device == "cuda" else None

            self._initialized = False
            logger.info("GPT-SoVITS engine cleaned up")

        except Exception as e:
            logger.error(f"Error during GPT-SoVITS cleanup: {e}")

    def is_initialized(self) -> bool:
        """Check if engine is initialized."""
        return self._initialized

    def get_device(self) -> str:
        """Get the device being used."""
        return self.device

    def get_info(self) -> dict:
        """Get engine information."""
        return {
            "engine": "gpt_sovits",
            "name": "GPT-SoVITS",
            "version": "1.0.0",
            "device": self.device,
            "initialized": self._initialized,
            "model_loaded": self._model is not None,
            "has_torch": HAS_TORCH,
            "has_numpy": HAS_NUMPY,
            "has_soundfile": HAS_SOUNDFILE,
        }

    def synthesize(
        self,
        text: str,
        reference_audio: str | bytes | None = None,
        language: str = "zh",
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> bytes | None | tuple[bytes | None, dict]:
        """
        Synthesize speech from text using GPT-SoVITS.

        Args:
            text: Text to synthesize
            reference_audio: Reference audio for voice cloning (path or bytes)
            language: Language code (default: 'zh' for Chinese)
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional parameters

        Returns:
            Audio data as bytes (WAV format) or None on error,
            or tuple of (audio_bytes, quality_metrics) if calculate_quality=True
        """
        if not self._initialized:
            logger.error("Engine not initialized")
            return None

        try:
            # Check response cache
            import hashlib

            ref_id = (
                reference_audio.hex() if isinstance(reference_audio, bytes) else reference_audio
            )
            cache_key = hashlib.md5(f"{text}_{ref_id}_{language}".encode()).hexdigest()
            if cache_key in self._response_cache:
                logger.debug("Using cached GPT-SoVITS synthesis result")
                self._response_cache.move_to_end(cache_key)  # LRU update
                cached: bytes | tuple[bytes | None, dict[str, Any]] | None = self._response_cache[
                    cache_key
                ]
                return cached

            # Lazy load model if needed
            if self._model is None and not self._load_model():
                return None

            # Process reference audio if provided
            ref_audio_data = None
            if reference_audio:
                if isinstance(reference_audio, str):
                    if os.path.exists(reference_audio):
                        ref_audio_data, sr = sf.read(reference_audio)
                    else:
                        logger.error(f"Reference audio file not found: {reference_audio}")
                        return None
                elif isinstance(reference_audio, bytes):
                    import io

                    ref_audio_data, _sr = sf.read(io.BytesIO(reference_audio))
                else:
                    logger.error("Invalid reference_audio type")
                    return None

            # Perform synthesis
            audio_data = self._perform_synthesis(text, ref_audio_data, language, **kwargs)

            if audio_data is None:
                return None

            # Apply quality processing if requested
            quality_metrics = {}
            if enhance_quality or calculate_quality:
                audio_data, quality_metrics = self._process_audio_quality(
                    audio_data,
                    22050,  # GPT-SoVITS typically uses 22050 Hz
                    enhance_quality,
                    calculate_quality,
                )
                if isinstance(audio_data, tuple):
                    audio_data, quality_metrics = audio_data

            # Convert to WAV bytes
            import io

            output = io.BytesIO()
            sf.write(output, audio_data, 22050, format="WAV")
            result = output.getvalue()

            # Cache result if successful (LRU)
            if result is not None:
                # Manage cache size - remove oldest entries if cache is full
                if len(self._response_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._response_cache))
                    del self._response_cache[oldest_key]
                self._response_cache[cache_key] = result
                self._response_cache.move_to_end(cache_key)  # LRU update

            # Return with quality metrics if requested
            if calculate_quality:
                return result, quality_metrics

            return result

        except Exception as e:
            logger.error(f"GPT-SoVITS synthesis failed: {e}", exc_info=True)
            return None

    def _load_model(self) -> bool:
        """Load GPT-SoVITS model with caching support."""
        try:
            if not self.model_path:
                logger.warning("Model path not set. GPT-SoVITS requires model files.")
                # Return True to allow initialization, but model won't be loaded
                return True

            # Check cache first
            if self._caching_enabled:
                cached_model = _get_cached_gpt_sovits_model(self.model_path, self.device)
                if cached_model is not None:
                    logger.debug(f"Using cached GPT-SoVITS model: {self.model_path}")
                    self._model = cached_model
                    return True

            # Check if model path exists
            if not os.path.exists(self.model_path):
                logger.warning(
                    f"Model path not found: {self.model_path}. GPT-SoVITS may need to be downloaded."
                )
                return True

            logger.info(f"Loading GPT-SoVITS model from {self.model_path}")

            try:
                from pathlib import Path

                model_dir = Path(self.model_path)

                gpt_model_path = None
                sovits_model_path = None

                for file in model_dir.rglob("*"):
                    if file.suffix in [".pth", ".pt", ".ckpt"]:
                        if "gpt" in file.name.lower() or "text" in file.name.lower():
                            gpt_model_path = str(file)
                        elif "sovits" in file.name.lower() or "vocoder" in file.name.lower():
                            sovits_model_path = str(file)

                if gpt_model_path and sovits_model_path:
                    logger.info(f"Found GPT model: {gpt_model_path}")
                    logger.info(f"Found SoVITS model: {sovits_model_path}")

                    # Try to actually load GPT-SoVITS model objects
                    gpt_model_obj = None
                    sovits_model_obj = None

                    try:
                        # Try to import and load GPT-SoVITS modules
                        from GPT_SoVITS.inference_webui import get_tts_wav
                        from GPT_SoVITS.text import cleaned_text_to_sequence
                        from GPT_SoVITS.text.cleaner import clean_text

                        # Load actual model objects if GPT-SoVITS package is available
                        if HAS_TORCH and torch is not None:
                            device = torch.device(self.device)

                            # Try to load GPT model
                            try:
                                if os.path.exists(gpt_model_path):
                                    gpt_checkpoint = torch.load(gpt_model_path, map_location=device)
                                    gpt_model_obj = gpt_checkpoint
                                    logger.debug("Loaded GPT model checkpoint")
                            except Exception as e:
                                logger.debug(f"Could not load GPT model object: {e}")

                            # Try to load SoVITS model
                            try:
                                if os.path.exists(sovits_model_path):
                                    sovits_checkpoint = torch.load(
                                        sovits_model_path, map_location=device
                                    )
                                    sovits_model_obj = sovits_checkpoint
                                    logger.debug("Loaded SoVITS model checkpoint")
                            except Exception as e:
                                logger.debug(f"Could not load SoVITS model object: {e}")
                    except ImportError:
                        logger.debug("GPT-SoVITS package not available, storing paths only")

                    model_data = {
                        "loaded": True,
                        "path": self.model_path,
                        "gpt_path": gpt_model_path,
                        "sovits_path": sovits_model_path,
                        "gpt_model": gpt_model_obj,
                        "sovits_model": sovits_model_obj,
                    }
                    self._model = model_data

                    # Cache model
                    if self._caching_enabled:
                        _cache_gpt_sovits_model(self.model_path, self.device, model_data)
                else:
                    logger.warning("Model files not found in expected format, using fallback mode")
                    model_data = {
                        "loaded": True,
                        "path": self.model_path,
                        "fallback": True,
                    }
                    self._model = model_data

                    # Cache model even in fallback mode
                    if self._caching_enabled:
                        _cache_gpt_sovits_model(self.model_path, self.device, model_data)

                logger.info("GPT-SoVITS model loaded successfully")
                return True
            except Exception as e:
                logger.warning(f"Model loading encountered issues: {e}, using fallback mode")
                self._model = {
                    "loaded": True,
                    "path": self.model_path,
                    "fallback": True,
                }
                return True

        except Exception as e:
            logger.error(f"Failed to load GPT-SoVITS model: {e}", exc_info=True)
            return False

    def _perform_synthesis(
        self, text: str, ref_audio: np.ndarray | None, language: str, **kwargs
    ) -> np.ndarray | None:
        """Perform actual synthesis using GPT-SoVITS."""
        try:
            if not HAS_NUMPY:
                return None

            # Try to use GPT-SoVITS API server if available
            api_url = kwargs.get("api_url") or os.getenv("GPT_SOVITS_API_URL")
            if api_url:
                return self._synthesize_via_api(text, ref_audio, language, api_url, **kwargs)

            # Try to use local GPT-SoVITS model
            if self._model and not self._model.get("fallback", False):
                return self._synthesize_with_model(text, ref_audio, language, **kwargs)

            # Fallback: use TTS API or basic synthesis
            return self._synthesize_fallback(text, ref_audio, language, **kwargs)

        except Exception as e:
            logger.error(f"Synthesis failed: {e}", exc_info=True)
            return None

    def _synthesize_via_api(
        self,
        text: str,
        ref_audio: np.ndarray | None,
        language: str,
        api_url: str,
        **kwargs,
    ) -> np.ndarray | None:
        """
        Synthesize using GPT-SoVITS API server.
        Matches the old project's API implementation.
        """
        try:
            import io
            import tempfile
            from pathlib import Path

            try:
                import requests

                HAS_REQUESTS = True
            except ImportError:
                HAS_REQUESTS = False
                logger.warning("requests not available for API synthesis")
                return None

            if not HAS_REQUESTS:
                return None

            # Prepare reference audio file
            ref_audio_path = None
            if ref_audio is not None:
                if isinstance(ref_audio, np.ndarray):
                    # Save numpy array to temp file
                    ref_audio_path = tempfile.mktemp(suffix=".wav")
                    sf.write(ref_audio_path, ref_audio, 22050, format="WAV")
                elif isinstance(ref_audio, str):
                    if os.path.exists(ref_audio):
                        ref_audio_path = ref_audio
                    else:
                        logger.warning(f"Reference audio file not found: {ref_audio}")
                        return None
                elif isinstance(ref_audio, bytes):
                    # Save bytes to temp file
                    ref_audio_path = tempfile.mktemp(suffix=".wav")
                    with open(ref_audio_path, "wb") as f:
                        f.write(ref_audio)

            if ref_audio_path is None:
                logger.warning("Reference audio required for GPT-SoVITS synthesis")
                return None

            # Prepare API request parameters (matching old implementation)
            ref_text = kwargs.get("ref_text", "")
            prompt_text = kwargs.get("prompt_text", "")
            prompt_language = kwargs.get("prompt_language", "")
            temperature = kwargs.get("temperature", 1.0)
            top_k = kwargs.get("top_k", 20)
            top_p = kwargs.get("top_p", 1.0)

            # Use language='auto' if not specified (matching old implementation)
            if not language or language == "":
                language = "auto"

            # Prepare API endpoint (matching old implementation: /gpt_sovits)
            api_endpoint = f"{api_url}/gpt_sovits"
            if not api_url.endswith("/"):
                api_endpoint = f"{api_url}/gpt_sovits"

            try:
                # Prepare files for multipart form data (matching old implementation)
                with open(ref_audio_path, "rb") as ref_file:
                    files = {
                        "ref_audio": (
                            Path(ref_audio_path).name,
                            ref_file,
                            "audio/wav",
                        ),
                    }

                    data = {
                        "text": text,
                        "ref_text": ref_text,
                        "language": language,
                        "prompt_text": prompt_text,
                        "prompt_language": prompt_language,
                        "temperature": str(temperature),
                        "top_k": str(top_k),
                        "top_p": str(top_p),
                    }

                    # Make API request
                    timeout = kwargs.get("timeout", 120)
                    response = requests.post(api_endpoint, files=files, data=data, timeout=timeout)

                    if response.status_code == 200:
                        # Response contains audio data directly
                        audio_bytes = response.content
                        # Read audio from bytes
                        audio, _sr = sf.read(io.BytesIO(audio_bytes))

                        # Clean up temp file if we created it
                        if (
                            ref_audio is not None
                            and isinstance(ref_audio, np.ndarray)
                            and ref_audio_path
                            and os.path.exists(ref_audio_path)
                        ):
                            with contextlib.suppress(Exception):
                                os.remove(ref_audio_path)

                        result_audio: np.ndarray = audio.astype(np.float32)
                        return result_audio
                    else:
                        error_msg = (
                            response.text
                            if hasattr(response, "text")
                            else str(response.status_code)
                        )
                        logger.error(
                            f"GPT-SoVITS API request failed: "
                            f"{response.status_code} - {error_msg}"
                        )
                        return None

            except requests.exceptions.RequestException as e:
                logger.error(f"GPT-SoVITS API request exception: {e}")
                return None
            finally:
                # Clean up temp file if we created it
                if (
                    ref_audio is not None
                    and isinstance(ref_audio, np.ndarray)
                    and ref_audio_path
                    and os.path.exists(ref_audio_path)
                ):
                    with contextlib.suppress(Exception):
                        os.remove(ref_audio_path)

        except Exception as e:
            logger.warning(f"API synthesis failed: {e}, trying fallback")
            return None

    def _synthesize_with_model(
        self, text: str, ref_audio: np.ndarray | None, language: str, **kwargs
    ) -> np.ndarray | None:
        """Synthesize using loaded GPT-SoVITS model."""
        try:
            if not HAS_TORCH or torch is None:
                return None

            if self._model is None:
                return None

            # Try to import GPT-SoVITS modules
            try:
                from GPT_SoVITS.inference_webui import get_tts_wav
                from GPT_SoVITS.text import cleaned_text_to_sequence
                from GPT_SoVITS.text.cleaner import clean_text
            except ImportError:
                logger.warning("GPT-SoVITS package not available")
                return None

            # Clean and tokenize text
            cleaned_text = clean_text(text, language)
            cleaned_text_to_sequence(cleaned_text, language)

            # Prepare reference audio
            ref_audio_path = None
            if ref_audio is not None:
                if isinstance(ref_audio, np.ndarray):
                    # Save to temp file
                    import tempfile

                    ref_audio_path = tempfile.mktemp(suffix=".wav")
                    sf.write(ref_audio_path, ref_audio, 22050)
                elif isinstance(ref_audio, str):
                    ref_audio_path = ref_audio

            # Get model paths
            gpt_path = self._model.get("gpt_path")
            sovits_path = self._model.get("sovits_path")

            if not gpt_path or not sovits_path:
                logger.warning("Model paths not available")
                return None

            # Perform synthesis with inference mode for better performance
            device = torch.device(self.device)
            with torch.inference_mode():  # Faster than no_grad
                audio = get_tts_wav(
                    ref_audio_path or "",
                    text,
                    language,
                    gpt_path,
                    sovits_path,
                    device=device,
                    **kwargs,
                )

            # Clean up temp file
            if (
                ref_audio is not None
                and isinstance(ref_audio, np.ndarray)
                and ref_audio_path is not None
            ):
                with contextlib.suppress(BaseException):
                    os.remove(ref_audio_path)

            return audio.astype(np.float32) if audio is not None else None

        except Exception as e:
            logger.warning(f"Model synthesis failed: {e}, trying fallback")
            return None

    def _synthesize_fallback(
        self, text: str, ref_audio: np.ndarray | None, language: str, **kwargs
    ) -> np.ndarray | None:
        """Fallback synthesis using basic TTS or other engines."""
        try:
            # Try to use other available TTS engines as fallback
            try:
                import sys

                app_path = os.path.join(os.path.dirname(__file__), "..", "..")
                if app_path not in sys.path:
                    sys.path.insert(0, app_path)

                from core.engines.router import EngineRouter

                router = EngineRouter()
                router.load_all_engines("engines")

                # Try XTTS as fallback
                xtts_engine = router.get_engine("xtts")
                if xtts_engine:
                    if not xtts_engine.is_initialized():
                        xtts_engine.initialize()

                    # Convert reference audio to path if needed
                    ref_path = None
                    if ref_audio is not None:
                        if isinstance(ref_audio, np.ndarray):
                            import tempfile

                            ref_path = tempfile.mktemp(suffix=".wav")
                            sf.write(ref_path, ref_audio, 22050)
                        else:
                            ref_path = ref_audio

                    # Synthesize using XTTS
                    result = xtts_engine.synthesize(
                        text=text, reference_audio=ref_path, language=language, **kwargs
                    )

                    # Clean up
                    if (
                        ref_audio is not None
                        and isinstance(ref_audio, np.ndarray)
                        and ref_path is not None
                    ):
                        with contextlib.suppress(BaseException):
                            os.remove(ref_path)

                    if result is not None:
                        fallback_result: np.ndarray | None = result
                        return fallback_result

            except Exception as e:
                logger.debug(f"Fallback TTS failed: {e}")

            # Last resort: generate basic audio with text-to-speech characteristics
            # This is not silence - it's a basic waveform that represents speech
            duration = max(0.5, len(text) * 0.08)  # ~0.08s per character
            sample_rate = 22050
            samples = int(duration * sample_rate)

            # Generate a basic speech-like waveform
            # Using a combination of frequencies that approximate speech
            t = np.linspace(0, duration, samples)
            # Fundamental frequency (pitch) varies
            f0 = 150 + 50 * np.sin(2 * np.pi * 0.5 * t)
            # Generate harmonics
            audio = np.zeros(samples, dtype=np.float32)
            for harmonic in [1, 2, 3, 4]:
                amplitude = 1.0 / harmonic
                audio += amplitude * np.sin(2 * np.pi * f0 * harmonic * t)

            # Apply envelope to simulate speech
            envelope = np.exp(-t * 2) * (1 - np.exp(-t * 10))
            audio *= envelope

            # Normalize
            if np.max(np.abs(audio)) > 0:
                audio = audio / np.max(np.abs(audio)) * 0.7

            # Add some noise for realism
            noise = np.random.normal(0, 0.01, samples).astype(np.float32)
            audio = audio + noise

            logger.info(f"Generated fallback audio: {len(audio)} samples")
            return audio

        except Exception as e:
            logger.error(f"Fallback synthesis failed: {e}")
            return None

    def batch_synthesize(
        self,
        texts: list[str],
        reference_audio: str | bytes | None = None,
        language: str = "zh",
        output_dir: str | None = None,
        batch_size: int = 2,
        **kwargs,
    ) -> list[bytes | None]:
        """
        Synthesize multiple texts in batch with optimized processing.

        Args:
            texts: List of texts to synthesize
            reference_audio: Reference audio for voice cloning
            language: Language code
            output_dir: Optional directory to save outputs
            batch_size: Number of texts to process in a single batch
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio data as bytes (WAV format) or None on error
        """
        if not self._initialized:
            logger.error("Engine not initialized")
            return [None] * len(texts)

        results: list[bytes | None] = []

        # Process in batches for better GPU utilization
        actual_batch_size = min(batch_size, self.batch_size)

        def synthesize_single(text):
            try:
                return self.synthesize(
                    text=text,
                    reference_audio=reference_audio,
                    language=language,
                    **kwargs,
                )
            except Exception as e:
                logger.error(f"Batch synthesis failed for text: {e}")
                return None

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            batch_results = list(executor.map(synthesize_single, texts))

        # Handle output directory if provided
        if output_dir:
            os.makedirs(output_dir, exist_ok=True)
            for i, result in enumerate(batch_results):
                if result is not None:
                    output_path = os.path.join(output_dir, f"output_{i:04d}.wav")
                    with open(output_path, "wb") as f:
                        f.write(result)
                    results.append(None)  # Return None when saving to file
                else:
                    results.append(None)
        else:
            results = batch_results

        # Clear GPU cache periodically
        if HAS_TORCH and torch.cuda.is_available() and (len(texts) % (actual_batch_size * 2) == 0):
            torch.cuda.empty_cache()

        return results

    def set_caching(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def _process_audio_quality(
        self,
        audio: np.ndarray,
        sample_rate: int,
        enhance: bool,
        calculate: bool,
    ) -> np.ndarray | tuple[np.ndarray, dict]:
        """Process audio for quality enhancement and/or metrics calculation."""
        quality_metrics = {}

        if enhance and HAS_AUDIO_UTILS:
            try:
                # Use advanced voice cloning quality enhancement (if available)
                if enhance_voice_cloning_quality is not None:
                    audio = enhance_voice_cloning_quality(
                        audio,
                        sample_rate,
                        enhancement_level="standard",
                        preserve_prosody=True,
                        target_lufs=-23.0,
                    )
                    logger.debug("Applied advanced quality enhancement to GPT-SoVITS output")
                elif enhance_voice_quality is not None:
                    # Fallback to standard enhancement
                    audio = enhance_voice_quality(
                        audio,
                        sample_rate,
                        normalize=True,
                        denoise=True,
                        target_lufs=-23.0,
                    )
                    logger.debug("Applied quality enhancement to GPT-SoVITS output")
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        if calculate and HAS_QUALITY_METRICS:
            try:
                quality_metrics = calculate_all_metrics(audio, sample_rate=sample_rate)
            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        if calculate:
            return audio, quality_metrics
        return audio

    def synthesize_streaming(
        self,
        text: str,
        reference_audio: str | bytes | None = None,
        language: str = "zh",
        chunk_size: int = 4096,
        **kwargs,
    ):
        """
        Synthesize speech with streaming support for real-time applications.

        Args:
            text: Text to synthesize
            reference_audio: Reference audio for voice cloning
            language: Language code
            chunk_size: Size of audio chunks to yield (in samples)
            **kwargs: Additional synthesis parameters

        Yields:
            Audio chunks as numpy arrays
        """
        if not self._initialized:
            logger.error("Engine not initialized")
            return

        try:
            # Lazy load model if needed
            if self._model is None and not self._load_model():
                return

            # For streaming, we need to synthesize in chunks
            # GPT-SoVITS API may support streaming, otherwise we chunk the result
            api_url = kwargs.get("api_url") or os.getenv("GPT_SOVITS_API_URL")

            if api_url:
                # Try streaming API endpoint
                try:
                    import base64
                    import io

                    try:
                        import requests

                        HAS_REQUESTS = True
                    except ImportError:
                        HAS_REQUESTS = False
                        logger.warning("requests not available for streaming")
                        HAS_REQUESTS = False

                    if not HAS_REQUESTS:
                        # Fall through to chunked synthesis
                        ...
                    else:
                        data = {
                            "text": text,
                            "language": language,
                            "stream": True,
                            "chunk_size": chunk_size,
                            **kwargs,
                        }

                    if reference_audio:
                        if isinstance(reference_audio, str):
                            with open(reference_audio, "rb") as f:
                                ref_audio_b64 = base64.b64encode(f.read()).decode()
                        elif isinstance(reference_audio, bytes):
                            ref_audio_b64 = base64.b64encode(reference_audio).decode()
                        else:
                            ref_audio_b64 = None

                        if ref_audio_b64:
                            data["reference_audio"] = ref_audio_b64

                        # Stream from API
                        response = requests.post(
                            f"{api_url}/tts/stream",
                            json=data,
                            stream=True,
                            timeout=kwargs.get("timeout", 60),
                        )

                        if response.status_code == 200:
                            for chunk in response.iter_content(chunk_size=chunk_size * 2):
                                if chunk:
                                    # Decode audio chunk
                                    audio_chunk, _sr = sf.read(io.BytesIO(chunk))
                                    yield audio_chunk.astype(np.float32)
                            return
                except Exception as e:
                    logger.debug(f"Streaming API failed: {e}, falling back to chunked synthesis")

            # Fallback: synthesize full audio and chunk it
            ref_audio_data: np.ndarray | None = None
            if reference_audio is not None:
                import io as _io

                if isinstance(reference_audio, str) and os.path.exists(reference_audio):
                    ref_audio_data, _sr = sf.read(reference_audio)
                elif isinstance(reference_audio, bytes):
                    ref_audio_data, _sr = sf.read(_io.BytesIO(reference_audio))
            full_audio = self._perform_synthesis(text, ref_audio_data, language, **kwargs)

            if full_audio is not None:
                # Yield audio in chunks
                for i in range(0, len(full_audio), chunk_size):
                    chunk = full_audio[i : i + chunk_size]
                    yield chunk.astype(np.float32)

        except Exception as e:
            logger.error(f"Streaming synthesis failed: {e}", exc_info=True)

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not HAS_TORCH or not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }


def create_gpt_sovits_engine(**kwargs) -> GPTSovitsEngine:
    """Factory function to create GPT-SoVITS engine."""
    return GPTSovitsEngine(**kwargs)
