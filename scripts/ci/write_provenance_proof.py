#!/usr/bin/env python3
"""
Produce provenance policy proof artifact for STATE.md Next 3 Steps.

Runs provenance + use-case tests and writes PROOF_PROVENANCE_2026-03-02.json.
Exit 0 only if pytest passes.

Machine-verifiable proof schema (GAP E): command, exit_code, stdout, stderr,
git_commit, git_branch, timestamp.

Usage: python scripts/ci/write_provenance_proof.py
"""
from __future__ import annotations

import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

PROOF_PATH = ROOT / "docs" / "reports" / "verification" / "PROOF_PROVENANCE_2026-03-02.json"
TEST_PATHS = [
    "tests/unit/test_audio_artifact_provenance.py",
    "tests/unit/test_audio_artifact_use_cases.py",
]
MAX_STDOUT_STDERR = 2000


def _git_info() -> tuple[str, str]:
    """Return (commit_hash, branch)."""
    try:
        commit = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=5,
        )
        branch = subprocess.run(
            ["git", "branch", "--show-current"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=5,
        )
        return (
            commit.stdout.strip() if commit.returncode == 0 else "unknown",
            branch.stdout.strip() if branch.returncode == 0 else "unknown",
        )
    except Exception:
        return ("unknown", "unknown")


def main() -> int:
    proof_path = PROOF_PATH
    proof_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [sys.executable, "-m", "pytest", *TEST_PATHS, "-q", "--tb=short"]
    result = subprocess.run(
        cmd,
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=120,
    )

    git_commit, git_branch = _git_info()

    proof: dict = {
        "step": "provenance_policy",
        "date": datetime.now(timezone.utc).strftime("%Y-%m-%d"),
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "exit_code": result.returncode,
        "tests_passed": "passed" if result.returncode == 0 else "failed",
        "command": f"pytest {' '.join(TEST_PATHS)} -q --tb=short",
        "stdout": (result.stdout or "")[:MAX_STDOUT_STDERR],
        "stderr": (result.stderr or "")[:MAX_STDOUT_STDERR],
        "git_commit": git_commit,
        "git_branch": git_branch,
    }

    # Try to infer test count from output
    if "passed" in result.stdout:
        parts = result.stdout.strip().split()
        for p in parts:
            if p.isdigit():
                proof["test_count"] = int(p)
                break

    if result.returncode != 0:
        print(result.stdout, file=sys.stderr)
        print(result.stderr, file=sys.stderr)
        print("Provenance tests failed. No proof file written.", file=sys.stderr)
        return result.returncode

    proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_PROVENANCE")
    proof_path.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(f"Proof written to {proof_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
