#!/usr/bin/env python3
"""
SSOT-compliant Golden Path Real Proof Writer.

Runs the golden path E2E test with real engines, computes model
hashes and output audio metrics.

FAILS (exit 1) if prerequisites are not met. No silent skips.

Usage:
    python scripts/ci/write_golden_path_real_proof.py
"""
from __future__ import annotations

import hashlib
import json
import os
import struct
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
E2E_TEST = "tests/e2e/test_golden_path.py"
BUILDLOGS = ROOT / ".buildlogs" / "proof_runs"


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "0" * 40


def _get_git_branch() -> str:
    try:
        out = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def _file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _compute_wav_metrics(path: Path) -> dict:
    """Extract duration and RMS energy from a WAV file."""
    with open(path, "rb") as f:
        header = f.read(44)
        if len(header) < 44:
            return {"duration_seconds": 0, "rms_energy": 0}
        channels = struct.unpack_from("<H", header, 22)[0]
        sample_rate = struct.unpack_from("<I", header, 24)[0]
        data = f.read()

    if channels == 0 or sample_rate == 0:
        return {"duration_seconds": 0, "rms_energy": 0}

    num_samples = len(data) // 2
    duration = num_samples / (sample_rate * channels)
    samples = struct.unpack(f"<{num_samples}h", data[:num_samples * 2])
    rms = (sum(s * s for s in samples) / num_samples) ** 0.5 / 32768.0

    return {
        "duration_seconds": round(duration, 3),
        "rms_energy": round(rms, 6),
        "output_sha256": _file_sha256(path),
    }


def _collect_model_hashes() -> dict[str, str]:
    """Compute SHA-256 hashes for available model files."""
    models_root = Path(
        os.environ.get("VOICESTUDIO_MODELS_PATH", "models")
    )
    hashes: dict[str, str] = {}

    whisper = models_root / "whisper"
    if whisper.exists():
        for gguf in whisper.glob("*.gguf"):
            hashes[f"whisper_cpp:{gguf.name}"] = _file_sha256(gguf)
            break

    piper = models_root / "piper"
    if piper.exists():
        for onnx in piper.glob("*.onnx"):
            hashes[f"piper:{onnx.name}"] = _file_sha256(onnx)
            break

    xtts = models_root / "xtts"
    if xtts.exists():
        for pt in xtts.rglob("*.pth"):
            hashes[f"xtts:{pt.name}"] = _file_sha256(pt)
            break

    return hashes


def main() -> int:
    precond = subprocess.run(
        [sys.executable,
         str(ROOT / "scripts" / "golden_path_preconditions.py"),
         "--check-backend", "http://localhost:8000", "--json"],
        cwd=ROOT, capture_output=True, text=True, timeout=15,
    )
    if precond.returncode != 0:
        print(
            "Preconditions check failed. Cannot run real mode.",
            file=sys.stderr,
        )
        return 1

    try:
        precond_data = json.loads(precond.stdout)
    except (json.JSONDecodeError, ValueError):
        print(
            "Preconditions output is not valid JSON.",
            file=sys.stderr,
        )
        return 1

    if not precond_data.get("ready_for_real_mode"):
        print(
            f"Not ready for real mode: "
            f"{json.dumps(precond_data, indent=2)}",
            file=sys.stderr,
        )
        return 1

    command = f"python -m pytest {E2E_TEST} -v --tb=short"

    git_sha = _get_git_commit()[:8]
    ts = datetime.utcnow().strftime("%Y%m%dT%H%M%SZ")
    output_dir = BUILDLOGS / f"golden_path_real_{ts}_{git_sha}"
    output_dir.mkdir(parents=True, exist_ok=True)

    os.environ["VOICESTUDIO_TEST_MODE"] = "real"
    os.environ["VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR"] = str(output_dir)

    start = time.perf_counter()
    result = subprocess.run(
        [sys.executable, "-m", "pytest",
         str(ROOT / E2E_TEST), "-v", "--tb=short"],
        cwd=ROOT, timeout=180,
        capture_output=True, text=True,
    )
    duration_sec = round(time.perf_counter() - start, 2)

    if result.returncode != 0:
        print(
            "Golden path real test failed.",
            file=sys.stderr,
        )
        return 1

    stdout_sha = hashlib.sha256(
        result.stdout.encode("utf-8")
    ).hexdigest()
    stderr_sha = hashlib.sha256(
        result.stderr.encode("utf-8")
    ).hexdigest()

    manifest_path = output_dir / "output_manifest.json"
    if not manifest_path.exists():
        print(
            f"output_manifest.json missing in {output_dir}",
            file=sys.stderr,
        )
        return 1

    manifest = json.loads(
        manifest_path.read_text(encoding="utf-8")
    )
    wav_name = manifest.get("output_wav")
    if not wav_name:
        print(
            "output_manifest.json missing output_wav",
            file=sys.stderr,
        )
        return 1

    wav_path = output_dir / wav_name
    if not wav_path.exists():
        print(
            f"Output WAV not found: {wav_path}",
            file=sys.stderr,
        )
        return 1

    metrics = _compute_wav_metrics(wav_path)
    if (metrics["duration_seconds"] <= 0
            or metrics.get("rms_energy", 0) <= 0.001):
        print(
            f"Output audio invalid: {metrics}",
            file=sys.stderr,
        )
        return 1

    model_hashes = _collect_model_hashes()
    if not model_hashes:
        print(
            "No model hashes collected. Models must be installed.",
            file=sys.stderr,
        )
        return 1

    artifact_bytes = wav_path.stat().st_size
    artifact_rel = str(
        wav_path.relative_to(ROOT)
    ).replace("\\", "/")

    proof = {
        "command": command,
        "exit_code": 0,
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "engine_mode": "real",
        "checks": {
            "preconditions": "passed",
            "e2e_test": "passed",
        },
        "output_metrics": metrics,
        "models": model_hashes,
        "passed": True,
        "test_ran": True,
        "pytest_stdout_sha256": stdout_sha,
        "pytest_stderr_sha256": stderr_sha,
        "pytest_duration_seconds": duration_sec,
        "artifact_path": artifact_rel,
        "artifact_sha256": metrics["output_sha256"],
        "artifact_bytes": artifact_bytes,
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_GOLDEN_PATH_REAL"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = (
        VERIFICATION_DIR
        / f"PROOF_GOLDEN_PATH_REAL_{date_str}.json"
    )

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
