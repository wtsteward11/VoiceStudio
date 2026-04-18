"""
Synthesis stub route tests — VOICESTUDIO_TEST_MODE=stub.

Proves the synthesis pipeline returns a valid WAV artifact through
the stub engine path (no real ML inference). Flow:
  profile create -> synthesize -> fetch audio -> verify WAV header.

Run: python -m pytest tests/unit/backend/api/routes/test_synthesis_stub.py -v
"""
from __future__ import annotations

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app


@pytest.fixture(autouse=True)
def stub_mode(monkeypatch):
    """Enable stub synthesis mode for all tests in this module."""
    monkeypatch.setenv("VOICESTUDIO_TEST_MODE", "stub")


@pytest.fixture
async def client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c


@pytest.mark.asyncio
async def test_health_check(client: AsyncClient) -> None:
    """GET /api/health returns 200 with a healthy status."""
    resp = await client.get("/api/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("status") in ("healthy", "ok", "running")


@pytest.mark.asyncio
async def test_synthesize_stub_returns_audio(client: AsyncClient) -> None:
    """POST /api/voice/synthesize (stub mode) returns audio_id and a fetchable WAV."""
    profile_resp = await client.post(
        "/api/profiles",
        json={"name": "synth-stub-test", "description": "Synthesis stub test profile"},
    )
    assert profile_resp.status_code in (200, 201), (
        f"Profile creation failed: {profile_resp.status_code} - {profile_resp.text}"
    )
    profile_data = profile_resp.json()
    profile_id = profile_data.get("id") or profile_data.get("profile_id")
    assert profile_id, f"No profile id in response: {profile_data}"

    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "piper",
            "text": "Synthesis stub test sentence.",
            "language": "en",
        },
    )
    assert synth_resp.status_code in (200, 201, 202), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:300]}"
    )
    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id in synthesis response: {synth_data}"
    assert "audio_url" in synth_data
    assert synth_data.get("duration", 0) >= 0

    audio_resp = await client.get(f"/api/audio/file/{audio_id}")
    assert audio_resp.status_code == 200, (
        f"Audio fetch failed: {audio_resp.status_code} - {audio_resp.text[:200]}"
    )
    assert len(audio_resp.content) > 100, (
        f"Audio content too small: {len(audio_resp.content)} bytes"
    )
    assert audio_resp.content[:4] == b"RIFF", "Response is not valid WAV"


@pytest.mark.asyncio
async def test_synthesize_missing_profile_returns_error(client: AsyncClient) -> None:
    """POST /api/voice/synthesize with a nonexistent profile returns an error."""
    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": "nonexistent-profile-00000",
            "engine": "piper",
            "text": "Should fail.",
            "language": "en",
        },
    )
    assert synth_resp.status_code >= 400


@pytest.mark.asyncio
async def test_synthesize_empty_text_returns_422(client: AsyncClient) -> None:
    """POST /api/voice/synthesize with empty text returns 422 validation error."""
    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": "some-profile-id",
            "engine": "piper",
            "text": "",
            "language": "en",
        },
    )
    assert synth_resp.status_code == 422
