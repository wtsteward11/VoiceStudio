"""
Vosk Engine for VoiceStudio
Vosk integration for speech-to-text transcription

Compatible with:
- Python 3.10.15+
- vosk 0.3.45+
"""

from __future__ import annotations

import json
import logging
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache: Any
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=3, max_memory_mb=1536.0)  # 1.5GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    _model_cache = None
    logger.debug("General model cache not available, using Vosk-specific cache")

# Fallback: Vosk-specific cache (for backward compatibility)
_VOSK_MODEL_CACHE: OrderedDict[str, Any] = OrderedDict()
_TRANSCRIPTION_CACHE: dict[str, dict[str, Any]] = {}
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory
_MAX_TRANSCRIPTION_CACHE_SIZE = 200  # Maximum number of transcriptions to cache


def _get_cache_key(model_path: str | None, model_name: str) -> str:
    """Generate cache key for Vosk model."""
    return f"vosk::{model_path or 'default'}::{model_name}"


def _get_cached_vosk_model(model_path: str | None, model_name: str) -> Any:
    """Get cached Vosk model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cache_key = model_path or model_name
        cached = _model_cache.get("vosk", cache_key, device="cpu")
        if cached is not None:
            return cached

    # Fallback to Vosk-specific cache
    cache_key = _get_cache_key(model_path, model_name)
    if cache_key in _VOSK_MODEL_CACHE:
        # Move to end (most recently used)
        _VOSK_MODEL_CACHE.move_to_end(cache_key)
        return _VOSK_MODEL_CACHE[cache_key]
    return None


def _cache_vosk_model(model_path: str | None, model_name: str, model: Any) -> None:
    """Cache Vosk model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            cache_key = model_path or model_name
            _model_cache.set("vosk", cache_key, model, device="cpu")
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Vosk-specific cache
    cache_key = _get_cache_key(model_path, model_name)

    # Remove if already exists
    if cache_key in _VOSK_MODEL_CACHE:
        _VOSK_MODEL_CACHE.move_to_end(cache_key)
        return

    # Add new model
    _VOSK_MODEL_CACHE[cache_key] = model

    # Evict oldest if cache full
    if len(_VOSK_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_model = _VOSK_MODEL_CACHE.popitem(last=False)
        # Cleanup oldest model
        try:
            del oldest_model
            logger.debug(f"Evicted Vosk model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Vosk model from cache: {e}")

    logger.debug(f"Cached Vosk model: {cache_key} (cache size: {len(_VOSK_MODEL_CACHE)})")


def _get_transcription_cache_key(
    audio: str | npt.NDArray[Any] | Path,
    word_timestamps: bool,
) -> str | None:
    """Generate cache key for transcription."""
    import hashlib

    # For file paths, use file path + modification time
    if isinstance(audio, (str, Path)):
        audio_path = Path(audio)
        if audio_path.exists():
            mtime = audio_path.stat().st_mtime
            key_data = f"{audio_path}::{mtime}::{word_timestamps}"
            return hashlib.md5(key_data.encode()).hexdigest()

    # For numpy arrays, use hash of audio data
    if isinstance(audio, np.ndarray):
        audio_hash = hashlib.md5(audio.tobytes()).hexdigest()
        key_data = f"{audio_hash}::{word_timestamps}"
        return hashlib.md5(key_data.encode()).hexdigest()

    return None


def _get_cached_transcription(
    audio: str | npt.NDArray[Any] | Path,
    word_timestamps: bool,
) -> dict[str, Any] | None:
    """Get cached transcription if available."""
    cache_key = _get_transcription_cache_key(audio, word_timestamps)
    if cache_key:
        return _TRANSCRIPTION_CACHE.get(cache_key)
    return None


def _cache_transcription(
    audio: str | npt.NDArray[Any] | Path,
    word_timestamps: bool,
    result: dict[str, Any],
) -> None:
    """Cache transcription with LRU eviction."""
    cache_key = _get_transcription_cache_key(audio, word_timestamps)
    if not cache_key:
        return

    # Remove if already exists
    if cache_key in _TRANSCRIPTION_CACHE:
        return

    # Add new transcription
    _TRANSCRIPTION_CACHE[cache_key] = result

    # Evict oldest if cache full
    if len(_TRANSCRIPTION_CACHE) > _MAX_TRANSCRIPTION_CACHE_SIZE:
        # Remove first item (oldest)
        oldest_key = next(iter(_TRANSCRIPTION_CACHE))
        del _TRANSCRIPTION_CACHE[oldest_key]
        logger.debug(f"Evicted transcription from cache: {oldest_key[:8]}")

    logger.debug(f"Cached transcription: {cache_key[:8]} (cache size: {len(_TRANSCRIPTION_CACHE)})")


# Try importing vosk
try:
    from vosk import KaldiRecognizer, Model, SetLogLevel

    HAS_VOSK = True
    SetLogLevel(-1)  # Suppress vosk logging
except ImportError:
    HAS_VOSK = False
    Model = None
    KaldiRecognizer = None
    SetLogLevel = None
    logger.warning("vosk not installed. Install with: pip install vosk>=0.3.45")

# Import base protocol from canonical source
from .base import EngineProtocol


class VoskEngine(EngineProtocol):
    """
    Vosk engine for speech-to-text transcription.

    Features:
    - Offline speech recognition
    - Multiple language models
    - Word-level timestamps
    - Low latency
    """

    def __init__(
        self,
        model_path: str | None = None,
        model_name: str = "vosk-model-en-us-0.22",
        sample_rate: int = 16000,
        device: str | None = None,
        gpu: bool = False,  # Vosk doesn't use GPU
    ):
        """
        Initialize Vosk engine.

        Args:
            model_path: Path to vosk model directory
            model_name: Model name (will download if not found)
            sample_rate: Audio sample rate (must be 16000 for vosk)
            device: Device (ignored, vosk is CPU-only)
            gpu: GPU flag (ignored, vosk is CPU-only)
        """
        super().__init__(device=device, gpu=False)  # Vosk is CPU-only
        self.model_path = model_path
        self.model_name = model_name
        self.sample_rate = sample_rate
        self.model: Any = None
        self.recognizer: Any = None
        self._initialized = False
        self.lazy_load = True
        self.batch_size = 4
        self._caching_enabled = True

        if not HAS_VOSK:
            raise ImportError("vosk is required. Install with: pip install vosk>=0.3.45")

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_model = _get_cached_vosk_model(self.model_path, self.model_name)
            if cached_model is not None:
                logger.debug(f"Using cached Vosk model: {self.model_name}")
                self.model = cached_model
                self.recognizer = KaldiRecognizer(self.model, self.sample_rate)
                self.recognizer.SetWords(True)  # Enable word-level timestamps
                self._initialized = True
                return True

        # Load model
        try:
            # Determine model path
            model_dir: Path | None
            if self.model_path:
                model_dir = Path(self.model_path)
            else:
                model_dir = self._find_model()

            if not model_dir or not model_dir.exists():
                raise FileNotFoundError(
                    f"Vosk model not found: {model_dir}. "
                    f"Download from: https://alphacephei.com/vosk/models"
                )

            logger.info(f"Loading Vosk model from: {model_dir}")
            self.model = Model(str(model_dir))
            self.recognizer = KaldiRecognizer(self.model, self.sample_rate)
            self.recognizer.SetWords(True)  # Enable word-level timestamps

            # Cache model
            if self._caching_enabled:
                _cache_vosk_model(self.model_path, self.model_name, self.model)

            self._initialized = True
            logger.info("Vosk engine initialized")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Vosk engine: {e}")
            self._initialized = False
            return False

    def initialize(self) -> bool:
        """Initialize Vosk model."""
        if self._initialized:
            return True

        # Lazy loading: defer until first use
        if self.lazy_load:
            logger.debug("Lazy loading enabled, model will be loaded on first use")
            return True

        return self._load_model()

    def _find_model(self) -> Path | None:
        """Find vosk model in common locations."""
        # Common model locations
        search_paths = [
            Path.home() / ".cache" / "vosk" / self.model_name,
            Path("models") / "vosk" / self.model_name,
            Path("vosk-models") / self.model_name,
        ]

        for path in search_paths:
            if path.exists() and (path / "am" / "final.mdl").exists():
                return path

        return None

    def batch_transcribe(
        self,
        audio_files: list[str | Path | npt.NDArray[Any]],
        word_timestamps: bool = True,
        **kwargs: Any,
    ) -> list[dict[str, Any]]:
        """
        Transcribe multiple audio files in batch with optimized processing.

        Args:
            audio_files: List of audio file paths or arrays
            word_timestamps: Whether to include word-level timestamps
            **kwargs: Additional parameters

        Returns:
            List of transcription results
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [{"text": "", "words": [], "confidence": 0.0}] * len(audio_files)

        results = []

        # Process in batches with parallel processing
        batch_size = self.batch_size

        def transcribe_single(audio_file: str | Path | npt.NDArray[Any]) -> dict[str, Any]:
            try:
                return self.transcribe(audio=audio_file, word_timestamps=word_timestamps, **kwargs)
            except Exception as e:
                logger.error(f"Batch transcription failed for {audio_file}: {e}")
                return {"text": "", "words": [], "confidence": 0.0}

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=batch_size) as executor:
            results = list(executor.map(transcribe_single, audio_files))

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Transcription caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int) -> None:
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def cleanup(self) -> None:
        """Cleanup Vosk resources."""
        # Don't delete cached model, just clear reference
        self.recognizer = None
        self.model = None
        self._initialized = False
        logger.debug("Vosk engine cleaned up")

    def transcribe(
        self,
        audio: str | Path | npt.NDArray[Any],
        language: str | None = None,
        word_timestamps: bool = True,
    ) -> dict[str, Any]:
        """
        Transcribe audio to text.

        Args:
            audio: Audio file path or numpy array
            language: Language code (ignored, determined by model)
            word_timestamps: Whether to include word-level timestamps

        Returns:
            Dictionary with transcription results:
            - text: Full transcription text
            - words: List of word dictionaries with timestamps
            - confidence: Overall confidence score
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return {"text": "", "words": [], "confidence": 0.0}

        # Check transcription cache
        if self._caching_enabled:
            cached_result = _get_cached_transcription(audio, word_timestamps)
            if cached_result is not None:
                logger.debug("Using cached transcription")
                return cached_result.copy()

        # Load audio if path provided
        audio_array = self._load_audio(audio) if isinstance(audio, (str, Path)) else audio

        # Ensure audio is int16 PCM at 16kHz
        if audio_array.dtype != np.int16:
            # Convert to int16
            audio_array = (audio_array * 32767).astype(np.int16)

        # Process audio in chunks (optimized chunk size)
        results = []
        chunk_size = 4000  # Process in chunks

        # Create new recognizer for this transcription (thread-safe)
        recognizer = KaldiRecognizer(self.model, self.sample_rate)
        recognizer.SetWords(word_timestamps)

        for i in range(0, len(audio_array), chunk_size):
            chunk = audio_array[i : i + chunk_size]
            chunk_bytes = chunk.tobytes()

            if recognizer.AcceptWaveform(chunk_bytes):
                result = json.loads(recognizer.Result())
                if result.get("text"):
                    results.append(result)

        # Get final result
        final_result = json.loads(recognizer.FinalResult())
        if final_result.get("text"):
            results.append(final_result)

        # Combine results
        full_text = " ".join([r.get("text", "") for r in results if r.get("text")])
        all_words = []
        for r in results:
            if "result" in r:
                all_words.extend(r["result"])

        result = {
            "text": full_text.strip(),
            "words": all_words if word_timestamps else [],
            "confidence": self._calculate_confidence(all_words) if all_words else 0.0,
        }

        # Cache transcription result
        if self._caching_enabled:
            _cache_transcription(audio, word_timestamps, result)

        return result

    def _load_audio(self, audio_path: str | Path) -> npt.NDArray[Any]:
        """Load audio file and resample to 16kHz if needed."""
        try:
            import soundfile as sf

            audio, sr = sf.read(str(audio_path))

            # Resample if needed
            if sr != self.sample_rate:
                import librosa

                audio = librosa.resample(audio, orig_sr=sr, target_sr=self.sample_rate)

            # Convert to int16
            if audio.dtype != np.int16:
                audio = (audio * 32767).astype(np.int16)

            return np.asarray(audio)
        except Exception as e:
            logger.error(f"Failed to load audio: {e}")
            raise

    def _calculate_confidence(self, words: list[dict[str, Any]]) -> float:
        """Calculate overall confidence from word confidences."""
        if not words:
            return 0.0

        confidences = [w.get("conf", 0.0) for w in words if "conf" in w]
        if not confidences:
            return 0.0

        return float(np.mean(confidences))

    def get_supported_languages(self) -> list[str]:
        """Get list of supported languages (depends on model)."""
        # Vosk supports many languages, but depends on model
        # Common models: en-us, es, fr, de, it, pt, ru, zh, ja, etc.
        return ["en", "es", "fr", "de", "it", "pt", "ru", "zh", "ja", "ko"]


def create_vosk_engine(
    model_path: str | None = None,
    model_name: str = "vosk-model-en-us-0.22",
    sample_rate: int = 16000,
) -> VoskEngine:
    """Factory function to create Vosk engine."""
    return VoskEngine(model_path=model_path, model_name=model_name, sample_rate=sample_rate)
