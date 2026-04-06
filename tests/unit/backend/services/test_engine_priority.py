"""
GAP-053: Engine priority resolution and settings validation.
"""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from pydantic import ValidationError

project_root = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(project_root))


@pytest.fixture()
def settings_module():
    from backend.api.routes import settings as s

    return s


def test_user_priority_overrides_yaml(settings_module):
    from backend.services.engine_priority import resolve_engine_priority

    eng = settings_module.EngineSettings(engine_priority_order=["piper", "xtts_v2"])
    data = settings_module.SettingsData(engine=eng)
    with patch("backend.api.routes.settings.load_settings", return_value=data):
        order, source = resolve_engine_priority("tts")
    assert source == "user"
    assert order == ["piper", "xtts_v2"]


def test_empty_user_priority_falls_through_to_yaml(settings_module):
    from backend.services.engine_priority import resolve_engine_priority

    eng = settings_module.EngineSettings(engine_priority_order=[])
    data = settings_module.SettingsData(engine=eng)
    mock_cfg = MagicMock()
    mock_cfg.get_fallback_chain.return_value = ["a", "b"]
    with (
        patch("backend.api.routes.settings.load_settings", return_value=data),
        patch(
            "backend.platform.config.unified_config.get_config",
            return_value=mock_cfg,
        ),
    ):
        order, source = resolve_engine_priority("tts")
    assert source == "yaml"
    assert order == ["a", "b"]


def test_empty_yaml_falls_through_to_defaults(settings_module):
    from backend.services.engine_priority import DEFAULT_TTS_PRIORITY, resolve_engine_priority

    eng = settings_module.EngineSettings(engine_priority_order=[])
    data = settings_module.SettingsData(engine=eng)
    mock_cfg = MagicMock()
    mock_cfg.get_fallback_chain.return_value = []
    with (
        patch("backend.api.routes.settings.load_settings", return_value=data),
        patch(
            "backend.platform.config.unified_config.get_config",
            return_value=mock_cfg,
        ),
    ):
        order, source = resolve_engine_priority("tts")
    assert source == "default"
    assert order == DEFAULT_TTS_PRIORITY


def test_unavailable_engine_skipped_in_resolution():
    from backend.services.engine_priority import build_effective_engine_priority_payload

    with (
        patch(
            "backend.services.engine_priority.resolve_engine_priority",
            return_value=(["missing_one", "piper"], "user"),
        ),
        patch(
            "backend.services.engine_priority.list_valid_engine_ids",
            return_value=["piper"],
        ),
    ):
        payload = build_effective_engine_priority_payload("tts")
    assert payload["source"] == "user"
    assert payload["order"] == ["missing_one", "piper"]
    assert payload["available"] == ["piper"]
    assert payload["skipped"] == ["missing_one"]


def test_priority_order_round_trips_through_settings(settings_module, tmp_path):
    settings_file = tmp_path / "settings.json"
    eng = settings_module.EngineSettings(engine_priority_order=["xtts_v2", "piper"])
    data = settings_module.SettingsData(
        general=settings_module.GeneralSettings(),
        engine=eng,
        audio=settings_module.AudioSettings(),
        timeline=settings_module.TimelineSettings(),
        backend=settings_module.BackendSettings(),
        performance=settings_module.PerformanceSettings(),
        plugins=settings_module.PluginSettings(),
        mcp=settings_module.McpSettings(),
        quality=settings_module.QualitySettings(),
    )
    with patch.object(settings_module, "SETTINGS_FILE", settings_file):
        with patch.object(settings_module, "HAS_UNIFIED_CONFIG", False):
            settings_module.save_settings(data)
            loaded = settings_module.load_settings(force_reload=True)
    assert loaded.engine is not None
    assert loaded.engine.engine_priority_order == ["xtts_v2", "piper"]


def test_invalid_engine_id_rejected(settings_module):
    with pytest.raises(ValidationError):
        settings_module.EngineSettings(engine_priority_order=["BadId"])


def test_effective_priority_endpoint_returns_source(settings_module):
    from backend.api import auth as _auth_module

    app = FastAPI()
    app.include_router(settings_module.router)
    app.dependency_overrides[_auth_module.require_auth_if_enabled] = lambda: None
    client = TestClient(app)

    eng = settings_module.EngineSettings(engine_priority_order=["piper"])
    mock_settings = settings_module.SettingsData(engine=eng)

    with (
        patch("backend.api.routes.settings.load_settings", return_value=mock_settings),
        patch(
            "backend.services.engine_priority.list_valid_engine_ids",
            return_value=["piper"],
        ),
    ):
        r = client.get("/api/settings/engine-priority/effective")
    assert r.status_code == 200
    body = r.json()
    assert body["source"] == "user"
    assert body["order"] == ["piper"]
    assert body["available"] == ["piper"]
