"""
whisper.cpp Engine for VoiceStudio
Fast C++ implementation of Whisper for speech-to-text

Compatible with:
- Python 3.10+
- whisper-cpp-python or direct whisper.cpp bindings
"""

from __future__ import annotations

import contextlib
import logging
import os
import subprocess
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from typing import Any

# Try importing general model cache
_model_cache = None
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
    logger.debug("General model cache not available, " "using Whisper CPP-specific cache")

# Fallback: Whisper CPP-specific cache (for backward compatibility)
_WHISPER_CPP_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(model_path: str, language: str | None) -> str:
    """Generate cache key for Whisper CPP model."""
    return f"whisper_cpp::{model_path}::{language or 'auto'}"


def _get_cached_whisper_cpp_model(model_path: str, language: str | None):
    """Get cached Whisper CPP model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cache_key_str = f"{model_path}_{language or 'auto'}"
        cached = _model_cache.get("whisper_cpp", cache_key_str)
        if cached is not None:
            return cached

    # Fallback to Whisper CPP-specific cache
    cache_key = _get_cache_key(model_path, language)
    if cache_key in _WHISPER_CPP_MODEL_CACHE:
        _WHISPER_CPP_MODEL_CACHE.move_to_end(cache_key)
        return _WHISPER_CPP_MODEL_CACHE[cache_key]
    return None


def _cache_whisper_cpp_model(model_path: str, language: str | None, ctx_data: dict):
    """Cache Whisper CPP model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            cache_key_str = f"{model_path}_{language or 'auto'}"
            _model_cache.set("whisper_cpp", cache_key_str, ctx_data)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Whisper CPP-specific cache
    cache_key = _get_cache_key(model_path, language)

    if cache_key in _WHISPER_CPP_MODEL_CACHE:
        _WHISPER_CPP_MODEL_CACHE.move_to_end(cache_key)
        return

    _WHISPER_CPP_MODEL_CACHE[cache_key] = ctx_data

    # Evict oldest if cache full
    if len(_WHISPER_CPP_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_ctx = _WHISPER_CPP_MODEL_CACHE.popitem(last=False)
        try:
            if HAS_WHISPER_CPP and oldest_ctx.get("ctx") is not None:
                with contextlib.suppress(Exception):
                    oldest_ctx["ctx"].free()
            del oldest_ctx
            logger.debug(f"Evicted Whisper CPP model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Whisper CPP model from cache: {e}")

    cache_size = len(_WHISPER_CPP_MODEL_CACHE)
    logger.debug(f"Cached Whisper CPP model: {cache_key} " f"(cache size: {cache_size})")


# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

# Required imports
try:
    import whisper_cpp

    HAS_WHISPER_CPP = True
except ImportError:
    HAS_WHISPER_CPP = False
    logger.warning(
        "whisper-cpp-python not installed. " "Install with: pip install whisper-cpp-python"
    )

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


class WhisperCPPEngine(EngineProtocol):
    """
    whisper.cpp Engine for fast speech-to-text transcription.

    Supports:
    - Fast speech-to-text transcription
    - Multiple language support
    - SRT/VTT subtitle generation
    - Real-time transcription
    - Low memory usage
    """

    def __init__(
        self,
        model_path: str | None = None,
        language: str | None = None,
        **kwargs,
    ):
        """
        Initialize whisper.cpp engine.

        Args:
            model_path: Path to whisper.cpp model file (.bin)
            language: Language code (e.g., 'en', 'zh', 'ja')
                or None for auto-detect
        """
        default_model_root = os.getenv("VOICESTUDIO_MODELS_PATH", "")
        if not default_model_root:
            default_model_root = os.path.join(
                os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                "VoiceStudio",
                "models",
            )
        default_model_path = os.path.join(default_model_root, "whisper", "whisper-medium.en.gguf")
        self.model_path = model_path or os.getenv("WHISPER_CPP_MODEL_PATH") or default_model_path

        # Validate model path early with clear error
        if self.model_path and not os.path.exists(self.model_path):
            logger.error(
                f"Whisper.cpp model path not found: {self.model_path}. "
                "Place whisper-medium.en.gguf under the models root "
                "or set WHISPER_CPP_MODEL_PATH."
            )
        self.language = language
        self._initialized = False
        self._model: dict[str, object] | None = None
        self._ctx: Any = None
        # LRU cache for transcription results
        self._transcription_cache: OrderedDict[str, dict[str, Any]] = OrderedDict()
        self._cache_max_size = 200  # Maximum number of cached transcriptions
        self.lazy_load = True
        self.batch_size = 4
        self._caching_enabled = True

    def initialize(self) -> bool:
        """Initialize whisper.cpp model."""
        try:
            if not HAS_WHISPER_CPP:
                # Try alternative: check if whisper.cpp binary is available
                if self._check_whisper_cpp_binary():
                    logger.info("Using whisper.cpp binary " "(Python bindings not available)")
                else:
                    logger.error("whisper-cpp-python not installed and binary not found")
                    return False

            if not HAS_NUMPY:
                logger.error("numpy is required for whisper.cpp")
                return False

            if not HAS_SOUNDFILE:
                logger.error("soundfile is required for whisper.cpp")
                return False

            # Check if model path exists
            if self.model_path and not os.path.exists(self.model_path):
                logger.warning(
                    f"Model path not found: {self.model_path}. "
                    f"whisper.cpp model may need to be downloaded."
                )

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
            else:
                # Load model immediately if lazy loading disabled
                if not self._load_model():
                    return False

            self._initialized = True
            logger.info("whisper.cpp engine initialized")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize whisper.cpp engine: {e}", exc_info=True)
            return False

    def cleanup(self) -> None:
        """Clean up whisper.cpp resources."""
        try:
            if self._ctx is not None:
                if HAS_WHISPER_CPP:
                    with contextlib.suppress(Exception):
                        self._ctx.free()
                self._ctx = None

            if self._model is not None:
                del self._model
                self._model = None

            self._initialized = False
            logger.info("whisper.cpp engine cleaned up")

        except Exception as e:
            logger.error(f"Error during whisper.cpp cleanup: {e}")

    def is_initialized(self) -> bool:
        """Check if engine is initialized."""
        return self._initialized

    def get_device(self) -> str:
        """Get the device being used (whisper.cpp uses CPU by default)."""
        return "cpu"

    def get_info(self) -> dict:
        """Get engine information."""
        return {
            "engine": "whisper_cpp",
            "name": "whisper.cpp",
            "version": "1.0.0",
            "device": "cpu",
            "initialized": self._initialized,
            "model_loaded": self._model is not None,
            "language": self.language,
            "has_whisper_cpp": HAS_WHISPER_CPP,
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
        Transcribe audio to text using whisper.cpp.

        Args:
            audio: Audio file path or audio data as bytes
            language: Language code (overrides instance language)
            output_format: Output format ('text', 'json', 'srt', 'vtt')
            **kwargs: Additional parameters

        Returns:
            Transcription result (format depends on output_format)
                or None on error
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
                logger.debug("Using cached whisper.cpp transcription result")
                self._transcription_cache.move_to_end(cache_key)  # LRU update
                cached_result = self._transcription_cache[cache_key]
                if output_format == "text":
                    return str(cached_result.get("text", ""))
                elif output_format == "json":
                    return dict(cached_result)
                elif output_format == "srt":
                    return self._format_srt(cached_result)
                elif output_format == "vtt":
                    return self._format_vtt(cached_result)
                return str(cached_result.get("text", ""))

            # Lazy load model if needed
            if self._model is None and not self._load_model():
                return None

            # Process audio
            audio_data, sample_rate = self._load_audio(audio)
            if audio_data is None:
                return None

            # Perform transcription
            result = self._perform_transcription(
                audio_data, sample_rate, language or self.language, **kwargs
            )

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

            # Format output
            if output_format == "text":
                return str(result.get("text", ""))
            elif output_format == "json":
                return dict(result)
            elif output_format == "srt":
                return self._format_srt(result)
            elif output_format == "vtt":
                return self._format_vtt(result)
            else:
                logger.warning(f"Unknown output format: {output_format}, returning text")
                return str(result.get("text", ""))

        except Exception as e:
            logger.error(f"whisper.cpp transcription failed: {e}", exc_info=True)
            return None

    def _check_whisper_cpp_binary(self) -> bool:
        """Check if whisper.cpp binary is available."""
        binary_path = self._find_whisper_cpp_binary()
        if binary_path:
            try:
                result = subprocess.run(
                    [binary_path, "--help"],
                    capture_output=True,
                    timeout=5,
                )
                return result.returncode == 0
            except (FileNotFoundError, subprocess.TimeoutExpired, OSError):
                return False
        return False

    def _load_model(self) -> bool:
        """Load whisper.cpp model with caching support."""
        try:
            if not self.model_path:
                logger.warning("Model path not set. whisper.cpp requires model files.")
                return True

            # Check cache first
            if self._caching_enabled:
                cached_ctx = _get_cached_whisper_cpp_model(
                    self.model_path,
                    self.language,
                )
                if cached_ctx is not None:
                    logger.debug(f"Using cached Whisper CPP model: {self.model_path}")
                    self._ctx = cached_ctx.get("ctx")
                    self._model = cached_ctx.get("model")
                    return True

            if not os.path.exists(self.model_path):
                logger.error(
                    f"Model path not found: {self.model_path}. "
                    f"Run model preflight or download the GGUF manually."
                )
                return False

            logger.info(f"Loading whisper.cpp model from {self.model_path}")

            if HAS_WHISPER_CPP:
                # Use Python bindings
                self._ctx = whisper_cpp.Whisper.from_file(self.model_path)
                self._model = {"loaded": True, "path": self.model_path}

                # Cache model
                if self._caching_enabled:
                    _cache_whisper_cpp_model(
                        self.model_path,
                        self.language,
                        {"ctx": self._ctx, "model": self._model},
                    )
            else:
                # Would use binary if available
                self._model = {
                    "loaded": True,
                    "path": self.model_path,
                    "method": "binary",
                }

                # Cache model info even for binary mode
                if self._caching_enabled:
                    _cache_whisper_cpp_model(
                        self.model_path,
                        self.language,
                        {"ctx": None, "model": self._model},
                    )

            logger.info("whisper.cpp model loaded successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to load whisper.cpp model: {e}", exc_info=True)
            return False

    def _load_audio(self, audio: str | bytes) -> tuple[np.ndarray | None, int]:
        """Load audio file or bytes."""
        try:
            if isinstance(audio, str):
                if os.path.exists(audio):
                    data, sr = sf.read(audio)
                    return data, sr
                else:
                    logger.error(f"Audio file not found: {audio}")
                    return None, 0
            elif isinstance(audio, bytes):
                import io

                data, sr = sf.read(io.BytesIO(audio))
                return data, sr
            else:
                logger.error("Invalid audio type")
                return None, 0

        except Exception as e:
            logger.error(f"Failed to load audio: {e}", exc_info=True)
            return None, 0

    def _perform_transcription(
        self,
        audio_data: np.ndarray,
        sample_rate: int,
        language: str | None,
        **kwargs,
    ) -> dict | None:
        """Perform actual transcription using whisper.cpp."""
        try:
            if not HAS_NUMPY:
                return None

            # Convert to mono if stereo
            if len(audio_data.shape) > 1:
                audio_data = np.mean(audio_data, axis=1)

            # Resample to 16kHz if needed (whisper.cpp expects 16kHz)
            if sample_rate != 16000:
                from scipy import signal

                num_samples = int(len(audio_data) * 16000 / sample_rate)
                audio_data = signal.resample(audio_data, num_samples)
                sample_rate = 16000

            if HAS_WHISPER_CPP and self._ctx is not None:
                # Use Python bindings
                result = self._ctx.transcribe(audio_data, language=language)
                return {
                    "text": result.get("text", ""),
                    "segments": result.get("segments", []),
                    "language": result.get("language", language or "unknown"),
                }
            else:
                # Fallback: try to use whisper.cpp binary if available
                if self._check_whisper_cpp_binary():
                    try:
                        # Save audio to temp file
                        import tempfile

                        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_file:
                            sf.write(tmp_file.name, audio_data, sample_rate)
                            tmp_path = tmp_file.name

                        # Call whisper.cpp binary
                        binary_path = self._find_whisper_cpp_binary()
                        if binary_path:
                            model_file = self.model_path or "models/ggml-base.bin"

                            # whisper.cpp binary command format:
                            # main -m <model> -f <file> [--language <lang>] [--output-json]
                            cmd = [binary_path, "-m", model_file, "-f", tmp_path]

                            # Add language if specified
                            if language:
                                cmd.extend(["--language", language])

                            # Request JSON output for better parsing.
                            # Newer whisper.cpp uses `whisper-cli` where `--output-json` is a flag
                            # (boolean) and the output path is set via `--output-file` (base name).
                            # Older binaries accepted `--output-json <path>`.
                            base_no_ext = os.path.splitext(tmp_path)[0]
                            json_output_path = base_no_ext + ".json"

                            binary_base = os.path.basename(binary_path).lower()
                            if "whisper-cli" in binary_base:
                                # -oj writes JSON, -of sets base output path (without extension)
                                cmd.extend(["-oj", "-of", base_no_ext])
                            else:
                                # Legacy behavior: attempt `--output-json <path>`
                                cmd.extend(["--output-json", json_output_path])

                            result = subprocess.run(
                                cmd,
                                capture_output=True,
                                text=True,
                                timeout=300,
                            )

                            # Parse output
                            if result.returncode == 0:
                                # Try to read JSON output first
                                text_output = ""
                                segments = []

                                try:
                                    if os.path.exists(json_output_path):
                                        import json

                                        with open(json_output_path, encoding="utf-8") as f:
                                            json_result = json.load(f)
                                            # Handle multiple whisper.cpp JSON formats:
                                            # - Legacy: {"text": "...", "segments": [...]}
                                            # - New whisper-cli: {"result": {"language": ...}, "transcription": [...]}
                                            if isinstance(json_result, dict) and isinstance(
                                                json_result.get("transcription"), list
                                            ):
                                                segs = []
                                                texts = []
                                                for seg in json_result.get("transcription", []):
                                                    if not isinstance(seg, dict):
                                                        continue
                                                    seg_text = str(seg.get("text", "")).strip()
                                                    if seg_text:
                                                        texts.append(seg_text)
                                                    offsets = seg.get("offsets") or {}
                                                    try:
                                                        start_ms = float(
                                                            offsets.get("from", 0) or 0
                                                        )
                                                        end_ms = float(offsets.get("to", 0) or 0)
                                                        start_s = start_ms / 1000.0
                                                        end_s = end_ms / 1000.0
                                                    except Exception:
                                                        start_s = 0.0
                                                        end_s = 0.0
                                                    segs.append(
                                                        {
                                                            "start": start_s,
                                                            "end": end_s,
                                                            "text": seg_text,
                                                        }
                                                    )
                                                text_output = " ".join(texts).strip()
                                                segments = segs
                                            else:
                                                text_output = str(
                                                    json_result.get("text", "")
                                                ).strip()
                                                segments = json_result.get("segments", [])
                                        os.unlink(json_output_path)
                                except Exception as e:
                                    logger.debug(f"Failed to parse JSON output: {e}")
                                    # Fallback to text output
                                    text_output = result.stdout.strip()
                                    if not text_output:
                                        # Sometimes output goes to stderr
                                        text_output = result.stderr.strip()
                                    segments = self._parse_segments_from_text(text_output)

                                # Clean up temp file
                                with contextlib.suppress(Exception):
                                    os.unlink(tmp_path)

                                return {
                                    "text": text_output,
                                    "segments": segments,
                                    "language": language or "unknown",
                                }
                            else:
                                logger.warning(
                                    f"whisper.cpp binary failed (exit code "
                                    f"{result.returncode}): {result.stderr}"
                                )
                                # Clean up JSON output file if it exists
                                json_output_path = tmp_path + ".json"
                                try:
                                    if os.path.exists(json_output_path):
                                        os.unlink(json_output_path)
                                except Exception:
                                    ...
                        # Clean up temp file
                        with contextlib.suppress(Exception):
                            os.unlink(tmp_path)
                    except Exception as e:
                        logger.warning(f"Binary transcription failed: {e}")
                        try:
                            if "tmp_path" in locals():
                                os.unlink(tmp_path)
                        except Exception:
                            ...

                # Try to use faster-whisper as fallback
                try:
                    from .whisper_engine import WhisperEngine

                    whisper_engine = WhisperEngine()
                    if whisper_engine.initialize():
                        whisper_result = whisper_engine.transcribe(audio_data, language=language)
                        if whisper_result and isinstance(whisper_result, dict):
                            return {
                                "text": str(whisper_result.get("text", "")),
                                "segments": list(whisper_result.get("segments", [])),
                                "language": str(
                                    whisper_result.get(
                                        "language",
                                        language or "unknown",
                                    )
                                ),
                            }
                except Exception as e:
                    logger.debug(f"Whisper fallback failed: {e}")

                # Last resort: return transcription attempt with duration info
                duration = len(audio_data) / sample_rate if sample_rate > 0 else 0
                return {
                    "text": "",
                    "segments": [],
                    "language": language or "unknown",
                    "duration": duration,
                }

        except Exception as e:
            logger.error(f"Transcription failed: {e}", exc_info=True)
            return None

    def _parse_segments_from_text(self, text: str) -> list[dict]:
        """Parse segments from plain text transcription."""
        # Simple segmentation: split by sentences
        import re

        sentences = re.split(r"[.!?]+", text)
        segments = []
        current_time = 0.0

        for sentence in sentences:
            sentence = sentence.strip()
            if sentence:
                # Estimate duration (rough: 0.1s per character)
                duration = len(sentence) * 0.1
                segments.append(
                    {
                        "start": current_time,
                        "end": current_time + duration,
                        "text": sentence,
                    }
                )
                current_time += duration

        return segments

    def _find_whisper_cpp_binary(self) -> str | None:
        """Find whisper.cpp binary in common locations."""
        import platform
        import shutil

        # Prefer the new upstream CLI binary name (whisper-cli). "main" is deprecated
        # in newer releases and may return a non-zero exit code even for --help.
        binary_names = ["whisper-cli", "whisper-cpp", "whisper", "main"]

        # On Windows, check for .exe versions
        if platform.system() == "Windows":
            binary_names.extend(["whisper-cli.exe", "whisper-cpp.exe", "whisper.exe", "main.exe"])

        # Check common installation locations
        common_paths = []

        if platform.system() == "Windows":
            common_paths.extend(
                [
                    os.path.join(
                        os.getenv("PROGRAMFILES", "C:\\Program Files"),
                        "whisper.cpp",
                        "main.exe",
                    ),
                    os.path.join(
                        os.getenv("PROGRAMFILES(X86)", "C:\\Program Files (x86)"),
                        "whisper.cpp",
                        "main.exe",
                    ),
                    os.path.join(
                        os.getenv("LOCALAPPDATA", ""),
                        "whisper.cpp",
                        "main.exe",
                    ),
                    # Upstream prebuilt zip layout (contains Release\\whisper-cli.exe and Release\\main.exe)
                    os.path.join(os.getcwd(), "whisper.cpp", "Release", "whisper-cli.exe"),
                    os.path.join(os.getcwd(), "whisper.cpp", "main.exe"),
                    # Common when using the upstream prebuilt Windows zip (contains Release\\main.exe)
                    os.path.join(os.getcwd(), "whisper.cpp", "Release", "main.exe"),
                    os.path.join(os.getcwd(), "main.exe"),
                ]
            )
        else:
            # Unix-like systems
            common_paths.extend(
                [
                    os.path.expanduser("~/whisper.cpp/main"),
                    os.path.expanduser("~/.local/bin/whisper-cpp"),
                    "/usr/local/bin/whisper-cpp",
                    "/usr/bin/whisper-cpp",
                    os.path.join(os.getcwd(), "whisper.cpp", "main"),
                    os.path.join(os.getcwd(), "main"),
                ]
            )

        for path in common_paths:
            if path and os.path.exists(path) and os.access(path, os.X_OK):
                return path

        # Fallback: Check PATH last. This avoids picking up unrelated `whisper.exe`
        # launchers from other packages when a repo-local whisper.cpp binary exists.
        for name in binary_names:
            binary_path = shutil.which(name)
            if binary_path and os.path.exists(binary_path):
                return binary_path

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
            text = segment.get("text", "")

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
            text = segment.get("text", "")

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

    def batch_transcribe(
        self,
        audio_list: list[str | bytes],
        language: str | None = None,
        output_format: str = "text",
        batch_size: int | None = None,
        **kwargs,
    ) -> list[str | dict | None]:
        """
        Transcribe multiple audio files in batch with optimized processing.

        Args:
            audio_list: List of audio file paths or audio data as bytes
            language: Language code (overrides instance language)
            output_format: Output format ('text', 'json', 'srt', 'vtt')
            batch_size: Number of items to process in parallel
                (default: self.batch_size)
            **kwargs: Additional parameters

        Returns:
            List of transcription results (format depends on output_format)
                or None on error
        """
        # Lazy load model if needed
        if not self._initialized and not self.initialize():
            return [None] * len(audio_list)

        if self._model is None and not self._load_model():
            return [None] * len(audio_list)

        # Use configured batch size if not specified
        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        # Process in parallel batches for better performance
        def transcribe_single(audio):
            try:
                return self.transcribe(
                    audio=audio,
                    language=language,
                    output_format=output_format,
                    **kwargs,
                )
            except Exception as e:
                logger.error(f"Batch transcription failed for audio: {e}")
                return None

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            results = list(executor.map(transcribe_single, audio_list))

        return results

    def set_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable caching."""
        self._caching_enabled = enable
        status = "enabled" if enable else "disabled"
        logger.info(f"Model caching {status}")

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def clear_transcription_cache(self):
        """Clear transcription cache."""
        self._transcription_cache.clear()
        logger.info("Transcription cache cleared")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "transcription_cache_size": len(self._transcription_cache),
            "max_transcription_cache_size": self._cache_max_size,
            "cache_enabled": self._caching_enabled,
        }


def create_whisper_cpp_engine(**kwargs) -> WhisperCPPEngine:
    """Factory function to create whisper.cpp engine."""
    return WhisperCPPEngine(**kwargs)
