"""
CI gate: deterministic golden-loop smoke — backend reachable + upload→stream→playback path.

Proves the critical path: health → synthesize → GET /api/audio/file/{id} (same endpoint
PlayBackendAudioIdAsync uses). Uses ASGITransport for determinism (no real HTTP server).

Skips consent check via VOICESTUDIO_TEST_MODE=stub (same as golden path integration tests).

Run: python -m pytest tests/ci/test_golden_loop_smoke.py -v
"""
from __future__ import annotations

import io
import struct

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app


@pytest.fixture(autouse=True)
def stub_mode(monkeypatch):
    """Scope VOICESTUDIO_TEST_MODE=stub to this module only (avoids cross-test contamination)."""
    monkeypatch.setenv("VOICESTUDIO_TEST_MODE", "stub")


def _make_wav_bytes(num_samples: int = 4000, sample_rate: int = 22050) -> bytes:
    """Generate a minimal valid WAV file (16-bit mono PCM silence)."""
    num_channels = 1
    bits_per_sample = 16
    byte_rate = sample_rate * num_channels * bits_per_sample // 8
    block_align = num_channels * bits_per_sample // 8
    data_size = num_samples * block_align

    buf = io.BytesIO()
    buf.write(b"RIFF")
    buf.write(struct.pack("<I", 36 + data_size))
    buf.write(b"WAVE")
    buf.write(b"fmt ")
    buf.write(struct.pack("<I", 16))
    buf.write(
        struct.pack(
            "<HHIIHH",
            1,
            num_channels,
            sample_rate,
            byte_rate,
            block_align,
            bits_per_sample,
        )
    )
    buf.write(b"data")
    buf.write(struct.pack("<I", data_size))
    buf.write(b"\x00" * data_size)
    return buf.getvalue()


@pytest.fixture
async def client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c


@pytest.mark.asyncio
async def test_golden_loop_health_synthesize_stream(client: AsyncClient) -> None:
    """
    Golden loop: health → create profile → synthesize → stream audio.

    Exercises the same /api/audio/file/{id} endpoint that PlayBackendAudioIdAsync uses.
    """
    # 1. Health
    resp = await client.get("/api/health")
    assert resp.status_code == 200, f"Health failed: {resp.status_code} - {resp.text}"
    data = resp.json()
    assert data.get("status") in ("healthy", "ok", "running")

    # 2. Create profile (required for synthesis)
    profile_resp = await client.post(
        "/api/profiles",
        json={"name": "golden-loop-smoke", "description": "CI smoke test profile"},
    )
    assert profile_resp.status_code in (200, 201), (
        f"Profile creation failed: {profile_resp.status_code} - {profile_resp.text}"
    )
    profile_data = profile_resp.json()
    profile_id = profile_data.get("id") or profile_data.get("profile_id")
    assert profile_id, f"No profile id in response: {profile_data}"

    # 3. Synthesize (piper has built-in voices)
    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "piper",
            "text": "Golden loop smoke test.",
            "language": "en",
        },
    )
    assert synth_resp.status_code in (200, 201, 202), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:300]}"
    )
    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id in synthesis response: {synth_data}"

    # 4. Stream audio (same endpoint PlayBackendAudioIdAsync uses)
    stream_resp = await client.get(f"/api/audio/file/{audio_id}")
    assert stream_resp.status_code == 200, (
        f"Audio stream failed: {stream_resp.status_code} - {stream_resp.text[:200]}"
    )
    assert len(stream_resp.content) > 100, (
        f"Audio stream empty or too small: {len(stream_resp.content)} bytes"
    )
    # WAV header
    assert stream_resp.content[:4] == b"RIFF", "Response is not valid WAV"


@pytest.mark.asyncio
async def test_golden_loop_upload_stream(client: AsyncClient) -> None:
    """
    Golden loop: upload tiny WAV → parse audio_id → GET /api/audio/file/{id} → assert RIFF.

    Proves the upload→stream path (same endpoint PlayBackendAudioIdAsync uses).
    Uses ASGITransport/in-process (no server).
    """
    wav_bytes = _make_wav_bytes()
    upload_resp = await client.post(
        "/api/audio/upload",
        files={"file": ("test.wav", io.BytesIO(wav_bytes), "audio/wav")},
    )
    assert upload_resp.status_code in (200, 201), (
        f"Upload failed: {upload_resp.status_code} - {upload_resp.text[:200]}"
    )
    upload_data = upload_resp.json()
    audio_id = upload_data.get("id")
    assert audio_id, f"No id in upload response: {upload_data}"

    stream_resp = await client.get(f"/api/audio/file/{audio_id}")
    assert stream_resp.status_code == 200, (
        f"Audio stream failed: {stream_resp.status_code} - {stream_resp.text[:200]}"
    )
    assert len(stream_resp.content) > 100, (
        f"Audio stream empty or too small: {len(stream_resp.content)} bytes"
    )
    assert stream_resp.content[:4] == b"RIFF", "Response is not valid WAV"
