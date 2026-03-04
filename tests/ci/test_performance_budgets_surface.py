"""
Performance Budget surface gate: verify that performance budget
infrastructure exists with sane values and CI regression
detection is available.

This is a static analysis gate -- it does NOT run performance
tests. It verifies the budget definitions and tooling exist.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

PROFILER_PATH = (
    ROOT / "src" / "VoiceStudio.App" / "Utilities"
    / "PerformanceProfiler.cs"
)
REGRESSION_SCRIPT = ROOT / "scripts" / "detect_performance_regression.py"

REQUIRED_BUDGETS = {
    "StartupMs": (1, 10000),
    "PanelLoadMs": (1, 2000),
    "ApiResponseMs": (1, 5000),
}

BUDGET_PATTERN = re.compile(
    r"public\s+const\s+(?:int|double)\s+(\w+)\s*=\s*([\d.]+)\s*;"
)


def _read_file(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return ""


def _extract_budgets(text: str) -> dict[str, float]:
    return {
        m.group(1): float(m.group(2))
        for m in BUDGET_PATTERN.finditer(text)
    }


def get_performance_budget_results() -> dict:
    """Scan files and return structured results for proof writer."""
    profiler_text = _read_file(PROFILER_PATH)
    detector_text = _read_file(REGRESSION_SCRIPT)

    budgets = _extract_budgets(profiler_text)
    budgets_ok = all(
        name in budgets and lo <= budgets[name] <= hi
        for name, (lo, hi) in REQUIRED_BUDGETS.items()
    )
    ci_mode = bool(re.search(r"--ci", detector_text))

    return {
        "budgets_defined": bool(budgets) and budgets_ok,
        "budgets_values": {
            k: budgets.get(k) for k in REQUIRED_BUDGETS
        },
        "regression_detector_exists": REGRESSION_SCRIPT.exists(),
        "ci_mode_available": ci_mode,
    }


def test_performance_profiler_exists() -> None:
    """PerformanceProfiler.cs must exist."""
    assert PROFILER_PATH.exists(), (
        f"Missing: {PROFILER_PATH.relative_to(ROOT)}"
    )


def test_required_budgets_defined() -> None:
    """All required budget constants must exist with sane values."""
    text = _read_file(PROFILER_PATH)
    budgets = _extract_budgets(text)
    for name, (lo, hi) in REQUIRED_BUDGETS.items():
        assert name in budgets, f"Missing budget: {name}"
        val = budgets[name]
        assert lo <= val <= hi, (
            f"{name}={val} outside sane range [{lo}, {hi}]"
        )


def test_regression_detector_exists() -> None:
    """detect_performance_regression.py must exist with --ci mode."""
    assert REGRESSION_SCRIPT.exists(), (
        f"Missing: {REGRESSION_SCRIPT.relative_to(ROOT)}"
    )
    text = _read_file(REGRESSION_SCRIPT)
    assert re.search(r"--ci", text), (
        "detect_performance_regression.py missing --ci flag"
    )
