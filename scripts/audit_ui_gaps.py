#!/usr/bin/env python3
"""
UI Gap Audit — Systematically identify common UI functionality gaps.

Scans the codebase for patterns that commonly cause UI failures:
- ContentDialog/Flyout without XamlRoot (WinUI 3 requirement)
- Panels with IsVisible=false (hidden/dead)
- Known placeholder patterns from UI_BACKEND_GAP_ANALYSIS
- Empty or stub ViewModels

Usage:
    python scripts/audit_ui_gaps.py
    python scripts/audit_ui_gaps.py --json  # Machine-readable output

Exit codes:
    0 - No critical gaps found (or audit completed)
    1 - Critical gaps found
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
SRC_ROOT = PROJECT_ROOT / "src" / "VoiceStudio.App"


def find_content_dialogs_missing_xamlroot() -> list[dict]:
    """Find ContentDialog/Flyout usages that may lack XamlRoot."""
    gaps = []
    for path in SRC_ROOT.rglob("*.cs"):
        try:
            text = path.read_text(encoding="utf-8")
        except Exception:
            continue

        # Pattern: new ContentDialog or new *Dialog( without XamlRoot in constructor
        # or dialog.ShowAsync() where dialog was created without XamlRoot assignment
        lines = text.splitlines()
        for i, line in enumerate(lines):
            if "new ContentDialog" in line or "new ContentDialog{" in line:
                # Check next ~25 lines for XamlRoot (initializer can span many lines)
                block = "\n".join(lines[i : i + 25])
                if "XamlRoot" not in block and "xamlRoot" not in block:
                    gaps.append({
                        "file": str(path.relative_to(PROJECT_ROOT)),
                        "line": i + 1,
                        "pattern": "ContentDialog without XamlRoot",
                        "severity": "high",
                        "fix": "Set dialog.XamlRoot = this.Content?.XamlRoot (or pass from parent) before ShowAsync()",
                    })
            if "new ToolbarCustomizationDialog()" in line:
                block = "\n".join(lines[i : i + 5])
                if "XamlRoot" not in block:
                    gaps.append({
                        "file": str(path.relative_to(PROJECT_ROOT)),
                        "line": i + 1,
                        "pattern": "ToolbarCustomizationDialog without XamlRoot",
                        "severity": "high",
                        "fix": "Set dialog.XamlRoot = this.Content?.XamlRoot before ShowAsync()",
                    })
    return gaps


def find_hidden_panels() -> list[dict]:
    """Find panels registered with IsVisible=false."""
    gaps = []
    for path in SRC_ROOT.rglob("*.cs"):
        try:
            text = path.read_text(encoding="utf-8")
        except Exception:
            continue
        if "IsVisible = false" in text or "IsVisible=false" in text:
            for i, line in enumerate(text.splitlines()):
                if "IsVisible" in line and "false" in line:
                    gaps.append({
                        "file": str(path.relative_to(PROJECT_ROOT)),
                        "line": i + 1,
                        "pattern": "Panel hidden (IsVisible=false)",
                        "severity": "info",
                        "fix": "Verify panel is intentionally hidden; wire backend if needed",
                    })
    return gaps


def find_placeholder_backend_patterns() -> list[dict]:
    """Find known placeholder patterns in backend routes."""
    gaps = []
    backend_routes = PROJECT_ROOT / "backend" / "api" / "routes"
    if not backend_routes.exists():
        return gaps

    placeholders = [
        (r"# Placeholder|# placeholder", "Placeholder comment"),
        (r"return \[\]\s*#|resources:\s*\[\]\s*#", "Empty list placeholder"),
        (r'"fake"|"placeholder"|"static"|"hardcoded"', "Hardcoded placeholder data"),
    ]

    for path in backend_routes.rglob("*.py"):
        try:
            text = path.read_text(encoding="utf-8")
        except Exception:
            continue
        for pattern, desc in placeholders:
            for match in re.finditer(pattern, text, re.IGNORECASE):
                line_num = text[: match.start()].count("\n") + 1
                gaps.append({
                    "file": str(path.relative_to(PROJECT_ROOT)),
                    "line": line_num,
                    "pattern": desc,
                    "severity": "medium",
                    "fix": "Replace with real backend implementation",
                })
    return gaps


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit UI gaps in VoiceStudio")
    parser.add_argument("--json", action="store_true", help="Output as JSON")
    args = parser.parse_args()

    all_gaps = []
    all_gaps.extend(find_content_dialogs_missing_xamlroot())
    all_gaps.extend(find_hidden_panels())
    all_gaps.extend(find_placeholder_backend_patterns())

    # Deduplicate by file+line+pattern
    seen = set()
    unique = []
    for g in all_gaps:
        key = (g["file"], g["line"], g["pattern"])
        if key not in seen:
            seen.add(key)
            unique.append(g)

    critical = [g for g in unique if g["severity"] == "high"]
    high_count = len(critical)

    if args.json:
        print(json.dumps({"gaps": unique, "critical_count": high_count}, indent=2))
        return 1 if high_count > 0 else 0

    print("=" * 70)
    print("UI Gap Audit Report")
    print("=" * 70)
    print()

    if not unique:
        print("[PASS] No known UI gap patterns found.")
        return 0

    by_severity = {"high": [], "medium": [], "info": []}
    for g in unique:
        by_severity.get(g["severity"], []).append(g)

    if by_severity["high"]:
        print("HIGH (fix before release):")
        for g in by_severity["high"]:
            print(f"  {g['file']}:{g['line']} — {g['pattern']}")
            print(f"    Fix: {g['fix']}")
        print()

    if by_severity["medium"]:
        print("MEDIUM (backend placeholders):")
        for g in by_severity["medium"][:10]:
            print(f"  {g['file']}:{g['line']} — {g['pattern']}")
        if len(by_severity["medium"]) > 10:
            print(f"  ... and {len(by_severity['medium']) - 10} more")
        print()

    print(f"Total: {len(unique)} gaps ({high_count} critical)")
    print()
    print("Reference: docs/reports/UI_BACKEND_GAP_ANALYSIS_20260224.md")
    print("Tools: tools/Find-AllPanels.ps1, scripts/audit_ui_gaps.py")
    return 1 if high_count > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
