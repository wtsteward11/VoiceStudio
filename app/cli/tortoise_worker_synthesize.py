"""
One-shot Tortoise TTS synthesis in the **venv_tortoise** interpreter (Slice 18B).

Invoked by TortoiseSubprocessEngine via subprocess — not imported by the API worker.
"""

from __future__ import annotations

import json
import logging
import os
import sys
import wave
from pathlib import Path

import numpy as np

logger = logging.getLogger(__name__)

QUALITY_PRESETS: dict[str, dict[str, object]] = {
    "ultra_fast": {
        "num_autoregressive_samples": 16,
        "diffusion_iterations": 30,
        "cond_free": False,
    },
    "fast": {
        "num_autoregressive_samples": 96,
        "diffusion_iterations": 80,
    },
    "standard": {
        "num_autoregressive_samples": 256,
        "diffusion_iterations": 200,
    },
    "high_quality": {
        "num_autoregressive_samples": 256,
        "diffusion_iterations": 400,
    },
}


def _default_models_dir() -> str:
    model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
    if not model_cache_dir:
        model_cache_dir = os.path.join(
            os.getenv("PROGRAMDATA", "C:\\ProgramData"),
            "VoiceStudio",
            "models",
            "tortoise",
        )
    os.makedirs(model_cache_dir, exist_ok=True)
    tortoise_models_dir = os.path.join(model_cache_dir, "tortoise_models")
    os.makedirs(tortoise_models_dir, exist_ok=True)
    return tortoise_models_dir


def _load_reference_wav_mono_22050(ref_path: str) -> torch.Tensor:
    """
    Load a reference WAV for Tortoise conditioning.

    Prefer ``torchaudio.load`` when it works. Newer ``torchaudio`` builds may require
    optional ``torchcodec`` for some backends; fall back to stdlib ``wave`` + resample
    so the isolated ``venv_tortoise`` stays self-contained.
    """
    import torch
    import torchaudio

    try:
        wav, sr = torchaudio.load(ref_path)
    except Exception:
        with wave.open(ref_path, "rb") as wf:
            n_channels = wf.getnchannels()
            sampwidth = wf.getsampwidth()
            framerate = wf.getframerate()
            raw = wf.readframes(wf.getnframes())
        if sampwidth == 2:
            x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
        elif sampwidth == 4:
            x = np.frombuffer(raw, dtype="<i4").astype(np.float32) / 2147483648.0
        else:
            raise RuntimeError(f"unsupported WAV sample width {sampwidth} for {ref_path}")
        if n_channels > 1:
            x = x.reshape(-1, n_channels).mean(axis=1)
        wav = torch.from_numpy(x).unsqueeze(0)
        sr = framerate
    if sr != 22050:
        wav = torchaudio.functional.resample(wav, sr, 22050)
    # Tortoise expects 2-D audio tensors [1, samples] per reference.
    if wav.dim() == 2 and wav.size(0) > 1:
        wav = wav.mean(dim=0, keepdim=True)
    elif wav.dim() == 1:
        wav = wav.unsqueeze(0)
    return wav


def _save_wav_float32_mono(path: str, audio: torch.Tensor, sample_rate: int) -> None:
    """Write float32 mono WAV; prefer torchaudio, fall back to stdlib ``wave``."""
    import torch
    import torchaudio

    t = audio.squeeze().detach().cpu()
    try:
        torchaudio.save(path, t.unsqueeze(0), sample_rate)
    except Exception:
        out_p = Path(path)
        out_p.parent.mkdir(parents=True, exist_ok=True)
        x = np.clip(t.numpy().astype(np.float64), -1.0, 1.0)
        pcm = (x * 32767.0).astype(np.int16)
        with wave.open(str(out_p), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(sample_rate)
            wf.writeframes(pcm.tobytes())


def run_from_request_path(request_path: Path) -> None:
    """Load JSON request, synthesize, write WAV. Raises on failure."""
    raw = request_path.read_text(encoding="utf-8")
    data = json.loads(raw)

    text = data.get("text") or ""
    speaker_wav = data.get("speaker_wav")
    voice_samples = data.get("voice_samples")
    output_path = data.get("output_path")
    quality_preset = data.get("quality_preset") or "high_quality"
    models_dir = data.get("models_dir") or _default_models_dir()
    device = data.get("device") or "cpu"

    if not text.strip():
        raise ValueError("text is empty")
    if not output_path:
        raise ValueError("output_path is required")

    if quality_preset not in QUALITY_PRESETS:
        quality_preset = "high_quality"

    if speaker_wav is None:
        raise ValueError("speaker_wav is required (reference audio path(s))")
    if isinstance(speaker_wav, (str, Path)):
        speaker_wav = [speaker_wav]
    speaker_wav = [str(p) for p in speaker_wav]
    voice_refs = voice_samples if voice_samples else speaker_wav
    if voice_refs:
        voice_refs = [str(p) for p in voice_refs]

    for ref in voice_refs:
        if not Path(ref).is_file():
            raise FileNotFoundError(f"reference audio not found: {ref}")

    import torch
    import torchaudio
    from tortoise.api import TextToSpeech

    tts = TextToSpeech(
        device=device,
        models_dir=str(models_dir),
        kv_cache=True,
    )

    quality_params = dict(QUALITY_PRESETS[quality_preset])
    sample_rate = 24000

    voice_tensors = []
    for ref_path in voice_refs:
        voice_tensors.append(_load_reference_wav_mono_22050(ref_path))

    with torch.inference_mode():
        if voice_tensors:
            audio = tts.tts(
                text,
                voice_samples=voice_tensors,
                **quality_params,
            )
        else:
            audio = tts.tts_with_preset(
                text,
                preset=quality_preset,
            )

    _save_wav_float32_mono(str(output_path), audio.squeeze(0), sample_rate)


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    if len(sys.argv) < 2:
        sys.stderr.write(
            "usage: python -m app.cli.tortoise_worker_synthesize <request.json>\n",
        )
        return 2
    req = Path(sys.argv[1])
    if not req.is_file():
        sys.stderr.write(f"request file not found: {req}\n")
        return 2
    try:
        run_from_request_path(req)
    except Exception as e:
        logger.exception("tortoise_worker_synthesize failed")
        sys.stderr.write(f"{type(e).__name__}: {e}\n")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
