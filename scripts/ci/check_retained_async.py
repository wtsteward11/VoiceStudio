#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Check for retained-async rule violations in ViewModels.

Flags prohibited patterns per docs/design/RETAINED_ASYNC_RULE.md:
- Constructor fire-and-forget (_ = .*Async in constructor)
- Task.Run for debounce in ViewModels
- Fire-and-forget without CTS in property handlers
- ContinueWith (often used without CTS/staleness guard)

Usage:
    python scripts/ci/check_retained_async.py [files...]
    python scripts/ci/check_retained_async.py --baseline  # Print baseline for .ci/retained_async_baseline.txt

With --baseline-file: known violations are allowed; fail only if NEW violations appear.
Exit 0 if no new violations; exit 1 if violations found (or new violations exceed baseline).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
BASELINE_FILE = ROOT / ".ci" / "retained_async_baseline.txt"


def find_project_root() -> Path:
    """Find project root (directory containing .git or VoiceStudio.sln)."""
    p = Path(__file__).resolve().parent.parent.parent
    for _ in range(5):
        if (p / ".git").exists() or (p / "VoiceStudio.sln").exists():
            return p
        p = p.parent
    return Path(__file__).resolve().parent.parent.parent


# Patterns to flag (advisory - may have false positives)
CONSTRUCTOR_FAF = re.compile(
    r"^\s+_\s*=\s+\w+Async\s*\(",
    re.MULTILINE,
)
TASK_RUN_DEBOUNCE = re.compile(
    r"Task\.Run\s*\(",
    re.MULTILINE,
)
# Property handler fire-and-forget: OnSelected*Changed with _ = .*Async(CancellationToken.None)
PROPERTY_FAF_NONE = re.compile(
    r"(?:OnSelected|OnFilter|OnProject)\w*Changed.*?_\s*=\s+\w+Async\s*\(\s*CancellationToken\.None\s*\)",
    re.DOTALL,
)
# ContinueWith - often used without CTS/staleness guard
CONTINUE_WITH = re.compile(r"\.ContinueWith\s*\(")

SKIP_DIRS = {".git", "__pycache__", "bin", "obj", ".buildlogs", "node_modules", "docs", "artifacts"}
VIEWMODEL_SUFFIX = "ViewModel.cs"


def should_skip(path: Path) -> bool:
    parts = set(path.parts)
    if parts & SKIP_DIRS:
        return True
    if "Tests" in parts or "tests" in parts:
        return True
    return False


def check_file(path: Path) -> list[tuple[int, str, str]]:
    """Return list of (line_num, pattern_name, line_content) violations."""
    violations = []
    try:
        content = path.read_text(encoding="utf-8", errors="replace")
        lines = content.splitlines()
    except OSError:
        return violations

    in_constructor = False
    brace_depth = 0
    constructor_start = -1

    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        if "public " in line and "(" in line and "{" in line and "ViewModel" in line:
            # Could be constructor - track brace depth
            if "ViewModel(" in line or "ViewModel (" in line:
                in_constructor = True
                brace_depth = line.count("{") - line.count("}")
                constructor_start = i
        if in_constructor:
            brace_depth += line.count("{") - line.count("}")
            if CONSTRUCTOR_FAF.search(line):
                violations.append((i, "constructor_fire_and_forget", line.strip()))
            if brace_depth <= 0:
                in_constructor = False

        if TASK_RUN_DEBOUNCE.search(line) and "ViewModel" in str(path):
            violations.append((i, "task_run_debounce", line.strip()))

        if CONTINUE_WITH.search(line) and "ViewModel" in str(path):
            violations.append((i, "continue_with_faf", line.strip()))

    # Check for property handler FAF with CancellationToken.None (simplified)
    for i, line in enumerate(lines, 1):
        if "OnSelected" in line or "OnFilter" in line:
            # Look at next ~5 lines for _ = .*Async(CancellationToken.None)
            block = "\n".join(lines[i - 1 : min(i + 5, len(lines))])
            if "_ = " in block and "Async(" in block and "CancellationToken.None" in block:
                violations.append((i, "property_faf_no_cts", line.strip()))

    return violations


def load_baseline() -> set[str]:
    """Load baseline of allowed violations (path:line)."""
    if not BASELINE_FILE.exists():
        return set()
    allowed = set()
    for line in BASELINE_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            key = line.split("#")[0].strip()
            if key:
                allowed.add(key)
    return allowed


def main() -> int:
    root = find_project_root()
    use_baseline = "--baseline-file" in sys.argv
    paths = [p for p in sys.argv[1:] if not p.startswith("--")]
    if paths:
        files = [Path(p) for p in paths if p.endswith(VIEWMODEL_SUFFIX) or p.endswith(".cs")]
    else:
        app_dir = root / "src" / "VoiceStudio.App"
        files = []
        for scan_dir in [app_dir / "ViewModels", app_dir / "Views" / "Panels"]:
            if scan_dir.exists():
                files.extend(
                    p for p in scan_dir.rglob("*.cs")
                    if VIEWMODEL_SUFFIX in p.name and not should_skip(p)
                )

    all_violations: list[tuple[Path, int, str, str]] = []
    for f in sorted(files):
        for line_num, pattern, line in check_file(f):
            try:
                rel = f.relative_to(root)
            except ValueError:
                rel = f
            all_violations.append((rel, line_num, pattern, line))

    if use_baseline:
        baseline = load_baseline()

        def key(v: tuple) -> str:
            p, ln = v[0], v[1]
            try:
                rel = p.relative_to(root)
            except ValueError:
                rel = p
            return f"{rel.as_posix()}:{ln}"

        new_violations = [v for v in all_violations if key(v) not in baseline]
        if new_violations:
            print("Retained-async rule: NEW violations (not in baseline):")
            for path, line_num, pattern, line in new_violations:
                print(f"  {path}:{line_num} [{pattern}]")
                print(f"    {line[:80]}{'...' if len(line) > 80 else ''}")
            print(f"\nBaseline: {BASELINE_FILE}. Add path:line to allow, or fix the violation.")
            return 1
        return 0

    if all_violations:
        print("Retained-async rule violations:")
        for path, line_num, pattern, line in all_violations:
            print(f"  {path}:{line_num} [{pattern}]")
            print(f"    {line[:80]}{'...' if len(line) > 80 else ''}")
        if "--baseline" in sys.argv:
            print("\n# Add these to .ci/retained_async_baseline.txt to allow (use --baseline-file for enforcement):")
            for path, line_num, _, _ in all_violations:
                print(f"  {path}:{line_num}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
