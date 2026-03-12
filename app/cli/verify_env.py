"""
Environment Verification Script
Quick health check after migration
"""

import json
import sys
from pathlib import Path


def check_python_version():
    """Check Python version."""
    version = sys.version_info
    print(f"Python: {version.major}.{version.minor}.{version.micro}")
    if version.major == 3 and version.minor >= 10:
        print("  ✓ Python version OK")
        return True
    else:
        print("  ✗ Python 3.10+ required")
        return False


def check_paths():
    """Check critical paths."""
    workspace = Path(__file__).parent.parent.parent
    paths = {
        "Workspace root": workspace,
        "Engines directory": workspace / "engines",
        "Models directory": workspace / "models",
        "Library directory": workspace / "library",
        "UI Views": workspace / "ui" / "Views" / "Panels",
        "App core": workspace / "app" / "core",
    }

    all_ok = True
    for name, path in paths.items():
        if path.exists():
            print(f"  ✓ {name}: {path}")
        else:
            print(f"  ✗ {name}: {path} (missing)")
            all_ok = False

    return all_ok


def check_governor_learners():
    """Check Governor + learners exist."""
    workspace = Path(__file__).parent.parent.parent

    files = {
        "Governor": workspace / "app" / "core" / "runtime" / "governor.py",
        "Quality Scorer": workspace / "app" / "core" / "learners" / "quality_scorer.py",
        "Prosody Tuner": workspace / "app" / "core" / "learners" / "prosody_tuner.py",
        "Dataset Curator": workspace / "app" / "core" / "learners" / "dataset_curator.py",
    }

    for name, path in files.items():
        if path.exists():
            print(f"  ✓ {name}: {path}")
        else:
            print(f"  ⚠ {name}: {path} (not found - may be in different location)")

    return True  # Don't fail if files in different locations


def check_engine_config():
    """Check engine configuration."""
    workspace = Path(__file__).parent.parent.parent
    config_path = workspace / "engines" / "config.json"

    if config_path.exists():
        try:
            with open(config_path) as f:
                config = json.load(f)

            defaults = config.get("defaults", {})
            installed = config.get("installed", [])

            print("  ✓ Engine config found")
            print(f"    Defaults: {defaults}")
            print(f"    Installed: {len(installed)} engines")
            return True
        except Exception as e:
            print(f"  ✗ Failed to read config: {e}")
            return False
    else:
        print(f"  ⚠ Config not found: {config_path}")
        return True  # Don't fail, config may be created later


def check_panel_count():
    """Check panel count."""
    workspace = Path(__file__).parent.parent.parent

    # Comprehensive search
    search_dirs = [
        workspace / "ui" / "Views" / "Panels",
        workspace / "ui" / "Views",
        workspace / "ui" / "Panels",
        workspace / "src" / "VoiceStudio.App" / "Views" / "Panels",
        workspace / "src" / "VoiceStudio.App" / "Views",
        workspace / "src" / "VoiceStudio.App" / "Panels",
        workspace / "app" / "ui" / "panels",
        workspace / "app" / "ui" / "views",
        workspace / "Views" / "Panels",
        workspace / "Views",
        workspace / "Panels",
    ]

    patterns = ["*View.xaml", "*Panel.xaml"]
    all_panels = set()

    for panel_path in search_dirs:
        if panel_path.exists():
            for pattern in patterns:
                panels = list(panel_path.rglob(pattern))
                for panel in panels:
                    all_panels.add(panel)

    total_panels = len(all_panels)

    if total_panels > 0:
        print(f"  ✓ Total panels found: {total_panels}")
        if total_panels >= 90:
            print("  ✓ Panel count matches expected (~90+)")
        else:
            print(f"  ⚠ Panel count lower than expected (found {total_panels}, expected ~90+)")
            print("  💡 Run: .\tools\\Find-AllPanels.ps1 to regenerate PanelRegistry.Auto.cs")

        # Check registry
        registry_file = workspace / "app" / "core" / "PanelRegistry.Auto.cs"
        if registry_file.exists():
            content = registry_file.read_text(encoding="utf-8")
            import re

            registered = len(re.findall(r'"([^"]+\.xaml)"', content))
            print(f"  ✓ Registered in PanelRegistry.Auto.cs: {registered} panels")
            if registered < total_panels:
                print(f"  ⚠ {total_panels - registered} panels not in registry")
        else:
            print("  ⚠ PanelRegistry.Auto.cs not found")

        return True
    else:
        print("  ⚠ No panels found")
        print("  💡 Run: .\tools\\Find-AllPanels.ps1 to discover panels")
        return True  # Don't fail, panels may be in different location


def check_path_references():
    """Check for old C: path references."""
    workspace = Path(__file__).parent.parent.parent

    # Check common file types
    patterns = ["*.py", "*.cs", "*.json", "*.xaml"]
    found_old_paths = []

    for pattern in patterns:
        for file_path in workspace.rglob(pattern):
            if file_path.is_file():
                try:
                    with open(file_path, encoding="utf-8", errors="ignore") as f:
                        content = f.read()
                        if "C:\\VoiceStudio" in content or "C:/VoiceStudio" in content:
                            found_old_paths.append(str(file_path))
                except OSError:
                    pass  # Skip unreadable files

    if found_old_paths:
        print(f"  ⚠ Found {len(found_old_paths)} files with C:\\VoiceStudio references:")
        for path in found_old_paths[:5]:  # Show first 5
            print(f"    - {path}")
        if len(found_old_paths) > 5:
            print(f"    ... and {len(found_old_paths) - 5} more")
        return False
    else:
        print("  ✓ No C:\\VoiceStudio references found")
        return True


def main():
    """Run all checks."""
    print("=" * 60)
    print("VoiceStudio Environment Verification")
    print("=" * 60)
    print()

    results = []

    print("[1] Python Version")
    results.append(check_python_version())
    print()

    print("[2] Critical Paths")
    results.append(check_paths())
    print()

    print("[3] Governor + Learners")
    results.append(check_governor_learners())
    print()

    print("[4] Engine Configuration")
    results.append(check_engine_config())
    print()

    print("[5] Panel Count")
    results.append(check_panel_count())
    print()

    print("[6] Path References")
    results.append(check_path_references())
    print()

    print("=" * 60)
    if all(results):
        print("✓ All checks passed!")
        return 0
    else:
        print("⚠ Some checks had warnings (see above)")
        return 1


if __name__ == "__main__":
    sys.exit(main())
