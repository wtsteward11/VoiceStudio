"""CI gate: forbid Views constructing root ViewModels directly.

Views must receive ViewModels via DI/registry (DataContext from PanelRegistry).
Child/item ViewModels (e.g. BenchmarkResultViewModel, ToolbarItemViewModel) used
in collections are allowed.

Fails when a View assigns ViewModel/_viewModel/DataContext = new XxxViewModel(...).
Uses baseline for known tech-debt; fails on NEW violations.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
VIEWS_DIR = ROOT / "src" / "VoiceStudio.App" / "Views"
BASELINE_JSON = ROOT / ".ci" / "view_model_construction_baseline.json"

# Child/item ViewModels used in collections, not as root panel VM.
CHILD_VM_ALLOWLIST = frozenset({
    "BenchmarkResultViewModel",
    "ToolbarItemViewModel",
    "ErrorLogEntryViewModel",
    "BudgetViolationViewModel",
    "AuditEntryViewModel",
    "FeatureFlagViewModel",
})

# Pattern: assignment to ViewModel, _viewModel, or DataContext = new XxxViewModel(
ROOT_VM_PATTERN = re.compile(
    r"(?:ViewModel|_viewModel|DataContext)\s*=\s*new\s+(\w+ViewModel)\s*\(",
    re.MULTILINE,
)


def _find_violations() -> list[tuple[str, str, int]]:
    """Return [(rel_path, vm_name, line_num), ...] for root VM construction."""
    violations: list[tuple[str, str, int]] = []
    for path in VIEWS_DIR.rglob("*.xaml.cs"):
        if not path.is_file():
            continue
        try:
            text = path.read_text(encoding="utf-8-sig")
        except OSError:
            continue
        rel = path.relative_to(ROOT).as_posix()
        for m in ROOT_VM_PATTERN.finditer(text):
            vm_name = m.group(1)
            if vm_name in CHILD_VM_ALLOWLIST:
                continue
            line_num = text[: m.start()].count("\n") + 1
            violations.append((rel, vm_name, line_num))
    return violations


def _load_baseline() -> set[str]:
    """Load known tech-debt file paths from baseline."""
    if not BASELINE_JSON.exists():
        return set()
    try:
        data = json.loads(BASELINE_JSON.read_text(encoding="utf-8"))
        return set(data.get("violating_files") or [])
    except (json.JSONDecodeError, OSError):
        return set()


def test_no_new_view_model_construction_violations() -> None:
    """Fail if any View constructs root ViewModel directly (except baseline)."""
    violations = _find_violations()
    baseline = _load_baseline()

    # Group by file
    violating_files: set[str] = set()
    for rel_path, _vm, _line in violations:
        violating_files.add(rel_path)

    new_violations = violating_files - baseline

    if new_violations:
        lines = sorted(new_violations)
        pytest.fail(
            f"NEW ViewModel construction violations ({len(new_violations)} files). "
            f"Views must use DI/registry. Violations:\n  " + "\n  ".join(lines)
        )
