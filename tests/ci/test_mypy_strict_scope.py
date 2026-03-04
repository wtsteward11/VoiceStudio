"""
CI gate: mypy --strict on a narrow scope with baseline error budget.

Fails if error count exceeds baseline. Baseline is stored in .ci/mypy_strict_baseline.json
and should be burned down over time.
"""
from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
BASELINE_PATH = ROOT / ".ci" / "mypy_strict_baseline.json"

STRICT_SCOPE = [
    "backend/api/routes/voice/",
    "backend/services/synthesis_service.py",
    "backend/services/engine_service.py",
]


def _run_mypy_strict() -> tuple[int, str]:
    """Run mypy --strict on STRICT_SCOPE. Returns (error_count, stderr+stdout)."""
    cmd = [
        "python",
        "-m",
        "mypy",
        "--strict",
        "--follow-imports=skip",
        "--config-file",
        str(ROOT / "pyproject.toml"),
        "--no-error-summary",
        *[str(ROOT / p) for p in STRICT_SCOPE],
    ]
    result = subprocess.run(
        cmd,
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=120,
    )
    output = (result.stdout or "") + (result.stderr or "")
    # Parse "Found N errors" from mypy output (e.g. "Found 105 errors in 9 files")
    match = re.search(r"Found (\d+) error", output)
    if match:
        error_count = int(match.group(1))
    else:
        # Fallback: count "error:" lines
        error_count = sum(1 for line in output.splitlines() if ": error:" in line)
    return error_count, output


def _load_baseline() -> dict:
    if not BASELINE_PATH.exists():
        pytest.fail(f"Baseline file missing: {BASELINE_PATH}")
    data = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    return data


def test_mypy_strict_scope_under_budget() -> None:
    """Mypy strict scope error count must not exceed baseline budget."""
    baseline = _load_baseline()
    budget = baseline.get("baseline_errors", 999)
    scope = baseline.get("scope", STRICT_SCOPE)

    error_count, output = _run_mypy_strict()
    delta = error_count - budget

    # Print for CI visibility
    print(f"\nMypy strict scope: {error_count} errors (budget: {budget}, delta: {delta:+d})")
    if output.strip():
        print(output.strip()[-2000:])  # last 2k chars

    assert error_count <= budget, (
        f"Mypy strict scope: {error_count} errors exceeds budget {budget} (delta: {delta:+d}). "
        f"Fix type errors or update .ci/mypy_strict_baseline.json"
    )
