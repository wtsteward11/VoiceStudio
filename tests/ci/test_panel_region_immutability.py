"""CI gate: PanelRegion must only be assigned in MainWindow constructor.

Runtime reassignment of PanelRegion breaks fixed physical region identity
and causes OpenPanelByIdAsync to route to the wrong host.
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"

# PanelRegion assignments are only allowed in the MainWindow constructor.
CONSTRUCTOR_START_MARKER = "public MainWindow()"


def _find_constructor_range(content: str) -> tuple[int, int] | None:
    """Find the line range of the MainWindow constructor."""
    idx = content.find(CONSTRUCTOR_START_MARKER)
    if idx < 0:
        return None
    start_line = content[:idx].count("\n") + 1
    brace_idx = content.find("{", idx)
    if brace_idx < 0:
        return None
    depth = 1
    pos = brace_idx
    for i, ch in enumerate(content[brace_idx + 1 :], start=brace_idx + 1):
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                pos = i
                break
    end_line = content[: pos + 1].count("\n") + 1
    return (start_line, end_line)


def _find_panel_region_assignments(content: str) -> list[tuple[int, str]]:
    """Find all .PanelRegion = assignments, returning (line_number, line_text)."""
    pattern = re.compile(r"\.PanelRegion\s*=")
    results: list[tuple[int, str]] = []
    for i, line in enumerate(content.splitlines(), start=1):
        if pattern.search(line):
            results.append((i, line.strip()))
    return results


def test_panel_region_assignments_only_in_constructor() -> None:
    """PanelRegion must only be assigned in MainWindow constructor initialization."""
    if not MAIN_WINDOW_CS.exists():
        pytest.skip(f"MainWindow.xaml.cs not found: {MAIN_WINDOW_CS}")

    content = MAIN_WINDOW_CS.read_text(encoding="utf-8-sig")
    constructor_range = _find_constructor_range(content)
    assert constructor_range is not None, "MainWindow constructor not found"

    start_line, end_line = constructor_range
    assignments = _find_panel_region_assignments(content)

    violations: list[str] = []
    for line_no, line_text in assignments:
        if not (start_line <= line_no <= end_line):
            violations.append(
                f"Line {line_no}: .PanelRegion = outside constructor (lines {start_line}-{end_line}): {line_text}"
            )

    assert not violations, (
        "PanelRegion must only be assigned in MainWindow constructor. "
        "Runtime reassignment breaks fixed physical region identity.\n"
        + "\n".join(violations)
    )
