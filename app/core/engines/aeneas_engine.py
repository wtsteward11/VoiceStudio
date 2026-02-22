"""
Aeneas Engine for VoiceStudio
Audio-text alignment and subtitle generation integration

Aeneas is a tool for automatically synchronizing audio and text,
useful for creating subtitles, captions, and audio-text alignment.

Compatible with:
- Python 3.10+
- aeneas>=1.7.3
- FFmpeg (system dependency)
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import shutil
import subprocess
import tempfile
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Import base protocol from canonical source
from .base import EngineProtocol


class AeneasEngine(EngineProtocol):
    """
    Aeneas Engine for audio-text alignment and subtitle generation.

    Supports:
    - Automatic audio-text synchronization
    - Subtitle generation (SRT, VTT, JSON)
    - Multiple languages
    - Forced alignment
    """

    # Supported output formats
    SUPPORTED_FORMATS = ["srt", "vtt", "json", "smil", "ttml", "audacity"]

    # Supported languages (Aeneas supports many languages)
    SUPPORTED_LANGUAGES = [
        "en",
        "es",
        "fr",
        "de",
        "it",
        "pt",
        "ru",
        "zh",
        "ja",
        "ko",
        "ar",
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
        "pl",
        "tr",
        "cs",
        "sk",
        "uk",
        "bg",
    ]

    def __init__(
        self,
        aeneas_path: str | None = None,
        device: str | None = None,
        gpu: bool = False,
    ):
        """
        Initialize Aeneas engine.

        Args:
            aeneas_path: Path to aeneas executable (auto-detect if None)
            device: Device parameter (not used, kept for protocol compatibility)
            gpu: GPU parameter (not used, kept for protocol compatibility)
        """
        super().__init__(device=device, gpu=gpu)

        self.aeneas_path = aeneas_path
        self.executable_path: str | None = None
        self.python_executable: str | None = None
        self._alignment_cache: OrderedDict[str, dict[str, Any]] = OrderedDict()
        self._cache_max_size = 100  # Maximum number of cached alignments
        self.enable_cache = True
        self.batch_size = 4
        self._temp_dir: str | None = None

    def _find_executable(self, name: str, custom_path: str | None = None) -> str | None:
        """Find executable in PATH or custom path."""
        if custom_path and os.path.isfile(custom_path) and os.access(custom_path, os.X_OK):
            return custom_path

        if custom_path and os.path.isdir(custom_path):
            candidate = os.path.join(custom_path, name)
            if os.path.isfile(candidate) and os.access(candidate, os.X_OK):
                return candidate

        found = shutil.which(name)
        if found:
            return found

        found = shutil.which(f"{name}.exe")
        if found:
            return found

        return None

    def _find_python_executable(self) -> str | None:
        """Find Python executable."""
        # Try python3 first, then python
        for py_name in ["python3", "python"]:
            py_path = shutil.which(py_name)
            if py_path:
                return py_path
        return None

    def initialize(self) -> bool:
        """
        Initialize the Aeneas engine.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            logger.info("Initializing Aeneas engine")

            # Find Python executable
            self.python_executable = self._find_python_executable()
            if not self.python_executable:
                logger.error("Python executable not found")
                self._initialized = False
                return False

            # Try to import aeneas as a Python module first
            try:
                import aeneas

                logger.info(f"Aeneas Python module found: {aeneas.__version__}")
                self._initialized = True
                return True
            except ImportError:
                logger.info("Aeneas Python module not found, will use command-line interface")

            # Find aeneas command-line tool
            self.executable_path = self._find_executable("aeneas", self.aeneas_path)
            if not self.executable_path:
                # Try python -m aeneas
                try:
                    result = subprocess.run(
                        [self.python_executable, "-m", "aeneas", "--version"],
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )
                    if result.returncode == 0:
                        logger.info("Aeneas found via python -m aeneas")
                        self.executable_path = f"{self.python_executable} -m aeneas"
                        self._initialized = True
                        return True
                except Exception:
                    ...

                logger.error("Aeneas not found. Install with: pip install aeneas")
                logger.error("Also requires FFmpeg: https://ffmpeg.org/download.html")
                self._initialized = False
                return False

            # Test executable
            try:
                if "python" in self.executable_path or " -m " in self.executable_path:
                    # Python module call
                    cmd = [*self.executable_path.split(), "--version"]
                else:
                    cmd = [self.executable_path, "--version"]

                result = subprocess.run(cmd, capture_output=True, text=True, timeout=5)

                if result.returncode == 0:
                    logger.info(f"Aeneas found: {self.executable_path}")
                    logger.info(f"Version: {result.stdout.strip()[:100]}")
                else:
                    logger.warning(f"Version check returned non-zero: {result.stderr}")
            except subprocess.TimeoutExpired:
                logger.warning("Version check timed out, but continuing")
            except Exception as e:
                logger.warning(f"Version check failed: {e}, but continuing")

            # Create reusable temp directory
            self._temp_dir = tempfile.mkdtemp(prefix="aeneas_")
            logger.debug(f"Created temp directory: {self._temp_dir}")

            self._initialized = True
            logger.info("Aeneas engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Aeneas engine: {e}")
            self._initialized = False
            return False

    def align(
        self,
        audio_path: str | Path,
        text: str,
        language: str = "en",
        output_path: str | Path | None = None,
        output_format: str = "srt",
        **kwargs: Any,
    ) -> dict[str, Any] | None:
        """
        Align audio with text and generate subtitles.

        Args:
            audio_path: Path to audio file
            text: Text to align with audio
            language: Language code (e.g., 'en', 'es', 'fr')
            output_path: Optional path to save alignment/subtitle file
            output_format: Output format ('srt', 'vtt', 'json', 'smil', 'ttml', 'audacity')
            **kwargs: Additional alignment parameters
                - sync_map: Sync map format ('auto', 'c', 'm', 's', 'w')
                - boundary_type: Boundary type ('auto', 's', 'w', 'p')
                - boundary_percent: Boundary percent (0-100, default 50)

        Returns:
            Dictionary with alignment data or None if failed
        """
        if not self._initialized and not self.initialize():
            return None

        try:
            audio_path = Path(audio_path)
            if not audio_path.exists():
                logger.error(f"Audio file not found: {audio_path}")
                return None

            # Check alignment cache (LRU)
            if self.enable_cache:
                # Create cache key from audio path hash and text
                audio_hash = hashlib.md5(
                    f"{audio_path}_{text}_{language}_{output_format}".encode()
                ).hexdigest()
                if audio_hash in self._alignment_cache:
                    logger.debug("Using cached Aeneas alignment result")
                    self._alignment_cache.move_to_end(audio_hash)  # LRU update
                    return dict(self._alignment_cache[audio_hash])

            # Validate output format
            if output_format not in self.SUPPORTED_FORMATS:
                logger.warning(f"Unsupported format {output_format}, using 'srt'")
                output_format = "srt"

            # Get alignment parameters
            sync_map = kwargs.get("sync_map", "auto")
            boundary_type = kwargs.get("boundary_type", "auto")
            boundary_percent = kwargs.get("boundary_percent", 50)

            # Create temporary text file (use reusable temp dir if available)
            temp_dir = self._temp_dir if self._temp_dir else None
            with tempfile.NamedTemporaryFile(
                mode="w", suffix=".txt", delete=False, encoding="utf-8", dir=temp_dir
            ) as tmp_text:
                tmp_text.write(text)
                tmp_text_path = tmp_text.name

            # Create temporary output file (use reusable temp dir if available)
            if output_path:
                output_file = Path(output_path)
            else:
                tmp_out = tempfile.NamedTemporaryFile(
                    suffix=f".{output_format}", delete=False, dir=temp_dir
                )
                tmp_out.close()
                output_file = Path(tmp_out.name)

            try:
                # Try Python module first
                try:
                    from aeneas.executetask import ExecuteTask
                    from aeneas.task import Task

                    # Create task
                    task = Task()
                    task.audio_file_path_absolute = str(audio_path.absolute())
                    task.text_file_path_absolute = tmp_text_path
                    task.sync_map_file_path_absolute = str(output_file.absolute())
                    task.config_language = language
                    task.config_string = f"task_language={language}|is_text_type=plain|os_task_file_format={output_format}"

                    # Execute task
                    executor = ExecuteTask(task)
                    result = executor.execute()

                    if result:
                        # Read alignment data
                        alignment_data = self._read_alignment_file(output_file, output_format)
                        logger.info(f"Alignment completed: {output_file}")

                        # Cache result if successful (LRU)
                        if self.enable_cache:
                            audio_hash = hashlib.md5(
                                f"{audio_path}_{text}_{language}_{output_format}".encode()
                            ).hexdigest()
                            # Manage cache size
                            if len(self._alignment_cache) >= self._cache_max_size:
                                oldest_key = next(iter(self._alignment_cache))
                                del self._alignment_cache[oldest_key]
                            self._alignment_cache[audio_hash] = alignment_data.copy()
                            self._alignment_cache.move_to_end(audio_hash)  # LRU update

                        return alignment_data
                    else:
                        logger.error("Aeneas alignment failed")
                        return None

                except ImportError:
                    # Use command-line interface
                    if self.executable_path is None and self.python_executable is None:
                        logger.error("No aeneas executable available")
                        return None

                    cmd: list[str]
                    if "python" in str(self.executable_path) or " -m " in str(self.executable_path):
                        if isinstance(self.executable_path, str) and " -m " in self.executable_path:
                            cmd = [
                                *self.executable_path.split(),
                                "sync",
                                str(audio_path.absolute()),
                                tmp_text_path,
                                str(output_file.absolute()),
                                f"--language={language}",
                                f"--sync-map={sync_map}",
                                f"--boundary-type={boundary_type}",
                                f"--boundary-percent={boundary_percent}",
                                f"--output-format={output_format}",
                            ]
                        elif self.python_executable is not None:
                            cmd = [
                                self.python_executable,
                                "-m",
                                "aeneas",
                                "sync",
                                str(audio_path.absolute()),
                                tmp_text_path,
                                str(output_file.absolute()),
                                f"--language={language}",
                                f"--sync-map={sync_map}",
                                f"--boundary-type={boundary_type}",
                                f"--boundary-percent={boundary_percent}",
                                f"--output-format={output_format}",
                            ]
                        else:
                            logger.error("No python executable available for aeneas")
                            return None
                    elif self.executable_path is not None:
                        cmd = [
                            self.executable_path,
                            "sync",
                            str(audio_path.absolute()),
                            tmp_text_path,
                            str(output_file.absolute()),
                            f"--language={language}",
                            f"--sync-map={sync_map}",
                            f"--boundary-type={boundary_type}",
                            f"--boundary-percent={boundary_percent}",
                            f"--output-format={output_format}",
                        ]
                    else:
                        logger.error("No aeneas executable configured")
                        return None

                    result = subprocess.run(
                        cmd,
                        capture_output=True,
                        text=True,
                        timeout=300,  # 5 minutes timeout
                    )

                    if result.returncode == 0:
                        alignment_data = self._read_alignment_file(output_file, output_format)
                        logger.info(f"Alignment completed: {output_file}")

                        # Cache result if successful (LRU)
                        if self.enable_cache:
                            audio_hash = hashlib.md5(
                                f"{audio_path}_{text}_{language}_{output_format}".encode()
                            ).hexdigest()
                            # Manage cache size
                            if len(self._alignment_cache) >= self._cache_max_size:
                                oldest_key = next(iter(self._alignment_cache))
                                del self._alignment_cache[oldest_key]
                            self._alignment_cache[audio_hash] = alignment_data.copy()
                            self._alignment_cache.move_to_end(audio_hash)  # LRU update

                        return alignment_data
                    else:
                        logger.error(f"Aeneas alignment failed: {result.stderr}")
                        return None

            finally:
                # Cleanup temporary text file
                try:
                    if os.path.exists(tmp_text_path):
                        os.unlink(tmp_text_path)
                except Exception as e:
                    logger.warning(f"Failed to cleanup temp file: {e}")

                # Cleanup output file if it was temporary
                if output_path is None:
                    try:
                        if os.path.exists(output_file):
                            os.unlink(output_file)
                    except Exception as e:
                        logger.warning(f"Failed to cleanup temp output: {e}")

        except Exception as e:
            logger.error(f"Aeneas alignment failed: {e}")
            return None

    def _read_alignment_file(self, file_path: Path, format: str) -> dict[str, Any]:
        """Read alignment data from file."""
        try:
            if format == "json":
                with open(file_path, encoding="utf-8") as f:
                    data: dict[str, Any] = json.load(f)
                    return data
            elif format == "srt":
                # Parse SRT format
                with open(file_path, encoding="utf-8") as f:
                    content = f.read()
                    return {"format": "srt", "content": content}
            elif format == "vtt":
                # Parse VTT format
                with open(file_path, encoding="utf-8") as f:
                    content = f.read()
                    return {"format": "vtt", "content": content}
            else:
                # Read as text
                with open(file_path, encoding="utf-8") as f:
                    content = f.read()
                    return {"format": format, "content": content}
        except Exception as e:
            logger.error(f"Failed to read alignment file: {e}")
            return {}

    def batch_align(
        self,
        alignments: list[tuple[str | Path, str, str, str | Path | None, str]],
        batch_size: int | None = None,
        **kwargs: Any,
    ) -> list[dict[str, Any] | None]:
        """
        Align multiple audio-text pairs in batch with parallel processing.

        Args:
            alignments: List of tuples (audio_path, text, language, output_path, output_format)
            batch_size: Number of parallel alignments (default: self.batch_size)
            **kwargs: Additional alignment parameters

        Returns:
            List of alignment results (None for failed alignments)
        """
        if not self._initialized and not self.initialize():
            return [None] * len(alignments)

        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        def align_single(
            args: tuple[str | Path, str, str, str | Path | None, str],
        ) -> dict[str, Any] | None:
            audio_path, text, language, output_path, output_format = args
            try:
                return self.align(
                    audio_path=audio_path,
                    text=text,
                    language=language,
                    output_path=output_path,
                    output_format=output_format,
                    **kwargs,
                )
            except Exception as e:
                logger.error(f"Batch alignment failed for {audio_path}: {e}")
                return None

        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            results = list(executor.map(align_single, alignments))

        return results

    def cleanup(self) -> None:
        """Clean up resources."""
        try:
            # Cleanup temp directory
            if self._temp_dir and os.path.exists(self._temp_dir):
                try:
                    shutil.rmtree(self._temp_dir)
                    logger.debug(f"Removed temp directory: {self._temp_dir}")
                except Exception as e:
                    logger.warning(f"Failed to remove temp directory: {e}")
                self._temp_dir = None

            # Clear alignment cache
            self._alignment_cache.clear()

            self._initialized = False
            logger.info("Aeneas engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during cleanup: {e}")

    def clear_cache(self) -> None:
        """Clear alignment cache."""
        self._alignment_cache.clear()
        logger.info("Alignment cache cleared")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "cache_size": len(self._alignment_cache),
            "max_cache_size": self._cache_max_size,
            "cache_enabled": self.enable_cache,
        }

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "executable_path": (str(self.executable_path) if self.executable_path else None),
                "python_executable": (
                    str(self.python_executable) if self.python_executable else None
                ),
                "supported_formats": self.SUPPORTED_FORMATS,
                "supported_languages": len(self.SUPPORTED_LANGUAGES),
            }
        )
        return info


def create_aeneas_engine(
    aeneas_path: str | None = None, device: str | None = None, gpu: bool = False
) -> AeneasEngine:
    """Factory function to create an Aeneas engine instance."""
    return AeneasEngine(aeneas_path=aeneas_path, device=device, gpu=gpu)
