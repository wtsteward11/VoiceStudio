#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Fix Empty Catch Blocks in C# Files.

Automatically converts empty catch blocks to use ErrorLogger.LogWarning().
This is Phase 1 of the empty catch remediation (TD-018).

Usage:
    python scripts/fix_empty_catches.py src/VoiceStudio.App/App.xaml.cs
    python scripts/fix_empty_catches.py --dry-run src/VoiceStudio.App/*.cs
    python scripts/fix_empty_catches.py --all  # Fix all C# files
"""

import re
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT


def get_class_name(content: str, pos: int) -> str:
    """Extract the enclosing class name for context."""
    # Search backward for class declaration
    class_pattern = r'(?:class|struct)\s+(\w+)'
    substring = content[:pos]
    matches = list(re.finditer(class_pattern, substring))
    if matches:
        return matches[-1].group(1)
    return "Unknown"


def get_method_name(content: str, pos: int) -> str:
    """Extract the enclosing method name for context."""
    # Search backward for method declaration
    method_pattern = r'(?:void|async|Task|bool|string|int|object|var|IAsyncEnumerable)\s+(\w+)\s*[<(]'
    substring = content[max(0, pos - 2000):pos]
    matches = list(re.finditer(method_pattern, substring))
    if matches:
        return matches[-1].group(1)
    return "Unknown"


def fix_empty_catches(content: str, filename: str) -> tuple[str, int]:
    """
    Fix empty catch blocks in C# content.

    Returns tuple of (fixed_content, number_of_fixes)
    """
    fixes = 0

    # Pattern 1: catch { /* comment */ }
    pattern1 = r'catch\s*\{\s*/\*[^*]*\*/\s*\}'

    def replace1(match):
        nonlocal fixes
        fixes += 1
        pos = match.start()
        class_name = get_class_name(content, pos)
        method_name = get_method_name(content, pos)
        context = f"{class_name}.{method_name}"
        return f'catch (Exception ex) {{ ErrorLogger.LogWarning($"Best effort operation failed: {{ex.Message}}", "{context}"); }}'

    content = re.sub(pattern1, replace1, content)

    # Pattern 2: catch { // comment \n }
    pattern2 = r'catch\s*\n?\s*\{\s*\n?\s*//[^\n]*\n\s*\}'

    def replace2(match):
        nonlocal fixes
        fixes += 1
        pos = match.start()
        class_name = get_class_name(content, pos)
        method_name = get_method_name(content, pos)
        context = f"{class_name}.{method_name}"
        return f'''catch (Exception ex)
      {{
        ErrorLogger.LogWarning($"Best effort operation failed: {{ex.Message}}", "{context}");
      }}'''

    content = re.sub(pattern2, replace2, content)

    # Pattern 3: catch (Exception) { // comment \n }
    pattern3 = r'catch\s*\([^)]*\)\s*\n?\s*\{\s*\n?\s*//[^\n]*\n\s*\}'

    def replace3(match):
        nonlocal fixes
        fixes += 1
        pos = match.start()
        class_name = get_class_name(content, pos)
        method_name = get_method_name(content, pos)
        context = f"{class_name}.{method_name}"
        return f'''catch (Exception ex)
      {{
        ErrorLogger.LogWarning($"Best effort operation failed: {{ex.Message}}", "{context}");
      }}'''

    content = re.sub(pattern3, replace3, content)

    # Pattern 4: Multi-line indented catch blocks
    pattern4 = r'catch\s*\n\s*\{\s*\n\s*//[^\n]*\n\s*\}'

    def replace4(match):
        nonlocal fixes
        fixes += 1
        pos = match.start()
        class_name = get_class_name(content, pos)
        method_name = get_method_name(content, pos)
        context = f"{class_name}.{method_name}"
        # Detect indentation
        lines = match.group(0).split('\n')
        indent = len(lines[1]) - len(lines[1].lstrip()) if len(lines) > 1 else 6
        indent_str = ' ' * indent
        return f'''catch (Exception ex)
{indent_str}{{
{indent_str}  ErrorLogger.LogWarning($"Best effort operation failed: {{ex.Message}}", "{context}");
{indent_str}}}'''

    content = re.sub(pattern4, replace4, content)

    return content, fixes


def ensure_using_statement(content: str) -> str:
    """Ensure the ErrorLogger using statement is present."""
    using_statement = "using VoiceStudio.App.Logging;"

    # Check if already present
    if using_statement in content:
        return content

    # Find the last using statement and add after it
    using_pattern = r'(using [^;]+;)\n(?!using)'
    match = re.search(using_pattern, content)
    if match:
        insert_pos = match.end()
        content = content[:insert_pos] + using_statement + "\n" + content[insert_pos:]

    return content


def process_file(filepath: Path, dry_run: bool = False) -> int:
    """Process a single file. Returns number of fixes."""
    try:
        content = filepath.read_text(encoding="utf-8")
    except Exception as e:
        print(f"  Error reading {filepath}: {e}")
        return 0

    fixed_content, fixes = fix_empty_catches(content, filepath.name)

    if fixes == 0:
        return 0

    if fixes > 0:
        fixed_content = ensure_using_statement(fixed_content)

    if dry_run:
        print(f"  Would fix {fixes} empty catch(es) in {filepath.name}")
        return fixes

    try:
        filepath.write_text(fixed_content, encoding="utf-8")
        print(f"  Fixed {fixes} empty catch(es) in {filepath.name}")
        return fixes
    except Exception as e:
        print(f"  Error writing {filepath}: {e}")
        return 0


def main():
    dry_run = "--dry-run" in sys.argv
    fix_all = "--all" in sys.argv

    # Get files to process
    if fix_all:
        src_dir = PROJECT_ROOT / "src" / "VoiceStudio.App"
        files = list(src_dir.rglob("*.cs"))
    else:
        files = []
        for arg in sys.argv[1:]:
            if arg.startswith("--"):
                continue
            path = Path(arg)
            if path.exists():
                files.append(path)

    if not files:
        print("Usage: python scripts/fix_empty_catches.py [--dry-run] [--all] [files...]")
        return 1

    print("=" * 70)
    print("Empty Catch Block Fixer (TD-018)")
    print("=" * 70)
    print()

    if dry_run:
        print("DRY RUN - No files will be modified")
        print()

    total_fixes = 0
    files_fixed = 0

    for filepath in sorted(files):
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
