"""CI gate: MainWindow._legacyPanelRegistry must contain exactly one entry: MiniTimeline.

Prevents restore from breaking silently if legacy registry grows again.
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"


def _extract_legacy_panel_ids(text: str) -> list[str]:
    """Extract panel IDs from _legacyPanelRegistry keys via static parse."""
    pattern = re.compile(r'\["(\w+)"\]\s*=')
    return pattern.findall(text)


def test_legacy_registry_contains_only_minitimeline() -> None:
    """_legacyPanelRegistry must have exactly one entry: MiniTimeline."""
    assert MAIN_WINDOW_CS.exists(), f"{MAIN_WINDOW_CS} not found"
    content = MAIN_WINDOW_CS.read_text(encoding="utf-8")

    # Find the legacy block (between _legacyPanelRegistry = new and };)
    start = content.find("_legacyPanelRegistry = new")
    assert start >= 0, "_legacyPanelRegistry block not found"
    block_start = content.find("{", start) + 1
    block_end = content.find("};", block_start)
    assert block_end >= 0, "_legacyPanelRegistry block end not found"
    block_text = content[block_start:block_end]

    ids = _extract_legacy_panel_ids(block_text)
    assert len(ids) == 1, (
        f"_legacyPanelRegistry must contain exactly 1 entry, found {len(ids)}: {ids}. "
        "Do not add panels to legacy registry; register via CorePanelRegistrationService."
    )
    assert ids[0] == "MiniTimeline", (
        f"_legacyPanelRegistry entry must be MiniTimeline, found: {ids[0]}"
    )
