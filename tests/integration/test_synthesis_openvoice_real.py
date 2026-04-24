"""
Real OpenVoice synthesis against a **live** FastAPI backend (non-stub).

Same HTTP shape as ``test_synthesis_chatterbox_real.py`` but with ``engine=openvoice`` and
``GET /api/health/preflight`` gate on ``checks.openvoice.ok``.

Override base URL: ``VOICESTUDIO_REAL_XTTS_HTTP_BASE`` (shared with other real_* proofs).

Run explicitly:
  python -m pytest tests/integration/test_synthesis_openvoice_real.py -v -m real_openvoice --tb=short
"""

from __future__ import annotations

import os
import time
from pathlib import Path

import pytest
from httpx import AsyncClient, Response

from tests.integration.test_synthesis_xtts_real import (
    _bind_profile_reference_audio,
    _grant_voice_usage_consent,
    _live_backend_base_url,
    _stub_like_mode,
    _wav_duration_and_peak,
)


def _openvoice_reference_wav() -> Path:
    """Speech reference for OpenVoice (VAD); not the 440 Hz tone (see bounded slice 19L)."""
    override = os.environ.get("VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV", "").strip().strip('"')
    if override:
        p = Path(override)
        if p.is_file():
            return p
    return (
        Path(__file__).resolve().parents[1]
        / "fixtures"
        / "audio"
        / "openvoice_reference_speech.wav"
    )


async def _preflight_openvoice_ok(client: AsyncClient) -> bool:
    try:
        r = await client.get("/api/health/preflight")
    except Exception:
        return False
    if r.status_code != 200:
        return False
    data = r.json()
    checks = data.get("checks") or {}
    ov = checks.get("openvoice")
    if not isinstance(ov, dict):
        return False
    return ov.get("ok") is True


def _skip_if_openvoice_engine_unavailable(synth_resp: Response) -> None:
    if synth_resp.status_code not in (500, 503):
        return
    body = synth_resp.text.lower()
    if "openvoice" in body and (
        "not available" in body
        or "failed" in body
        or "503" in body
        or "torch" in body
    ):
        pytest.skip(
            "Live OpenVoice engine not initialized or unavailable (opt-in real_openvoice proof)."
        )


async def _synthesize_openvoice_and_fetch_primary_file(
    client: AsyncClient,
    *,
    profile_name: str,
    profile_description: str,
    synth_text: str,
) -> tuple[str, str, Response]:
    profile_resp = await client.post(
        "/api/profiles",
        json={"name": profile_name, "description": profile_description},
    )
    assert profile_resp.status_code in (200, 201), (
        f"Profile creation failed: {profile_resp.status_code} - {profile_resp.text[:500]}"
    )
    profile_data = profile_resp.json()
    profile_id = profile_data.get("id") or profile_data.get("profile_id")
    assert profile_id, f"No profile id: {profile_data}"

    await _bind_profile_reference_audio(client, str(profile_id), _openvoice_reference_wav())
    await _grant_voice_usage_consent(client, str(profile_id))

    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "openvoice",
            "text": synth_text,
            "language": "en",
        },
    )
    if synth_resp.status_code == 403:
        pytest.skip(
            "Synthesis 403 (consent or policy). Ensure POST /api/profiles default owner_user_id is local."
        )
    _skip_if_openvoice_engine_unavailable(synth_resp)
    assert synth_resp.status_code in (200, 201), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:800]}"
    )
    assert "ci_golden_loop_stub" not in synth_resp.text

    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id: {synth_data}"
    assert synth_data.get("routed_engine") == "openvoice", (
        f"Expected routed_engine=openvoice, got {synth_data.get('routed_engine')!r}"
    )
    assert synth_data.get("duration", 0) >= 0.0

    audio_resp = await client.get(f"/api/audio/file/{audio_id}")
    return str(profile_id), str(audio_id), audio_resp


@pytest.fixture
async def live_openvoice_client():
    """HTTP client; gates on preflight checks.openvoice.ok == True."""
    base = _live_backend_base_url()
    async with AsyncClient(base_url=base, timeout=900.0) as client:
        try:
            health = await client.get("/health")
        except Exception as exc:
            pytest.skip(
                f"Real OpenVoice proof requires live backend at {base}. Not reachable: {exc}"
            )
        if health.status_code != 200:
            pytest.skip(
                f"Live backend {base}/health returned HTTP {health.status_code}; start uvicorn first."
            )
        payload = health.json()
        if not payload.get("engines_ready"):
            pytest.skip(
                f"Live backend {base} reports engines_ready=false; wait for startup or fix engine init."
            )
        if not await _preflight_openvoice_ok(client):
            pytest.skip(
                f"Preflight checks.openvoice.ok is not true at {base}/api/health/preflight; "
                "see ensure_openvoice (venv_openvoice / runtime/venvs/openvoice + checkpoint trees)."
            )
        yield client


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_openvoice
@pytest.mark.timeout(900)
async def test_real_openvoice_synthesize_returns_audible_wav(
    live_openvoice_client: AsyncClient,
) -> None:
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    profile_id, audio_id, audio_resp = await _synthesize_openvoice_and_fetch_primary_file(
        live_openvoice_client,
        profile_name="slice19-openvoice-real",
        profile_description="Slice 19A real OpenVoice live HTTP proof",
        synth_text="VoiceStudio slice nineteen openvoice real synthesis proof.",
    )
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

    proof_dir = (
        Path(__file__).resolve().parents[2]
        / "docs"
        / "reports"
        / "verification"
        / "slice19"
        / "openvoice"
    )
    proof_dir.mkdir(parents=True, exist_ok=True)
    proof_wav = proof_dir / "openvoice_output.wav"
    proof_wav.write_bytes(raw)
    snippet = proof_dir / "openvoice_backend_log_snippet.txt"
    snippet.write_text(
        "Slice 19A real OpenVoice live-backend proof (synthesis succeeded).\n"
        f"timestamp_utc: {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}\n"
        f"backend_base: {_live_backend_base_url()}\n"
        f"profile_id: {profile_id}\n"
        f"audio_id: {audio_id}\n"
        f"wav_bytes: {len(raw)}\n"
        f"duration_s: {duration}\n"
        f"pcm_peak_abs: {peak}\n",
        encoding="utf-8",
    )


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_openvoice
@pytest.mark.timeout(900)
async def test_real_openvoice_primary_audio_file_route_content_type(
    live_openvoice_client: AsyncClient,
) -> None:
    """Primary GET /api/audio/file/{id} + Content-Type (same seam as other real_* slices)."""
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    _profile_id, _audio_id, audio_resp = await _synthesize_openvoice_and_fetch_primary_file(
        live_openvoice_client,
        profile_name="slice19-openvoice-file-route",
        profile_description="Slice 19A OpenVoice file route proof",
        synth_text="VoiceStudio slice nineteen openvoice file route proof.",
    )
    assert audio_resp.status_code == 200
    ct = audio_resp.headers.get("content-type", "")
    assert "audio" in ct.lower() or "octet-stream" in ct.lower(), f"Unexpected Content-Type: {ct!r}"
    assert len(audio_resp.content) > 512
