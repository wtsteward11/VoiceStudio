"""
One-shot Chatterbox synthesis in the family venv (torch26 / venv_advanced_tts).

Invoked by ChatterboxTorch26Engine via subprocess — not imported by the API worker.
"""

from __future__ import annotations

import json
import logging
import os
import sys
from pathlib import Path

logger = logging.getLogger(__name__)

_EMOTION_EXAGGERATION: dict[str, float] = {
    "happy": 0.8,
    "excited": 1.0,
    "sad": 0.3,
    "angry": 1.2,
    "calm": 0.2,
    "surprised": 0.9,
}


def _default_model_cache_dir() -> str:
    model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
    if not model_cache_dir:
        program_data = os.getenv("PROGRAMDATA", "C:\\ProgramData")
        model_cache_dir = os.path.join(
            program_data,
            "VoiceStudio",
            "models",
            "chatterbox",
        )
    os.makedirs(model_cache_dir, exist_ok=True)
    return model_cache_dir


def run_from_request_path(request_path: Path) -> None:
    """Load JSON request, synthesize, write WAV. Raises on failure."""
    raw = request_path.read_text(encoding="utf-8")
    data = json.loads(raw)

    text = data.get("text") or ""
    speaker_wav = data.get("speaker_wav")
    output_path = data.get("output_path")
    emotion = data.get("emotion")
    device = data.get("device") or "cpu"

    if not text.strip():
        raise ValueError("text is empty")
    if not speaker_wav:
        raise ValueError("speaker_wav is required")
    if not output_path:
        raise ValueError("output_path is required")
    if not Path(speaker_wav).is_file():
        raise FileNotFoundError(f"speaker_wav not found: {speaker_wav}")

    _default_model_cache_dir()

    import torch
    import torchaudio
    from chatterbox.tts import ChatterboxTTS

    tts = ChatterboxTTS.from_pretrained(device=device)

    exaggeration = 0.5
    if emotion and str(emotion).lower() != "neutral":
        exaggeration = _EMOTION_EXAGGERATION.get(str(emotion).lower(), 0.5)

    gen_kwargs: dict[str, object] = {
        "text": text,
        "audio_prompt_path": str(speaker_wav),
        "exaggeration": exaggeration,
    }
    for k in ("cfg_weight", "temperature", "repetition_penalty", "top_p"):
        if k in data and data[k] is not None:
            gen_kwargs[k] = data[k]

    sample_rate = int(getattr(tts, "sr", 24000))

    with torch.inference_mode():
        wav = tts.generate(**gen_kwargs)

    out_p = Path(output_path)
    out_p.parent.mkdir(parents=True, exist_ok=True)
    torchaudio.save(str(out_p), wav.cpu(), sample_rate)


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    if len(sys.argv) < 2:
        sys.stderr.write(
            "usage: python -m app.cli.chatterbox_worker_synthesize <request.json>\n"
        )
        return 2
    req = Path(sys.argv[1])
    if not req.is_file():
        sys.stderr.write(f"request file not found: {req}\n")
        return 2
    try:
        run_from_request_path(req)
    except Exception as e:
        logger.exception("chatterbox_worker_synthesize failed")
        sys.stderr.write(f"{type(e).__name__}: {e}\n")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
