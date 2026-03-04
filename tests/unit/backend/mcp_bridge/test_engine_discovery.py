"""Tests for the MCP Engine Discovery server."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from backend.mcp_bridge.engine_discovery_server import (
    _tool_get_engine_details,
    _tool_get_streaming_engines,
    _tool_list_engines,
    app,
    load_all_manifests,
)

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

SAMPLE_TTS_MANIFEST = {
    "engine_id": "test_tts",
    "name": "Test TTS Engine",
    "type": "audio",
    "subtype": "tts",
    "version": "1.0",
    "description": "A test TTS engine",
    "supported_languages": ["en", "fr"],
    "capabilities": [
        "multi_language_tts",
        "emotion_control",
    ],
    "quality_features": {
        "mos_estimate": "4.0-4.5",
    },
    "device_requirements": {
        "gpu": "optional",
        "vram_min_gb": 2,
        "ram_min_gb": 4,
    },
    "implementation_status": "full",
    "contract": {"input": {}, "output": {}},
    "config_schema": {},
}

SAMPLE_STT_MANIFEST = {
    "engine_id": "test_stt",
    "name": "Test STT Engine",
    "type": "audio",
    "subtype": "stt",
    "version": "1.0",
    "description": "A test STT engine",
    "supported_languages": ["en", "de", "auto"],
    "capabilities": [
        "speech_to_text",
        "language_detection",
    ],
    "implementation_status": "full",
}

SAMPLE_STREAMING_MANIFEST = {
    "engine_id": "test_streaming",
    "name": "Test Streaming Engine",
    "type": "audio",
    "subtype": "s2s",
    "version": "1.0",
    "description": "A test streaming engine",
    "capabilities": [
        "streaming",
        "low_latency",
        "barge_in",
    ],
    "implementation_status": "basic",
}


@pytest.fixture()
def sample_manifests():
    return {
        "test_tts": SAMPLE_TTS_MANIFEST,
        "test_stt": SAMPLE_STT_MANIFEST,
        "test_streaming": SAMPLE_STREAMING_MANIFEST,
    }


@pytest.fixture()
def engines_tmpdir(tmp_path):
    """Create a temp engines dir with sample manifests."""
    for manifest in (
        SAMPLE_TTS_MANIFEST,
        SAMPLE_STT_MANIFEST,
        SAMPLE_STREAMING_MANIFEST,
    ):
        eid = manifest["engine_id"]
        engine_dir = tmp_path / "audio" / eid
        engine_dir.mkdir(parents=True, exist_ok=True)
        path = engine_dir / "engine.manifest.json"
        path.write_text(
            json.dumps(manifest), encoding="utf-8"
        )
    return tmp_path


@pytest.fixture()
def flask_client():
    app.config["TESTING"] = True
    with app.test_client() as client:
        yield client


# ---------------------------------------------------------------------------
# Unit tests — tool functions (no Flask, no HTTP)
# ---------------------------------------------------------------------------


class TestListEngines:
    def test_returns_all_engines(self, sample_manifests):
        result = json.loads(
            _tool_list_engines(sample_manifests)
        )
        assert result["count"] == 3
        ids = {e["engine_id"] for e in result["engines"]}
        assert ids == {
            "test_tts",
            "test_stt",
            "test_streaming",
        }

    def test_engine_type_classification(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_list_engines(sample_manifests)
        )
        by_id = {
            e["engine_id"]: e for e in result["engines"]
        }
        assert by_id["test_tts"]["type"] == "tts"
        assert by_id["test_stt"]["type"] == "stt"
        assert by_id["test_streaming"]["type"] == "s2s"

    def test_includes_languages(self, sample_manifests):
        result = json.loads(
            _tool_list_engines(sample_manifests)
        )
        tts = next(
            e
            for e in result["engines"]
            if e["engine_id"] == "test_tts"
        )
        assert "en" in tts["supported_languages"]
        assert "fr" in tts["supported_languages"]

    def test_includes_quality_metrics(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_list_engines(sample_manifests)
        )
        tts = next(
            e
            for e in result["engines"]
            if e["engine_id"] == "test_tts"
        )
        assert "mos_estimate" in tts["quality_metrics"]

    def test_empty_manifests(self):
        result = json.loads(_tool_list_engines({}))
        assert result["count"] == 0
        assert result["engines"] == []


class TestGetEngineDetails:
    def test_valid_engine(self, sample_manifests):
        result = json.loads(
            _tool_get_engine_details(
                sample_manifests, "test_tts"
            )
        )
        assert result["engine_id"] == "test_tts"
        assert result["name"] == "Test TTS Engine"
        assert result["resolved_type"] == "tts"
        assert result["streaming_capable"] is False

    def test_streaming_engine_flagged(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_get_engine_details(
                sample_manifests, "test_streaming"
            )
        )
        assert result["streaming_capable"] is True

    def test_invalid_engine_returns_error(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_get_engine_details(
                sample_manifests, "nonexistent"
            )
        )
        assert "error" in result
        assert "nonexistent" in result["error"]
        assert "available_engines" in result

    def test_entry_point_excluded(self, sample_manifests):
        manifests_with_ep = {
            "ep_test": {
                **SAMPLE_TTS_MANIFEST,
                "entry_point": "some.module:Class",
            },
        }
        result = json.loads(
            _tool_get_engine_details(
                manifests_with_ep, "ep_test"
            )
        )
        assert "entry_point" not in result


class TestGetStreamingEngines:
    def test_returns_only_streaming(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_get_streaming_engines(sample_manifests)
        )
        assert result["count"] == 1
        eng = result["engines"][0]
        assert eng["engine_id"] == "test_streaming"

    def test_streaming_capabilities_listed(
        self, sample_manifests
    ):
        result = json.loads(
            _tool_get_streaming_engines(sample_manifests)
        )
        caps = result["engines"][0][
            "streaming_capabilities"
        ]
        assert "streaming" in caps
        assert "low_latency" in caps

    def test_empty_when_none_streaming(self):
        non_streaming = {
            "tts_only": SAMPLE_TTS_MANIFEST,
        }
        result = json.loads(
            _tool_get_streaming_engines(non_streaming)
        )
        assert result["count"] == 0


# ---------------------------------------------------------------------------
# Manifest loading from disk
# ---------------------------------------------------------------------------


class TestLoadManifests:
    def test_loads_from_directory(self, engines_tmpdir):
        manifests = load_all_manifests(engines_tmpdir)
        assert len(manifests) == 3
        assert "test_tts" in manifests
        assert "test_stt" in manifests
        assert "test_streaming" in manifests

    def test_nonexistent_directory_returns_empty(
        self, tmp_path
    ):
        path = tmp_path / "does_not_exist"
        manifests = load_all_manifests(path)
        assert manifests == {}

    def test_invalid_json_skipped(self, tmp_path):
        bad_dir = tmp_path / "broken"
        bad_dir.mkdir()
        bad_file = bad_dir / "engine.manifest.json"
        bad_file.write_text(
            "NOT JSON", encoding="utf-8"
        )
        manifests = load_all_manifests(tmp_path)
        assert manifests == {}


# ---------------------------------------------------------------------------
# MCP JSON-RPC endpoint (integration via test client)
# ---------------------------------------------------------------------------


class TestMCPEndpoint:
    def _rpc(
        self, client, method, params=None, msg_id=1
    ):
        body = {
            "jsonrpc": "2.0",
            "id": msg_id,
            "method": method,
        }
        if params:
            body["params"] = params
        resp = client.post("/mcp", json=body)
        assert resp.status_code == 200
        return resp.get_json()

    def test_initialize(self, flask_client):
        result = self._rpc(
            flask_client,
            "initialize",
            {"protocolVersion": "2025-03-26"},
        )
        rv = result["result"]
        assert rv["protocolVersion"] == "2025-03-26"
        server = rv["serverInfo"]
        assert server["name"] == (
            "voicestudio-engine-discovery"
        )

    def test_tools_list_returns_three_tools(
        self, flask_client
    ):
        result = self._rpc(
            flask_client, "tools/list"
        )
        tools = result["result"]["tools"]
        assert len(tools) == 3
        names = {t["name"] for t in tools}
        assert names == {
            "list_engines",
            "get_engine_details",
            "get_streaming_engines",
        }

    def test_ping(self, flask_client):
        result = self._rpc(flask_client, "ping")
        assert result["result"] == {}

    def test_unknown_method(self, flask_client):
        result = self._rpc(
            flask_client, "bogus/method"
        )
        assert "error" in result
        assert result["error"]["code"] == -32601

    def test_notification_returns_202(
        self, flask_client
    ):
        resp = flask_client.post(
            "/mcp",
            json={
                "jsonrpc": "2.0",
                "method": "notifications/initialized",
            },
        )
        assert resp.status_code == 202

    def test_tools_call_unknown_tool(
        self, flask_client
    ):
        result = self._rpc(
            flask_client,
            "tools/call",
            {"name": "no_such_tool"},
        )
        assert "error" in result
        assert result["error"]["code"] == -32601

    def test_sse_get(self, flask_client):
        resp = flask_client.get("/mcp")
        assert resp.status_code == 200
        assert "text/event-stream" in resp.content_type
