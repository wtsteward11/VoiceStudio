"""
OpenVoice Engine for VoiceStudio
Quick voice cloning option integration

OpenVoice is a versatile instant voice cloning approach that enables
zero-shot text-to-speech synthesis with voice cloning capabilities.

Compatible with:
- Python 3.10+
- OpenVoice library
- PyTorch 2.0+
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from collections.abc import Iterator
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

import numpy as np
import torch

# Initialize logger early
logger = logging.getLogger(__name__)

_MISSING: Any = None

# Try importing general model cache
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=3, max_memory_mb=2048.0)  # 2GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False
    _model_cache = _MISSING
    logger.debug("General model cache not available, using OpenVoice-specific cache")

# Fallback: OpenVoice-specific cache (for backward compatibility)
_OPENVOICE_MODEL_CACHE: OrderedDict = OrderedDict()
_SPEAKER_EMBEDDING_CACHE: dict[str, np.ndarray] = {}
_MAX_CACHE_SIZE = 2  # Maximum number of model pairs to cache in memory
_MAX_EMBEDDING_CACHE_SIZE = 100  # Maximum number of speaker embeddings to cache


def _get_cache_key(base_model: str, converter_model: str, device: str) -> str:
    """Generate cache key for OpenVoice models."""
    return f"openvoice::{base_model}::{converter_model}::{device}"


def _get_cached_openvoice_models(base_model: str, converter_model: str, device: str):
    """Get cached OpenVoice models if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cache_key = f"{base_model}::{converter_model}"
        cached = _model_cache.get("openvoice", cache_key, device=device)
        if cached is not None:
            return cached

    # Fallback to OpenVoice-specific cache
    cache_key = _get_cache_key(base_model, converter_model, device)
    if cache_key in _OPENVOICE_MODEL_CACHE:
        _OPENVOICE_MODEL_CACHE.move_to_end(cache_key)
        return _OPENVOICE_MODEL_CACHE[cache_key]
    return None


def _cache_openvoice_models(base_model: str, converter_model: str, device: str, models: dict):
    """Cache OpenVoice models with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            cache_key = f"{base_model}::{converter_model}"
            _model_cache.set("openvoice", cache_key, models, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to OpenVoice-specific cache
    cache_key = _get_cache_key(base_model, converter_model, device)

    if cache_key in _OPENVOICE_MODEL_CACHE:
        _OPENVOICE_MODEL_CACHE.move_to_end(cache_key)
        return

    _OPENVOICE_MODEL_CACHE[cache_key] = models

    # Evict oldest if cache full
    if len(_OPENVOICE_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_models = _OPENVOICE_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_models
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted OpenVoice models from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting OpenVoice models from cache: {e}")

    logger.debug(
        f"Cached OpenVoice models: {cache_key} (cache size: {len(_OPENVOICE_MODEL_CACHE)})"
    )


def _get_speaker_embedding_cache_key(speaker_wav: str | Path) -> str | None:
    """Generate cache key for speaker embedding."""
    import hashlib

    audio_path = Path(speaker_wav)
    if audio_path.exists():
        mtime = audio_path.stat().st_mtime
        return hashlib.md5(f"{audio_path}::{mtime}".encode()).hexdigest()
    return None


def _get_cached_speaker_embedding(speaker_wav: str | Path) -> np.ndarray | None:
    """Get cached speaker embedding if available."""
    cache_key = _get_speaker_embedding_cache_key(speaker_wav)
    if cache_key:
        return _SPEAKER_EMBEDDING_CACHE.get(cache_key)
    return None


def _cache_speaker_embedding(speaker_wav: str | Path, embedding: np.ndarray):
    """Cache speaker embedding with LRU eviction."""
    cache_key = _get_speaker_embedding_cache_key(speaker_wav)
    if not cache_key:
        return

    if cache_key in _SPEAKER_EMBEDDING_CACHE:
        return

    _SPEAKER_EMBEDDING_CACHE[cache_key] = embedding

    # Evict oldest if cache full
    if len(_SPEAKER_EMBEDDING_CACHE) > _MAX_EMBEDDING_CACHE_SIZE:
        oldest_key = next(iter(_SPEAKER_EMBEDDING_CACHE))
        del _SPEAKER_EMBEDDING_CACHE[oldest_key]
        logger.debug(f"Evicted speaker embedding from cache: {oldest_key[:8]}")

    logger.debug(
        f"Cached speaker embedding: {cache_key[:8]} (cache size: {len(_SPEAKER_EMBEDDING_CACHE)})"
    )


# Try to import librosa for style control
try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    librosa = _MISSING
    logger.warning("librosa not available. Some style control features will be limited.")

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
        enhance_voice_cloning_quality,
        enhance_voice_quality,
        match_voice_profile,
        normalize_lufs,
        remove_artifacts,
    )

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    enhance_voice_cloning_quality = _MISSING

# Try to import OpenVoice
try:
    import openvoice
    from openvoice import se_extractor
    from openvoice.api import BaseSpeakerTTS, ToneColorConverter

    HAS_OPENVOICE = True
except ImportError:
    HAS_OPENVOICE = False
    logger.warning("OpenVoice not installed. Install with: pip install openvoice")
    # Create stand-in classes for type hints
    BaseSpeakerTTS = _MISSING
    ToneColorConverter = _MISSING

# Import base protocol from canonical source
from .base import EngineProtocol


class OpenVoiceEngine(EngineProtocol):
    """
    OpenVoice Engine for quick voice cloning and text-to-speech synthesis.

    Supports:
    - Zero-shot voice cloning
    - Multi-language synthesis
    - Fast inference
    - High-quality voice conversion
    """

    # Supported languages (expanded)
    SUPPORTED_LANGUAGES = [
        "en",
        "es",
        "fr",
        "de",
        "it",
        "pt",
        "pl",
        "tr",
        "ru",
        "nl",
        "cs",
        "ar",
        "zh",
        "ja",
        "ko",
        "hi",
        "th",
        "vi",
        "id",
        "ms",
    ]

    # Style control parameters
    EMOTIONS = ["neutral", "happy", "sad", "angry", "excited", "calm"]
    ACCENTS = ["american", "british", "australian", "indian", "neutral"]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self,
        base_speaker_model: str = "checkpoints/base_speakers/EN",
        tone_color_converter_model: str = "checkpoints/converter",
        device: str | None = None,
        gpu: bool = True,
        enable_style_control: bool = True,
    ):
        """
        Initialize OpenVoice engine.

        Args:
            base_speaker_model: Path to base speaker TTS model
            tone_color_converter_model: Path to tone color converter model
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            enable_style_control: Enable granular style control features
        """
        super().__init__(device=device, gpu=gpu)

        if not HAS_OPENVOICE:
            raise ImportError("OpenVoice not installed. Install with: pip install openvoice")

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

        self.base_speaker_model = base_speaker_model
        self.tone_color_converter_model = tone_color_converter_model
        self.enable_style_control = enable_style_control
        self.base_speaker_tts: Any = None
        self.tone_color_converter: Any = None
        self._base_models_cache: dict[str, Any] = {}
        self._synthesis_cache: OrderedDict = OrderedDict()
        self._cache_max_size = 100
        self.lazy_load = True
        self.batch_size = 2
        self._enable_caching = True

    def _load_models(self) -> bool:
        """Load models with caching support."""
        # Check cache first
        if self._enable_caching:
            cached_models = _get_cached_openvoice_models(
                self.base_speaker_model, self.tone_color_converter_model, self.device
            )
            if cached_models is not None:
                logger.debug("Using cached OpenVoice models")
                self.base_speaker_tts = cached_models.get("base_speaker_tts")
                self.tone_color_converter = cached_models.get("tone_color_converter")
                self._initialized = True
                return True

        # Use model cache directory if available
        model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
        if not model_cache_dir:
            model_cache_dir = os.path.join(
                os.getenv("PROGRAMDATA", "C:\\ProgramData"), "VoiceStudio", "models"
            )
        os.makedirs(model_cache_dir, exist_ok=True)

        # Initialize base speaker TTS
        try:
            base_model_path = self.base_speaker_model
            if not os.path.isabs(base_model_path):
                # Try in model cache, then relative to current directory
                cache_path = os.path.join(model_cache_dir, base_model_path)
                if os.path.exists(cache_path):
                    base_model_path = cache_path

            self.base_speaker_tts = BaseSpeakerTTS(
                f"{base_model_path}/config.json", device=self.device
            )
            self.base_speaker_tts.load_ckpt(f"{base_model_path}/checkpoint.pth")
            logger.info("Base speaker TTS loaded")
        except Exception as e:
            logger.error(f"Failed to load base speaker TTS: {e}")
            logger.error("Download models from: https://github.com/myshell-ai/OpenVoice")
            return False

        # Initialize tone color converter
        try:
            converter_path = self.tone_color_converter_model
            if not os.path.isabs(converter_path):
                cache_path = os.path.join(model_cache_dir, converter_path)
                if os.path.exists(cache_path):
                    converter_path = cache_path

            self.tone_color_converter = ToneColorConverter(
                f"{converter_path}/config.json", device=self.device
            )
            self.tone_color_converter.load_ckpt(f"{converter_path}/checkpoint.pth")
            logger.info("Tone color converter loaded")
        except Exception as e:
            logger.error(f"Failed to load tone color converter: {e}")
            logger.error("Download models from: https://github.com/myshell-ai/OpenVoice")
            return False

        # Cache models
        if self._enable_caching:
            _cache_openvoice_models(
                self.base_speaker_model,
                self.tone_color_converter_model,
                self.device,
                {
                    "base_speaker_tts": self.base_speaker_tts,
                    "tone_color_converter": self.tone_color_converter,
                },
            )

        self._initialized = True
        logger.info("OpenVoice engine initialized successfully")
        return True

    def initialize(self) -> bool:
        """
        Initialize the OpenVoice model.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            if not HAS_OPENVOICE:
                logger.error("OpenVoice library not available")
                return False

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, models will be loaded on first use")
                return True

            logger.info(f"Loading OpenVoice models (device: {self.device})")
            return self._load_models()

        except Exception as e:
            logger.error(f"Failed to initialize OpenVoice engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        speaker_wav: str | Path | list[str | Path],
        language: str = "en",
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using OpenVoice voice cloning.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio file(s)
            language: Language code (e.g., 'en', 'es', 'fr')
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - speed: Speech rate (0.5-2.0, default 1.0)

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        # Lazy load models if needed
        if not self._initialized and not self._load_models():
            return None

        try:
            # Convert speaker_wav to list if single path
            if isinstance(speaker_wav, (str, Path)):
                speaker_wav = [speaker_wav]

            # Use first reference audio
            reference_audio_path = str(speaker_wav[0])

            # Check speaker embedding cache
            speaker_embedding = None
            if self._enable_caching:
                speaker_embedding = _get_cached_speaker_embedding(reference_audio_path)
                if speaker_embedding is not None:
                    logger.debug("Using cached speaker embedding")

            # Extract speaker embedding if not cached
            if speaker_embedding is None:
                try:
                    with torch.inference_mode():  # Faster inference
                        speaker_embedding = se_extractor.get_se(
                            reference_audio_path, self.tone_color_converter, vad=True
                        )
                    # Cache embedding
                    if self._enable_caching:
                        _cache_speaker_embedding(reference_audio_path, speaker_embedding)
                except Exception as e:
                    logger.error(f"Failed to extract speaker embedding: {e}")
                    return None

            # Synthesize with base speaker
            try:
                # Generate base audio with inference mode
                with torch.inference_mode():  # Faster inference
                    base_audio = self.base_speaker_tts.tts(
                        text, language=language, speed=kwargs.get("speed", 1.0)
                    )
            except Exception as e:
                logger.error(f"Base TTS synthesis failed: {e}")
                return None

            # Convert tone color with inference mode
            try:
                with torch.inference_mode():  # Faster inference
                    audio = self.tone_color_converter.convert(
                        audio_src_path=base_audio if isinstance(base_audio, str) else None,
                        src_se=speaker_embedding,
                        tgt_se=speaker_embedding,
                        output_path=output_path if output_path else None,
                    )

                # If output_path provided, audio is saved to file
                if output_path:
                    # Load saved audio
                    import soundfile as sf

                    audio, sample_rate = sf.read(output_path)
                else:
                    # audio is numpy array
                    if isinstance(audio, str):
                        import soundfile as sf

                        audio, sample_rate = sf.read(audio)
                    else:
                        sample_rate = self.DEFAULT_SAMPLE_RATE

                # Convert to numpy array if needed
                if not isinstance(audio, np.ndarray):
                    audio = np.array(audio)

                # Convert to mono if stereo
                if len(audio.shape) > 1:
                    audio = np.mean(audio, axis=1)

                # Convert to float32
                if audio.dtype != np.float32:
                    audio = audio.astype(np.float32)

                # Normalize
                if np.max(np.abs(audio)) > 0:
                    audio = audio / np.max(np.abs(audio)) * 0.95

                # Apply quality processing if requested
                if enhance_quality or calculate_quality:
                    audio = self._process_audio_quality(
                        audio,
                        sample_rate,
                        reference_audio_path,
                        enhance_quality,
                        calculate_quality,
                    )
                    if isinstance(audio, tuple):
                        enhanced_audio, quality_metrics = audio
                        if output_path:
                            import soundfile as sf

                            sf.write(output_path, enhanced_audio, sample_rate)
                            return None, quality_metrics
                        return enhanced_audio, quality_metrics
                    else:
                        if output_path:
                            import soundfile as sf

                            sf.write(output_path, audio, sample_rate)
                            return None
                        return audio

                # Save to file if requested
                if output_path:
                    import soundfile as sf

                    sf.write(output_path, audio, sample_rate)
                    logger.info(f"Audio saved to: {output_path}")
                    return None

                return np.asarray(audio)

            except Exception as e:
                logger.error(f"Tone color conversion failed: {e}")
                return None

        except Exception as e:
            logger.error(f"OpenVoice synthesis failed: {e}")
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
                # Use advanced voice cloning quality enhancement (if available)
                if enhance_voice_cloning_quality is not None:
                    audio = enhance_voice_cloning_quality(
                        audio,
                        sample_rate,
                        enhancement_level="standard",
                        preserve_prosody=True,
                        target_lufs=-23.0,
                    )
                    logger.debug("Applied advanced quality enhancement to OpenVoice output")
                elif enhance_voice_quality is not None:
                    # Fallback to standard enhancement
                    audio = enhance_voice_quality(
                        audio,
                        sample_rate,
                        normalize=True,
                        denoise=True,
                        target_lufs=-23.0,
                    )
                    logger.debug("Applied quality enhancement to OpenVoice output")
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        if calculate and HAS_QUALITY_METRICS:
            try:
                quality_metrics = calculate_all_metrics(audio, sample_rate=sample_rate)
                if reference_audio:
                    try:
                        import soundfile as sf

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

    def synthesize_with_style(
        self,
        text: str,
        speaker_wav: str | Path,
        language: str = "en",
        emotion: str | None = None,
        accent: str | None = None,
        rhythm: float | None = None,
        pauses: list[float] | None = None,
        intonation: dict[str, float] | None = None,
        output_path: str | Path | None = None,
        **kwargs,
    ) -> np.ndarray | None:
        """
        Synthesize with granular style control.

        NEW METHOD - Enhanced style control for OpenVoice.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            emotion: Emotion style ("neutral", "happy", "sad", etc.)
            accent: Accent style ("american", "british", etc.)
            rhythm: Speech rhythm factor (0.5-2.0, default 1.0)
            pauses: List of pause durations in seconds at specific positions
            intonation: Dict of intonation adjustments
                - "pitch_shift": Overall pitch shift in semitones
                - "pitch_variance": Pitch variance factor (0.0-1.0)
                - "energy": Energy level (0.0-1.0)
            output_path: Optional path to save output
            **kwargs: Additional parameters

        Returns:
            Audio array (numpy) or None if synthesis failed
        """
        if not self._initialized and not self.initialize():
            return None

        # First, synthesize base audio
        base_audio = self.synthesize(
            text=text,
            speaker_wav=speaker_wav,
            language=language,
            output_path=None,
            enhance_quality=False,
            calculate_quality=False,
            **kwargs,
        )

        if base_audio is None or isinstance(base_audio, tuple):
            return None

        # Apply style modifications
        if self.enable_style_control:
            if emotion:
                base_audio = self._apply_emotion(
                    base_audio, emotion, intensity=kwargs.get("emotion_intensity", 0.5)
                )

            if accent:
                base_audio = self._apply_accent(base_audio, accent)

            if rhythm and rhythm != 1.0:
                base_audio = self._apply_rhythm(base_audio, rhythm)

            if pauses:
                pause_positions = kwargs.get("pause_positions", [0.3, 0.7])  # Default positions
                base_audio = self._insert_pauses(base_audio, pause_positions, pauses)

            if intonation:
                base_audio = self._apply_intonation(base_audio, intonation)

        # Save if requested
        if output_path:
            import soundfile as sf

            sf.write(output_path, base_audio, self.DEFAULT_SAMPLE_RATE)
            logger.info(f"Styled audio saved to: {output_path}")
            return None

        return base_audio

    def synthesize_cross_lingual(
        self,
        text: str,
        speaker_wav: str | Path,
        source_language: str = "en",
        target_language: str = "es",
        output_path: str | Path | None = None,
        **kwargs,
    ) -> np.ndarray | None:
        """
        Zero-shot cross-lingual voice cloning.

        NEW METHOD - Cross-lingual voice cloning support.

        Args:
            text: Text to synthesize (in target language)
            speaker_wav: Path to reference speaker audio (any language)
            source_language: Language of reference audio
            target_language: Language for synthesis
            output_path: Optional path to save output
            **kwargs: Additional parameters

        Returns:
            Audio array (numpy) or None if synthesis failed
        """
        if not self._initialized and not self.initialize():
            return None

        try:
            # Extract speaker embedding (language-agnostic)
            speaker_embedding = se_extractor.get_se(
                str(speaker_wav), self.tone_color_converter, vad=True
            )

            # Get or load target language base speaker model
            target_base_model = self._get_base_model_for_language(target_language)

            if target_base_model is None:
                logger.warning(
                    f"Target language {target_language} base model not available, using default"
                )
                target_base_model = self.base_speaker_tts

            # Synthesize with target language base
            base_audio = target_base_model.tts(
                text, language=target_language, speed=kwargs.get("speed", 1.0)
            )

            # Convert tone color (base_audio can be string path or array)
            cloned_audio = self.tone_color_converter.convert(
                audio_src_path=base_audio if isinstance(base_audio, str) else None,
                src_se=speaker_embedding,
                tgt_se=speaker_embedding,
            )

            # Process audio
            if isinstance(cloned_audio, str):
                import soundfile as sf

                audio, sample_rate = sf.read(cloned_audio)
            else:
                audio = cloned_audio
                sample_rate = self.DEFAULT_SAMPLE_RATE

            # Convert to numpy array if needed
            if not isinstance(audio, np.ndarray):
                audio = np.array(audio)

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Convert to float32
            if audio.dtype != np.float32:
                audio = audio.astype(np.float32)

            # Normalize
            if np.max(np.abs(audio)) > 0:
                audio = audio / np.max(np.abs(audio)) * 0.95

            # Save if requested
            if output_path:
                import soundfile as sf

                sf.write(output_path, audio, sample_rate)
                logger.info(f"Cross-lingual audio saved to: {output_path}")
                return None

            return np.asarray(audio)

        except Exception as e:
            logger.error(f"Cross-lingual synthesis failed: {e}")
            return None

    def synthesize_stream(
        self,
        text: str,
        speaker_wav: str | Path,
        language: str = "en",
        chunk_size: int = 100,
        overlap: int = 20,
        **kwargs,
    ) -> Iterator[np.ndarray]:
        """
        Stream synthesis in real-time chunks.

        NEW METHOD - Real-time streaming synthesis.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            chunk_size: Number of characters per chunk
            overlap: Number of overlapping characters between chunks
            **kwargs: Additional parameters

        Yields:
            Audio chunks (numpy arrays)
        """
        if not self._initialized and not self.initialize():
            return

        # Split text into chunks
        chunks = self._split_text_with_overlap(text, chunk_size, overlap)

        # Pre-extract speaker embedding (once)
        try:
            speaker_embedding = se_extractor.get_se(
                str(speaker_wav), self.tone_color_converter, vad=True
            )
        except Exception as e:
            logger.error(f"Failed to extract speaker embedding: {e}")
            return

        # Buffer for overlap-add
        overlap_buffer = None
        overlap_samples = int(self.DEFAULT_SAMPLE_RATE * 0.1)  # 100ms overlap

        for chunk_text in chunks:
            try:
                # Synthesize chunk
                base_audio = self.base_speaker_tts.tts(
                    chunk_text, language=language, speed=kwargs.get("speed", 1.0)
                )

                # Convert tone color
                chunk_audio = self.tone_color_converter.convert(
                    audio_src_path=base_audio if isinstance(base_audio, str) else None,
                    src_se=speaker_embedding,
                    tgt_se=speaker_embedding,
                )

                # Load audio if path
                if isinstance(chunk_audio, str):
                    import soundfile as sf

                    chunk_audio, _ = sf.read(chunk_audio)

                # Convert to numpy array
                if not isinstance(chunk_audio, np.ndarray):
                    chunk_audio = np.array(chunk_audio)

                # Convert to mono if stereo
                if len(chunk_audio.shape) > 1:
                    chunk_audio = np.mean(chunk_audio, axis=1)

                # Convert to float32
                if chunk_audio.dtype != np.float32:
                    chunk_audio = chunk_audio.astype(np.float32)

                # Validate chunk_audio
                if len(chunk_audio) == 0:
                    logger.warning("Empty audio chunk, skipping")
                    continue

                # Ensure overlap_samples is valid
                overlap_samples = max(0, min(overlap_samples, len(chunk_audio) // 2))

                # Apply overlap-add
                if (
                    overlap_buffer is not None
                    and len(chunk_audio) > overlap_samples
                    and len(overlap_buffer) == overlap_samples
                ):
                    # Blend overlap region
                    overlap_region = chunk_audio[:overlap_samples]
                    if len(overlap_region) == len(overlap_buffer):
                        blended = overlap_buffer * 0.5 + overlap_region * 0.5
                        chunk_audio = np.concatenate([blended, chunk_audio[overlap_samples:]])
                    else:
                        # Size mismatch, skip overlap-add
                        logger.warning(
                            f"Overlap buffer size mismatch: {len(overlap_buffer)} vs {len(overlap_region)}"
                        )

                # Update overlap buffer with bounds checking
                if len(chunk_audio) > overlap_samples and overlap_samples > 0:
                    overlap_buffer = chunk_audio[-overlap_samples:].copy()
                else:
                    overlap_buffer = chunk_audio.copy()

                # Yield chunk (excluding overlap if we have buffer)
                if (
                    overlap_buffer is not None
                    and len(chunk_audio) > overlap_samples
                    and overlap_samples > 0
                ):
                    yield_chunk = chunk_audio[:-overlap_samples]
                    if len(yield_chunk) > 0:
                        yield yield_chunk
                else:
                    if len(chunk_audio) > 0:
                        yield chunk_audio

            except Exception as e:
                logger.error(f"Stream synthesis chunk failed: {e}")
                continue

    def _split_text_with_overlap(self, text: str, chunk_size: int, overlap: int) -> list[str]:
        """Split text into chunks with overlap."""
        chunks = []
        start = 0

        while start < len(text):
            end = start + chunk_size
            chunk = text[start:end]

            # Try to break at word boundary
            if end < len(text) and text[end] != " ":
                # Find last space in chunk
                last_space = chunk.rfind(" ")
                if last_space > chunk_size // 2:  # Only break if space is in second half
                    chunk = chunk[: last_space + 1]
                    end = start + len(chunk)

            chunks.append(chunk)
            start = end - overlap  # Overlap for next chunk

            if start >= len(text):
                break

        return chunks

    def _apply_emotion(self, audio: np.ndarray, emotion: str, intensity: float = 0.5) -> np.ndarray:
        """Apply emotion to audio."""
        if librosa is None:
            logger.warning("librosa not available for emotion control")
            return audio

        emotion_params = {
            "happy": {
                "pitch_shift": 2.0 * intensity,  # semitones
                "energy_multiplier": 1.0 + (0.2 * intensity),
                "tempo_multiplier": 1.0 + (0.1 * intensity),
            },
            "sad": {
                "pitch_shift": -2.0 * intensity,
                "energy_multiplier": 1.0 - (0.2 * intensity),
                "tempo_multiplier": 1.0 - (0.1 * intensity),
            },
            "angry": {
                "pitch_shift": 1.0 * intensity,
                "energy_multiplier": 1.0 + (0.5 * intensity),
                "tempo_multiplier": 1.0 + (0.2 * intensity),
            },
            "excited": {
                "pitch_shift": 3.0 * intensity,
                "energy_multiplier": 1.0 + (0.3 * intensity),
                "tempo_multiplier": 1.0 + (0.15 * intensity),
            },
            "calm": {
                "pitch_shift": -1.0 * intensity,
                "energy_multiplier": 1.0 - (0.1 * intensity),
                "tempo_multiplier": 1.0 - (0.05 * intensity),
            },
            "neutral": {
                "pitch_shift": 0.0,
                "energy_multiplier": 1.0,
                "tempo_multiplier": 1.0,
            },
        }

        params = emotion_params.get(emotion.lower(), emotion_params["neutral"])

        # Apply pitch shift with bounds checking
        if params["pitch_shift"] != 0:
            try:
                # Limit pitch shift to reasonable range
                pitch_shift = max(-12.0, min(12.0, params["pitch_shift"]))
                audio = librosa.effects.pitch_shift(
                    audio, sr=self.DEFAULT_SAMPLE_RATE, n_steps=pitch_shift
                )
            except Exception as e:
                logger.warning(f"Pitch shift failed: {e}, skipping")

        # Apply tempo change with bounds checking
        if params["tempo_multiplier"] != 1.0:
            try:
                # Limit tempo multiplier to reasonable range
                tempo = max(0.5, min(2.0, params["tempo_multiplier"]))
                audio = librosa.effects.time_stretch(audio, rate=tempo)
            except Exception as e:
                logger.warning(f"Tempo change failed: {e}, skipping")

        # Apply energy (volume)
        audio = audio * params["energy_multiplier"]

        # Normalize to prevent clipping
        if np.max(np.abs(audio)) > 0:
            audio = audio / np.max(np.abs(audio)) * 0.95

        return audio

    def _apply_accent(self, audio: np.ndarray, accent: str) -> np.ndarray:
        """Apply accent to audio using prosody modifications."""
        if librosa is None:
            logger.warning("librosa not available for accent control")
            return audio

        if len(audio) == 0:
            return audio

        logger.info(f"Applying accent control: {accent}")

        # Accent-specific prosody adjustments
        accent_params = {
            "american": {"pitch_shift": 0, "tempo": 1.0, "formant_shift": 0},
            "british": {"pitch_shift": -0.5, "tempo": 0.95, "formant_shift": -0.1},
            "australian": {"pitch_shift": 0.3, "tempo": 1.05, "formant_shift": 0.1},
            "indian": {"pitch_shift": 0.5, "tempo": 1.1, "formant_shift": 0.2},
            "neutral": {"pitch_shift": 0, "tempo": 1.0, "formant_shift": 0},
        }

        params = accent_params.get(accent.lower(), accent_params["neutral"])

        # Apply pitch shift
        if params["pitch_shift"] != 0:
            try:
                audio = librosa.effects.pitch_shift(
                    audio,
                    sr=self.DEFAULT_SAMPLE_RATE,
                    n_steps=params["pitch_shift"] * 12,
                )
            except Exception as e:
                logger.warning(f"Pitch shift failed: {e}")

        # Apply tempo change
        if params["tempo"] != 1.0:
            try:
                audio = librosa.effects.time_stretch(audio, rate=params["tempo"])
            except Exception as e:
                logger.warning(f"Tempo change failed: {e}")

        # Apply formant shift (simplified via spectral modification)
        if params["formant_shift"] != 0:
            try:
                # Use phase vocoder for formant shifting
                stft = librosa.stft(audio)
                stft_shifted = librosa.phase_vocoder(stft, rate=1.0 + params["formant_shift"])
                audio = librosa.istft(stft_shifted)
            except Exception as e:
                logger.warning(f"Formant shift failed: {e}")

        return audio

    def _apply_rhythm(self, audio: np.ndarray, rhythm_factor: float) -> np.ndarray:
        """Adjust speech rhythm."""
        if not HAS_LIBROSA or librosa is None:
            logger.warning("librosa not available for rhythm control")
            return audio

        # Validate input
        if len(audio) == 0:
            logger.warning("Empty audio array, cannot apply rhythm")
            return audio

        # Validate and clamp rhythm factor
        rhythm_factor = max(0.5, min(2.0, float(rhythm_factor)))
        if rhythm_factor == 1.0:
            return audio  # No change needed

        # Use time-stretching to adjust rhythm with error handling
        try:
            audio = librosa.effects.time_stretch(audio, rate=rhythm_factor)
        except Exception as e:
            logger.warning(f"Rhythm adjustment failed: {e}, returning original")
            return audio

        return audio

    def _insert_pauses(
        self,
        audio: np.ndarray,
        pause_positions: list[float],
        pause_durations: list[float],
    ) -> np.ndarray:
        """Insert pauses at specific positions."""
        if len(pause_positions) != len(pause_durations):
            logger.warning("Pause positions and durations mismatch")
            return audio

        # Sort by position
        sorted_pauses = sorted(zip(pause_positions, pause_durations), key=lambda x: x[0])

        result = audio.copy()
        offset = 0

        for position, duration in sorted_pauses:
            # Validate position (0.0-1.0)
            position = max(0.0, min(1.0, float(position)))

            # Validate duration (must be positive)
            duration = max(0.0, float(duration))
            if duration == 0:
                continue

            # Convert position (0.0-1.0) to sample index
            base_index = int(position * len(audio))
            sample_index = base_index + offset

            # Ensure sample_index is within bounds
            sample_index = max(0, min(sample_index, len(result)))

            # Convert duration to samples (with reasonable limit)
            pause_samples = int(duration * self.DEFAULT_SAMPLE_RATE)
            pause_samples = max(
                1, min(pause_samples, int(self.DEFAULT_SAMPLE_RATE * 5.0))
            )  # Max 5 seconds

            # Create pause (silence)
            pause = np.zeros(pause_samples, dtype=audio.dtype)

            # Insert pause with bounds checking
            if sample_index >= len(result):
                # Append pause at the end
                result = np.concatenate([result, pause])
            elif sample_index <= 0:
                # Prepend pause at the beginning
                result = np.concatenate([pause, result])
            else:
                # Insert pause in the middle
                result = np.concatenate([result[:sample_index], pause, result[sample_index:]])

            offset += pause_samples

        return result

    def _apply_intonation(
        self, audio: np.ndarray, intonation_params: dict[str, float]
    ) -> np.ndarray:
        """Apply intonation adjustments."""
        if librosa is None:
            logger.warning("librosa not available for intonation control")
            return audio

        # Apply pitch shift
        pitch_shift = intonation_params.get("pitch_shift", 0.0)
        if pitch_shift != 0:
            audio = librosa.effects.pitch_shift(
                audio, sr=self.DEFAULT_SAMPLE_RATE, n_steps=pitch_shift
            )

        # Note: Pitch variance and energy adjustments would require
        # more sophisticated prosody modification
        # This is a simplified implementation

        energy = intonation_params.get("energy", 1.0)
        if energy != 1.0:
            audio = audio * energy
            # Normalize to prevent clipping
            if np.max(np.abs(audio)) > 0:
                audio = audio / np.max(np.abs(audio)) * 0.95

        return audio

    def _get_base_model_for_language(self, language: str) -> Any | None:
        """Get or load base speaker model for specific language."""
        if language in self._base_models_cache:
            return self._base_models_cache[language]

        # Try to load language-specific model
        try:
            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"), "VoiceStudio", "models"
                )

            lang_model_path = os.path.join(
                model_cache_dir, "openvoice", "base_speakers", language.upper()
            )

            if os.path.exists(lang_model_path):
                lang_model = BaseSpeakerTTS(f"{lang_model_path}/config.json", device=self.device)
                lang_model.load_ckpt(f"{lang_model_path}/checkpoint.pth")
                self._base_models_cache[language] = lang_model
                return lang_model
        except Exception as e:
            logger.warning(f"Failed to load language-specific model for {language}: {e}")

        # Fallback to default model
        return None

    def get_languages(self) -> list[str]:
        """Get available languages."""
        return self.SUPPORTED_LANGUAGES

    def batch_synthesize(
        self,
        texts: list[str],
        speaker_wav: str | Path | list[str | Path],
        language: str = "en",
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
            speaker_wav: Path to reference speaker audio file(s)
            language: Language code
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

        results: list[np.ndarray | tuple[np.ndarray | None, dict] | None] = []

        # Process in batches for better GPU utilization
        actual_batch_size = min(batch_size, self.batch_size)

        def synthesize_single(text):
            try:
                return self.synthesize(
                    text=text,
                    speaker_wav=speaker_wav,
                    language=language,
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
        if torch.cuda.is_available() and (len(texts) % (actual_batch_size * 2) == 0):
            torch.cuda.empty_cache()

        return results

    def enable_caching(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._enable_caching = enable
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
            self.base_speaker_tts = None
            self.tone_color_converter = None
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            self._initialized = False
            logger.info("OpenVoice engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during OpenVoice cleanup: {e}")

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "base_speaker_model": self.base_speaker_model,
                "tone_color_converter_model": self.tone_color_converter_model,
                "sample_rate": self.DEFAULT_SAMPLE_RATE,
            }
        )
        return info


def create_openvoice_engine(
    base_speaker_model: str = "checkpoints/base_speakers/EN",
    tone_color_converter_model: str = "checkpoints/converter",
    device: str | None = None,
    gpu: bool = True,
) -> OpenVoiceEngine:
    """Factory function to create an OpenVoice engine instance."""
    return OpenVoiceEngine(
        base_speaker_model=base_speaker_model,
        tone_color_converter_model=tone_color_converter_model,
        device=device,
        gpu=gpu,
    )
