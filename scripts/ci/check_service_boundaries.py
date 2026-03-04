#!/usr/bin/env python3
"""
CI check: fail if services import from API layer.

Scans backend/services/**/*.py for:
- from backend.api.* (any submodule: routes, ws, utils, ml_optimization, etc.)
- import backend.api.*

Services must not depend on the API layer. Use storage abstractions and
injectable interfaces (e.g. training_broadcaster) instead.
Exceptions: see ALLOWED_API_IMPORTS for Phase 2.3 tech debt.

Exits 0 if clean; 1 if violations found.
Output: file:line: PATTERN_DESC: <matched_line>

Migration bypass: set VOICESTUDIO_SKIP_SERVICE_BOUNDARIES=1 to skip (local use only; CI does not set it).
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

if os.environ.get("VOICESTUDIO_SKIP_SERVICE_BOUNDARIES") == "1":
    sys.exit(0)

# (pattern, description) - matches any backend.api.* import
VIOLATION_PATTERNS = [
    (r"from\s+backend\.api\.", "service imports api (from backend.api.*)"),
    (r"import\s+backend\.api\.", "service imports api (import backend.api.*)"),
]

# Module prefixes that services may temporarily import from api (Phase 2.3 cleanup).
ALLOWED_API_IMPORTS = [
    "backend.api.models_additional",
    "backend.api.exceptions",
    "backend.api.middleware.correlation_id",
    "backend.api.utils.instrumentation",
]


def _extract_imported_module(line: str, pattern: str) -> str | None:
    """Extract the full module path from an import line, or None if not matched."""
    if "from" in pattern:
        m = re.search(r"from\s+(backend\.api\.\S+?)\s+import", line)
        return m.group(1) if m else None
    m = re.search(r"import\s+(backend\.api\.\S+?)(?:\s|$|,|#|as)", line)
    return m.group(1).rstrip(",") if m else None


def _is_allowed(module_path: str) -> bool:
    """Return True if this module is in the allowlist."""
    return any(module_path == prefix or module_path.startswith(prefix + ".") for prefix in ALLOWED_API_IMPORTS)


def get_service_files() -> list[Path]:
    """Return Python files in backend/services."""
    services_dir = ROOT / "backend" / "services"
    if not services_dir.exists():
        return []
    return list(services_dir.rglob("*.py"))


def audit_file(path: Path) -> list[tuple[int, str, str]]:
    """Return [(line_num, desc, line), ...] for violations."""
    violations = []
    try:
        content = path.read_text(encoding="utf-8")
        lines = content.split("\n")
    except Exception:
        return violations

    for i, line in enumerate(lines, start=1):
        for pattern, desc in VIOLATION_PATTERNS:
            if re.search(pattern, line):
                module_path = _extract_imported_module(line, pattern)
                if module_path and _is_allowed(module_path):
                    continue
                violations.append((i, desc, line.strip()))
                break

    return violations


def main() -> int:
    service_files = get_service_files()
    all_violations: list[tuple[Path, int, str, str]] = []

    for path in sorted(service_files):
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
