"""
Real XTTS v2 synthesis via ASGI (non-stub).

Proves: POST /api/profiles -> POST /api/voice/synthesize (engine=xtts_v2) ->
GET /api/audio/file/{audio_id} -> valid WAV with non-silent PCM and duration >= 0.5s.

Skips (does not fail the suite) when:
- VOICESTUDIO_TEST_MODE is stub-like (cannot prove non-stub path).
- Coqui TTS is not importable.
- Optional: XTTS model cache not present (first-run would download; skip with hint).

Run explicitly (slow, may require GPU/CPU and models):
  python -m pytest tests/integration/test_synthesis_xtts_real.py -v --tb=short
"""

from __future__ import annotations

import os
import struct
import sys
import wave
from io import BytesIO
from pathlib import Path

import pytest
from httpx import ASGITransport, AsyncClient


def _stub_like_mode() -> bool:
    v = os.environ.get("VOICESTUDIO_TEST_MODE", "").strip().lower()
    return v in ("1", "true", "yes", "stub")


def _coqui_import_error() -> str | None:
    try:
        from TTS.api import TTS  # noqa: F401, PLC0415

        _ = TTS
    except ImportError as e:
        return f"Coqui TTS not importable: {e}"
    return None


def _xtts_model_hint_missing() -> str | None:
    """
    Soft gate: on Windows, require a visible VoiceStudio XTTS cache dir unless the
    operator explicitly allows a first-run download in this test process.
    """
    if sys.platform != "win32":
        return None
    if os.environ.get("VOICESTUDIO_ALLOW_XTTS_DOWNLOAD_IN_TEST", "").strip() == "1":
        return None
    pd = os.environ.get("PROGRAMDATA", r"C:\ProgramData")
    xtts_dir = Path(pd) / "VoiceStudio" / "models" / "xtts_v2"
    if xtts_dir.is_dir() and any(xtts_dir.iterdir()):
        return None
    return (
        f"No XTTS cache under {xtts_dir}. Populate models, or set "
        "VOICESTUDIO_ALLOW_XTTS_DOWNLOAD_IN_TEST=1 to allow a first-run download."
    )


def _wav_duration_and_peak(wav_bytes: bytes) -> tuple[float, int]:
    with wave.open(BytesIO(wav_bytes), "rb") as w:
        nchan = w.getnchannels()
        sw = w.getsampwidth()
        nframes = w.getnframes()
        rate = w.getframerate()
        frames = w.readframes(nframes)
        duration = nframes / float(rate) if rate else 0.0
    if sw != 2:
        raise AssertionError(f"Expected 16-bit WAV, got sample width {sw}")
    n_samples = len(frames) // (2 * nchan)
    fmt = f"<{n_samples * nchan}h"
    samples = struct.unpack(fmt, frames[: n_samples * nchan * 2])
    peak = max(abs(x) for x in samples) if samples else 0
    return duration, peak


@pytest.fixture
async def asgi_client():
    # Defer importing the full FastAPI app until this optional real-XTTS test runs.
    from backend.api.main import app

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test", timeout=900.0) as c:
        yield c


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_xtts
@pytest.mark.timeout(900)
async def test_real_xtts_synthesize_returns_audible_wav(asgi_client: AsyncClient) -> None:
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    err = _coqui_import_error()
    if err:
        pytest.skip(err)

    hint = _xtts_model_hint_missing()
    if hint:
        pytest.skip(hint)

    profile_resp = await asgi_client.post(
        "/api/profiles",
        json={"name": "slice8-xtts-real", "description": "Slice 8 real XTTS ASGI proof"},
    )
    assert profile_resp.status_code in (200, 201), (
        f"Profile creation failed: {profile_resp.status_code} - {profile_resp.text[:500]}"
    )
    profile_data = profile_resp.json()
    profile_id = profile_data.get("id") or profile_data.get("profile_id")
    assert profile_id, f"No profile id: {profile_data}"

    synth_resp = await asgi_client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "xtts_v2",
            "text": "VoiceStudio slice eight real synthesis.",
            "language": "en",
        },
    )
    if synth_resp.status_code == 403:
        pytest.skip(
            "Synthesis 403 (consent or policy). Ensure POST /api/profiles default owner_user_id is local."
        )
    assert synth_resp.status_code in (200, 201), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:800]}"
    )
    body_text = synth_resp.text
    assert "ci_golden_loop_stub" not in body_text

    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id: {synth_data}"
    assert synth_data.get("duration", 0) >= 0.0

    audio_resp = await asgi_client.get(f"/api/audio/file/{audio_id}")
    assert audio_resp.status_code == 200, (
        f"Audio fetch failed: {audio_resp.status_code} - {audio_resp.text[:200]}"
    )
    raw = audio_resp.content
    assert len(raw) > 1024, f"WAV too small: {len(raw)} bytes"
    assert raw[:4] == b"RIFF", "Not a RIFF/WAV"

    duration, peak = _wav_duration_and_peak(raw)
    assert duration >= 0.5, f"Duration too short for real speech: {duration}s"
    assert peak > 200, (
        f"PCM looks like silence (peak={peak}); expected non-stub synthesis energy."
    )
