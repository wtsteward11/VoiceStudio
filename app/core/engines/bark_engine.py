"""
Bark Engine for VoiceStudio
Text-to-speech using Bark (Suno AI) - High-quality, expressive TTS

Compatible with:
- Python 3.10+
- torch>=2.0.0
- bark or suno-bark package
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    logger.debug("General model cache not available, using Bark-specific cache")

# Fallback: Bark-specific cache (for backward compatibility)
_BARK_MODEL_CACHE: OrderedDict = OrderedDict()
_VOICE_CLONING_CACHE: dict[str, Any] = {}
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory
_MAX_VOICE_CLONING_CACHE_SIZE = 50  # Maximum number of voice embeddings to cache


def _get_cached_bark_model(cache_key: str):
    """Get cached Bark model state if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("bark", cache_key, device="cpu")
        if cached is not None:
            return cached

    # Fallback to Bark-specific cache
    if cache_key in _BARK_MODEL_CACHE:
        _BARK_MODEL_CACHE.move_to_end(cache_key)
        return _BARK_MODEL_CACHE[cache_key]
    return None


def _cache_bark_model(cache_key: str, model_state: dict):
    """Cache Bark model state with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("bark", cache_key, model_state, device="cpu")
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Bark-specific cache
    if cache_key in _BARK_MODEL_CACHE:
        _BARK_MODEL_CACHE.move_to_end(cache_key)
        return

    _BARK_MODEL_CACHE[cache_key] = model_state

    # Evict oldest if cache full
    if len(_BARK_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, _ = _BARK_MODEL_CACHE.popitem(last=False)
        logger.debug(f"Evicted Bark model from cache: {oldest_key}")

    logger.debug(f"Cached Bark model: {cache_key} (cache size: {len(_BARK_MODEL_CACHE)})")


def _get_voice_cloning_cache_key(reference_audio: str | Path | np.ndarray) -> str | None:
    """Generate cache key for voice cloning."""
    import hashlib

    if isinstance(reference_audio, (str, Path)):
        audio_path = Path(reference_audio)
        if audio_path.exists():
            mtime = audio_path.stat().st_mtime
            return hashlib.md5(f"{audio_path}::{mtime}".encode()).hexdigest()

    if isinstance(reference_audio, np.ndarray):
        return hashlib.md5(reference_audio.tobytes()).hexdigest()

    return None


def _get_cached_voice_cloning(cache_key: str):
    """Get cached voice cloning prompt if available."""
    return _VOICE_CLONING_CACHE.get(cache_key)


def _cache_voice_cloning(cache_key: str, prompt: Any) -> None:
    """Cache voice cloning prompt with LRU eviction."""
    if cache_key in _VOICE_CLONING_CACHE:
        return

    _VOICE_CLONING_CACHE[cache_key] = prompt

    # Evict oldest if cache full
    if len(_VOICE_CLONING_CACHE) > _MAX_VOICE_CLONING_CACHE_SIZE:
        oldest_key = next(iter(_VOICE_CLONING_CACHE))
        del _VOICE_CLONING_CACHE[oldest_key]
        logger.debug(f"Evicted voice cloning from cache: {oldest_key[:8]}")

    logger.debug(f"Cached voice cloning: {cache_key[:8]} (cache size: {len(_VOICE_CLONING_CACHE)})")


# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

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

# Try to import Bark
try:
    from bark import SAMPLE_RATE, generate_audio, preload_models
    from bark.generation import SUPPORTED_LANGS

    HAS_BARK = True
except ImportError:
    try:
        from bark_voice_cloning import SAMPLE_RATE, generate_audio, preload_models
        from bark_voice_cloning.generation import SUPPORTED_LANGS

        HAS_BARK = True
    except ImportError:
        HAS_BARK = False
        SAMPLE_RATE = 24000
        SUPPORTED_LANGS = ["en"]
        logger.warning(
            "bark not installed. Install with: pip install bark or pip install suno-bark"
        )


class BarkEngine(EngineProtocol):
    """
    Bark Engine for high-quality, expressive text-to-speech.

    Supports:
    - High-quality TTS synthesis
    - Expressive speech with emotion
    - Multiple languages
    - Voice cloning (with reference audio)
    - Music generation
    - Sound effects
    """

    DEFAULT_SAMPLE_RATE = 24000

    # Supported emotions for structured emotion control API
    # Bark uses text prompts for emotion (e.g., [laughing], [sad], etc.)
    SUPPORTED_EMOTIONS = [
        "neutral",
        "happy",
        "sad",
        "angry",
        "excited",
        "fearful",
        "surprised",
        "laughing",
        "whispering",
    ]

    # Emotion prompt mappings for Bark's text-based emotion control
    EMOTION_PROMPTS = {
        "neutral": "",
        "happy": "[happy]",
        "sad": "[sad]",
        "angry": "[angry]",
        "excited": "[excited]",
        "fearful": "[fearful]",
        "surprised": "[surprised]",
        "laughing": "[laughing]",
        "whispering": "[whispering]",
    }

    def __init__(
        self,
        device: str | None = None,
        gpu: bool = True,
        model_path: str | None = None,
    ):
        """
        Initialize Bark engine.

        Args:
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            model_path: Path to Bark model files (optional)
        """
        if not HAS_TORCH:
            raise ImportError("torch is required for Bark. Install with: pip install torch")

        if not HAS_NUMPY:
            raise ImportError("numpy is required for Bark. Install with: pip install numpy")

        super().__init__(device=device, gpu=gpu)

        self.model_path = model_path
        self.model = None
        self._models_loaded = False

        # Caching for performance (LRU cache)
        self._synthesis_cache: OrderedDict = OrderedDict()  # LRU cache for synthesis results
        self._cache_max_size = 100  # Maximum number of cached syntheses
        self.lazy_load = True
        self.batch_size = 2  # Smaller batch size for Bark (memory intensive)
        self._caching_enabled = True

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _load_models(self) -> bool:
        """Load models with caching support."""
        # Check if models are already cached/loaded globally
        cache_key = f"bark::{self.device}::{self.model_path or 'default'}"

        if self._caching_enabled:
            cached_state = _get_cached_bark_model(cache_key)
            if cached_state is not None:
                logger.debug("Using cached Bark model state")
                self._models_loaded = cached_state.get("models_loaded", False)
                self._initialized = True
                return True

        # Use model cache directory if available
        model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
        if not model_cache_dir:
            model_cache_dir = os.path.join(
                os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                "VoiceStudio",
                "models",
                "bark",
            )
        os.makedirs(model_cache_dir, exist_ok=True)

        # Preload Bark models
        try:
            preload_models()
            self._models_loaded = True
            logger.info("Bark models preloaded successfully")

            # Cache model state
            if self._caching_enabled:
                _cache_bark_model(cache_key, {"models_loaded": True})
        except Exception as e:
            logger.warning(f"Failed to preload Bark models: {e}")
            self._models_loaded = False

        self._initialized = True
        logger.info(f"Bark engine initialized (device: {self.device})")
        return True

    def initialize(self) -> bool:
        """Initialize the Bark model."""
        try:
            if self._initialized:
                return True

            if not HAS_BARK:
                logger.error("Bark package not installed")
                return False

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, models will be loaded on first use")
                return True

            logger.info("Initializing Bark engine")
            return self._load_models()

        except Exception as e:
            logger.error(f"Failed to initialize Bark engine: {e}", exc_info=True)
            self._initialized = False
            return False

    def cleanup(self):
        """Clean up resources and free memory."""
        try:
            if self.model is not None:
                del self.model
                self.model = None

            if torch.cuda.is_available():
                torch.cuda.empty_cache()

            # Clear synthesis cache
            self._synthesis_cache.clear()

            self._models_loaded = False
            self._initialized = False
            logger.info("Bark engine cleaned up")
        except Exception as e:
            logger.error(f"Error during Bark engine cleanup: {e}", exc_info=True)

    def batch_synthesize(
        self,
        texts: list[str],
        reference_audio: str | Path | np.ndarray | None = None,
        language: str = "en",
        speaker: str | None = None,
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
            reference_audio: Reference audio for voice cloning
            language: Language code
            speaker: Speaker preset
            output_dir: Optional directory to save outputs
            enhance_quality: If True, apply quality enhancement
            calculate_quality: If True, return quality metrics
            batch_size: Number of texts to process in a single batch
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio arrays or tuples of (audio, quality_metrics)
        """
        # Lazy load models if needed
        if not self._initialized and not self._load_models():
            return [None] * len(texts)

        results: list[np.ndarray | None | tuple[np.ndarray | None, dict]] = []

        # Process in batches for better GPU utilization
        actual_batch_size = min(batch_size, self.batch_size)
        (len(texts) + actual_batch_size - 1) // actual_batch_size

        def synthesize_single(text):
            try:
                return self.synthesize(
                    text=text,
                    reference_audio=reference_audio,
                    language=language,
                    speaker=speaker,
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
                            import soundfile as sf

                            sf.write(str(output_path), audio, self.DEFAULT_SAMPLE_RATE)
                            results.append((None, metrics))
                        else:
                            results.append((None, {}))
                    else:
                        if result is not None:
                            import soundfile as sf

                            sf.write(str(output_path), result, self.DEFAULT_SAMPLE_RATE)
                            results.append(None)
                        else:
                            results.append(None)
                else:
                    results.append(None)
        else:
            results = batch_results

        # Clear GPU cache periodically
        if HAS_TORCH and torch.cuda.is_available() and (len(texts) % (actual_batch_size * 2) == 0):
            torch.cuda.empty_cache()

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not HAS_TORCH or not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }

    def synthesize(
        self,
        text: str,
        reference_audio: str | Path | np.ndarray | None = None,
        language: str = "en",
        speaker: str | None = None,
        emotion: str | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using Bark.

        Args:
            text: Text to synthesize
            reference_audio: Reference audio for voice cloning (optional)
            language: Language code (default: 'en')
            speaker: Speaker preset (optional)
            emotion: Emotion for synthesis (optional). One of: neutral, happy,
                sad, angry, excited, fearful, surprised, laughing, whispering.
                Also available via kwargs['emotion'].
            output_path: Path to save output audio
            enhance_quality: If True, apply quality enhancement
            calculate_quality: If True, return quality metrics
            **kwargs: Additional parameters
                - temperature: Sampling temperature (default: 0.7)
                - semantic_temperature: Semantic temperature (default: 0.7)
                - coarse_temperature: Coarse temperature (default: 0.7)
                - fine_temperature: Fine temperature (default: 0.7)
                - emotion: Emotion for synthesis (alternative to emotion param)

        Returns:
            Audio array or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        # Lazy load models if needed
        if not self._initialized and not self._load_models():
            return None

        try:
            if not HAS_BARK:
                logger.error("Bark package not available")
                return None

            # Check synthesis cache
            import hashlib

            ref_key = (
                str(reference_audio)
                if isinstance(reference_audio, (str, Path))
                else (
                    hashlib.md5(np.array(reference_audio).tobytes()).hexdigest()
                    if reference_audio is not None
                    else "none"
                )
            )
            # Get emotion from parameter or kwargs, validate against supported emotions
            effective_emotion = emotion or kwargs.get("emotion", "neutral")
            if effective_emotion not in self.SUPPORTED_EMOTIONS:
                logger.warning(f"Emotion '{effective_emotion}' not supported, using 'neutral'")
                effective_emotion = "neutral"

            cache_key = hashlib.md5(
                f"{text}_{speaker}_{effective_emotion}_{language}_{ref_key}_{kwargs.get('temperature', 0.7)}_{kwargs.get('fine_temperature', 0.7)}_{enhance_quality}_{calculate_quality}".encode()
            ).hexdigest()

            if cache_key in self._synthesis_cache:
                # Move to end (most recently used)
                self._synthesis_cache.move_to_end(cache_key)
                logger.debug("Using cached Bark synthesis result")
                cached_result = self._synthesis_cache[cache_key]
                if output_path:
                    if isinstance(cached_result, str) and os.path.exists(cached_result):
                        if calculate_quality:
                            # Return cached file path with quality metrics
                            return None, {}
                        return None
                    elif isinstance(cached_result, np.ndarray):
                        # Save cached audio to output path
                        sf.write(output_path, cached_result, self.DEFAULT_SAMPLE_RATE)
                        if calculate_quality:
                            return None, {}
                        return None
                else:
                    if isinstance(cached_result, np.ndarray):
                        if calculate_quality:
                            return cached_result, {}
                        return cached_result

            # Check voice cloning cache for reference audio
            history_prompt = None
            if reference_audio is not None:
                ref_cache_key = _get_voice_cloning_cache_key(reference_audio)
                if ref_cache_key and self._caching_enabled:
                    cached_prompt = _get_cached_voice_cloning(ref_cache_key)
                    if cached_prompt is not None:
                        history_prompt = cached_prompt
                        logger.debug("Using cached voice cloning prompt")
                    else:
                        # Extract voice cloning prompt (Bark handles this internally)
                        history_prompt = reference_audio
                        # Cache it for future use
                        _cache_voice_cloning(ref_cache_key, history_prompt)
                else:
                    history_prompt = reference_audio

            # Prepare text with emotion and speaker prompts
            # Emotion prompt is prepended, speaker prompt wraps the text
            emotion_prompt = self.EMOTION_PROMPTS.get(effective_emotion, "")
            if speaker:
                text_with_speaker = f"[{speaker}] {emotion_prompt} {text}".strip()
            elif emotion_prompt:
                text_with_speaker = f"{emotion_prompt} {text}"
            else:
                text_with_speaker = text

            # Generate audio using Bark (with inference mode if using torch)
            if HAS_TORCH and torch.cuda.is_available() and self.device == "cuda":
                with torch.inference_mode():  # Faster inference
                    audio_array = generate_audio(
                        text_with_speaker,
                        history_prompt=history_prompt,
                        text_temp=kwargs.get("temperature", 0.7),
                        waveform_temp=kwargs.get("fine_temperature", 0.7),
                        **{
                            k: v
                            for k, v in kwargs.items()
                            if k not in ["temperature", "fine_temperature"]
                        },
                    )
            else:
                audio_array = generate_audio(
                    text_with_speaker,
                    history_prompt=history_prompt,
                    text_temp=kwargs.get("temperature", 0.7),
                    waveform_temp=kwargs.get("fine_temperature", 0.7),
                    **{
                        k: v
                        for k, v in kwargs.items()
                        if k not in ["temperature", "fine_temperature"]
                    },
                )

            if audio_array is None:
                logger.error("Bark synthesis returned None")
                return None

            # Convert to numpy array if needed
            if not isinstance(audio_array, np.ndarray):
                audio_array = np.array(audio_array, dtype=np.float32)
            else:
                audio_array = audio_array.astype(np.float32)

            # Normalize audio
            if np.max(np.abs(audio_array)) > 0:
                audio_array = audio_array / np.max(np.abs(audio_array)) * 0.95

            # Apply quality processing if requested
            if enhance_quality or calculate_quality:
                audio_array = self._process_audio_quality(
                    audio_array,
                    self.DEFAULT_SAMPLE_RATE,
                    reference_audio,
                    enhance_quality,
                    calculate_quality,
                )
                if isinstance(audio_array, tuple):
                    enhanced_audio, quality_metrics = audio_array
                    if output_path:
                        sf.write(output_path, enhanced_audio, self.DEFAULT_SAMPLE_RATE)
                        return None, quality_metrics
                    return enhanced_audio, quality_metrics
                else:
                    if output_path:
                        sf.write(output_path, audio_array, self.DEFAULT_SAMPLE_RATE)
                        return None
                    return audio_array

            # Save to file if requested
            if output_path:
                sf.write(output_path, audio_array, self.DEFAULT_SAMPLE_RATE)
                logger.info(f"Audio saved to: {output_path}")
            # Cache file path if successful (LRU eviction)
            if output_path and os.path.exists(output_path):
                if len(self._synthesis_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._synthesis_cache))
                    del self._synthesis_cache[oldest_key]
                self._synthesis_cache[cache_key] = str(output_path)
                if calculate_quality:
                    return None, {}
                return None

            # Cache audio array if not saving to file (LRU eviction)
            if len(self._synthesis_cache) >= self._cache_max_size:
                oldest_key = next(iter(self._synthesis_cache))
                del self._synthesis_cache[oldest_key]
            self._synthesis_cache[cache_key] = audio_array

            if calculate_quality:
                return audio_array, {}
            return np.asarray(audio_array)

        except Exception as e:
            logger.error(f"Bark synthesis failed: {e}", exc_info=True)
            return None

    def _process_audio_quality(
        self,
        audio: np.ndarray,
        sample_rate: int,
        reference_audio: str | Path | np.ndarray | None,
        enhance: bool,
        calculate: bool,
    ) -> np.ndarray | tuple[np.ndarray, dict]:
        """Process audio for quality enhancement and/or metrics calculation."""
        quality_metrics = {}

        if enhance:
            try:
                from app.core.audio.audio_utils import (
                    enhance_voice_quality,
                    normalize_lufs,
                    remove_artifacts,
                )

                audio = enhance_voice_quality(audio, sample_rate)
                audio = normalize_lufs(audio, sample_rate, target_lufs=-23.0)
                audio = remove_artifacts(audio, sample_rate)
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        if calculate:
            try:
                from .quality_metrics import calculate_all_metrics

                quality_metrics = calculate_all_metrics(audio, sample_rate=sample_rate)
            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        if calculate:
            return audio, quality_metrics
        return audio

    def get_supported_languages(self) -> list[str]:
        """Get list of supported languages."""
        if HAS_BARK:
            return list(SUPPORTED_LANGS) if SUPPORTED_LANGS else ["en"]
        return ["en"]

    def get_supported_emotions(self) -> list[str]:
        """
        Get list of supported emotions.

        Returns:
            List of emotion names that can be used with the emotion parameter.
        """
        return self.SUPPORTED_EMOTIONS.copy()

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "engine_type": "tts",
                "sample_rate": self.DEFAULT_SAMPLE_RATE,
                "models_loaded": self._models_loaded,
                "has_bark": HAS_BARK,
                "supported_languages": self.get_supported_languages(),
                "supported_emotions": self.get_supported_emotions(),
            }
        )
        return info


def create_bark_engine(
    device: str | None = None,
    gpu: bool = True,
    model_path: str | None = None,
) -> BarkEngine:
    """Factory function to create a Bark engine instance."""
    return BarkEngine(device=device, gpu=gpu, model_path=model_path)
