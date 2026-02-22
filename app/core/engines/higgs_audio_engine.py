"""
Higgs Audio Engine for VoiceStudio
High-fidelity, zero-shot TTS integration

Higgs Audio is a high-fidelity, zero-shot text-to-speech system that provides
exceptional quality voice synthesis with voice cloning capabilities.

Compatible with:
- Python 3.10+
- torch>=2.0.0
- transformers>=4.20.0
- higgs-audio package or direct model loading
"""

from __future__ import annotations

import hashlib
import logging
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import numpy as np
import soundfile as sf
import torch

# Try importing general model cache
_model_cache: Any = None
HAS_MODEL_CACHE = False

try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:  # ALLOWED: bare except - optional model cache
    pass

logger = logging.getLogger(__name__)

# Log cache availability
if not HAS_MODEL_CACHE:
    logger.debug("General model cache not available, using Higgs Audio-specific cache")

# Fallback: Higgs Audio-specific cache (for backward compatibility)
_HIGGS_AUDIO_MODEL_CACHE: OrderedDict = OrderedDict()
_HIGGS_AUDIO_SPEAKER_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory
_MAX_SPEAKER_CACHE_SIZE = 50  # Maximum number of speaker embeddings to cache


def _get_cache_key(model_name: str, device: str) -> str:
    """Generate cache key for Higgs Audio model."""
    return f"higgs_audio::{model_name}::{device}"


def _get_cached_higgs_audio_model(model_name: str, device: str):
    """Get cached Higgs Audio model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("higgs_audio", model_name, device=device)
        if cached is not None:
            return cached

    # Fallback to Higgs Audio-specific cache
    cache_key = _get_cache_key(model_name, device)
    if cache_key in _HIGGS_AUDIO_MODEL_CACHE:
        _HIGGS_AUDIO_MODEL_CACHE.move_to_end(cache_key)
        return _HIGGS_AUDIO_MODEL_CACHE[cache_key]
    return None


def _cache_higgs_audio_model(model_name: str, device: str, models: dict):
    """Cache Higgs Audio model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("higgs_audio", model_name, models, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Higgs Audio-specific cache
    cache_key = _get_cache_key(model_name, device)

    if cache_key in _HIGGS_AUDIO_MODEL_CACHE:
        _HIGGS_AUDIO_MODEL_CACHE.move_to_end(cache_key)
        return

    _HIGGS_AUDIO_MODEL_CACHE[cache_key] = models

    # Evict oldest if cache full
    if len(_HIGGS_AUDIO_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_models = _HIGGS_AUDIO_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_models
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted Higgs Audio model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Higgs Audio model from cache: {e}")

    logger.debug(
        f"Cached Higgs Audio model: {cache_key} (cache size: {len(_HIGGS_AUDIO_MODEL_CACHE)})"
    )


# Optional quality metrics import
try:
    from .quality_metrics import (
        calculate_all_metrics,
        calculate_mos_score,
        calculate_naturalness,
        calculate_similarity,
    )

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False

# Optional audio utilities import for quality enhancement
try:
    from app.core.audio.audio_utils import (
        enhance_voice_quality,
        match_voice_profile,
        normalize_lufs,
        remove_artifacts,
    )

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False

# Import base protocol from canonical source
from .base import EngineProtocol


class HiggsAudioEngine(EngineProtocol):
    """
    Higgs Audio Engine for high-fidelity, zero-shot text-to-speech synthesis.

    Supports:
    - High-fidelity voice synthesis
    - Zero-shot voice cloning
    - Multiple languages
    - Natural-sounding speech
    """

    # Supported languages
    SUPPORTED_LANGUAGES = ["en", "es", "fr", "de", "it", "pt", "ru", "zh", "ja", "ko"]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self, model_name: str = "higgs-audio/base", device: str | None = None, gpu: bool = True
    ):
        """
        Initialize Higgs Audio engine.

        Args:
            model_name: Higgs Audio model identifier
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
        """
        super().__init__(device=device, gpu=gpu)

        self.model_name = model_name
        self.model: Any = None
        self.tokenizer: Any = None
        self.vocoder: Any = None
        self.sample_rate = self.DEFAULT_SAMPLE_RATE
        self.lazy_load = True
        self.batch_size = 2
        self._caching_enabled = True

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_models = _get_cached_higgs_audio_model(self.model_name, self.device)
            if cached_models is not None:
                logger.debug(f"Using cached Higgs Audio model: {self.model_name}")
                self.model = cached_models.get("model")
                self.tokenizer = cached_models.get("tokenizer")
                self.vocoder = cached_models.get("vocoder")
                if hasattr(self.model, "config") and hasattr(self.model.config, "sample_rate"):
                    self.sample_rate = self.model.config.sample_rate
                else:
                    self.sample_rate = self.DEFAULT_SAMPLE_RATE
                self._initialized = True
                return True

        # Try importing transformers and loading Higgs Audio
        try:
            from transformers import AutoModel, AutoTokenizer
        except ImportError:
            logger.error("transformers not installed. Install with: pip install transformers")
            self._initialized = False
            return False

        try:
            # Try loading from HuggingFace
            model_id = (
                "higgs-audio/higgs" if "higgs" in self.model_name.lower() else self.model_name
            )

            # Load tokenizer
            self.tokenizer = AutoTokenizer.from_pretrained(model_id, trust_remote_code=True)

            # Load model
            self.model = AutoModel.from_pretrained(
                model_id,
                trust_remote_code=True,
                torch_dtype=torch.float16 if self.device == "cuda" else torch.float32,
            )
            self.model = self.model.to(self.device)
            self.model.eval()

            # Get sample rate from model config if available
            if hasattr(self.model, "config") and hasattr(self.model.config, "sample_rate"):
                self.sample_rate = self.model.config.sample_rate

            # Cache models
            if self._caching_enabled:
                _cache_higgs_audio_model(
                    self.model_name,
                    self.device,
                    {"model": self.model, "tokenizer": self.tokenizer, "vocoder": self.vocoder},
                )

            logger.info(f"Higgs Audio model loaded successfully (sample_rate: {self.sample_rate})")
            self._initialized = True
            return True

        except Exception as e:
            logger.error(f"Failed to load Higgs Audio model: {e}")
            logger.info("Trying alternative loading method...")

            # Alternative: Try direct Higgs Audio package
            try:
                import higgs_audio

                self.model = higgs_audio.HiggsAudio()
                self.sample_rate = 24000

                # Cache model
                if self._caching_enabled:
                    _cache_higgs_audio_model(
                        self.model_name,
                        self.device,
                        {"model": self.model, "tokenizer": None, "vocoder": None},
                    )

                logger.info("Higgs Audio loaded via higgs-audio package")
                self._initialized = True
                return True
            except ImportError:
                logger.error("higgs-audio package not found. Install with: pip install higgs-audio")
                logger.error("Or use transformers with model: higgs-audio/higgs")
                self._initialized = False
                return False
            except Exception as e2:
                logger.error(f"Alternative loading also failed: {e2}")
                self._initialized = False
                return False

    def initialize(self) -> bool:
        """
        Initialize the Higgs Audio model.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
                return True

            logger.info(f"Loading Higgs Audio model: {self.model_name} on {self.device}")
            return self._load_model()

        except Exception as e:
            logger.error(f"Failed to initialize Higgs Audio engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        language: str = "en",
        voice: str | None = None,
        speaker_wav: str | Path | list[str | Path] | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using Higgs Audio.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'en', 'es', 'fr')
            voice: Voice/speaker ID (optional, uses speaker_wav if provided)
            speaker_wav: Reference audio for voice cloning (optional)
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - speed: Speech speed (0.5-2.0, default 1.0)
                - temperature: Generation temperature (0.1-2.0, default 1.0)

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return None

        try:
            # Get speed
            speed = kwargs.get("speed", 1.0)

            # Get temperature
            temperature = kwargs.get("temperature", 1.0)

            # Prepare speaker audio if provided
            speaker_audio = None
            if speaker_wav:
                if isinstance(speaker_wav, (str, Path)):
                    speaker_audio = str(speaker_wav)
                elif isinstance(speaker_wav, list) and len(speaker_wav) > 0:
                    speaker_audio = str(speaker_wav[0])

            # Synthesize using transformers model
            if self.tokenizer is not None and self.model is not None:
                # Tokenize text
                inputs = self.tokenizer(text, return_tensors="pt", padding=True)
                inputs = {k: v.to(self.device) for k, v in inputs.items()}

                # Add speaker audio if provided (with caching)
                if speaker_audio:
                    # Check speaker audio cache
                    speaker_hash = hashlib.md5(speaker_audio.encode()).hexdigest()
                    if speaker_hash in _HIGGS_AUDIO_SPEAKER_CACHE:
                        logger.debug("Using cached speaker audio")
                        _HIGGS_AUDIO_SPEAKER_CACHE.move_to_end(speaker_hash)  # LRU update
                        ref_audio_tensor = _HIGGS_AUDIO_SPEAKER_CACHE[speaker_hash]
                        inputs["speaker_audio"] = ref_audio_tensor
                    else:
                        # Load and process speaker audio
                        try:
                            ref_audio, ref_sr = sf.read(speaker_audio)
                            # Convert to mono if stereo
                            if len(ref_audio.shape) > 1:
                                ref_audio = np.mean(ref_audio, axis=1)
                            # Resample if needed
                            if ref_sr != self.sample_rate:
                                try:
                                    import librosa

                                    ref_audio = librosa.resample(
                                        ref_audio, orig_sr=ref_sr, target_sr=self.sample_rate
                                    )
                                except ImportError:
                                    logger.warning("librosa not available for resampling")
                            # Convert to tensor
                            ref_audio_tensor = (
                                torch.tensor(ref_audio, dtype=torch.float32)
                                .unsqueeze(0)
                                .to(self.device)
                            )
                            inputs["speaker_audio"] = ref_audio_tensor

                            # Cache speaker audio (LRU)
                            if len(_HIGGS_AUDIO_SPEAKER_CACHE) >= _MAX_SPEAKER_CACHE_SIZE:
                                oldest_key = next(iter(_HIGGS_AUDIO_SPEAKER_CACHE))
                                del _HIGGS_AUDIO_SPEAKER_CACHE[oldest_key]
                            _HIGGS_AUDIO_SPEAKER_CACHE[speaker_hash] = ref_audio_tensor
                            _HIGGS_AUDIO_SPEAKER_CACHE.move_to_end(speaker_hash)  # LRU update
                        except Exception as e:
                            logger.warning(f"Failed to load speaker audio: {e}")

                # Generate audio with inference mode for better performance
                with torch.inference_mode():  # Faster than no_grad
                    if hasattr(self.model, "generate"):
                        # Use generate method if available
                        outputs = self.model.generate(
                            **inputs, language=language, temperature=temperature, max_length=512
                        )
                    else:
                        # Use forward pass
                        outputs = self.model(**inputs)

                    # Extract audio from outputs
                    if isinstance(outputs, dict):
                        audio = outputs.get("audio", outputs.get("waveform", None))
                    elif isinstance(outputs, tuple):
                        audio = outputs[0]
                    else:
                        audio = outputs

                    if audio is None:
                        logger.error("Higgs Audio did not return audio")
                        return None

                    # Convert to numpy
                    if isinstance(audio, torch.Tensor):
                        audio = audio.cpu().numpy()

                    # Handle batch dimension
                    if len(audio.shape) > 1:
                        if audio.shape[0] == 1:
                            audio = audio[0]
                        else:
                            audio = audio[0]  # Take first item

                    # Ensure mono
                    if len(audio.shape) > 1:
                        audio = np.mean(audio, axis=0)

                    # Convert to float32
                    if audio.dtype != np.float32:
                        audio = audio.astype(np.float32)

                    # Normalize
                    if np.max(np.abs(audio)) > 0:
                        audio = audio / np.max(np.abs(audio)) * 0.95

            else:
                # Try higgs-audio package API
                try:
                    audio = self.model.synthesize(
                        text=text, language=language, speaker_audio=speaker_audio, speed=speed
                    )

                    if isinstance(audio, torch.Tensor):
                        audio = audio.cpu().numpy()

                    # Ensure mono
                    if len(audio.shape) > 1:
                        audio = np.mean(audio, axis=0)

                    # Convert to float32
                    if audio.dtype != np.float32:
                        audio = audio.astype(np.float32)

                    # Normalize
                    if np.max(np.abs(audio)) > 0:
                        audio = audio / np.max(np.abs(audio)) * 0.95

                except Exception as e:
                    logger.error(f"Higgs Audio synthesis failed: {e}")
                    return None

            # Apply speed adjustment if needed
            if speed != 1.0:
                try:
                    import librosa

                    audio = librosa.effects.time_stretch(audio, rate=speed)
                except ImportError:
                    logger.warning("librosa not available for speed adjustment")

            # Apply quality processing if requested
            if enhance_quality or calculate_quality:
                audio = self._process_audio_quality(
                    audio, self.sample_rate, speaker_audio, enhance_quality, calculate_quality
                )
                if isinstance(audio, tuple):
                    enhanced_audio, quality_metrics = audio
                    if output_path:
                        sf.write(output_path, enhanced_audio, self.sample_rate)
                        return None, quality_metrics
                    return enhanced_audio, quality_metrics
                else:
                    if output_path:
                        sf.write(output_path, audio, self.sample_rate)
                        return None
                    return audio

            # Save to file if requested
            if output_path:
                sf.write(output_path, audio, self.sample_rate)
                logger.info(f"Audio saved to: {output_path}")
                return None

            return np.asarray(audio)

        except Exception as e:
            logger.error(f"Higgs Audio synthesis failed: {e}")
            return None

    def _process_audio_quality(
        self,
        audio: np.ndarray,
        sample_rate: int,
        reference_audio: str | Path | None = None,
        enhance: bool = False,
        calculate: bool = False,
    ) -> np.ndarray | tuple[np.ndarray, dict]:
        """Process audio for quality enhancement and/or metrics calculation."""
        quality_metrics = {}

        if enhance and HAS_AUDIO_UTILS:
            try:
                audio = enhance_voice_quality(audio, sample_rate)
                audio = normalize_lufs(audio, sample_rate, target_lufs=-23.0)
                audio = remove_artifacts(audio, sample_rate)
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        if calculate and HAS_QUALITY_METRICS:
            try:
                quality_metrics = calculate_all_metrics(audio, sample_rate=sample_rate)
                if reference_audio:
                    try:
                        ref_audio, ref_sr = sf.read(reference_audio)
                        similarity = calculate_similarity(ref_audio, audio)
                        quality_metrics["similarity"] = similarity
                    except Exception as e:
                        logger.warning(f"Similarity calculation failed: {e}")
            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        if calculate:
            return audio, quality_metrics
        return audio

    def get_voices(self, language: str | None = None) -> list[str]:
        """Get available voices/speakers."""
        if not self._initialized and not self.initialize():
            return []

        # Higgs Audio supports zero-shot cloning, so voices are based on reference audio
        voices = []
        if language:
            voices.append(f"{language}_zero_shot")
        else:
            for lang in ["en", "es", "fr", "de"]:
                voices.append(f"{lang}_zero_shot")

        return voices

    def get_languages(self) -> list[str]:
        """Get available languages."""
        return self.SUPPORTED_LANGUAGES

    def batch_synthesize(
        self,
        texts: list[str],
        language: str = "en",
        voice: str | None = None,
        speaker_wav: str | Path | list[str | Path] | None = None,
        output_dir: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        batch_size: int = 2,
        **kwargs,
    ) -> list[np.ndarray | None | tuple[np.ndarray | None, dict]]:
        """
        Synthesize multiple texts in batch with optimized processing.

        Args:
            texts: List of texts to synthesize
            language: Language code
            voice: Voice/speaker ID
            speaker_wav: Reference audio for voice cloning
            output_dir: Optional directory to save outputs
            enhance_quality: If True, apply quality enhancement
            calculate_quality: If True, return quality metrics
            batch_size: Number of texts to process in a single batch
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio arrays or tuples of (audio, quality_metrics)
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [None] * len(texts)

        results: list[np.ndarray | tuple[np.ndarray | None, dict] | None] = []

        # Process in batches for better GPU utilization
        actual_batch_size = min(batch_size, self.batch_size)

        def synthesize_single(text):
            try:
                return self.synthesize(
                    text=text,
                    language=language,
                    voice=voice,
                    speaker_wav=speaker_wav,
                    enhance_quality=enhance_quality,
                    calculate_quality=calculate_quality,
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
            output_dir = Path(output_dir)
            output_dir.mkdir(parents=True, exist_ok=True)
            for i, result in enumerate(batch_results):
                if result is not None:
                    output_path = output_dir / f"output_{i:04d}.wav"
                    if isinstance(result, tuple):
                        audio, metrics = result
                        if audio is not None:
                            sf.write(str(output_path), audio, self.sample_rate)
                            results.append((None, metrics))
                        else:
                            results.append((None, {}))
                    else:
                        if result is not None:
                            sf.write(str(output_path), result, self.sample_rate)
                            results.append(None)
                        else:
                            results.append(None)
                else:
                    results.append(None)
        else:
            results = batch_results

        # Clear GPU cache periodically
        if torch.cuda.is_available() and (len(texts) % (actual_batch_size * 2) == 0):
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

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }

    def cleanup(self):
        """Clean up resources."""
        try:
            # Don't delete cached models, just clear references
            self.model = None
            self.tokenizer = None
            self.vocoder = None

            # Clear CUDA cache if using GPU
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("Higgs Audio engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during Higgs Audio cleanup: {e}")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model_name": self.model_name,
                "sample_rate": self.sample_rate,
                "supported_languages": len(self.SUPPORTED_LANGUAGES),
                "zero_shot": True,
            }
        )
        return info


def create_higgs_audio_engine(
    model_name: str = "higgs-audio/base", device: str | None = None, gpu: bool = True
) -> HiggsAudioEngine:
    """Factory function to create a Higgs Audio engine instance."""
    return HiggsAudioEngine(model_name=model_name, device=device, gpu=gpu)
