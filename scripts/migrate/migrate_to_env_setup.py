#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
TD-019: Python Path Standardization Migration Script.

Migrates scripts to use _env_setup.py for standardized path setup.

Usage:
    python scripts/migrate_to_env_setup.py         # Dry run
    python scripts/migrate_to_env_setup.py --fix   # Apply fixes
"""

import re
import sys
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent

# Patterns to detect manual path setup (to be removed)
PATH_SETUP_PATTERNS = [
    # Pattern 1: _project_root = Path(__file__).parent.parent
    r"^_project_root\s*=\s*Path\(__file__\)\.parent\.parent.*$",
    # Pattern 2: project_root = Path(__file__).parent.parent
    r"^project_root\s*=\s*Path\(__file__\)\.parent\.parent.*$",
    # Pattern 3: if str(_project_root) not in sys.path:
    r"^if\s+str\(_project_root\)\s+not\s+in\s+sys\.path:.*$",
    # Pattern 4: if str(project_root) not in sys.path:
    r"^if\s+str\(project_root\)\s+not\s+in\s+sys\.path:.*$",
    # Pattern 5: sys.path.insert(0, str(_project_root))
    r"^\s*sys\.path\.insert\(0,\s*str\(_project_root\)\).*$",
    # Pattern 6: sys.path.insert(0, str(project_root))
    r"^\s*sys\.path\.insert\(0,\s*str\(project_root\)\).*$",
    # Pattern 7: # Add project root to path
    r"^#\s*Add\s+project\s+root\s+to\s+path.*$",
    # Pattern 8: Empty lines after path setup (preserve for now)
]

# Files to skip
SKIP_FILES = {"_env_setup.py", "migrate_to_env_setup.py"}


def needs_migration(script_path: Path) -> tuple[bool, list[str]]:
    """Check if a script needs migration and return reasons."""
    content = script_path.read_text(encoding="utf-8")

    if "from _env_setup import" in content:
        return False, ["Already uses _env_setup"]

    reasons = []

    # Check for manual path setup
    if "sys.path.insert" in content or "sys.path.append" in content:
        if "Path(__file__).parent" in content:
            reasons.append("Has manual path setup")

    # Check for imports from app, backend, tools
    if re.search(r"from (app|backend|tools)\.", content):
        reasons.append("Imports from app/backend/tools")
    if re.search(r"import (app|backend|tools)\.", content):
        reasons.append("Imports from app/backend/tools")

    return len(reasons) > 0, reasons


def migrate_script(script_path: Path, dry_run: bool = True) -> tuple[bool, str]:
    """Migrate a script to use _env_setup.py."""
    content = script_path.read_text(encoding="utf-8")
    lines = content.split("\n")

    new_lines = []
    i = 0
    added_import = False
    removed_count = 0

    while i < len(lines):
        line = lines[i]
        skip_line = False

        # Check if this line matches any path setup pattern
        for pattern in PATH_SETUP_PATTERNS:
            if re.match(pattern, line):
                skip_line = True
                removed_count += 1
                break

        if skip_line:
            i += 1
            continue

        # Add _env_setup import after shebang/docstring/copyright
        if not added_import:
            # Find the right place to insert (after imports but before first code)
            if line.startswith("import ") or line.startswith("from "):
                # Check if this is the pathlib import we can augment
                if line == "from pathlib import Path":
                    new_lines.append("from _env_setup import PROJECT_ROOT")
                    new_lines.append(line)
                    added_import = True
                    i += 1
                    continue
                elif "from pathlib" not in line and "import sys" not in line:
                    # Insert before this import
                    new_lines.append("from _env_setup import PROJECT_ROOT")
                    new_lines.append("")
                    added_import = True

        new_lines.append(line)
        i += 1

    # If we haven't added the import yet, add it after the docstring
    if not added_import:
        # Find end of docstring
        insert_pos = 0
        in_docstring = False
        for j, line in enumerate(new_lines):
            if '"""' in line or "'''" in line:
                if in_docstring:
                    insert_pos = j + 1
                    break
                else:
                    in_docstring = True

        if insert_pos > 0:
            new_lines.insert(insert_pos, "")
            new_lines.insert(insert_pos + 1, "from _env_setup import PROJECT_ROOT")
            added_import = True

    new_content = "\n".join(new_lines)

    # Replace _project_root with PROJECT_ROOT
    new_content = re.sub(r"\b_project_root\b", "PROJECT_ROOT", new_content)
    new_content = re.sub(r"\bproject_root\b(?!\.)", "PROJECT_ROOT", new_content)

    if dry_run:
        return True, f"Would remove {removed_count} lines, add _env_setup import"

    script_path.write_text(new_content, encoding="utf-8")
    return True, f"Removed {removed_count} lines, added _env_setup import"


def main():
    dry_run = "--fix" not in sys.argv

    print("=" * 70)
    print("TD-019: Python Path Standardization")
    print("=" * 70)
    print()

    if dry_run:
        print("DRY RUN - use --fix to apply changes")
        print()

    scripts_to_migrate = []
    already_migrated = []
    no_migration_needed = []

    for script_path in sorted(SCRIPTS_DIR.glob("*.py")):
        if script_path.name in SKIP_FILES:
            continue

        needs, reasons = needs_migration(script_path)

        if "from _env_setup import" in script_path.read_text(encoding="utf-8"):
            already_migrated.append(script_path.name)
        elif needs:
            scripts_to_migrate.append((script_path, reasons))
        else:
            no_migration_needed.append(script_path.name)

    print(f"Already using _env_setup: {len(already_migrated)}")
    print(f"Need migration: {len(scripts_to_migrate)}")
    print(f"Standalone (no migration needed): {len(no_migration_needed)}")
    print()

    if scripts_to_migrate:
        print("Scripts to migrate:")
        print("-" * 70)

        migrated_count = 0
        failed_count = 0

        for script_path, reasons in scripts_to_migrate:
            print(f"\n  {script_path.name}:")
            print(f"    Reasons: {', '.join(reasons)}")

            try:
                success, message = migrate_script(script_path, dry_run)
                if success:
                    print(f"    {'Would migrate' if dry_run else 'Migrated'}: {message}")
                    migrated_count += 1
                else:
                    print(f"    Skipped: {message}")
            except Exception as e:
                print(f"    ERROR: {e}")
                failed_count += 1

        print()
        print("-" * 70)
        print(f"{'Would migrate' if dry_run else 'Migrated'}: {migrated_count}")
        print(f"Failed: {failed_count}")
    else:
        print("No scripts need migration!")

    print()
    if dry_run and scripts_to_migrate:
        print("Run with --fix to apply changes")


if __name__ == "__main__":
    main()
