"""
Whisper Engine for VoiceStudio
faster-whisper integration for speech-to-text transcription

Compatible with:
- Python 3.10.15+
- faster-whisper 1.0.3
- PyTorch 2.2.2+cu121 (optional, for GPU)
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from pathlib import Path
from typing import Any

import numpy as np
import torch

# Optional audio utilities import
try:
    from app.core.audio.audio_utils import load_audio

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False

    def load_audio(file_path: str | Path) -> tuple[np.ndarray, int]:
        """Fallback audio loading."""
        import soundfile as sf

        audio, sr = sf.read(str(file_path))
        return np.asarray(audio), int(sr)


try:
    from faster_whisper import WhisperModel

    HAS_WHISPER = True
except ImportError:
    WhisperModel = None
    logging.warning("faster-whisper not installed. Install with: pip install faster-whisper==1.0.3")

# Try importing vosk as alternative STT engine
try:
    from vosk import KaldiRecognizer, Model, SetLogLevel

    HAS_VOSK = True
except ImportError:
    HAS_VOSK = False
    Model = None
    KaldiRecognizer = None
    SetLogLevel = None
    logging.debug("vosk not installed. Alternative STT engine will be limited.")

logger = logging.getLogger(__name__)

# Try importing general model cache
_model_cache = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=3, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    logger.debug("General model cache not available, using Whisper-specific cache")

# Fallback: Whisper-specific cache (for backward compatibility)
_MODEL_CACHE: OrderedDict = OrderedDict()
_TRANSCRIPTION_CACHE: dict[str, dict] = {}
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory
_MAX_TRANSCRIPTION_CACHE_SIZE = 200  # Maximum number of transcriptions to cache


def _get_cache_key(model_name: str, device: str, compute_type: str) -> str:
    """Generate cache key for model."""
    return f"whisper::{model_name}::{device}::{compute_type}"


def _get_cached_model(model_name: str, device: str, compute_type: str):
    """Get cached model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("whisper", model_name, device=device)
        if cached is not None:
            return cached

    # Fallback to Whisper-specific cache
    cache_key = _get_cache_key(model_name, device, compute_type)
    if cache_key in _MODEL_CACHE:
        # Move to end (most recently used)
        _MODEL_CACHE.move_to_end(cache_key)
        return _MODEL_CACHE[cache_key]
    return None


def _cache_model(model_name: str, device: str, compute_type: str, model):
    """Cache model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("whisper", model_name, model, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Whisper-specific cache
    cache_key = _get_cache_key(model_name, device, compute_type)

    # Remove if already exists
    if cache_key in _MODEL_CACHE:
        _MODEL_CACHE.move_to_end(cache_key)
        return

    # Add new model
    _MODEL_CACHE[cache_key] = model

    # Evict oldest if cache full
    if len(_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_model = _MODEL_CACHE.popitem(last=False)
        # Cleanup oldest model
        try:
            del oldest_model
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting model from cache: {e}")

    logger.debug(f"Cached model: {cache_key} (cache size: {len(_MODEL_CACHE)})")


def _get_transcription_cache_key(
    audio: str | np.ndarray | Path,
    language: str | None,
    task: str,
    word_timestamps: bool,
) -> str | None:
    """Generate cache key for transcription."""
    import hashlib

    # For file paths, use file path + modification time
    if isinstance(audio, (str, Path)):
        audio_path = Path(audio)
        if audio_path.exists():
            mtime = audio_path.stat().st_mtime
            key_data = f"{audio_path}::{mtime}::{language}::{task}::{word_timestamps}"
            return hashlib.md5(key_data.encode()).hexdigest()

    # For numpy arrays, use hash of audio data
    if isinstance(audio, np.ndarray):
        audio_hash = hashlib.md5(audio.tobytes()).hexdigest()
        key_data = f"{audio_hash}::{language}::{task}::{word_timestamps}"
        return hashlib.md5(key_data.encode()).hexdigest()

    return None


def _get_cached_transcription(
    audio: str | np.ndarray | Path,
    language: str | None,
    task: str,
    word_timestamps: bool,
) -> dict | None:
    """Get cached transcription if available."""
    cache_key = _get_transcription_cache_key(audio, language, task, word_timestamps)
    if cache_key:
        return _TRANSCRIPTION_CACHE.get(cache_key)
    return None


def _cache_transcription(
    audio: str | np.ndarray | Path,
    language: str | None,
    task: str,
    word_timestamps: bool,
    result: dict,
):
    """Cache transcription with LRU eviction."""
    cache_key = _get_transcription_cache_key(audio, language, task, word_timestamps)
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


# Try importing VAD (Voice Activity Detection)
try:
    from silero_vad import get_speech_timestamps, load_vad_model

    HAS_VAD = True
except ImportError:
    HAS_VAD = False
    logger.debug("silero-vad not available, VAD optimization will be limited")


# Import base protocol from canonical source
from .base import EngineProtocol


class WhisperEngine(EngineProtocol):
    """
    Whisper engine using faster-whisper for speech-to-text transcription.

    Features:
    - Fast inference with CTranslate2 backend
    - GPU acceleration support
    - Multiple model sizes (tiny, base, small, medium, large-v2, large-v3)
    - Word-level timestamps
    - Language detection
    - Multi-language support
    """

    # Supported model sizes
    SUPPORTED_MODELS = [
        "tiny",
        "base",
        "small",
        "medium",
        "large-v2",
        "large-v3",
        "large-v3-turbo",
    ]

    # Supported languages (from Whisper)
    SUPPORTED_LANGUAGES = [
        "auto",
        "en",
        "zh",
        "de",
        "es",
        "ru",
        "ja",
        "pt",
        "fr",
        "it",
        "ko",
        "pl",
        "tr",
        "nl",
        "ar",
        "cs",
        "fi",
        "hu",
        "sv",
        "vi",
        "id",
        "hi",
        "th",
        "uk",
        "ca",
        "bg",
        "hr",
        "sk",
        "sl",
        "sr",
        "et",
        "lt",
        "lv",
        "mt",
        "ro",
        "el",
        "da",
        "no",
        "is",
        "ga",
        "cy",
        "he",
        "fa",
        "ta",
        "ur",
        "bn",
        "te",
        "kn",
        "ml",
        "si",
        "my",
        "km",
        "lo",
        "ka",
        "am",
        "sw",
        "af",
        "az",
        "mk",
        "be",
        "bs",
        "eu",
        "gl",
        "ha",
        "jv",
        "ka",
        "kk",
        "ky",
        "lb",
        "mg",
        "ms",
        "ne",
        "ny",
        "ps",
        "so",
        "su",
        "tg",
        "uz",
        "yi",
        "yo",
        "zu",
    ]

    def __init__(
        self,
        model_name: str = "base",
        device: str | None = None,
        gpu: bool = True,
        compute_type: str = "float16",
        lazy_load: bool = True,
        batch_size: int = 4,
        enable_caching: bool = True,
        enable_vad: bool = False,
    ):
        """
        Initialize Whisper engine.

        Args:
            model_name: Model size ("tiny", "base", "small", "medium", "large-v2", "large-v3", "large-v3-turbo")
            device: Device to use ("cuda", "cpu", or None for auto)
            gpu: Whether to use GPU if available
            compute_type: Compute type for faster-whisper ("float16", "int8", "int8_float16", "int8_bfloat16")
            lazy_load: If True, defer model loading until first use
            batch_size: Batch size for batch transcription operations
            enable_caching: If True, enable model and transcription caching
            enable_vad: If True, enable VAD (Voice Activity Detection) optimization
        """
        super().__init__(device=device, gpu=gpu)

        if not HAS_WHISPER:
            raise ImportError(
                "faster-whisper not installed. Install with: pip install faster-whisper==1.0.3"
            )

        self.model_name = model_name
        self.compute_type = compute_type

        # Determine device for faster-whisper
        if self.device == "cuda" and torch.cuda.is_available():
            self.whisper_device = "cuda"
            if compute_type not in ["float16", "int8_float16", "int8_bfloat16"]:
                self.compute_type = "float16"  # Default for CUDA
        else:
            self.whisper_device = "cpu"
            if compute_type not in ["int8", "float32"]:
                self.compute_type = "int8"  # Default for CPU (faster)

        self.model: WhisperModel | None = None
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._caching_enabled = enable_caching
        self._vad_enabled = enable_vad and HAS_VAD

        # VAD model (loaded on demand)
        self._vad_model = None

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_model = _get_cached_model(
                self.model_name, self.whisper_device, self.compute_type
            )
            if cached_model is not None:
                logger.debug(f"Using cached model: {self.model_name}")
                self.model = cached_model
                self._initialized = True
                return True

        # Load model
        logger.info(f"Loading Whisper model: {self.model_name} on {self.whisper_device}")

        models_root = os.getenv("VOICESTUDIO_MODELS_PATH", "")
        if not models_root:
            models_root = os.path.join(
                os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                "VoiceStudio",
                "models",
            )
        whisper_cache = os.path.join(models_root, "whisper")
        os.makedirs(whisper_cache, exist_ok=True)

        # Prefer local CTranslate2 model directory if it exists
        model_id = self.model_name
        local_ct2_dir = os.path.join(
            whisper_cache, f"faster-whisper-{self.model_name}"
        )
        model_bin = os.path.join(local_ct2_dir, "model.bin")
        if os.path.isdir(local_ct2_dir) and os.path.exists(model_bin):
            model_id = local_ct2_dir

        self.model = WhisperModel(
            model_size_or_path=model_id,
            device=self.whisper_device,
            compute_type=self.compute_type,
            download_root=whisper_cache,
            local_files_only=True,  # Never download; fail fast if missing
        )

        # Cache model
        if self._caching_enabled:
            _cache_model(self.model_name, self.whisper_device, self.compute_type, self.model)

        self._initialized = True
        logger.info("Whisper model loaded successfully")
        return True

    def initialize(self) -> bool:
        """Initialize the Whisper model."""
        try:
            if self._initialized:
                return True

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
                return True

            return self._load_model()

        except Exception as e:
            logger.error(f"Failed to initialize Whisper model: {e}")
            self._initialized = False
            return False

    def _load_vad_model(self):
        """Load VAD model if enabled."""
        if not self._vad_enabled or not HAS_VAD:
            return None

        if self._vad_model is None:
            try:
                self._vad_model = load_vad_model(device=self.whisper_device)
                logger.debug("VAD model loaded successfully")
            except Exception as e:
                logger.warning(f"Failed to load VAD model: {e}")
                self._vad_model = None

        return self._vad_model

    def transcribe(
        self,
        audio: str | np.ndarray | Path,
        language: str | None = None,
        task: str = "transcribe",
        word_timestamps: bool = False,
        beam_size: int = 5,
        best_of: int = 5,
        temperature: float | list[float] = 0.0,
        initial_prompt: str | None = None,
        condition_on_previous_text: bool = True,
    ) -> dict[str, Any] | None:
        """
        Transcribe audio to text.

        Args:
            audio: Audio file path, numpy array, or Path object
            language: Language code (None for auto-detect)
            task: Task type ("transcribe" or "translate")
            word_timestamps: Whether to return word-level timestamps
            beam_size: Beam size for beam search
            best_of: Number of candidates to consider
            temperature: Sampling temperature (0.0 for deterministic)
            initial_prompt: Optional prompt to guide the model
            condition_on_previous_text: Whether to condition on previous text

        Returns:
            Dictionary with transcription results:
            {
                "text": str,
                "language": str,
                "segments": List[Dict],
                "word_timestamps": List[Dict] (if word_timestamps=True)
            }
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            raise RuntimeError("Failed to load Whisper model")

        try:
            # Check transcription cache
            if self._caching_enabled:
                cached_result = _get_cached_transcription(audio, language, task, word_timestamps)
                if cached_result is not None:
                    logger.debug("Using cached transcription")
                    return cached_result.copy()

            # Load audio if path provided
            audio_input: str | np.ndarray
            if isinstance(audio, (str, Path)):
                audio_path = Path(audio)
                if not audio_path.exists():
                    raise FileNotFoundError(f"Audio file not found: {audio_path}")

                # Load audio (faster-whisper accepts file paths directly)
                audio_input = str(audio_path)
            else:
                # Use numpy array directly
                audio_input = audio

            # Prepare language
            if language == "auto":
                language = None

            # Optimize with VAD if enabled
            vad_segments = None
            if self._vad_enabled and isinstance(audio_input, (str, Path)):
                vad_model = self._load_vad_model()
                if vad_model is not None:
                    try:
                        # Load audio for VAD
                        import soundfile as sf

                        audio_data, sample_rate = sf.read(str(audio_input))
                        vad_segments = get_speech_timestamps(
                            audio_data, vad_model, sampling_rate=sample_rate
                        )
                        logger.debug(f"VAD detected {len(vad_segments)} speech segments")
                    except Exception as e:
                        logger.debug(f"VAD processing failed: {e}")

            # Transcribe
            logger.debug(
                f"Transcribing audio: language={language}, task={task}, word_timestamps={word_timestamps}"
            )

            if self.model is None:
                logger.error("Whisper model not loaded")
                return None

            segments, info = self.model.transcribe(
                audio_input,
                language=language,
                task=task,
                beam_size=beam_size,
                best_of=best_of,
                temperature=temperature,
                initial_prompt=initial_prompt,
                condition_on_previous_text=condition_on_previous_text,
                word_timestamps=word_timestamps,
            )

            # Extract results
            text_parts = []
            segments_list = []
            word_timestamps_list = []

            for segment in segments:
                segment_dict = {
                    "text": segment.text,
                    "start": segment.start,
                    "end": segment.end,
                    "no_speech_prob": getattr(segment, "no_speech_prob", None),
                }
                segments_list.append(segment_dict)
                text_parts.append(segment.text)

                # Extract word timestamps if available
                if word_timestamps and hasattr(segment, "words"):
                    for word in segment.words:
                        word_timestamps_list.append(
                            {
                                "word": word.word,
                                "start": word.start,
                                "end": word.end,
                                "probability": getattr(word, "probability", None),
                            }
                        )

            result = {
                "text": " ".join(text_parts).strip(),
                "language": info.language,
                "language_probability": info.language_probability,
                "segments": segments_list,
                "duration": sum(s["end"] - s["start"] for s in segments_list),
            }

            if word_timestamps:
                result["word_timestamps"] = word_timestamps_list

            logger.debug(
                f"Transcription complete: language={result['language']}, duration={result['duration']:.2f}s"
            )

            # Cache transcription result
            if self._caching_enabled:
                _cache_transcription(audio, language, task, word_timestamps, result)

            return result

        except Exception as e:
            logger.error(f"Transcription failed: {e}", exc_info=True)
            raise

    def get_supported_languages(self) -> list[str]:
        """Get list of supported language codes."""
        return self.SUPPORTED_LANGUAGES.copy()

    def batch_transcribe(
        self,
        audio_files: list[str | Path],
        language: str | None = None,
        task: str = "transcribe",
        word_timestamps: bool = False,
        **kwargs,
    ) -> list[dict[str, Any] | None]:
        """
        Transcribe multiple audio files in batch.

        Args:
            audio_files: List of audio file paths
            language: Language code (None for auto-detect)
            task: Task type ("transcribe" or "translate")
            word_timestamps: Whether to return word-level timestamps
            **kwargs: Additional transcription parameters

        Returns:
            List of transcription results
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [{"error": "Failed to load model"}] * len(audio_files)

        results = []

        # Process in batches for better GPU utilization
        batch_size = self.batch_size
        for batch_start in range(0, len(audio_files), batch_size):
            batch_files = audio_files[batch_start : batch_start + batch_size]
            batch_results = []

            for audio_file in batch_files:
                try:
                    result = self.transcribe(
                        audio=audio_file,
                        language=language,
                        task=task,
                        word_timestamps=word_timestamps,
                        **kwargs,
                    )
                    batch_results.append(result)
                except Exception as e:
                    logger.error(f"Batch transcription failed for {audio_file}: {e}")
                    batch_results.append({"error": str(e)})

            results.extend(batch_results)

            # Clear GPU cache periodically
            if torch.cuda.is_available() and (batch_start + batch_size) % (batch_size * 2) == 0:
                torch.cuda.empty_cache()

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Transcription caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def set_vad_enabled(self, enable: bool = True) -> None:
        """Enable or disable VAD optimization."""
        if enable and not HAS_VAD:
            logger.warning("VAD not available, install silero-vad to enable")
            return
        self._vad_enabled = enable
        if enable:
            self._load_vad_model()
        logger.info(f"VAD optimization {'enabled' if enable else 'disabled'}")

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
        if self.model is not None:
            # faster-whisper models are cleaned up automatically
            self.model = None
        if self._vad_model is not None:
            del self._vad_model
            self._vad_model = None
        self._initialized = False
        logger.info("Whisper engine cleaned up")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model_name": self.model_name,
                "whisper_device": self.whisper_device,
                "compute_type": self.compute_type,
                "supported_languages": len(self.SUPPORTED_LANGUAGES),
                "supported_models": self.SUPPORTED_MODELS,
            }
        )
        return info


def create_whisper_engine(
    model_name: str = "base", device: str | None = None, gpu: bool = True
) -> WhisperEngine:
    """
    Factory function to create a Whisper engine.

    Args:
        model_name: Model size
        device: Device to use
        gpu: Whether to use GPU

    Returns:
        WhisperEngine instance
    """
    engine = WhisperEngine(model_name=model_name, device=device, gpu=gpu)
    engine.initialize()
    return engine
