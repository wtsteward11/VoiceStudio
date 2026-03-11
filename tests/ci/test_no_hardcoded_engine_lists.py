"""
CI gate: fail if ViewModels contain hardcoded engine ID arrays.

Engine lists must come from /api/engines/list. Patterns that indicate
hardcoded engine lists:
- new[] { "xtts", "chatterbox", ...
- new List<string> { "xtts", ...
- new ObservableCollection<string> { "xtts", ...
- new() { "xtts_v2", "chatterbox", "tortoise" }
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
VM_DIR = ROOT / "src" / "VoiceStudio.App"
VM_PATHS = [
    VM_DIR / "ViewModels",
    VM_DIR / "Views" / "Panels",
]

# Patterns that indicate hardcoded engine list (engine IDs in array literal)
ENGINE_LIST_PATTERNS = [
    re.compile(r'new\s*\[\s*\]\s*\{\s*["\']xtts'),
    re.compile(r'new\s+List<string>\s*\{\s*["\']xtts'),
    re.compile(r'new\s+ObservableCollection<string>\s*\{\s*["\']xtts'),
    re.compile(r'new\s*\(\)\s*\{\s*["\']xtts'),
    re.compile(r'new\s*\(\)\s*\{\s*["\']xtts_v2'),
    re.compile(r'\.Add\s*\(\s*["\']xtts["\']\s*\)\s*;\s*\n\s*\.Add\s*\(\s*["\']chatterbox'),
    re.compile(r'foreach\s*\(\s*var\s+engine\s+in\s+new\s*\[\s*\]\s*\{\s*["\'](?:svd|deforum|fomm)'),
]

# Files allowlisted (contain "xtts" etc. for other reasons: default value, asset type, etc.)
ALLOWLIST = frozenset({
    "Training.cs",  # default Engine = "xtts"
    "VoiceSynthesisRequest.cs",
    "SettingsData.cs",
    "SettingsViewModel.cs",
    "LibraryViewModel.cs",
    "LibraryView.xaml.cs",
    "QualityPipelineModels.cs",
    "MultiEngineEnsemble.cs",
    "BatchQueueTimelineControl.xaml.cs",
    "EngineParameterTuningViewModel.cs",  # sample engine for UI
    "VideoGenViewModel.cs",  # video engines - uses /api/video/engines/list
})


def _scan_hardcoded_engine_lists() -> list[tuple[str, int, str]]:
    """Return [(file, line, matched_line), ...] for violations."""
    violations: list[tuple[str, int, str]] = []
    for base in VM_PATHS:
        if not base.exists():
            continue
        for cs in base.rglob("*.cs"):
            name = cs.name
            if name in ALLOWLIST:
                continue
            try:
                text = cs.read_text(encoding="utf-8", errors="replace")
            except Exception:
                continue
            rel = str(cs.relative_to(ROOT)).replace("\\", "/")
            for i, line in enumerate(text.splitlines(), 1):
                for pat in ENGINE_LIST_PATTERNS:
                    if pat.search(line):
                        violations.append((rel, i, line.strip()[:100]))
                        break
    return violations


def test_no_hardcoded_engine_lists_in_viewmodels() -> None:
    """Fail if ViewModels contain hardcoded engine ID arrays."""
    violations = _scan_hardcoded_engine_lists()
    assert not violations, (
        "ViewModels must not contain hardcoded engine lists. "
        "Use /api/engines/list via BackendClient.GetEnginesAsync().\n"
        + "\n".join(f"  {f}:{ln}: {s}" for f, ln, s in violations[:15])
        + ("\n  ..." if len(violations) > 15 else "")
    )
