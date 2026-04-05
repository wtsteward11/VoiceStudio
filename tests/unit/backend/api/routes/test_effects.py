"""
GAP-039: deterministic tests for effects routes (project process, bypass, preview query).

Replaces skipped legacy module that targeted removed in-memory _effect_chains dict.
"""

from __future__ import annotations

import sys
from datetime import datetime
from pathlib import Path

import numpy as np
import pytest
import soundfile as sf
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.api.routes import effects
from backend.api.routes.effects import (
    Effect,
    EffectChain,
    EffectParameter,
    EffectProcessRequest,
)


@pytest.fixture()
def effects_app() -> FastAPI:
    app = FastAPI()
    app.include_router(effects.router)
    app.include_router(effects.project_effects_router)
    return app


def _chain(
    *,
    chain_id: str = "chain-1",
    project_id: str = "proj-1",
    fx: list[Effect] | None = None,
) -> EffectChain:
    now = datetime.utcnow().isoformat()
    return EffectChain(
        id=chain_id,
        name="Test Chain",
        project_id=project_id,
        effects=fx or [],
        created=now,
        modified=now,
    )


def test_process_chain_in_memory_bypass_passthrough() -> None:
    from backend.services.effect_chain_process import process_chain_in_memory

    now = datetime.utcnow().isoformat()
    chain = _chain(
        fx=[
            Effect(
                id="e1",
                type="eq",
                name="EQ",
                enabled=True,
                order=0,
                parameters=[
                    EffectParameter(name="low_gain", value=0.0, min_value=-12.0, max_value=12.0)
                ],
            )
        ],
    )
    audio = np.linspace(-0.1, 0.1, 64, dtype=np.float32)
    out, passthrough = process_chain_in_memory(
        chain, audio, 48000, bypass_chain=True, strict_no_enabled=True
    )
    assert passthrough is True
    assert out.shape == audio.shape
    assert np.allclose(out, audio)


def test_process_chain_in_memory_strict_raises_when_no_enabled() -> None:
    from fastapi import HTTPException

    from backend.services.effect_chain_process import process_chain_in_memory

    chain = _chain(fx=[])
    audio = np.zeros(32, dtype=np.float32)
    with pytest.raises(HTTPException) as exc:
        process_chain_in_memory(
            chain, audio, 48000, bypass_chain=False, strict_no_enabled=True
        )
    assert exc.value.status_code == 400


def _patch_audio_registry(monkeypatch: pytest.MonkeyPatch, wav: Path) -> None:
    """Monkeypatch AudioRegistry.get_path to resolve 'audio-reg-1' to a temp WAV."""
    from backend.services.audio_artifacts.registry import AudioRegistry

    _orig_get = AudioRegistry.get_path

    @staticmethod  # type: ignore[misc]
    def _mock_get(audio_id: str) -> str | None:
        if audio_id == "audio-reg-1":
            return str(wav)
        return None

    monkeypatch.setattr(AudioRegistry, "get_path", _mock_get)


def test_project_process_bypass_returns_input_audio_id(
    effects_app: FastAPI, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    wav = tmp_path / "in.wav"
    sf.write(str(wav), np.zeros(800, dtype=np.float32), 48000)

    chain = _chain()

    monkeypatch.setattr(effects, "_get_chain", lambda cid: chain if cid == "chain-1" else None)
    _patch_audio_registry(monkeypatch, wav)

    client = TestClient(effects_app)
    r = client.post(
        "/api/effects/chains/proj-1/chain-1/process",
        params={"audio_id": "audio-reg-1", "bypass_chain": "true"},
    )
    assert r.status_code == 200, r.text
    data = r.json()
    assert data["success"] is True
    assert data["output_audio_id"] == "audio-reg-1"
    assert "bypass" in data["message"].lower()


def test_project_process_preview_query_tags_message(
    effects_app: FastAPI, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    wav = tmp_path / "in.wav"
    sf.write(str(wav), np.zeros(800, dtype=np.float32), 48000)
    chain = _chain()

    monkeypatch.setattr(effects, "_get_chain", lambda cid: chain if cid == "chain-1" else None)
    _patch_audio_registry(monkeypatch, wav)

    client = TestClient(effects_app)
    r = client.post(
        "/api/effects/chains/proj-1/chain-1/process",
        params={"audio_id": "audio-reg-1", "bypass_chain": "true", "preview": "true"},
    )
    assert r.status_code == 200, r.text
    assert "[preview]" in r.json()["message"]


def test_legacy_body_process_no_enabled_returns_400(
    effects_app: FastAPI, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    wav = tmp_path / "in.wav"
    sf.write(str(wav), np.zeros(400, dtype=np.float32), 48000)
    chain = _chain()

    monkeypatch.setattr(effects, "_get_chain", lambda cid: chain if cid == "chain-1" else None)
    _patch_audio_registry(monkeypatch, wav)

    client = TestClient(effects_app)
    r = client.post(
        "/api/effects/chains/chain-1/process?project_id=proj-1",
        json=EffectProcessRequest(audio_id="audio-reg-1").model_dump(),
    )
    assert r.status_code == 400


def test_legacy_body_process_bypass_ok_when_no_enabled(
    effects_app: FastAPI, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    wav = tmp_path / "in.wav"
    sf.write(str(wav), np.zeros(400, dtype=np.float32), 48000)
    chain = _chain()

    monkeypatch.setattr(effects, "_get_chain", lambda cid: chain if cid == "chain-1" else None)
    _patch_audio_registry(monkeypatch, wav)

    client = TestClient(effects_app)
    r = client.post(
        "/api/effects/chains/chain-1/process?project_id=proj-1&bypass_chain=true",
        json=EffectProcessRequest(audio_id="audio-reg-1").model_dump(),
    )
    assert r.status_code == 200, r.text
    assert r.json()["output_audio_id"] == "audio-reg-1"


def test_router_registers_process_routes(effects_app: FastAPI) -> None:
    paths = [getattr(r, "path", "") for r in effects_app.routes]
    assert any("process" in p and "{chain_id}" in p for p in paths)
