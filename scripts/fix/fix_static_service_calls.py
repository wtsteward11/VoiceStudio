#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Static ServiceProvider Call Fixer.

Automatically migrates ViewModels from static ServiceProvider.GetXxx() calls
to explicit constructor parameter injection.

Usage:
    python scripts/fix_static_service_calls.py --dry-run  # Preview changes
    python scripts/fix_static_service_calls.py --fix      # Apply fixes
"""

import re
import sys
from dataclasses import dataclass
from pathlib import Path

from _env_setup import PROJECT_ROOT


@dataclass
class ServiceCall:
    """Represents a static service call that needs migration."""
    line_num: int
    original_call: str
    service_type: str
    field_name: str


SERVICE_PATTERNS = {
    r'ServiceProvider\.GetToastNotificationService\(\)': ('ToastNotificationService?', '_toastNotificationService'),
    r'ServiceProvider\.GetUndoRedoService\(\)': ('UndoRedoService?', '_undoRedoService'),
    r'ServiceProvider\.GetMultiSelectService\(\)': ('MultiSelectService', '_multiSelectService'),
    r'ServiceProvider\.GetPluginManager\(\)': ('PluginManager', '_pluginManager'),
}


def find_static_calls(content: str) -> list[ServiceCall]:
    """Find all static ServiceProvider calls in the content."""
    calls = []
    lines = content.split('\n')

    for i, line in enumerate(lines, start=1):
        for pattern, (service_type, field_name) in SERVICE_PATTERNS.items():
            if re.search(pattern, line):
                calls.append(ServiceCall(
                    line_num=i,
                    original_call=re.search(pattern, line).group(0),
                    service_type=service_type,
                    field_name=field_name
                ))

    return calls


def get_try_call_replacement(service_type: str) -> str:
    """Get the TryGet version of the service call."""
    base = service_type.rstrip('?')
    return f'AppServices.TryGet{base}()'


def fix_viewmodel(filepath: Path, dry_run: bool = True) -> str | None:
    """
    Fix a ViewModel file by replacing static calls with TryGet versions.

    For now, we use AppServices.TryGetXxx() which is safer and doesn't
    require constructor changes. This is a pragmatic first step.
    """
    try:
        content = filepath.read_text(encoding='utf-8')
    except Exception as e:
        print(f"  Error reading {filepath}: {e}")
        return None

    original = content
    calls = find_static_calls(content)

    if not calls:
        return None

    # Replace static calls with TryGet versions
    replacements = {
        'ServiceProvider.GetToastNotificationService()': 'AppServices.TryGetToastNotificationService()',
        'ServiceProvider.GetUndoRedoService()': 'AppServices.TryGetUndoRedoService()',
        'ServiceProvider.GetMultiSelectService()': 'AppServices.TryGetMultiSelectService()',
        'ServiceProvider.GetPluginManager()': 'AppServices.GetPluginManager()',
    }

    for old, new in replacements.items():
        content = content.replace(old, new)

    if content == original:
        return None

    # Check if we need to add AppServices using
    if 'using VoiceStudio.App.Services;' not in content:
        # Find the last using statement and add after it
        lines = content.split('\n')
        last_using = 0
        for i, line in enumerate(lines):
            if line.strip().startswith('using '):
                last_using = i

        if last_using > 0:
            lines.insert(last_using + 1, 'using VoiceStudio.App.Services;')
            content = '\n'.join(lines)

    if not dry_run:
        filepath.write_text(content, encoding='utf-8')

    return content


def main():
    import argparse
    parser = argparse.ArgumentParser(description='Fix static ServiceProvider calls')
    parser.add_argument('--dry-run', action='store_true', help='Preview changes without applying')
    parser.add_argument('--fix', action='store_true', help='Apply fixes')
    args = parser.parse_args()

    if not args.dry_run and not args.fix:
        print("Usage: python scripts/fix_static_service_calls.py [--dry-run | --fix]")
        return 1

    print("=" * 70)
    print("Static ServiceProvider Call Fixer")
    print("=" * 70)
    print()

    viewmodels_dir = PROJECT_ROOT / "src" / "VoiceStudio.App" / "ViewModels"
    panel_vms_dir = PROJECT_ROOT / "src" / "VoiceStudio.App" / "Views" / "Panels"

    all_files = list(viewmodels_dir.glob("*.cs")) + list(panel_vms_dir.glob("*ViewModel.cs"))

    print(f"Scanning {len(all_files)} ViewModel files...")
    print()

    fixed_count = 0

    for filepath in sorted(all_files):
        calls = find_static_calls(filepath.read_text(encoding='utf-8'))

        if not calls:
            continue

        print(f"  {filepath.name}:")
        for call in calls:
            print(f"    Line {call.line_num}: {call.original_call}")

        if args.fix:
            result = fix_viewmodel(filepath, dry_run=False)
            if result:
                print("    -> Fixed!")
                fixed_count += 1
        elif args.dry_run:
            result = fix_viewmodel(filepath, dry_run=True)
            if result:
                print("    -> Would fix")
                fixed_count += 1

        print()

    print("-" * 70)
    action = "Fixed" if args.fix else "Would fix"
    print(f"{action}: {fixed_count} files")
    print("-" * 70)

    return 0


if __name__ == "__main__":
    sys.exit(main())
