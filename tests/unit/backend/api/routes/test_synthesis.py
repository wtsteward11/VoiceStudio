"""
GAP-004: Primary voice synthesis routes delegate to SynthesisService.

Seam proof: route is thin; service is canonical authority.
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

import backend.api.routes.voice.synthesis as synthesis_routes
from backend.api.dependencies import require_synthesis_clearance
from backend.api.middleware.auth_middleware import require_auth_if_enabled
from backend.api.models_additional import (
    MultiPassSynthesisResponse,
    PassResult,
    QualityMetrics,
    VoiceSynthesizeResponse,
)
from backend.api.routes.voice._shared import router
from backend.api.security.voice_policy import enforce_voice_policy_http
from backend.core.exceptions import ServiceError


@pytest.fixture
def synthesis_client() -> TestClient:
    app = FastAPI()
    app.include_router(router)

    async def _noop() -> None:
        return None

    app.dependency_overrides[require_auth_if_enabled] = _noop
    app.dependency_overrides[enforce_voice_policy_http] = _noop
    app.dependency_overrides[require_synthesis_clearance] = _noop
    return TestClient(app)


def test_voice_synthesis_service_module_removed() -> None:
    spec = importlib.util.find_spec("backend.services.voice_synthesis_service")
    assert spec is None


@patch(
    "backend.api.routes.voice.synthesis.SynthesisService.synthesize",
    new_callable=AsyncMock,
)
def test_post_synthesize_delegates_to_service(
    mock_synthesize: AsyncMock, synthesis_client: TestClient
) -> None:
    mock_synthesize.return_value = VoiceSynthesizeResponse(
        audio_id="test_audio_1",
        audio_url="/api/voice/audio/test_audio_1",
        duration=1.25,
        quality_score=0.88,
        quality_metrics=None,
        ssml_handling=None,
    )
    payload = {
        "engine": "piper",
        "profile_id": "profile-a",
        "text": "Hello delegation",
        "language": "en",
    }
    response = synthesis_client.post("/api/voice/synthesize", json=payload)
    assert response.status_code == 200, response.text
    data = response.json()
    assert data["audio_id"] == "test_audio_1"
    mock_synthesize.assert_awaited_once()
    call_args = mock_synthesize.await_args
    assert call_args is not None
    req_arg = call_args.args[0]
    assert req_arg.profile_id == "profile-a"
    assert req_arg.text == "Hello delegation"


@patch(
    "backend.api.routes.voice.synthesis.SynthesisService.synthesize",
    new_callable=AsyncMock,
)
def test_service_error_maps_to_http_exception(
    mock_synthesize: AsyncMock, synthesis_client: TestClient
) -> None:
    mock_synthesize.side_effect = ServiceError(422, "SSML rejected for this engine")
    response = synthesis_client.post(
        "/api/voice/synthesize",
        json={
            "engine": "piper",
            "profile_id": "p1",
            "text": "Hello",
            "language": "en",
        },
    )
    assert response.status_code == 422
    assert "SSML rejected" in response.json().get("detail", "")


@patch(
    "backend.api.routes.voice.synthesis.SynthesisService.synthesize_multipass",
    new_callable=AsyncMock,
)
def test_multipass_delegates_to_service(
    mock_mp: AsyncMock, synthesis_client: TestClient
) -> None:
    qm = QualityMetrics(mos_score=4.0, similarity=0.9)
    mock_mp.return_value = MultiPassSynthesisResponse(
        audio_id="mp_best",
        audio_url="/api/voice/audio/mp_best",
        duration=2.0,
        quality_score=0.9,
        quality_metrics=qm,
        passes_completed=1,
        passes=[
            PassResult(
                pass_number=1,
                audio_id="mp_best",
                audio_url="/api/voice/audio/mp_best",
                quality_metrics=qm,
                quality_score=0.9,
                improvement=None,
            )
        ],
        best_pass=1,
        improvement_tracking=[0.0],
    )
    response = synthesis_client.post(
        "/api/voice/synthesize/multipass",
        json={
            "engine": "piper",
            "profile_id": "p1",
            "text": "Multi pass text",
            "language": "en",
        },
    )
    assert response.status_code == 200, response.text
    mock_mp.assert_awaited_once()


def test_voice_testing_module_imports_canonical_service() -> None:
    import backend.api.routes.voice.testing as vt

    src = Path(vt.__file__).read_text(encoding="utf-8")
    assert "SynthesisService" in src
    assert "voice_synthesis_service" not in src
