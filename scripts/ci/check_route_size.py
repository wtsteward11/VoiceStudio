#!/usr/bin/env python3
"""
CI check: fail if route files exceed line limit (god-route prevention).

Scans backend/api/routes/**/*.py and fails if any file exceeds MAX_LINES
unless exempted in route_size_exemptions.txt.

Exits 0 if clean; 1 if violations found.
Output: file: lines (limit: N)

Migration bypass: set VOICESTUDIO_SKIP_ROUTE_SIZE=1 to skip (local use only; CI does not set it).
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
MAX_LINES = 2000
EXEMPTIONS_FILE = Path(__file__).resolve().parent / "route_size_exemptions.txt"


def get_exemptions() -> set[str]:
    """Return set of exempt file paths (relative to backend/api/routes)."""
    if not EXEMPTIONS_FILE.exists():
        return set()
    exemptions = set()
    for line in EXEMPTIONS_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            exemptions.add(line)
    return exemptions


def get_route_files() -> list[Path]:
    """Return Python files in backend/api/routes."""
    routes_dir = ROOT / "backend" / "api" / "routes"
    if not routes_dir.exists():
        return []
    return [
        f
        for f in routes_dir.rglob("*.py")
        if "_archived" not in str(f)
    ]


def main() -> int:
    if os.environ.get("VOICESTUDIO_SKIP_ROUTE_SIZE") == "1":
        sys.exit(0)

    exemptions = get_exemptions()
    violations: list[tuple[Path, int]] = []

    for path in sorted(get_route_files()):
        routes_rel = path.relative_to(ROOT / "backend" / "api" / "routes")
        exempt_key = str(routes_rel).replace("\\", "/")
        if exempt_key in exemptions:
            continue
        try:
            line_count = len(path.read_text(encoding="utf-8").splitlines())
        except Exception:
            continue
        if line_count > MAX_LINES:
            violations.append((path, line_count))

    if violations:
        for path, count in violations:
            rel = path.relative_to(ROOT)
            print(f"{rel}: {count} lines (limit: {MAX_LINES})")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
