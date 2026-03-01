#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
XAML Linting Script for VoiceStudio.

Detects problematic XAML patterns that cause WinAppSDK 1.8 XAML compiler crashes,
including attached property syntax issues on ContentPresenter and animation targets.

Usage:
    python scripts/lint_xaml.py [files...]

If no files are provided, checks all XAML files in the src/ directory.
"""

import re
import sys
from pathlib import Path

# Patterns that cause XAML compiler crashes in WinAppSDK 1.8
# Note: Patterns should be line-scoped or element-scoped to avoid matching comments
FORBIDDEN_PATTERNS = [
    # Attached property on ContentPresenter (VS-0040)
    (
        r'TextElement\.Foreground="\{TemplateBinding',
        "Attached property TextElement.Foreground on ContentPresenter causes XAML compiler crash",
        "Use explicit Foreground binding or move to child TextBlock"
    ),
    # Animation targeting attached property - match within same line or element (not across lines)
    # Uses [^<>]* to stay within single element boundaries
    (
        r'<ObjectAnimationUsingKeyFrames[^>]*Storyboard\.TargetProperty="\(TextElement\.',
        "ObjectAnimationUsingKeyFrames targeting TextElement attached property",
        "Animate explicit property on child element instead"
    ),
    # Storyboard targeting attached property
    (
        r'Storyboard\.TargetProperty="\(TextElement\.',
        "Storyboard targeting TextElement attached property",
        "Target explicit property on child element instead"
    ),
    # ColorAnimation on attached property - element-scoped
    (
        r'<ColorAnimation[^>]*Storyboard\.TargetProperty="\(TextElement\.',
        "ColorAnimation targeting TextElement attached property",
        "Animate explicit property on child element instead"
    ),
    # DoubleAnimation on attached property that may not exist - element-scoped
    (
        r'<DoubleAnimation[^>]*Storyboard\.TargetProperty="\(TextElement\.',
        "DoubleAnimation targeting TextElement attached property",
        "Verify property exists and is animatable"
    ),
]

# Warning patterns (not blocking, but worth noting)
WARNING_PATTERNS = [
    # Deep nesting that may cause issues
    (
        r'<ControlTemplate[^>]*>(?:(?!</ControlTemplate>).)*<ControlTemplate',
        "Nested ControlTemplate detected - may cause XAML compiler issues",
        "Consider extracting nested template to a resource"
    ),
    # Very long DataTemplate
    (
        r'<DataTemplate[^>]*>(?:[^<]|<(?!/DataTemplate>))*</DataTemplate>',
        "DataTemplate may be too complex - validate build succeeds",
        "Consider breaking into smaller templates"
    ),
]

# Directories to skip
SKIP_DIRS = {
    '.git', '__pycache__', 'node_modules', 'bin', 'obj',
    '.venv', 'venv', '.tox', 'dist', 'build', '.buildlogs',
    'runtime/external',  # External dependencies
}


def should_skip_path(path: Path) -> bool:
    """Check if a path should be skipped."""
    parts = set(path.parts)
    return bool(parts & SKIP_DIRS)


def check_xaml_file(path: Path) -> tuple[list[tuple[int, str, str]], list[tuple[int, str, str]]]:
    """
    Check a XAML file for problematic patterns.

    Returns:
        Tuple of (errors, warnings) where each is a list of (line_num, description, suggestion)
    """
    errors = []
    warnings = []

    try:
        content = path.read_text(encoding='utf-8')
    except Exception as e:
        print(f"Warning: Could not read {path}: {e}", file=sys.stderr)
        return errors, warnings

    # Check forbidden patterns (errors)
    for pattern, description, suggestion in FORBIDDEN_PATTERNS:
        for match in re.finditer(pattern, content, re.MULTILINE | re.DOTALL):
            line_num = content.count('\n', 0, match.start()) + 1
            errors.append((line_num, description, suggestion))

    # Check warning patterns
    for pattern, description, suggestion in WARNING_PATTERNS:
        for match in re.finditer(pattern, content, re.MULTILINE | re.DOTALL):
            line_num = content.count('\n', 0, match.start()) + 1
            # Only report first match per file for warnings
            warnings.append((line_num, description, suggestion))
            break

    return errors, warnings


def get_all_xaml_files(root: Path) -> list[Path]:
    """Get all XAML files, respecting skip dirs."""
    files = []
    for path in root.rglob('*.xaml'):
        if path.is_file() and not should_skip_path(path):
            files.append(path)
    return files


def main(args: list[str]) -> int:
    """Main entry point."""
    exit_code = 0
    all_errors: list[tuple[Path, int, str, str]] = []
    all_warnings: list[tuple[Path, int, str, str]] = []

    if args:
        # Check specific files
        files = [Path(f) for f in args if Path(f).exists() and Path(f).suffix.lower() == '.xaml']
    else:
        # Check all XAML files in src/
        root = Path(__file__).parent.parent / "src"
        files = get_all_xaml_files(root)

    for path in files:
        if should_skip_path(path):
            continue

        errors, warnings = check_xaml_file(path)

        for line_num, desc, suggestion in errors:
            all_errors.append((path, line_num, desc, suggestion))

        for line_num, desc, suggestion in warnings:
            all_warnings.append((path, line_num, desc, suggestion))

    # Report warnings (non-blocking)
    if all_warnings:
        print("\n" + "-" * 70)
        print("XAML WARNINGS (non-blocking)")
        print("-" * 70 + "\n")

        for path, line_num, desc, suggestion in all_warnings:
            print(f"  {path}:{line_num}")
            print(f"    Warning: {desc}")
            print(f"    Suggestion: {suggestion}")
            print()

    # Report errors (blocking)
    if all_errors:
        print("\n" + "=" * 70)
        print("XAML SAFETY CHECK FAILED")
        print("=" * 70)
        print("\nThe following patterns will cause XAML compiler crashes:\n")

        for path, line_num, desc, suggestion in all_errors:
            print(f"  {path}:{line_num}")
            print(f"    ERROR: {desc}")
            print(f"    FIX: {suggestion}")
            print()

        print("=" * 70)
        print("REFERENCE: See docs/developer/XAML_CHANGE_PROTOCOL.md for guidance")
        print("=" * 70 + "\n")

        exit_code = 1
    else:
        print("XAML safety check: PASS")

    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
