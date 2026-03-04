"""
CI gate: skip debt guardrail. Fail if skip counts exceed budget.

Prevents silent growth of skipped tests. Budgets are from baseline (2026-03-04).
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
TESTS_DIR = ROOT / "tests"

SKIP_BUDGET = {
    "collection_skips": 60,
    "module_level_skips": 310,
    "total_skip_calls": 1750,
}
MARGIN = 5


def _get_collection_skips() -> int:
    """Run pytest --co -q and parse 'X skipped' from output."""
    result = subprocess.run(
        ["python", "-m", "pytest", str(TESTS_DIR), "--co", "-q"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=120,
    )
    output = (result.stdout or "") + (result.stderr or "")
    match = re.search(r"(\d+)\s+skipped", output)
    return int(match.group(1)) if match else 0


def _count_skip_calls() -> int:
    """Count pytest.skip( patterns in tests/."""
    total = 0
    for path in TESTS_DIR.rglob("*.py"):
        try:
            text = path.read_text(encoding="utf-8")
            total += len(re.findall(r"pytest\.skip\s*\(", text))
        except (OSError, UnicodeDecodeError):
            pass
    return total


def _count_module_level_skips() -> int:
    """Count allow_module_level=True patterns in tests/."""
    total = 0
    for path in TESTS_DIR.rglob("*.py"):
        try:
            text = path.read_text(encoding="utf-8")
            total += len(re.findall(r"allow_module_level\s*=\s*True", text))
        except (OSError, UnicodeDecodeError):
            pass
    return total


def test_skip_budget_collection() -> None:
    """Collection skips must not exceed budget + margin."""
    count = _get_collection_skips()
    budget = SKIP_BUDGET["collection_skips"] + MARGIN
    assert count <= budget, (
        f"Collection skips {count} exceeds budget {budget}. "
        "Reduce skips or update SKIP_BUDGET in this test."
    )


def test_skip_budget_module_level() -> None:
    """Module-level skip count must not exceed budget + margin."""
    count = _count_module_level_skips()
    budget = SKIP_BUDGET["module_level_skips"] + MARGIN
    assert count <= budget, (
        f"Module-level skips {count} exceeds budget {budget}. "
        "Reduce skips or update SKIP_BUDGET in this test."
    )


def test_skip_budget_total_calls() -> None:
    """Total pytest.skip() call sites must not exceed budget + margin."""
    count = _count_skip_calls()
    budget = SKIP_BUDGET["total_skip_calls"] + MARGIN
    assert count <= budget, (
        f"Total skip calls {count} exceeds budget {budget}. "
        "Reduce skips or update SKIP_BUDGET in this test."
    )
