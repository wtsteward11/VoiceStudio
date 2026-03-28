"""Runtime honesty: prosody route must not pretend DSP ran when it did not."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import pytest

project_root = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(project_root))

pytest.importorskip("numpy")
pytest.importorskip("soundfile")

from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.routes.voice import _shared as voice_shared

# Import processing so routes register on shared voice router
from backend.api.routes.voice import processing as _voice_processing


def test_prosody_returns_501_when_not_implemented(tmp_path):
    wav = tmp_path / "in.wav"
    wav.write_bytes(b"not-real-wav-but-probed")

    app = FastAPI()
    app.include_router(voice_shared.router)
    client = TestClient(app)

    with patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        return_value=str(wav),
    ):
        with patch("soundfile.read", return_value=(__import__("numpy").zeros(100), 16000)):
            response = client.post(
                "/api/voice/prosody-control",
                json={
                    "audio_id": "aid1",
                    "pitch_contour": [1.0, 1.1],
                },
            )

    assert response.status_code == 501
    detail = response.json().get("detail", "")
    dtext = detail.lower() if isinstance(detail, str) else str(detail).lower()
    assert "not yet implemented" in dtext
    assert "modified" in dtext
