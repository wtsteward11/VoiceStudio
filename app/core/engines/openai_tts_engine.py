"""
OpenAI TTS Engine for VoiceStudio
OpenAI Text-to-Speech API integration

Compatible with:
- Python 3.10+
- openai>=1.0.0
- requests>=2.28.0
"""

from __future__ import annotations

import contextlib
import hashlib
import logging
import os

# Try importing requests for connection pooling
import types
from collections import OrderedDict
from collections.abc import Iterator
from pathlib import Path
from typing import Any, cast

import numpy as np
import numpy.typing as npt

_requests_mod: types.ModuleType | None = None
try:
    import requests
    from requests.adapters import HTTPAdapter
    from urllib3.util.retry import Retry

    HAS_REQUESTS = True
    _requests_mod = requests
except ImportError:
    HAS_REQUESTS = False

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Try importing OpenAI
try:
    from openai import OpenAI

    HAS_OPENAI = True
except ImportError:
    HAS_OPENAI = False
    logger.warning("openai not installed. Install with: pip install openai>=1.0.0")
    OpenAI = None

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
    logger.warning("librosa not installed. Install with: pip install librosa")

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


class OpenAITTSEngine(EngineProtocol):
    """
    OpenAI TTS Engine for text-to-speech synthesis using OpenAI API.

    Supports:
    - Multiple voices (alloy, echo, fable, onyx, nova, shimmer)
    - Multiple formats (mp3, opus, aac, flac, pcm)
    - Streaming synthesis
    - Quality enhancement
    - Response caching
    """

    # Supported voices
    SUPPORTED_VOICES = [
        "alloy",
        "echo",
        "fable",
        "onyx",
        "nova",
        "shimmer",
    ]

    # Supported formats
    SUPPORTED_FORMATS = ["mp3", "opus", "aac", "flac", "pcm"]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self,
        api_key: str | None = None,
        base_url: str | None = None,
        model: str = "tts-1",
        voice: str = "alloy",
        device: str | None = None,
        gpu: bool = True,
        enable_cache: bool = True,
        cache_size: int = 100,
    ):
        """
        Initialize OpenAI TTS engine.

        Args:
            api_key: OpenAI API key (or use OPENAI_API_KEY env var)
            base_url: Base URL for API (default: OpenAI API)
            model: Model to use ('tts-1' or 'tts-1-hd')
            voice: Voice to use (default: 'alloy')
            device: Device to use (for local processing, not API)
            gpu: Whether to use GPU if available (for local processing)
            enable_cache: Enable response caching
            cache_size: Maximum cache size
        """
        super().__init__(device=device, gpu=gpu)

        # Get API key from parameter or environment
        self.api_key = api_key or os.getenv("OPENAI_API_KEY")
        self.base_url = base_url
        self.model = model
        self.voice = voice.lower()
        self.enable_cache = enable_cache
        self.cache_size = cache_size

        # Initialize OpenAI client
        self.client: Any = None
        self._session: Any = None  # For connection pooling

        # Response cache (LRU: key: hash of (text, voice, model), value: audio)
        self._response_cache: OrderedDict[str, npt.NDArray[np.float32]] = OrderedDict()

        # Validate voice
        if self.voice not in self.SUPPORTED_VOICES:
            logger.warning(
                f"Voice '{self.voice}' not in supported voices. " f"Using 'alloy' instead."
            )
            self.voice = "alloy"

    def initialize(self) -> bool:
        """
        Initialize the OpenAI TTS client.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            if not HAS_OPENAI:
                logger.error("OpenAI library not available")
                return False

            if not self.api_key:
                logger.error(
                    "OpenAI API key not provided. "
                    "Set OPENAI_API_KEY environment variable or pass api_key."
                )
                return False

            logger.info("Initializing OpenAI TTS client")

            # Initialize OpenAI client with connection pooling
            client_kwargs = {"api_key": self.api_key}
            if self.base_url:
                client_kwargs["base_url"] = self.base_url

            # Set up connection pooling if requests is available
            if HAS_REQUESTS:
                self._session = requests.Session()
                # Configure retry strategy
                retry_strategy = Retry(
                    total=3,
                    backoff_factor=1,
                    status_forcelist=[429, 500, 502, 503, 504],
                )
                adapter = HTTPAdapter(
                    max_retries=retry_strategy,
                    pool_connections=10,
                    pool_maxsize=20,
                )
                self._session.mount("http://", adapter)
                self._session.mount("https://", adapter)
                # Use session for OpenAI client if supported
                client_kwargs["http_client"] = self._session

            self.client = OpenAI(**client_kwargs)

            self._initialized = True
            logger.info("OpenAI TTS engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize OpenAI TTS engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        speaker_wav: str | Path | None = None,
        language: str = "en",
        output_path: str | Path | None = None,
        voice: str | None = None,
        model: str | None = None,
        format: str = "mp3",
        speed: float = 1.0,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> npt.NDArray[np.float32] | None | tuple[npt.NDArray[np.float32] | None, dict[str, Any]]:
        """
        Synthesize speech from text using OpenAI TTS API.

        Args:
            text: Text to synthesize
            speaker_wav: Not used (OpenAI TTS uses predefined voices)
            language: Language code (not used, OpenAI TTS is English-only)
            output_path: Optional path to save output audio
            voice: Voice to use (default: instance voice)
            model: Model to use ('tts-1' or 'tts-1-hd', default: instance model)
            format: Output format ('mp3', 'opus', 'aac', 'flac', 'pcm')
            speed: Speech speed (0.25 to 4.0, default: 1.0)
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        if not self._initialized and not self.initialize():
            return None

        try:
            # Use provided voice or instance voice
            voice_to_use = (voice or self.voice).lower()
            if voice_to_use not in self.SUPPORTED_VOICES:
                logger.warning(f"Voice '{voice_to_use}' not supported. Using '{self.voice}'")
                voice_to_use = self.voice

            # Use provided model or instance model
            model_to_use = model or self.model

            # Validate speed
            speed = max(0.25, min(4.0, float(speed)))

            # Validate format
            if format not in self.SUPPORTED_FORMATS:
                logger.warning(f"Format '{format}' not supported. Using 'mp3'")
                format = "mp3"

            # Check cache (LRU)
            cache_key = None
            if self.enable_cache:
                cache_key = self._get_cache_key(text, voice_to_use, model_to_use, format, speed)
                if cache_key in self._response_cache:
                    logger.debug("Using cached OpenAI TTS response")
                    self._response_cache.move_to_end(cache_key)  # LRU update
                    cached_audio = self._response_cache[cache_key].copy()
                    return self._process_audio_output(
                        cached_audio,
                        output_path,
                        enhance_quality,
                        calculate_quality,
                    )

            # Call OpenAI TTS API
            logger.info(f"Synthesizing with OpenAI TTS (voice: {voice_to_use})")

            response = self.client.audio.speech.create(
                model=model_to_use,
                voice=voice_to_use,
                input=text,
                response_format=format,
                speed=speed,
            )

            # Get audio data
            audio_data = response.content

            # Convert to numpy array
            audio = self._convert_audio_to_numpy(audio_data, format)

            if audio is None:
                logger.error("Failed to convert audio to numpy array")
                return None

            # Cache response
            if cache_key and self.enable_cache:
                self._cache_response(cache_key, audio)

            # Process and return
            return self._process_audio_output(
                audio, output_path, enhance_quality, calculate_quality
            )

        except Exception as e:
            logger.error(f"OpenAI TTS synthesis failed: {e}")
            return None

    def synthesize_stream(
        self,
        text: str,
        voice: str | None = None,
        model: str | None = None,
        format: str = "mp3",
        speed: float = 1.0,
        chunk_size: int = 100,
        **kwargs: Any,
    ) -> Iterator[npt.NDArray[np.float32]]:
        """
        Stream synthesis in real-time chunks.

        Args:
            text: Text to synthesize
            voice: Voice to use (default: instance voice)
            model: Model to use (default: instance model)
            format: Output format (default: 'mp3')
            speed: Speech speed (0.25 to 4.0, default: 1.0)
            chunk_size: Number of characters per chunk
            **kwargs: Additional parameters

        Yields:
            Audio chunks (numpy arrays)
        """
        if not self._initialized and not self.initialize():
            return

        try:
            # Use provided voice or instance voice
            voice_to_use = (voice or self.voice).lower()
            if voice_to_use not in self.SUPPORTED_VOICES:
                voice_to_use = self.voice

            # Use provided model or instance model
            model_to_use = model or self.model

            # Validate speed
            speed = max(0.25, min(4.0, float(speed)))

            # Split text into chunks
            chunks = self._split_text_into_chunks(text, chunk_size)

            for chunk_text in chunks:
                try:
                    # Synthesize chunk
                    response = self.client.audio.speech.create(
                        model=model_to_use,
                        voice=voice_to_use,
                        input=chunk_text,
                        response_format=format,
                        speed=speed,
                    )

                    # Get audio data
                    audio_data = response.content

                    # Convert to numpy array
                    audio = self._convert_audio_to_numpy(audio_data, format)

                    if audio is not None and len(audio) > 0:
                        yield audio

                except Exception as e:
                    logger.error(f"Stream synthesis chunk failed: {e}")
                    continue

        except Exception as e:
            logger.error(f"OpenAI TTS stream synthesis failed: {e}")

    def _convert_audio_to_numpy(
        self, audio_data: bytes, format: str
    ) -> npt.NDArray[np.float32] | None:
        """Convert audio bytes to numpy array."""
        try:
            import tempfile

            # Create temp file
            with tempfile.NamedTemporaryFile(delete=False, suffix=f".{format}") as tmp_file:
                tmp_file.write(audio_data)
                tmp_path = tmp_file.name

            try:
                # Load audio file
                if HAS_SOUNDFILE:
                    audio, sample_rate = sf.read(tmp_path)
                elif HAS_LIBROSA:
                    audio, _sample_rate = librosa.load(tmp_path, sr=None)
                else:
                    logger.error("Neither soundfile nor librosa available for audio loading")
                    return None

                # Convert to mono if stereo
                if len(audio.shape) > 1:
                    audio = np.mean(audio, axis=1)

                # Convert to float32
                if audio.dtype != np.float32:
                    audio = audio.astype(np.float32)

                # Normalize
                if np.max(np.abs(audio)) > 0:
                    audio = audio / np.max(np.abs(audio)) * 0.95

                return cast(npt.NDArray[np.float32], audio)

            finally:
                # Clean up temp file
                with contextlib.suppress(Exception):
                    os.unlink(tmp_path)

        except Exception as e:
            logger.error(f"Failed to convert audio to numpy: {e}")
            return None

    def _process_audio_output(
        self,
        audio: npt.NDArray[np.float32],
        output_path: str | Path | None,
        enhance_quality: bool,
        calculate_quality: bool,
    ) -> npt.NDArray[np.float32] | None | tuple[npt.NDArray[np.float32] | None, dict[str, Any]]:
        """Process audio output with quality enhancement and/or metrics."""
        quality_metrics = {}

        # Enhance quality if requested
        if enhance_quality and HAS_AUDIO_UTILS:
            try:
                audio = enhance_voice_quality(audio, self.DEFAULT_SAMPLE_RATE)
                audio = normalize_lufs(audio, self.DEFAULT_SAMPLE_RATE, -23.0)
                audio = remove_artifacts(audio, self.DEFAULT_SAMPLE_RATE)
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        # Calculate quality metrics if requested
        if calculate_quality and HAS_QUALITY_METRICS:
            try:
                quality_metrics = calculate_all_metrics(audio, sample_rate=self.DEFAULT_SAMPLE_RATE)
            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        # Save to file if requested
        if output_path:
            try:
                if HAS_SOUNDFILE:
                    sf.write(str(output_path), audio, self.DEFAULT_SAMPLE_RATE)
                elif HAS_LIBROSA:
                    from scipy.io.wavfile import write as _wav_write

                    _wav_write(str(output_path), self.DEFAULT_SAMPLE_RATE, audio)
                else:
                    logger.error("Neither soundfile nor librosa available for saving")
                    return None

                logger.info(f"Audio saved to: {output_path}")
                if calculate_quality:
                    return None, quality_metrics
                return None

            except Exception as e:
                logger.error(f"Failed to save audio: {e}")
                return None

        # Return audio (and metrics if requested)
        if calculate_quality:
            return audio, quality_metrics
        return audio

    def _split_text_into_chunks(self, text: str, chunk_size: int) -> list[str]:
        """Split text into chunks for streaming."""
        chunks = []
        words = text.split()

        current_chunk: list[str] = []
        current_length = 0

        for word in words:
            word_length = len(word) + 1  # +1 for space

            if current_length + word_length > chunk_size and current_chunk:
                chunks.append(" ".join(current_chunk))
                current_chunk = [word]
                current_length = word_length
            else:
                current_chunk.append(word)
                current_length += word_length

        if current_chunk:
            chunks.append(" ".join(current_chunk))

        return chunks

    def _get_cache_key(self, text: str, voice: str, model: str, format: str, speed: float) -> str:
        """Generate cache key for request."""
        key_string = f"{text}_{voice}_{model}_{format}_{speed}"
        return hashlib.md5(key_string.encode()).hexdigest()

    def _cache_response(self, key: str, audio: npt.NDArray[np.float32]) -> None:
        """Cache response with LRU eviction."""
        # Remove oldest entries if cache is full (LRU)
        if len(self._response_cache) >= self.cache_size:
            oldest_key = next(iter(self._response_cache))
            del self._response_cache[oldest_key]

        self._response_cache[key] = audio.copy()
        self._response_cache.move_to_end(key)  # LRU update

    def clear_cache(self) -> None:
        """Clear response cache."""
        self._response_cache.clear()
        logger.info("Response cache cleared")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "cache_size": len(self._response_cache),
            "max_cache_size": self.cache_size,
            "cache_enabled": self.enable_cache,
        }

    def get_voices(self) -> list[str]:
        """Get available voices."""
        return self.SUPPORTED_VOICES.copy()

    def cleanup(self) -> None:
        """Clean up resources."""
        try:
            # Close session for connection pooling
            if self._session is not None:
                self._session.close()
                self._session = None

            if self.client is not None:
                del self.client
                self.client = None

            self._response_cache.clear()
            self._initialized = False
            logger.info("OpenAI TTS engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during OpenAI TTS cleanup: {e}")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model": self.model,
                "voice": self.voice,
                "supported_voices": self.SUPPORTED_VOICES,
                "supported_formats": self.SUPPORTED_FORMATS,
                "cache_enabled": self.enable_cache,
                "cache_size": len(self._response_cache),
                "has_openai": HAS_OPENAI,
            }
        )
        return info


def create_openai_tts_engine(
    api_key: str | None = None,
    base_url: str | None = None,
    model: str = "tts-1",
    voice: str = "alloy",
    device: str | None = None,
    gpu: bool = True,
    enable_cache: bool = True,
) -> OpenAITTSEngine:
    """
    Factory function to create an OpenAI TTS engine instance.

    Args:
        api_key: OpenAI API key (or use OPENAI_API_KEY env var)
        base_url: Base URL for API (default: OpenAI API)
        model: Model to use ('tts-1' or 'tts-1-hd')
        voice: Voice to use (default: 'alloy')
        device: Device to use (for local processing)
        gpu: Whether to use GPU if available
        enable_cache: Enable response caching

    Returns:
        Initialized OpenAITTSEngine instance
    """
    engine = OpenAITTSEngine(
        api_key=api_key,
        base_url=base_url,
        model=model,
        voice=voice,
        device=device,
        gpu=gpu,
        enable_cache=enable_cache,
    )
    engine.initialize()
    return engine
