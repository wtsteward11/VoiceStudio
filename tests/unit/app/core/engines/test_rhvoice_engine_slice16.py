"""Slice 16 — RHVoice contract: executable_path alias and PATH discovery names."""

from __future__ import annotations

from pathlib import Path
from unittest.mock import patch

import pytest

from app.core.engines.rhvoice_engine import RHVoiceEngine


def test_rhvoice_engine_executable_path_alias_maps_to_rhvoice_path() -> None:
    eng = RHVoiceEngine(executable_path="C:\\Tools\\rhvoice-say.exe")
    assert eng.rhvoice_path == "C:\\Tools\\rhvoice-say.exe"


def test_rhvoice_engine_rhvoice_path_wins_over_executable_path() -> None:
    eng = RHVoiceEngine(rhvoice_path="C:\\A\\say.exe", executable_path="C:\\B\\say.exe")
    assert eng.rhvoice_path == "C:\\A\\say.exe"


def test_find_executable_includes_rhvoice_client_name() -> None:
    eng = RHVoiceEngine()
    fake_which = {
        "rhvoice-client": "/usr/bin/rhvoice-client",
    }

    def _which(name: str) -> str | None:
        return fake_which.get(name)

    with patch("app.core.engines.rhvoice_engine.shutil.which", side_effect=_which):
        found = eng._find_executable("rhvoice-say", None)
    assert found == "/usr/bin/rhvoice-client"


def test_ensure_rhvoice_prefers_configured_executable(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Preflight uses the same executable_path key as the engine (Slice 16)."""
    from backend.services.model_preflight import ensure_rhvoice

    exe = tmp_path / "rhvoice-say"
    exe.write_text("@echo off\n", encoding="utf-8")

    cfg = {
        "parameters": {"executable_path": str(exe)},
    }

    class _FakeECS:
        def get_engine_config(self, engine_id: str) -> dict:
            return cfg if engine_id == "rhvoice" else {}

    monkeypatch.setattr(
        "backend.services.model_preflight.get_engine_config_service",
        lambda: _FakeECS(),
    )

    result = ensure_rhvoice(auto_download=False)
    assert result["ok"] is True
    paths = result.get("paths", [])
    assert paths and Path(paths[0]).resolve() == exe.resolve()
