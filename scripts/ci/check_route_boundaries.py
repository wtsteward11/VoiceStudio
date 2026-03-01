#!/usr/bin/env python3
"""
CI check: fail if routes reintroduce prototype soup behaviors.

Scans backend/api/routes/**/*.py for:
- Route-to-route imports (from ..routes, from .voice import synthesize, etc.)
- sys.path.insert usage
- Repo-relative persistent writes (Path("backups"), Path("data"), os.path.join("data",...), open("data/..."))

Exits 0 if clean; 1 if violations found.
Output: file:line: PATTERN_DESC: <matched_line>

Migration bypass: set VOICESTUDIO_SKIP_ROUTE_BOUNDARIES=1 to skip (local use only; CI does not set it).
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

if os.environ.get("VOICESTUDIO_SKIP_ROUTE_BOUNDARIES") == "1":
    sys.exit(0)

# (pattern, description) - order matters for reporting
VIOLATION_PATTERNS = [
    (r"from\s+\.\.?routes\b", "route-to-route import (from ..routes)"),
    (r"from\s+\.(voice|audio|voice_morph|prosody|style_transfer|ensemble)\s+import", "route-to-route import (from .<route>)"),
    (r"sys\.path\.insert\s*\(", "sys.path.insert (use proper module imports)"),
    (r'Path\s*\(\s*["\']backups["\']\s*\)', "repo-relative Path(backups) - use get_path"),
    (r'Path\s*\(\s*["\']data["\']\s*\)', "repo-relative Path(data) - use get_path"),
    (r'Path\s*\(\s*["\']data/', "repo-relative Path(data/...) - use get_path"),
    (r'os\.path\.join\s*\(\s*["\']data["\']\s*', "repo-relative os.path.join(data,...) - use get_path"),
    (r'open\s*\(\s*["\']data/', "repo-relative open(data/...) - use get_path"),
    (r'os\.makedirs\s*\(\s*["\']data', "repo-relative os.makedirs(data...) - use get_path"),
]

EXEMPTION_SUBSTRINGS = ("tempfile", "get_path(", "backend.config.path_config")


def get_route_files() -> list[Path]:
    """Return Python files in routes, excluding _archived and voice_monolith_backup.py."""
    routes_dir = ROOT / "backend" / "api" / "routes"
    if not routes_dir.exists():
        return []
    files = list(routes_dir.rglob("*.py"))
    return [
        f
        for f in files
        if "_archived" not in str(f) and f.name != "voice_monolith_backup.py"
    ]


def is_exempt(line: str) -> bool:
    """Skip lines that use tempfile or get_path (allowed patterns)."""
    return any(ex in line for ex in EXEMPTION_SUBSTRINGS)


def audit_file(path: Path) -> list[tuple[int, str, str]]:
    """Return [(line_num, desc, line), ...] for violations."""
    violations = []
    try:
        content = path.read_text(encoding="utf-8")
        lines = content.split("\n")
    except Exception:
        return violations

    for i, line in enumerate(lines, start=1):
        if is_exempt(line):
            continue
        for pattern, desc in VIOLATION_PATTERNS:
            if re.search(pattern, line):
                violations.append((i, desc, line.strip()))
                break

    return violations


def main() -> int:
    route_files = get_route_files()
    all_violations: list[tuple[Path, int, str, str]] = []

    for path in sorted(route_files):
        rel = path.relative_to(ROOT)
        for line_num, desc, line in audit_file(path):
            all_violations.append((path, line_num, desc, line))

    if all_violations:
        for path, line_num, desc, line in all_violations:
            rel = path.relative_to(ROOT)
            snippet = (line[:80] + "..." if len(line) > 80 else line)
            print(f"{rel}:{line_num}: {desc}: {snippet}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
