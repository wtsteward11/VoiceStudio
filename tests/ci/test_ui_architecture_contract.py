"""CI gate: enforce UI/UX Architecture Contract.

- Rule A: _legacyPanelRegistry keys must exactly match .ci/ui_arch_legacy_allowlist.json.
- Rule B: new XxxView() outside the frozen registry block must not exceed budget.
  Budget must only shrink (migrate to CreatePanelFromRegistry), never grow.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"
ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_legacy_allowlist.json"

# Current count of new XxxView() outside _legacyPanelRegistry block (lines 137-197).
# Must only shrink; never increase. Migrate to CreatePanelFromRegistry.
MAX_LEGACY_VIEW_INSTANTIATIONS = 126


def _find_legacy_block_boundaries(lines: list[str]) -> tuple[int | None, int | None]:
    """Return (start_line, end_line) 1-based for _legacyPanelRegistry block."""
    start = end = None
    for i, line in enumerate(lines, 1):
        if "_legacyPanelRegistry = new" in line:
            start = i
            break
    for i, line in enumerate(lines, 1):
        if start and i > start and line.strip() == "};":
            end = i
            break
    return start, end


def _extract_legacy_panel_ids(text: str) -> set[str]:
    """Extract panel IDs from _legacyPanelRegistry keys."""
    # Match ["PanelId"] = ... between declaration and };
    pattern = re.compile(r'\["(\w+)"\]\s*=')
    return set(pattern.findall(text))


def _get_legacy_block_text(lines: list[str], start: int, end: int) -> str:
    """Return text of the legacy block for ID extraction."""
    return "\n".join(lines[start - 1 : end])


@pytest.fixture
def main_window_content() -> str:
    """MainWindow.xaml.cs content."""
    assert MAIN_WINDOW_CS.exists(), f"{MAIN_WINDOW_CS} not found"
    return MAIN_WINDOW_CS.read_text(encoding="utf-8")


@pytest.fixture
def allowlist_panel_ids() -> set[str]:
    """Panel IDs from frozen allowlist."""
    assert ALLOWLIST_JSON.exists(), f"{ALLOWLIST_JSON} not found"
    data = json.loads(ALLOWLIST_JSON.read_text(encoding="utf-8"))
    return set(data["panel_ids"])


def test_legacy_panel_registry_matches_allowlist(
    main_window_content: str, allowlist_panel_ids: set[str]
) -> None:
    """_legacyPanelRegistry keys must exactly match .ci/ui_arch_legacy_allowlist.json."""
    lines = main_window_content.splitlines()
    start, end = _find_legacy_block_boundaries(lines)
    assert start is not None and end is not None, "Could not find _legacyPanelRegistry block"
    block_text = _get_legacy_block_text(lines, start, end)
    registry_ids = _extract_legacy_panel_ids(block_text)
    added = registry_ids - allowlist_panel_ids
    removed = allowlist_panel_ids - registry_ids
    assert not added, (
        f"_legacyPanelRegistry has {len(added)} panel IDs not in allowlist: {sorted(added)}. "
        "Do not add new panels to legacy registry; register via CorePanelRegistrationService."
    )
    assert not removed, (
        f"_legacyPanelRegistry is missing {len(removed)} panel IDs from allowlist: {sorted(removed)}. "
        "Update .ci/ui_arch_legacy_allowlist.json only when removing migrated panels."
    )


def test_no_new_view_outside_legacy_registry(main_window_content: str) -> None:
    """new XxxView() outside _legacyPanelRegistry block must not exceed budget."""
    lines = main_window_content.splitlines()
    start, end = _find_legacy_block_boundaries(lines)
    assert start is not None and end is not None, "Could not find _legacyPanelRegistry block"
    pattern = re.compile(r"new\s+\w+View\s*\(")
    outside_matches: list[tuple[int, str]] = []
    for i, line in enumerate(lines, 1):
        if start <= i <= end:
            continue
        if pattern.search(line):
            outside_matches.append((i, line.strip()))
    count = len(outside_matches)
    assert count <= MAX_LEGACY_VIEW_INSTANTIATIONS, (
        f"Found {count} new XxxView() outside _legacyPanelRegistry (lines {start}-{end}), "
        f"exceeds budget of {MAX_LEGACY_VIEW_INSTANTIATIONS}. "
        "Migrate to CreatePanelFromRegistry. First 10: "
        + "; ".join(f"L{n}: {c[:60]}..." for n, c in outside_matches[:10])
    )
