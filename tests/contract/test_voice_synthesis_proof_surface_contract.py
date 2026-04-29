"""Contract tests for the route surface used by the voice synthesis proof harness."""
from __future__ import annotations

import pytest

from scripts.proof.run_voice_synthesis_real_engine_proof import ProofApiRoutes

pytestmark = pytest.mark.contract


def _assert_json_response(resp) -> dict:
    content_type = resp.headers.get("content-type", "")
    assert "application/json" in content_type
    data = resp.json()
    assert isinstance(data, dict)
    return data


def test_health_route_returns_json_object(contract_client) -> None:
    resp = contract_client.get(ProofApiRoutes.HEALTH)
    assert resp.status_code in (200, 503)
    _assert_json_response(resp)


def test_readiness_route_returns_json_or_documented_503(contract_client) -> None:
    resp = contract_client.get(ProofApiRoutes.READINESS)
    assert resp.status_code in (200, 503)
    _assert_json_response(resp)


def test_profiles_list_shape_supports_harness_selection(contract_client) -> None:
    resp = contract_client.get(ProofApiRoutes.PROFILES)
    assert resp.status_code in (200, 401, 403)
    data = _assert_json_response(resp)
    if resp.status_code == 200:
        assert "items" in data
        assert isinstance(data["items"], list)
        if data["items"]:
            assert "id" in data["items"][0]


def test_synthesis_error_shape_is_json_not_traceback(contract_client) -> None:
    resp = contract_client.post(ProofApiRoutes.SYNTHESIZE, json={})
    assert resp.status_code >= 400
    data = _assert_json_response(resp)
    assert "traceback" not in str(data).lower()


def test_audio_missing_id_returns_json_error_not_html(contract_client) -> None:
    resp = contract_client.get(ProofApiRoutes.audio_url("missing-proof-audio-id"))
    assert resp.status_code >= 400
    _assert_json_response(resp)


def test_library_upload_missing_file_returns_json_error(contract_client) -> None:
    resp = contract_client.post(ProofApiRoutes.LIBRARY_UPLOAD)
    assert resp.status_code >= 400
    _assert_json_response(resp)


def test_timeline_state_returns_json(contract_client) -> None:
    resp = contract_client.get(ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_STATE, "proof-contract"))
    assert resp.status_code in (200, 401, 403)
    _assert_json_response(resp)


def test_harness_route_paths_exist_in_registered_app(contract_client) -> None:
    paths = {getattr(route, "path", "") for route in contract_client.app.routes}
    expected = {
        ProofApiRoutes.HEALTH,
        ProofApiRoutes.READINESS,
        ProofApiRoutes.PROFILES,
        ProofApiRoutes.SYNTHESIZE,
        ProofApiRoutes.AUDIO,
        ProofApiRoutes.LIBRARY_UPLOAD,
        ProofApiRoutes.TIMELINE_STATE,
        ProofApiRoutes.TIMELINE_CREATE,
        ProofApiRoutes.TIMELINE_TRACKS,
        ProofApiRoutes.TIMELINE_CLIPS,
    }
    missing = expected - paths
    assert not missing
