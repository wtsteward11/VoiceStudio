"""
eSpeak NG Engine for VoiceStudio
Compact multilingual TTS integration

eSpeak NG (Next Generation) is a compact, open-source multilingual
text-to-speech synthesis engine. It's lightweight and supports many languages.

Compatible with:
- Python 3.10+
- eSpeak NG system installation (requires separate installation)
- Command-line interface for synthesis
"""

from __future__ import annotations

import hashlib
import logging
import os
import shutil
import subprocess
import tempfile
import time
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
import soundfile as sf

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

# Import base protocol from canonical source
from .base import EngineProtocol


class ESpeakNGEngine(EngineProtocol):
    """
    eSpeak NG Engine for compact multilingual text-to-speech synthesis.

    Supports:
    - 100+ languages
    - Multiple voices per language
    - SSML markup
    - Phonetic input
    - Fast, lightweight synthesis
    """

    # Supported languages (eSpeak NG supports 100+ languages)
    SUPPORTED_LANGUAGES = [
        "en",
        "en-us",
        "en-gb",
        "en-au",
        "en-in",
        "en-nz",
        "en-za",
        "es",
        "es-es",
        "es-mx",
        "es-ar",
        "fr",
        "fr-fr",
        "fr-ca",
        "fr-be",
        "de",
        "de-de",
        "de-at",
        "de-ch",
        "it",
        "pt",
        "pt-br",
        "pt-pt",
        "ru",
        "pl",
        "nl",
        "nl-nl",
        "nl-be",
        "sv",
        "da",
        "no",
        "fi",
        "cs",
        "sk",
        "hu",
        "ro",
        "bg",
        "hr",
        "sr",
        "sl",
        "et",
        "lv",
        "lt",
        "el",
        "tr",
        "ar",
        "he",
        "hi",
        "zh",
        "zh-cn",
        "zh-tw",
        "ja",
        "ko",
        "th",
        "vi",
        "id",
        "ms",
        "ta",
        "te",
        "kn",
        "ml",
        "bn",
        "gu",
        "pa",
        "ur",
        "fa",
        "uk",
        "be",
        "ka",
        "hy",
        "az",
        "kk",
        "ky",
        "uz",
        "mn",
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
    DEFAULT_SAMPLE_RATE = 22050

    def __init__(
        self,
        espeak_path: str | None = None,
        device: str | None = None,
        gpu: bool = False,
    ):
        """
        Initialize eSpeak NG engine.

        Args:
            espeak_path: Path to espeak-ng executable (auto-detect if None)
            device: Device parameter (not used, kept for protocol compatibility)
            gpu: GPU parameter (not used, kept for protocol compatibility)
        """
        super().__init__(device=device, gpu=gpu)

        self.espeak_path = espeak_path
        self.executable_path: str | None = None
        self.voices: list[str] = []
        self.available_languages: list[str] = []
        self._synthesis_cache: OrderedDict[str, dict] = OrderedDict()
        self._cache_max_size = 200  # Increased cache size for better hit rate
        self.enable_cache = True
        self.batch_size = 8  # Increased batch size for better parallelization
        self._temp_dir: str | Path | None = None
        self._cache_stats = {
            "hits": 0,
            "misses": 0,
        }

    @staticmethod
    def _find_executable(name: str, custom_path: str | None = None) -> str | None:
        """Find executable in PATH or custom path."""
        if custom_path and os.path.isfile(custom_path) and os.access(custom_path, os.X_OK):
            return custom_path

        if custom_path and os.path.isdir(custom_path):
            exe_path = os.path.join(custom_path, name)
            if os.path.isfile(exe_path) and os.access(exe_path, os.X_OK):
                return exe_path

        for exe_name in [name, "espeak", "espeak-ng"]:
            found = shutil.which(exe_name)
            if found:
                return found

            found = shutil.which(f"{exe_name}.exe")
            if found:
                return found

        return None

    def initialize(self) -> bool:
        """
        Initialize the eSpeak NG engine.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            logger.info("Initializing eSpeak NG engine")

            # Find executable
            self.executable_path = self._find_executable("espeak-ng", self.espeak_path)
            if not self.executable_path:
                logger.error(
                    "eSpeak NG executable not found. Install eSpeak NG or set espeak_path."
                )
                logger.error("Install from: https://github.com/espeak-ng/espeak-ng")
                logger.error("Windows: https://github.com/espeak-ng/espeak-ng/releases")
                self._initialized = False
                return False

            # Test executable
            try:
                result = subprocess.run(
                    [self.executable_path, "--version"],
                    capture_output=True,
                    text=True,
                    timeout=5,
                )

                if result.returncode == 0:
                    logger.info(f"eSpeak NG found: {self.executable_path}")
                    logger.info(f"Version: {result.stdout.strip()[:100]}")
                else:
                    logger.warning(f"Version check returned non-zero: {result.stderr}")
            except subprocess.TimeoutExpired:
                logger.warning("Version check timed out, but continuing")
            except Exception as e:
                logger.warning(f"Version check failed: {e}, but continuing")

            # Get available voices
            try:
                result = subprocess.run(
                    [self.executable_path, "--voices"],
                    capture_output=True,
                    text=True,
                    timeout=5,
                )
                if result.returncode == 0:
                    # Parse voices from output (format: "Pty Language Age/Gender VoiceName File")
                    lines = result.stdout.strip().split("\n")[1:]  # Skip header
                    self.voices = []
                    for line in lines:
                        if line.strip():
                            parts = line.split()
                            if len(parts) >= 4:
                                voice_name = parts[3]
                                language = parts[1]
                                self.voices.append(f"{voice_name} ({language})")
                                if language not in self.available_languages:
                                    self.available_languages.append(language)
                    logger.info(
                        f"Found {len(self.voices)} eSpeak NG voices in {len(self.available_languages)} languages"
                    )
                else:
                    # Default voices
                    self.voices = ["default (en)"]
                    self.available_languages = ["en"]
            except Exception as e:
                logger.warning(f"Failed to list voices: {e}")
                self.voices = ["default (en)"]
                self.available_languages = ["en"]

            # Create reusable temp directory (using temp file manager if available)
            try:
                from app.core.utils.temp_file_manager import get_temp_file_manager

                temp_manager = get_temp_file_manager()
                self._temp_dir = temp_manager.create_temp_directory(
                    prefix="espeak_ng_", owner="espeak_ng_engine"
                )
                logger.debug(f"Created temp directory via manager: {self._temp_dir}")
            except Exception as e:
                logger.debug(f"Temp file manager not available, using tempfile: {e}")
                self._temp_dir = tempfile.mkdtemp(prefix="espeak_ng_")
                logger.debug(f"Created temp directory: {self._temp_dir}")

            self._initialized = True
            logger.info("eSpeak NG engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize eSpeak NG engine: {e}")
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
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using eSpeak NG.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'en', 'en-us', 'es', 'fr')
            voice: Voice name (optional, uses default for language if not specified)
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - speed: Speech rate (50-450, default 175)
                - pitch: Pitch adjustment (0-99, default 50)
                - amplitude: Amplitude (0-200, default 100)
                - ssml: If True, treat text as SSML markup

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        if not self._initialized and not self.initialize():
            return None

        try:
            # Check synthesis cache (LRU) - optimized
            if self.enable_cache:
                cache_key = hashlib.md5(
                    f"{text}_{language}_{voice}_{kwargs.get('speed', 175)}_{kwargs.get('pitch', 50)}_{kwargs.get('amplitude', 100)}".encode()
                ).hexdigest()
                if cache_key in self._synthesis_cache:
                    logger.debug("Using cached eSpeak NG synthesis result")
                    self._synthesis_cache.move_to_end(cache_key)  # LRU update
                    self._cache_stats["hits"] += 1
                    cached_result = self._synthesis_cache[cache_key]
                    # Return cached audio
                    if output_path:
                        sf.write(
                            output_path,
                            cached_result["audio"],
                            cached_result["sample_rate"],
                        )
                        return None
                    if calculate_quality:
                        return np.asarray(cached_result["audio"]), cached_result.get(
                            "quality_metrics", {}
                        )
                    return np.asarray(cached_result["audio"])
                else:
                    self._cache_stats["misses"] += 1

            if self.executable_path is None:
                logger.error("Executable path not set")
                return None

            temp_dir = self._temp_dir if self._temp_dir else None
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False, dir=temp_dir) as tmp_file:
                tmp_output = tmp_file.name

            try:
                cmd: list[str] = [
                    self.executable_path,
                    "-v",
                    language if not voice else voice.split("(")[0].strip(),
                    "-s",
                    str(kwargs.get("speed", 175)),  # Speed
                    "-p",
                    str(kwargs.get("pitch", 50)),  # Pitch
                    "-a",
                    str(kwargs.get("amplitude", 100)),  # Amplitude
                    "-w",
                    tmp_output,  # Output WAV file
                ]

                # Add SSML flag if needed
                if kwargs.get("ssml", False):
                    cmd.append("-m")  # SSML markup mode

                # Add text (last argument)
                cmd.append(text)

                # Run synthesis
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)

                if result.returncode != 0:
                    logger.error(f"eSpeak NG synthesis failed: {result.stderr}")
                    return None

                # Read generated audio
                if not os.path.exists(tmp_output):
                    logger.error("Output file was not created")
                    return None

                audio, sample_rate = sf.read(tmp_output)

                # Convert to mono if stereo
                if len(audio.shape) > 1:
                    audio = np.mean(audio, axis=1)

                # Convert to float32
                if audio.dtype != np.float32:
                    audio = audio.astype(np.float32)

                # Normalize
                if np.max(np.abs(audio)) > 0:
                    audio = audio / np.max(np.abs(audio)) * 0.95

                # Cache result if successful (LRU)
                quality_metrics_result = {}
                if self.enable_cache:
                    # Process quality if needed for caching
                    if calculate_quality:
                        quality_metrics_result = self._calculate_quality_metrics(audio, sample_rate)

                    # Manage cache size - remove oldest entries if cache is full
                    if len(self._synthesis_cache) >= self._cache_max_size:
                        oldest_key = next(iter(self._synthesis_cache))
                        del self._synthesis_cache[oldest_key]

                    cache_key = hashlib.md5(
                        f"{text}_{language}_{voice}_{kwargs.get('speed', 175)}_{kwargs.get('pitch', 50)}_{kwargs.get('amplitude', 100)}".encode()
                    ).hexdigest()
                    self._synthesis_cache[cache_key] = {
                        "audio": audio.copy(),
                        "sample_rate": sample_rate,
                        "quality_metrics": (quality_metrics_result if calculate_quality else {}),
                    }
                    self._synthesis_cache.move_to_end(cache_key)  # LRU update

                # Resample if needed (eSpeak NG typically outputs 22050 Hz)
                if sample_rate != self.DEFAULT_SAMPLE_RATE:
                    try:
                        import librosa

                        audio = librosa.resample(
                            audio,
                            orig_sr=sample_rate,
                            target_sr=self.DEFAULT_SAMPLE_RATE,
                        )
                        sample_rate = self.DEFAULT_SAMPLE_RATE
                    except ImportError:
                        logger.warning("librosa not available for resampling")

                # Apply quality processing if requested
                if enhance_quality or calculate_quality:
                    audio = self._process_audio_quality(
                        audio, sample_rate, None, enhance_quality, calculate_quality
                    )
                    if isinstance(audio, tuple):
                        enhanced_audio, quality_metrics = audio
                        if output_path:
                            sf.write(output_path, enhanced_audio, sample_rate)
                            return None, quality_metrics
                        return enhanced_audio, quality_metrics
                    else:
                        if output_path:
                            sf.write(output_path, audio, sample_rate)
                            return None
                        return audio

                if output_path:
                    sf.write(output_path, audio, sample_rate)
                    logger.info(f"Audio saved to: {output_path}")
                    return None

                return np.asarray(audio)

            finally:
                # Cleanup temporary files
                try:
                    if os.path.exists(tmp_output):
                        os.unlink(tmp_output)
                except Exception as e:
                    logger.warning(f"Failed to cleanup temp files: {e}")

        except subprocess.TimeoutExpired:
            logger.error("eSpeak NG synthesis timed out")
            return None
        except Exception as e:
            logger.error(f"eSpeak NG synthesis failed: {e}")
            return None

    def _calculate_quality_metrics(
        self,
        audio: np.ndarray,
        sample_rate: int,
        reference_audio: str | Path | None = None,
    ) -> dict:
        """Calculate quality metrics for audio."""
        quality_metrics = {}

        if HAS_QUALITY_METRICS:
            try:
                quality_metrics = calculate_all_metrics(audio, reference_audio, sample_rate)
                if reference_audio:
                    try:
                        ref_audio, ref_sr = sf.read(reference_audio)
                        similarity = calculate_similarity(audio, ref_audio)
                        quality_metrics["similarity"] = similarity
                    except Exception as e:
                        logger.warning(f"Similarity calculation failed: {e}")
            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        return quality_metrics

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

        if calculate:
            quality_metrics = self._calculate_quality_metrics(audio, sample_rate, reference_audio)

        if calculate:
            return audio, quality_metrics
        return audio

    def get_voices(self, language: str | None = None) -> list[str]:
        """Get available voices."""
        if not self._initialized and not self.initialize():
            return []
        if language:
            return [v for v in self.voices if f"({language})" in v]
        return self.voices

    def get_languages(self) -> list[str]:
        """Get available languages."""
        if not self._initialized and not self.initialize():
            return []
        return self.available_languages if self.available_languages else self.SUPPORTED_LANGUAGES

    def batch_synthesize(
        self,
        text_list: list[str],
        language: str = "en",
        voice: str | None = None,
        output_paths: list[str | Path] | None = None,
        batch_size: int | None = None,
        **kwargs,
    ) -> list[np.ndarray | None | tuple[np.ndarray | None, dict]]:
        """
        Synthesize multiple texts in batch with optimized parallel processing.

        Args:
            text_list: List of texts to synthesize
            language: Language code
            voice: Voice name (optional)
            output_paths: Optional list of output paths
            batch_size: Number of parallel synthesis operations (default: self.batch_size)
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio arrays or tuples of (audio, quality_metrics)
        """
        if not self._initialized and not self.initialize():
            return [None] * len(text_list)

        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        resolved_paths: list[str | Path | None]
        if output_paths is None:
            resolved_paths = [None] * len(text_list)
        elif len(output_paths) != len(text_list):
            logger.warning("output_paths length doesn't match text_list, using None for extras")
            resolved_paths = list(output_paths[: len(text_list)]) + [None] * (
                len(text_list) - len(output_paths)
            )
        else:
            resolved_paths = list(output_paths)

        def synthesize_single(args):
            text, output_path = args
            try:
                # Record synthesis time if metrics available
                start_time = time.time() if hasattr(time, "perf_counter") else None
                result = self.synthesize(
                    text=text,
                    language=language,
                    voice=voice,
                    output_path=output_path,
                    **kwargs,
                )
                # Record metrics if available
                if start_time and hasattr(time, "perf_counter"):
                    try:
                        from .performance_metrics import get_engine_metrics

                        metrics = get_engine_metrics()
                        duration = time.perf_counter() - start_time
                        metrics.record_synthesis_time("espeak_ng", duration, cached=False)
                    except Exception:
                        logger.debug("Performance metrics unavailable for espeak_ng batch.")
                return result
            except Exception as e:
                logger.error(f"Batch synthesis failed for text: {e}")
                # Record error if metrics available
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    metrics.record_error("espeak_ng", "synthesis_error")
                except Exception:
                    ...
                return None

        # Optimize batch processing with better chunking
        results = []
        for i in range(0, len(text_list), actual_batch_size):
            batch_texts = text_list[i : i + actual_batch_size]
            batch_outputs = resolved_paths[i : i + actual_batch_size]

            with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
                batch_results = list(
                    executor.map(synthesize_single, zip(batch_texts, batch_outputs))
                )
            results.extend(batch_results)

        return results

    def cleanup(self):
        """Clean up resources (enhanced)."""
        try:
            # Cleanup temp directory (using temp file manager if available)
            if self._temp_dir:
                try:
                    from app.core.utils.temp_file_manager import get_temp_file_manager

                    temp_manager = get_temp_file_manager()
                    temp_manager.remove_temp_file(self._temp_dir, force=True)
                    logger.debug(f"Removed temp directory via manager: {self._temp_dir}")
                except Exception:
                    # Fallback to direct removal
                    if os.path.exists(self._temp_dir):
                        try:
                            shutil.rmtree(self._temp_dir)
                            logger.debug(f"Removed temp directory: {self._temp_dir}")
                        except Exception as e:
                            logger.warning(f"Failed to remove temp directory: {e}")
                self._temp_dir = None

            # Clear synthesis cache
            self._synthesis_cache.clear()
            self._cache_stats = {"hits": 0, "misses": 0}

            self._initialized = False
            logger.info("eSpeak NG engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def clear_cache(self):
        """Clear synthesis cache."""
        self._synthesis_cache.clear()
        logger.info("Synthesis cache cleared")

    def get_cache_stats(self) -> dict[str, int | float | str]:
        """Get cache statistics (enhanced)."""
        total_requests = self._cache_stats["hits"] + self._cache_stats["misses"]
        hit_rate = (self._cache_stats["hits"] / total_requests * 100) if total_requests > 0 else 0.0
        return {
            "cache_size": len(self._synthesis_cache),
            "max_cache_size": self._cache_max_size,
            "cache_enabled": self.enable_cache,
            "cache_hits": self._cache_stats["hits"],
            "cache_misses": self._cache_stats["misses"],
            "cache_hit_rate": f"{hit_rate:.2f}%",
        }

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "executable_path": self.executable_path,
                "voices_count": len(self.voices),
                "languages": self.available_languages,
                "sample_rate": self.DEFAULT_SAMPLE_RATE,
            }
        )
        return info


def create_espeak_ng_engine(
    espeak_path: str | None = None, device: str | None = None, gpu: bool = False
) -> ESpeakNGEngine:
    """Factory function to create an eSpeak NG engine instance."""
    return ESpeakNGEngine(espeak_path=espeak_path, device=device, gpu=gpu)
