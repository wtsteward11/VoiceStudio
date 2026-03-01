#!/usr/bin/env python3
"""
Check XAML files for {Binding} usage that could be migrated to {x:Bind}.

x:Bind provides:
- Compile-time validation of binding paths
- Better performance (no reflection)
- Function binding support
- Explicit mode (default OneTime vs OneWay)

Usage:
    python scripts/check_binding_usage.py [--verbose]
"""

import argparse
import re
from pathlib import Path


def find_xaml_files(root_dir: str) -> list[Path]:
    """Find all XAML files in the source directory."""
    xaml_files = []
    src_path = Path(root_dir) / "src" / "VoiceStudio.App"

    if not src_path.exists():
        print(f"Warning: {src_path} does not exist")
        return xaml_files

    for xaml_file in src_path.rglob("*.xaml"):
        # Skip resource dictionaries (they don't have code-behind for x:Bind)
        if "Resources" in str(xaml_file) or "Themes" in str(xaml_file):
            continue
        xaml_files.append(xaml_file)

    return xaml_files


def analyze_xaml_file(file_path: Path) -> tuple[int, int, list[str]]:
    """
    Analyze a XAML file for binding usage.

    Returns:
        Tuple of (binding_count, x_bind_count, binding_samples)
    """
    try:
        content = file_path.read_text(encoding='utf-8')
    except Exception as e:
        return 0, 0, [f"Error reading file: {e}"]

    # Count {Binding ...} patterns
    binding_pattern = r'\{Binding\s+[^}]+\}'
    bindings = re.findall(binding_pattern, content)

    # Count {x:Bind ...} patterns
    xbind_pattern = r'\{x:Bind\s+[^}]+\}'
    xbinds = re.findall(xbind_pattern, content)

    # Also count simple {Binding} and {x:Bind}
    simple_binding = content.count('{Binding}')
    simple_xbind = content.count('{x:Bind}')

    binding_count = len(bindings) + simple_binding
    xbind_count = len(xbinds) + simple_xbind

    # Get sample bindings (first 3)
    samples = bindings[:3] if bindings else []

    return binding_count, xbind_count, samples


def main():
    parser = argparse.ArgumentParser(description="Check XAML binding usage")
    parser.add_argument("--verbose", "-v", action="store_true", help="Show detailed output")
    parser.add_argument("--root", default=".", help="Root directory of the project")
    args = parser.parse_args()

    root_dir = args.root
    xaml_files = find_xaml_files(root_dir)

    print(f"Analyzing {len(xaml_files)} XAML files...")
    print()

    total_binding = 0
    total_xbind = 0
    files_with_binding = []

    for xaml_file in xaml_files:
        binding_count, xbind_count, samples = analyze_xaml_file(xaml_file)
        total_binding += binding_count
        total_xbind += xbind_count

        if binding_count > 0:
            relative_path = xaml_file.relative_to(Path(root_dir))
            files_with_binding.append((relative_path, binding_count, xbind_count, samples))

    # Sort by binding count (highest first)
    files_with_binding.sort(key=lambda x: x[1], reverse=True)

    print("=" * 70)
    print("XAML Binding Usage Summary")
    print("=" * 70)
    print(f"Total {{Binding}} usage: {total_binding}")
    print(f"Total {{x:Bind}} usage: {total_xbind}")
    print(f"Files with {{Binding}}: {len(files_with_binding)}")

    if total_binding + total_xbind > 0:
        xbind_pct = (total_xbind / (total_binding + total_xbind)) * 100
        print(f"x:Bind adoption rate: {xbind_pct:.1f}%")

    print()
    print("-" * 70)
    print("Priority Files for Migration (highest {Binding} count):")
    print("-" * 70)

    for path, binding_count, xbind_count, samples in files_with_binding[:20]:
        print(f"  {path}: {binding_count} Binding, {xbind_count} x:Bind")
        if args.verbose and samples:
            for sample in samples[:2]:
                print(f"    Sample: {sample[:60]}...")

    if len(files_with_binding) > 20:
        print(f"  ... and {len(files_with_binding) - 20} more files")

    print()
    print("=" * 70)
    print("Migration Notes:")
    print("=" * 70)
    print("1. Add x:DataType to each Page/UserControl for compile-time validation")
    print("2. Change {Binding Property} to {x:Bind ViewModel.Property, Mode=OneWay}")
    print("3. x:Bind defaults to OneTime mode (add Mode=OneWay for dynamic properties)")
    print("4. Use {Binding} only for ElementName or RelativeSource scenarios")
    print("5. See docs/developer/WINUI_MIGRATION_GUIDE.md for detailed guidance")

    return 0 if total_binding == 0 else 1


if __name__ == "__main__":
    exit(main())
