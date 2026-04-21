"""
Tortoise TTS via **venv_tortoise** subprocess (Slice 18B).

The API worker does not import ``tortoise``; synthesis runs in the family interpreter
(see ``app/cli/tortoise_worker_synthesize.py``).
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

# CPU Tortoise can exceed 15 minutes per job (model load + inference); live proofs align with
# integration tests (``real_tortoise``). Override via VOICESTUDIO_TORTOISE_SUBPROCESS_TIMEOUT_SEC.
_DEFAULT_SYNTH_TIMEOUT_SEC = float(
    os.environ.get("VOICESTUDIO_TORTOISE_SUBPROCESS_TIMEOUT_SEC", "2400"),
)


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _resolve_family_python_exe() -> Path:
    from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager

    mgr = get_venv_manager()
    fam = VenvFamily.TORTOISE
    if not mgr.is_venv_created(fam):
        raise RuntimeError(
            "venv_tortoise (runtime/venvs/tortoise) is not created; "
            "run scripts/engines/create_engine_venv.py --family tortoise "
            "then install tortoise-tts into that venv.",
        )
    return Path(mgr.get_python_executable(fam))


def _worker_environ(root: Path) -> dict[str, str]:
    env = os.environ.copy()
    rp = str(root)
    if env.get("PYTHONPATH"):
        env["PYTHONPATH"] = rp + os.pathsep + env["PYTHONPATH"]
    else:
        env["PYTHONPATH"] = rp
    # Tortoise loads HF-hosted checkpoints (e.g. wav2vec) inside venv_tortoise. The Hugging Face
    # *router* endpoint can return 404 for direct model repo paths; canonical hub matches Slice 17
    # Chatterbox worker behavior (see ensure_chatterbox / dev_server scripts).
    hub = "https://huggingface.co"
    for key in ("HF_ENDPOINT", "HF_INFERENCE_API_BASE"):
        val = (env.get(key) or "").strip().lower()
        if val and "router.huggingface.co" in val:
            env[key] = hub
    return env


def _models_dir_for_payload() -> str:
    model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
    if not model_cache_dir:
        model_cache_dir = os.path.join(
            os.getenv("PROGRAMDATA", "C:\\ProgramData"),
            "VoiceStudio",
            "models",
            "tortoise",
        )
    return str(Path(model_cache_dir) / "tortoise_models")


def _read_output_wav_mono_float32(path: Path) -> np.ndarray:
    """Load mono float32 samples from the worker-written WAV (matches ``tortoise_worker_synthesize`` SR)."""
    try:
        import soundfile as sf

        audio, _sr = sf.read(str(path), always_2d=False)
        if audio.ndim > 1:
            audio = np.mean(audio, axis=1)
        return audio.astype(np.float32, copy=False)
    except Exception as sf_err:
        import wave

        logger.debug("soundfile read failed (%s), trying stdlib wave: %s", path, sf_err)
        with wave.open(str(path), "rb") as wf:
            n_frames = wf.getnframes()
            n_channels = wf.getnchannels()
            sampwidth = wf.getsampwidth()
            raw = wf.readframes(n_frames)
        if sampwidth != 2:
            msg = f"unsupported WAV sample width {sampwidth} for {path}"
            raise RuntimeError(msg) from sf_err
        x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
        if n_channels > 1:
            x = x.reshape(-1, n_channels).mean(axis=1)
        return x


class TortoiseSubprocessEngine(EngineProtocol):
    """Tortoise TTS: synthesis delegated to the isolated Tortoise venv."""

    def __init__(
        self,
        device: str | None = None,
        gpu: bool = True,
        quality_preset: str = "high_quality",
        lazy_load: bool = True,
        batch_size: int = 2,
        enable_caching: bool = True,
    ) -> None:
        super().__init__(device=device, gpu=gpu)
        self._family_python_exe = _resolve_family_python_exe()
        self.quality_preset = quality_preset
        self.lazy_load = lazy_load
        self.batch_size = batch_size
        self._caching_enabled = enable_caching
        self._initialized = True
        # Worker ``_save_wav_float32_mono`` uses 24 kHz (see ``tortoise_worker_synthesize``).
        self.sample_rate = 24000

    def initialize(self) -> bool:
        return True

    def cleanup(self) -> None:
        self._initialized = False
        logger.info("TortoiseSubprocessEngine cleanup (subprocess workers are ephemeral)")

    def synthesize(
        self,
        text: str,
        speaker_wav: str | Path | list[str | Path],
        voice_samples: list[str | Path] | None = None,
        output_path: str | Path | None = None,
        quality_preset: str | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> np.ndarray | None | tuple[np.ndarray | None, dict[Any, Any]]:
        if not output_path:
            logger.error(
                "TortoiseSubprocessEngine requires output_path for subprocess synthesis",
            )
            return None

        preset = quality_preset or self.quality_preset
        root = _repo_root()
        env = _worker_environ(root)

        if speaker_wav is None:
            logger.error(
                "TortoiseSubprocessEngine requires speaker_wav (reference audio path(s))",
            )
            return None
        if isinstance(speaker_wav, (str, Path)):
            speaker_wav = [speaker_wav]
        try:
            speaker_paths = list(speaker_wav)
        except TypeError:
            logger.error("speaker_wav must be str, Path, or iterable of paths")
            return None
        if not speaker_paths:
            logger.error("TortoiseSubprocessEngine requires non-empty speaker_wav")
            return None

        # Subprocess runs in venv_tortoise; its torch/CUDA stack may differ from the API worker.
        # Default CPU for deterministic live proofs (override: VOICESTUDIO_TORTOISE_DEVICE=cuda).
        eff_device = os.environ.get("VOICESTUDIO_TORTOISE_DEVICE", "cpu")
        payload: dict[str, Any] = {
            "text": text,
            "speaker_wav": [str(p) for p in speaker_paths],
            "output_path": str(output_path),
            "quality_preset": preset,
            "device": eff_device,
            "models_dir": _models_dir_for_payload(),
        }
        if voice_samples:
            payload["voice_samples"] = [str(p) for p in voice_samples]

        tmp_path: str | None = None
        proc: subprocess.CompletedProcess[str] | None = None
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
                "app.cli.tortoise_worker_synthesize",
                tmp_path,
            ]
            try:
                proc = subprocess.run(
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
                    "Tortoise subprocess timed out after %s s",
                    _DEFAULT_SYNTH_TIMEOUT_SEC,
                )
                return None
        finally:
            if tmp_path:
                try:
                    os.unlink(tmp_path)
                except OSError as e:
                    logger.debug("Could not delete temp request json: %s", e)

        if proc is None:
            return None

        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip()
            logger.error(
                "Tortoise worker failed (exit %s): %s",
                proc.returncode,
                err[:2000],
            )
            return None

        out = Path(output_path)
        if not out.is_file() or out.stat().st_size < 64:
            logger.error("Tortoise worker did not produce output at %s", output_path)
            return None

        if enhance_quality or calculate_quality:
            logger.warning(
                "enhance_quality/calculate_quality in Tortoise subprocess mode not implemented; "
                "returning file-backed audio only",
            )

        try:
            return _read_output_wav_mono_float32(out)
        except Exception as read_err:
            logger.error("Failed to read Tortoise output WAV %s: %s", output_path, read_err)
            return None
