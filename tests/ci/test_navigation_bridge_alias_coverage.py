"""CI gate: NavigationBridge NormalizeToCanonicalId mappings must resolve to registered panels.

If an alias maps to a canonical ID that doesn't exist in the registry, navigation silently fails.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

ROOT = Path(__file__).resolve().parent.parent.parent
NAVIGATION_BRIDGE_CS = ROOT / "src" / "VoiceStudio.App" / "Services" / "NavigationBridge.cs"
SRC_APP = ROOT / "src" / "VoiceStudio.App"
ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_legacy_allowlist.json"

REGISTRATION_SERVICES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
    SRC_APP / "Services" / "ModulePanelRegistrationService.cs",
]

PANEL_ID_RE = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
CANONICAL_ID_RE = re.compile(r'=>\s*["\']([^"\']+)["\']')


def _extract_registered_panel_ids() -> set[str]:
    """Panel IDs from Core, Advanced, Module registration services."""
    ids: set[str] = set()
    for path in REGISTRATION_SERVICES:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        for m in PANEL_ID_RE.finditer(text):
            ids.add(m.group(1))
    return ids


def _extract_legacy_allowlist() -> set[str]:
    """Panel IDs explicitly allowed as legacy-only."""
    if not ALLOWLIST_JSON.exists():
        return set()
    import json
    data = json.loads(ALLOWLIST_JSON.read_text(encoding="utf-8"))
    return set(data.get("panel_ids", []))


def _extract_canonical_ids_from_normalize() -> set[str]:
    """Canonical IDs from NormalizeToCanonicalId switch (explicit mappings only, not _ => panelId)."""
    if not NAVIGATION_BRIDGE_CS.exists():
        return set()
    text = NAVIGATION_BRIDGE_CS.read_text(encoding="utf-8-sig")
    start = text.find("NormalizeToCanonicalId")
    if start < 0:
        return set()
    start = text.find("=>", start)
    if start < 0:
        return set()
    end = text.find("};", start)
    if end < 0:
        return set()
    block = text[start:end]
    ids: set[str] = set()
    for m in CANONICAL_ID_RE.finditer(block):
        canonical = m.group(1)
        if canonical != "panelId":
            ids.add(canonical)
    return ids


@pytest.fixture(scope="module")
def registered_ids() -> set[str]:
    return _extract_registered_panel_ids()


@pytest.fixture(scope="module")
def legacy_allowlist() -> set[str]:
    return _extract_legacy_allowlist()


@pytest.fixture(scope="module")
def canonical_ids() -> set[str]:
    return _extract_canonical_ids_from_normalize()


def test_navigation_bridge_canonical_ids_are_registered(
    registered_ids: set[str],
    legacy_allowlist: set[str],
    canonical_ids: set[str],
) -> None:
    """Every canonical ID in NormalizeToCanonicalId must be registered or legacy-allowed."""
    valid_ids = registered_ids | legacy_allowlist
    missing = canonical_ids - valid_ids
    assert not missing, (
        f"NavigationBridge NormalizeToCanonicalId maps to unregistered IDs: {sorted(missing)}. "
        "Register in Core/Advanced/Module or add to legacy allowlist."
    )
