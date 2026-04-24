"""
Task 125 — opt-in live TCP contract for ``GET /api/health/preflight`` ``checks.whisper_cpp``.

CI stays green when ``VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL`` is unset (``pytest.skip``).
"""

from __future__ import annotations

import os

import httpx
import pytest


@pytest.mark.integration
@pytest.mark.live_whisper_cpp_preflight
def test_live_preflight_whisper_cpp_shape() -> None:
    base = os.environ.get("VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL", "").strip().rstrip("/")
    if not base:
        pytest.skip("VOICESTUDIO_LIVE_PREFLIGHT_BASE_URL unset (Task 125 opt-in)")
    url = f"{base}/api/health/preflight"
    r = httpx.get(url, timeout=30.0)
    assert r.status_code == 200, f"GET {url} -> {r.status_code}: {(r.text or '')[:500]}"
    data = r.json()
    checks = data.get("checks")
    assert isinstance(checks, dict), "preflight checks must be dict"
    wcpp = checks.get("whisper_cpp")
    assert isinstance(wcpp, dict), (
        "checks.whisper_cpp missing or not dict — P0 regression (router mount / wrong app)"
    )
    assert "ok" in wcpp, "checks.whisper_cpp must include ok"
    assert isinstance(wcpp.get("ok"), bool), "checks.whisper_cpp.ok must be bool"
