#!/usr/bin/env python3
"""
validate_xaml_resources.py - Validate StaticResource references in XAML files

This script scans XAML files for StaticResource references and verifies that
all referenced VSQ.* resources are defined in the ResourceDictionary files.

Usage:
    python scripts/validate_xaml_resources.py [--fix] [--verbose]

Exit codes:
    0: All resources validated (or fixed with --fix)
    1: Missing resources found (or error)
"""

import argparse
import re
import sys
from pathlib import Path

# Paths relative to project root
RESOURCE_DIRS = [
    "src/VoiceStudio.App/Resources",
    "src/VoiceStudio.App/Resources/Styles",
]
XAML_DIRS = [
    "src/VoiceStudio.App/Views",
    "src/VoiceStudio.App/Controls",
]

# Patterns
RESOURCE_DEF_PATTERN = re.compile(r'x:Key="(VSQ\.[A-Za-z0-9.]+)"')
RESOURCE_REF_PATTERN = re.compile(r'\{(?:Static|Theme)Resource\s+(VSQ\.[A-Za-z0-9.]+)\}')


def find_project_root() -> Path:
    """Find the project root directory."""
    script_dir = Path(__file__).parent
    # Look for VoiceStudio.sln to identify project root
    for parent in [script_dir, script_dir.parent]:
        if (parent / "VoiceStudio.sln").exists():
            return parent
    raise RuntimeError("Could not find project root (VoiceStudio.sln)")


def collect_defined_resources(project_root: Path) -> dict[str, Path]:
    """Collect all VSQ.* resources defined in ResourceDictionary files."""
    defined = {}

    for res_dir in RESOURCE_DIRS:
        res_path = project_root / res_dir
        if not res_path.exists():
            continue

        for xaml_file in res_path.glob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8")
                for match in RESOURCE_DEF_PATTERN.finditer(content):
                    key = match.group(1)
                    defined[key] = xaml_file
            except Exception as e:
                print(f"Warning: Could not read {xaml_file}: {e}", file=sys.stderr)

    return defined


def collect_referenced_resources(project_root: Path) -> dict[str, list[tuple[Path, int]]]:
    """Collect all VSQ.* resources referenced in XAML files."""
    referenced = {}

    for xaml_dir in XAML_DIRS:
        dir_path = project_root / xaml_dir
        if not dir_path.exists():
            continue

        for xaml_file in dir_path.rglob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8")
                for i, line in enumerate(content.splitlines(), 1):
                    for match in RESOURCE_REF_PATTERN.finditer(line):
                        key = match.group(1)
                        if key not in referenced:
                            referenced[key] = []
                        referenced[key].append((xaml_file, i))
            except Exception as e:
                print(f"Warning: Could not read {xaml_file}: {e}", file=sys.stderr)

    return referenced


def validate_resources(defined: dict[str, Path],
                       referenced: dict[str, list[tuple[Path, int]]],
                       verbose: bool = False) -> set[str]:
    """Find resources that are referenced but not defined."""
    missing = set()

    for key, locations in referenced.items():
        if key not in defined:
            missing.add(key)
            if verbose:
                print(f"\nMissing resource: {key}")
                for file_path, line_num in locations[:5]:
                    print(f"  Referenced at: {file_path.name}:{line_num}")
                if len(locations) > 5:
                    print(f"  ... and {len(locations) - 5} more locations")

    return missing


def print_summary(defined: dict[str, Path],
                  referenced: dict[str, list[tuple[Path, int]]],
                  missing: set[str]):
    """Print validation summary."""
    print("\n" + "=" * 60)
    print("XAML Resource Validation Summary")
    print("=" * 60)
    print(f"Defined VSQ.* resources:    {len(defined)}")
    print(f"Referenced VSQ.* resources: {len(referenced)}")
    print(f"Missing resources:          {len(missing)}")

    if missing:
        print("\n--- Missing Resources ---")
        for key in sorted(missing):
            print(f"  - {key}")

    print("=" * 60)


def suggest_additions(missing: set[str]) -> str:
    """Generate XAML snippets for missing resources."""
    suggestions = ["<!-- Add to DesignTokens.xaml or appropriate ResourceDictionary -->"]

    for key in sorted(missing):
        if "Brush" in key or "Color" in key:
            suggestions.append(f'<SolidColorBrush x:Key="{key}" Color="#FF808080" />')
        elif "FontSize" in key:
            suggestions.append(f'<x:Double x:Key="{key}">14</x:Double>')
        elif "Width" in key or "Height" in key or "Size" in key:
            suggestions.append(f'<x:Double x:Key="{key}">100</x:Double>')
        elif "Spacing" in key:
            suggestions.append(f'<Thickness x:Key="{key}">8</Thickness>')
        elif "CornerRadius" in key:
            suggestions.append(f'<CornerRadius x:Key="{key}">4</CornerRadius>')
        elif "Text" in key and "Brush" not in key:
            # Style for TextBlock
            suggestions.append(f'<Style x:Key="{key}" TargetType="TextBlock" />')
        elif "Button" in key:
            suggestions.append(f'<Style x:Key="{key}" TargetType="Button" />')
        else:
            suggestions.append(f'<!-- Unknown type: {key} -->')

    return "\n".join(suggestions)


def main():
    parser = argparse.ArgumentParser(description="Validate XAML StaticResource references")
    parser.add_argument("--verbose", "-v", action="store_true", help="Show detailed output")
    parser.add_argument("--suggest", "-s", action="store_true", help="Suggest XAML for missing resources")
    args = parser.parse_args()

    try:
        project_root = find_project_root()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    print(f"Project root: {project_root}")
    print("Scanning for VSQ.* resources...")

    defined = collect_defined_resources(project_root)
    referenced = collect_referenced_resources(project_root)
    missing = validate_resources(defined, referenced, verbose=args.verbose)

    print_summary(defined, referenced, missing)

    if args.suggest and missing:
        print("\n--- Suggested Additions ---")
        print(suggest_additions(missing))

    if missing:
        print("\nValidation FAILED: Missing resources found")
        return 1
    else:
        print("\nValidation PASSED: All resources are defined")
        return 0


if __name__ == "__main__":
    sys.exit(main())
