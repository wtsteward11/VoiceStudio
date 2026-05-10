"""Tests for scripts.proof.audio_forensics."""
from __future__ import annotations

import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.proof.audio_forensics import analyze_wav_bytes, is_json_error_body, sha256_hex


def _wav(samples: list[int], *, sample_rate: int = 8000, channels: int = 1) -> bytes:
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


def test_valid_pcm16_wav_parses_as_wav() -> None:
    result = analyze_wav_bytes(_wav([0, 1, -1]))
    assert result["is_wav"] is True
    assert result["container"] == "RIFF/WAVE"


def test_valid_wav_reports_sample_rate_channels_and_bits() -> None:
    result = analyze_wav_bytes(_wav([0, 0], sample_rate=44100, channels=1))
    assert result["sample_rate_hz"] == 44100
    assert result["channels"] == 1
    assert result["bits_per_sample"] == 16


def test_valid_wav_reports_duration() -> None:
    result = analyze_wav_bytes(_wav([0] * 8000, sample_rate=8000))
    assert result["duration_seconds"] == 1.0


def test_silent_wav_reports_non_silent_false() -> None:
    result = analyze_wav_bytes(_wav([0] * 16))
    assert result["non_silent"] is False
    assert result["peak_abs_sample"] == 0


def test_non_silent_wav_reports_non_silent_true() -> None:
    result = analyze_wav_bytes(_wav([0, 1000, -1000, 0]))
    assert result["non_silent"] is True
    assert result["peak_abs_sample"] == 1000
    assert result["rms"] > 0


def test_json_body_detection_works() -> None:
    assert is_json_error_body(b'  {"detail":"missing"}') is True
    assert is_json_error_body(_wav([0])) is False


def test_corrupt_riff_fails_gracefully() -> None:
    result = analyze_wav_bytes(b"NOPE" + b"\x00" * 64)
    assert result["is_wav"] is False
    assert "RIFF" in result["error"]


def test_missing_data_chunk_fails_gracefully() -> None:
    fmt = struct.pack("<HHIIHH", 1, 1, 8000, 16000, 2, 16)
    riff_size = 4 + (8 + len(fmt))
    body = b"RIFF" + struct.pack("<I", riff_size) + b"WAVEfmt " + struct.pack("<I", len(fmt)) + fmt
    result = analyze_wav_bytes(body)
    assert result["is_wav"] is True
    assert result["error"] == "missing data chunk"


def test_sha256_is_stable_lowercase_hex() -> None:
    digest = sha256_hex(b"voice-studio-proof")
    assert digest == sha256_hex(b"voice-studio-proof")
    assert len(digest) == 64
    assert digest == digest.lower()


def test_short_input_does_not_throw() -> None:
    result = analyze_wav_bytes(b"RIFF")
    assert result["is_wav"] is False
    assert "shorter" in result["error"]
