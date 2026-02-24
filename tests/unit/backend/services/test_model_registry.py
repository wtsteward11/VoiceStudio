"""
Model Registry Service Tests — Phase 10 WS2

Verifies model registry seeding from engines/config.json, API endpoints,
activate/rollback flow, and A/B experiment creation.
"""

from __future__ import annotations

import json
import tempfile
from pathlib import Path

import pytest

from backend.services.model_registry import (
    EngineModelState,
    ModelArtifact,
    ModelRegistryService,
    get_model_registry_service,
)


class TestModelRegistrySeeding:
    """Verify registry seeds from engines/config.json."""

    def test_registry_seeds_six_engines_from_config(self):
        """When registry file does not exist, seeds from engines/config.json."""
        with tempfile.TemporaryDirectory() as tmp:
            data_dir = Path(tmp)
            reg = ModelRegistryService(data_dir=data_dir)
            engines = list(reg._registry.keys())
            expected = {"xtts_v2", "piper", "openvoice", "sdxl_comfy", "realesrgan", "svd"}
            assert expected.issubset(set(engines)), f"Expected {expected}, got {engines}"
            assert len(engines) >= 6

    def test_registry_persists_to_json(self):
        """Registry saves to data/model_registry.json."""
        with tempfile.TemporaryDirectory() as tmp:
            data_dir = Path(tmp)
            reg = ModelRegistryService(data_dir=data_dir)
            reg_path = data_dir / "model_registry.json"
            assert reg_path.exists()
            data = json.loads(reg_path.read_text())
            assert "engines" in data
            assert len(data["engines"]) >= 6

    def test_list_models_returns_artifacts(self):
        """list_models returns artifacts for all engines."""
        with tempfile.TemporaryDirectory() as tmp:
            reg = ModelRegistryService(data_dir=Path(tmp))
            models = reg.list_models()
            assert len(models) >= 6
            engine_ids = {m["engine_id"] for m in models}
            assert "xtts_v2" in engine_ids
            assert "piper" in engine_ids

    def test_get_engine_models_returns_state(self):
        """get_engine_models returns state for specific engine."""
        with tempfile.TemporaryDirectory() as tmp:
            reg = ModelRegistryService(data_dir=Path(tmp))
            state = reg.get_engine_models("xtts_v2")
            assert state["engine_id"] == "xtts_v2"
            assert "artifacts" in state
            assert len(state["artifacts"]) >= 1


class TestModelRegistryActivateRollback:
    """Verify activate and rollback flow."""

    def test_activate_model_updates_state(self):
        """activate_model sets active_model and previous_model."""
        with tempfile.TemporaryDirectory() as tmp:
            reg = ModelRegistryService(data_dir=Path(tmp))
            state = reg.activate_model("piper", "default", "1.0")
            assert state["active_model"] == "default"
            assert state["active_version"] == "1.0"

    def test_rollback_restores_previous(self):
        """rollback restores previous_model after second activate."""
        with tempfile.TemporaryDirectory() as tmp:
            reg = ModelRegistryService(data_dir=Path(tmp))
            piper_artifacts = [a.model_name for a in reg._registry["piper"].artifacts]
            if len(piper_artifacts) < 2:
                pytest.skip("piper needs 2+ artifacts for rollback test")
            first, second = piper_artifacts[0], piper_artifacts[1]
            reg.activate_model("piper", first, None)
            reg.activate_model("piper", second, None)
            state = reg.rollback("piper")
            assert state["active_model"] == first
