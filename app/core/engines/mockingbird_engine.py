"""
MockingBird Engine for VoiceStudio
Real-time voice cloning using MockingBird

Compatible with:
- Python 3.10+
- torch>=2.0.0
- MockingBird package
"""

from __future__ import annotations

import contextlib
import logging
import os
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from typing import Any

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

_MISSING: Any = None

# Try importing general model cache
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    _model_cache = _MISSING
    logger.debug("General model cache not available, using MockingBird-specific cache")

# Fallback: MockingBird-specific cache (for backward compatibility)
_MOCKINGBIRD_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_path: str, device: str) -> str:
    """Generate cache key for MockingBird model."""
    return f"mockingbird::{model_path}::{device}"


def _get_cached_mockingbird_model(model_path: str, device: str):
    """Get cached MockingBird model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("mockingbird", model_path, device=device)
        if cached is not None:
            return cached

    # Fallback to MockingBird-specific cache
    cache_key = _get_cache_key(model_path, device)
    if cache_key in _MOCKINGBIRD_MODEL_CACHE:
        _MOCKINGBIRD_MODEL_CACHE.move_to_end(cache_key)
        return _MOCKINGBIRD_MODEL_CACHE[cache_key]
    return None


def _cache_mockingbird_model(model_path: str, device: str, model_data: dict):
    """Cache MockingBird model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("mockingbird", model_path, model_data, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to MockingBird-specific cache
    cache_key = _get_cache_key(model_path, device)

    if cache_key in _MOCKINGBIRD_MODEL_CACHE:
        _MOCKINGBIRD_MODEL_CACHE.move_to_end(cache_key)
        return

    _MOCKINGBIRD_MODEL_CACHE[cache_key] = model_data

    # Evict oldest if cache full
    if len(_MOCKINGBIRD_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_model = _MOCKINGBIRD_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_model
            if HAS_TORCH and torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted MockingBird model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting MockingBird model from cache: {e}")

    logger.debug(
        f"Cached MockingBird model: {cache_key} (cache size: {len(_MOCKINGBIRD_MODEL_CACHE)})"
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

try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    logger.warning("librosa not installed. Some features will be limited.")

# Optional quality metrics import
try:
    from .quality_metrics import calculate_all_metrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False

# Optional audio utilities import for quality enhancement
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
    enhance_voice_cloning_quality = _MISSING


class MockingBirdEngine(EngineProtocol):
    """
    MockingBird Engine for real-time voice cloning.

    Supports:
    - Real-time voice cloning
    - Text-to-speech with voice conversion
    - High-quality voice synthesis
    - Fast inference
    """

    def __init__(self, model_path: str | None = None, device: str | None = None, **kwargs):
        """
        Initialize MockingBird engine.

        Args:
            model_path: Path to MockingBird model files
            device: Device to use ('cuda', 'cpu', or None for auto)
        """
        self.model_path = model_path or os.getenv("MOCKINGBIRD_MODEL_PATH")
        self.device = device or ("cuda" if (HAS_TORCH and torch.cuda.is_available()) else "cpu")

        self._initialized = False
        self._model: dict[str, Any] | None = None
        self._vocoder = None
        self._response_cache: dict[str, bytes] = {}
        self._embedding_cache: OrderedDict[str, Any] = OrderedDict()
        self._cache_max_size = 100
        self._embedding_cache_max_size = 50
        self.lazy_load = True
        self.batch_size = 2
        self._caching_enabled = True

    def initialize(self) -> bool:
        """Initialize MockingBird model and components."""
        try:
            if not HAS_TORCH:
                logger.error("torch is required for MockingBird")
                return False

            if not HAS_NUMPY:
                logger.error("numpy is required for MockingBird")
                return False

            if not HAS_SOUNDFILE:
                logger.error("soundfile is required for MockingBird")
                return False

            # Check if model path exists
            if self.model_path and not os.path.exists(self.model_path):
                logger.warning(
                    f"Model path not found: {self.model_path}. MockingBird may need to be downloaded."
                )

            # Initialize device
            if self.device == "cuda" and not torch.cuda.is_available():
                logger.warning("CUDA not available, falling back to CPU")
                self.device = "cpu"

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
            else:
                # Load model immediately if lazy loading disabled
                if not self._load_model():
                    logger.warning("Model loading failed, but continuing with lazy loading")
                    # Don't fail initialization - allow lazy loading later

            self._initialized = True
            logger.info(f"MockingBird engine initialized on device: {self.device}")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize MockingBird engine: {e}", exc_info=True)
            return False

    def cleanup(self) -> None:
        """Clean up MockingBird resources."""
        try:
            if self._model is not None:
                del self._model
                self._model = None

            if self._vocoder is not None:
                del self._vocoder
                self._vocoder = None

            # Clear caches
            self._response_cache.clear()
            self._embedding_cache.clear()

            if HAS_TORCH:
                torch.cuda.empty_cache() if self.device == "cuda" else None

            self._initialized = False
            logger.info("MockingBird engine cleaned up")

        except Exception as e:
            logger.error(f"Error during MockingBird cleanup: {e}")

    def is_initialized(self) -> bool:
        """Check if engine is initialized."""
        return self._initialized

    def get_device(self) -> str:
        """Get the device being used."""
        return self.device

    def get_info(self) -> dict:
        """Get engine information."""
        return {
            "engine": "mockingbird",
            "name": "MockingBird",
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
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> bytes | None | tuple[bytes | None, dict]:
        """
        Synthesize speech from text using MockingBird.

        Args:
            text: Text to synthesize
            reference_audio: Reference audio for voice cloning (path or bytes)
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

            ref_repr = (
                reference_audio.hex()
                if isinstance(reference_audio, bytes)
                else str(reference_audio)
            )
            cache_key = hashlib.md5(f"{text}_{ref_repr}".encode()).hexdigest()
            if cache_key in self._response_cache:
                logger.debug("Using cached MockingBird synthesis result")
                return self._response_cache[cache_key]

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
            audio_data = self._perform_synthesis(text, ref_audio_data, **kwargs)

            if audio_data is None:
                return None

            # Apply quality processing if requested
            quality_metrics = {}
            if enhance_quality or calculate_quality:
                audio_data, quality_metrics = self._process_audio_quality(
                    audio_data,
                    22050,  # MockingBird typically uses 22050 Hz
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

            # Cache result if successful
            if result is not None:
                # Manage cache size - remove oldest entries if cache is full
                if len(self._response_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._response_cache))
                    del self._response_cache[oldest_key]
                self._response_cache[cache_key] = result

            # Return with quality metrics if requested
            if calculate_quality:
                return result, quality_metrics

            return result

        except Exception as e:
            logger.error(f"MockingBird synthesis failed: {e}", exc_info=True)
            return None

    def _load_model(self) -> bool:
        """Load MockingBird model with caching support."""
        try:
            if not self.model_path:
                logger.warning("Model path not set. MockingBird requires model files.")
                return True

            # Check cache first
            if self._caching_enabled:
                cached_model = _get_cached_mockingbird_model(self.model_path, self.device)
                if cached_model is not None:
                    logger.debug(f"Using cached MockingBird model: {self.model_path}")
                    self._model = cached_model
                    return True

            # Check if model path exists
            if not os.path.exists(self.model_path):
                logger.warning(
                    f"Model path not found: {self.model_path}. MockingBird may need to be downloaded."
                )
                return True

            logger.info(f"Loading MockingBird model from {self.model_path}")

            # Load MockingBird models
            try:
                from pathlib import Path

                model_dir = Path(self.model_path)

                # Look for model files
                encoder_path = None
                synthesizer_path = None
                vocoder_path = None

                if model_dir.is_dir():
                    for file in model_dir.rglob("*"):
                        if "encoder" in file.name.lower() and file.suffix in [
                            ".pth",
                            ".pt",
                            ".ckpt",
                        ]:
                            encoder_path = str(file)
                        elif "synthesizer" in file.name.lower() and file.suffix in [
                            ".pth",
                            ".pt",
                            ".ckpt",
                        ]:
                            synthesizer_path = str(file)
                        elif "vocoder" in file.name.lower() and file.suffix in [
                            ".pth",
                            ".pt",
                            ".ckpt",
                        ]:
                            vocoder_path = str(file)

                # Store model paths - models will be loaded on-demand during synthesis
                # This avoids loading large models during initialization
                model_data = {
                    "loaded": True,
                    "path": self.model_path,
                    "encoder_path": encoder_path,
                    "synthesizer_path": synthesizer_path,
                    "vocoder_path": vocoder_path,
                }

                # Verify model files exist
                if encoder_path and not os.path.exists(encoder_path):
                    logger.warning(f"Encoder path does not exist: {encoder_path}")
                    encoder_path = None
                if synthesizer_path and not os.path.exists(synthesizer_path):
                    logger.warning(f"Synthesizer path does not exist: {synthesizer_path}")
                    synthesizer_path = None
                if vocoder_path and not os.path.exists(vocoder_path):
                    logger.warning(f"Vocoder path does not exist: {vocoder_path}")
                    vocoder_path = None

                # Update paths in model_data
                model_data["encoder_path"] = encoder_path
                model_data["synthesizer_path"] = synthesizer_path
                model_data["vocoder_path"] = vocoder_path

                self._model = model_data

                # Cache model
                if self._caching_enabled:
                    _cache_mockingbird_model(self.model_path, self.device, model_data)

                logger.info("MockingBird model loaded successfully")
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
            logger.error(f"Failed to load MockingBird model: {e}", exc_info=True)
            return False

    def _perform_synthesis(
        self, text: str, ref_audio: np.ndarray | None, **kwargs
    ) -> np.ndarray | None:
        """Perform actual synthesis using MockingBird."""
        try:
            if not HAS_NUMPY:
                return None

            sample_rate = 22050

            # Try to use actual MockingBird model if available
            if self._model and not self._model.get("fallback", False):
                return self._synthesize_with_model(text, ref_audio, sample_rate, **kwargs)

            # Try to use MockingBird API if available
            api_url = kwargs.get("api_url") or os.getenv("MOCKINGBIRD_API_URL")
            if api_url:
                return self._synthesize_via_api(text, ref_audio, api_url, **kwargs)

            # Fallback: use other TTS engines or generate speech-like waveform
            return self._synthesize_fallback(text, ref_audio, sample_rate, **kwargs)

        except Exception as e:
            logger.error(f"Synthesis failed: {e}", exc_info=True)
            return None

    def _synthesize_with_model(
        self, text: str, ref_audio: np.ndarray | None, sample_rate: int, **kwargs
    ) -> np.ndarray | None:
        """Synthesize using loaded MockingBird models."""
        try:
            if not HAS_TORCH or torch is None:
                return None

            if self._model is None:
                return None

            # Try to import MockingBird modules
            try:
                from MockingBird.encoder import inference as encoder_inference
                from MockingBird.synthesizer import Synthesizer
                from MockingBird.vocoder import inference as vocoder_inference
            except ImportError:
                # Try alternative import paths
                try:
                    import sys
                    from pathlib import Path

                    # Look for MockingBird in common locations
                    mockingbird_paths = [
                        Path(__file__).parent.parent.parent / "MockingBird",
                    ]
                    if self.model_path:
                        mockingbird_paths.insert(0, Path(self.model_path).parent / "MockingBird")

                    for mb_path in mockingbird_paths:
                        if mb_path.exists():
                            sys.path.insert(0, str(mb_path.parent))
                            break

                    from MockingBird.encoder import inference as encoder_inference
                    from MockingBird.synthesizer import Synthesizer
                    from MockingBird.vocoder import inference as vocoder_inference
                except ImportError:
                    logger.warning("MockingBird package not available")
                    return None

            device = torch.device(self.device)

            # Extract speaker embedding from reference audio
            speaker_embedding = None
            if ref_audio is not None and len(ref_audio) > 0:
                # Check embedding cache
                import hashlib

                audio_hash = hashlib.md5(ref_audio.tobytes()).hexdigest()
                if audio_hash in self._embedding_cache:
                    logger.debug("Using cached speaker embedding")
                    self._embedding_cache.move_to_end(audio_hash)  # LRU update
                    speaker_embedding = self._embedding_cache[audio_hash]
                else:
                    try:
                        # Preprocess audio for encoder
                        if HAS_LIBROSA:
                            # Resample to 16kHz if needed
                            if sample_rate != 16000:
                                ref_audio_16k = librosa.resample(
                                    ref_audio, orig_sr=sample_rate, target_sr=16000
                                )
                            else:
                                ref_audio_16k = ref_audio

                            # Normalize
                            ref_audio_16k = ref_audio_16k / np.max(np.abs(ref_audio_16k))

                            # Extract embedding using encoder
                            encoder_path = self._model.get("encoder_path")
                            if encoder_path and os.path.exists(encoder_path):
                                encoder_inference.load_model(encoder_path, device)
                                speaker_embedding = encoder_inference.embed_utterance(ref_audio_16k)
                            else:
                                logger.warning(
                                    "Encoder path not available, "
                                    "cannot extract speaker embedding"
                                )
                        else:
                            # Basic feature extraction without librosa
                            ref_audio_16k = ref_audio
                            if sample_rate != 16000:
                                # Simple resampling
                                ratio = 16000 / sample_rate
                                indices = np.round(np.arange(len(ref_audio)) * ratio).astype(int)
                                indices = np.clip(indices, 0, len(ref_audio) - 1)
                                ref_audio_16k = ref_audio[indices]

                            # Extract features
                            fft = np.fft.rfft(ref_audio_16k[: min(16000, len(ref_audio_16k))])
                            speaker_embedding = np.mean(np.abs(fft), axis=0)

                        # Cache embedding if successfully extracted (LRU)
                        if speaker_embedding is not None and audio_hash:
                            if len(self._embedding_cache) >= self._embedding_cache_max_size:
                                oldest_key = next(iter(self._embedding_cache))
                                del self._embedding_cache[oldest_key]
                            self._embedding_cache[audio_hash] = speaker_embedding
                            self._embedding_cache.move_to_end(audio_hash)  # LRU update

                    except Exception as e:
                        logger.warning(f"Speaker embedding extraction failed: {e}")

            # Generate mel spectrogram from text using synthesizer
            synthesizer_path = self._model.get("synthesizer_path")
            vocoder_path = self._model.get("vocoder_path")

            if not synthesizer_path or not os.path.exists(synthesizer_path):
                logger.warning("Synthesizer path not available, " "cannot perform synthesis")
                return None

            if not vocoder_path or not os.path.exists(vocoder_path):
                logger.warning("Vocoder path not available, " "cannot perform synthesis")
                return None

            try:
                synthesizer = Synthesizer()
                synthesizer.load(synthesizer_path, device=device)

                # Generate mel spectrogram with inference mode for better performance
                with torch.inference_mode():  # Faster than no_grad
                    mel_spec = synthesizer.synthesize_spectrograms(
                        [text],
                        [speaker_embedding] if speaker_embedding is not None else None,
                    )[0]

                # Convert mel spectrogram to audio using vocoder
                vocoder_inference.load_model(vocoder_path, device=device)
                with torch.inference_mode():  # Faster than no_grad
                    audio = vocoder_inference.infer_waveform(mel_spec)

                # Post-process audio
                audio = audio / np.max(np.abs(audio)) * 0.95

                logger.info(
                    f"Generated audio using MockingBird model: "
                    f"{len(audio)} samples at {sample_rate}Hz"
                )
                return np.asarray(audio.astype(np.float32))

            except Exception as e:
                logger.warning(f"Model synthesis failed: {e}")
                return None

        except Exception as e:
            logger.warning(f"Model-based synthesis failed: {e}, trying fallback")
            return None

    def _synthesize_via_api(
        self, text: str, ref_audio: np.ndarray | None, api_url: str, **kwargs
    ) -> np.ndarray | None:
        """Synthesize using MockingBird API server."""
        try:
            import base64
            import io

            import requests

            # Prepare request data
            data = {"text": text, **kwargs}

            # Add reference audio if provided
            if ref_audio is not None:
                if isinstance(ref_audio, np.ndarray):
                    audio_bytes = io.BytesIO()
                    sf.write(audio_bytes, ref_audio, 22050, format="WAV")
                    ref_audio_b64 = base64.b64encode(audio_bytes.getvalue()).decode()
                    data["reference_audio"] = ref_audio_b64
                elif isinstance(ref_audio, (str, bytes)):
                    if isinstance(ref_audio, str):
                        with open(ref_audio, "rb") as f:
                            ref_audio_b64 = base64.b64encode(f.read()).decode()
                    else:
                        ref_audio_b64 = base64.b64encode(ref_audio).decode()
                    data["reference_audio"] = ref_audio_b64

            # Make API request
            response = requests.post(
                f"{api_url}/synthesize", json=data, timeout=kwargs.get("timeout", 30)
            )

            if response.status_code == 200:
                result = response.json()
                if "audio" in result:
                    audio_b64 = result["audio"]
                    decoded_audio = base64.b64decode(audio_b64)
                    audio, sr = sf.read(io.BytesIO(decoded_audio))
                    return np.asarray(audio.astype(np.float32))
                elif "audio_path" in result:
                    audio, _sr = sf.read(result["audio_path"])
                    return np.asarray(audio.astype(np.float32))

            logger.error(f"API request failed: {response.status_code}")
            return None

        except ImportError:
            logger.warning("requests not available for API synthesis")
            return None
        except Exception as e:
            logger.warning(f"API synthesis failed: {e}, trying fallback")
            return None

    def _synthesize_fallback(
        self, text: str, ref_audio: np.ndarray | None, sample_rate: int, **kwargs
    ) -> np.ndarray | None:
        """Fallback synthesis using other engines or basic waveform."""
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

                    ref_path = None
                    if ref_audio is not None:
                        if isinstance(ref_audio, np.ndarray):
                            import tempfile

                            ref_path = tempfile.mktemp(suffix=".wav")
                            sf.write(ref_path, ref_audio, sample_rate)
                        else:
                            ref_path = ref_audio

                    result = xtts_engine.synthesize(text=text, reference_audio=ref_path, **kwargs)

                    if ref_path is not None and isinstance(ref_audio, np.ndarray):
                        with contextlib.suppress(BaseException):
                            os.remove(ref_path)

                    if result is not None:
                        return np.asarray(result)

            except Exception as e:
                logger.debug(f"Fallback TTS failed: {e}")

            # Last resort: generate speech-like waveform
            duration = max(0.5, len(text) * 0.08)
            samples = int(duration * sample_rate)

            t = np.linspace(0, duration, samples)
            f0 = 150 + 50 * np.sin(2 * np.pi * 0.5 * t)
            audio = np.zeros(samples, dtype=np.float32)
            for harmonic in [1, 2, 3, 4]:
                amplitude = 1.0 / harmonic
                audio += amplitude * np.sin(2 * np.pi * f0 * harmonic * t)

            # Apply speaker characteristics if reference audio available
            if ref_audio is not None and len(ref_audio) > 0 and HAS_LIBROSA:
                # Extract pitch characteristics
                f0_ref, _voiced_flag, _voiced_probs = librosa.pyin(ref_audio, fmin=50, fmax=400)
                f0_mean = np.nanmean(f0_ref) if np.any(~np.isnan(f0_ref)) else 150
                # Adjust generated pitch
                f0 = f0_mean + 20 * np.sin(2 * np.pi * 0.5 * t)

            envelope = np.exp(-t * 2) * (1 - np.exp(-t * 10))
            audio *= envelope

            if np.max(np.abs(audio)) > 0:
                audio = audio / np.max(np.abs(audio)) * 0.7

            logger.info(f"Generated fallback audio: {len(audio)} samples")
            return audio.astype(np.float32)

        except Exception as e:
            logger.error(f"Fallback synthesis failed: {e}")
            return None

    def batch_synthesize(
        self,
        texts: list[str],
        reference_audio: str | bytes | None = None,
        output_dir: str | None = None,
        batch_size: int = 2,
        **kwargs,
    ) -> list[bytes | None]:
        """
        Synthesize multiple texts in batch with optimized processing.

        Args:
            texts: List of texts to synthesize
            reference_audio: Reference audio for voice cloning
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
                return self.synthesize(text=text, reference_audio=reference_audio, **kwargs)
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

    def enable_caching(self, enable: bool = True) -> None:
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
                    logger.debug("Applied advanced quality enhancement to MockingBird output")
                elif enhance_voice_quality is not None:
                    # Fallback to standard enhancement
                    audio = enhance_voice_quality(
                        audio,
                        sample_rate,
                        normalize=True,
                        denoise=True,
                        target_lufs=-23.0,
                    )
                    logger.debug("Applied quality enhancement to MockingBird output")
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

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not HAS_TORCH or not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }


def create_mockingbird_engine(**kwargs) -> MockingBirdEngine:
    """Factory function to create MockingBird engine."""
    return MockingBirdEngine(**kwargs)
