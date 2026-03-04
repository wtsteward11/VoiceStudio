#!/usr/bin/env python3
"""
Refresh git_commit, git_branch, timestamp in proof JSON files (M11 locked down).

MUST set historical_proof=true, refreshed=true, refreshed_reason, refreshed_at.
May update ONLY: git_commit, git_branch, timestamp, date.
MUST NOT change evidence fields. Fingerprint is recomputed and must remain unchanged.

Refreshed proofs require entry in .ci/historical_proofs_allowlist.json to pass CI.

Usage:
  python scripts/ci/refresh_proof_git_metadata.py --reason "pull after gatec run" [path1 path2 ...]
  python scripts/ci/refresh_proof_git_metadata.py --reason "..."  # refreshes all PROOF_*.json
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
PROOF_DIR = ROOT / "docs" / "reports" / "verification"

from scripts.ci.proof_fingerprint import compute_fingerprint


def get_proof_type(basename: str) -> str | None:
    """Return schema key for proof type or None if unknown."""
    if not basename.startswith("PROOF_") or not basename.endswith(".json"):
        return None
    stem = basename[: -len(".json")]
    for prefix in ("PROOF_PAYLOAD_DETOX", "PROOF_PROVENANCE", "PROOF_GATE_C", "PROOF_INSTALLER"):
        if stem == prefix or stem.startswith(prefix + "_"):
            return prefix
    return None


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


def refresh_proof(path: Path, reason: str) -> bool:
    """
    Update git metadata only. Set refreshed, historical_proof, refreshed_reason, refreshed_at.
    Assert evidence_fingerprint unchanged. Return True if updated.
    """
    if not path.exists() or path.suffix.lower() != ".json":
        return False
    proof_type = get_proof_type(path.name)
    if not proof_type:
        return False
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError:
        return False

    fp_before = compute_fingerprint(data, proof_type)
    if "evidence_fingerprint" in data and data["evidence_fingerprint"] != fp_before:
        print(
            f"ERROR: {path.relative_to(ROOT)} evidence changed (fingerprint mismatch). Refuse to refresh.",
            file=sys.stderr,
        )
        return False

    git_commit, git_branch = _git_info()
    data["git_commit"] = git_commit
    data["git_branch"] = git_branch
    data["timestamp"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    if "date" in data:
        data["date"] = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    data["refreshed"] = True
    data["refreshed_reason"] = reason
    data["refreshed_at"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    data["historical_proof"] = True

    fp_after = compute_fingerprint(data, proof_type)
    if fp_before != fp_after:
        print(
            f"ERROR: {path.relative_to(ROOT)} fingerprint changed after update. Refuse to write.",
            file=sys.stderr,
        )
        return False

    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return True


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Refresh git metadata in proofs (M11: sets historical_proof, refreshed, requires allowlist)."
    )
    parser.add_argument("--reason", required=True, help="Required reason for refresh (e.g. 'pull after gatec run')")
    parser.add_argument("paths", nargs="*", help="Proof file paths (default: all PROOF_*.json)")
    args = parser.parse_args()

    if args.paths:
        paths = [Path(p) for p in args.paths]
    else:
        paths = list(PROOF_DIR.glob("PROOF_*.json"))

    if not paths:
        print("No proof files to refresh.", file=sys.stderr)
        return 1

    updated = 0
    for p in paths:
        if not p.is_absolute():
            p = ROOT / p
        if refresh_proof(p, args.reason):
            print(f"Refreshed: {p.relative_to(ROOT)}")
            updated += 1

    if updated == 0:
        print("No files updated.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
