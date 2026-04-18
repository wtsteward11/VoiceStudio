"""
Slice 11: Primary engine failure must not silently substitute gTTS/pyttsx3 utility engines.
"""

from __future__ import annotations

import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.api.dependencies import require_synthesis_clearance
from backend.api.middleware.auth_middleware import require_auth_if_enabled
from backend.api.routes.voice._shared import router
from backend.api.security.voice_policy import enforce_voice_policy_http


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


class _BoomEngine:
    """Primary engine that always fails — must not trigger utility substitution."""

    def synthesize(self, **kwargs):
        raise RuntimeError("simulated primary engine failure")

    def cleanup(self) -> None:
        return None

    def initialize(self) -> None:
        return None


@patch("backend.services.profile_search_service.get_profiles_proxy")
@patch("backend.services.synthesis_service._resolve_profile_audio")
def test_runtime_error_no_utility_routed_engine_in_response(
    mock_resolve_audio,
    mock_profiles,
    synthesis_client: TestClient,
    tmp_path,
) -> None:
    ref = tmp_path / "reference_audio.wav"
    ref.write_bytes(b"RIFF" + b"\x00" * 120)

    async def _resolve(*_a, **_k):
        return str(ref)

    mock_resolve_audio.side_effect = _resolve
    mock_profiles.return_value = {
        "ptest": SimpleNamespace(reference_audio_url=None),
    }

    mock_router = MagicMock()
    mock_router.list_engines.return_value = ["piper"]
    mock_router.get_engine.return_value = _BoomEngine()

    import backend.services.engine_shared as engine_shared

    prev_router = engine_shared.engine_router
    prev_avail = engine_shared.ENGINE_AVAILABLE
    engine_shared.engine_router = mock_router
    engine_shared.ENGINE_AVAILABLE = True
    try:
        payload = {
            "engine": "piper",
            "profile_id": "ptest",
            "text": "Hello no utility fallback",
            "language": "en",
        }
        response = synthesis_client.post("/api/voice/synthesize", json=payload)
    finally:
        engine_shared.engine_router = prev_router
        engine_shared.ENGINE_AVAILABLE = prev_avail

    assert response.status_code >= 400, response.text
    body = response.text
    assert "gtts_utility" not in body
    assert "pyttsx3_utility" not in body
