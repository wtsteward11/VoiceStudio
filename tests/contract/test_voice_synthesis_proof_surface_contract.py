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


def _openapi(contract_client) -> dict:
    resp = contract_client.get("/openapi.json")
    assert resp.status_code == 200
    data = resp.json()
    assert isinstance(data, dict)
    return data


def _schema_props(openapi: dict, component_name: str) -> dict:
    schemas = openapi.get("components", {}).get("schemas", {})
    schema = schemas.get(component_name)
    assert isinstance(schema, dict), f"missing OpenAPI schema {component_name}"
    props = schema.get("properties", {})
    assert isinstance(props, dict)
    return props


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
        ProofApiRoutes.TIMELINE_EXPORT,
    }
    missing = expected - paths
    assert not missing


def test_product_closure_synthesis_request_accepts_project_and_session_ids(contract_client) -> None:
    props = _schema_props(_openapi(contract_client), "VoiceSynthesizeRequest")
    assert "project_id" in props
    assert "session_id" in props


def test_product_closure_synthesis_response_exposes_identity_fields(contract_client) -> None:
    props = _schema_props(_openapi(contract_client), "VoiceSynthesizeResponse")
    assert "generated_audio_id" in props
    assert "profile_id" in props


def test_product_closure_library_upload_exposes_provenance_parameters(contract_client) -> None:
    openapi = _openapi(contract_client)
    operation = openapi["paths"][ProofApiRoutes.LIBRARY_UPLOAD]["post"]
    parameters = {
        parameter["name"]
        for parameter in operation.get("parameters", [])
        if isinstance(parameter, dict)
    }
    assert {
        "metadata_json",
        "project_id",
        "session_id",
        "generated_audio_id",
        "source_engine",
        "routed_engine",
        "profile_id",
    } <= parameters


def test_product_closure_timeline_clip_request_accepts_metadata(contract_client) -> None:
    props = _schema_props(_openapi(contract_client), "AddClipRequest")
    assert "metadata" in props


def test_product_closure_timeline_export_route_shape(contract_client) -> None:
    openapi = _openapi(contract_client)
    operation = openapi["paths"][ProofApiRoutes.TIMELINE_EXPORT]["post"]
    request_ref = (
        operation["requestBody"]["content"]["application/json"]["schema"]["$ref"]
    )
    response_ref = (
        operation["responses"]["200"]["content"]["application/json"]["schema"]["$ref"]
    )
    assert request_ref.endswith("ExportRequest")
    assert response_ref.endswith("ExportResponse")
    props = _schema_props(openapi, "ExportRequest")
    assert {"output_path", "format", "sample_rate"} <= set(props)


def test_product_closure_timeline_export_empty_timeline_error_is_json(contract_client) -> None:
    resp = contract_client.post(
        ProofApiRoutes.TIMELINE_EXPORT,
        json={"output_path": "proof-contract-empty.wav", "format": "wav"},
    )
    assert resp.status_code >= 400
    data = _assert_json_response(resp)
    assert "traceback" not in str(data).lower()


def test_product_closure_proof_schema_exposes_project_generated_audio_export() -> None:
    import json
    from pathlib import Path

    schema = json.loads(Path("schemas/voice_synthesis_proof.schema.json").read_text(encoding="utf-8"))
    props = schema["properties"]
    assert "project" in props
    assert "generated_audio" in props
    assert "export" in props
