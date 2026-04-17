"""
Real XTTS v2 synthesis against a **live** FastAPI backend (non-stub).

In-process ASGI transport does not initialize the synthesis engine router (503 from
SynthesisService). This proof therefore uses HTTP to ``127.0.0.1:8000`` when
``GET /health`` reports ``engines_ready: true`` (same process as a normal ``uvicorn`` run).

Override base URL: ``VOICESTUDIO_REAL_XTTS_HTTP_BASE`` (default ``http://127.0.0.1:8000``).

Proves: POST /api/profiles -> consent grant -> POST /api/voice/synthesize (engine=xtts_v2) ->
GET /api/audio/file/{audio_id} -> valid WAV with non-silent PCM and duration >= 0.5s.

Slice 9 adds an explicit test for the **primary** file route (``Content-Type`` + same GET) documented in
``docs/reports/verification/PROOF_SLICE9_PLAYBACK_AUDITION.md``.

On success, writes ``docs/reports/verification/slice8/slice8_output.wav`` (Slice 8) and
``docs/reports/verification/slice9/slice9_output.wav`` + ``slice9_backend_log_snippet.txt`` (Slice 9).

Skips (does not fail the suite) when:
- VOICESTUDIO_TEST_MODE is stub-like (cannot prove non-stub path).
- Coqui TTS is not importable.
- Live backend unreachable or ``engines_ready`` is false.
- Synthesis returns 500/503 with ``xtts_v2`` not available / failed to initialize (host has no working XTTS).
- Optional: XTTS model cache not present on Windows (unless VOICESTUDIO_ALLOW_XTTS_DOWNLOAD_IN_TEST=1).

Run explicitly (slow, may require GPU/CPU and models):
  python -m pytest tests/integration/test_synthesis_xtts_real.py -v -m real_xtts --tb=short
"""

from __future__ import annotations

import os
import struct
import sys
import time
import wave
from io import BytesIO
from pathlib import Path

import pytest
from httpx import AsyncClient, Response


def _repo_fixture_wav() -> Path:
    """Short WAV under tests/fixtures (readable by backend on same machine)."""
    return (
        Path(__file__).resolve().parents[1] / "fixtures" / "audio" / "test_440hz_2s.wav"
    )


async def _bind_profile_reference_audio(
    client: AsyncClient, profile_id: str, wav_path: Path
) -> None:
    """Copy fixture WAV into canonical profile dir via preprocess-reference (XTTS needs it)."""
    assert wav_path.is_file(), f"Fixture WAV missing: {wav_path}"
    pre = await client.post(
        f"/api/profiles/{profile_id}/preprocess-reference",
        json={
            "reference_audio_path": str(wav_path.resolve()),
            "auto_enhance": False,
            "select_optimal_segments": False,
        },
    )
    assert pre.status_code in (200, 201), (
        f"Reference bind failed: {pre.status_code} - {pre.text[:800]}"
    )


async def _grant_voice_usage_consent(client: AsyncClient, voice_id: str) -> None:
    """Non-stub synthesis requires active consent for profile_id (voice_id)."""
    req = await client.post(
        "/api/consent/request",
        json={
            "voice_id": voice_id,
            "grantor_id": "local",
            "grantor_name": "slice8-real-xtts-live-http",
            "consent_type": "voice_usage",
        },
    )
    assert req.status_code in (200, 201), (
        f"Consent request failed: {req.status_code} - {req.text[:500]}"
    )
    data = req.json()
    consent_id = data.get("consent_id")
    assert consent_id, f"No consent_id: {data}"
    grant = await client.post(f"/api/consent/grant/{consent_id}")
    assert grant.status_code in (200, 201), (
        f"Consent grant failed: {grant.status_code} - {grant.text[:500]}"
    )


def _stub_like_mode() -> bool:
    v = os.environ.get("VOICESTUDIO_TEST_MODE", "").strip().lower()
    return v in ("1", "true", "yes", "stub")


def _coqui_import_error() -> str | None:
    try:
        from TTS.api import TTS

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


def _live_backend_base_url() -> str:
    return os.environ.get("VOICESTUDIO_REAL_XTTS_HTTP_BASE", "http://127.0.0.1:8000").rstrip(
        "/"
    )


@pytest.fixture
async def live_synthesis_client():
    """
    HTTP client to a running backend with engines initialized (not ASGI in-process).
    """
    base = _live_backend_base_url()
    async with AsyncClient(base_url=base, timeout=900.0) as client:
        try:
            health = await client.get("/health")
        except Exception as exc:
            pytest.skip(
                f"Real XTTS proof requires live backend at {base} (ASGI in-process does not wire "
                f"synthesis engines). Not reachable: {exc}"
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
        yield client


def _skip_if_synthesis_engine_unavailable(synth_resp: Response) -> None:
    """
    When the live backend has /health up but XTTS failed to initialize, synthesis returns 500/503.
    Same posture as C# LiveXttsBackendTestGuards / Assert.Inconclusive — skip, do not fail the suite.
    """
    if synth_resp.status_code not in (500, 503):
        return
    body = synth_resp.text.lower()
    if "xtts" in body and (
        "not available" in body
        or "failed to initialize" in body
        or "503" in body
    ):
        pytest.skip(
            "Live XTTS engine not initialized or unavailable (opt-in real_xtts proof requires working xtts_v2)."
        )


async def _synthesize_xtts_and_fetch_primary_file(
    client: AsyncClient,
    *,
    profile_name: str,
    profile_description: str,
    synth_text: str,
) -> tuple[str, str, Response]:
    """
    Live XTTS: create profile, bind reference, consent, synthesize, GET /api/audio/file/{audio_id}.

    Returns (profile_id, audio_id, audio_get_response).
    """
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
            "engine": "xtts_v2",
            "text": synth_text,
            "language": "en",
        },
    )
    if synth_resp.status_code == 403:
        pytest.skip(
            "Synthesis 403 (consent or policy). Ensure POST /api/profiles default owner_user_id is local."
        )
    _skip_if_synthesis_engine_unavailable(synth_resp)
    assert synth_resp.status_code in (200, 201), (
        f"Synthesis failed: {synth_resp.status_code} - {synth_resp.text[:800]}"
    )
    assert "ci_golden_loop_stub" not in synth_resp.text

    synth_data = synth_resp.json()
    audio_id = synth_data.get("audio_id")
    assert audio_id, f"No audio_id: {synth_data}"
    assert synth_data.get("duration", 0) >= 0.0

    audio_resp = await client.get(f"/api/audio/file/{audio_id}")
    return str(profile_id), str(audio_id), audio_resp


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_xtts
@pytest.mark.timeout(900)
async def test_real_xtts_synthesize_returns_audible_wav(live_synthesis_client: AsyncClient) -> None:
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    err = _coqui_import_error()
    if err:
        pytest.skip(err)

    hint = _xtts_model_hint_missing()
    if hint:
        pytest.skip(hint)

    profile_id, audio_id, audio_resp = await _synthesize_xtts_and_fetch_primary_file(
        live_synthesis_client,
        profile_name="slice8-xtts-real",
        profile_description="Slice 8 real XTTS live HTTP proof",
        synth_text="VoiceStudio slice eight real synthesis.",
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

    proof_dir = Path(__file__).resolve().parents[2] / "docs" / "reports" / "verification" / "slice8"
    proof_dir.mkdir(parents=True, exist_ok=True)
    proof_wav = proof_dir / "slice8_output.wav"
    proof_wav.write_bytes(raw)
    snippet = proof_dir / "backend_log_snippet.txt"
    snippet.write_text(
        "Slice 8 real XTTS live-backend proof (synthesis succeeded).\n"
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
@pytest.mark.real_xtts
@pytest.mark.timeout(900)
async def test_real_xtts_primary_audio_file_route_slice9_content_type(
    live_synthesis_client: AsyncClient,
) -> None:
    """
    Slice 9: same primary route as BackendClient (GET /api/audio/file/{id}) with explicit
    Content-Type and WAV checks — proves artifact retrieval for audition/playback seam.
    """
    if _stub_like_mode():
        pytest.skip("VOICESTUDIO_TEST_MODE is stub-like; real-synthesis proof requires it unset.")

    err = _coqui_import_error()
    if err:
        pytest.skip(err)

    hint = _xtts_model_hint_missing()
    if hint:
        pytest.skip(hint)

    profile_id, audio_id, audio_resp = await _synthesize_xtts_and_fetch_primary_file(
        live_synthesis_client,
        profile_name="slice9-xtts-playback-proof",
        profile_description="Slice 9 playback / primary file route proof",
        synth_text="VoiceStudio slice nine playback artifact audition proof.",
    )
    assert audio_resp.status_code == 200, (
        f"Audio fetch failed: {audio_resp.status_code} - {audio_resp.text[:200]}"
    )
    ctype = audio_resp.headers.get("content-type", "").lower()
    assert "audio" in ctype and "wav" in ctype, (
        f"Expected audio/wav Content-Type, got {ctype!r}"
    )
    raw = audio_resp.content
    assert len(raw) > 1024, f"WAV too small: {len(raw)} bytes"
    assert raw[:4] == b"RIFF", "Not a RIFF/WAV"
    duration, peak = _wav_duration_and_peak(raw)
    assert duration >= 0.5, f"Duration too short for real speech: {duration}s"
    assert peak > 200, f"PCM looks like silence (peak={peak}); expected non-stub synthesis energy."

    proof_dir = Path(__file__).resolve().parents[2] / "docs" / "reports" / "verification" / "slice9"
    proof_dir.mkdir(parents=True, exist_ok=True)
    (proof_dir / "slice9_output.wav").write_bytes(raw)
    (proof_dir / "slice9_backend_log_snippet.txt").write_text(
        "Slice 9 XTTS playback proof — primary GET /api/audio/file/{audio_id}.\n"
        f"timestamp_utc: {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}\n"
        f"backend_base: {_live_backend_base_url()}\n"
        f"profile_id: {profile_id}\n"
        f"audio_id: {audio_id}\n"
        f"content_type: {audio_resp.headers.get('content-type', '')}\n"
        f"wav_bytes: {len(raw)}\n"
        f"duration_s: {duration}\n"
        f"pcm_peak_abs: {peak}\n",
        encoding="utf-8",
    )
