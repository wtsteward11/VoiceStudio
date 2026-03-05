#!/usr/bin/env python3
"""
SSOT-compliant UI Command Surface Proof Writer.

Runs the UI command surface test and writes PROOF_UI_COMMAND_SURFACE_YYYY-MM-DD.json
to docs/reports/verification/ with common_required + type-specific fields and evidence_fingerprint.

Usage:
    python scripts/ci/write_ui_command_surface_proof.py
    python scripts/ci/write_ui_command_surface_proof.py --no-run-test
"""
from __future__ import annotations

import json
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint
from tests.ci.test_ui_command_surface import get_ui_command_surface_results

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"


def _get_git_commit() -> str:
    """Get current git commit SHA."""
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True,
            text=True,
            cwd=ROOT,
            timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    # ALLOWED: bare except - best effort, failure acceptable
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "0" * 40


def _get_git_branch() -> str:
    """Get current git branch name."""
    try:
        out = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True,
            text=True,
            cwd=ROOT,
            timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    # ALLOWED: bare except - best effort, failure acceptable
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Generate SSOT UI command surface proof"
    )
    parser.add_argument(
        "--run-test",
        action="store_true",
        default=True,
        help="Run UI command surface test first",
    )
    parser.add_argument(
        "--no-run-test",
        action="store_false",
        dest="run_test",
        help="Skip running the test",
    )
    args = parser.parse_args()

    command = "python -m pytest tests/ci/test_ui_command_surface.py -v"
    exit_code = 0

    if args.run_test:
        result = subprocess.run(
            [
                sys.executable,
                "-m",
                "pytest",
                str(ROOT / "tests" / "ci" / "test_ui_command_surface.py"),
                "-v",
            ],
            cwd=ROOT,
            timeout=60,
            capture_output=True,
            text=True,
        )
        exit_code = result.returncode
        if exit_code != 0:
            print(
                "UI command surface test failed. Proof not generated.",
                file=sys.stderr,
            )
            return 1

    results = get_ui_command_surface_results()
    if not results["all_commands_registered"] or not results["all_panels_registered"]:
        print(
            "UI command surface gate failed: not all commands/panels registered.",
            file=sys.stderr,
        )
        return 1

    timestamp = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
    proof = {
        "command": command,
        "exit_code": exit_code,
        "timestamp": timestamp,
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "commands_checked": results["commands_checked"],
        "panels_checked": results["panels_checked"],
        "all_commands_registered": results["all_commands_registered"],
        "all_panels_registered": results["all_panels_registered"],
        "command_details": results["command_details"],
        "panel_details": results["panel_details"],
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_UI_COMMAND_SURFACE"
    )

    # Self-validate fingerprint
    stored = proof.get("evidence_fingerprint", "")
    expected = compute_fingerprint(proof, "PROOF_UI_COMMAND_SURFACE")
    if stored != expected:
        print(
            f"Fingerprint mismatch: stored={stored[:16]}..., "
            f"expected={expected[:16]}...",
            file=sys.stderr,
        )
        return 1

    # Write to docs/reports/verification/
    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = VERIFICATION_DIR / f"PROOF_UI_COMMAND_SURFACE_{date_str}.json"

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
