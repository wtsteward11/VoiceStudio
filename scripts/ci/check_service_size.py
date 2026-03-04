#!/usr/bin/env python3
"""
CI check: fail if services exceed line limit (monolith prevention).

Scans backend/services/**/*.py and fails if any file exceeds MAX_LINES
unless exempted in service_size_exemptions.txt.

Exits 0 if clean; 1 if violations found.
Output: file: lines (limit: N)

Migration bypass: set VOICESTUDIO_SKIP_SERVICE_SIZE=1 to skip (local use only; CI does not set it).
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
MAX_LINES = 1500
EXEMPTIONS_FILE = Path(__file__).resolve().parent / "service_size_exemptions.txt"


def get_exemptions() -> set[str]:
    """Return set of exempt file paths (relative to backend/services)."""
    if not EXEMPTIONS_FILE.exists():
        return set()
    exemptions = set()
    for line in EXEMPTIONS_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            exemptions.add(line)
    return exemptions


def get_service_files() -> list[Path]:
    """Return Python files in backend/services."""
    services_dir = ROOT / "backend" / "services"
    if not services_dir.exists():
        return []
    return list(services_dir.rglob("*.py"))


def main() -> int:
    if os.environ.get("VOICESTUDIO_SKIP_SERVICE_SIZE") == "1":
        sys.exit(0)

    exemptions = get_exemptions()
    violations: list[tuple[Path, int]] = []

    for path in sorted(get_service_files()):
        rel = path.relative_to(ROOT)
        # Exemption key: path relative to backend/services (e.g. plugin_service.py or subdir/file.py)
        services_rel = path.relative_to(ROOT / "backend" / "services")
        exempt_key = str(services_rel).replace("\\", "/")
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
