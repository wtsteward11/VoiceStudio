#!/usr/bin/env python3
"""
Produce Phase 2.1 bulletproof proof artifact (Milestone-proof schema).

Runs check_service_boundaries, check_route_boundaries, check_route_size.
Writes PROOF_PHASE_2_1_BULLETPROOF_YYYY-MM-DD.json with full schema fields.

Usage: python scripts/ci/write_phase2_1_proof.py
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

PROOF_DIR = ROOT / "docs" / "reports" / "verification"


def _run(cmd: list[str], cwd: Path | None = None) -> tuple[int, str, str]:
    """Run command, return (exit_code, stdout, stderr)."""
    r = subprocess.run(
        cmd,
        cwd=cwd or ROOT,
        capture_output=True,
        text=True,
        timeout=60,
    )
    return r.returncode, r.stdout or "", r.stderr or ""


def _git_info() -> tuple[str, str]:
    """Return (commit_hash, branch)."""
    commit_ec, commit_out, _ = _run(["git", "rev-parse", "HEAD"])
    branch_ec, branch_out, _ = _run(["git", "branch", "--show-current"])
    return (
        commit_out.strip() if commit_ec == 0 else "unknown",
        branch_out.strip() if branch_ec == 0 else "unknown",
    )


def main() -> int:
    PROOF_DIR.mkdir(parents=True, exist_ok=True)

    checks: dict[str, dict[str, int]] = {}
    all_ok = True

    ec, _, _ = _run([sys.executable, "scripts/ci/check_service_boundaries.py"])
    checks["check_service_boundaries"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    ec, _, _ = _run([sys.executable, "scripts/ci/check_route_boundaries.py"])
    checks["check_route_boundaries"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    ec, _, _ = _run([sys.executable, "scripts/ci/check_route_size.py"])
    checks["check_route_size"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    timestamp_str = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    git_commit, git_branch = _git_info()

    proof: dict = {
        "phase": "2.1",
        "date": date_str,
        "checks": checks,
        "command": "check_service_boundaries && check_route_boundaries && check_route_size",
        "exit_code": 0 if all_ok else 1,
        "timestamp": timestamp_str,
        "git_commit": git_commit,
        "git_branch": git_branch,
        "note": "Phase 2.1: training_service in backend.services; route boundaries enforced.",
    }

    proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_PHASE_2_1")

    proof_path = PROOF_DIR / f"PROOF_PHASE_2_1_BULLETPROOF_{date_str}.json"
    proof_path.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(f"Proof written to {proof_path}")

    if not all_ok:
        print("Some checks failed. Proof written with exit_code=1.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
