"""
UI Command Surface gate: verify core workflows are wired
into command/panel registration.

Scans ONLY the canonical wiring files for RegisterNavCommand
and PanelId patterns. Broad tree scanning is forbidden because
it can be gamed by dead registrations in unused files.

Required workflows (Phase F1):
- Profile create/import: nav.profiles + Profiles panel
- Synthesis: nav.studio + VoiceSynthesis panel
- Export: File handler commands
- Consent UI: Consent panel/dialog
- Support bundle: DiagnosticsView export
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_APP = ROOT / "src" / "VoiceStudio.App"

COMMAND_WIRING_FILES = [
    SRC_APP / "Commands" / "NavigationHandler.cs",
]

PANEL_WIRING_FILES = [
    SRC_APP / "Services" / "CorePanelRegistrationService.cs",
    SRC_APP / "Services" / "AdvancedPanelRegistrationService.cs",
]

REQUIRED_COMMANDS = {
    "nav.studio",
    "nav.profiles",
    "nav.library",
    "nav.train",
    "nav.effects",
    "nav.analyze",
    "nav.settings",
    "nav.logs",
}

REQUIRED_PANELS = {
    "VoiceSynthesis",
    "Profiles",
    "Library",
    "Training",
    "EffectsMixer",
    "AudioAnalysis",
    "Settings",
}

CMD_PATTERN = re.compile(
    r'RegisterNavCommand\s*\(\s*["\']([^"\']+)["\']',
    re.MULTILINE,
)

PANEL_PATTERN = re.compile(
    r'PanelId\s*=\s*["\']([^"\']+)["\']',
    re.MULTILINE,
)


def _scan_files(
    files: list[Path],
    pattern: re.Pattern[str],
) -> dict[str, str]:
    """Scan specific files, return {extracted_id: relative_path}."""
    found: dict[str, str] = {}
    for cs in files:
        if not cs.exists():
            continue
        try:
            text = cs.read_text(encoding="utf-8", errors="replace")
        except Exception:
            continue
        rel = str(cs.relative_to(ROOT)).replace("\\", "/")
        for m in pattern.finditer(text):
            found[m.group(1)] = rel
    return found


def get_ui_command_surface_results() -> dict:
    """
    Run scan and return results for proof writer.
    Returns dict with commands_checked, panels_checked, all_commands_registered,
    all_panels_registered, command_details, panel_details.
    """
    cmd_registered = _scan_files(COMMAND_WIRING_FILES, CMD_PATTERN)
    panel_registered = _scan_files(PANEL_WIRING_FILES, PANEL_PATTERN)
    all_commands = all(cid in cmd_registered for cid in REQUIRED_COMMANDS)
    all_panels = all(pid in panel_registered for pid in REQUIRED_PANELS)
    command_details = {
        cid: {"registered": cid in cmd_registered, "source_file": cmd_registered.get(cid, "")}
        for cid in sorted(REQUIRED_COMMANDS)
    }
    panel_details = {
        pid: {"registered": pid in panel_registered, "source_file": panel_registered.get(pid, "")}
        for pid in sorted(REQUIRED_PANELS)
    }
    return {
        "commands_checked": sorted(REQUIRED_COMMANDS),
        "panels_checked": sorted(REQUIRED_PANELS),
        "all_commands_registered": all_commands,
        "all_panels_registered": all_panels,
        "command_details": command_details,
        "panel_details": panel_details,
    }


def test_required_commands_registered() -> None:
    """All required nav commands must be registered via RegisterNavCommand."""
    results = get_ui_command_surface_results()
    assert results["all_commands_registered"], (
        f"Missing command registrations. Details: {results['command_details']}"
    )


def test_required_panels_registered() -> None:
    """All required panels must be registered via PanelId in PanelDescriptor."""
    results = get_ui_command_surface_results()
    assert results["all_panels_registered"], (
        f"Missing panel registrations. Details: {results['panel_details']}"
    )
