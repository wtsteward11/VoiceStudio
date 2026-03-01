#!/usr/bin/env python3
"""
Panel Migration Helper - Migrate panels from VoiceStudio.App to feature modules.

Usage:
    python scripts/migrate_panel.py <panel_name> <target_module>

Example:
    python scripts/migrate_panel.py VoiceSynthesisView Voice
    python scripts/migrate_panel.py TimelineView Media

This script:
1. Copies XAML and code-behind files
2. Updates namespaces in both files
3. Adds using statements for shared types
4. Updates ViewModel reference if present
5. Prints reminder for manual steps (service registration)
"""

import re
import sys
from pathlib import Path

# Module mappings
MODULES = {
    "Voice": "VoiceStudio.Module.Voice",
    "Media": "VoiceStudio.Module.Media",
    "Analysis": "VoiceStudio.Module.Analysis",
    "Workflow": "VoiceStudio.Module.Workflow",
}

def read_file(path: Path) -> str:
    """Read file content."""
    return path.read_text(encoding="utf-8")

def write_file(path: Path, content: str):
    """Write content to file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    print(f"  Created: {path}")

def update_xaml_namespace(content: str, panel_name: str, module_name: str) -> str:
    """Update x:Class namespace in XAML."""
    old_class = f"VoiceStudio.App.Views.Panels.{panel_name}"
    new_class = f"{module_name}.Views.{panel_name}"
    content = content.replace(old_class, new_class)

    # Update xmlns:local if present
    content = content.replace(
        'xmlns:local="using:VoiceStudio.App.Views.Panels"',
        f'xmlns:local="using:{module_name}.Views"'
    )

    # Add Common.UI controls reference
    if 'xmlns:controls="using:VoiceStudio.App.Controls"' in content:
        content = content.replace(
            'xmlns:controls="using:VoiceStudio.App.Controls"',
            'xmlns:controls="using:VoiceStudio.App.Controls"\n  xmlns:commonui="using:VoiceStudio.Common.UI.Controls"'
        )

    return content

def update_cs_namespace(content: str, panel_name: str, module_name: str) -> str:
    """Update namespace in C# code-behind."""
    # Update namespace
    content = content.replace(
        "namespace VoiceStudio.App.Views.Panels",
        f"namespace {module_name}.Views"
    )

    # Add using statements for App services (they stay in App for now)
    additional_usings = """using VoiceStudio.App.Services;
using VoiceStudio.App.Controls;
using VoiceStudio.App.ViewModels;
"""

    # Check if we need to add usings
    if "using VoiceStudio.App.Services;" not in content:
        # Find first using statement and add after
        content = re.sub(
            r'(using Microsoft\.UI\.Xaml\.Controls;)',
            r'\1\n' + additional_usings,
            content,
            count=1
        )

    return content

def update_viewmodel(content: str, panel_name: str, module_name: str) -> str:
    """Update ViewModel references (optional - VMs can stay in App for now)."""
    # ViewModels stay in App.ViewModels for now - just ensure the using is present
    return content

def migrate_panel(panel_name: str, target_module: str, src_dir: Path):
    """Migrate a panel to the target module."""
    if target_module not in MODULES:
        print(f"ERROR: Unknown module '{target_module}'. Valid modules: {list(MODULES.keys())}")
        return False

    module_name = MODULES[target_module]

    # Source paths
    xaml_src = src_dir / "VoiceStudio.App" / "Views" / "Panels" / f"{panel_name}.xaml"
    cs_src = src_dir / "VoiceStudio.App" / "Views" / "Panels" / f"{panel_name}.xaml.cs"

    # Target paths
    module_dir = src_dir / f"VoiceStudio.Module.{target_module}"
    xaml_dst = module_dir / "Views" / f"{panel_name}.xaml"
    cs_dst = module_dir / "Views" / f"{panel_name}.xaml.cs"

    # Check source exists
    if not xaml_src.exists():
        print(f"ERROR: Source XAML not found: {xaml_src}")
        return False
    if not cs_src.exists():
        print(f"ERROR: Source code-behind not found: {cs_src}")
        return False

    print(f"\nMigrating {panel_name} to {target_module} module...")

    # Read and transform XAML
    xaml_content = read_file(xaml_src)
    xaml_content = update_xaml_namespace(xaml_content, panel_name, module_name)
    write_file(xaml_dst, xaml_content)

    # Read and transform C#
    cs_content = read_file(cs_src)
    cs_content = update_cs_namespace(cs_content, panel_name, module_name)
    cs_content = update_viewmodel(cs_content, panel_name, module_name)
    write_file(cs_dst, cs_content)

    print(f"\n  SUCCESS: {panel_name} migrated to {module_name}")
    print("\n  MANUAL STEPS REQUIRED:")
    print(f"  1. Register panel in {target_module}Module.OnInitialized():")
    print("     registry.RegisterPanel(new PanelDescriptor {")
    print(f"         Id = \"{panel_name.replace('View', '')}\",")
    print(f"         Name = \"{panel_name.replace('View', '')}\",")
    print(f"         ViewType = typeof({panel_name}),")
    print("         Region = PanelRegion.Center")
    print("     });")
    print("  2. Delete original files from VoiceStudio.App after verification")
    print("  3. Update any references to the old namespace")

    return True

def main():
    if len(sys.argv) < 3:
        print("Usage: python scripts/migrate_panel.py <panel_name> <target_module>")
        print("Example: python scripts/migrate_panel.py VoiceSynthesisView Voice")
        return 1

    panel_name = sys.argv[1]
    target_module = sys.argv[2]

    # Find src directory
    script_dir = Path(__file__).parent
    src_dir = script_dir.parent / "src"

    if not src_dir.exists():
        print(f"ERROR: Source directory not found: {src_dir}")
        return 1

    success = migrate_panel(panel_name, target_module, src_dir)
    return 0 if success else 1

if __name__ == "__main__":
    sys.exit(main())
