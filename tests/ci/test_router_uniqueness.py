"""
Invariant I-1: Router Uniqueness Gate.

Ensures:
- No two routes share the same HTTP method + path combination
- The voice router is registered from exactly one source module
- (After Phase A): backend/api/routes/voice.py does not exist on disk

Roadmap v2.0 Phase 0 — Permanent CI invariant.
"""
from __future__ import annotations

import os
from collections import defaultdict
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent


@pytest.fixture(scope="module")
def app():
    """Get the FastAPI application instance."""
    os.environ.setdefault("VOICESTUDIO_TEST_MODE", "stub")
    from backend.api.main import app as _app
    from backend.api.route_registry import register_all_routes

    register_all_routes(_app)
    return _app


def test_no_duplicate_routes(app):
    """Assert no two routes share the same method + path combination."""
    seen: dict[str, list[str]] = defaultdict(list)
    duplicates = []

    for route in app.routes:
        path = getattr(route, "path", None)
        methods = getattr(route, "methods", None)
        if path is None or methods is None:
            continue
        for method in methods:
            key = f"{method.upper()} {path}"
            seen[key].append(getattr(route, "endpoint", route).__qualname__)

    for key, endpoints in seen.items():
        if len(endpoints) > 1:
            duplicates.append(f"{key} registered by: {endpoints}")

    assert not duplicates, (
        f"Duplicate routes detected ({len(duplicates)}):\n"
        + "\n".join(f"  - {d}" for d in duplicates)
    )


CORE_VOICE_PATHS = {
    "/api/voice/synthesize",
    "/api/voice/clone",
    "/api/voice/profiles",
    "/api/voice/tts",
}

SEPARATE_VOICE_MODULES = {
    "backend.api.routes.voice_browser",
    "backend.api.routes.voice_morph",
    "backend.api.routes.voice_effects",
    "backend.api.routes.voice_speech",
    "backend.api.routes.voice_cloning_wizard",
    "backend.api.routes.multi_voice_generator",
    "backend.api.routes.gateway_aliases",
}


def test_voice_router_single_source(app):
    """Assert core voice synthesis routes come from one source family.

    The concern is dual routing: voice.py (monolith) vs voice/ (split package).
    Separate modules like voice_browser, voice_morph are distinct domains
    and are excluded from this check.
    """
    voice_route_modules = set()

    for route in app.routes:
        path = getattr(route, "path", "")
        if not path.startswith("/api/voice"):
            continue
        endpoint = getattr(route, "endpoint", None)
        if endpoint is None:
            continue
        module = getattr(endpoint, "__module__", "unknown")

        if module in SEPARATE_VOICE_MODULES:
            continue
        if module in ("backend.api.main", "backend.api.route_registry"):
            continue

        voice_route_modules.add(module)

    source_families = set()
    for mod in voice_route_modules:
        if mod.startswith("backend.api.routes.voice."):
            source_families.add("voice-package")
        elif mod == "backend.api.routes.voice":
            source_families.add("voice-monolith")
        else:
            source_families.add(mod)

    assert len(source_families) <= 1, (
        f"Core voice routes registered from multiple source families: {source_families}. "
        "voice.py (monolith) and voice/ (package) must not coexist. "
        "Phase A will resolve this."
    )


def test_voice_py_god_route_deleted():
    """Assert backend/api/routes/voice.py does not exist on disk.

    Phase A3 completed: voice.py deleted, all endpoints in voice/ package.
    """
    god_route = PROJECT_ROOT / "backend" / "api" / "routes" / "voice.py"
    assert not god_route.exists(), (
        f"God-route voice.py still exists ({god_route.stat().st_size:,} bytes). "
        "Phase A3 requires deletion after migrating all endpoints to voice/ submodules."
    )


def test_mediator_layer_deleted():
    """Phase B: backend/application/ must not contain .py files (ADR-046).

    The mediator/CQRS layer was deleted. If the directory exists (e.g. __pycache__
    ghost), it must contain zero .py source files. Prevents resurrection.
    """
    app_dir = PROJECT_ROOT / "backend" / "application"
    py_files = list(app_dir.rglob("*.py")) if app_dir.exists() else []
    assert not py_files, (
        f"backend/application/ contains .py files (ADR-046 violation): "
        f"{[str(p.relative_to(PROJECT_ROOT)) for p in py_files]}"
    )
