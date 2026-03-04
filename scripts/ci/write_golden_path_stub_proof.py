#!/usr/bin/env python3
"""
SSOT-compliant Golden Path Stub Proof Writer.

Runs the golden path integration test in stub mode (ASGI, no
real engines) and emits PROOF_GOLDEN_PATH_STUB_YYYY-MM-DD.json.

Non-fakeable: test MUST run; output metrics from actual pipeline output.
No --no-run-test escape hatch.

Usage:
    python scripts/ci/write_golden_path_stub_proof.py
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
TEST_PATH = "tests/integration/test_golden_path_e2e.py"
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


def _compute_wav_metrics(path: Path) -> dict:
    """Extract duration, RMS, and SHA-256 from a WAV file."""
    with open(path, "rb") as f:
        header = f.read(44)
        if len(header) < 44:
            return {"duration_seconds": 0, "rms_energy": 0, "output_sha256": ""}
        channels = struct.unpack_from("<H", header, 22)[0]
        sample_rate = struct.unpack_from("<I", header, 24)[0]
        data = f.read()

    if channels == 0 or sample_rate == 0:
        return {"duration_seconds": 0, "rms_energy": 0, "output_sha256": ""}

    with open(path, "rb") as f:
        sha = hashlib.sha256(f.read()).hexdigest()

    num_samples = len(data) // 2
    duration = num_samples / (sample_rate * channels)
    samples = struct.unpack(f"<{num_samples}h", data[: num_samples * 2])
    rms = (sum(s * s for s in samples) / num_samples) ** 0.5 / 32768.0

    return {
        "duration_seconds": round(duration, 3),
        "rms_energy": round(rms, 6),
        "output_sha256": sha,
    }


def main() -> int:
    command = f"python -m pytest {TEST_PATH} -v --tb=short"

    git_sha = _get_git_commit()[:8]
    ts = datetime.utcnow().strftime("%Y%m%dT%H%M%SZ")
    output_dir = BUILDLOGS / f"golden_path_stub_{ts}_{git_sha}"
    output_dir.mkdir(parents=True, exist_ok=True)

    os.environ["VOICESTUDIO_TEST_MODE"] = "stub"
    os.environ["VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR"] = str(output_dir)

    start = time.perf_counter()
    result = subprocess.run(
        [
            sys.executable, "-m", "pytest",
            f"{TEST_PATH}::TestGoldenPathOutputArtifact::test_golden_path_output_artifact",
            "-v", "--tb=short",
        ],
        cwd=ROOT, timeout=120,
        capture_output=True, text=True,
    )
    duration_sec = round(time.perf_counter() - start, 2)

    if result.returncode != 0:
        print("Golden path stub test failed.", file=sys.stderr)
        return 1

    stdout_sha = hashlib.sha256(result.stdout.encode("utf-8")).hexdigest()
    stderr_sha = hashlib.sha256(result.stderr.encode("utf-8")).hexdigest()

    manifest_path = output_dir / "output_manifest.json"
    if not manifest_path.exists():
        print(f"output_manifest.json missing in {output_dir}", file=sys.stderr)
        return 1

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    wav_name = manifest.get("output_wav")
    if not wav_name:
        print("output_manifest.json missing output_wav", file=sys.stderr)
        return 1

    wav_path = output_dir / wav_name
    if not wav_path.exists():
        print(f"Output WAV not found: {wav_path}", file=sys.stderr)
        return 1

    metrics = _compute_wav_metrics(wav_path)
    if metrics["duration_seconds"] <= 0 or metrics["rms_energy"] <= 0.001:
        print(f"Invalid output audio: {metrics}", file=sys.stderr)
        return 1

    artifact_bytes = wav_path.stat().st_size
    artifact_rel = str(wav_path.relative_to(ROOT)).replace("\\", "/")

    proof = {
        "command": command,
        "exit_code": 0,
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "engine_mode": "stub",
        "checks": {"integration_test": "passed"},
        "output_metrics": metrics,
        "models": {"not_required": True},
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
        proof, "PROOF_GOLDEN_PATH_STUB"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = VERIFICATION_DIR / f"PROOF_GOLDEN_PATH_STUB_{date_str}.json"

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
