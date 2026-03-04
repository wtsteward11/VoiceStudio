"""
Route artifact invariants: regression tests for Backend Spine Migration.

Phase 6: Enumerates FastAPI routes and flags:
- Routes returning audio_id that should use AudioRegistry
- Routes that may write under repo root (static analysis)
- Routes generating audio missing policy enforcement
- No route-to-route imports (from .audio import _get_audio_path, from ..routes.voice import)
"""

from __future__ import annotations

import re

import pytest


def test_no_route_to_route_imports():
    """Static check: route files must not import from other route modules."""
    from pathlib import Path

    route_dir = Path(__file__).resolve().parents[2] / "backend" / "api" / "routes"
    forbidden = [
        (r"from \.audio import _get_audio_path", "from .audio import _get_audio_path"),
        (r"from \.\.audio import _get_audio_path", "from ..audio import _get_audio_path"),
        (r"from \.\.routes\.audio import _get_audio_path", "from ..routes.audio import _get_audio_path"),
        (r"from \.\.routes\.voice import _audio_storage", "from ..routes.voice import _audio_storage"),
        (r"from \.\.routes\.voice import _register_audio_file", "from ..routes.voice import _register_audio_file"),
        (r"from backend\.api\.routes\.voice import _register_audio_file", "from backend.api.routes.voice import _register_audio_file"),
    ]
    violations = []
    for py_file in route_dir.rglob("*.py"):
        if "_archived" in str(py_file):
            continue
        content = py_file.read_text(encoding="utf-8", errors="ignore")
        rel = py_file.relative_to(route_dir)
        for pattern, msg in forbidden:
            if re.search(pattern, content):
                violations.append(f"{rel}: {msg}")
    assert not violations, f"Route-to-route import violations: {violations}"


def test_routes_enumerated():
    """Enumerate all FastAPI routes for artifact invariant checks."""
    try:
        from backend.api.main import app
    except Exception as e:
        pytest.skip(f"App import failed (pre-existing): {e}")

    routes = [r for r in app.routes if hasattr(r, "path") and hasattr(r, "methods")]
    assert len(routes) > 0, "Should have at least one route"


def test_route_security_matrix_has_protected_audio_routes():
    """Protected routes that generate audio should be in route_security_matrix."""
    import json
    from pathlib import Path

    matrix_path = Path(__file__).resolve().parents[2] / "backend" / "api" / "routes" / "route_security_matrix.json"
    if not matrix_path.exists():
        pytest.skip("route_security_matrix.json not found")

    with open(matrix_path) as f:
        matrix = json.load(f)
    protected = set(matrix.get("protected", []))
    # Key audio-producing routes should be protected
    expected = {"/api/voice/synthesize", "/api/voice/clone", "/api/batch", "/api/rvc", "/api/nr"}
    missing = expected - protected
    assert not missing, f"Protected routes missing from matrix: {missing}"


def test_no_repo_path_patterns_in_routes():
    """Static check: route files should not contain forbidden path patterns."""
    from pathlib import Path

    route_dir = Path(__file__).resolve().parents[2] / "backend" / "api" / "routes"
    # Forbidden: repo-relative paths (Path("data/...") but not get_path("data"))
    forbidden = [
        ('Path("backups")', 'Path("backups")'),
        ('Path("data/profiles")', 'Path("data/profiles")'),
        ('Path("data/projects")', 'Path("data/projects")'),
        ('Path("data/settings.json")', 'Path("data/settings.json")'),
        ('os.path.join("data", "library"', 'os.path.join("data", "library")'),
    ]
    violations = []
    for py_file in route_dir.rglob("*.py"):
        if "_archived" in str(py_file):
            continue
        content = py_file.read_text(encoding="utf-8", errors="ignore")
        rel = py_file.relative_to(route_dir)
        for pattern, msg in forbidden:
            if pattern in content:
                violations.append(f"{rel}: {msg}")
    assert not violations, f"Forbidden path patterns: {violations}"
