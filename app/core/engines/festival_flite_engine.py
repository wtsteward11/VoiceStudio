"""
Festival/Flite Engine for VoiceStudio
Legacy TTS system integration

Festival is a general multi-lingual speech synthesis system.
Flite (Festival Lite) is a lightweight, fast TTS engine derived from Festival.

Compatible with:
- Python 3.10+
- Festival/Flite system installation (requires separate installation)
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
    from .quality_metrics import calculate_all_metrics, calculate_similarity

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False

# Optional audio utilities import for quality enhancement
try:
    from app.core.audio.audio_utils import enhance_voice_quality, normalize_lufs, remove_artifacts

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False

logger = logging.getLogger(__name__)

# Import base protocol from canonical source
from .base import EngineProtocol


class FestivalFliteEngine(EngineProtocol):
    """
    Festival/Flite Engine for text-to-speech synthesis.

    Supports:
    - Festival (full system with multiple voices)
    - Flite (lightweight, fast synthesis)
    - Multiple languages (depends on installed voices)
    - SSML and plain text input
    """

    # Supported languages (depends on installed voices)
    SUPPORTED_LANGUAGES = [
        "en",
        "en_US",
        "en_GB",
        "en_AU",
        "en_IN",
        "en_NZ",
        "en_ZA",
        "es",
        "fr",
        "de",
        "it",
        "pt",
        "ru",
        "hi",
        "zh",
        "ja",
        "ko",
    ]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 16000

    def __init__(
        self,
        use_flite: bool = True,
        flite_path: str | None = None,
        festival_path: str | None = None,
        device: str | None = None,
        gpu: bool = False,
    ):
        # Use Flite (faster) or Festival (more features)
        """
        Initialize Festival/Flite engine.

        Args:
            use_flite: If True, use Flite; if False, use Festival
            flite_path: Path to flite executable (auto-detect if None)
            festival_path: Path to festival executable (auto-detect if None)
            device: Device parameter (not used, kept for protocol compatibility)
            gpu: GPU parameter (not used, kept for protocol compatibility)
        """
        super().__init__(device=device, gpu=gpu)

        self.use_flite = use_flite
        self.flite_path = flite_path
        self.festival_path = festival_path
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

        found = shutil.which(name)
        if found:
            return found

        found = shutil.which(f"{name}.exe")
        if found:
            return found

        return None

    def initialize(self) -> bool:
        """
        Initialize the Festival/Flite engine.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            engine_name = "Flite" if self.use_flite else "Festival"
            logger.info(f"Initializing {engine_name} engine")

            # Find executable
            if self.use_flite:
                self.executable_path = self._find_executable("flite", self.flite_path)
                if not self.executable_path:
                    logger.error("Flite executable not found. " "Install Flite or set flite_path.")
                    logger.error("Install from: http://www.festvox.org/flite/")
                    self._initialized = False
                    return False
            else:
                self.executable_path = self._find_executable("festival", self.festival_path)
                if not self.executable_path:
                    logger.error(
                        "Festival executable not found. " "Install Festival or set festival_path."
                    )
                    logger.error("Install from: " "http://www.cstr.ed.ac.uk/projects/festival/")
                    self._initialized = False
                    return False

            # Test executable
            try:
                if self.use_flite:
                    result = subprocess.run(
                        [self.executable_path, "--version"],
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )
                else:
                    result = subprocess.run(
                        [self.executable_path, "--version"],
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )

                if result.returncode == 0:
                    engine_name = "Flite" if self.use_flite else "Festival"
                    logger.info(f"{engine_name} found: {self.executable_path}")
                    logger.info(f"Version info: {result.stdout[:100]}")
                else:
                    logger.warning(f"Version check returned non-zero: {result.stderr}")
            except subprocess.TimeoutExpired:
                logger.warning("Version check timed out, but continuing")
            except Exception as e:
                logger.warning(f"Version check failed: {e}, but continuing")

            # Get available voices (Flite)
            if self.use_flite:
                try:
                    result = subprocess.run(
                        [self.executable_path, "-lv"],
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )
                    if result.returncode == 0:
                        # Parse voices from output
                        lines = result.stdout.strip().split("\n")
                        self.voices = [
                            line.strip()
                            for line in lines
                            if line.strip() and not line.startswith("#")
                        ]
                        logger.info(f"Found {len(self.voices)} Flite voices")
                    else:
                        # Default voices
                        self.voices = ["kal", "rms", "awb", "slt"]
                except Exception as e:
                    logger.warning(f"Failed to list voices: {e}")
                    self.voices = ["kal", "rms", "awb", "slt"]
            else:
                # Festival voices (more complex, would need scheme script)
                self.voices = ["default"]

            self.available_languages = ["en"]  # Default, can be extended

            # Create reusable temp directory (using temp file manager if available)
            try:
                from app.core.utils.temp_file_manager import get_temp_file_manager

                temp_manager = get_temp_file_manager()
                self._temp_dir = temp_manager.create_temp_directory(
                    prefix="festival_flite_", owner="festival_flite_engine"
                )
                logger.debug(f"Created temp directory via manager: {self._temp_dir}")
            except Exception as e:
                logger.debug(f"Temp file manager not available, using tempfile: {e}")
                self._temp_dir = tempfile.mkdtemp(prefix="festival_flite_")
                logger.debug(f"Created temp directory: {self._temp_dir}")

            self._initialized = True
            engine_name = "Flite" if self.use_flite else "Festival"
            logger.info(f"{engine_name} engine initialized successfully")
            return True

        except Exception as e:
            engine_name = "Flite" if self.use_flite else "Festival"
            logger.error(f"Failed to initialize {engine_name} engine: {e}")
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
        Synthesize speech from text using Festival/Flite.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'en')
            voice: Voice name (optional, uses default if not specified)
            output_path: Optional path to save output audio
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
            # Check synthesis cache (LRU) - optimized
            if self.enable_cache:
                cache_key = hashlib.md5(
                    f"{text}_{language}_{voice}_{self.use_flite}".encode()
                ).hexdigest()
                if cache_key in self._synthesis_cache:
                    logger.debug("Using cached Festival/Flite synthesis result")
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
                if self.use_flite:
                    cmd: list[str] = [self.executable_path, "-t", text, "-o", tmp_output]

                    # Add voice if specified
                    if voice:
                        cmd.extend(["-voice", voice])
                    elif self.voices:
                        # Use first available voice
                        cmd.extend(["-voice", self.voices[0]])
                else:
                    temp_dir = self._temp_dir if self._temp_dir else None
                    scheme_script = f"""
                    (set! utt1 (Utterance Text "{text}"))
                    (utt.synth utt1)
                    (utt.save.wave utt1 "{tmp_output}")
                    """

                    with tempfile.NamedTemporaryFile(
                        mode="w", suffix=".scm", delete=False, dir=temp_dir
                    ) as scm_file:
                        scm_file.write(scheme_script)
                        scm_path = scm_file.name

                    cmd = [self.executable_path, "-b", scm_path]

                result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)

                if result.returncode != 0:
                    engine_name = "Festival/Flite"
                    logger.error(f"{engine_name} synthesis failed: {result.stderr}")
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
                        f"{text}_{language}_{voice}_{self.use_flite}".encode()
                    ).hexdigest()
                    self._synthesis_cache[cache_key] = {
                        "audio": audio.copy(),
                        "sample_rate": sample_rate,
                        "quality_metrics": (quality_metrics_result if calculate_quality else {}),
                    }
                    self._synthesis_cache.move_to_end(cache_key)  # LRU update

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
                    if not self.use_flite and "scm_path" in locals() and os.path.exists(scm_path):
                        os.unlink(scm_path)
                except Exception as e:
                    logger.warning(f"Failed to cleanup temp files: {e}")

        except subprocess.TimeoutExpired:
            logger.error("Festival/Flite synthesis timed out")
            return None
        except Exception as e:
            logger.error(f"Festival/Flite synthesis failed: {e}")
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
                start_time = time.perf_counter() if hasattr(time, "perf_counter") else None
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
                        engine_name = "flite" if self.use_flite else "festival"
                        metrics.record_synthesis_time(engine_name, duration, cached=False)
                    except Exception:
                        logger.debug("Performance metrics unavailable for festival/flite batch.")
                return result
            except Exception as e:
                logger.error(f"Batch synthesis failed for text: {e}")
                # Record error if metrics available
                try:
                    from .performance_metrics import get_engine_metrics

                    metrics = get_engine_metrics()
                    engine_name = "flite" if self.use_flite else "festival"
                    metrics.record_error(engine_name, "synthesis_error")
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
            logger.info("Festival/Flite engine cleaned up")
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
                "engine_type": "Flite" if self.use_flite else "Festival",
                "executable_path": self.executable_path,
                "voices_count": len(self.voices),
                "languages": self.available_languages,
                "sample_rate": self.DEFAULT_SAMPLE_RATE,
            }
        )
        return info


def create_festival_flite_engine(
    use_flite: bool = True,
    flite_path: str | None = None,
    festival_path: str | None = None,
    device: str | None = None,
    gpu: bool = False,
) -> FestivalFliteEngine:
    """Factory function to create a Festival/Flite engine instance."""
    return FestivalFliteEngine(
        use_flite=use_flite,
        flite_path=flite_path,
        festival_path=festival_path,
        device=device,
        gpu=gpu,
    )
