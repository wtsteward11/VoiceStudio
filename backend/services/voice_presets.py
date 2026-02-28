"""
Voice Preset System.

Task 4.1.4: Quick voice switching with presets.
Enables instant switching between configured voice profiles.
"""

from __future__ import annotations

import json
import logging
from collections.abc import Callable
from dataclasses import asdict, dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class PresetCategory(Enum):
    """Categories for voice presets."""

    USER_CREATED = "user_created"
    SYSTEM_DEFAULT = "system_default"
    COMMUNITY = "community"
    IMPORTED = "imported"


@dataclass
class VoicePresetSettings:
    """Settings for a voice preset."""

    # Voice conversion
    pitch_shift: int = 0
    formant_shift: float = 0.0

    # RVC settings
    rvc_model: str | None = None
    rvc_index: str | None = None
    rvc_index_rate: float = 0.75
    rvc_protect: float = 0.33
    rvc_f0_method: str = "rmvpe"

    # Processing
    sample_rate: int = 16000
    denoise: bool = False
    normalize: bool = True

    # Effects
    reverb: float = 0.0
    eq_low: float = 0.0
    eq_mid: float = 0.0
    eq_high: float = 0.0
    compression: float = 0.0


@dataclass
class VoicePreset:
    """A complete voice preset configuration."""

    id: str
    name: str
    description: str = ""
    category: PresetCategory = PresetCategory.USER_CREATED
    settings: VoicePresetSettings = field(default_factory=VoicePresetSettings)
    thumbnail: str | None = None
    created_at: str = field(default_factory=lambda: datetime.now().isoformat())
    updated_at: str = field(default_factory=lambda: datetime.now().isoformat())
    is_favorite: bool = False
    tags: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        data = asdict(self)
        data["category"] = self.category.value
        return data

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> VoicePreset:
        if "category" in data:
            data["category"] = PresetCategory(data["category"])
        if "settings" in data and isinstance(data["settings"], dict):
            data["settings"] = VoicePresetSettings(**data["settings"])
        return cls(**data)


class VoicePresetManager:
    """
    Manages voice presets for quick switching.

    Features:
    - Create, edit, delete presets
    - Quick loading with caching
    - Preset organization (favorites, categories)
    - Import/export
    """

    def __init__(
        self,
        presets_dir: str | None = None,
        cache_loaded_models: bool = True,
    ):
        self._presets_dir = Path(presets_dir or "data/voice_presets")
        self._cache_models = cache_loaded_models
        self._presets: dict[str, VoicePreset] = {}
        self._active_preset: str | None = None
        self._model_cache: dict[str, Any] = {}
        self._load_callbacks: list[Callable] = []

        # Ensure directory exists
        self._presets_dir.mkdir(parents=True, exist_ok=True)

    async def initialize(self) -> None:
        """Load all presets from disk."""
        logger.info(f"Initializing voice presets from {self._presets_dir}")

        # Load user presets
        for preset_file in self._presets_dir.glob("*.json"):
            try:
                with open(preset_file, encoding="utf-8") as f:
                    data = json.load(f)
                    preset = VoicePreset.from_dict(data)
                    self._presets[preset.id] = preset
            except Exception as e:
                logger.warning(f"Failed to load preset {preset_file}: {e}")

        # Create default presets if none exist
        if not self._presets:
            await self._create_default_presets()

        logger.info(f"Loaded {len(self._presets)} voice presets")

    async def _create_default_presets(self) -> None:
        """Create default system presets."""
        defaults = [
            VoicePreset(
                id="natural",
                name="Natural Voice",
                description="No voice conversion, natural output",
                category=PresetCategory.SYSTEM_DEFAULT,
                settings=VoicePresetSettings(
                    pitch_shift=0,
                    normalize=True,
                ),
            ),
            VoicePreset(
                id="higher_pitch",
                name="Higher Pitch",
                description="Voice raised by 4 semitones",
                category=PresetCategory.SYSTEM_DEFAULT,
                settings=VoicePresetSettings(
                    pitch_shift=4,
                    normalize=True,
                ),
            ),
            VoicePreset(
                id="lower_pitch",
                name="Lower Pitch",
                description="Voice lowered by 4 semitones",
                category=PresetCategory.SYSTEM_DEFAULT,
                settings=VoicePresetSettings(
                    pitch_shift=-4,
                    normalize=True,
                ),
            ),
            VoicePreset(
                id="broadcast",
                name="Broadcast Quality",
                description="Radio/podcast ready with compression",
                category=PresetCategory.SYSTEM_DEFAULT,
                settings=VoicePresetSettings(
                    normalize=True,
                    compression=0.5,
                    eq_low=-2,
                    eq_mid=1,
                    eq_high=0,
                ),
            ),
        ]

        for preset in defaults:
            await self.save_preset(preset)

    async def get_preset(self, preset_id: str) -> VoicePreset | None:
        """Get a preset by ID."""
        return self._presets.get(preset_id)

    async def get_all_presets(self) -> list[VoicePreset]:
        """Get all presets."""
        return list(self._presets.values())

    async def get_presets_by_category(
        self,
        category: PresetCategory,
    ) -> list[VoicePreset]:
        """Get presets filtered by category."""
        return [p for p in self._presets.values() if p.category == category]

    async def get_favorite_presets(self) -> list[VoicePreset]:
        """Get favorite presets."""
        return [p for p in self._presets.values() if p.is_favorite]

    async def save_preset(self, preset: VoicePreset) -> bool:
        """Save a preset to disk."""
        try:
            preset.updated_at = datetime.now().isoformat()

            preset_file = self._presets_dir / f"{preset.id}.json"
            with open(preset_file, "w", encoding="utf-8") as f:
                json.dump(preset.to_dict(), f, indent=2)

            self._presets[preset.id] = preset
            logger.info(f"Saved preset: {preset.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to save preset: {e}")
            return False

    async def delete_preset(self, preset_id: str) -> bool:
        """Delete a preset."""
        preset = self._presets.get(preset_id)
        if not preset:
            return False

        # Don't allow deleting system presets
        if preset.category == PresetCategory.SYSTEM_DEFAULT:
            logger.warning("Cannot delete system preset")
            return False

        try:
            preset_file = self._presets_dir / f"{preset_id}.json"
            if preset_file.exists():
                preset_file.unlink()

            del self._presets[preset_id]

            # Clear from cache
            if preset_id in self._model_cache:
                del self._model_cache[preset_id]

            logger.info(f"Deleted preset: {preset.name}")
            return True

        except Exception as e:
            logger.error(f"Failed to delete preset: {e}")
            return False

    async def duplicate_preset(
        self,
        preset_id: str,
        new_name: str,
    ) -> VoicePreset | None:
        """Duplicate an existing preset."""
        source = self._presets.get(preset_id)
        if not source:
            return None

        import uuid

        new_id = str(uuid.uuid4())[:8]

        new_preset = VoicePreset(
            id=new_id,
            name=new_name,
            description=f"Copy of {source.name}",
            category=PresetCategory.USER_CREATED,
            settings=VoicePresetSettings(**asdict(source.settings)),
            tags=source.tags.copy(),
        )

        if await self.save_preset(new_preset):
            return new_preset
        return None

    async def activate_preset(self, preset_id: str) -> bool:
        """
        Activate a preset for use.

        Loads associated models if needed.
        """
        preset = self._presets.get(preset_id)
        if not preset:
            return False

        logger.info(f"Activating preset: {preset.name}")

        # Load RVC model if specified
        if preset.settings.rvc_model and not await self._load_rvc_model(preset):
            logger.warning("Failed to load RVC model for preset")

        self._active_preset = preset_id

        # Notify callbacks
        for callback in self._load_callbacks:
            try:
                callback(preset)
            except Exception as e:
                logger.error(f"Preset callback error: {e}")

        return True

    async def _load_rvc_model(self, preset: VoicePreset) -> bool:
        """Load RVC model for preset."""
        model_path = preset.settings.rvc_model
        if not model_path:
            return True

        # Check cache
        if self._cache_models and model_path in self._model_cache:
            logger.debug("Using cached RVC model")
            return True

        try:
            # Load model (placeholder - actual loading depends on RVC engine)
            # In production: self._rvc_engine.load_model(model_path)

            if self._cache_models:
                self._model_cache[model_path] = True

            return True

        except Exception as e:
            logger.error(f"Failed to load RVC model: {e}")
            return False

    def on_preset_activated(self, callback: Callable[[VoicePreset], None]) -> None:
        """Register callback for preset activation."""
        self._load_callbacks.append(callback)

    @property
    def active_preset(self) -> VoicePreset | None:
        """Get currently active preset."""
        if self._active_preset:
            return self._presets.get(self._active_preset)
        return None

    async def export_preset(self, preset_id: str, output_path: str) -> bool:
        """Export a preset to a file."""
        preset = self._presets.get(preset_id)
        if not preset:
            return False

        try:
            with open(output_path, "w", encoding="utf-8") as f:
                json.dump(preset.to_dict(), f, indent=2)
            return True
        except Exception as e:
            logger.error(f"Failed to export preset: {e}")
            return False

    async def import_preset(self, file_path: str) -> VoicePreset | None:
        """Import a preset from a file."""
        try:
            with open(file_path, encoding="utf-8") as f:
                data = json.load(f)

            # Generate new ID to avoid conflicts
            import uuid

            data["id"] = str(uuid.uuid4())[:8]
            data["category"] = PresetCategory.IMPORTED.value

            preset = VoicePreset.from_dict(data)

            if await self.save_preset(preset):
                return preset
            return None

        except Exception as e:
            logger.error(f"Failed to import preset: {e}")
            return None

    async def search_presets(
        self,
        query: str,
        category: PresetCategory | None = None,
    ) -> list[VoicePreset]:
        """Search presets by name, description, or tags."""
        query_lower = query.lower()
        results = []

        for preset in self._presets.values():
            if category and preset.category != category:
                continue

            if (
                query_lower in preset.name.lower()
                or query_lower in preset.description.lower()
                or any(query_lower in tag.lower() for tag in preset.tags)
            ):
                results.append(preset)

        return results

    async def toggle_favorite(self, preset_id: str) -> bool:
        """Toggle favorite status of a preset."""
        preset = self._presets.get(preset_id)
        if not preset:
            return False

        preset.is_favorite = not preset.is_favorite
        return await self.save_preset(preset)
