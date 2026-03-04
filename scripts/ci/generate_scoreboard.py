#!/usr/bin/env python3
"""
Generate quality_scoreboard.json -- single-truth quality artifact per run.

Output: <artifacts_dir>/quality_scoreboard.json (< 50KB)
Contains: Python test counts, C# test counts, proof validation status,
suppression policy check, mypy budget status.
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent


def _run(cmd: list[str], timeout: int = 120) -> tuple[int, str]:
    result = subprocess.run(
        cmd, cwd=ROOT, capture_output=True, text=True, timeout=timeout
    )
    return result.returncode, (result.stdout or "") + (result.stderr or "")


def collect_python_ci_tests() -> dict:
    """Run CI tests and collect pass/fail/skip counts."""
    rc, out = _run(
        ["python", "-m", "pytest", "tests/ci/", "-q", "-p", "randomly", "--randomly-seed=12345"],
        timeout=180,
    )
    match = re.search(r"(\d+) passed", out)
    passed = int(match.group(1)) if match else 0
    match = re.search(r"(\d+) failed", out)
    failed = int(match.group(1)) if match else 0
    match = re.search(r"(\d+) skipped", out)
    skipped = int(match.group(1)) if match else 0
    return {
        "exit_code": rc,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "green": rc == 0,
    }


def collect_proof_validation() -> dict:
    """Run check_state_proofs.py and report status."""
    rc, out = _run(
        ["python", "scripts/ci/check_state_proofs.py", "--no-git-match"],
        timeout=30,
    )
    return {
        "exit_code": rc,
        "green": rc == 0,
        "output_excerpt": out.strip()[-500:] if out.strip() else "",
    }


def collect_mypy_budget() -> dict:
    """Check mypy strict scope budget."""
    baseline_path = ROOT / ".ci" / "mypy_strict_baseline.json"
    if not baseline_path.exists():
        return {"green": False, "error": "baseline file missing"}

    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    budget = baseline.get("baseline_errors", 999)
    scope = baseline.get("scope", [])

    scope_paths = [str(ROOT / p) for p in scope]
    rc, out = _run(
        ["python", "-m", "mypy", "--strict", "--follow-imports=skip",
         "--config-file", str(ROOT / "pyproject.toml")] + scope_paths,
        timeout=120,
    )
    match = re.search(r"Found (\d+) error", out)
    if match:
        errors = int(match.group(1))
    else:
        errors = sum(1 for line in out.splitlines() if ": error:" in line)

    return {
        "errors": errors,
        "budget": budget,
        "delta": errors - budget,
        "green": errors <= budget,
    }


def collect_suppression_check() -> dict:
    """Run CI suppression guard test."""
    rc, out = _run(
        ["python", "-m", "pytest", "tests/ci/test_ci_suppression_guard.py", "-q"],
        timeout=30,
    )
    return {
        "exit_code": rc,
        "green": rc == 0,
    }


def collect_git_info() -> dict:
    """Get current git state."""
    rc, commit = _run(["git", "rev-parse", "HEAD"], timeout=5)
    rc2, branch = _run(["git", "branch", "--show-current"], timeout=5)
    return {
        "commit": commit.strip()[:40] if rc == 0 else "unknown",
        "branch": branch.strip() if rc2 == 0 else "unknown",
    }


def main() -> int:
    output_dir = ROOT / "artifacts" / "verify" / "latest"
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / "quality_scoreboard.json"

    scoreboard = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "git": collect_git_info(),
        "python_ci_tests": collect_python_ci_tests(),
        "proof_validation": collect_proof_validation(),
        "mypy_budget": collect_mypy_budget(),
        "suppression_check": collect_suppression_check(),
    }

    all_green = all(
        section.get("green", False)
        for key, section in scoreboard.items()
        if isinstance(section, dict) and "green" in section
    )
    scoreboard["all_green"] = all_green

    content = json.dumps(scoreboard, indent=2, ensure_ascii=False)
    if len(content.encode("utf-8")) > 50_000:
        print("ERROR: Scoreboard exceeds 50KB limit", file=sys.stderr)
        return 1

    output_path.write_text(content, encoding="utf-8")
    print(f"Scoreboard written to {output_path} ({len(content)} bytes)")
    print(f"All green: {all_green}")
    return 0 if all_green else 1


if __name__ == "__main__":
    sys.exit(main())
