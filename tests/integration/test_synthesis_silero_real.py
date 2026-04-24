"""
Real Silero TTS synthesis against a **live** FastAPI backend (non-stub).

Same HTTP shape as ``test_synthesis_espeak_ng_real.py`` but with ``engine=silero`` and
``GET /api/health/preflight`` gate on ``checks.silero.ok``.

Override base URL: ``VOICESTUDIO_REAL_XTTS_HTTP_BASE`` (shared with XTTS live proofs).

Run explicitly:
  python -m pytest tests/integration/test_synthesis_silero_real.py -v -m real_silero --tb=short
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from httpx import AsyncClient, Response

from tests.integration.test_synthesis_xtts_real import (
    _bind_profile_reference_audio,
    _grant_voice_usage_consent,
    _live_backend_base_url,
    _repo_fixture_wav,
    _stub_like_mode,
    _wav_duration_and_peak,
)


async def _preflight_silero_ok(client: AsyncClient) -> bool:
    try:
        r = await client.get("/api/health/preflight")
    except Exception:
        return False
    if r.status_code != 200:
        return False
    data = r.json()
    checks = data.get("checks") or {}
    si = checks.get("silero")
    if not isinstance(si, dict):
        return False
    return si.get("ok") is True


def _skip_if_silero_engine_unavailable(synth_resp: Response) -> None:
    if synth_resp.status_code not in (500, 503):
        return
    body = synth_resp.text.lower()
    if "silero" in body and (
        "not available" in body
        or "failed" in body
        or "503" in body
        or "torch" in body
    ):
        pytest.skip(
            "Live Silero engine not initialized or unavailable (opt-in real_silero proof)."
        )


async def _synthesize_silero_and_fetch_primary_file(
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

    await _bind_profile_reference_audio(client, str(profile_id), _repo_fixture_wav())
    await _grant_voice_usage_consent(client, str(profile_id))

    synth_resp = await client.post(
        "/api/voice/synthesize",
        json={
            "profile_id": profile_id,
            "engine": "silero",
            "text": synth_text,
            "language": "en",
        },
    )
    if synth_resp.status_code == 403:
        pytest.skip(
            "Synthesis 403 (consent or policy). Ensure POST /api/profiles default owner_user_id is local."
        )
    _skip_if_silero_engine_unavailable(synth_resp)
    assert synth_resp.status_code in (200, 201), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:800]}"
    )
    assert "ci_golden_loop_stub" not in synth_resp.text

    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id: {synth_data}"
    assert synth_data.get("routed_engine") == "silero", (
        f"Expected routed_engine=silero, got {synth_data.get('routed_engine')!r}"
    )
    assert synth_data.get("duration", 0) >= 0.0

    audio_resp = await client.get(f"/api/audio/file/{audio_id}")
    return str(profile_id), str(audio_id), audio_resp


@pytest.fixture
async def live_silero_client():
    """HTTP client; gates on preflight silero.ok == True."""
    base = _live_backend_base_url()
    async with AsyncClient(base_url=base, timeout=900.0) as client:
        try:
            health = await client.get("/health")
        except Exception as exc:
            pytest.skip(
                f"Real Silero proof requires live backend at {base}. Not reachable: {exc}"
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
        if not await _preflight_silero_ok(client):
            pytest.skip(
                f"Preflight checks.silero.ok is not true at {base}/api/health/preflight; "
                "warm torch.hub cache for snakers4/silero-models (see ensure_silero)."
            )
        yield client


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_silero
@pytest.mark.timeout(900)
async def test_real_silero_synthesize_returns_audible_wav(
    live_silero_client: AsyncClient,
) -> None:
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    profile_id, audio_id, audio_resp = await _synthesize_silero_and_fetch_primary_file(
        live_silero_client,
        profile_name="slice15-silero-real",
        profile_description="Slice 15 real Silero live HTTP proof",
        synth_text="VoiceStudio slice fifteen silero real synthesis proof.",
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
        / "slice15"
        / "silero"
    )
    proof_dir.mkdir(parents=True, exist_ok=True)
    proof_wav = proof_dir / "silero_output.wav"
    proof_wav.write_bytes(raw)
    snippet = proof_dir / "silero_backend_log_snippet.txt"
    snippet.write_text(
        "Slice 15 real Silero live-backend proof (synthesis succeeded).\n"
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
@pytest.mark.real_silero
@pytest.mark.timeout(900)
async def test_real_silero_primary_audio_file_route_content_type(
    live_silero_client: AsyncClient,
) -> None:
    """Primary GET /api/audio/file/{id} + Content-Type (same seam as Slice 9)."""
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    _profile_id, audio_id, audio_resp = await _synthesize_silero_and_fetch_primary_file(
        live_silero_client,
        profile_name="slice15-silero-file-route",
        profile_description="Slice 15 Silero file route proof",
        synth_text="VoiceStudio slice fifteen silero file route proof.",
    )
    assert audio_resp.status_code == 200
    ct = audio_resp.headers.get("content-type", "")
    assert "audio" in ct.lower() or "octet-stream" in ct.lower(), f"Unexpected Content-Type: {ct!r}"
    assert len(audio_resp.content) > 512
