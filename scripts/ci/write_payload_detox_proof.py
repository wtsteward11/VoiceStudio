#!/usr/bin/env python3
"""
Produce repo payload detox proof artifact for STATE.md.

Runs check_repo_payloads.py and writes PROOF_PAYLOAD_DETOX_2026-03-02.json on success.
Exit 0 only if check passes.

Machine-verifiable proof schema: command, exit_code, timestamp, git_commit, git_branch,
check_repo_payloads, policy_file_summary.

Usage: python scripts/ci/write_payload_detox_proof.py
"""
from __future__ import annotations

import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
PROOF_PATH = ROOT / "docs" / "reports" / "verification" / "PROOF_PAYLOAD_DETOX_2026-03-02.json"
POLICY_PATH = ROOT / ".ci" / "repo_payload_policy.json"


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


def _policy_summary() -> dict:
    """Return policy_file_summary from repo_payload_policy.json."""
    if not POLICY_PATH.exists():
        return {"large_file_exceptions_count": 0, "payload_dir_baselines": 0}
    data = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    exceptions = data.get("large_file_exceptions", [])
    baselines = data.get("payload_dir_baselines", [])
    return {
        "large_file_exceptions_count": len(exceptions),
        "payload_dir_baselines": len(baselines),
    }


def main() -> int:
    proof_path = PROOF_PATH
    proof_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [sys.executable, "scripts/ci/check_repo_payloads.py"]
    result = subprocess.run(
        cmd,
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=60,
    )

    git_commit, git_branch = _git_info()
    policy_summary = _policy_summary()

    if result.returncode != 0:
        print(result.stdout, file=sys.stderr)
        print(result.stderr, file=sys.stderr)
        print("check_repo_payloads failed. No proof file written.", file=sys.stderr)
        return result.returncode

    summary = "PASS (git-tracked large files: 0; large_file_exceptions: empty)"
    if result.stdout.strip():
        summary = result.stdout.strip().split("\n")[0][:200]

    proof: dict = {
        "milestone": "M8 Repo Payload Detox",
        "step": "payload_detox",
        "date": datetime.now(timezone.utc).strftime("%Y-%m-%d"),
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "command": "python scripts/ci/check_repo_payloads.py",
        "exit_code": 0,
        "git_commit": git_commit,
        "git_branch": git_branch,
        "check_repo_payloads": summary,
        "policy_file_summary": policy_summary,
        "note": "check_repo_payloads passes when run in clean environment. Local installer/runtime (gitignored) may cause FORBIDDEN DIR; CI clone has no installer/runtime.",
    }

    proof_path.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(f"Proof written to {proof_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
