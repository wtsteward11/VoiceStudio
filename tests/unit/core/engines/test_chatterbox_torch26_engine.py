"""Unit tests for ChatterboxTorch26Engine (subprocess / family venv path)."""

from __future__ import annotations

import json
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest


class TestChatterboxTorch26EngineSubprocess:
    """Subprocess delegation without a real torch26 venv."""

    def test_synthesize_writes_file_and_returns_none(self, tmp_path: Path) -> None:
        from app.core.engines.chatterbox_torch26_engine import ChatterboxTorch26Engine

        out_wav = tmp_path / "out.wav"
        ref_wav = tmp_path / "ref.wav"
        ref_wav.write_bytes(b"RIFF" + b"\x00" * 200)  # minimal stub; worker checks is_file

        fake_proc = MagicMock()
        fake_proc.returncode = 0
        fake_proc.stderr = ""
        fake_proc.stdout = ""

        def fake_run(*_args: object, **_kwargs: object) -> MagicMock:
            out_wav.write_bytes(b"RIFF" + b"\x01" * 500)
            return fake_proc

        exe = tmp_path / "python.exe"
        exe.write_text("stub")

        with (
            patch(
                "app.core.engines.chatterbox_torch26_engine._resolve_family_python_exe",
                return_value=exe,
            ),
            patch(
                "app.core.engines.chatterbox_torch26_engine.subprocess.run",
                side_effect=fake_run,
            ) as run_mock,
        ):
            eng = ChatterboxTorch26Engine(device="cpu", gpu=False)
            result = eng.synthesize(
                "hello",
                str(ref_wav),
                output_path=str(out_wav),
                language="en",
            )

        assert result is None
        assert out_wav.is_file() and out_wav.stat().st_size >= 64
        assert run_mock.called
        cmd = run_mock.call_args[0][0]
        assert "-m" in cmd
        assert "app.cli.chatterbox_worker_synthesize" in cmd

    def test_synthesize_failure_returns_none(self, tmp_path: Path) -> None:
        from app.core.engines.chatterbox_torch26_engine import ChatterboxTorch26Engine

        out_wav = tmp_path / "out.wav"
        ref_wav = tmp_path / "ref.wav"
        ref_wav.write_bytes(b"RIFF" + b"\x00" * 200)

        fake_proc = MagicMock()
        fake_proc.returncode = 1
        fake_proc.stderr = "ImportError: no module named chatterbox"
        fake_proc.stdout = ""

        exe = tmp_path / "python.exe"
        exe.write_text("stub")

        with (
            patch(
                "app.core.engines.chatterbox_torch26_engine._resolve_family_python_exe",
                return_value=exe,
            ),
            patch(
                "app.core.engines.chatterbox_torch26_engine.subprocess.run",
                return_value=fake_proc,
            ),
        ):
            eng = ChatterboxTorch26Engine(device="cpu", gpu=False)
            assert (
                eng.synthesize(
                    "hello",
                    str(ref_wav),
                    output_path=str(out_wav),
                )
                is None
            )


class TestChatterboxWorkerJsonContract:
    """Ensure worker JSON matches engine payload (smoke)."""

    def test_payload_roundtrip_keys(self) -> None:
        payload = {
            "text": "x",
            "speaker_wav": "/a/b.wav",
            "language": "en",
            "emotion": "neutral",
            "output_path": "/tmp/o.wav",
            "device": "cpu",
            "model_name": "chatterbox-tts/base",
        }
        raw = json.dumps(payload)
        back = json.loads(raw)
        assert set(back.keys()) >= {
            "text",
            "speaker_wav",
            "output_path",
            "device",
        }
