"""Tests for generated-audio automated replay validation."""
from __future__ import annotations

import contextlib
import io
import json
import struct
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.proof import verify_generated_audio_replay as replay


def _wav(samples: list[int], *, sample_rate: int = 16000, channels: int = 1) -> bytes:
    pcm = b"".join(struct.pack("<h", sample) for sample in samples)
    byte_rate = sample_rate * channels * 2
    block_align = channels * 2
    fmt = struct.pack("<HHIIHH", 1, channels, sample_rate, byte_rate, block_align, 16)
    riff_size = 4 + (8 + len(fmt)) + (8 + len(pcm))
    return (
        b"RIFF"
        + struct.pack("<I", riff_size)
        + b"WAVE"
        + b"fmt "
        + struct.pack("<I", len(fmt))
        + fmt
        + b"data"
        + struct.pack("<I", len(pcm))
        + pcm
    )


def test_audio_path_valid_non_silent_wav_passes(tmp_path: Path) -> None:
    audio = tmp_path / "generated.wav"
    audio.write_bytes(_wav([0, 1000, -1000, 0] * 4000))

    result = replay.validate_audio_path(audio)

    assert result["status"] == "pass"
    assert result["automated_replay_validation"]["decoded"] is True
    assert result["automated_replay_validation"]["non_silent"] is True
    assert result["sha256"]


def test_audio_path_silent_wav_fails(tmp_path: Path) -> None:
    audio = tmp_path / "silent.wav"
    audio.write_bytes(_wav([0] * 4000))

    result = replay.validate_audio_path(audio)

    assert result["status"] == "fail"
    assert any("non-silent" in blocker for blocker in result["blockers"])


def test_json_error_body_fails_without_decode(tmp_path: Path) -> None:
    audio = tmp_path / "error.wav"
    audio.write_bytes(b'{"detail":"not found"}')

    result = replay.validate_audio_path(audio)

    assert result["status"] == "fail"
    assert result["automated_replay_validation"]["decoded"] is False
    assert any("JSON" in blocker for blocker in result["blockers"])


def test_proof_json_resolves_generated_audio_artifact_path(tmp_path: Path) -> None:
    audio = tmp_path / "generated.wav"
    audio.write_bytes(_wav([0, 500, -500, 0] * 4000))
    proof = tmp_path / "proof.json"
    proof.write_text(json.dumps({"generated_audio": {"artifact_path": "generated.wav"}}), encoding="utf-8")

    result = replay.validate_proof_json(proof, timeout_seconds=1.0)

    assert result["status"] == "pass"
    assert result["source"]["kind"] == "proof_json"
    assert result["source"]["resolved_audio_path"].endswith("generated.wav")


def test_audio_url_fetches_and_validates_bytes(monkeypatch: pytest.MonkeyPatch) -> None:
    class FakeResponse:
        def __enter__(self) -> FakeResponse:
            return self

        def __exit__(self, exc_type: object, exc: object, tb: object) -> None:
            return None

        def read(self) -> bytes:
            return _wav([0, 1000, -1000, 0] * 4000)

    monkeypatch.setattr(replay, "urlopen", lambda url, timeout: FakeResponse())

    result = replay.validate_audio_url("http://127.0.0.1/audio.wav", timeout_seconds=1.0)

    assert result["status"] == "pass"
    assert result["source"]["kind"] == "audio_url"


def test_main_json_output_returns_failure_for_missing_target(tmp_path: Path) -> None:
    missing = tmp_path / "missing.wav"

    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = replay.main(["--audio-path", str(missing), "--json"])
    output = json.loads(buf.getvalue())

    assert rc == 1
    assert output["status"] == "fail"
    assert any("failed to read audio path" in blocker for blocker in output["blockers"])
