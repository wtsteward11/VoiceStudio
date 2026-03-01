#!/usr/bin/env python3
"""
Hardcoded Color Detection - Detect hardcoded colors in XAML files.

This script scans XAML files for hardcoded colors (hex values, named colors)
to ensure design token compliance.

Usage:
    python scripts/check_hardcoded_colors.py [--verbose] [--strict]

Options:
    --verbose, -v    Show all violations (not just summary)
    --strict         Fail on any violation (default: warn only)

Exit codes:
    0 - No violations found (or warnings only in non-strict mode)
    1 - Violations found (in strict mode)
"""

import re
import sys
from pathlib import Path

# Patterns that indicate hardcoded colors
HARDCODED_PATTERNS = [
    (r'Background="#[0-9A-Fa-f]{6,8}"', "Background with hex color"),
    (r'Foreground="#[0-9A-Fa-f]{6,8}"', "Foreground with hex color"),
    (r'Fill="#[0-9A-Fa-f]{6,8}"', "Fill with hex color"),
    (r'Color="#[0-9A-Fa-f]{6,8}"', "Color with hex value"),
    (r'BorderBrush="#[0-9A-Fa-f]{6,8}"', "BorderBrush with hex color"),
    (r'Foreground="White"', "Foreground with named color"),
    (r'Foreground="Black"', "Foreground with named color"),
    (r'Background="White"', "Background with named color"),
    (r'Background="Black"', "Background with named color"),
]

# Patterns to exclude (these are acceptable)
EXCLUDE_PATTERNS = [
    r'StaticResource',     # Using design tokens
    r'ThemeResource',      # Using theme resources
    r'DynamicResource',    # Using dynamic resources
    r'x:Key=',             # Resource definitions are OK
    r'\.Legacy\.xaml',     # Legacy files are excluded
    r'DesignTokens\.xaml', # Token definitions are OK
    r'Theme\.',            # Theme definition files
]

# Directories to skip
SKIP_DIRS = [
    "bin", "obj", "node_modules", ".git", "packages"
]


def should_skip_file(path: Path) -> bool:
    """Check if file should be skipped."""
    path_str = str(path)
    return any(skip_dir in path_str for skip_dir in SKIP_DIRS)


def check_file(path: Path) -> list[tuple[int, str, str]]:
    """Check a single XAML file for hardcoded colors. Returns list of (line_num, line, pattern_name)."""
    violations = []

    try:
        content = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return violations

    lines = content.split('\n')

    for line_num, line in enumerate(lines, 1):
        # Skip if line contains exclude patterns
        if any(re.search(pattern, line) for pattern in EXCLUDE_PATTERNS):
            continue

        for pattern, name in HARDCODED_PATTERNS:
            if re.search(pattern, line):
                violations.append((line_num, line.strip(), name))
                break  # Only report first match per line

    return violations


def main():
    verbose = "--verbose" in sys.argv or "-v" in sys.argv
    strict = "--strict" in sys.argv

    # Find src directory
    script_dir = Path(__file__).parent
    src_dir = script_dir.parent / "src"

    if not src_dir.exists():
        print(f"ERROR: Source directory not found: {src_dir}")
        return 1

    all_violations = []
    files_checked = 0
    files_with_violations = 0

    # Scan all XAML files
    for xaml_path in src_dir.glob("**/*.xaml"):
        if should_skip_file(xaml_path):
            continue

        files_checked += 1
        violations = check_file(xaml_path)

        if violations:
            files_with_violations += 1
            for line_num, line, pattern_name in violations:
                all_violations.append((xaml_path, line_num, line, pattern_name))

    # Print results
    print("\n" + "=" * 60)
    print("Hardcoded Color Detection Results")
    print("=" * 60)
    print(f"Files checked: {files_checked}")
    print(f"Files with violations: {files_with_violations}")
    print(f"Total violations: {len(all_violations)}")
    print("=" * 60)

    if all_violations:
        if verbose:
            print("\nViolations found:")
            current_file = None
            for path, line_num, line, pattern_name in all_violations:
                rel_path = path.relative_to(src_dir)
                if path != current_file:
                    current_file = path
                    print(f"\n{rel_path}:")
                print(f"  L{line_num} ({pattern_name}): {line[:80]}...")
        else:
            print("\nTop files with most violations:")
            # Group by file
            file_counts = {}
            for path, _, _, _ in all_violations:
                file_counts[path] = file_counts.get(path, 0) + 1

            for path, count in sorted(file_counts.items(), key=lambda x: -x[1])[:10]:
                rel_path = path.relative_to(src_dir)
                print(f"  {count} violations: {rel_path}")

            print("\nRun with --verbose to see all violations.")

        print("\nRecommendation: Replace hardcoded colors with VSQ.* design tokens.")

        if strict:
            print("\nFAIL: Hardcoded colors detected (strict mode).")
            return 1
        else:
            print("\nWARN: Hardcoded colors detected (non-strict mode).")
            return 0
    else:
        print("\nPASS: No hardcoded colors found.")
        return 0


if __name__ == "__main__":
    sys.exit(main())
