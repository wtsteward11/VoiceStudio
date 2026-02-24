import sys
from pathlib import Path
from types import SimpleNamespace

import pytest
from fastapi import HTTPException

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.services import model_preflight


def test_run_preflight_aggregates_results(monkeypatch):
    """run_preflight should aggregate per-engine dicts without modification."""

    monkeypatch.setattr(
        model_preflight,
        "ensure_xtts",
        lambda auto_download: {"ok": True, "engine": "xtts"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_piper",
        lambda auto_download: {"ok": True, "engine": "piper"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_whisper_cpp",
        lambda auto_download: {"ok": False, "message": "missing"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_sovits",
        lambda auto_download: {"ok": True, "engine": "sovits"},
    )

    result = model_preflight.run_preflight(auto_download=False)
    assert set(result["results"].keys()) == {
        "xtts_v2",
        "piper",
        "whisper_cpp",
        "gpt_sovits",
    }
    assert result["results"]["xtts_v2"]["engine"] == "xtts"
    assert result["results"]["whisper_cpp"]["ok"] is False


def test_ensure_sovits_missing_files_raises(tmp_path, monkeypatch):
    """ensure_sovits should raise HTTPException when files are missing."""

    def _fake_config_service():
        # Return model/config under tmp_path that do not exist yet
        return SimpleNamespace(
            get_engine_config=lambda _engine: {
                "parameters": {
                    "model_path": str(tmp_path / "checkpoint.pth"),
                    "config_path": str(tmp_path / "config.json"),
                }
            }
        )

    monkeypatch.setattr(model_preflight, "get_engine_config_service", _fake_config_service)

    with pytest.raises((HTTPException, model_preflight.PreflightError)) as exc:
        model_preflight.ensure_sovits(auto_download=False)

    err = exc.value
    if isinstance(err, HTTPException):
        assert err.status_code == 424
        assert "missing" in str(err.detail)
    else:
        # PreflightError (service-layer, route converts to HTTPException)
        assert "missing" in str(err).lower() or "checkpoint" in str(err).lower()


def test_ensure_sovits_ok_when_files_exist(tmp_path, monkeypatch):
    """ensure_sovits should succeed once checkpoint + config exist."""

    model_path = tmp_path / "MyVoice" / "model.pth"
    config_path = model_path.parent / "config.json"
    model_path.parent.mkdir(parents=True, exist_ok=True)
    model_path.write_bytes(b"checkpoint")
    config_path.write_text("{}", encoding="utf-8")

    def _fake_config_service():
        return SimpleNamespace(
            get_engine_config=lambda _engine: {
                "parameters": {
                    "model_path": str(model_path),
                    "config_path": str(config_path),
                }
            }
        )

    monkeypatch.setattr(model_preflight, "get_engine_config_service", _fake_config_service)

    result = model_preflight.ensure_sovits(auto_download=False)
    assert result["ok"] is True
    assert str(model_path) in result["paths"]
