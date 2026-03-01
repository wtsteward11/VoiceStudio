#!/usr/bin/env python3
"""CI check: Verify no route file directly imports from app.core.engines.

Routes must use backend.services.engine_gateway instead.
Direct engine imports bypass the circuit breaker, warm pool, and fallback chain.

Exit code 0 = clean, 1 = violations found.
"""

from __future__ import annotations

import ast
import sys
from pathlib import Path

ROUTES_DIR = Path(__file__).parent.parent / "backend" / "api" / "routes"
FORBIDDEN_PREFIX = "app.core.engines"
ALLOWED_FILES = {"engine_audit.py"}  # Audit route is allowed to inspect engines


def check_file(filepath: Path) -> list[str]:
    """Check a single file for forbidden imports."""
    violations = []
    try:
        tree = ast.parse(filepath.read_text(encoding="utf-8"), filename=str(filepath))
    except SyntaxError:
        return []

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                if alias.name.startswith(FORBIDDEN_PREFIX):
                    violations.append(
                        f"  {filepath.name}:{node.lineno} - import {alias.name}"
                    )
        elif isinstance(node, ast.ImportFrom):
            if node.module and node.module.startswith(FORBIDDEN_PREFIX):
                names = ", ".join(a.name for a in node.names)
                violations.append(
                    f"  {filepath.name}:{node.lineno} - from {node.module} import {names}"
                )

    return violations


def main() -> int:
    if not ROUTES_DIR.exists():
        print(f"Routes directory not found: {ROUTES_DIR}")
        return 1

    all_violations: list[str] = []

    for py_file in sorted(ROUTES_DIR.glob("*.py")):
        if py_file.name.startswith("_") or py_file.name in ALLOWED_FILES:
            continue
        violations = check_file(py_file)
        all_violations.extend(violations)

    if all_violations:
        print(f"FAIL: {len(all_violations)} direct engine import(s) in route files:")
        print(f"  Routes must use backend.services.engine_gateway instead.\n")
        for v in all_violations:
            print(v)
        return 1

    print("PASS: No direct engine imports in route files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
