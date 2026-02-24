"""
WhisperX Engine for VoiceStudio.
Speech-to-text with word-level timestamps and optional speaker diarization.

Requires: pip install whisperx (and optionally pyannote-audio for diarization).
Compatible with Python 3.10+.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

from .base import EngineProtocol

logger = logging.getLogger(__name__)

np: Any = None
HAS_NUMPY = False
try:
    import numpy

    np = numpy
    HAS_NUMPY = True
except ImportError:  # ALLOWED: bare except - optional numpy
    pass

try:
    import whisperx

    HAS_WHISPERX = True
except ImportError:
    HAS_WHISPERX = False
    whisperx = None
    logger.warning("whisperx not installed. Install with: pip install whisperx")


def _get_torch():
    try:
        import torch

        return torch
    except ImportError:
        return None


def _to_dict(obj: Any) -> dict[str, Any]:
    """Normalize WhisperX result (dict or typed object) to dict for .get() access."""
    if obj is None:
        return {}
    if isinstance(obj, dict):
        return obj
    return {
        "segments": getattr(obj, "segments", []),
        "language": getattr(obj, "language", "en"),
        "word_segments": getattr(obj, "word_segments", None),
    }


class WhisperXEngine(EngineProtocol):
    """
    WhisperX engine: speech-to-text with alignment and optional speaker diarization.

    Uses whisperx (faster-whisper + wav2vec2 alignment + pyannote diarization).
    Returns the same result shape as WhisperEngine; when diarization=True
    each segment may include a "speaker" field.
    """

    SUPPORTED_MODELS = ["tiny", "base", "small", "medium", "large-v2", "large-v3"]
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
        "el",
        "da",
        "no",
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
        "eu",
        "gl",
        "ha",
        "jv",
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
        batch_size: int = 16,
    ):
        super().__init__(device=device, gpu=gpu)
        if not HAS_WHISPERX:
            raise ImportError("whisperx not installed. Install with: pip install whisperx")
        if not HAS_NUMPY:
            raise ImportError("numpy is required for WhisperX. Install with: pip install numpy")
        self.model_name = model_name
        self.compute_type = compute_type
        torch = _get_torch()
        if torch and torch.cuda.is_available() and self.device == "cuda":
            self._wx_device = "cuda"
        else:
            self._wx_device = "cpu"
            if compute_type not in ("int8", "float32"):
                self.compute_type = "int8"
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._model: Any = None
        self._align_model = None
        self._align_metadata = None
        self._diarize_model = None

    def _load_model(self) -> bool:
        if self._model is not None:
            self._initialized = True
            return True
        logger.info("Loading WhisperX model: %s on %s", self.model_name, self._wx_device)
        self._model = whisperx.load_model(
            self.model_name,
            self._wx_device,
            compute_type=self.compute_type,
        )
        self._initialized = True
        logger.info("WhisperX model loaded")
        return True

    def initialize(self) -> bool:
        try:
            if self._initialized:
                return True
            if self.lazy_load:
                return True
            return self._load_model()
        except Exception as e:
            logger.error("Failed to initialize WhisperX: %s", e)
            self._initialized = False
            return False

    def transcribe(
        self,
        audio: str | Path | Any,
        language: str | None = None,
        task: str = "transcribe",
        word_timestamps: bool = False,
        diarization: bool = False,
        **kwargs: Any,
    ) -> dict[str, Any]:
        """
        Transcribe audio. When diarization=True, segments include a "speaker" field.
        """
        if not self._initialized and not self._load_model():
            raise RuntimeError("Failed to load WhisperX model")
        if isinstance(audio, (str, Path)):
            audio_path = Path(audio)
            if not audio_path.exists():
                raise FileNotFoundError(f"Audio file not found: {audio_path}")
            audio_np = whisperx.load_audio(str(audio_path))
        elif HAS_NUMPY and hasattr(audio, "shape"):
            audio_np = audio
        else:
            raise TypeError("audio must be a file path or numpy array")

        result = self._model.transcribe(audio_np, batch_size=self.batch_size)
        # Normalize: WhisperX may return dict or typed object
        result = _to_dict(result)
        if language is None and result.get("language"):
            language = result["language"]
        if language is None:
            language = "en"
        model_a, meta = whisperx.load_align_model(
            language_code=language,
            device=self._wx_device,
        )
        self._align_model = model_a
        self._align_metadata = meta
        aligned = whisperx.align(
            result.get("segments") or [],
            model_a,
            meta,
            audio_np,
            self._wx_device,
            return_char_alignments=False,
        )
        result = _to_dict(aligned)
        segments_list = result.get("segments") or []
        if diarization and self._wx_device == "cuda":
            try:
                diarize_model = whisperx.DiarizationPipeline(
                    use_auth_token=None,
                    device=self._wx_device,
                )
                result = whisperx.assign_word_speakers(diarize_model, result, audio_np)
                result = _to_dict(result)
                segments_list = result.get("segments") or []
            except Exception as e:
                logger.warning("Diarization skipped: %s", e)

        text_parts = []
        out_segments = []
        word_ts = []
        for seg in segments_list:
            t = seg.get("text", "").strip()
            start = float(seg.get("start", 0))
            end = float(seg.get("end", 0))
            text_parts.append(t)
            seg_dict = {"text": t, "start": start, "end": end}
            if seg.get("speaker") is not None:
                seg_dict["speaker"] = str(seg["speaker"])
            out_segments.append(seg_dict)
            if word_timestamps and "words" in seg:
                for w in seg["words"]:
                    word_ts.append(
                        {
                            "word": w.get("word", ""),
                            "start": float(w.get("start", start)),
                            "end": float(w.get("end", end)),
                            "probability": w.get("score"),
                        }
                    )
        full_text = " ".join(text_parts).strip()
        duration = sum(s["end"] - s["start"] for s in out_segments)
        return {
            "text": full_text,
            "language": language or "en",
            "language_probability": 1.0,
            "segments": out_segments,
            "duration": duration,
            "word_timestamps": word_ts if word_timestamps else [],
        }

    def get_supported_languages(self) -> list[str]:
        return list(self.SUPPORTED_LANGUAGES)

    def cleanup(self) -> None:
        self._model = None
        self._align_model = None
        self._align_metadata = None
        self._diarize_model = None
        self._initialized = False
        logger.info("WhisperX engine cleaned up")

    def get_info(self) -> dict[str, Any]:
        info = super().get_info()
        info.update(
            {
                "model_name": self.model_name,
                "compute_type": self.compute_type,
                "supported_models": self.SUPPORTED_MODELS,
            }
        )
        return info
