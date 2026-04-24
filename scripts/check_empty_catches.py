#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Pre-commit hook to detect empty catch blocks in C# and Python files.

This script blocks commits that contain empty catch blocks, which hide errors
and make debugging difficult. Use ErrorBoundary patterns instead.

Usage:
    python scripts/check_empty_catches.py [files...]

If no files are provided, checks all C# and Python files in the repository.
"""

import os
import re
import sys
from pathlib import Path

# Patterns that indicate empty or near-empty catch blocks
CSHARP_EMPTY_CATCH_PATTERNS = [
    # Empty catch block: catch { } or catch (Exception) { }
    (r'catch\s*(?:\([^)]*\))?\s*\{\s*\}', "Empty catch block"),
    # Catch with only a comment
    (r'catch\s*(?:\([^)]*\))?\s*\{\s*//[^\n]*\s*\}', "Catch block with only comment"),
    (r'catch\s*(?:\([^)]*\))?\s*\{\s*/\*[^*]*\*/\s*\}', "Catch block with only block comment"),
]

# Patterns for minimal catch blocks (warning level - check in ViewModels)
CSHARP_MINIMAL_CATCH_PATTERNS = [
    # Catch with only Debug.WriteLine or Console.WriteLine
    (r'catch\s*\([^)]*\)\s*\{\s*(?:Debug|Console)\.Write(?:Line)?\([^)]+\);\s*\}',
     "Minimal catch - only logs to console/debug"),
    # Catch with only a simple log statement and no re-throw
    (r'catch\s*\(\s*\w+\s+(\w+)\s*\)\s*\{\s*_?[lL]ogger\.Log(?:Error|Warning)?\([^)]+\);\s*\}',
     "Minimal catch - only logs without handling"),
    # Catch that swallows exception with just return
    (r'catch\s*(?:\([^)]*\))?\s*\{\s*return(?:\s+\w+)?;\s*\}',
     "Minimal catch - silently returns"),
    # Catch that swallows with just a boolean
    (r'catch\s*(?:\([^)]*\))?\s*\{\s*\w+\s*=\s*(?:true|false);\s*\}',
     "Minimal catch - only sets boolean"),
]

PYTHON_BARE_EXCEPT_PATTERNS = [
    # Bare except with pass
    (r'except\s*:\s*\n\s*pass\s*$', "Bare except with pass"),
    # Bare except with only comment
    (r'except\s*:\s*\n\s*#[^\n]*\s*$', "Bare except with only comment"),
    # except Exception with pass (also problematic)
    (r'except\s+\w+(?:\s+as\s+\w+)?:\s*\n\s*pass\s*$', "Exception catch with pass"),
]

# Allowlist patterns - legitimate uses that should not be flagged
ALLOWLIST_PATTERNS = [
    r'# noqa: empty-catch',  # Explicit suppression with comment
    r'// ALLOWED: empty catch',  # C# explicit allowance
    r'# ALLOWED: bare except',  # Python explicit allowance
]

# Directories to skip (exact match)
SKIP_DIRS = {
    '.git', '__pycache__', 'node_modules', 'bin', 'obj',
    '.venv', 'venv', '.venvs', '.tox', 'dist', 'build', '.buildlogs',
    'runtime', 'env',  # External dependencies and virtual environments
    '.cursor',  # Cursor hooks and skills (intentional patterns)
    'tests',  # Test code often uses except: pass for teardown/skip (allowed in tests)
    'docs',  # Archived code snapshots (e.g. pre_restore_20260228) are not live code
    # Operator model roots; torch.hub trees are third-party, not repo source
    'models',
}

# Directory prefixes to skip (partial match for venv_* patterns)
SKIP_DIR_PREFIXES = {'venv_', 'env_'}


def should_skip_path(path: Path) -> bool:
    """Check if a path should be skipped."""
    parts = set(path.parts)
    # Exact match
    if parts & SKIP_DIRS:
        return True
    # Prefix match (for venv_*, env_* directories)
    for part in path.parts:
        for prefix in SKIP_DIR_PREFIXES:
            if part.startswith(prefix):
                return True
    return False


def is_allowlisted(content: str, match_start: int, match_end: int) -> bool:
    """Check if a match is within an allowlisted context."""
    # Check surrounding lines for allowlist patterns
    line_start = content.rfind('\n', 0, match_start) + 1
    line_end = content.find('\n', match_end)
    if line_end == -1:
        line_end = len(content)

    # Check 2 lines before and the current line for allowlist
    context_start = content.rfind('\n', 0, line_start - 1) + 1 if line_start > 0 else 0
    context = content[context_start:line_end]

    return any(re.search(pattern, context) for pattern in ALLOWLIST_PATTERNS)


def check_csharp_file(path: Path, check_minimal: bool = False) -> list[tuple[int, str, str, str]]:
    """Check a C# file for empty catch blocks.

    Args:
        path: Path to the C# file
        check_minimal: If True, also check for minimal catch blocks

    Returns:
        List of (line_num, description, snippet, severity) tuples
    """
    issues = []
    try:
        content = path.read_text(encoding='utf-8')
    except Exception as e:
        print(f"Warning: Could not read {path}: {e}", file=sys.stderr)
        return issues

    # Always check for empty catch blocks (error level)
    for pattern, description in CSHARP_EMPTY_CATCH_PATTERNS:
        for match in re.finditer(pattern, content, re.MULTILINE | re.DOTALL):
            if not is_allowlisted(content, match.start(), match.end()):
                line_num = content.count('\n', 0, match.start()) + 1
                issues.append((line_num, description, match.group(0)[:50], "error"))

    # Check for minimal catch blocks if enabled (warning level)
    if check_minimal:
        for pattern, description in CSHARP_MINIMAL_CATCH_PATTERNS:
            for match in re.finditer(pattern, content, re.MULTILINE | re.DOTALL):
                if not is_allowlisted(content, match.start(), match.end()):
                    line_num = content.count('\n', 0, match.start()) + 1
                    issues.append((line_num, description, match.group(0)[:50], "warning"))

    return issues


def check_python_file(path: Path) -> list[tuple[int, str, str, str]]:
    """Check a Python file for bare except blocks.

    Returns:
        List of (line_num, description, snippet, severity) tuples
    """
    issues = []
    try:
        content = path.read_text(encoding='utf-8')
    except Exception as e:
        print(f"Warning: Could not read {path}: {e}", file=sys.stderr)
        return issues

    for pattern, description in PYTHON_BARE_EXCEPT_PATTERNS:
        for match in re.finditer(pattern, content, re.MULTILINE):
            if not is_allowlisted(content, match.start(), match.end()):
                line_num = content.count('\n', 0, match.start()) + 1
                issues.append((line_num, description, match.group(0).strip()[:50], "error"))

    return issues


def is_viewmodel_file(path: Path) -> bool:
    """Check if a file is a ViewModel file."""
    return 'ViewModel' in path.name and path.suffix.lower() == '.cs'


def _walk_error(err: OSError) -> None:
    """Log and continue on walk errors (e.g. permission denied on Windows)."""
    print(f"Warning: Skipping {getattr(err, 'filename', '?')}: {err}", file=sys.stderr)


def get_all_files(root: Path, extensions: set[str]) -> list[Path]:
    """Get all files with the given extensions, respecting skip dirs."""
    files = []
    try:
        for dirpath, dirnames, filenames in os.walk(root, topdown=True, onerror=_walk_error):
            dirnames[:] = [d for d in dirnames if not should_skip_path(Path(dirpath) / d)]
            for name in filenames:
                path = Path(dirpath) / name
                if path.suffix.lower() in extensions and not should_skip_path(path):
                    files.append(path)
    except OSError as e:
        print(f"Warning: Walk failed at {root}: {e}", file=sys.stderr)
    return files


def main(args: list[str]) -> int:
    """Main entry point."""
    import argparse

    parser = argparse.ArgumentParser(
        description="Check for empty and minimal catch blocks in C# and Python files."
    )
    parser.add_argument(
        'files',
        nargs='*',
        help='Files to check (default: all C# and Python files)'
    )
    parser.add_argument(
        '--check-minimal',
        action='store_true',
        help='Also check for minimal catch blocks (warning level)'
    )
    parser.add_argument(
        '--viewmodels',
        action='store_true',
        help='Enable minimal catch checking for ViewModel files'
    )
    parser.add_argument(
        '--json',
        action='store_true',
        help='Output results as JSON'
    )

    parsed_args = parser.parse_args(args)

    exit_code = 0
    issues_found: list[tuple[Path, int, str, str, str]] = []

    if parsed_args.files:
        # Check specific files
        files = [Path(f) for f in parsed_args.files if Path(f).exists()]
    else:
        # Check all relevant files in the repo
        root = Path(__file__).parent.parent
        files = get_all_files(root, {'.cs', '.py'})

    for path in files:
        if should_skip_path(path):
            continue

        if path.suffix.lower() == '.cs':
            # Check minimal catches for ViewModels if flag is set
            check_minimal = parsed_args.check_minimal or (
                parsed_args.viewmodels and is_viewmodel_file(path)
            )
            for line_num, desc, snippet, severity in check_csharp_file(path, check_minimal):
                issues_found.append((path, line_num, desc, snippet, severity))
        elif path.suffix.lower() == '.py':
            # Skip this script itself
            if path.name == 'check_empty_catches.py':
                continue
            for line_num, desc, snippet, severity in check_python_file(path):
                issues_found.append((path, line_num, desc, snippet, severity))

    # Separate errors from warnings
    errors = [i for i in issues_found if i[4] == 'error']
    warnings = [i for i in issues_found if i[4] == 'warning']

    if parsed_args.json:
        import json
        result = {
            'errors': len(errors),
            'warnings': len(warnings),
            'issues': [
                {
                    'file': str(path),
                    'line': line_num,
                    'description': desc,
                    'snippet': snippet,
                    'severity': severity
                }
                for path, line_num, desc, snippet, severity in issues_found
            ]
        }
        print(json.dumps(result, indent=2))
        if errors:
            exit_code = 1
    else:
        if issues_found:
            print("\n" + "=" * 70)
            print("CATCH BLOCK ANALYSIS RESULTS")
            print("=" * 70)

            if errors:
                print("\nERRORS (must fix):\n")
                for path, line_num, desc, snippet, severity in errors:
                    print(f"  {path}:{line_num}")
                    print(f"    Issue: {desc}")
                    print(f"    Code:  {snippet}...")
                    print()

            if warnings:
                print("\nWARNINGS (consider improving):\n")
                for path, line_num, desc, snippet, severity in warnings:
                    print(f"  {path}:{line_num}")
                    print(f"    Issue: {desc}")
                    print(f"    Code:  {snippet}...")
                    print()

            print("=" * 70)
            print("FIX: Use ErrorBoundary patterns instead of empty catches.")
            print()
            print("C# Example:")
            print("  var result = ErrorBoundary.Execute(() => SomeOperation(), fallback);")
            print()
            print("Python Example:")
            print("  result = try_execute(lambda: some_operation(), fallback, context='...')")
            print()
            print("To allow an empty catch (rare), add comment on preceding line:")
            print("  C#:     // ALLOWED: empty catch - [reason]")
            print("  Python: # ALLOWED: bare except - [reason]")
            print("=" * 70 + "\n")

            print(f"Summary: {len(errors)} errors, {len(warnings)} warnings")

            if errors:
                exit_code = 1
        else:
            print("Empty catch block check: PASS")

    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
