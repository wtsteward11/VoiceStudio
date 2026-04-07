"""
Focused prosody API tests (GAP-023).

Replaces legacy mock-heavy module that skipped at import time.
"""

from __future__ import annotations

import sys
import uuid
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(project_root))

pytest.importorskip("numpy")

from backend.api.routes import prosody as prosody_module
from backend.services.prosody_authority_service import ProsodyTransformResult


@pytest.fixture
def prosody_client():
    prosody_module._prosody_configs.clear()
    app = FastAPI()
    app.include_router(prosody_module.router)
    return TestClient(app)


def test_create_prosody_config_success(prosody_client):
    request_data = {
        "name": "Test Config",
        "pitch": 1.2,
        "rate": 1.0,
        "volume": 0.9,
    }
    response = prosody_client.post("/api/prosody/configs", json=request_data)
    assert response.status_code == 200
    data = response.json()
    assert data["name"] == "Test Config"
    assert data["pitch"] == 1.2


def test_apply_prosody_returns_handling_and_voice_audio_url(prosody_client, tmp_path):
    """Contract: success payload includes prosody_handling and /api/voice/audio/ URL."""
    prosody_module._prosody_configs.clear()
    cid = str(uuid.uuid4())
    prosody_module._prosody_configs[cid] = {
        "config_id": cid,
        "name": "c1",
        "pitch": 1.0,
        "rate": 1.0,
        "volume": 1.0,
        "intonation": None,
    }

    wav = tmp_path / "synth.wav"
    wav.parent.mkdir(parents=True, exist_ok=True)
    wav.write_bytes(b"fake")

    fake_audio = np.zeros(200, dtype=np.float32)

    async def _fake_synth(*_a, **_kw):
        r = MagicMock()
        r.audio_id = "synth_1"
        return r

    transform_out = ProsodyTransformResult(
        audio=fake_audio.copy(),
        diagnostics={
            "action": "none",
            "applied_operations": [],
            "skipped_operations": [{"operation": "all", "reason": "identity_request"}],
            "warnings": [],
            "errors": [],
            "pitch_factor": 1.0,
            "rate_factor": 1.0,
            "volume_factor": 1.0,
            "context": "prosody_apply",
        },
    )

    with patch(
        "backend.services.synthesis_service.SynthesisService.synthesize",
        new=AsyncMock(side_effect=_fake_synth),
    ):
        with patch(
            "backend.services.audio_artifacts.AudioRegistry.get_path",
            return_value=str(wav),
        ):
            with patch(
                "backend.audio.audio_utils.load_audio",
                return_value=(fake_audio, 16000),
            ):
                with patch(
                    "backend.services.prosody_authority_service.apply_transform",
                    return_value=transform_out,
                ):
                    with patch(
                        "backend.services.audio_artifacts.create_audio_artifact_from_wav_array",
                        return_value=("out_aid", str(tmp_path / "o.wav"), {}),
                    ):
                        response = prosody_client.post(
                            "/api/prosody/apply",
                            json={
                                "config_id": cid,
                                "text": "hello",
                                "voice_profile_id": "p1",
                                "engine": "xtts",
                                "language": "en",
                            },
                        )

    assert response.status_code == 200, response.text
    body = response.json()
    assert body["audio_id"] == "out_aid"
    assert body["original_audio_id"] == "synth_1"
    assert body["audio_url"].startswith("/api/voice/audio/")
    assert "prosody_handling" in body
    assert body["prosody_handling"]["action"] == "none"
