"""
Focused tests for emotion routes (GAP-050).

Anti-relapse: apply-extended delegates DSP to prosody authority only.
"""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest
import soundfile as sf
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.api.routes import emotion  # noqa: E402
from backend.services.prosody_authority_service import ProsodyTransformResult  # noqa: E402


@pytest.fixture
def emotion_client() -> TestClient:
    app = FastAPI()
    app.include_router(emotion.router)
    return TestClient(app)


def test_list_emotions_includes_warm_energetic(emotion_client: TestClient) -> None:
    response = emotion_client.get("/api/emotion/list")
    assert response.status_code == 200
    data = response.json()
    assert "warm" in data
    assert "energetic" in data


def test_apply_extended_404_unknown_audio(emotion_client: TestClient) -> None:
    with patch(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        return_value=None,
    ):
        response = emotion_client.post(
            "/api/emotion/apply-extended",
            json={
                "audio_id": "missing",
                "primary_emotion": "neutral",
                "primary_intensity": 100.0,
                "secondary_emotion": None,
                "secondary_intensity": 0.0,
            },
        )
    assert response.status_code == 404


def test_apply_extended_delegates_to_apply_transform(
    emotion_client: TestClient, tmp_path: Path
) -> None:
    wav_path = tmp_path / "in.wav"
    audio = np.zeros(4000, dtype=np.float32)
    sf.write(wav_path, audio, 8000)

    calls: dict[str, object] = {}

    def fake_apply_transform(
        audio_in: np.ndarray,
        sample_rate: int,
        *,
        pitch: float = 1.0,
        rate: float = 1.0,
        volume: float = 1.0,
        context: str = "prosody",
    ) -> ProsodyTransformResult:
        calls["pitch"] = pitch
        calls["rate"] = rate
        calls["volume"] = volume
        calls["context"] = context
        return ProsodyTransformResult(
            audio=np.asarray(audio_in, dtype=np.float64).copy(),
            diagnostics={
                "action": "none",
                "applied_operations": [],
                "skipped_operations": [
                    {"operation": "all", "reason": "identity_request"},
                ],
                "warnings": [],
                "errors": [],
                "pitch_factor": pitch,
                "rate_factor": rate,
                "volume_factor": volume,
                "context": context,
            },
        )

    def fake_artifact(arr: np.ndarray, sr: int, **kwargs: object) -> tuple[str, None, None]:
        _ = (arr, sr, kwargs)
        return ("emotion_out_test", None, None)

    with (
        patch(
            "backend.services.audio_artifacts.AudioRegistry.get_path",
            return_value=str(wav_path),
        ),
        patch(
            "backend.services.prosody_authority_service.apply_transform",
            side_effect=fake_apply_transform,
        ),
        patch(
            "backend.services.audio_artifacts.create_audio_artifact_from_wav_array",
            side_effect=fake_artifact,
        ),
    ):
        response = emotion_client.post(
            "/api/emotion/apply-extended",
            json={
                "audio_id": "aid1",
                "primary_emotion": "energetic",
                "primary_intensity": 100.0,
                "secondary_emotion": None,
                "secondary_intensity": 0.0,
            },
        )

    assert response.status_code == 200
    body = response.json()
    assert body["audio_id"] == "emotion_out_test"
    assert "prosody_handling" in body
    assert body["prosody_handling"]["action"] == "none"
    assert body["emotion_mapping_source"] == "canonical_preset"
    assert calls["context"] == "emotion_apply_extended"
    assert calls["pitch"] == pytest.approx(1.12)
    assert calls["rate"] == pytest.approx(1.10)
    assert calls["volume"] == pytest.approx(1.06)


def test_apply_extended_invalid_primary_emotion(emotion_client: TestClient) -> None:
    response = emotion_client.post(
        "/api/emotion/apply-extended",
        json={
            "audio_id": "x",
            "primary_emotion": "not_an_emotion",
            "primary_intensity": 50.0,
            "secondary_emotion": None,
            "secondary_intensity": 0.0,
        },
    )
    assert response.status_code == 400
