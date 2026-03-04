"""
Parakeet Engine for VoiceStudio
Fast and efficient TTS integration

Parakeet (PaddleSpeech TTS) is a fast and efficient text-to-speech system
with support for multiple languages and high-quality synthesis.

Compatible with:
- Python 3.10+
- paddlepaddle>=2.4.0
- paddlespeech>=1.2.0
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
import soundfile as sf
import torch

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=1536.0)  # 1.5GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    logger.debug("General model cache not available, using Parakeet-specific cache")

# Fallback: Parakeet-specific cache (for backward compatibility)
_PARAKET_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of TTS executors to cache in memory


def _get_cache_key(model_name: str, device: str) -> str:
    """Generate cache key for Parakeet model."""
    return f"parakeet::{model_name}::{device}"


def _get_cached_parakeet_model(model_name: str, device: str):
    """Get cached Parakeet TTS executor if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("parakeet", model_name, device=device)
        if cached is not None:
            return cached

    # Fallback to Parakeet-specific cache
    cache_key = _get_cache_key(model_name, device)
    if cache_key in _PARAKET_MODEL_CACHE:
        _PARAKET_MODEL_CACHE.move_to_end(cache_key)
        return _PARAKET_MODEL_CACHE[cache_key]
    return None


def _cache_parakeet_model(model_name: str, device: str, tts_engine):
    """Cache Parakeet TTS executor with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("parakeet", model_name, tts_engine, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Parakeet-specific cache
    cache_key = _get_cache_key(model_name, device)

    if cache_key in _PARAKET_MODEL_CACHE:
        _PARAKET_MODEL_CACHE.move_to_end(cache_key)
        return

    _PARAKET_MODEL_CACHE[cache_key] = tts_engine

    # Evict oldest if cache full
    if len(_PARAKET_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_engine = _PARAKET_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_engine
            logger.debug(f"Evicted Parakeet model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Parakeet model from cache: {e}")

    logger.debug(f"Cached Parakeet model: {cache_key} (cache size: {len(_PARAKET_MODEL_CACHE)})")


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


class ParakeetEngine(EngineProtocol):
    """
    Parakeet (PaddleSpeech) Engine for fast and efficient text-to-speech synthesis.

    Supports:
    - Fast inference
    - Multiple languages (Chinese, English, etc.)
    - High-quality synthesis
    - Multiple voices
    """

    # Supported languages
    SUPPORTED_LANGUAGES = ["zh", "en", "zh-cn", "zh-tw", "en-us", "en-gb"]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 22050

    def __init__(
        self,
        model_name: str = "fastspeech2_cnndecoder_csmsc",
        language: str = "zh",
        device: str | None = None,
        gpu: bool = True,
    ):
        """
        Initialize Parakeet engine.

        Args:
            model_name: Parakeet/PaddleSpeech model name
            language: Default language code
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
        """
        super().__init__(device=device, gpu=gpu)

        self.model_name = model_name
        self.default_language = language
        self.tts_engine = None
        self.sample_rate = self.DEFAULT_SAMPLE_RATE
        self.lazy_load = True
        self.batch_size = 4
        self._caching_enabled = True

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_engine = _get_cached_parakeet_model(self.model_name, self.device)
            if cached_engine is not None:
                logger.debug(f"Using cached Parakeet TTS executor: {self.model_name}")
                self.tts_engine = cached_engine
                self.sample_rate = 22050  # Default for most Parakeet models
                self._initialized = True
                return True

        # Try importing PaddleSpeech
        try:
            from paddlespeech.cli.tts import TTSExecutor
        except ImportError:
            logger.error("PaddleSpeech not installed. Install with: pip install paddlespeech")
            logger.error("Also requires: pip install paddlepaddle")
            self._initialized = False
            return False

        try:
            # Initialize TTS executor
            self.tts_engine = TTSExecutor()

            # Get sample rate from model config if available
            # PaddleSpeech typically uses 22050 or 24000 Hz
            self.sample_rate = 22050  # Default for most Parakeet models

            # Cache TTS executor
            if self._caching_enabled:
                _cache_parakeet_model(self.model_name, self.device, self.tts_engine)

            logger.info(
                f"Parakeet/PaddleSpeech model loaded successfully (sample_rate: {self.sample_rate})"
            )
            self._initialized = True
            return True

        except Exception as e:
            logger.error(f"Failed to load Parakeet model: {e}")
            self._initialized = False
            return False

    def initialize(self) -> bool:
        """
        Initialize the Parakeet TTS model.

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

            logger.info(f"Loading Parakeet/PaddleSpeech model: {self.model_name} on {self.device}")
            return self._load_model()

        except Exception as e:
            logger.error(f"Failed to initialize Parakeet engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        language: str = "zh",
        voice: str | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using Parakeet/PaddleSpeech.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'zh', 'en')
            voice: Voice/speaker ID (optional)
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - speed: Speech speed (0.5-2.0, default 1.0)
                - am: Acoustic model name
                - voc: Vocoder name

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return None

        try:
            # Use provided language or default
            lang = language if language in self.SUPPORTED_LANGUAGES else self.default_language

            # Get speed
            speed = kwargs.get("speed", 1.0)

            # Get model names
            am = kwargs.get("am", self.model_name)
            voc = kwargs.get("voc", "pwg_csmsc")  # Default vocoder

            # Synthesize using PaddleSpeech
            if output_path:
                output_path = str(output_path)
                if self.tts_engine is None:
                    logger.error("PaddleSpeech TTS engine not loaded")
                    return None
                # Use TTS executor to synthesize to file
                self.tts_engine(
                    text=text,
                    output=output_path,
                    am=am,
                    voc=voc,
                    lang=lang,
                    spk_id=0,  # Default speaker
                )

                # Read generated audio
                audio, sample_rate = sf.read(output_path)

                # Convert to mono if stereo
                if len(audio.shape) > 1:
                    audio = np.mean(audio, axis=1)

                # Convert to float32
                if audio.dtype != np.float32:
                    audio = audio.astype(np.float32)

                # Normalize
                if np.max(np.abs(audio)) > 0:
                    audio = audio / np.max(np.abs(audio)) * 0.95

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
                        audio, sample_rate, None, enhance_quality, calculate_quality
                    )
                    if isinstance(audio, tuple):
                        enhanced_audio, quality_metrics = audio
                        sf.write(output_path, enhanced_audio, sample_rate)
                        return None, quality_metrics
                    else:
                        sf.write(output_path, audio, sample_rate)

                logger.info(f"Audio saved to: {output_path}")
                return None
            else:
                # Synthesize to intermediate file then read
                import tempfile

                with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_file:
                    tmp_path = tmp_file.name

                try:
                    if self.tts_engine is None:
                        logger.error("PaddleSpeech TTS engine not loaded")
                        return None
                    # Synthesize to temp file
                    self.tts_engine(text=text, output=tmp_path, am=am, voc=voc, lang=lang, spk_id=0)

                    # Read audio
                    audio, sample_rate = sf.read(tmp_path)

                    # Convert to mono if stereo
                    if len(audio.shape) > 1:
                        audio = np.mean(audio, axis=1)

                    # Convert to float32
                    if audio.dtype != np.float32:
                        audio = audio.astype(np.float32)

                    # Normalize
                    if np.max(np.abs(audio)) > 0:
                        audio = audio / np.max(np.abs(audio)) * 0.95

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
                            audio, sample_rate, None, enhance_quality, calculate_quality
                        )
                        if isinstance(audio, tuple):
                            return audio  # (audio, quality_metrics)

                    return np.asarray(audio)

                finally:
                    # Cleanup temp file
                    try:
                        if os.path.exists(tmp_path):
                            os.unlink(tmp_path)
                    except Exception as e:
                        logger.warning(f"Failed to cleanup temp file: {e}")

        except Exception as e:
            logger.error(f"Parakeet synthesis failed: {e}")
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

        # PaddleSpeech typically has speaker IDs (0, 1, 2, etc.)
        voices = []
        if language:
            # Return speakers for specific language
            for i in range(5):  # Typically 0-4 speakers per language
                voices.append(f"{language}_spk_{i}")
        else:
            # Return default speakers
            for lang in ["zh", "en"]:
                voices.append(f"{lang}_spk_0")

        return voices

    def get_languages(self) -> list[str]:
        """Get available languages."""
        return self.SUPPORTED_LANGUAGES

    def batch_synthesize(
        self,
        texts: list[str],
        language: str = "zh",
        voice: str | None = None,
        output_dir: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        batch_size: int = 4,
        **kwargs,
    ) -> list[np.ndarray | None | tuple[np.ndarray | None, dict]]:
        """
        Synthesize multiple texts in batch with optimized processing.

        Args:
            texts: List of texts to synthesize
            language: Language code
            voice: Voice/speaker ID
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

        results: list[np.ndarray | None | tuple[np.ndarray | None, dict]] = []

        # Process in batches for better resource utilization
        actual_batch_size = min(batch_size, self.batch_size)
        (len(texts) + actual_batch_size - 1) // actual_batch_size

        def synthesize_single(text):
            try:
                return self.synthesize(
                    text=text,
                    language=language,
                    voice=voice,
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

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def cleanup(self):
        """Clean up resources."""
        try:
            # Don't delete cached engine, just clear reference
            self.tts_engine = None

            # Clear CUDA cache if using GPU
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("Parakeet engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during Parakeet cleanup: {e}")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model_name": self.model_name,
                "default_language": self.default_language,
                "sample_rate": self.sample_rate,
                "supported_languages": len(self.SUPPORTED_LANGUAGES),
            }
        )
        return info


def create_parakeet_engine(
    model_name: str = "fastspeech2_cnndecoder_csmsc",
    language: str = "zh",
    device: str | None = None,
    gpu: bool = True,
) -> ParakeetEngine:
    """Factory function to create a Parakeet TTS engine instance."""
    return ParakeetEngine(model_name=model_name, language=language, device=device, gpu=gpu)
