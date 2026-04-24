"""
Chatterbox TTS via **venv_advanced_tts** (torch26) subprocess.

The API worker does not import ``chatterbox``; synthesis runs in the family interpreter
(see ``app/cli/chatterbox_worker_synthesize.py``).
"""

from __future__ import annotations

import json
import logging
import os
import subprocess
import tempfile
from pathlib import Path
from typing import Any

import numpy as np

from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Mirror chatterbox_engine for UI / diagnostics
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
    "zh-cn",
    "ja",
    "ko",
    "hi",
    "sv",
    "da",
    "no",
    "fi",
    "el",
    "hu",
    "ro",
]

SUPPORTED_EMOTIONS = [
    "neutral",
    "happy",
    "sad",
    "angry",
    "excited",
    "calm",
    "fearful",
    "disgusted",
    "surprised",
]

_DEFAULT_SYNTH_TIMEOUT_SEC = float(
    os.environ.get("VOICESTUDIO_CHATTERBOX_SUBPROCESS_TIMEOUT_SEC", "900")
)

_OPTIONAL_SYNTH_KEYS = (
    "cfg_weight",
    "temperature",
    "repetition_penalty",
    "top_p",
    "speed",
    "pitch",
)


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _resolve_family_python_exe() -> Path:
    from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager

    mgr = get_venv_manager()
    fam = VenvFamily.ADVANCED_TTS
    if not mgr.is_venv_created(fam):
        raise RuntimeError(
            "venv_advanced_tts (runtime/venvs/torch26) is not created; "
            "run scripts/engines/create_engine_venv.py for the Advanced TTS family, "
            "then install chatterbox-tts into that venv."
        )
    return Path(mgr.get_python_executable(fam))


def _normalize_language(language: str) -> str:
    lang = (language or "en").lower()
    if lang not in SUPPORTED_LANGUAGES:
        logger.warning("Language %s not in supported list, using 'en'", language)
        return "en"
    return lang


def _normalize_emotion(emotion: str | None) -> str | None:
    if emotion and emotion not in SUPPORTED_EMOTIONS:
        logger.warning("Emotion %s not in supported list, using 'neutral'", emotion)
        return None
    return emotion


def _first_reference_path(speaker_wav: str | Path | list[str | Path]) -> str | None:
    if isinstance(speaker_wav, (str, Path)):
        speaker_wav = [speaker_wav]
    paths = [str(p) for p in speaker_wav]
    if not paths:
        logger.error("speaker_wav is empty")
        return None
    ref = paths[0]
    if not Path(ref).is_file():
        logger.error("Reference audio not found: %s", ref)
        return None
    return ref


def _worker_environ(root: Path) -> dict[str, str]:
    """Build env for the torch26 worker subprocess.

    Always set ``HF_ENDPOINT`` to the canonical Hub URL so ``huggingface_hub`` does not
    default to ``router.huggingface.co`` (404 on model resolves) — same rule as
    ``ensure_chatterbox`` / Chatterbox HF preflight subprocess.
    Optional override: ``VOICESTUDIO_CHATTERBOX_WORKER_HF_ENDPOINT``.
    """
    env = os.environ.copy()
    rp = str(root)
    if env.get("PYTHONPATH"):
        env["PYTHONPATH"] = rp + os.pathsep + env["PYTHONPATH"]
    else:
        env["PYTHONPATH"] = rp
    hub = os.environ.get(
        "VOICESTUDIO_CHATTERBOX_WORKER_HF_ENDPOINT",
        "https://huggingface.co",
    )
    env["HF_ENDPOINT"] = hub
    return env


class ChatterboxTorch26Engine(EngineProtocol):
    """
    Chatterbox TTS: synthesis delegated to the Advanced TTS family venv (Model B).
    """

    def __init__(
        self,
        model_name: str = "chatterbox-tts/base",
        device: str | None = None,
        gpu: bool = True,
        lazy_load: bool = True,
        batch_size: int = 4,
        enable_caching: bool = True,
    ) -> None:
        super().__init__(device=device, gpu=gpu)
        self._family_python_exe = _resolve_family_python_exe()
        self.model_name = model_name
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._caching_enabled = enable_caching
        self._initialized = True

    def initialize(self) -> bool:
        return True

    def cleanup(self) -> None:
        self._initialized = False
        logger.info("ChatterboxTorch26Engine cleanup (subprocess workers are ephemeral)")

    def get_supported_languages(self) -> list[str]:
        return list(SUPPORTED_LANGUAGES)

    def get_supported_emotions(self) -> list[str]:
        return list(SUPPORTED_EMOTIONS)

    def _build_payload(
        self,
        text: str,
        ref: str,
        lang: str,
        emotion: str | None,
        output_path: str | Path,
        kwargs: dict[str, Any],
    ) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "text": text,
            "speaker_wav": ref,
            "language": lang,
            "emotion": emotion,
            "output_path": str(output_path),
            "device": self.device,
            "model_name": self.model_name,
        }
        for k in _OPTIONAL_SYNTH_KEYS:
            if k in kwargs and kwargs[k] is not None:
                payload[k] = kwargs[k]
        return payload

    def _invoke_worker(self, payload: dict[str, Any]) -> subprocess.CompletedProcess | None:
        root = _repo_root()
        env = _worker_environ(root)
        tmp_path: str | None = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="w",
                suffix=".json",
                delete=False,
                encoding="utf-8",
            ) as tmp:
                json.dump(payload, tmp)
                tmp_path = tmp.name
            cmd = [
                str(self._family_python_exe),
                "-m",
                "app.cli.chatterbox_worker_synthesize",
                tmp_path,
            ]
            try:
                return subprocess.run(
                    cmd,
                    cwd=str(root),
                    env=env,
                    capture_output=True,
                    text=True,
                    timeout=_DEFAULT_SYNTH_TIMEOUT_SEC,
                    check=False,
                )
            except subprocess.TimeoutExpired:
                logger.error(
                    "Chatterbox subprocess timed out after %s s",
                    _DEFAULT_SYNTH_TIMEOUT_SEC,
                )
                return None
        finally:
            if tmp_path:
                try:
                    os.unlink(tmp_path)
                except OSError as e:
                    logger.debug("Could not delete temp request json: %s", e)

    def synthesize(
        self,
        text: str,
        speaker_wav: str | Path | list[str | Path],
        language: str = "en",
        emotion: str | None = None,
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict[Any, Any]]:
        if not output_path:
            logger.error(
                "ChatterboxTorch26Engine requires output_path for subprocess synthesis",
            )
            return None

        ref = _first_reference_path(speaker_wav)
        if ref is None:
            return None

        lang = _normalize_language(language)
        emotion = _normalize_emotion(emotion)
        payload = self._build_payload(text, ref, lang, emotion, output_path, kwargs)

        proc = self._invoke_worker(payload)
        if proc is None:
            return None

        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()
            logger.error(
                "Chatterbox worker failed (exit %s): %s",
                proc.returncode,
                err[:2000],
            )
            return None

        out = Path(output_path)
        if not out.is_file() or out.stat().st_size < 64:
            logger.error("Chatterbox worker did not produce output at %s", output_path)
            return None

        if enhance_quality or calculate_quality:
            logger.warning(
                "enhance_quality/calculate_quality in subprocess mode not fully "
                "implemented; returning None with file written",
            )

        return None
