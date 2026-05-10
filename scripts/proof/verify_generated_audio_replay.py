#!/usr/bin/env python3
"""Automated replay/decode validation for generated audio proof artifacts."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.proof.audio_forensics import analyze_wav_bytes, is_json_error_body, sha256_hex

SUPPORTED_SAMPLE_RATES = {8000, 16000, 22050, 24000, 32000, 44100, 48000}
SUPPORTED_CHANNELS = {1, 2}


def _load_json(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("proof JSON must be an object")
    return data


def _proof_audio_path(proof: dict[str, Any]) -> Path | None:
    generated = proof.get("generated_audio")
    export = proof.get("export")
    for section in (generated, export):
        if not isinstance(section, dict):
            continue
        raw = section.get("artifact_path") or section.get("path")
        if isinstance(raw, str) and raw.strip():
            return Path(raw)
    return None


def _read_audio_from_url(url: str, timeout_seconds: float) -> bytes:
    with urlopen(url, timeout=timeout_seconds) as response:
        return response.read()


def _failure(source: dict[str, Any], blockers: list[str]) -> dict[str, Any]:
    return {
        "status": "fail",
        "source": source,
        "sha256": None,
        "size_bytes": None,
        "automated_replay_validation": {
            "decoded": False,
            "non_silent": None,
            "duration_seconds": None,
            "sample_rate_hz": None,
            "channels": None,
            "supported_sample_rate": None,
            "supported_channels": None,
        },
        "blockers": blockers,
    }


def validate_audio_bytes(data: bytes, source: dict[str, Any]) -> dict[str, Any]:
    blockers: list[str] = []
    if not data:
        blockers.append("audio payload is empty")
        return _failure(source, blockers)
    if is_json_error_body(data):
        blockers.append("audio payload is JSON, not generated audio")
        return _failure(source, blockers)

    analysis = analyze_wav_bytes(data)
    decoded = bool(analysis.get("is_wav")) and not analysis.get("error")
    sample_rate = analysis.get("sample_rate_hz")
    channels = analysis.get("channels")
    duration = analysis.get("duration_seconds")
    supported_sample_rate = sample_rate in SUPPORTED_SAMPLE_RATES
    supported_channels = channels in SUPPORTED_CHANNELS

    if not decoded:
        blockers.append(f"WAV decode failed: {analysis.get('error') or 'unknown error'}")
    if duration is None or float(duration) <= 0:
        blockers.append("decoded WAV duration must be greater than zero")
    if analysis.get("non_silent") is not True:
        blockers.append("decoded WAV must contain non-silent samples")
    if not supported_sample_rate:
        blockers.append(f"unsupported sample rate: {sample_rate}")
    if not supported_channels:
        blockers.append(f"unsupported channel count: {channels}")

    return {
        "status": "pass" if not blockers else "fail",
        "source": source,
        "sha256": sha256_hex(data),
        "size_bytes": len(data),
        "automated_replay_validation": {
            "decoded": decoded,
            "non_silent": analysis.get("non_silent"),
            "duration_seconds": duration,
            "sample_rate_hz": sample_rate,
            "channels": channels,
            "supported_sample_rate": supported_sample_rate,
            "supported_channels": supported_channels,
            "container": analysis.get("container"),
            "peak_abs_sample": analysis.get("peak_abs_sample"),
            "rms": analysis.get("rms"),
        },
        "blockers": blockers,
    }


def validate_audio_path(path: Path) -> dict[str, Any]:
    source = {"kind": "audio_path", "path": str(path)}
    try:
        data = path.read_bytes()
    except OSError as exc:
        return _failure(source, [f"failed to read audio path: {exc}"])
    return validate_audio_bytes(data, source)


def validate_audio_url(url: str, timeout_seconds: float) -> dict[str, Any]:
    source = {"kind": "audio_url", "url": url}
    try:
        data = _read_audio_from_url(url, timeout_seconds)
    except (HTTPError, URLError, TimeoutError, OSError) as exc:
        return _failure(source, [f"failed to fetch audio URL: {exc}"])
    return validate_audio_bytes(data, source)


def validate_proof_json(path: Path, timeout_seconds: float) -> dict[str, Any]:
    source = {"kind": "proof_json", "path": str(path)}
    try:
        proof = _load_json(path)
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        return _failure(source, [f"failed to load proof JSON: {exc}"])

    audio_path = _proof_audio_path(proof)
    if audio_path is None:
        return _failure(source, ["proof JSON has no generated_audio.artifact_path or export.path"])
    if not audio_path.is_absolute():
        audio_path = path.parent / audio_path
    result = validate_audio_path(audio_path)
    result["source"] = {
        "kind": "proof_json",
        "path": str(path),
        "resolved_audio_path": str(audio_path),
    }
    return result


def _human_report(result: dict[str, Any]) -> str:
    replay = result["automated_replay_validation"]
    lines = [
        f"status={result['status']}",
        f"source={result['source']}",
        f"decoded={replay['decoded']}",
        f"non_silent={replay['non_silent']}",
        f"duration_seconds={replay['duration_seconds']}",
        f"sample_rate_hz={replay['sample_rate_hz']}",
        f"channels={replay['channels']}",
    ]
    if result["blockers"]:
        lines.append("blockers:")
        lines.extend(f"- {blocker}" for blocker in result["blockers"])
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--audio-path", type=Path, help="Generated WAV file to validate")
    target.add_argument("--audio-url", help="Generated audio URL to fetch and validate")
    target.add_argument("--proof-json", type=Path, help="Proof JSON containing a generated audio artifact path")
    parser.add_argument("--timeout-seconds", type=float, default=10.0)
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON")
    args = parser.parse_args(argv)

    if args.audio_path is not None:
        result = validate_audio_path(args.audio_path)
    elif args.audio_url:
        result = validate_audio_url(args.audio_url, args.timeout_seconds)
    else:
        result = validate_proof_json(args.proof_json, args.timeout_seconds)

    if args.json:
        print(json.dumps(result, indent=2, sort_keys=True))
    else:
        print(_human_report(result))
    return 0 if result["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
