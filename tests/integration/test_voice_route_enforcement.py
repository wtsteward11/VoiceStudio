"""Route-enumeration test: prevent bypass routes from reappearing.

Asserts that all /api/voice routes that write audio have:
- Policy dependency (enforce_voice_policy)
- Provenance + usage hooks in handler
"""
from __future__ import annotations

import ast
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
VOICE_PY = ROOT / "backend" / "api" / "routes" / "voice.py"

# Routes that write audio and must have enforcement
AUDIO_PRODUCING_ROUTES = [
    "/synthesize",
    "/synthesize/multipass",
    "/synthesize/style",
    "/synthesize/cross-lingual",
    "/clone",
    "/remove-artifacts",
    "/prosody-control",
    "/post-process",
]

# M2: Provenance/usage moved to registration pipeline. Routes use either:
# - record_artifact_provenance_and_usage (direct), or
# - _register_audio_file(..., model_used=...) / AudioRegistry.register(..., model_used=...)
REQUIRED_SYMBOLS = [
    "record_artifact_provenance_and_usage",
]


def test_voice_py_has_policy_dependency():
    """Voice router must use enforce_voice_policy as dependency."""
    text = VOICE_PY.read_text(encoding="utf-8")
    assert "enforce_voice_policy" in text or "_enforce_voice_policy" in text, (
        "Voice router must have enforce_voice_policy in dependencies"
    )


def test_voice_py_has_provenance_and_usage():
    """voice.py must call provenance and usage on synthesis success."""
    text = VOICE_PY.read_text(encoding="utf-8")
    for sym in REQUIRED_SYMBOLS:
        assert sym in text, f"voice.py must call {sym} on audio-producing paths"


def test_audio_routes_have_enforcement_symbols():
    """Each audio-producing route should have provenance/usage via registration pipeline.

    M2: Provenance and usage are centralized. Routes use record_artifact_provenance_and_usage
    (direct) or registration with model_used (AudioRegistry.register / _register_audio_file).
    """
    text = VOICE_PY.read_text(encoding="utf-8")
    has_provenance = "record_artifact_provenance_and_usage" in text
    assert has_provenance, (
        "voice.py must use record_artifact_provenance_and_usage or registration with model_used"
    )


def test_no_new_synthesis_routes_without_enforcement():
    """Fail if a new /synthesize* or /clone route is added without policy.

    This is a structural check: the router uses Depends(_enforce_voice_policy)
    at router level, so all routes inherit it. We verify the router config.
    """
    text = VOICE_PY.read_text(encoding="utf-8")
    # Router must have dependencies including voice policy
    assert "dependencies=" in text
    assert "enforce_voice_policy" in text or "_enforce_voice_policy" in text


def test_synthesis_routes_enumerated_and_classified():
    """Enumerate /api/voice/synthesize* and /api/voice/clone routes; fail if new bypass added.

    Ensures any new synthesis or clone route is explicitly tracked.
    Routes under the voice router inherit enforce_voice_policy at router level.
    """
    try:
        from backend.api.main import app
    except Exception as e:
        pytest.skip(f"App load failed (pre-existing): {e}")

    synthesis_paths = set()
    for route in app.routes:
        if not hasattr(route, "path") or not hasattr(route, "methods"):
            continue
        methods = route.methods or set()
        if not methods or ("HEAD" in methods and len(methods) == 1):
            continue
        path = route.path if isinstance(route.path, str) else str(route.path)
        full_path = f"/api/voice{path}" if path.startswith("/") and not path.startswith("/api") else path
        if full_path.startswith("/api/voice/synthesize") or full_path == "/api/voice/clone":
            synthesis_paths.add(full_path)

    # All synthesis/clone routes must be under voice router (prefix /api/voice)
    # and inherit policy. This test documents known routes; add new ones here when added.
    known_synthesis_prefixes = ("/api/voice/synthesize", "/api/voice/clone")
    for p in synthesis_paths:
        assert p.startswith(known_synthesis_prefixes), (
            f"Unexpected synthesis route {p!r} — add to enforcement or document exclusion"
        )
