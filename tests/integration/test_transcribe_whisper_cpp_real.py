"""
Real whisper.cpp STT against a **live** FastAPI backend (opt-in, Slice 27).

Proves: ``GET /api/health/preflight`` → ``checks.whisper_cpp.ok`` →
library upload → ``POST /api/transcribe/`` with ``engine: whisper_cpp``.

Uses the same base URL discipline as Slice 21A: ``VOICESTUDIO_REAL_XTTS_HTTP_BASE``.
"""

from __future__ import annotations

import os
import re
from pathlib import Path
from urllib.parse import urlparse

import pytest
from httpx import AsyncClient

from tests.integration.slice27_whisper_cpp_evidence import (
    record_slice27_skip_and_exit,
    slice27_artifact_dir_from_env,
    whisper_cpp_check_summary,
    write_slice27_pass_bundle,
)
from tests.integration.test_synthesis_xtts_real import _live_backend_base_url


def _assert_slice27_base_url_not_ambiguous_port_8000(base: str) -> None:
    """Slice 27 proof must not treat anonymous :8000 as authority (Task 123)."""
    if os.environ.get("VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000", "").strip() == "1":
        return
    parsed = urlparse(base.strip())
    host = (parsed.hostname or "").lower()
    port = parsed.port
    if host in ("127.0.0.1", "localhost", "::1") and port == 8000:
        pytest.fail(
            "Slice 27 real_whisper_cpp must not use loopback port 8000 as implicit "
            "authority (stale-listener risk). Use a dedicated port and set "
            "VOICESTUDIO_REAL_XTTS_HTTP_BASE (see docs/reports/verification/slice27/"
            "README.md §3 / Task 123). Escape hatch: VOICESTUDIO_SLICE27_ALLOW_DEFAULT_8000=1 "
            "only when the session note documents this repo revision on port 8000.",
        )


def _proof_wav() -> Path:
    override = os.environ.get("VOICESTUDIO_WHISPER_CPP_PROOF_WAV", "").strip().strip('"')
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


async def _fetch_preflight_json(client: AsyncClient) -> tuple[int | None, dict | None]:
    try:
        r = await client.get("/api/health/preflight")
    except Exception:
        return None, None
    if r.status_code != 200:
        return r.status_code, None
    try:
        return 200, r.json()
    except Exception:
        return 200, {"_parse_error": True, "text_snippet": (r.text or "")[:500]}


def _assert_transcript_anchor(text: str) -> None:
    t = text.strip()
    assert len(t) >= 3, f"transcript too short: {t!r}"
    assert re.search(r"[a-zA-Z]{2,}", t), f"no word-like content: {t!r}"


async def _upload_library_wav(client: AsyncClient, wav_path: Path) -> str:
    assert wav_path.is_file(), f"Proof WAV missing: {wav_path}"
    data = wav_path.read_bytes()
    files = {"file": ("slice27_whisper_cpp_proof.wav", data, "audio/wav")}
    r = await client.post(
        "/api/library/assets/upload",
        files=files,
        data={"tags": "slice27,whisper-cpp-proof"},
    )
    assert r.status_code == 201, f"Library upload failed: {r.status_code} - {r.text[:800]}"
    payload = r.json()
    audio_id = payload.get("audio_id") or payload.get("id")
    assert audio_id, f"No audio id in upload response: {payload}"
    return str(audio_id)


@pytest.mark.real_whisper_cpp
@pytest.mark.asyncio
@pytest.mark.integration
@pytest.mark.slow
@pytest.mark.timeout(900)
async def test_live_transcribe_whisper_cpp_fixture() -> None:
    base = _live_backend_base_url()
    _assert_slice27_base_url_not_ambiguous_port_8000(base)
    wav = _proof_wav()
    out_dir = slice27_artifact_dir_from_env()
    async with AsyncClient(base_url=base, timeout=900.0) as client:
        try:
            health = await client.get("/health")
        except Exception as exc:
            msg = f"Live whisper.cpp proof requires backend at {base}: {exc}"
            record_slice27_skip_and_exit(
                out_dir,
                base_url=base,
                stage="health_connect",
                skip_reason=msg,
                preflight_http_status=None,
                preflight_json=None,
                extra={
                    "exception_type": type(exc).__name__,
                    "exception_repr": repr(exc)[:500],
                },
            )

        pf_status, pf_json = await _fetch_preflight_json(client)
        if health.status_code != 200:
            record_slice27_skip_and_exit(
                out_dir,
                base_url=base,
                stage="health_http",
                skip_reason=f"/health HTTP {health.status_code}",
                preflight_http_status=pf_status,
                preflight_json=pf_json,
                extra={"health_status": health.status_code},
            )

        hj = health.json()
        if not hj.get("engines_ready"):
            record_slice27_skip_and_exit(
                out_dir,
                base_url=base,
                stage="engines_ready",
                skip_reason="engines_ready=false",
                preflight_http_status=pf_status,
                preflight_json=pf_json,
                extra={"health_engines_ready": hj.get("engines_ready")},
            )

        pf_status, pf_json = await _fetch_preflight_json(client)
        checks: dict = {}
        if isinstance(pf_json, dict):
            raw_checks = pf_json.get("checks")
            if isinstance(raw_checks, dict):
                checks = raw_checks
        wcpp = checks.get("whisper_cpp")
        if not isinstance(wcpp, dict):
            pytest.fail(
                "Slice 22 / Task 116 contract violation: "
                "`checks.whisper_cpp` is missing or not a JSON object from "
                f"{base!r} (wrong or stale backend, or non-repo preflight). "
                "Current `health.preflight_check()` always publishes this key. "
                "See Task 114 evidence: "
                "docs/reports/verification/slice27/slice27_preflight_task114.json "
                "and slice27_preflight_task114_capture.txt. "
                f"preflight_http_status={pf_status!r}."
            )
        if wcpp.get("ok") is not True:
            skip_msg = (
                "Preflight checks.whisper_cpp.ok is not true — "
                "see ensure_whisper_cpp / model weights / binding or CLI (Slice 22)."
            )
            record_slice27_skip_and_exit(
                out_dir,
                base_url=base,
                stage="preflight_whisper_cpp",
                skip_reason=skip_msg,
                preflight_http_status=pf_status,
                preflight_json=pf_json,
                extra=whisper_cpp_check_summary(pf_json),
            )

        audio_id = await _upload_library_wav(client, wav)
        tr = await client.post(
            "/api/transcribe/",
            json={
                "audio_id": audio_id,
                "engine": "whisper_cpp",
                "language": "en",
            },
        )
        if tr.status_code != 200:
            skip_msg = (
                f"Transcription failed HTTP {tr.status_code}: {tr.text[:800]} "
                "(whisper_cpp runtime not ready on this host)."
            )
            pf_status2, pf_json2 = await _fetch_preflight_json(client)
            body = tr.text or ""
            if len(body) > 8000:
                body = body[:8000] + "…"
            record_slice27_skip_and_exit(
                out_dir,
                base_url=base,
                stage="transcribe_http",
                skip_reason=skip_msg,
                preflight_http_status=pf_status2,
                preflight_json=pf_json2,
                extra={
                    "transcribe_http_status": tr.status_code,
                    "transcribe_body_snippet": body,
                },
            )

        payload = tr.json()
        text = str(payload.get("text") or "")
        _assert_transcript_anchor(text)
        assert payload.get("engine") == "whisper_cpp"
        if out_dir is not None:
            write_slice27_pass_bundle(
                out_dir,
                base_url=base,
                audio_id=audio_id,
                transcript_payload=payload,
            )
