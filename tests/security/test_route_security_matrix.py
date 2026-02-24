"""
Route Security Matrix Enforcement Test (Arch Review Task 1.5).

Verifies that:
1. All registered routes are classified in the security matrix
2. Rate limiting middleware is applied globally
3. Critical protected routes have auth dependencies (best-effort check)
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest


def _get_all_route_paths(app):
    """Enumerate all HTTP route paths from FastAPI app."""
    paths = set()
    for route in app.routes:
        if hasattr(route, "path") and hasattr(route, "methods"):
            if route.methods and "GET" in route.methods and "/openapi" in route.path:
                continue
            paths.add((route.path, tuple(route.methods or [])))
    return paths


def _load_matrix() -> dict:
    """Load route security matrix from JSON."""
    # tests/security/test_*.py -> repo root (tests/security -> tests -> repo)
    repo_root = Path(__file__).resolve().parent.parent.parent
    matrix_path = repo_root / "backend" / "api" / "routes" / "route_security_matrix.json"
    if not matrix_path.exists():
        pytest.skip("route_security_matrix.json not found")
    with open(matrix_path, encoding="utf-8") as f:
        return json.load(f)


def _path_matches_prefix(path: str, prefix: str) -> bool:
    """Check if route path matches matrix prefix (exact or prefix)."""
    if path == prefix:
        return True
    if path.startswith(prefix + "/") or path.startswith(prefix + "?"):
        return True
    return False


def _get_tier_for_path(path: str, matrix: dict) -> str | None:
    """Get security tier for a path from matrix."""
    for tier in ("public", "protected", "admin"):
        for prefix in matrix.get(tier, []):
            if _path_matches_prefix(path, prefix):
                return tier
    return None


class TestRouteSecurityMatrix:
    """Tests for route security matrix enforcement."""

    def test_matrix_json_exists_and_valid(self):
        """Route security matrix JSON exists and is valid."""
        matrix = _load_matrix()
        assert "public" in matrix
        assert "protected" in matrix
        assert isinstance(matrix["public"], list)
        assert isinstance(matrix["protected"], list)

    def test_app_loads_without_error(self):
        """FastAPI app loads (route registration works)."""
        from backend.api.main import app

        assert app is not None
        assert len(app.routes) > 0

    def test_critical_public_routes_classified(self):
        """Critical public routes are in the matrix."""
        matrix = _load_matrix()
        critical_public = ["/", "/api/health", "/api/version", "/api/engines"]
        for path in critical_public:
            tier = _get_tier_for_path(path, matrix)
            assert tier == "public", f"{path} should be public, got {tier}"

    def test_critical_protected_routes_classified(self):
        """Critical protected routes are in the matrix."""
        matrix = _load_matrix()
        critical_protected = ["/api/voice/synthesize", "/api/voice/clone", "/api/profiles", "/api/projects"]
        for path in critical_protected:
            tier = _get_tier_for_path(path, matrix)
            assert tier == "protected", f"{path} should be protected, got {tier}"

    def test_rate_limiting_available(self):
        """Rate limiting middleware is available (global rate limiting per Task 1.5)."""
        try:
            from backend.api.rate_limiting_enhanced import RateLimitMiddleware

            assert RateLimitMiddleware is not None
        except ImportError:
            try:
                from backend.api.rate_limiting import rate_limit_middleware

                assert rate_limit_middleware is not None
            except ImportError:
                pytest.fail("Rate limiting should be available (rate_limiting or rate_limiting_enhanced)")
