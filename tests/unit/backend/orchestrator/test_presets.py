"""Tests for the presets service."""

from __future__ import annotations

import pytest

from backend.orchestrator.presets import PresetsService
from backend.orchestrator.schemas import (
    PresetCategory,
    StrategyPreset,
)


class TestPresetsService:
    def setup_method(self):
        self.service = PresetsService()

    def test_builtin_presets_loaded(self):
        presets = self.service.list_all()
        assert len(presets) >= 6

    def test_builtin_preset_ids(self):
        presets = self.service.list_all()
        ids = {p.preset_id for p in presets}
        assert "cinematic" in ids
        assert "audiobook" in ids
        assert "podcast" in ids
        assert "broadcast" in ids
        assert "game_character" in ids
        assert "conversational" in ids

    def test_get_existing_preset(self):
        preset = self.service.get("cinematic")
        assert preset is not None
        assert preset.name == "Cinematic Narration"

    def test_get_nonexistent_returns_none(self):
        assert self.service.get("nonexistent") is None

    def test_cannot_delete_builtin(self):
        assert self.service.delete_user_preset("cinematic") is False

    def test_preset_quality_policy(self):
        preset = self.service.get("cinematic")
        assert preset is not None
        assert preset.default_quality_policy.min_mos >= 4.0

    def test_preset_has_steps(self):
        preset = self.service.get("audiobook")
        assert preset is not None
        assert len(preset.default_chain.steps) >= 2
