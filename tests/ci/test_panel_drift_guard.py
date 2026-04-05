"""CI gate: make panel registration drift impossible.

- Rule A: Every View in Views/Panels must be registered or explicitly excluded.
- Rule B: Every PanelDescriptor must have MenuCategory and Maturity.
- Rule C: Default workspaces must not reference non-existent PanelIds.
- Rule D: new XxxView() outside legacy block must be in frozen allowlist.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest
from _panel_registry_utils import (
    extract_panel_ids_and_view_types,
    extract_registered_panel_ids,
)

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"
PANEL_IDS_CS = ROOT / "src" / "VoiceStudio.Core" / "Panels" / "PanelIds.cs"
PANELS_DIR = SRC_APP / "Views" / "Panels"
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"
WORKSPACES_DIR = SRC_APP / "Resources" / "Workspaces"
INSTANTIATION_ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_view_instantiation_allowlist.json"
LEGACY_ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_legacy_allowlist.json"

REGISTRATION_SERVICES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
    SRC_APP / "Services" / "ModulePanelRegistrationService.cs",
]

# View files that are NOT standalone panels (helpers, dialogs, sub-components).
# Use the part before "View" (e.g. MiniTimelineView -> MiniTimeline).
EXCLUDED_VIEWS = frozenset({
    "MiniTimeline", "KeyboardShortcuts", "AdvancedSearch",
    "AdvancedRealTimeVisualization", "AdvancedSpectrogramVisualization",
    "AdvancedWaveformVisualization", "AnalyticsDashboard",
    "AudioMonitoringDashboard", "EmotionStylePresetEditor",
    "EngineParameterTuning", "EngineRecommendation",
    "ImageVideoEnhancementPipeline", "Lexicon", "MarkerManager",
    "MixAssistant", "PipelineConversation", "PluginDetail",
    "PluginHealthDashboard", "ProfileHealthDashboard",
    "SLODashboard", "SpatialStage", "StyleTransfer",
    "TagOrganization", "TextBasedSpeechEditor", "TextHighlighting",
    "TrainingQualityVisualization", "UltimateDashboard",
    "VoiceBrowser", "Assistant", "Welcome",
})


def _extract_registered_panel_ids_and_view_types() -> tuple[set[str], set[str]]:
    """Extract (panel_ids, view_type_names) from all 3 registration services."""
    return extract_panel_ids_and_view_types(REGISTRATION_SERVICES, PANEL_IDS_CS)


def _extract_legacy_panel_ids_and_view_types(main_window_content: str) -> tuple[set[str], set[str]]:
    """Extract (panel_ids, view_type_names) from _legacyPanelRegistry block."""
    panel_ids: set[str] = set()
    view_types: set[str] = set()
    pid_re = re.compile(r'\["(\w+)"\]\s*=')
    view_re = re.compile(r'new\s+(\w+View)\s*\(')
    start = main_window_content.find("_legacyPanelRegistry = new")
    if start < 0:
        return panel_ids, view_types
    end = main_window_content.find("};", start)
    if end < 0:
        return panel_ids, view_types
    block = main_window_content[start:end]
    for m in pid_re.finditer(block):
        panel_ids.add(m.group(1))
    for m in view_re.finditer(block):
        view_types.add(m.group(1))
    return panel_ids, view_types


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


def _extract_descriptor_blocks(path: Path) -> list[dict]:
    """Extract PanelDescriptor blocks with PanelId, MenuCategory, Maturity."""
    if not path.exists():
        return []
    text = path.read_text(encoding="utf-8-sig")
    blocks: list[dict] = []
    # Split by registration pattern; each part after first is a block body
    parts = re.split(
        r"(?:RegisterIfNotExists|registry\.Register)\s*\(\s*(?:registry\s*,\s*)?new\s+PanelDescriptor\s*\{",
        text,
    )
    panel_id_re = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
    menu_cat_re = re.compile(r'MenuCategory\s*=\s*["\']([^"\']+)["\']')
    maturity_re = re.compile(r'Maturity\s*=\s*PanelMaturity\.(\w+)')
    for part in parts[1:]:
        block_end = part.find("});")
        if block_end >= 0:
            part = part[:block_end]
        pid_m = panel_id_re.search(part)
        if not pid_m:
            continue
        blocks.append({
            "panel_id": pid_m.group(1),
            "has_menu_category": menu_cat_re.search(part) is not None,
            "has_maturity": maturity_re.search(part) is not None,
        })
    return blocks


def _extract_workspace_panel_ids() -> dict[str, set[str]]:
    """Extract all panel IDs referenced in each workspace JSON. Returns {filename: {panel_ids}}."""
    result: dict[str, set[str]] = {}
    if not WORKSPACES_DIR.exists():
        return result
    for jpath in WORKSPACES_DIR.glob("*.json"):
        try:
            data = json.loads(jpath.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            continue
        ids: set[str] = set()
        layout = data.get("layout") or {}
        regions = layout.get("regions") or []
        for r in regions:
            aid = r.get("activePanelId")
            if aid:
                ids.add(aid)
            for pid in r.get("openedPanels") or []:
                ids.add(pid)
        result[jpath.name] = ids
    return result


@pytest.fixture
def main_window_content() -> str:
    """MainWindow.xaml.cs content."""
    assert MAIN_WINDOW_CS.exists(), f"{MAIN_WINDOW_CS} not found"
    return MAIN_WINDOW_CS.read_text(encoding="utf-8")


@pytest.fixture
def all_valid_panel_ids(main_window_content: str) -> set[str]:
    """Union of registered + legacy panel IDs."""
    reg_ids, _ = _extract_registered_panel_ids_and_view_types()
    leg_ids, _ = _extract_legacy_panel_ids_and_view_types(main_window_content)
    if LEGACY_ALLOWLIST_JSON.exists():
        data = json.loads(LEGACY_ALLOWLIST_JSON.read_text(encoding="utf-8"))
        leg_ids = leg_ids | set(data.get("panel_ids") or [])
    return reg_ids | leg_ids


def test_no_unregistered_views(main_window_content: str) -> None:
    """Every View in Views/Panels must be registered, in legacy, or explicitly excluded."""
    reg_ids, reg_view_types = _extract_registered_panel_ids_and_view_types()
    _, leg_view_types = _extract_legacy_panel_ids_and_view_types(main_window_content)
    known_view_types = reg_view_types | leg_view_types

    unregistered: list[str] = []
    for xaml in PANELS_DIR.glob("*View.xaml"):
        stem = xaml.stem  # e.g. VoiceSynthesisView
        if not stem.endswith("View"):
            continue
        base = stem[:-4]  # VoiceSynthesis
        if base in EXCLUDED_VIEWS:
            continue
        if stem not in known_view_types:
            unregistered.append(f"{stem} ({xaml.name})")

    assert not unregistered, (
        f"Views in Views/Panels not registered and not excluded: {sorted(unregistered)}. "
        "Register via Core/Advanced/ModulePanelRegistrationService or add to EXCLUDED_VIEWS."
    )


def test_all_descriptors_have_category_and_maturity() -> None:
    """Every PanelDescriptor must have MenuCategory and Maturity."""
    missing: list[str] = []
    for path in REGISTRATION_SERVICES:
        for block in _extract_descriptor_blocks(path):
            pid = block["panel_id"]
            if not block["has_menu_category"]:
                missing.append(f"{pid}: missing MenuCategory")
            if not block["has_maturity"]:
                missing.append(f"{pid}: missing Maturity")

    assert not missing, (
        f"PanelDescriptors missing Category or Maturity: {missing}. "
        "Add MenuCategory and Maturity to every PanelDescriptor."
    )


def test_workspace_json_panel_ids_exist(all_valid_panel_ids: set[str]) -> None:
    """Default workspace JSONs must not reference non-existent PanelIds."""
    workspace_ids = _extract_workspace_panel_ids()
    invalid: list[tuple[str, str]] = []
    for filename, ids in workspace_ids.items():
        for pid in ids:
            if pid not in all_valid_panel_ids:
                invalid.append((filename, pid))

    assert not invalid, (
        f"Workspace JSONs reference non-existent PanelIds: "
        f"{[(f, p) for f, p in sorted(invalid)]}. "
        "Register the panel or fix the workspace JSON."
    )


def test_new_view_instantiation_allowlist(main_window_content: str) -> None:
    """new XxxView() outside _legacyPanelRegistry must be in frozen allowlist."""
    assert INSTANTIATION_ALLOWLIST_JSON.exists(), (
        f"{INSTANTIATION_ALLOWLIST_JSON} not found. "
        "Create it with the frozen list of View class names allowed outside legacy block."
    )
    data = json.loads(INSTANTIATION_ALLOWLIST_JSON.read_text(encoding="utf-8"))
    allowlist = set(data.get("view_class_names") or [])

    lines = main_window_content.splitlines()
    start, end = _find_legacy_block_boundaries(lines)
    assert start is not None and end is not None, "Could not find _legacyPanelRegistry block"

    pattern = re.compile(r"new\s+(\w+View)\s*\(")
    outside: list[tuple[int, str]] = []
    for i, line in enumerate(lines, 1):
        if start <= i <= end:
            continue
        m = pattern.search(line)
        if m:
            outside.append((i, m.group(1)))

    disallowed = [(ln, vc) for ln, vc in outside if vc not in allowlist]
    assert not disallowed, (
        f"new XxxView() outside legacy block not in allowlist: {disallowed}. "
        f"Update {INSTANTIATION_ALLOWLIST_JSON} or migrate to CreatePanelFromRegistry."
    )
