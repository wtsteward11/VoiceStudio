"""Tests for GET /api/audio/file/{audio_id} endpoint.

Proves that audio uploaded via POST /api/audio/upload can be retrieved
by ID via GET /api/audio/file/{audio_id}.
"""

import io
import struct
import sys
from pathlib import Path

import pytest

_project_root = str(Path(__file__).parent.parent.parent.parent.parent.parent)
if _project_root not in sys.path:
    sys.path.insert(0, _project_root)


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
    buf.write(struct.pack("<HHIIHH", 1, num_channels, sample_rate, byte_rate, block_align, bits_per_sample))
    buf.write(b"data")
    buf.write(struct.pack("<I", data_size))
    buf.write(b"\x00" * data_size)
    return buf.getvalue()


@pytest.fixture
def client():
    from fastapi.testclient import TestClient
    from backend.api.main import app

    return TestClient(app)


class TestAudioFileEndpoint:
    """Verify uploaded audio is retrievable via /api/audio/file/{id}."""

    def test_upload_then_retrieve(self, client):
        wav_bytes = _make_wav_bytes()
        upload_resp = client.post(
            "/api/audio/upload",
            files={"file": ("test.wav", io.BytesIO(wav_bytes), "audio/wav")},
        )
        assert upload_resp.status_code in (200, 201), f"Upload failed: {upload_resp.text}"
        audio_id = upload_resp.json()["id"]

        get_resp = client.get(f"/api/audio/file/{audio_id}")
        assert get_resp.status_code == 200, f"Retrieve failed: {get_resp.status_code} {get_resp.text}"
        assert len(get_resp.content) > 100
        assert get_resp.headers.get("content-type", "").startswith("audio/")

    def test_nonexistent_id_returns_404(self, client):
        resp = client.get("/api/audio/file/00000000-0000-0000-0000-000000000000")
        assert resp.status_code == 404
