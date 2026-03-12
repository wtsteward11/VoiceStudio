#!/usr/bin/env python3
"""
CI guardrail: flag raw ContentDialog or ConfirmationDialog usage in ViewModels.

Per PANEL_HARDENING_PATTERN: ViewModels must use IDialogService for confirmations.
Approved locations: DialogService, ErrorDialogService, ConfirmationDialog (utility),
dedicated dialog classes (e.g. TelemetryConsentDialog).

Scans:
- src/VoiceStudio.App/ViewModels/
- src/VoiceStudio.App/Views/Panels/*ViewModel*.cs

Violations:
- ConfirmationDialog.ShowDeleteConfirmationAsync, ConfirmationDialog.ShowAsync
- new ContentDialog (except in approved paths)

Baseline: .ci/dialog_pattern_baseline.txt lists known violations (path:line).
New violations (not in baseline) fail the check. Reduce baseline as ViewModels migrate.

Exits 0 if clean; 1 if new violations found.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
APP = ROOT / "src" / "VoiceStudio.App"
BASELINE = ROOT / ".ci" / "dialog_pattern_baseline.txt"

# Paths to scan for violations (ViewModels)
SCAN_DIRS = [
    APP / "ViewModels",
]
SCAN_GLOB = "**/*ViewModel*.cs"

# Approved paths (contain dialog implementation, not violations)
APPROVED_PREFIXES = (
    "DialogService",
    "ErrorDialogService",
    "ErrorDialog",
    "ConfirmationDialog",  # The utility class itself
    "TelemetryConsentDialog",
    "WelcomeDialog",
    "UpdateDialog",
)

CONFIRMATION_DIALOG_PATTERN = re.compile(
    r"ConfirmationDialog\.(ShowDeleteConfirmationAsync|ShowAsync)\s*\("
)
CONTENT_DIALOG_PATTERN = re.compile(
    r"new\s+ContentDialog\b"  # match "new ContentDialog" (constructor or initializer)
)


def is_approved(path: Path) -> bool:
    """True if file is an approved dialog implementation."""
    name = path.name
    return any(name.startswith(p) for p in APPROVED_PREFIXES)


def check_file(path: Path) -> list[tuple[int, str, str]]:
    """Return list of (line_no, pattern_desc, line_text) violations."""
    violations = []
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return violations

    for i, line in enumerate(text.splitlines(), 1):
        if CONFIRMATION_DIALOG_PATTERN.search(line):
            violations.append((i, "ConfirmationDialog usage (use IDialogService)", line.strip()))
        if CONTENT_DIALOG_PATTERN.search(line):
            violations.append((i, "raw ContentDialog construction (use IDialogService)", line.strip()))

    return violations


def load_baseline() -> set[str]:
    """Load baseline of allowed violations (path:line)."""
    if not BASELINE.exists():
        return set()
    allowed = set()
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            allowed.add(line)
    return allowed


def main() -> int:
    violations: list[tuple[Path, int, str, str]] = []
    baseline = load_baseline()

    for scan_dir in SCAN_DIRS:
        if not scan_dir.exists():
            continue
        for path in scan_dir.rglob("*.cs"):
            if is_approved(path):
                continue
            for line_no, desc, line_text in check_file(path):
                key = f"{path.relative_to(ROOT).as_posix()}:{line_no}"
                if key not in baseline:
                    violations.append((path, line_no, desc, line_text))

    # Also scan Views/Panels for *ViewModel*.cs
    panels = APP / "Views" / "Panels"
    if panels.exists():
        for path in panels.glob("*ViewModel*.cs"):
            if is_approved(path):
                continue
            for line_no, desc, line_text in check_file(path):
                key = f"{path.relative_to(ROOT).as_posix()}:{line_no}"
                if key not in baseline:
                    violations.append((path, line_no, desc, line_text))

    if violations:
        print("DIALOG_PATTERN_VIOLATION: New violations (ViewModels must use IDialogService)")
        print("Add to .ci/dialog_pattern_baseline.txt to allow during migration, or fix.")
        print()
        for path, line_no, desc, line_text in violations:
            rel = path.relative_to(ROOT)
            print(f"  {rel}:{line_no}: {desc}")
            print(f"    {line_text[:80]}{'...' if len(line_text) > 80 else ''}")
        print()
        print("See docs/developer/PANEL_HARDENING_PATTERN.md")
        return 1

    print("Dialog pattern check: OK (ViewModels use IDialogService)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
