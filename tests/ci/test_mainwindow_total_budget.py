"""CI gate: total combined size of MainWindow*.cs partials must not exceed budget."""
from __future__ import annotations

from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
MW_DIR = ROOT / "src" / "VoiceStudio.App"
TOTAL_BUDGET = 200_000  # bytes


def test_mainwindow_partials_total_under_budget():
    partials = list(MW_DIR.glob("MainWindow*.cs"))
    assert len(partials) >= 1, "No MainWindow*.cs files found"
    total = sum(p.stat().st_size for p in partials)
    names = ", ".join(p.name for p in sorted(partials))
    assert total <= TOTAL_BUDGET, (
        f"Total MainWindow partials ({names}) = {total} bytes, "
        f"exceeds budget of {TOTAL_BUDGET}. "
        f"Refactor before growing. Do not raise the budget."
    )
