"""CI gate: enforce UI/UX Architecture Contract.

- Rule A: _legacyPanelRegistry keys must exactly match .ci/ui_arch_legacy_allowlist.json.
- Rule B: new XxxView() outside the frozen registry block must not exceed budget.
  Budget must only shrink (migrate to CreatePanelFromRegistry), never grow.
- Rule C: UI smoke: workspace JSONs must reference registered panels with valid ViewType.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"
MAIN_WINDOW_CS = ROOT / "src" / "VoiceStudio.App" / "MainWindow.xaml.cs"
ALLOWLIST_JSON = ROOT / ".ci" / "ui_arch_legacy_allowlist.json"
WORKSPACES_DIR = SRC_APP / "Resources" / "Workspaces"
PANELS_DIR = SRC_APP / "Views" / "Panels"

REGISTRATION_SERVICES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
    SRC_APP / "Services" / "ModulePanelRegistrationService.cs",
]

# Current count of new XxxView() outside _legacyPanelRegistry block.
# Must only shrink; never increase. Migrate to CreatePanelFromRegistry.
# Reduced to 1 after Phase D (WelcomeView dialog). Budget = actual count + 0 margin.
MAX_LEGACY_VIEW_INSTANTIATIONS = 1


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


def _find_switch_to_panel_line_range(lines: list[str]) -> tuple[int, int] | None:
    """Return (start_line, end_line) 1-based for obsolete SwitchToPanel method body."""
    decl_line = None
    for i, line in enumerate(lines):
        if "void SwitchToPanel(" in line:
            decl_line = i
            break
    if decl_line is None:
        return None
    # Find the method's opening brace (same line or next)
    body_start = decl_line
    for i in range(decl_line, min(decl_line + 3, len(lines))):
        if "{" in lines[i]:
            body_start = i
            break
    depth = 0
    for i in range(body_start, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        if depth == 0:
            return body_start + 1, i + 1
    return body_start + 1, len(lines)


def test_no_direct_content_assignment(main_window_content: str) -> None:
    """Forbid .Content = new XxxView or .Content = panelFactory outside obsolete SwitchToPanel."""
    lines = main_window_content.splitlines()
    switch_range = _find_switch_to_panel_line_range(lines)
    assert switch_range is not None, "Could not find SwitchToPanel method"

    start, end = switch_range
    content_new_pattern = re.compile(r"\.Content\s*=\s*new\s+\w+View\s*\(")
    content_factory_pattern = re.compile(r"\.Content\s*=\s*panelFactory\s*\(\s*\)")

    violations: list[tuple[int, str]] = []
    for i, line in enumerate(lines, 1):
        if content_new_pattern.search(line):
            violations.append((i, f"L{i}: .Content = new XxxView() forbidden"))
        elif content_factory_pattern.search(line) and not (start <= i <= end):
            violations.append((i, f"L{i}: .Content = panelFactory() outside SwitchToPanel"))

    assert not violations, (
        "Direct Content assignment forbidden. Use LoadPanel/LoadPanelAsync. Violations: "
        + "; ".join(v[1] for v in violations)
    )


def test_switchto_panel_not_called(main_window_content: str) -> None:
    """Obsolete SwitchToPanel must have zero callers (method may exist but must not be invoked)."""
    # Match SwitchToPanel( as a call (exclude the definition "void SwitchToPanel(")
    call_pattern = re.compile(r"SwitchToPanel\s*\(")
    def_pattern = re.compile(r"void\s+SwitchToPanel\s*\(")

    callers: list[tuple[int, str]] = []
    for i, line in enumerate(main_window_content.splitlines(), 1):
        if call_pattern.search(line) and not def_pattern.search(line):
            callers.append((i, line.strip()))

    assert not callers, (
        f"Obsolete SwitchToPanel must not be called. Found {len(callers)} caller(s): "
        + "; ".join(f"L{n}: {c[:50]}..." for n, c in callers)
    )


def _panel_id_to_view_type() -> dict[str, str]:
    """Build panel_id -> view_type from all 3 registration services and legacy."""
    result: dict[str, str] = {}
    panel_id_re = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
    view_type_re = re.compile(r'ViewType\s*=\s*typeof\s*\(\s*(\w+)\s*\)')
    for path in REGISTRATION_SERVICES:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        parts = re.split(
            r"(?:RegisterIfNotExists|registry\.Register)\s*\(\s*(?:registry\s*,\s*)?new\s+PanelDescriptor\s*\{",
            text,
        )
        for part in parts[1:]:
            block_end = part.find("});")
            block = part[:block_end] if block_end >= 0 else part
            pid_m = panel_id_re.search(block)
            vt_m = view_type_re.search(block)
            if pid_m and vt_m:
                result[pid_m.group(1)] = vt_m.group(1)
    # Legacy: ["PanelId"] = (..., () => new ViewType())
    mw = MAIN_WINDOW_CS.read_text(encoding="utf-8")
    start = mw.find("_legacyPanelRegistry = new")
    if start >= 0:
        end = mw.find("};", start)
        if end >= 0:
            block = mw[start:end]
            pid_re = re.compile(r'\["(\w+)"\]\s*=')
            view_re = re.compile(r'new\s+(\w+View)\s*\(')
            for pid_m in pid_re.finditer(block):
                # Find the next view in same block after this pid
                rest = block[pid_m.end() :]
                vt_m = view_re.search(rest)
                if vt_m:
                    result[pid_m.group(1)] = vt_m.group(1)
    return result


@pytest.fixture
def panel_id_to_view_type() -> dict[str, str]:
    """Panel ID -> ViewType class name mapping."""
    return _panel_id_to_view_type()


def test_ui_smoke_default_panels_loaded(panel_id_to_view_type: dict[str, str]) -> None:
    """Workspace JSONs must reference registered panels with valid ViewType.
    Asserts actual panel types loaded into regions for >=8 steps (8 workspaces x 4 regions).
    """
    if not WORKSPACES_DIR.exists():
        pytest.skip("Workspaces directory not found")
    workspace_files = list(WORKSPACES_DIR.glob("*.json"))
    assert len(workspace_files) >= 8, (
        f"Expected at least 8 workspace JSONs, found {len(workspace_files)}"
    )
    errors: list[str] = []
    for wpath in sorted(workspace_files):
        try:
            data = json.loads(wpath.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as e:
            errors.append(f"{wpath.name}: {e}")
            continue
        layout = data.get("layout") or {}
        regions = layout.get("regions") or []
        for r in regions:
            aid = r.get("activePanelId")
            if not aid:
                continue
            if aid not in panel_id_to_view_type:
                errors.append(f"{wpath.name} region {r.get('region')}: PanelId '{aid}' not registered")
                continue
            view_type = panel_id_to_view_type[aid]
            xaml = PANELS_DIR / f"{view_type}.xaml"
            if not xaml.exists():
                errors.append(f"{wpath.name} region {r.get('region')}: {aid} -> {view_type}.xaml missing")
    assert not errors, "Workspace panel assertions failed:\n" + "\n".join(errors)
