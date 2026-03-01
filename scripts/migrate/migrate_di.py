#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
DI Migration Audit Script.

Identifies ViewModels using static ServiceProvider calls and
suggests migration to IViewModelContext.

Usage:
    python scripts/migrate_di.py
    python scripts/migrate_di.py --fix  # Apply fixes
"""

import re
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

# Mapping of static calls to IViewModelContext properties
SERVICE_MAPPINGS = {
    "ServiceProvider.GetBackendClient()": "_context.BackendClient",
    "ServiceProvider.GetSettingsManager()": "_context.SettingsManager",
    "ServiceProvider.GetPanelManager()": "_context.PanelManager",
    "ServiceProvider.GetLogger()": "_context.Logger",
    "ServiceProvider.GetMultiSelectService()": "_context.MultiSelectService",
    "ServiceProvider.GetProfileService()": "_context.ProfileService",
    "ServiceProvider.GetPluginManager()": "_context.PluginManager",
    "ServiceProvider.GetThemeService()": "_context.ThemeService",
    "ServiceProvider.GetNotificationService()": "_context.NotificationService",
}


def audit_file(filepath: Path) -> list[tuple[int, str, str]]:
    """
    Audit a file for static ServiceProvider calls.

    Returns list of (line_number, call, suggested_replacement)
    """
    issues = []

    try:
        content = filepath.read_text(encoding="utf-8")
        lines = content.split("\n")
    except Exception as e:
        print(f"  Error reading {filepath}: {e}")
        return issues

    for i, line in enumerate(lines, start=1):
        for static_call, replacement in SERVICE_MAPPINGS.items():
            if static_call in line:
                issues.append((i, static_call, replacement))

        # Check for other ServiceProvider calls not in mapping
        if "ServiceProvider.Get" in line and not any(call in line for call in SERVICE_MAPPINGS):
            match = re.search(r'ServiceProvider\.Get\w+\([^)]*\)', line)
            if match:
                issues.append((i, match.group(0), "MANUAL: Add to IViewModelContext"))

    return issues


def main():
    print("=" * 70)
    print("DI Migration Audit (Phase 8)")
    print("=" * 70)
    print()

    viewmodels_dir = PROJECT_ROOT / "src" / "VoiceStudio.App" / "ViewModels"
    files = list(viewmodels_dir.glob("*.cs"))

    print(f"Scanning {len(files)} ViewModel files...")
    print()

    total_issues = 0
    files_with_issues = 0

    for filepath in sorted(files):
        issues = audit_file(filepath)

        if issues:
            files_with_issues += 1
            print(f"  {filepath.name}:")
            for line_num, call, replacement in issues:
                print(f"    Line {line_num}: {call}")
                print(f"      -> {replacement}")
                total_issues += 1
            print()

    print("-" * 70)
    print(f"Total: {total_issues} static calls in {files_with_issues} files")
    print("-" * 70)
    print()
    print("MIGRATION PATTERN:")
    print()
    print("  1. Add IViewModelContext to constructor")
    print("  2. Store as private readonly _context field")
    print("  3. Replace ServiceProvider.GetXxx() with _context.Xxx")
    print()
    print("  Example:")
    print("    // BEFORE")
    print("    private readonly IBackendClient _backendClient = ServiceProvider.GetBackendClient();")
    print()
    print("    // AFTER")
    print("    private readonly IViewModelContext _context;")
    print("    public MyViewModel(IViewModelContext context) => _context = context;")
    print("    // Use: _context.BackendClient")

    return 0


if __name__ == "__main__":
    sys.exit(main())
