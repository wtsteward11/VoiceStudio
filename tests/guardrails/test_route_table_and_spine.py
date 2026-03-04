"""
Guardrail tests: app import, route enumeration, artifact spine compliance.

These tests ensure:
- The FastAPI app imports successfully
- At least one /api/voice route exists
- Route source files pass artifact spine compliance (no forbidden patterns)

Expected first run: failures due to existing violations. M6 builds the guardrails;
fixing violations is a separate milestone.
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent


def _load_compliance_module():
    """Load check_artifact_spine_compliance module for reuse."""
    path = ROOT / "scripts" / "ci" / "check_artifact_spine_compliance.py"
    spec = importlib.util.spec_from_file_location("check_artifact_spine_compliance", path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load compliance module from {path}")
    mod = importlib.util.module_from_spec(spec)
    sys.modules["check_artifact_spine_compliance"] = mod
    spec.loader.exec_module(mod)
    return mod


def test_app_imports_successfully():
    """App must import without error. No skip on failure."""
    from backend.api.main import app

    assert app is not None


def test_voice_routes_exist():
    """At least one /api/voice route must exist."""
    from backend.api.main import app

    paths = [r.path for r in app.routes if hasattr(r, "path")]
    voice_paths = [p for p in paths if "/voice" in p]
    assert len(voice_paths) >= 1, "No /api/voice routes found"


def test_route_source_files_pass_compliance():
    """Route source files must pass artifact spine compliance."""
    mod = _load_compliance_module()
    audit_file_ast = mod.audit_file_ast
    get_route_files = mod.get_route_files

    all_violations: list[tuple[Path, int, str, str]] = []
    for path in get_route_files():
        for line_num, rule_id, snippet in audit_file_ast(path):
            all_violations.append((path, line_num, rule_id, snippet))

    if all_violations:
        rel_root = ROOT
        lines = []
        for path, line_num, rule_id, snippet in all_violations:
            try:
                rel = path.relative_to(rel_root)
            except ValueError:
                rel = path
            lines.append(f"{rel}:{line_num}:{rule_id}: {snippet}")
        pytest.fail(
            "Artifact spine compliance violations in route source files:\n" + "\n".join(lines)
        )
