"""
Whisper UI Engine for VoiceStudio
User interface wrapper for Whisper transcription

Compatible with:
- Python 3.10+
- openai-whisper or faster-whisper
"""

from __future__ import annotations

import contextlib
import logging
import os
from collections import OrderedDict
from typing import Any

# Try importing general model cache
_model_cache: Any = None
try:
    from app.core.models.cache import get_model_cache

    # 1GB max
    _model_cache = get_model_cache(max_models=2, max_memory_mb=1024.0)
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False

logger = logging.getLogger(__name__)

# Log cache availability
if not HAS_MODEL_CACHE:
    logger.debug("General model cache not available, " "using Whisper UI-specific cache")

# Fallback: Whisper UI-specific cache (for backward compatibility)
_WHISPER_UI_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_size: str, device: str, use_faster_whisper: bool) -> str:
    """Generate cache key for Whisper UI model."""
    return f"whisper_ui::{model_size}::{device}::{use_faster_whisper}"


def _get_cached_whisper_ui_model(model_size: str, device: str, use_faster_whisper: bool):
    """Get cached Whisper UI model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cache_key_str = f"{model_size}_{device}_{use_faster_whisper}"
        cached = _model_cache.get("whisper_ui", cache_key_str)
        if cached is not None:
            return cached

    # Fallback to Whisper UI-specific cache
    cache_key = _get_cache_key(model_size, device, use_faster_whisper)
    if cache_key in _WHISPER_UI_MODEL_CACHE:
        _WHISPER_UI_MODEL_CACHE.move_to_end(cache_key)
        return _WHISPER_UI_MODEL_CACHE[cache_key]
    return None


def _cache_whisper_ui_model(
    model_size: str, device: str, use_faster_whisper: bool, model_data: dict
):
    """Cache Whisper UI model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            cache_key_str = f"{model_size}_{device}_{use_faster_whisper}"
            _model_cache.set("whisper_ui", cache_key_str, model_data)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Whisper UI-specific cache
    cache_key = _get_cache_key(model_size, device, use_faster_whisper)

    if cache_key in _WHISPER_UI_MODEL_CACHE:
        _WHISPER_UI_MODEL_CACHE.move_to_end(cache_key)
        return

    _WHISPER_UI_MODEL_CACHE[cache_key] = model_data

    # Evict oldest if cache full
    if len(_WHISPER_UI_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_model = _WHISPER_UI_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_model
            logger.debug(f"Evicted Whisper UI model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Whisper UI model from cache: {e}")

    cache_size = len(_WHISPER_UI_MODEL_CACHE)
    logger.debug(f"Cached Whisper UI model: {cache_key} (cache size: {cache_size})")


# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

# Required imports
try:
    import whisper

    HAS_WHISPER = True
except ImportError:
    HAS_WHISPER = False
    logger.warning("openai-whisper not installed. Install with: pip install openai-whisper")

try:
    from faster_whisper import WhisperModel

    HAS_FASTER_WHISPER = True
except ImportError:
    HAS_FASTER_WHISPER = False

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


class WhisperUIEngine(EngineProtocol):
    """
    Whisper UI Engine - User interface wrapper for Whisper transcription.

    Supports:
    - Speech-to-text transcription
    - Multiple language support
    - Subtitle generation
    - Batch processing
    - Progress tracking
    """

    def __init__(
        self,
        model_size: str = "base",
        device: str | None = None,
        use_faster_whisper: bool = True,
        **kwargs,
    ):
        """
        Initialize Whisper UI engine.

        Args:
            model_size: Model size ('tiny', 'base', 'small', 'medium', 'large')
            device: Device to use ('cuda', 'cpu', or None for auto)
            use_faster_whisper: Use faster-whisper if available (faster inference)
        """
        self.model_size = model_size
        self.use_faster_whisper = use_faster_whisper and HAS_FASTER_WHISPER
        self.device = device or ("cuda" if (HAS_WHISPER or HAS_FASTER_WHISPER) else "cpu")

        self._initialized = False
        self._model = None
        self.lazy_load = True
        self._caching_enabled = True
        self._transcription_cache: OrderedDict[str, dict[str, Any]] = OrderedDict()
        self._cache_max_size = 200  # Maximum number of cached transcriptions

    def initialize(self) -> bool:
        """Initialize Whisper model."""
        try:
            if not HAS_WHISPER and not HAS_FASTER_WHISPER:
                logger.error("Neither openai-whisper nor faster-whisper is installed")
                return False

            if not HAS_NUMPY:
                logger.error("numpy is required for Whisper")
                return False

            if not HAS_SOUNDFILE:
                logger.error("soundfile is required for Whisper")
                return False

            # Initialize device
            if self.device == "cuda":
                try:
                    import torch

                    if not torch.cuda.is_available():
                        logger.warning("CUDA not available, falling back to CPU")
                        self.device = "cpu"
                except ImportError:
                    logger.warning("torch not available, using CPU")
                    self.device = "cpu"

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
            else:
                # Load model immediately if lazy loading disabled
                if not self._load_model():
                    return False

            self._initialized = True
            logger.info(
                f"Whisper UI engine initialized (model: {self.model_size}, device: {self.device})"
            )
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Whisper UI engine: {e}", exc_info=True)
            return False

    def cleanup(self) -> None:
        """Clean up Whisper resources."""
        try:
            if self._model is not None:
                del self._model
                self._model = None

            self._initialized = False
            logger.info("Whisper UI engine cleaned up")

        except Exception as e:
            logger.error(f"Error during Whisper UI cleanup: {e}")

    def is_initialized(self) -> bool:
        """Check if engine is initialized."""
        return self._initialized

    def get_device(self) -> str:
        """Get the device being used."""
        return self.device

    def get_info(self) -> dict:
        """Get engine information."""
        return {
            "engine": "whisper_ui",
            "name": "Whisper UI",
            "version": "1.0.0",
            "model_size": self.model_size,
            "device": self.device,
            "initialized": self._initialized,
            "model_loaded": self._model is not None,
            "use_faster_whisper": self.use_faster_whisper,
            "has_whisper": HAS_WHISPER,
            "has_faster_whisper": HAS_FASTER_WHISPER,
            "has_numpy": HAS_NUMPY,
            "has_soundfile": HAS_SOUNDFILE,
        }

    def transcribe(
        self,
        audio: str | bytes,
        language: str | None = None,
        output_format: str = "text",
        **kwargs,
    ) -> str | dict | None:
        """
        Transcribe audio to text using Whisper.

        Args:
            audio: Audio file path or audio data as bytes
            language: Language code (e.g., 'en', 'zh', 'ja') or None for auto-detect
            output_format: Output format ('text', 'json', 'srt', 'vtt')
            **kwargs: Additional parameters (task, temperature, etc.)

        Returns:
            Transcription result (format depends on output_format) or None on error
        """
        if not self._initialized:
            logger.error("Engine not initialized")
            return None

        try:
            # Check transcription cache
            import hashlib

            cache_key = hashlib.md5(
                f"{audio}_{language}_{output_format}".encode() if isinstance(audio, str) else audio
            ).hexdigest()
            if cache_key in self._transcription_cache:
                logger.debug("Using cached Whisper UI transcription result")
                self._transcription_cache.move_to_end(cache_key)  # LRU update
                cached_result = self._transcription_cache[cache_key]
                if output_format == "text":
                    return str(cached_result.get("text", ""))
                elif output_format == "json":
                    return dict(cached_result)
                elif output_format == "srt":
                    return str(self._format_srt(cached_result))
                elif output_format == "vtt":
                    return str(self._format_vtt(cached_result))
                return str(cached_result.get("text", ""))

            # Lazy load model if needed
            if self._model is None and not self._load_model():
                return None

            # Process audio
            audio_path = self._prepare_audio(audio)
            if audio_path is None:
                return None

            # Perform transcription
            result = self._perform_transcription(audio_path, language, **kwargs)

            # Cleanup temp file if created
            if isinstance(audio, bytes) and os.path.exists(audio_path):
                with contextlib.suppress(Exception):
                    os.remove(audio_path)

            if result is None:
                return None

            # Cache result if successful (LRU)
            if result:
                # Manage cache size - remove oldest entries if cache is full
                if len(self._transcription_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._transcription_cache))
                    del self._transcription_cache[oldest_key]
                self._transcription_cache[cache_key] = result
                self._transcription_cache.move_to_end(cache_key)  # LRU update

            if output_format == "text":
                return str(result.get("text", ""))
            elif output_format == "json":
                return result
            elif output_format == "srt":
                return str(self._format_srt(result))
            elif output_format == "vtt":
                return str(self._format_vtt(result))
            else:
                logger.warning(f"Unknown output format: {output_format}, returning text")
                return str(result.get("text", ""))

        except Exception as e:
            logger.error(f"Whisper UI transcription failed: {e}", exc_info=True)
            return None

    def _load_model(self) -> bool:
        """Load Whisper model with caching support."""
        try:
            # Check cache first
            if self._caching_enabled:
                cached_model = _get_cached_whisper_ui_model(
                    self.model_size,
                    self.device,
                    self.use_faster_whisper,
                )
                if cached_model is not None:
                    logger.debug(f"Using cached Whisper UI model: {self.model_size}")
                    self._model = cached_model.get("model")
                    return True

            logger.info(f"Loading Whisper model: {self.model_size}")

            if self.use_faster_whisper and HAS_FASTER_WHISPER:
                # Use faster-whisper (faster inference)
                device = "cuda" if self.device == "cuda" else "cpu"
                compute_type = "float16" if device == "cuda" else "int8"
                self._model = WhisperModel(
                    self.model_size, device=device, compute_type=compute_type
                )
                logger.info("Loaded faster-whisper model")
            elif HAS_WHISPER:
                # Use openai-whisper
                self._model = whisper.load_model(self.model_size, device=self.device)
                logger.info("Loaded openai-whisper model")
            else:
                logger.error("No Whisper implementation available")
                return False

            if self._caching_enabled:
                _cache_whisper_ui_model(
                    self.model_size,
                    self.device,
                    self.use_faster_whisper,
                    {"model": self._model},
                )

            return True

        except Exception as e:
            logger.error(f"Failed to load Whisper model: {e}", exc_info=True)
            return False

    def _prepare_audio(self, audio: str | bytes) -> str | None:
        """Prepare audio file for transcription."""
        try:
            if isinstance(audio, str):
                if os.path.exists(audio):
                    return audio
                else:
                    logger.error(f"Audio file not found: {audio}")
                    return None
            elif isinstance(audio, bytes):
                # Save to temp file
                import tempfile

                with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
                    tmp_file.write(audio)
                    return tmp_file.name
            else:
                logger.error("Invalid audio type")
                return None

        except Exception as e:
            logger.error(f"Failed to prepare audio: {e}", exc_info=True)
            return None

    def _perform_transcription(
        self, audio_path: str, language: str | None, **kwargs
    ) -> dict | None:
        """Perform actual transcription using Whisper."""
        try:
            if (
                self.use_faster_whisper
                and HAS_FASTER_WHISPER
                and isinstance(self._model, WhisperModel)
            ):
                # Use faster-whisper
                segments, info = self._model.transcribe(audio_path, language=language, **kwargs)

                text_parts = []
                segment_list = []
                for segment in segments:
                    text_parts.append(segment.text)
                    segment_list.append(
                        {
                            "start": segment.start,
                            "end": segment.end,
                            "text": segment.text,
                        }
                    )

                return {
                    "text": " ".join(text_parts),
                    "segments": segment_list,
                    "language": info.language,
                }

            elif HAS_WHISPER and self._model is not None:
                # Use openai-whisper
                result = self._model.transcribe(audio_path, language=language, **kwargs)

                return {
                    "text": result.get("text", ""),
                    "segments": result.get("segments", []),
                    "language": result.get("language", language or "unknown"),
                }

            else:
                logger.error("No Whisper model available")
                return None

        except Exception as e:
            logger.error(f"Transcription failed: {e}", exc_info=True)
            return None

    def _format_srt(self, result: dict) -> str:
        """Format transcription as SRT subtitles."""
        segments = result.get("segments", [])
        if not segments:
            return ""

        srt_lines = []
        for i, segment in enumerate(segments, 1):
            start = segment.get("start", 0)
            end = segment.get("end", 0)
            text = segment.get("text", "").strip()

            # Format time as SRT timestamp
            start_time = self._format_srt_time(start)
            end_time = self._format_srt_time(end)

            srt_lines.append(f"{i}")
            srt_lines.append(f"{start_time} --> {end_time}")
            srt_lines.append(text)
            srt_lines.append("")

        return "\n".join(srt_lines)

    def _format_vtt(self, result: dict) -> str:
        """Format transcription as VTT subtitles."""
        segments = result.get("segments", [])
        if not segments:
            return "WEBVTT\n\n"

        vtt_lines = ["WEBVTT", ""]
        for segment in segments:
            start = segment.get("start", 0)
            end = segment.get("end", 0)
            text = segment.get("text", "").strip()

            # Format time as VTT timestamp
            start_time = self._format_vtt_time(start)
            end_time = self._format_vtt_time(end)

            vtt_lines.append(f"{start_time} --> {end_time}")
            vtt_lines.append(text)
            vtt_lines.append("")

        return "\n".join(vtt_lines)

    def _format_srt_time(self, seconds: float) -> str:
        """Format seconds as SRT timestamp (HH:MM:SS,mmm)."""
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        secs = int(seconds % 60)
        millis = int((seconds % 1) * 1000)
        return f"{hours:02d}:{minutes:02d}:{secs:02d},{millis:03d}"

    def _format_vtt_time(self, seconds: float) -> str:
        """Format seconds as VTT timestamp (HH:MM:SS.mmm)."""
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        secs = int(seconds % 60)
        millis = int((seconds % 1) * 1000)
        return f"{hours:02d}:{minutes:02d}:{secs:02d}.{millis:03d}"

    def set_caching(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        status = "enabled" if enable else "disabled"
        logger.info(f"Model caching {status}")

    def clear_transcription_cache(self):
        """Clear transcription cache."""
        self._transcription_cache.clear()
        logger.info("Transcription cache cleared")

    def get_cache_stats(self) -> dict[str, int | bool]:
        """Get cache statistics."""
        return {
            "transcription_cache_size": len(self._transcription_cache),
            "max_transcription_cache_size": self._cache_max_size,
            "cache_enabled": self._caching_enabled,
        }


def create_whisper_ui_engine(**kwargs) -> WhisperUIEngine:
    """Factory function to create Whisper UI engine."""
    return WhisperUIEngine(**kwargs)
