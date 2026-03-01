#!/usr/bin/env python3
"""
Warning Budget Check (TD-007)

Checks that the build warning count stays within the defined budget.
Fails the CI build if warnings exceed the budget threshold.

Usage:
    python scripts/check_warning_budget.py [--budget N] [--json]

Exit codes:
    0 - Warning count within budget
    1 - Warning count exceeds budget
    2 - Build failed or could not parse warning count
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

# Default budget: current count + 20% headroom
# Based on BUILD_WARNING_ANALYSIS.md (2026-02-02): 2046 warnings after reduction
DEFAULT_BUDGET = 2500


def run_build() -> tuple[int, str, str]:
    """Run dotnet build and capture output."""
    cmd = [
        "dotnet", "build", "VoiceStudio.sln",
        "-c", "Debug", "-p:Platform=x64"
    ]

    result = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=Path(__file__).parent.parent
    )

    return result.returncode, result.stdout, result.stderr


def parse_warning_count(stdout: str, stderr: str) -> int | None:
    """Extract warning count from build output."""
    combined = stdout + stderr

    # Look for the summary line: "X Warning(s)"
    match = re.search(r"(\d+)\s+Warning\(s\)", combined)
    if match:
        return int(match.group(1))

    return None


def check_budget(budget: int, emit_json: bool = False) -> int:
    """Run build and check warning count against budget."""

    print("Building VoiceStudio.sln (Debug x64)...")
    exit_code, stdout, stderr = run_build()

    # Check for build failure
    if exit_code != 0:
        # Check if it's just warnings (exit code 0 expected)
        # Some builds may exit non-zero for other reasons
        if "Error(s)" in (stdout + stderr):
            error_match = re.search(r"(\d+)\s+Error\(s\)", stdout + stderr)
            if error_match and int(error_match.group(1)) > 0:
                result = {
                    "passed": False,
                    "reason": "build_failed",
                    "errors": int(error_match.group(1)),
                    "budget": budget
                }
                if emit_json:
                    print(json.dumps(result, indent=2))
                else:
                    print(f"FAIL: Build failed with {result['errors']} error(s)")
                return 2

    # Parse warning count
    warning_count = parse_warning_count(stdout, stderr)

    if warning_count is None:
        result = {
            "passed": False,
            "reason": "parse_error",
            "message": "Could not parse warning count from build output",
            "budget": budget
        }
        if emit_json:
            print(json.dumps(result, indent=2))
        else:
            print("FAIL: Could not parse warning count from build output")
        return 2

    # Check against budget
    passed = warning_count <= budget
    headroom = budget - warning_count

    result = {
        "passed": passed,
        "warning_count": warning_count,
        "budget": budget,
        "headroom": headroom,
        "utilization_percent": round((warning_count / budget) * 100, 1)
    }

    if emit_json:
        print(json.dumps(result, indent=2))
    else:
        if passed:
            print(f"PASS: {warning_count} warnings (budget: {budget}, headroom: {headroom})")
        else:
            print(f"FAIL: {warning_count} warnings exceeds budget of {budget} by {-headroom}")

    return 0 if passed else 1


def main():
    parser = argparse.ArgumentParser(
        description="Check build warning count against budget"
    )
    parser.add_argument(
        "--budget", "-b",
        type=int,
        default=DEFAULT_BUDGET,
        help=f"Warning budget threshold (default: {DEFAULT_BUDGET})"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output result as JSON"
    )
    args = parser.parse_args()

    return check_budget(args.budget, args.json)


if __name__ == "__main__":
    sys.exit(main())
