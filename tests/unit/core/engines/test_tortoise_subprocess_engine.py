"""Unit tests for Tortoise subprocess bridge (no venv_tortoise required)."""

from __future__ import annotations

import tempfile
import wave
from pathlib import Path

import numpy as np


def test_read_output_wav_mono_float32_int16_mono() -> None:
    from app.core.engines.tortoise_subprocess_engine import _read_output_wav_mono_float32

    p = Path(tempfile.mktemp(suffix=".wav"))
    try:
        with wave.open(str(p), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(24000)
            pcm = (np.sin(np.linspace(0, 3, 800)) * 20000).astype(np.int16)
            wf.writeframes(pcm.tobytes())
        audio = _read_output_wav_mono_float32(p)
        assert audio.dtype == np.float32
        assert len(audio) == 800
        assert float(np.max(np.abs(audio))) > 0.01
    finally:
        if p.is_file():
            p.unlink()
