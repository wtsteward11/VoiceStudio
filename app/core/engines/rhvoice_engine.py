"""
RHVoice Engine for VoiceStudio
Multilingual TTS with high-quality voices integration

RHVoice is a multilingual TTS system with high-quality voices.
It supports multiple languages and provides natural-sounding speech.

Compatible with:
- Python 3.10+
- RHVoice system installation (requires separate installation)
- Command-line interface for synthesis
"""

from __future__ import annotations

import logging
import os
import shutil
import subprocess
import tempfile
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

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


class RHVoiceEngine(EngineProtocol):
    """
    RHVoice Engine for high-quality multilingual text-to-speech synthesis.

    Supports:
    - Multiple languages (Russian, English, Ukrainian, etc.)
    - High-quality natural voices
    - SSML markup
    - Prosody control
    """

    # Supported languages
    SUPPORTED_LANGUAGES = ["ru", "en", "uk", "tt", "ky", "ka", "eo"]

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self,
        rhvoice_path: str | None = None,
        device: str | None = None,
        gpu: bool = False,
    ):
        """
        Initialize RHVoice engine.

        Args:
            rhvoice_path: Path to RHVoice executable (auto-detect if None)
            device: Device parameter (not used, kept for protocol compatibility)
            gpu: GPU parameter (not used, kept for protocol compatibility)
        """
        super().__init__(device=device, gpu=gpu)

        self.rhvoice_path = rhvoice_path
        self.executable_path: str | None = None
        self.voices: list[str] = []
        self.available_languages: list[str] = []
        self.batch_size = 4
        self.lazy_load = True
        self._temp_dir: str | None = None
        self._synthesis_cache: OrderedDict[str, Any] = OrderedDict()
        self._cache_max_size = 100

    def _find_executable(self, name: str, custom_path: str | None = None) -> str | None:
        """Find executable in PATH or custom path."""
        if custom_path and os.path.isfile(custom_path) and os.access(custom_path, os.X_OK):
            return custom_path

        if custom_path and os.path.isdir(custom_path):
            exe_path = os.path.join(custom_path, name)
            if os.path.isfile(exe_path) and os.access(exe_path, os.X_OK):
                return exe_path

        # Try rhvoice-say or rhvoice-cli
        for exe_name in [name, "rhvoice-say", "rhvoice-cli", "RHVoice-test"]:
            found = shutil.which(exe_name)
            if found:
                return found

            found = shutil.which(f"{exe_name}.exe")
            if found:
                return found

        return None

    def initialize(self) -> bool:
        """
        Initialize the RHVoice engine.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            logger.info("Initializing RHVoice engine")

            # Find executable
            self.executable_path = self._find_executable("rhvoice-say", self.rhvoice_path)
            if not self.executable_path:
                logger.error("RHVoice executable not found. Install RHVoice or set rhvoice_path.")
                logger.error("Install from: https://github.com/Olga-Yakovleva/RHVoice")
                logger.error("Linux: Install via package manager (rhvoice, rhvoice-common)")
                logger.error("Windows: Download from GitHub releases")
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
                    logger.info(f"RHVoice found: {self.executable_path}")
                    logger.info(f"Version: {result.stdout.strip()[:100]}")
                else:
                    logger.warning(f"Version check returned non-zero: {result.stderr}")
            except subprocess.TimeoutExpired:
                logger.warning("Version check timed out, but continuing")
            except Exception as e:
                logger.warning(f"Version check failed: {e}, but continuing")

            # Get available voices (try --list-voices or similar)
            try:
                # Try different commands to list voices
                for cmd_variant in [
                    [self.executable_path, "--list-voices"],
                    [self.executable_path, "-l"],
                    ["rhvoice-cli", "--list-voices"],
                ]:
                    try:
                        result = subprocess.run(
                            cmd_variant, capture_output=True, text=True, timeout=5
                        )
                        if result.returncode == 0 and result.stdout.strip():
                            # Parse voices from output
                            lines = result.stdout.strip().split("\n")
                            self.voices = [line.strip() for line in lines if line.strip()]
                            # Extract languages from voices
                            for voice in self.voices:
                                # Voice format might be "voice_name (language)" or just "voice_name"
                                if "(" in voice:
                                    lang = voice.split("(")[1].split(")")[0].strip()
                                    if lang not in self.available_languages:
                                        self.available_languages.append(lang)
                            logger.info(
                                f"Found {len(self.voices)} RHVoice voices in {len(self.available_languages)} languages"
                            )
                            break
                    except (
                        subprocess.TimeoutExpired,
                        FileNotFoundError,
                        subprocess.SubprocessError,
                    ):
                        continue

                if not self.voices:
                    # Default voices (common RHVoice voices)
                    self.voices = [
                        "Anna (ru)",
                        "Elena (ru)",
                        "Irina (ru)",
                        "Aleksandr (ru)",
                        "Pavel (ru)",
                        "Bdl (en)",
                        "Clb (en)",
                        "Slt (en)",
                    ]
                    self.available_languages = ["ru", "en"]
                    logger.info("Using default RHVoice voices")
            except Exception as e:
                logger.warning(f"Failed to list voices: {e}")
                self.voices = ["Anna (ru)", "Bdl (en)"]
                self.available_languages = ["ru", "en"]

            # Create reusable temp directory for file I/O optimization
            if self._temp_dir is None:
                self._temp_dir = tempfile.mkdtemp(prefix="rhvoice_")
                logger.debug(f"Created reusable temp directory: {self._temp_dir}")

            # Lazy loading: defer voice discovery if needed
            if self.lazy_load:
                logger.debug("Lazy loading enabled")

            self._initialized = True
            logger.info("RHVoice engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize RHVoice engine: {e}")
            self._initialized = False
            return False

    def synthesize(
        self,
        text: str,
        language: str = "ru",
        voice: str | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict]:
        """
        Synthesize speech from text using RHVoice.

        Args:
            text: Text to synthesize
            language: Language code (e.g., 'ru', 'en', 'uk')
            voice: Voice name (optional, uses default for language if not specified)
            output_path: Optional path to save output audio
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            **kwargs: Additional synthesis parameters
                - ssml: If True, treat text as SSML markup
                - rate: Speech rate (0.0-2.0, default 1.0)
                - pitch: Pitch adjustment (-1.0 to 1.0, default 0.0)
                - volume: Volume (0.0-1.0, default 1.0)

        Returns:
            Audio array (numpy) or None if synthesis failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        if not self._initialized and not self.initialize():
            return None

        try:
            # Use reusable temp directory for better performance
            if self._temp_dir and os.path.exists(self._temp_dir):
                import uuid

                tmp_output = os.path.join(self._temp_dir, f"{uuid.uuid4().hex}.wav")
            else:
                # Fallback to standard temp file
                with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_file:
                    tmp_output = tmp_file.name

            try:
                # Select voice
                selected_voice = voice
                if not selected_voice:
                    # Find default voice for language
                    for v in self.voices:
                        if f"({language})" in v:
                            selected_voice = v.split("(")[0].strip()
                            break
                    if not selected_voice and self.voices:
                        selected_voice = self.voices[0].split("(")[0].strip()

                # Build command
                if self.executable_path is None:
                    return None
                cmd: list[str] = [self.executable_path]

                # Add voice if specified
                if selected_voice:
                    cmd.extend(["-v", selected_voice])

                # Add language
                cmd.extend(["-l", language])

                # Add output file
                cmd.extend(["-o", tmp_output])

                # Add SSML flag if needed
                if kwargs.get("ssml", False):
                    cmd.append("--ssml")

                # Add prosody parameters
                rate = kwargs.get("rate", 1.0)
                if rate != 1.0:
                    cmd.extend(["--rate", str(rate)])

                pitch = kwargs.get("pitch", 0.0)
                if pitch != 0.0:
                    cmd.extend(["--pitch", str(pitch)])

                volume = kwargs.get("volume", 1.0)
                if volume != 1.0:
                    cmd.extend(["--volume", str(volume)])

                # Add text (last argument or via stdin)
                # Some RHVoice versions accept text as argument, others via stdin
                cmd.append(text)

                # Run synthesis
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)

                if result.returncode != 0:
                    # Try with stdin if direct argument failed
                    if text in cmd:
                        cmd.remove(text)
                        result = subprocess.run(
                            cmd, input=text, capture_output=True, text=True, timeout=60
                        )

                    if result.returncode != 0:
                        logger.error(f"RHVoice synthesis failed: {result.stderr}")
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

                # Resample if needed
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

                # Save to file if requested
                if output_path:
                    sf.write(output_path, audio, sample_rate)
                    logger.info(f"Audio saved to: {output_path}")
                    return None

                return np.asarray(audio)

            finally:
                # Cleanup temp files
                try:
                    if os.path.exists(tmp_output):
                        os.unlink(tmp_output)
                except Exception as e:
                    logger.warning(f"Failed to cleanup temp files: {e}")

        except subprocess.TimeoutExpired:
            logger.error("RHVoice synthesis timed out")
            return None
        except Exception as e:
            logger.error(f"RHVoice synthesis failed: {e}")
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
                        ref_audio, _ = sf.read(reference_audio)
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

    def cleanup(self):
        """Clean up resources."""
        try:
            # Clean up temp directory
            if self._temp_dir and os.path.exists(self._temp_dir):
                try:
                    # Remove all files in temp directory
                    for filename in os.listdir(self._temp_dir):
                        file_path = os.path.join(self._temp_dir, filename)
                        try:
                            if os.path.isfile(file_path):
                                os.unlink(file_path)
                        except Exception as e:
                            logger.debug(f"Failed to remove temp file {file_path}: {e}")
                    # Remove directory
                    os.rmdir(self._temp_dir)
                    logger.debug(f"Cleaned up temp directory: {self._temp_dir}")
                except Exception as e:
                    logger.warning(f"Failed to cleanup temp directory: {e}")
                self._temp_dir = None

            # Clear cache
            self._synthesis_cache.clear()

            self._initialized = False
            logger.info("RHVoice engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def batch_synthesize(
        self,
        text_list: list[str],
        language: str = "ru",
        voice: str | None = None,
        output_paths: list[str | Path] | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        batch_size: int | None = None,
        **kwargs,
    ) -> list[np.ndarray | None | tuple[np.ndarray | None, dict]]:
        """
        Synthesize multiple texts in batch with optimized parallel processing.

        Args:
            text_list: List of texts to synthesize
            language: Language code (e.g., 'ru', 'en', 'uk')
            voice: Voice name (optional, uses default for language if not specified)
            output_paths: Optional list of paths to save output audio (must match text_list length)
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio
            batch_size: Number of items to process in parallel (default: self.batch_size)
            **kwargs: Additional synthesis parameters

        Returns:
            List of audio arrays (numpy) or None if synthesis failed,
            or tuples of (audio, quality_metrics) if calculate_quality=True
        """
        if not self._initialized and not self.initialize():
            return [None] * len(text_list)

        # Use configured batch size if not specified
        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        # Prepare output paths with proper type for None entries
        resolved_paths: list[str | Path | None]
        if output_paths is None:
            resolved_paths = [None] * len(text_list)
        elif len(output_paths) != len(text_list):
            logger.warning("output_paths length doesn't match text_list, using None for extras")
            padded: list[str | Path | None] = list(output_paths[: len(text_list)])
            padded.extend([None] * (len(text_list) - len(output_paths)))
            resolved_paths = padded
        else:
            resolved_paths = list(output_paths)

        # Process in parallel batches for better performance
        def synthesize_single(
            args: tuple[str, str | Path | None],
        ) -> np.ndarray | None | tuple[np.ndarray | None, dict[str, Any]]:
            text, out_path = args
            try:
                return self.synthesize(
                    text=text,
                    language=language,
                    voice=voice,
                    output_path=out_path,
                    enhance_quality=enhance_quality,
                    calculate_quality=calculate_quality,
                    **kwargs,
                )
            except Exception as e:
                logger.error(f"Batch synthesis failed for text: {e}")
                return None

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            results = list(executor.map(synthesize_single, zip(text_list, resolved_paths)))

        return results

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "synthesis_cache_size": len(self._synthesis_cache),
            "max_cache_size": self._cache_max_size,
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


def create_rhvoice_engine(
    rhvoice_path: str | None = None, device: str | None = None, gpu: bool = False
) -> RHVoiceEngine:
    """Factory function to create an RHVoice engine instance."""
    return RHVoiceEngine(rhvoice_path=rhvoice_path, device=device, gpu=gpu)
