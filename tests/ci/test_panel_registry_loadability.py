"""CI gate: Tool Catalog cannot list panels that cannot be loaded.

Ensures every registered panel descriptor has:
- Non-empty, unique PanelId across all registration services
- ViewType with both .xaml and .xaml.cs under Views/Panels (loadable UserControl)
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"
PANELS_DIR = SRC_APP / "Views" / "Panels"

REGISTRATION_SERVICES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
    SRC_APP / "Services" / "ModulePanelRegistrationService.cs",
]

PANEL_ID_RE = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
VIEW_TYPE_RE = re.compile(r'ViewType\s*=\s*typeof\s*\(\s*(\w+)\s*\)')


def _extract_registered_panels() -> list[dict]:
    """Extract panel_id and view_type from all registration services."""
    panels: list[dict] = []
    for path in REGISTRATION_SERVICES:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        blocks = re.split(
            r"(?:RegisterIfNotExists|registry\.Register)\s*\(\s*(?:registry\s*,\s*)?new\s+PanelDescriptor",
            text,
        )
        for block in blocks[1:]:
            pid = PANEL_ID_RE.search(block)
            vt = VIEW_TYPE_RE.search(block)
            if pid and vt:
                panels.append({
                    "panel_id": pid.group(1),
                    "view_type": vt.group(1),
                })
    return panels


@pytest.fixture(scope="module")
def registered_panels() -> list[dict]:
    """All panels from Core, Advanced, and Module registration."""
    return _extract_registered_panels()


def test_panel_ids_non_empty(registered_panels: list[dict]) -> None:
    """Every PanelDescriptor must have a non-empty PanelId."""
    empty: list[str] = []
    for p in registered_panels:
        if not p["panel_id"] or not p["panel_id"].strip():
            empty.append(f"ViewType={p['view_type']}")
    assert not empty, (
        f"PanelDescriptors with empty PanelId: {empty}. "
        "PanelId must be non-empty."
    )


def test_panel_ids_unique(registered_panels: list[dict]) -> None:
    """PanelId must be unique across all registration services."""
    seen: dict[str, list[str]] = {}
    for p in registered_panels:
        pid = p["panel_id"]
        vt = p["view_type"]
        if pid not in seen:
            seen[pid] = []
        seen[pid].append(vt)

    duplicates = [(pid, vts) for pid, vts in seen.items() if len(vts) > 1]
    assert not duplicates, (
        f"Duplicate PanelIds: {duplicates}. "
        "Each PanelId must be unique across Core, Advanced, and Module."
    )


def test_view_type_loadable(registered_panels: list[dict]) -> None:
    """Each ViewType must have both .xaml and .xaml.cs under Views/Panels (loadable UserControl)."""
    missing: list[str] = []
    for p in registered_panels:
        vt = p["view_type"]
        xaml = PANELS_DIR / f"{vt}.xaml"
        cs = PANELS_DIR / f"{vt}.xaml.cs"
        if not xaml.exists():
            missing.append(f"{p['panel_id']}: {vt}.xaml")
        if not cs.exists():
            missing.append(f"{p['panel_id']}: {vt}.xaml.cs")
    assert not missing, (
        f"Registered panels missing loadable View files: {missing}. "
        "Tool Catalog must not list panels that cannot be loaded. "
        "Create both .xaml and .xaml.cs under Views/Panels or remove the registration."
    )
