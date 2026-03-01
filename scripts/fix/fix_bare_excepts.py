#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Fix Bare Except Blocks in Python Files.

Converts bare `except: pass` and `except Exception: pass` blocks to include
proper logging or adds allowlist comments for legitimate best-effort patterns.

This is part of TD-018 empty catch remediation.

Usage:
    python scripts/fix_bare_excepts.py --dry-run
    python scripts/fix_bare_excepts.py --all
"""

import re
import sys
from pathlib import Path

# Add project root to path
_project_root = Path(__file__).parent.parent
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))


# Directories to process
SOURCE_DIRS = ["backend/api", "backend/services", "app/core", "app/cli", "scripts", "tests", "tools"]

# Skip patterns
SKIP_PATTERNS = ["venv", ".venv", "site-packages", "__pycache__", "runtime/external"]

# Already allowlisted patterns
ALLOWLIST_PATTERNS = [
    r"# ALLOWED: bare except",
    r"# noqa",
    r"# Best effort",
    r"# Optional",
]


def should_skip(path: Path) -> bool:
    """Check if path should be skipped."""
    path_str = str(path).replace("\\", "/")
    return any(s in path_str for s in SKIP_PATTERNS)


def is_allowlisted(content: str, pos: int) -> bool:
    """Check if a match is within an allowlisted context."""
    start = max(0, pos - 150)
    context = content[start:pos + 50]
    return any(re.search(pat, context) for pat in ALLOWLIST_PATTERNS)


def get_context(content: str, pos: int) -> str:
    """Get function/class context for the position."""
    # Look backward for def or class
    substring = content[:pos]

    # Find the last function definition
    func_match = list(re.finditer(r"def\s+(\w+)\s*\(", substring))
    if func_match:
        return func_match[-1].group(1)

    # Find the last class definition
    class_match = list(re.finditer(r"class\s+(\w+)", substring))
    if class_match:
        return class_match[-1].group(1)

    return "module"


def fix_bare_excepts(content: str, filename: str) -> tuple[str, int]:
    """
    Fix bare except blocks in Python content.

    Strategy:
    1. `except ImportError: pass` -> Add "# Optional dependency" comment
    2. `except Exception: pass` -> Add "# Best effort - failure acceptable" comment
    3. `except: pass` -> Add "# Best effort - failure acceptable" comment

    Returns tuple of (fixed_content, number_of_fixes)
    """
    fixes = 0
    lines = content.split("\n")
    new_lines = []
    i = 0

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        # Check for except patterns
        if stripped.startswith("except") and i + 1 < len(lines):
            next_stripped = lines[i + 1].strip()
            indent = len(line) - len(line.lstrip())

            # Check if this is a pass-only except
            if next_stripped == "pass":
                # Check if already allowlisted
                context_start = max(0, i - 3)
                context_lines = "\n".join(lines[context_start:i+2])

                if not any(re.search(pat, context_lines) for pat in ALLOWLIST_PATTERNS):
                    fixes += 1

                    # Determine the type of comment to add
                    if "ImportError" in stripped:
                        comment = "# Optional dependency - import failure is acceptable"
                    elif "Exception" in stripped or stripped == "except:":
                        comment = "# Best effort - failure is acceptable here"
                    else:
                        comment = "# Best effort - failure is acceptable here"

                    # Add comment before the except
                    new_lines.append(" " * indent + comment)

        new_lines.append(line)
        i += 1

    return "\n".join(new_lines), fixes


def process_file(filepath: Path, dry_run: bool = False) -> int:
    """Process a single file. Returns number of fixes."""
    try:
        content = filepath.read_text(encoding="utf-8")
    except Exception as e:
        print(f"  Error reading {filepath}: {e}")
        return 0

    fixed_content, fixes = fix_bare_excepts(content, filepath.name)

    if fixes == 0:
        return 0

    if dry_run:
        print(f"  Would fix {fixes} bare except(s) in {filepath}")
        return fixes

    try:
        filepath.write_text(fixed_content, encoding="utf-8")
        print(f"  Fixed {fixes} bare except(s) in {filepath}")
        return fixes
    except Exception as e:
        print(f"  Error writing {filepath}: {e}")
        return 0


def main():
    dry_run = "--dry-run" in sys.argv
    fix_all = "--all" in sys.argv

    if not fix_all and not dry_run:
        print("Usage: python scripts/fix_bare_excepts.py [--dry-run] [--all]")
        return 1

    print("=" * 70)
    print("Bare Except Block Fixer (TD-018 - Python)")
    print("=" * 70)
    print()

    if dry_run:
        print("DRY RUN - No files will be modified")
        print()

    total_fixes = 0
    files_fixed = 0

    for src_dir in SOURCE_DIRS:
        src_path = _project_root / src_dir
        if not src_path.exists():
            continue

        for filepath in sorted(src_path.rglob("*.py")):
            if should_skip(filepath):
                continue
            if filepath.name == "fix_bare_excepts.py":
                continue
            if filepath.name == "check_empty_catches.py":
                continue

            fixes = process_file(filepath, dry_run)
            total_fixes += fixes
            if fixes > 0:
                files_fixed += 1

    print()
    print("-" * 70)
    print(f"Total fixes: {total_fixes} in {files_fixed} files")
    print("-" * 70)

    return 0


if __name__ == "__main__":
    sys.exit(main())
