"""
CI gate: real-mode golden-loop smoke (nightly/workflow_dispatch only).

Same flow as test_golden_loop_smoke.py but with VOICESTUDIO_TEST_MODE=real.
Requires real engines/models and voice consent; excluded from default CI.
Fails (no skip) when prerequisites are missing.

Run: python -m pytest tests/ci/test_golden_loop_smoke_real.py -v -m nightly
Excluded from default: pytest -m "not nightly"
"""
from __future__ import annotations

import io
import struct

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app


@pytest.fixture(autouse=True)
def real_mode(monkeypatch):
    """Scope VOICESTUDIO_TEST_MODE=real to this module (requires engines/consent)."""
    monkeypatch.setenv("VOICESTUDIO_TEST_MODE", "real")


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


@pytest.mark.nightly
@pytest.mark.asyncio
async def test_golden_loop_real_health_synthesize_stream(client: AsyncClient) -> None:
    """
    Real-mode golden loop: health → profile → synthesize → stream.

    Requires real piper engine and voice consent. Fails when missing.
    """
    resp = await client.get("/api/health")
    assert resp.status_code == 200, f"Health failed: {resp.status_code} - {resp.text}"
    data = resp.json()
    assert data.get("status") in ("healthy", "ok", "running")

    profile_resp = await client.post(
        "/api/profiles",
        json={"name": "golden-loop-real-smoke", "description": "Nightly real-mode smoke"},
    )
    if profile_resp.status_code not in (200, 201):
        pytest.fail(
            f"Real-mode golden loop requires profile creation. Missing: backend healthy. "
            f"Profile creation failed: {profile_resp.status_code} - {profile_resp.text[:200]}"
        )
    profile_data = profile_resp.json()
    profile_id = profile_data.get("id") or profile_data.get("profile_id")
    assert profile_id, f"No profile id in response: {profile_data}"

    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "piper",
            "text": "Real-mode golden loop smoke.",
            "language": "en",
        },
    )
    if synth_resp.status_code not in (200, 201, 202):
        pytest.fail(
            f"Real-mode golden loop requires: piper engine, voice consent. "
            f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:200]}"
        )
    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id in synthesis response: {synth_data}"

    stream_resp = await client.get(f"/api/audio/file/{audio_id}")
    assert stream_resp.status_code == 200, (
        f"Audio stream failed: {stream_resp.status_code} - {stream_resp.text[:200]}"
    )
    assert len(stream_resp.content) > 100, (
        f"Audio stream empty or too small: {len(stream_resp.content)} bytes"
    )
    assert stream_resp.content[:4] == b"RIFF", "Response is not valid WAV"


@pytest.mark.nightly
@pytest.mark.asyncio
async def test_golden_loop_real_upload_stream(client: AsyncClient) -> None:
    """
    Real-mode golden loop: upload WAV → stream.

    Upload path requires backend healthy. Fails when missing.
    """
    wav_bytes = _make_wav_bytes()
    upload_resp = await client.post(
        "/api/audio/upload",
        files={"file": ("test.wav", io.BytesIO(wav_bytes), "audio/wav")},
    )
    if upload_resp.status_code not in (200, 201):
        pytest.fail(
            f"Real-mode golden loop requires backend upload endpoint. "
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
