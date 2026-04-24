"""
OpenVoice via **venv_openvoice** subprocess (Slice 19F / ADR-054).

The API worker does not import ``openvoice``; synthesis runs in the family interpreter
(see ``app/cli/openvoice_worker_synthesize.py``).
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

# Keep in sync with ``OpenVoiceEngine.SUPPORTED_LANGUAGES`` (avoid importing that module in API worker).
_OPENVOICE_SUPPORTED_LANGUAGES = [
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

_DEFAULT_SYNTH_TIMEOUT_SEC = float(
    os.environ.get("VOICESTUDIO_OPENVOICE_SUBPROCESS_TIMEOUT_SEC", "900"),
)


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _resolve_family_python_exe() -> Path:
    from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager

    mgr = get_venv_manager()
    fam = VenvFamily.OPENVOICE
    if not mgr.is_venv_created(fam):
        raise RuntimeError(
            "venv_openvoice (runtime/venvs/openvoice) is not created; "
            "run scripts/engines/create_engine_venv.py --family openvoice "
            "then install config/venv_families/requirements-openvoice.txt into that venv.",
        )
    return Path(mgr.get_python_executable(fam))


def _worker_environ(root: Path) -> dict[str, str]:
    env = os.environ.copy()
    rp = str(root)
    if env.get("PYTHONPATH"):
        env["PYTHONPATH"] = rp + os.pathsep + env["PYTHONPATH"]
    else:
        env["PYTHONPATH"] = rp
    # Windows: OpenVoice's BaseSpeakerTTS prints split sentences; default cp1252 fails on some text.
    env["PYTHONIOENCODING"] = "utf-8"
    hub = os.environ.get(
        "VOICESTUDIO_OPENVOICE_WORKER_HF_ENDPOINT",
        "https://huggingface.co",
    )
    env["HF_ENDPOINT"] = hub
    return env


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


class OpenVoiceSubprocessEngine(EngineProtocol):
    """OpenVoice: synthesis delegated to the isolated OpenVoice venv."""

    def __init__(
        self,
        base_speaker_model: str = "openvoice/base_speakers/EN",
        tone_color_converter_model: str = "openvoice/converter",
        device: str | None = None,
        gpu: bool = True,
        enable_style_control: bool = True,
        lazy_load: bool = True,
        batch_size: int = 2,
        enable_caching: bool = True,
    ) -> None:
        super().__init__(device=device, gpu=gpu)
        self.gpu = gpu
        self._family_python_exe = _resolve_family_python_exe()
        self.base_speaker_model = base_speaker_model
        self.tone_color_converter_model = tone_color_converter_model
        self.enable_style_control = enable_style_control
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._caching_enabled = enable_caching
        self._initialized = True
        self.sample_rate = 22050

    def initialize(self) -> bool:
        return True

    def cleanup(self) -> None:
        self._initialized = False
        logger.info("OpenVoiceSubprocessEngine cleanup (subprocess workers are ephemeral)")

    def get_supported_languages(self) -> list[str]:
        return list(_OPENVOICE_SUPPORTED_LANGUAGES)

    def _invoke_worker(self, payload: dict[str, Any]) -> subprocess.CompletedProcess[str] | None:
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
                "app.cli.openvoice_worker_synthesize",
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
                    "OpenVoice subprocess timed out after %s s",
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
        output_path: str | Path | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict[Any, Any]]:
        if not output_path:
            logger.error(
                "OpenVoiceSubprocessEngine requires output_path for subprocess synthesis",
            )
            return None

        ref = _first_reference_path(speaker_wav)
        if ref is None:
            return None

        eff_device = self.device or os.environ.get("VOICESTUDIO_OPENVOICE_DEVICE", "cpu")
        payload: dict[str, Any] = {
            "text": text,
            "speaker_wav": ref,
            "language": language,
            "output_path": str(output_path),
            "base_speaker_model": self.base_speaker_model,
            "tone_color_converter_model": self.tone_color_converter_model,
            "device": eff_device,
            "gpu": self.gpu,
            "enable_style_control": self.enable_style_control,
            "enhance_quality": enhance_quality,
            "calculate_quality": calculate_quality,
            "speed": kwargs.get("speed", 1.0),
        }

        proc = self._invoke_worker(payload)
        if proc is None:
            return None

        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()
            logger.error(
                "OpenVoice worker failed (exit %s): %s",
                proc.returncode,
                err[:2000],
            )
            return None

        out = Path(output_path)
        if not out.is_file() or out.stat().st_size < 64:
            logger.error("OpenVoice worker did not produce output at %s", output_path)
            return None

        if enhance_quality or calculate_quality:
            logger.warning(
                "enhance_quality/calculate_quality in subprocess mode not fully "
                "implemented; returning None with file written",
            )

        return None
