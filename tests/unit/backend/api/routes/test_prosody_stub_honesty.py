"""Runtime honesty: prosody-control must not fake DSP; apply path must not silent-skip."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest

project_root = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(project_root))

pytest.importorskip("numpy")

from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.routes.voice import _shared as voice_shared
from backend.api.routes.voice import processing as voice_processing

assert callable(voice_processing.prosody_control)


def test_prosody_control_returns_200_when_dsp_succeeds(tmp_path):
    wav = tmp_path / "in.wav"
    wav.write_bytes(b"x")

    app = FastAPI()
    app.include_router(voice_shared.router)
    client = TestClient(app)

    fake_audio = np.zeros(120, dtype=np.float32)

    def _fake_apply_transform(audio, sample_rate, **kwargs):
        from backend.services.prosody_authority_service import ProsodyTransformResult

        return ProsodyTransformResult(
            audio=fake_audio.copy(),
            diagnostics={
                "action": "applied",
                "applied_operations": ["pitch_shift"],
                "skipped_operations": [],
                "warnings": [],
                "errors": [],
                "pitch_factor": 1.05,
                "rate_factor": 1.0,
                "volume_factor": 1.0,
                "context": "prosody_control",
            },
        )

    with patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        return_value=str(wav),
    ):
        with patch(
            "backend.audio.audio_utils.load_audio",
            return_value=(fake_audio, 16000),
        ):
            with patch(
                "backend.services.prosody_authority_service.apply_transform",
                side_effect=_fake_apply_transform,
            ):
                with patch(
                    "backend.api.routes.voice.processing.create_audio_artifact_from_wav_array",
                    return_value=("proc_audio_1", "/tmp/x.wav", {}),
                ):
                    response = client.post(
                        "/api/voice/prosody-control",
                        json={
                            "audio_id": "aid1",
                            "pitch_contour": [1.0, 1.1],
                        },
                    )

    assert response.status_code == 200, response.text
    body = response.json()
    assert body["processed_audio_id"] == "proc_audio_1"
    assert "/api/voice/audio/proc_audio_1" in body["processed_audio_url"]


def test_prosody_control_422_when_only_metadata(tmp_path):
    wav = tmp_path / "in.wav"
    wav.write_bytes(b"x")
    fake_audio = np.zeros(80, dtype=np.float32)

    app = FastAPI()
    app.include_router(voice_shared.router)
    client = TestClient(app)

    with patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        return_value=str(wav),
    ):
        with patch(
            "backend.audio.audio_utils.load_audio",
            return_value=(fake_audio, 16000),
        ):
            response = client.post(
                "/api/voice/prosody-control",
                json={
                    "audio_id": "aid1",
                    "intonation_pattern": "rising",
                },
            )

    assert response.status_code == 422
    assert "pitch_contour" in response.json()["detail"].lower()


def test_prosody_control_422_when_no_transform_params(tmp_path):
    wav = tmp_path / "in.wav"
    wav.write_bytes(b"x")
    fake_audio = np.zeros(80, dtype=np.float32)

    app = FastAPI()
    app.include_router(voice_shared.router)
    client = TestClient(app)

    with patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        return_value=str(wav),
    ):
        with patch(
            "backend.audio.audio_utils.load_audio",
            return_value=(fake_audio, 16000),
        ):
            response = client.post(
                "/api/voice/prosody-control",
                json={"audio_id": "aid1"},
            )

    assert response.status_code == 422
