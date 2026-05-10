"""Small WAV forensic helpers for voice synthesis proof artifacts."""
from __future__ import annotations

import hashlib
import math
import struct
from typing import Any


def is_json_error_body(data: bytes) -> bool:
    """Return true when a response body looks like JSON instead of audio."""
    stripped = data.lstrip()
    return stripped.startswith(b"{") or stripped.startswith(b"[")


def sha256_hex(data: bytes) -> str:
    """Return lowercase SHA-256 hex digest for binary audio proof evidence."""
    return hashlib.sha256(data).hexdigest()


def analyze_wav_bytes(data: bytes) -> dict[str, Any]:
    """Parse enough PCM16 WAV structure for proof validation.

    The parser intentionally supports the minimal RIFF/WAVE surface needed by
    generated-audio proof checks and fails explicitly for unknown formats.
    """
    result: dict[str, Any] = {
        "is_wav": False,
        "header_hex": data[:16].hex(),
        "container": None,
        "sample_rate_hz": None,
        "channels": None,
        "bits_per_sample": None,
        "data_chunk_size": None,
        "duration_seconds": None,
        "non_silent": None,
        "peak_abs_sample": None,
        "rms": None,
        "error": None,
    }
    if len(data) < 12:
        result["error"] = "input shorter than RIFF/WAVE header"
        return result
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        result["error"] = "missing RIFF/WAVE signature"
        return result

    result["is_wav"] = True
    result["container"] = "RIFF/WAVE"
    pos = 12
    fmt: dict[str, int] | None = None
    data_chunk: bytes | None = None
    data_chunk_size = 0

    while pos + 8 <= len(data):
        chunk_id = data[pos:pos + 4]
        chunk_size = int.from_bytes(data[pos + 4:pos + 8], "little", signed=False)
        payload_start = pos + 8
        payload_end = payload_start + chunk_size
        if payload_end > len(data):
            result["error"] = f"chunk {chunk_id!r} extends beyond input"
            return result
        payload = data[payload_start:payload_end]

        if chunk_id == b"fmt ":
            if chunk_size < 16:
                result["error"] = "fmt chunk shorter than 16 bytes"
                return result
            audio_format, channels, sample_rate, byte_rate, block_align, bits = struct.unpack(
                "<HHIIHH",
                payload[:16],
            )
            del byte_rate
            fmt = {
                "audio_format": audio_format,
                "channels": channels,
                "sample_rate": sample_rate,
                "block_align": block_align,
                "bits_per_sample": bits,
            }
        elif chunk_id == b"data":
            data_chunk = payload
            data_chunk_size = chunk_size

        pos = payload_end + (chunk_size % 2)

    if fmt is None:
        result["error"] = "missing fmt chunk"
        return result
    result["sample_rate_hz"] = fmt["sample_rate"]
    result["channels"] = fmt["channels"]
    result["bits_per_sample"] = fmt["bits_per_sample"]

    if fmt["audio_format"] != 1:
        result["error"] = f"unsupported WAV audio_format {fmt['audio_format']}"
        return result
    if fmt["bits_per_sample"] != 16:
        result["error"] = f"unsupported bits_per_sample {fmt['bits_per_sample']}"
        return result
    if data_chunk is None:
        result["error"] = "missing data chunk"
        return result

    result["data_chunk_size"] = data_chunk_size
    bytes_per_second = fmt["sample_rate"] * fmt["channels"] * (fmt["bits_per_sample"] / 8.0)
    result["duration_seconds"] = data_chunk_size / bytes_per_second if bytes_per_second else None

    sample_count = len(data_chunk) // 2
    if sample_count == 0:
        result["non_silent"] = False
        result["peak_abs_sample"] = 0
        result["rms"] = 0.0
        return result

    peak = 0
    square_sum = 0
    for (sample,) in struct.iter_unpack("<h", data_chunk[:sample_count * 2]):
        abs_sample = abs(sample)
        peak = max(peak, abs_sample)
        square_sum += sample * sample

    rms = math.sqrt(square_sum / sample_count)
    result["peak_abs_sample"] = peak
    result["rms"] = rms
    result["non_silent"] = peak >= 10
    return result
