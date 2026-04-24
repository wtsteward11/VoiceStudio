"""
Silero Models Engine for VoiceStudio
Fast, high-quality multilingual TTS integration

Silero TTS provides fast, high-quality multilingual text-to-speech synthesis
with support for many languages and voices.

Compatible with:
- Python 3.10+
- torch>=1.9.0
- silero-tts package
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt
import soundfile as sf
import torch

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
    logger.debug("General model cache not available, using Silero-specific cache")

# Fallback: Silero-specific cache (for backward compatibility)
from collections import OrderedDict

_MODEL_CACHE: OrderedDict[str, Any] = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_id: str, language: str, device: str) -> str:
    """Generate cache key for model."""
    return f"silero::{model_id}::{language}::{device}"


# ``torch.hub.load(..., speaker=...)`` selects which checkpoint package (e.g. ``v3_en``).
# ``TTSModelMultiAcc_v3.apply_tts(speaker=...)`` expects per-model voice IDs (e.g. ``en_0``, ``aidar``).
_HUB_LOAD_TO_APPLY_TTS_SPEAKER: dict[str, str] = {
    "v3_en": "en_0",
    "v4_ru": "aidar",
    "v3_de": "bernd_ungerer",
    "v3_es": "es_0",
    "v3_fr": "fr_0",
}


def _get_cached_model(model_id: str, language: str, device: str) -> Any:
    """Get cached model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("silero", f"silero_{model_id}_{language}", device=device)
        if cached is not None:
            return cached

    # Fallback to Silero-specific cache
    cache_key = _get_cache_key(model_id, language, device)
    if cache_key in _MODEL_CACHE:
        # Move to end (most recently used)
        _MODEL_CACHE.move_to_end(cache_key)
        return _MODEL_CACHE[cache_key]
    return None


def _cache_model(model_id: str, language: str, device: str, model: Any) -> None:
    """Cache model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("silero", f"silero_{model_id}_{language}", model, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Silero-specific cache
    cache_key = _get_cache_key(model_id, language, device)

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
            if isinstance(oldest_model, torch.nn.Module):
                del oldest_model
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting model from cache: {e}")

    logger.debug(f"Cached model: {cache_key} (cache size: {len(_MODEL_CACHE)})")


# Import base protocol from canonical source
from .base import EngineProtocol


class SileroEngine(EngineProtocol):
    """
    Silero TTS Engine for fast, high-quality multilingual text-to-speech synthesis.

    Supports:
    - 100+ languages
    - Multiple voices per language
    - Fast inference
    - High-quality natural speech
    """

    # Supported languages (Silero supports 100+ languages)
    SUPPORTED_LANGUAGES = [
        "en",
        "de",
        "es",
        "fr",
        "it",
        "pt",
        "pl",
        "ru",
        "uk",
        "tr",
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
        "nl",
        "sv",
        "da",
        "no",
        "fi",
        "el",
        "hu",
        "ro",
        "bg",
        "hr",
        "sr",
        "sk",
        "sl",
        "et",
        "lv",
        "lt",
        "mk",
        "sq",
        "is",
        "ga",
        "cy",
        "mt",
        "eu",
        "ca",
        "gl",
        "af",
        "sw",
        "zu",
        "xh",
        "yi",
        "eo",
        "la",
        "ia",
        "vo",
    ]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self,
        model_id: str = "v4",
        language: str = "en",
        device: str | None = None,
        gpu: bool = False,
        lazy_load: bool = True,
        batch_size: int = 4,
        enable_caching: bool = True,
    ):
        """
        Initialize Silero TTS engine.

        Args:
            model_id: Silero model version ('v3', 'v4', 'v5')
            language: Default language code
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available (default False: Silero hub JIT/package
                 models are CPU-first; set True only on GPUs supported by the installed PyTorch build)
            lazy_load: If True, defer model loading until first use
            batch_size: Batch size for batch synthesis operations
            enable_caching: If True, enable model caching
        """
        super().__init__(device=device, gpu=gpu)

        # If base picked CUDA, ensure this PyTorch build actually supports the GPU architecture.
        # A tiny cuda tensor can succeed while torch.package TTS kernels cannot (e.g. sm_120 on older torch).
        if self.device == "cuda":
            try:
                cap = torch.cuda.get_device_capability(0)
                arch = f"sm_{cap[0]}{cap[1]}"
                supported = torch.cuda.get_arch_list()
                if supported and arch not in supported:
                    logger.warning(
                        "Silero: GPU architecture %s is not in PyTorch arch list %s; using CPU.",
                        arch,
                        supported,
                    )
                    self.device = "cpu"
            except Exception as ex:
                logger.warning("Silero: CUDA capability check failed (%s); using CPU.", ex)
                self.device = "cpu"

        self.model_id = model_id
        self.default_language = language
        self.model: Any = None
        self.speaker = None
        self.sample_rate = self.DEFAULT_SAMPLE_RATE
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._caching_enabled = enable_caching
        self._hub_load_speaker: str | None = None

        # Override device if GPU requested and available
        if gpu and torch.cuda.is_available() and self.device == "cpu":
            self.device = "cuda"

    def _torch_hub_speaker_for_load(self) -> str:
        """
        Speaker ID for ``torch.hub.load(..., model='silero_tts')`` on snakers4/silero-models.

        Current hub models expose string IDs such as ``v3_en``, ``v4_ru`` — not ``silero_tts_v4``.
        """
        lang = (self.default_language or "en").lower().strip()
        hub_by_lang = {
            "en": "v3_en",
            "ru": "v4_ru",
            "de": "v3_de",
            "es": "v3_es",
            "fr": "v3_fr",
        }
        return hub_by_lang.get(lang, "v3_en")

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._caching_enabled:
            cached_model = _get_cached_model(self.model_id, self.default_language, self.device)
            if cached_model is not None:
                logger.debug(f"Using cached model: {self.model_id} ({self.default_language})")
                self.model = cached_model
                self._hub_load_speaker = self._torch_hub_speaker_for_load()
                if hasattr(cached_model, "sample_rate"):
                    self.sample_rate = cached_model.sample_rate
                elif hasattr(cached_model, "config") and hasattr(
                    cached_model.config, "sample_rate"
                ):
                    self.sample_rate = cached_model.config.sample_rate
                self._initialized = True
                return True

        # Load model
        logger.info(f"Loading Silero TTS model {self.model_id} on {self.device}")

        try:
            import silero_tts
        except ImportError:
            silero_tts = None  # optional; torch.hub path below is sufficient

        try:
            # Load model using torch.hub (Silero's recommended method)
            model, _example_text = torch.hub.load(
                repo_or_dir="snakers4/silero-models",
                model="silero_tts",
                language=self.default_language,
                speaker=self._torch_hub_speaker_for_load(),
                trust_repo=True,
            )

            # torch.package TTS models may implement ``.to()`` in-place and return ``None``
            # (standard ``nn.Module.to`` returns self). Keep the original reference.
            moved = model.to(self.device)
            self.model = model if moved is None else moved
            # torch.package TTS wrappers may omit ``nn.Module.eval``; inference uses ``torch.inference_mode`` in synthesize.
            eval_fn = getattr(self.model, "eval", None)
            if callable(eval_fn):
                eval_fn()

            self._hub_load_speaker = self._torch_hub_speaker_for_load()

            # Get sample rate from model
            if hasattr(model, "sample_rate"):
                self.sample_rate = model.sample_rate
            elif hasattr(model, "config") and hasattr(model.config, "sample_rate"):
                self.sample_rate = model.config.sample_rate

            # Cache model
            if self._caching_enabled:
                _cache_model(self.model_id, self.default_language, self.device, self.model)

            logger.info(f"Silero TTS model loaded successfully (sample_rate: {self.sample_rate})")
            self._initialized = True
            return True

        except Exception as e:
            logger.error(f"Failed to load Silero model via torch.hub: {e}")
            logger.info("Trying alternative loading method...")

            # Alternative: Try direct model loading (optional package)
            try:
                if silero_tts is None:
                    raise ImportError("silero_tts not installed")
                from silero_tts import tts

                self.model = tts
                self.sample_rate = 24000  # Default for Silero
                logger.info("Silero TTS loaded via silero_tts package")
                self._initialized = True
                return True
            except Exception as e2:
                logger.error(f"Alternative loading also failed: {e2}")
                self._initialized = False
                return False

    def initialize(self) -> bool:
        """
        Initialize the Silero TTS model.

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

            return bool(self._load_model())

        except Exception as e:
            logger.error(f"Failed to initialize Silero TTS engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        language: str = "en",
        voice: str | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> npt.NDArray[np.float32] | None | tuple[npt.NDArray[np.float32] | None, dict[str, Any]]:
        """
        Synthesize speech from text using Silero TTS.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'en', 'ru', 'de')
            voice: Voice/speaker ID (optional, uses default for language)
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - speaker: Speaker ID (alternative to voice parameter)
                - speed: Speech speed (0.5-2.0, default 1.0)

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return None

        try:
            # Get speaker ID
            speaker_id = voice or kwargs.get("speaker")
            if speaker_id is None:
                # Use default speaker for language
                speaker_id = self._get_default_speaker(language)

            # Get speed
            speed = kwargs.get("speed", 1.0)

            # Synthesize using torch.hub model with inference mode for better performance
            with torch.inference_mode():
                if hasattr(self.model, "apply_tts"):
                    # Newer Silero API
                    audio = self.model.apply_tts(
                        text=text,
                        speaker=speaker_id,
                        sample_rate=self.sample_rate,
                        put_accent=True,
                        put_yo=True,
                    )
                    audio = torch.tensor(audio, dtype=torch.float32)
                elif callable(self.model):
                    # Direct function call
                    audio = self.model(text=text, speaker=speaker_id, sample_rate=self.sample_rate)
                    if not isinstance(audio, torch.Tensor):
                        audio = torch.tensor(audio, dtype=torch.float32)
                else:
                    # Try silero_tts package API
                    try:
                        from silero_tts import tts

                        audio = tts(
                            text=text,
                            speaker=speaker_id,
                            language=language,
                            model_path=None,  # Use default
                            device=self.device,
                        )
                        if isinstance(audio, torch.Tensor):
                            ...
                        else:
                            audio = torch.tensor(audio, dtype=torch.float32)
                    except Exception as e:
                        logger.error(f"Silero synthesis failed: {e}")
                        return None

            # Convert to numpy
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
                    audio, self.sample_rate, None, enhance_quality, calculate_quality
                )
                if isinstance(audio, tuple):
                    enhanced_audio, quality_metrics = audio
                    if output_path:
                        sf.write(output_path, enhanced_audio, self.sample_rate)
                        # File handoff: synthesis_service treats ``None`` + on-disk WAV as success.
                        # Do not return ``(None, metrics)`` — the service unpacks tuples as ``(audio, _)``.
                        return None
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
            logger.error(f"Silero TTS synthesis failed: {e}")
            return None

    def _get_default_speaker(self, _language: str) -> str:
        """Default ``apply_tts`` speaker for the loaded hub checkpoint (not the hub load ID)."""
        hub = self._hub_load_speaker or self._torch_hub_speaker_for_load()
        return _HUB_LOAD_TO_APPLY_TTS_SPEAKER.get(hub, "en_0")

    def _process_audio_quality(
        self,
        audio: npt.NDArray[np.float32],
        sample_rate: int,
        reference_audio: str | Path | None = None,
        enhance: bool = False,
        calculate: bool = False,
    ) -> npt.NDArray[np.float32] | tuple[npt.NDArray[np.float32], dict[str, Any]]:
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

        # Silero typically has multiple speakers per language
        # Format: "{language}_{speaker_index}"
        voices = []
        if language:
            # Return speakers for specific language
            for i in range(10):  # Typically 0-9 speakers per language
                voices.append(f"{language}_{i}")
        else:
            # Return all default speakers
            for lang in ["en", "ru", "de", "es", "fr", "it", "pt", "pl", "tr", "uk"]:
                voices.append(f"{lang}_0")

        return voices

    def get_languages(self) -> list[str]:
        """Get available languages."""
        return self.SUPPORTED_LANGUAGES

    def batch_synthesize(
        self,
        texts: list[str],
        language: str = "en",
        voice: str | None = None,
        output_dir: str | Path | None = None,
        **kwargs: Any,
    ) -> list[npt.NDArray[np.float32] | None]:
        """
        Synthesize multiple texts in batch with optimized processing.

        Args:
            texts: List of texts to synthesize
            language: Language code
            voice: Voice/speaker ID (optional)
            output_dir: Optional directory to save outputs
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio arrays
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [None] * len(texts)

        # Get speaker ID
        speaker_id = voice or kwargs.get("speaker")
        if speaker_id is None:
            speaker_id = self._get_default_speaker(language)

        results = []

        # Process in batches for better GPU utilization
        batch_size = self.batch_size
        for batch_start in range(0, len(texts), batch_size):
            batch_texts = texts[batch_start : batch_start + batch_size]
            batch_results: list[npt.NDArray[np.float32] | None] = []

            # Use inference mode for better performance
            with torch.inference_mode():
                for i, text in enumerate(batch_texts):
                    try:
                        # Synthesize
                        if hasattr(self.model, "apply_tts"):
                            audio = self.model.apply_tts(
                                text=text,
                                speaker=speaker_id,
                                sample_rate=self.sample_rate,
                                put_accent=True,
                                put_yo=True,
                            )
                            audio = torch.tensor(audio, dtype=torch.float32)
                        elif callable(self.model):
                            audio = self.model(
                                text=text,
                                speaker=speaker_id,
                                sample_rate=self.sample_rate,
                            )
                            if not isinstance(audio, torch.Tensor):
                                audio = torch.tensor(audio, dtype=torch.float32)
                        else:
                            from silero_tts import tts

                            audio = tts(
                                text=text,
                                speaker=speaker_id,
                                language=language,
                                model_path=None,
                                device=self.device,
                            )
                            if not isinstance(audio, torch.Tensor):
                                audio = torch.tensor(audio, dtype=torch.float32)

                        # Convert to numpy
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

                        # Save to file if output_dir provided
                        if output_dir:
                            output_path = Path(output_dir) / f"output_{batch_start + i:04d}.wav"
                            sf.write(str(output_path), audio, self.sample_rate)
                            batch_results.append(None)
                        else:
                            batch_results.append(audio)
                    except Exception as e:
                        logger.error(f"Batch synthesis failed for text {batch_start + i}: {e}")
                        batch_results.append(None)

            results.extend(batch_results)

            # Clear GPU cache periodically
            if torch.cuda.is_available() and (batch_start + batch_size) % (batch_size * 2) == 0:
                torch.cuda.empty_cache()

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def set_batch_size(self, batch_size: int) -> None:
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

    def cleanup(self) -> None:
        """Clean up resources."""
        try:
            # Don't delete cached model, just clear reference
            self.model = None

            # Clear CUDA cache if using GPU
            if self.device == "cuda" and torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._initialized = False
            logger.info("Silero TTS engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during Silero cleanup: {e}")

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "model_id": self.model_id,
                "default_language": self.default_language,
                "sample_rate": self.sample_rate,
                "supported_languages": len(self.SUPPORTED_LANGUAGES),
            }
        )
        return info


def create_silero_engine(
    model_id: str = "v4",
    language: str = "en",
    device: str | None = None,
    gpu: bool = False,
) -> SileroEngine:
    """Factory function to create a Silero TTS engine instance."""
    return SileroEngine(model_id=model_id, language=language, device=device, gpu=gpu)
