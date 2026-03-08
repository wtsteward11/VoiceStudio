"""
CI regression guard: uploaded audio playback endpoint contract.

Ensures BackendClient.GetAudioStreamAsync uses the canonical endpoint
/api/audio/file/{id} first, with fallback to /api/voice/audio/{id} for
synthesized audio. Fails if UI playback points only at /api/voice/audio/
for uploads (which returns 404 for uploaded audio).
"""
from __future__ import annotations

from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
BACKEND_CLIENT = PROJECT_ROOT / "src" / "VoiceStudio.App" / "Services" / "BackendClient.cs"

CANONICAL_ENDPOINT = "/api/audio/file/"
SYNTHESIZED_FALLBACK = "/api/voice/audio/"


def _extract_get_audio_stream_method(content: str) -> str | None:
    """Extract the GetAudioStreamAsync method body."""
    start = content.find("GetAudioStreamAsync")
    if start == -1:
        return None
    # Find method start (opening brace after async Task)
    brace_start = content.find("{", start)
    if brace_start == -1:
        return None
    depth = 1
    i = brace_start + 1
    while i < len(content) and depth > 0:
        if content[i] == "{":
            depth += 1
        elif content[i] == "}":
            depth -= 1
        i += 1
    return content[brace_start:i]


def test_backend_client_uses_canonical_audio_endpoint_first():
    """GetAudioStreamAsync must try /api/audio/file/ before /api/voice/audio/.

    Uploaded audio is served by /api/audio/file/{id}. Synthesized audio
    is in AudioRegistry and served by /api/voice/audio/{id}. The client
    must try the canonical endpoint first to support upload playback.
    """
    assert BACKEND_CLIENT.exists(), f"BackendClient not found: {BACKEND_CLIENT}"
    content = BACKEND_CLIENT.read_text(encoding="utf-8")

    method = _extract_get_audio_stream_method(content)
    assert method is not None, "GetAudioStreamAsync not found in BackendClient.cs"

    assert CANONICAL_ENDPOINT in method, (
        f"GetAudioStreamAsync must use {CANONICAL_ENDPOINT} for uploaded audio. "
        "Uploaded audio is not in AudioRegistry and returns 404 from /api/voice/audio/."
    )

    idx_canonical = method.find(CANONICAL_ENDPOINT)
    idx_fallback = method.find(SYNTHESIZED_FALLBACK)

    if idx_fallback != -1:
        assert idx_canonical < idx_fallback, (
            f"GetAudioStreamAsync must try {CANONICAL_ENDPOINT} before "
            f"{SYNTHESIZED_FALLBACK}. Current order would 404 on uploaded audio."
        )
