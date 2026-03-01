#!/usr/bin/env python3
"""
Audit Uncommitted Dependencies

Identifies code in tracked files that imports from untracked files/directories.
Prevents broken imports when untracked code is referenced by committed code.

Exit codes:
  0 - No uncommitted dependencies detected
  1 - Uncommitted dependency violations found
"""

import subprocess
import sys
from pathlib import Path


def get_untracked_py_files():
    """Get untracked Python files."""
    try:
        result = subprocess.run(
            ["git", "ls-files", "--others", "--exclude-standard"],
            capture_output=True,
            text=True,
            check=True
        )

        untracked = result.stdout.strip().split("\n")
        py_files = [f for f in untracked if f.endswith(".py") and f]
        return py_files
    except subprocess.CalledProcessError:
        print("Warning: Could not run git command")
        return []


def get_tracked_py_files():
    """Get tracked Python files."""
    try:
        result = subprocess.run(
            ["git", "ls-files", "*.py"],
            capture_output=True,
            text=True,
            check=True
        )

        tracked = result.stdout.strip().split("\n")
        return [f for f in tracked if f]
    except subprocess.CalledProcessError:
        print("Warning: Could not run git command")
        return []


def extract_imports(file_path):
    """Extract module imports from a Python file."""
    imports = []
    try:
        content = Path(file_path).read_text(encoding="utf-8")
        for line in content.split("\n"):
            line = line.strip()
            if line.startswith("from ") or line.startswith("import "):
                # Extract module name
                if line.startswith("from "):
                    module = line.split()[1].split(".")[0]
                else:
                    module = line.split()[1].split(".")[0]
                imports.append(module)
    # ALLOWED: bare except - Best effort file parsing, failure is acceptable
    except Exception:
        pass
    return imports


def audit():
    """Audit for uncommitted dependencies."""
    # Get untracked directories containing Python files
    untracked_files = get_untracked_py_files()
    untracked_dirs = {str(Path(f).parent) for f in untracked_files}

    # Convert to module names
    untracked_modules = set()
    for dir_path in untracked_dirs:
        module = dir_path.replace("/", ".").replace("\\", ".")
        if module.startswith("."):
            module = module[1:]
        untracked_modules.add(module)
        # Also add top-level module
        if "." in module:
            untracked_modules.add(module.split(".")[0])

    if not untracked_modules:
        print("✅ No untracked Python modules detected")
        return 0

    print(f"Found {len(untracked_modules)} untracked module(s): {', '.join(sorted(untracked_modules))}")

    # Check tracked files for imports from untracked modules
    tracked_files = get_tracked_py_files()
    violations = []

    for tracked_file in tracked_files:
        imports = extract_imports(tracked_file)
        for imp in imports:
            if imp in untracked_modules:
                violations.append({
                    "tracked_file": tracked_file,
                    "untracked_module": imp
                })

    if violations:
        print(f"\n❌ Uncommitted dependency violations ({len(violations)} found):")
        print("   Tracked files importing from untracked modules:")
        for v in violations:
            print(f"     - {v['tracked_file']} imports {v['untracked_module']}")
        print("\n   Fix: Commit the untracked modules before importing them")
        return 1

    print("✅ No uncommitted dependencies detected")
    return 0


if __name__ == "__main__":
    sys.exit(audit())
