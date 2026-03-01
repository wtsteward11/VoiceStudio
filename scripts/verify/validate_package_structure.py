#!/usr/bin/env python3
"""
Validate Package Structure

Ensures every directory containing .py files has __init__.py.
Prevents ModuleNotFoundError from missing package markers.

Exit codes:
  0 - All package structures valid
  1 - Missing __init__.py files detected
"""

import io
import sys
from pathlib import Path

# Ensure UTF-8 output on Windows console
if sys.platform == "win32" and hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")


def validate_packages():
    """Validate that all Python package directories have __init__.py."""
    project_root = Path(__file__).parent.parent
    errors = []

    # Directories to check (exclude venvs, external, build artifacts)
    check_dirs = [
        project_root / "tools",
        project_root / "backend",
        project_root / "app",
        project_root / "tests",
    ]

    print("Validating package structure...")

    for base_dir in check_dirs:
        if not base_dir.exists():
            print(f"  Skipping {base_dir.relative_to(project_root)} (doesn't exist)")
            continue

        # Find directories with .py files
        for dir_path in base_dir.rglob("*"):
            if not dir_path.is_dir():
                continue

            # Skip excluded directories
            dir_str = str(dir_path)
            if any(exclude in dir_str for exclude in ["venv", "external", ".buildlogs", "__pycache__", ".pytest_cache"]):
                continue

            # Check if directory has .py files (exclude test-only files)
            py_files = list(dir_path.glob("*.py"))
            # Filter out test files if this is a test directory
            is_test_dir = "test" in str(dir_path).lower()
            if is_test_dir:
                py_files = [f for f in py_files if not f.name.startswith("test_") and not f.name.startswith("conftest")]

            if py_files:
                init_file = dir_path / "__init__.py"
                if not init_file.exists():
                    rel_path = dir_path.relative_to(project_root)
                    errors.append(str(rel_path))
                    print(f"  ✗ {rel_path}/ (has {len(py_files)} .py files, no __init__.py)")

    if errors:
        print("\n❌ Package structure validation failed")
        print("   The following directories contain .py files but lack __init__.py:")
        for error in errors:
            print(f"     - {error}/")
        print("\n   Fix: Create __init__.py in each directory (can be empty)")
        return 1

    print("\n✅ All package structures valid")
    return 0


if __name__ == "__main__":
    sys.exit(validate_packages())
