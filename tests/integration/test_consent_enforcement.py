"""Consent enforcement integration tests (Task 9)."""
from __future__ import annotations

import pytest


def _get_test_client():
    from fastapi.testclient import TestClient

    from backend.api.main import app

    return TestClient(app)


def test_synthesis_with_invalid_consent_id_returns_403():
    """Invalid consent_id must return 403 when profile requires consent."""
    client = _get_test_client()
    resp = client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": "external/voice_001",
            "text": "Hello world",
            "engine": "piper",
            "consent_id": "definitely_fake_consent_id_xxxxxxxx",
        },
    )
    assert resp.status_code == 403, (
        f"Expected 403 for fake consent_id, got {resp.status_code}: {resp.text[:200]}"
    )


def test_synthesis_without_consent_on_third_party_profile_returns_403():
    """Omitting consent_id on a non-local profile must return 403, not 200."""
    client = _get_test_client()
    resp = client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": "external/voice_001",
            "text": "Hello world",
            "engine": "piper",
        },
    )
    assert resp.status_code in (403, 422), (
        f"Expected 403 or 422, got {resp.status_code}: {resp.text[:200]}"
    )


def test_synthesis_in_demo_mode_returns_403(monkeypatch):
    """VOICESTUDIO_DEMO_MODE=true must block synthesis."""
    monkeypatch.setenv("VOICESTUDIO_DEMO_MODE", "true")
    client = _get_test_client()
    resp = client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": "any_profile",
            "text": "Hello world",
            "engine": "piper",
        },
    )
    assert resp.status_code == 403, (
        f"Expected 403 in demo mode, got {resp.status_code}: {resp.text[:200]}"
    )
