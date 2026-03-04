"""CI gate: god-object files must not exceed size budgets.

Budgets are current size + 5KB allowance. No refactoring in this sprint;
this gate prevents silent growth. To reduce the budget, refactor first.
"""
from __future__ import annotations

from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent

BUDGETS: dict[str, int] = {
    "src/VoiceStudio.App/Services/BackendClient.cs": 191632,
    "src/VoiceStudio.App/MainWindow.xaml.cs": 138981,
}


@pytest.mark.parametrize("rel_path,budget", BUDGETS.items())
def test_file_size_within_budget(rel_path: str, budget: int):
    path = ROOT / rel_path
    assert path.exists(), f"{rel_path} not found"
    size = path.stat().st_size
    assert size <= budget, (
        f"{rel_path} is {size} bytes, exceeds budget of {budget} bytes "
        f"(over by {size - budget}). This is a god-object. "
        f"Split before growing. Do not raise the budget."
    )
