import json
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock

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
        "ensure_espeak_ng",
        lambda auto_download: {"ok": True, "engine": "espeak_ng"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_rhvoice",
        lambda auto_download: {"ok": False, "engine": "rhvoice"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_silero",
        lambda auto_download: {"ok": True, "engine": "silero"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_chatterbox",
        lambda auto_download: {"ok": True, "engine": "chatterbox"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_tortoise",
        lambda auto_download: {"ok": True, "engine": "tortoise"},
    )
    monkeypatch.setattr(
        model_preflight,
        "ensure_openvoice",
        lambda auto_download: {"ok": True, "engine": "openvoice"},
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
    monkeypatch.setattr(
        model_preflight,
        "ensure_faster_whisper",
        lambda auto_download: {"ok": True, "engine": "faster_whisper"},
    )

    result = model_preflight.run_preflight(auto_download=False)
    assert set(result["results"].keys()) == {
        "xtts_v2",
        "piper",
        "espeak_ng",
        "rhvoice",
        "silero",
        "chatterbox",
        "tortoise",
        "openvoice",
        "whisper_cpp",
        "faster_whisper",
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


def test_ensure_chatterbox_venv_advanced_tts_not_created(monkeypatch):
    """Missing venv_advanced_tts yields explicit PreflightError (Slice 17A)."""

    class _Mgr:
        def is_venv_created(self, _fam):
            return False

        def get_python_executable(self, _fam):
            return "unused"

    def _fake_get_venv_manager():
        return _Mgr()

    monkeypatch.setattr(
        "app.core.runtime.venv_family_manager.get_venv_manager",
        _fake_get_venv_manager,
    )

    with pytest.raises(model_preflight.PreflightError) as exc:
        model_preflight.ensure_chatterbox(auto_download=False)

    detail = exc.value.detail
    assert isinstance(detail, dict)
    assert detail.get("reason") == "venv_advanced_tts_not_created"
    assert "venv_advanced_tts" in detail.get("message", "")


def test_ensure_chatterbox_subprocess_ok(monkeypatch, tmp_path):
    """Green path: import + HF probe succeed in family venv python (mocked)."""
    fake_py = tmp_path / "python.exe"
    fake_py.write_text("")

    monkeypatch.setattr(
        model_preflight,
        "_require_venv_advanced_tts_python_exe",
        lambda: fake_py,
    )

    def fake_run(cmd, **kwargs):
        proc = MagicMock()
        proc.returncode = 0
        proc.stderr = ""
        if len(cmd) >= 3 and cmd[1] == "-c" and "chatterbox" in cmd[2]:
            proc.stdout = "chatterbox_import_ok\n"
        elif len(cmd) >= 2 and "_chatterbox_hf.py" in str(cmd[1]):
            proc.stdout = json.dumps(
                {
                    "ok": True,
                    "path": str(tmp_path / "hub" / "ve.safetensors"),
                    "downloaded": False,
                }
            )
        else:
            proc.returncode = 1
            proc.stdout = ""
        return proc

    monkeypatch.setattr(model_preflight.subprocess, "run", fake_run)

    out = model_preflight.ensure_chatterbox(auto_download=False)
    assert out["ok"] is True
    assert out.get("python_exe") == str(fake_py)
    assert "chatterbox" in out.get("message", "").lower()


def test_ensure_tortoise_venv_tortoise_not_created(monkeypatch):
    """Missing venv_tortoise yields explicit PreflightError (Slice 18B)."""

    class _Mgr:
        def is_venv_created(self, _fam):
            return False

        def get_python_executable(self, _fam):
            return "unused"

    def _fake_get_venv_manager():
        return _Mgr()

    monkeypatch.setattr(
        "app.core.runtime.venv_family_manager.get_venv_manager",
        _fake_get_venv_manager,
    )

    with pytest.raises(model_preflight.PreflightError) as exc:
        model_preflight.ensure_tortoise(auto_download=False)

    detail = exc.value.detail
    assert isinstance(detail, dict)
    assert detail.get("reason") == "venv_tortoise_not_created"
    assert "venv_tortoise" in detail.get("message", "")


def test_ensure_tortoise_no_cached_weights_raises_when_import_ok(monkeypatch, tmp_path):
    """Empty tortoise_models with auto_download=False yields 424 (Slice 18B)."""
    fake_py = tmp_path / "python.exe"
    fake_py.write_text("")

    monkeypatch.setattr(
        model_preflight,
        "_require_venv_tortoise_python_exe",
        lambda: fake_py,
    )
    monkeypatch.setattr(model_preflight, "_subprocess_tortoise_import_ok", lambda _p: None)

    toroot = tmp_path / "tcache"
    tmodels = toroot / "tortoise_models"
    tmodels.mkdir(parents=True)
    monkeypatch.setenv("VOICESTUDIO_MODELS_PATH", str(toroot))
    monkeypatch.setattr(model_preflight, "_tortoise_has_cached_weights", lambda _p: False)

    with pytest.raises(model_preflight.PreflightError) as exc:
        model_preflight.ensure_tortoise(auto_download=False)

    assert exc.value.status_code == 424
    detail = exc.value.detail
    assert isinstance(detail, dict)
    assert "tortoise_models" in detail.get("message", "").lower() or "cached" in detail.get(
        "message",
        "",
    ).lower()


def test_ensure_tortoise_ok_when_dummy_weight_present(monkeypatch, tmp_path):
    """Green path: import probe passes; cached weight file present (mocked)."""
    fake_py = tmp_path / "python.exe"
    fake_py.write_text("")

    monkeypatch.setattr(
        model_preflight,
        "_require_venv_tortoise_python_exe",
        lambda: fake_py,
    )
    monkeypatch.setattr(model_preflight, "_subprocess_tortoise_import_ok", lambda _p: None)

    toroot = tmp_path / "tcache"
    tmodels = toroot / "tortoise_models"
    tmodels.mkdir(parents=True)
    (tmodels / "probe.bin").write_bytes(b"x")
    monkeypatch.setenv("VOICESTUDIO_MODELS_PATH", str(toroot))

    out = model_preflight.ensure_tortoise(auto_download=False)
    assert out["ok"] is True
    assert str(tmodels) in (out.get("paths") or [""])[0]
    assert out.get("python_exe") == str(fake_py)


def test_ensure_openvoice_venv_advanced_tts_not_created(monkeypatch):
    """Missing venv_advanced_tts yields explicit PreflightError for OpenVoice (Slice 19)."""

    class _Mgr:
        def is_venv_created(self, _fam):
            return False

        def get_python_executable(self, _fam):
            return "unused"

    def _fake_get_venv_manager():
        return _Mgr()

    monkeypatch.setattr(
        "app.core.runtime.venv_family_manager.get_venv_manager",
        _fake_get_venv_manager,
    )

    with pytest.raises(model_preflight.PreflightError) as exc:
        model_preflight.ensure_openvoice(auto_download=False)

    detail = exc.value.detail
    assert isinstance(detail, dict)
    assert detail.get("reason") == "venv_advanced_tts_not_created"
    assert "venv_advanced_tts" in detail.get("message", "")


def test_ensure_openvoice_import_fail(monkeypatch, tmp_path):
    fake_py = tmp_path / "python.exe"
    fake_py.write_text("")

    monkeypatch.setattr(
        model_preflight,
        "_require_venv_advanced_tts_python_exe",
        lambda **_: fake_py,
    )
    monkeypatch.setattr(
        model_preflight,
        "_subprocess_openvoice_import_ok",
        lambda _p: "ModuleNotFoundError: No module named 'openvoice'",
    )

    with pytest.raises(model_preflight.PreflightError) as exc:
        model_preflight.ensure_openvoice(auto_download=False)

    assert exc.value.status_code == 503
    detail = exc.value.detail
    assert isinstance(detail, dict)
    assert "openvoice" in detail.get("message", "").lower()


def test_ensure_openvoice_ok_when_checkpoint_layout_present(monkeypatch, tmp_path):
    """Green path: import probe passes and both asset trees have config+checkpoint."""
    fake_py = tmp_path / "python.exe"
    fake_py.write_text("")

    monkeypatch.setattr(
        model_preflight,
        "_require_venv_advanced_tts_python_exe",
        lambda **_: fake_py,
    )
    monkeypatch.setattr(model_preflight, "_subprocess_openvoice_import_ok", lambda _p: None)

    root = tmp_path / "models"
    base = root / "openvoice" / "base_speakers" / "EN"
    conv = root / "openvoice" / "converter"
    base.mkdir(parents=True)
    conv.mkdir(parents=True)
    (base / "config.json").write_text("{}", encoding="utf-8")
    (base / "checkpoint.pth").write_bytes(b"x")
    (conv / "config.json").write_text("{}", encoding="utf-8")
    (conv / "checkpoint.pth").write_bytes(b"y")
    monkeypatch.setenv("VOICESTUDIO_MODELS_PATH", str(root))

    out = model_preflight.ensure_openvoice(auto_download=False)
    assert out["ok"] is True
    assert out.get("python_exe") == str(fake_py)
    assert "openvoice" in out.get("message", "").lower()
