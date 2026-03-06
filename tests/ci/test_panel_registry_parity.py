"""
Panel Registry Parity CI Gate.

Ensures all registered panels have corresponding View XAML and ViewModel CS files.
A panel registered without a View or ViewModel is a phantom entry.

FCM-009 item 11: Add panel parity test.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"
PANELS_DIR = SRC_APP / "Views" / "Panels"
VIEWMODELS_DIR = SRC_APP / "ViewModels"

PANEL_WIRING_FILES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
]

PANEL_ID_RE = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
VIEW_TYPE_RE = re.compile(r'ViewType\s*=\s*typeof\s*\(\s*(\w+)\s*\)')
VIEWMODEL_TYPE_RE = re.compile(r'ViewModelType\s*=\s*typeof\s*\(\s*(\w+)\s*\)')


def _extract_registered_panels() -> list[dict]:
    """Extract panel_id, view_type, view_model_type from registration services."""
    panels: list[dict] = []
    for path in PANEL_WIRING_FILES:
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
            vmt = VIEWMODEL_TYPE_RE.search(block)
            if pid and vt and vmt:
                panels.append({
                    "panel_id": pid.group(1),
                    "view_type": vt.group(1),
                    "view_model_type": vmt.group(1),
                })
    return panels


@pytest.fixture(scope="module")
def registered_panels() -> list[dict]:
    """All panels from Core and Advanced registration."""
    return _extract_registered_panels()


def test_registered_panels_have_view_xaml(registered_panels: list[dict]) -> None:
    """Each registered panel must have a View XAML file under Views/Panels/."""
    missing: list[str] = []
    for p in registered_panels:
        xaml = PANELS_DIR / f"{p['view_type']}.xaml"
        if not xaml.exists():
            missing.append(f"{p['panel_id']}: {p['view_type']}.xaml")
    assert not missing, (
        f"Registered panels missing View XAML: {missing}. "
        "Create the XAML file or remove the dead registration."
    )


def test_registered_panels_have_viewmodel_cs(registered_panels: list[dict]) -> None:
    """Each registered panel must have a ViewModel CS file under ViewModels/ or Views/Panels/."""
    missing: list[str] = []
    for p in registered_panels:
        vm_cs = VIEWMODELS_DIR / f"{p['view_model_type']}.cs"
        vm_panels = PANELS_DIR / f"{p['view_model_type']}.cs"
        if not vm_cs.exists() and not vm_panels.exists():
            missing.append(f"{p['panel_id']}: {p['view_model_type']}.cs")
    assert not missing, (
        f"Registered panels missing ViewModel CS: {missing}. "
        "Create the ViewModel file or remove the dead registration."
    )
