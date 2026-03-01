#!/usr/bin/env python3
# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Route Boundary Audit Script.

Identifies route files that directly import from app.core.engines,
violating the service layer boundary defined in ADR-008.

Usage:
    python scripts/audit_route_boundaries.py
    python scripts/audit_route_boundaries.py --fix  # Show fix suggestions
"""

import re
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

# Forbidden patterns for route files
FORBIDDEN_PATTERNS = [
    (r"from app\.core\.engines", "Direct engine import - use EngineService"),
    (r"import app\.core\.engines", "Direct engine import - use EngineService"),
]

# Allowed patterns (exemptions)
ALLOWED_EXEMPTIONS = [
    "engine_audit.py",  # Auditing tool, legitimately needs engine access
    "engines.py",       # Engine listing endpoint, may need direct access
    "engine.py",        # Engine management endpoint
]


def audit_file(path: Path) -> list[tuple[int, str, str]]:
    """
    Audit a single file for boundary violations.

    Returns:
        List of (line_number, pattern_description, matched_line)
    """
    violations = []

    try:
        content = path.read_text(encoding="utf-8")
        lines = content.split("\n")
    except Exception as e:
        print(f"Warning: Could not read {path}: {e}", file=sys.stderr)
        return violations

    for pattern, description in FORBIDDEN_PATTERNS:
        for i, line in enumerate(lines, start=1):
            if re.search(pattern, line):
                violations.append((i, description, line.strip()))

    return violations


def get_route_files() -> list[Path]:
    """Get all Python files in the routes directory."""
    routes_dir = PROJECT_ROOT / "backend" / "api" / "routes"
    return list(routes_dir.glob("*.py"))


def main():
    show_fix = "--fix" in sys.argv

    print("=" * 70)
    print("Route Boundary Audit (ADR-008)")
    print("=" * 70)
    print()

    route_files = get_route_files()
    print(f"Scanning {len(route_files)} route files...")
    print()

    total_violations = 0
    files_with_violations = 0

    for path in sorted(route_files):
        # Check exemptions
        if path.name in ALLOWED_EXEMPTIONS:
            continue

        violations = audit_file(path)

        if violations:
            files_with_violations += 1
            print(f"  {path.name}:")
            for line_num, desc, line in violations:
                print(f"    Line {line_num}: {desc}")
                print(f"      {line[:60]}...")
                total_violations += 1
            print()

    print("-" * 70)
    print(f"Total: {total_violations} violations in {files_with_violations} files")
    print("-" * 70)

    if total_violations > 0 and show_fix:
        print()
        print("SUGGESTED FIX PATTERN:")
        print()
        print("  # BEFORE (violation):")
        print("  from app.core.engines.router import get_engine, get_available_engines")
        print()
        print("  # AFTER (correct):")
        print("  from backend.services.engine_service import get_engine_service")
        print()
        print("  engine_service = get_engine_service()")
        print("  engines = engine_service.list_engines()")
        print("  engine = engine_service.get_engine(engine_id)")
        print()
        print("See backend/services/engine_service.py for available methods.")

    if total_violations > 0:
        print()
        print("NOTE: These violations are documented for tracking (TD-023).")
        print("Migration will be done incrementally per ADR-008.")
        return 0  # Don't fail build, just report
    else:
        print()
        print("No boundary violations found!")
        return 0


if __name__ == "__main__":
    sys.exit(main())
