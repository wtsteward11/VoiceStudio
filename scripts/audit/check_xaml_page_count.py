#!/usr/bin/env python3
"""
XAML Page Count Gate - Fail build if any project exceeds XAML page threshold.

This script helps prevent the WinUI XAML compiler ~150 page limit issue by
checking that each project stays within its allocated XAML page budget.

Usage:
    python scripts/check_xaml_page_count.py [--verbose]

Exit codes:
    0 - All projects within thresholds
    1 - One or more projects exceed thresholds
"""

import sys
from pathlib import Path

# Thresholds per project - adjust as modules grow
THRESHOLDS = {
    "VoiceStudio.App": 25,            # Shell only - most panels migrated to modules
    "VoiceStudio.Module.Voice": 50,   # Voice panels
    "VoiceStudio.Module.Media": 50,   # Media panels
    "VoiceStudio.Module.Analysis": 50, # Analysis panels
    "VoiceStudio.Module.Workflow": 50, # Workflow panels
    "VoiceStudio.Common.UI": 10,      # Shared controls and templates only
}


def count_xaml_pages(project_dir: Path) -> int:
    """Count XAML files in a project directory, excluding resource dictionaries."""
    count = 0
    for xaml_file in project_dir.glob("**/*.xaml"):
        # Read first few lines to check if it's a ResourceDictionary
        try:
            with open(xaml_file, encoding="utf-8") as f:
                content = f.read(500)
                # ResourceDictionaries are not "pages" for compiler limit purposes
                if "<ResourceDictionary" not in content:
                    count += 1
        except (OSError, UnicodeDecodeError):
            # If we can't read it, assume it's a page to be safe
            count += 1
    return count


def main():
    verbose = "--verbose" in sys.argv or "-v" in sys.argv

    # Find src directory
    script_dir = Path(__file__).parent
    src_dir = script_dir.parent / "src"

    if not src_dir.exists():
        print(f"ERROR: Source directory not found: {src_dir}")
        return 1

    failed = False
    results = []

    for project, threshold in THRESHOLDS.items():
        path = src_dir / project
        if path.exists():
            count = count_xaml_pages(path)
            status = "PASS" if count <= threshold else "FAIL"
            results.append((project, count, threshold, status))

            if count > threshold:
                failed = True
        else:
            if verbose:
                print(f"SKIP: {project} (directory not found)")

    # Print results
    print("\n" + "=" * 60)
    print("XAML Page Count Gate Results")
    print("=" * 60)

    for project, count, threshold, status in results:
        marker = "[OK]" if status == "PASS" else "[XX]"
        print(f"{marker} {project}: {count}/{threshold} pages [{status}]")

    print("=" * 60)

    if failed:
        print("\nFAIL: One or more projects exceed XAML page thresholds.")
        print("Migrate panels to appropriate modules to reduce page count.")
        return 1
    else:
        print("\nPASS: All projects within XAML page thresholds.")
        return 0


if __name__ == "__main__":
    sys.exit(main())
