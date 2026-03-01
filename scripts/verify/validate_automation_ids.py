#!/usr/bin/env python3
"""
AutomationId Registry Validation Script

Validates that all AutomationIds documented in AUTOMATION_ID_REGISTRY.md
actually exist in the XAML files, and vice versa.

Usage:
    python scripts/validate_automation_ids.py
    python scripts/validate_automation_ids.py --strict
    python scripts/validate_automation_ids.py --fix
"""

import argparse
import re
import sys
from pathlib import Path

# Project paths
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
REGISTRY_FILE = PROJECT_ROOT / "docs" / "developer" / "AUTOMATION_ID_REGISTRY.md"
VIEWS_DIR = PROJECT_ROOT / "src" / "VoiceStudio.App" / "Views"
CONTROLS_DIR = PROJECT_ROOT / "src" / "VoiceStudio.App" / "Controls"


def extract_ids_from_registry() -> set[str]:
    """Extract all AutomationIds from the registry markdown file."""
    if not REGISTRY_FILE.exists():
        print(f"ERROR: Registry file not found: {REGISTRY_FILE}")
        return set()

    content = REGISTRY_FILE.read_text(encoding="utf-8")

    # Match AutomationId patterns in markdown tables and code blocks
    # Pattern: backticks around ID or first column in table
    pattern = r"`([A-Za-z]+_[A-Za-z0-9_]+)`"

    ids = set()
    for match in re.finditer(pattern, content):
        id_value = match.group(1)
        # Filter out non-AutomationId patterns
        if "_" in id_value and not id_value.startswith("VSQ."):
            ids.add(id_value)

    return ids


def extract_ids_from_xaml() -> dict[str, list[str]]:
    """Extract all AutomationIds from XAML files."""
    xaml_ids: dict[str, list[str]] = {}

    # Search patterns
    pattern = r'AutomationProperties\.AutomationId="([^"]+)"'

    # Search in Views directory
    for xaml_file in VIEWS_DIR.rglob("*.xaml"):
        content = xaml_file.read_text(encoding="utf-8")
        matches = re.findall(pattern, content)
        if matches:
            rel_path = xaml_file.relative_to(PROJECT_ROOT)
            xaml_ids[str(rel_path)] = matches

    # Search in Controls directory
    for xaml_file in CONTROLS_DIR.rglob("*.xaml"):
        content = xaml_file.read_text(encoding="utf-8")
        matches = re.findall(pattern, content)
        if matches:
            rel_path = xaml_file.relative_to(PROJECT_ROOT)
            xaml_ids[str(rel_path)] = matches

    return xaml_ids


def validate(strict: bool = False) -> tuple[bool, list[str]]:
    """
    Validate AutomationIds between registry and XAML.

    Returns:
        Tuple of (passed, list of issues)
    """
    issues: list[str] = []

    # Extract IDs
    registry_ids = extract_ids_from_registry()
    xaml_data = extract_ids_from_xaml()

    # Flatten XAML IDs
    xaml_ids: set[str] = set()
    xaml_id_locations: dict[str, str] = {}
    for file_path, ids in xaml_data.items():
        for id_value in ids:
            xaml_ids.add(id_value)
            xaml_id_locations[id_value] = file_path

    print(f"Registry IDs: {len(registry_ids)}")
    print(f"XAML IDs: {len(xaml_ids)}")
    print()

    # Check for IDs in registry but not in XAML
    missing_in_xaml = registry_ids - xaml_ids
    if missing_in_xaml:
        issues.append("IDs in registry but not in XAML:")
        for id_value in sorted(missing_in_xaml):
            issues.append(f"  - {id_value}")

    # Check for IDs in XAML but not in registry
    missing_in_registry = xaml_ids - registry_ids
    if missing_in_registry:
        if strict:
            issues.append("IDs in XAML but not in registry (strict mode):")
            for id_value in sorted(missing_in_registry):
                location = xaml_id_locations.get(id_value, "unknown")
                issues.append(f"  - {id_value} ({location})")
        else:
            print(f"INFO: {len(missing_in_registry)} IDs in XAML not documented in registry")
            print("      (Use --strict to treat as errors)")

    # Check for _Root IDs (every View should have one)
    {id_value for id_value in xaml_ids if id_value.endswith("_Root")}
    view_files = list(VIEWS_DIR.glob("Panels/*View.xaml"))

    for view_file in view_files:
        view_name = view_file.stem
        expected_root = f"{view_name}_Root"
        if expected_root not in xaml_ids:
            issues.append(f"Missing _Root ID for {view_name}: expected {expected_root}")

    # Summary
    passed = len(issues) == 0

    if passed:
        print("✅ PASSED: All AutomationIds validated")
    else:
        print("❌ FAILED: AutomationId validation issues found")
        print()
        for issue in issues:
            print(issue)

    return passed, issues


def generate_missing_entries() -> str:
    """Generate markdown entries for IDs in XAML but not in registry."""
    registry_ids = extract_ids_from_registry()
    xaml_data = extract_ids_from_xaml()

    # Find missing
    missing_entries: list[tuple[str, str, str]] = []

    for file_path, ids in xaml_data.items():
        for id_value in ids:
            if id_value not in registry_ids:
                # Infer control type from ID
                parts = id_value.split("_")
                control_type = parts[-1] if len(parts) > 1 else "Unknown"
                missing_entries.append((id_value, control_type, file_path))

    if not missing_entries:
        return "No missing entries found."

    output = ["## Missing Registry Entries", ""]
    output.append("| AutomationId | Control Type | Location |")
    output.append("|--------------|--------------|----------|")

    for id_value, control_type, location in sorted(missing_entries):
        output.append(f"| `{id_value}` | {control_type} | {location} |")

    return "\n".join(output)


def main():
    parser = argparse.ArgumentParser(
        description="Validate AutomationId registry against XAML files"
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Treat undocumented XAML IDs as errors"
    )
    parser.add_argument(
        "--fix",
        action="store_true",
        help="Generate markdown for missing entries"
    )
    args = parser.parse_args()

    print("=" * 60)
    print("AutomationId Registry Validation")
    print("=" * 60)
    print()

    if args.fix:
        output = generate_missing_entries()
        print(output)
        return 0

    passed, _issues = validate(strict=args.strict)

    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
