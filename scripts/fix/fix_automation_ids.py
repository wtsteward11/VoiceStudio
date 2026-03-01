#!/usr/bin/env python3
"""
Fix malformed AutomationIds in panel XAML files.

Fixes the issue where <GridAutomationProperties... was generated
instead of <Grid AutomationProperties...

Usage:
    python scripts/fix_automation_ids.py
"""

import re
from pathlib import Path


def fix_malformed_id(content: str) -> tuple[str, bool]:
    """
    Fix malformed <GridAutomationProperties... pattern.

    Returns:
        tuple of (fixed_content, was_fixed)
    """
    # Pattern to match malformed <GridAutomationProperties.AutomationId="...">
    malformed_pattern = r'<GridAutomationProperties\.AutomationId="([^"]+)">'

    def replacement(match):
        automation_id = match.group(1)
        return f'<Grid AutomationProperties.AutomationId="{automation_id}">'

    fixed, count = re.subn(malformed_pattern, replacement, content)

    return fixed, count > 0


def process_file(file_path: Path) -> tuple[bool, str]:
    """
    Process a single XAML file to fix AutomationId.

    Returns:
        tuple of (success, message)
    """
    try:
        content = file_path.read_text(encoding='utf-8')

        fixed_content, was_fixed = fix_malformed_id(content)

        if not was_fixed:
            return True, f"SKIP: {file_path.name} - No malformed pattern found"

        file_path.write_text(fixed_content, encoding='utf-8')
        return True, f"FIXED: {file_path.name}"

    except Exception as e:
        return False, f"ERROR: {file_path.name} - {e}"


def main():
    # Find project root
    script_dir = Path(__file__).parent
    project_root = script_dir.parent
    panels_dir = project_root / "src/VoiceStudio.App/Views/Panels"

    if not panels_dir.exists():
        print(f"ERROR: Panels directory not found: {panels_dir}")
        return 1

    # Find all XAML files
    xaml_files = sorted(panels_dir.glob("*.xaml"))

    print(f"Fixing {len(xaml_files)} XAML files in {panels_dir}")
    print("=" * 60)

    fixed = 0
    skipped = 0
    errors = 0

    for file_path in xaml_files:
        _success, message = process_file(file_path)

        if "FIXED" in message:
            print(message)
            fixed += 1
        elif "SKIP" in message:
            skipped += 1
        else:
            print(message)
            errors += 1

    print("=" * 60)
    print(f"Summary: {fixed} fixed, {skipped} already correct, {errors} errors")

    return 0 if errors == 0 else 1


if __name__ == "__main__":
    exit(main())
