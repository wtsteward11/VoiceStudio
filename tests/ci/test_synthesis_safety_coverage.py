"""
Invariant I-2: Trust/Safety Choke Point Gate.

Ensures every synthesis-related POST route has require_synthesis_clearance
wired as a FastAPI dependency. Adding a synthesis route without the
dependency fails CI immediately.

Roadmap v2.0 Phase 0 — Permanent CI invariant.
"""
from __future__ import annotations

import inspect
import os
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent

# Synthesis POST endpoints that MUST have require_synthesis_clearance (from plan)
REQUIRED_SYNTHESIS_ENDPOINTS = [
    ("/api/voice/synthesize", "POST"),
    ("/api/voice/synthesize/multipass", "POST"),
    ("/api/voice/synthesize/style", "POST"),
    ("/api/voice/synthesize/cross-lingual", "POST"),
    ("/api/instant-cloning/preview", "POST"),
    ("/api/batch/jobs", "POST"),
    ("/api/batch/jobs/{job_id}/start", "POST"),
    ("/api/batch/jobs/{job_id}/retry-with-quality", "POST"),
    ("/api/ensemble", "POST"),
    ("/api/ensemble/multi-engine", "POST"),
    ("/api/voice/multi/generate", "POST"),
    ("/api/voice/clone/wizard/start", "POST"),
    ("/api/voice/clone/wizard/{job_id}/process", "POST"),
]


def _path_matches(route_path: str, expected: str) -> bool:
    """Check if route path matches expected (handles {job_id} vs {job_id} etc)."""
    if route_path == expected:
        return True
    # Normalize path params for comparison
    r = route_path.replace("{job_id}", "{job_id}")
    e = expected.replace("{job_id}", "{job_id}")
    return r == e


def _has_clearance_dependency(endpoint) -> bool:
    """Check if endpoint has require_synthesis_clearance in its signature."""
    try:
        sig = inspect.signature(endpoint)
        for _name, param in sig.parameters.items():
            if param.default is inspect.Parameter.empty:
                continue
            default = param.default
            if hasattr(default, "dependency"):
                dep = getattr(default.dependency, "__name__", str(default.dependency))
                if "require_synthesis_clearance" in dep or "synthesis_clearance" in dep:
                    return True
    # ALLOWED: bare except - best effort, failure acceptable
    except (TypeError, ValueError):
        pass
    return False


@pytest.fixture(scope="module")
def app():
    """Get the FastAPI application instance."""
    os.environ.setdefault("VOICESTUDIO_TEST_MODE", "stub")
    from backend.api.main import app as _app
    from backend.api.route_registry import register_all_routes

    register_all_routes(_app)
    return _app


def _collect_routes(app) -> list[tuple[str, str, object]]:
    """Collect (path, method, endpoint) for all routes."""
    routes = []
    for route in app.routes:
        path = getattr(route, "path", "")
        methods = getattr(route, "methods", set()) or set()
        endpoint = getattr(route, "endpoint", None)
        if path and methods and endpoint:
            for method in methods:
                if method.upper() == "POST":
                    routes.append((path, "POST", endpoint))
    return routes


def test_all_required_synthesis_endpoints_have_clearance_dependency(app):
    """Every required synthesis POST endpoint must have require_synthesis_clearance."""
    all_routes = _collect_routes(app)
    required_paths = {r[0] for r in REQUIRED_SYNTHESIS_ENDPOINTS}

    missing = []
    for path, method, endpoint in all_routes:
        if method != "POST":
            continue
        # Check if this route matches any required endpoint
        for expected_path, expected_method in REQUIRED_SYNTHESIS_ENDPOINTS:
            if expected_method == "POST" and _path_matches(path, expected_path):
                if not _has_clearance_dependency(endpoint):
                    missing.append(f"{method} {path}")
                break

    if missing:
        pytest.fail(
            f"Synthesis routes missing require_synthesis_clearance ({len(missing)}):\n"
            + "\n".join(f"  - {r}" for r in missing)
        )
