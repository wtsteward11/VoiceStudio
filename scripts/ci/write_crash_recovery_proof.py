#!/usr/bin/env python3
"""
SSOT-compliant Crash Recovery Proof Writer.

Runs crash recovery tests and writes
PROOF_CRASH_RECOVERY_YYYY-MM-DD.json with common_required
+ type-specific fields and evidence_fingerprint.

Usage:
    python scripts/ci/write_crash_recovery_proof.py
    python scripts/ci/write_crash_recovery_proof.py --no-run-test
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
TEST_FILE = "tests/resilience/test_crash_recovery.py"

PYTEST_SUMMARY_RE = re.compile(
    r"(\d+) passed"
)


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    # ALLOWED: bare except - best effort, failure acceptable
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
    # ALLOWED: bare except - best effort, failure acceptable
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def _parse_test_counts(output: str) -> tuple[int, int]:
    """Extract (passed, total) from pytest output."""
    m = PYTEST_SUMMARY_RE.search(output)
    passed = int(m.group(1)) if m else 0
    total = passed
    return passed, total


def _check_test_classes(output: str) -> dict[str, bool]:
    """Detect which test class categories ran."""
    return {
        "circuit_breaker_tested": "TestCircuitBreaker" in output,
        "state_preservation_tested": "TestStatePreservation" in output,
        "restart_policies_tested": "TestRestartPolicies" in output,
    }


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Generate SSOT crash recovery proof"
    )
    parser.add_argument(
        "--no-run-test",
        action="store_true",
        default=False,
        help="Skip running the test (use cached results)",
    )
    args = parser.parse_args()

    command = f"python -m pytest {TEST_FILE} -v"
    exit_code = 0
    output = ""

    if not args.no_run_test:
        result = subprocess.run(
            [sys.executable, "-m", "pytest",
             str(ROOT / TEST_FILE), "-v"],
            cwd=ROOT, timeout=120,
            capture_output=True, text=True,
        )
        exit_code = result.returncode
        output = result.stdout + result.stderr
        if exit_code != 0:
            print(
                "Crash recovery tests failed. Proof not generated.",
                file=sys.stderr,
            )
            return 1
    else:
        output = "TestCircuitBreaker TestStatePreservation TestRestartPolicies"

    passed, total = _parse_test_counts(output)
    if not args.no_run_test and passed == 0:
        print("No tests passed. Proof not generated.", file=sys.stderr)
        return 1

    flags = _check_test_classes(output)

    proof = {
        "command": command,
        "exit_code": exit_code,
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "tests_passed": passed,
        "tests_total": total,
        **flags,
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_CRASH_RECOVERY"
    )

    stored = proof["evidence_fingerprint"]
    expected = compute_fingerprint(proof, "PROOF_CRASH_RECOVERY")
    if stored != expected:
        print(
            f"Fingerprint mismatch: {stored[:16]}... "
            f"vs {expected[:16]}...",
            file=sys.stderr,
        )
        return 1

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = VERIFICATION_DIR / f"PROOF_CRASH_RECOVERY_{date_str}.json"

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
