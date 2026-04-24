"""Unit tests for OpenVoice subprocess bridge (no venv_openvoice required)."""

from __future__ import annotations

import tempfile
import wave
from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np


def test_openvoice_subprocess_synthesize_invokes_worker() -> None:
    from app.core.engines.openvoice_subprocess_engine import OpenVoiceSubprocessEngine

    fake_py = Path(tempfile.gettempdir()) / "fake_openvoice_python.exe"
    out_wav = Path(tempfile.mktemp(suffix=".wav"))
    try:
        fake_py.parent.mkdir(parents=True, exist_ok=True)
        fake_py.write_text("", encoding="utf-8")

        with wave.open(str(out_wav), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(22050)
            pcm = (np.sin(np.linspace(0, 3, 400)) * 20000).astype(np.int16)
            wf.writeframes(pcm.tobytes())

        ref_wav = Path(tempfile.mktemp(suffix=".wav"))
        with wave.open(str(ref_wav), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(16000)
            wf.writeframes(pcm[:200].tobytes())

        proc = MagicMock()
        proc.returncode = 0
        proc.stderr = ""
        proc.stdout = ""

        with (
            patch(
                "app.core.engines.openvoice_subprocess_engine._resolve_family_python_exe",
                return_value=fake_py,
            ),
            patch(
                "app.core.engines.openvoice_subprocess_engine.subprocess.run",
                return_value=proc,
            ) as run_mock,
        ):
            eng = OpenVoiceSubprocessEngine(device="cpu", gpu=False)
            ret = eng.synthesize(
                "hello",
                str(ref_wav),
                language="en",
                output_path=str(out_wav),
            )
            assert ret is None
            run_mock.assert_called_once()
            args, kwargs = run_mock.call_args
            cmd = args[0]
            assert str(fake_py) in cmd[0]
            assert cmd[1] == "-m"
            assert cmd[2] == "app.cli.openvoice_worker_synthesize"
    finally:
        for p in (fake_py, out_wav, ref_wav):
            if p.is_file():
                p.unlink(missing_ok=True)
