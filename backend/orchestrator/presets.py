"""
Strategy Presets Service — Phase X-A

Manages built-in and user-saved orchestration presets.
Built-in presets are loaded from JSON files in backend/orchestrator/presets/.
User presets are stored in the VoiceStudio data directory.
"""

from __future__ import annotations

import json
import logging
from pathlib import Path

from .schemas import (
    StrategyPreset,
)

logger = logging.getLogger(__name__)

_PRESETS_DIR = Path(__file__).parent / "presets"
_USER_PRESETS_DIR = Path.home() / ".voicestudio" / "presets"


class PresetsService:
    """Load, list, save, and delete orchestration strategy presets."""

    def __init__(self) -> None:
        self._builtin: dict[str, StrategyPreset] = {}
        self._user: dict[str, StrategyPreset] = {}
        self._load_builtin()
        self._load_user()

    def _load_builtin(self) -> None:
        if not _PRESETS_DIR.exists():
            return
        for path in sorted(_PRESETS_DIR.glob("*.json")):
            try:
                raw = json.loads(path.read_text(encoding="utf-8"))
                preset = StrategyPreset(**raw, is_builtin=True)
                self._builtin[preset.preset_id] = preset
            except Exception:
                logger.warning("Failed to load preset %s", path.name)

    def _load_user(self) -> None:
        if not _USER_PRESETS_DIR.exists():
            return
        for path in sorted(_USER_PRESETS_DIR.glob("*.json")):
            try:
                raw = json.loads(path.read_text(encoding="utf-8"))
                preset = StrategyPreset(**raw, is_builtin=False)
                self._user[preset.preset_id] = preset
            except Exception:
                logger.warning("Failed to load user preset %s", path.name)

    def list_all(self) -> list[StrategyPreset]:
        combined: dict[str, StrategyPreset] = {}
        combined.update(self._builtin)
        combined.update(self._user)
        return list(combined.values())

    def get(self, preset_id: str) -> StrategyPreset | None:
        return self._user.get(preset_id) or self._builtin.get(preset_id)

    def save_user_preset(self, preset: StrategyPreset) -> StrategyPreset:
        preset.is_builtin = False
        _USER_PRESETS_DIR.mkdir(parents=True, exist_ok=True)
        path = _USER_PRESETS_DIR / f"{preset.preset_id}.json"
        path.write_text(
            preset.model_dump_json(indent=2, exclude_none=True), encoding="utf-8"
        )
        self._user[preset.preset_id] = preset
        return preset

    def delete_user_preset(self, preset_id: str) -> bool:
        if preset_id in self._builtin:
            return False
        if preset_id not in self._user:
            return False
        path = _USER_PRESETS_DIR / f"{preset_id}.json"
        if path.exists():
            path.unlink()
        del self._user[preset_id]
        return True


_presets_service: PresetsService | None = None


def get_presets_service() -> PresetsService:
    global _presets_service
    if _presets_service is None:
        _presets_service = PresetsService()
    return _presets_service
