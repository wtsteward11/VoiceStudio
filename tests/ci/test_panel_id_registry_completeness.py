"""CI gate: every panel ID referenced by smoke, workspaces, shortcuts, or menus must be registered.

Prevents drift: if a panel ID is used anywhere, it must exist in Core/Advanced/Module or
be explicitly allowed as legacy-only (MiniTimeline).
"""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest
from _panel_registry_utils import extract_registered_panel_ids

pytestmark = [pytest.mark.ci]

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"
PANEL_IDS_CS = ROOT / "src" / "VoiceStudio.Core" / "Panels" / "PanelIds.cs"
MAIN_WINDOW_CS = SRC_APP / "MainWindow.xaml.cs"
WORKSPACES_DIR = SRC_APP / "Resources" / "Workspaces"
ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_legacy_allowlist.json"

REGISTRATION_SERVICES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
    SRC_APP / "Services" / "ModulePanelRegistrationService.cs",
]
LEGACY_KEY_RE = re.compile(r'\["(\w+)"\]\s*=')


def _extract_legacy_allowlist() -> set[str]:
    """Panel IDs explicitly allowed as legacy-only (e.g. MiniTimeline)."""
    if not ALLOWLIST_JSON.exists():
        return set()
    data = json.loads(ALLOWLIST_JSON.read_text(encoding="utf-8"))
    return set(data.get("panel_ids", []))


def _extract_referenced_panel_ids_from_main_window() -> set[str]:
    """Panel IDs referenced in MainWindow: smoke steps, shortcuts, OpenPanelById, constructor."""
    if not MAIN_WINDOW_CS.exists():
        return set()
    text = MAIN_WINDOW_CS.read_text(encoding="utf-8")
    ids: set[str] = set()

    # AssertPanelOpened("PanelId", PanelRegion.X)
    for m in re.finditer(r'AssertPanelOpened\s*\(\s*["\']([^"\']+)["\']', text):
        ids.add(m.group(1))

    # OpenPanelById("PanelId") or OpenPanelById("PanelId", PanelRegion.X)
    for m in re.finditer(r'OpenPanelById\s*\(\s*["\']([^"\']+)["\']', text):
        ids.add(m.group(1))

    # RegisterPanelQuickSwitchShortcut(..., "PanelId")
    for m in re.finditer(r'RegisterPanelQuickSwitchShortcut\s*\([^)]+,\s*["\']([^"\']+)["\']\s*\)', text):
        ids.add(m.group(1))

    return ids


def _extract_referenced_panel_ids_from_workspaces() -> set[str]:
    """Panel IDs from workspace JSON files: activePanelId and openedPanels."""
    ids: set[str] = set()
    if not WORKSPACES_DIR.exists():
        return ids
    for wpath in WORKSPACES_DIR.glob("*.json"):
        try:
            data = json.loads(wpath.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            continue
        layout = data.get("layout") or {}
        for r in layout.get("regions") or []:
            if aid := r.get("activePanelId"):
                ids.add(aid)
            for pid in r.get("openedPanels") or []:
                ids.add(pid)
    return ids


@pytest.fixture(scope="module")
def registered_ids() -> set[str]:
    """All panel IDs from Core, Advanced, Module."""
    return extract_registered_panel_ids(REGISTRATION_SERVICES, PANEL_IDS_CS)


@pytest.fixture(scope="module")
def legacy_allowlist() -> set[str]:
    """Panel IDs allowed as legacy-only."""
    return _extract_legacy_allowlist()


@pytest.fixture(scope="module")
def referenced_ids() -> set[str]:
    """All panel IDs referenced in MainWindow and workspace templates."""
    mw_ids = _extract_referenced_panel_ids_from_main_window()
    ws_ids = _extract_referenced_panel_ids_from_workspaces()
    return mw_ids | ws_ids


def test_all_referenced_panel_ids_are_registered(
    registered_ids: set[str],
    legacy_allowlist: set[str],
    referenced_ids: set[str],
) -> None:
    """Every panel ID used by smoke, workspaces, shortcuts, or menus must be registered or legacy-allowed."""
    valid_ids = registered_ids | legacy_allowlist
    missing = referenced_ids - valid_ids
    assert not missing, (
        f"Panel IDs referenced but not registered: {sorted(missing)}. "
        "Register in CorePanelRegistrationService, AdvancedPanelRegistrationService, or "
        "ModulePanelRegistrationService. Only MiniTimeline may remain legacy-only."
    )
