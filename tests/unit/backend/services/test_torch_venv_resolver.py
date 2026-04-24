"""Unit tests for GAP-062 torch venv resolver."""

from __future__ import annotations

from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

from backend.services.torch_venv_resolver import (
    build_effective_torch_status_payload,
    resolve_torch_runtime,
)


@pytest.fixture
def core_tts_family():
    from app.core.runtime.venv_family_manager import VenvFamily

    return VenvFamily.CORE_TTS


def test_chatterbox_maps_to_advanced_tts():
    from app.core.runtime.venv_family_manager import get_venv_manager

    mgr = get_venv_manager()
    f = mgr.get_family_for_engine("chatterbox")
    assert f is not None
    assert f.value == "venv_advanced_tts"


def test_engine_unmapped():
    out = resolve_torch_runtime("zzz_unknown_engine_id_quiet")
    assert out["status"] == "unresolved"
    assert out["detail"] == "engine_not_mapped"
    assert out["family"] is None


def test_piper_not_torch_relevant_family():
    out = resolve_torch_runtime("piper")
    assert out["status"] == "unresolved"
    assert out["detail"] == "not_torch_relevant_family"
    assert out["family"] is not None


@patch("app.core.runtime.venv_family_manager.get_venv_manager")
def test_engine_mapped_venv_present(mock_gvm, core_tts_family):
    mgr = MagicMock()
    mgr.get_family_for_engine.return_value = core_tts_family
    mgr.is_venv_created.return_value = True
    mgr.get_python_executable.return_value = Path("/fake/python.exe")
    mock_gvm.return_value = mgr

    with patch(
        "backend.services.torch_venv_resolver.probe_torch_version",
        return_value=("2.4.0", None),
    ):
        out = resolve_torch_runtime("xtts_v2")

    assert out["status"] == "present"
    assert out["torch_version"] == "2.4.0"
    assert out["engine_id"] == "xtts_v2"
    assert Path(out["python_exe"]) == Path("/fake/python.exe")


@patch("app.core.runtime.venv_family_manager.get_venv_manager")
def test_engine_mapped_venv_missing(mock_gvm, core_tts_family):
    mgr = MagicMock()
    mgr.get_family_for_engine.return_value = core_tts_family
    mgr.is_venv_created.return_value = False
    mock_gvm.return_value = mgr

    with patch("backend.services.torch_venv_resolver.probe_torch_version") as mock_probe:
        out = resolve_torch_runtime("xtts_v2")

    mock_probe.assert_not_called()
    assert out["status"] == "missing"
    assert out["detail"] == "venv_not_created"


@patch("app.core.runtime.venv_family_manager.get_venv_manager")
def test_engine_mapped_venv_probe_fails(mock_gvm, core_tts_family):
    mgr = MagicMock()
    mgr.get_family_for_engine.return_value = core_tts_family
    mgr.is_venv_created.return_value = True
    mgr.get_python_executable.return_value = Path("/fake/python.exe")
    mock_gvm.return_value = mgr

    with patch(
        "backend.services.torch_venv_resolver.probe_torch_version",
        return_value=(None, "ModuleNotFoundError: No module named 'torch'"),
    ):
        out = resolve_torch_runtime("xtts_v2")

    assert out["status"] == "incompatible"
    assert out["torch_version"] is None
    assert "torch" in (out.get("detail") or "")


@patch("app.core.runtime.venv_family_manager.get_venv_manager")
def test_build_effective_payload_structure(mock_gvm):
    mgr = MagicMock()
    mgr.is_venv_created.return_value = False
    mgr.get_python_executable.return_value = Path("/fake/python.exe")
    mock_gvm.return_value = mgr

    payload = build_effective_torch_status_payload()
    assert payload["source"] == "torch_venv_resolver"
    assert "families" in payload
    assert isinstance(payload["families"], list)
    for row in payload["families"]:
        assert "family" in row
        assert "status" in row
        assert "engines" in row
        assert "source" in row


@patch("app.core.runtime.venv_family_manager.get_venv_manager")
def test_build_effective_payload_all_torch_families_covered(mock_gvm):
    mgr = MagicMock()
    mgr.is_venv_created.return_value = False
    mock_gvm.return_value = mgr

    payload = build_effective_torch_status_payload()
    keys = {row["family"] for row in payload["families"]}
    assert keys == {
        "venv_core_tts",
        "venv_advanced_tts",
        "venv_stt",
        "venv_voice_conversion",
        "venv_openvoice",
    }
