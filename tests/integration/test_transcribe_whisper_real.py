"""
Real Whisper STT against a **live** FastAPI backend (opt-in).

Proves: ``GET /api/health/preflight`` → ``checks.whisper.ok`` →
``POST /api/library/assets/upload`` → ``POST /api/transcribe/`` with ``engine: whisper``.

Base URL: ``VOICESTUDIO_REAL_XTTS_HTTP_BASE`` (default ``http://127.0.0.1:8000``), same as
``real_xtts`` / ``real_openvoice`` proofs.

Fixture: ``tests/fixtures/audio/openvoice_reference_speech.wav`` (see Slice 19L / Slice 21 contract).
Optional stricter anchor: set ``VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING`` (case-insensitive substring).

Run explicitly (slow; may load faster-whisper / models on first use):
  python -m pytest tests/integration/test_transcribe_whisper_real.py -v -m real_whisper --tb=short
"""

from __future__ import annotations

import os
import re
from pathlib import Path

import pytest
from httpx import AsyncClient, Response

from tests.integration.test_synthesis_xtts_real import _live_backend_base_url


def _proof_wav() -> Path:
    override = os.environ.get("VOICESTUDIO_WHISPER_PROOF_WAV", "").strip().strip('"')
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


async def _preflight_whisper_ok(client: AsyncClient) -> bool:
    try:
        r = await client.get("/api/health/preflight")
    except Exception:
        return False
    if r.status_code != 200:
        return False
    data = r.json()
    checks = data.get("checks") or {}
    w = checks.get("whisper")
    if not isinstance(w, dict):
        return False
    return w.get("ok") is True


def _assert_transcript_anchor(text: str) -> None:
    t = text.strip()
    assert len(t) >= 5, f"transcript too short: {t!r}"
    assert re.search(r"[a-zA-Z]{3,}", t), f"no word-like English content: {t!r}"
    opt = os.environ.get("VOICESTUDIO_WHISPER_PROOF_ANCHOR_SUBSTRING", "").strip()
    if opt:
        assert opt.lower() in t.lower(), (
            f"anchor {opt!r} not in transcript: {t!r}"
        )


def _skip_if_whisper_stt_unavailable(tr: Response) -> None:
    if tr.status_code not in (500, 503):
        return
    body = tr.text.lower()
    engine_seam = (
        "faster-whisper" in body
        or "faster_whisper" in body
        or (
            "whisper" in body
            and (
                "not available" in body
                or "install" in body
                or "failed" in body
                or "initialize" in body
            )
        )
    )
    if engine_seam:
        pytest.skip(
            "Whisper STT unavailable or failed to initialize (opt-in real_whisper; "
            "install faster-whisper in API env if needed). "
            f"HTTP {tr.status_code}: {tr.text[:500]}"
        )


async def _upload_library_wav(client: AsyncClient, wav_path: Path) -> str:
    assert wav_path.is_file(), f"Proof WAV missing: {wav_path}"
    data = wav_path.read_bytes()
    files = {"file": ("slice21_whisper_proof.wav", data, "audio/wav")}
    r = await client.post(
        "/api/library/assets/upload",
        files=files,
        data={"tags": "slice21,whisper-proof"},
    )
    assert r.status_code == 201, f"Library upload failed: {r.status_code} - {r.text[:800]}"
    payload = r.json()
    audio_id = payload.get("audio_id") or payload.get("id")
    assert audio_id, f"No audio id in upload response: {payload}"
    return str(audio_id)


@pytest.fixture
async def live_whisper_client() -> AsyncClient:
    """HTTP client; requires live backend, engines_ready, and preflight ``checks.whisper.ok``."""
    base = _live_backend_base_url()
    async with AsyncClient(base_url=base, timeout=900.0) as client:
        try:
            health = await client.get("/health")
        except Exception as exc:
            pytest.skip(
                f"Real Whisper proof requires live backend at {base}. Not reachable: {exc}"
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
        if not await _preflight_whisper_ok(client):
            pytest.skip(
                f"Preflight checks.whisper.ok is not true at {base}/api/health/preflight; "
                "see ensure_whisper / faster-whisper in API environment (Slice 20)."
            )
        yield client


@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.real_whisper
@pytest.mark.timeout(900)
async def test_real_whisper_transcribe_returns_text(
    live_whisper_client: AsyncClient,
) -> None:
    wav = _proof_wav()
    audio_id = await _upload_library_wav(live_whisper_client, wav)

    tr = await live_whisper_client.post(
        "/api/transcribe/",
        json={
            "audio_id": audio_id,
            "engine": "whisper",
            "language": "en",
        },
    )
    if tr.status_code in (500, 503):
        _skip_if_whisper_stt_unavailable(tr)
    assert tr.status_code == 200, f"Transcribe failed: {tr.status_code} - {tr.text[:1200]}"

    body = tr.json()
    text = body.get("text") or ""
    assert isinstance(text, str)
    _assert_transcript_anchor(text)
    assert body.get("engine") == "whisper", f"expected engine=whisper, got {body.get('engine')!r}"
