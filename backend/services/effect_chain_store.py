"""
Persistent Effect Chain Store for VoiceStudio (GAP-BE-001)

Migrates effect chains and presets from in-memory storage to durable
disk-backed JsonFileStore. Chains persist across restarts.
"""

from __future__ import annotations

import logging
from typing import Any

from .json_file_store import JsonFileStore

logger = logging.getLogger(__name__)


class EffectChainStore:
    """
    Disk-backed effect chain storage.

    Uses JsonFileStore for persistent storage with in-memory caching.
    Chains are organized by project_id for efficient lookup.

    Storage layout:
        data/stores/effect_chains/{chain_id}.json
    """

    def __init__(self, max_chains: int = 5000):
        self._store = JsonFileStore("effect_chains", max_items=max_chains)
        self._project_index: dict[str, list[str]] = {}
        self._rebuild_project_index()

    def _rebuild_project_index(self) -> None:
        """Rebuild the project-to-chain index from stored data."""
        self._project_index.clear()
        for chain in self._store.list():
            project_id = chain.get("project_id", "")
            if project_id:
                if project_id not in self._project_index:
                    self._project_index[project_id] = []
                chain_id = chain.get("id", "")
                if chain_id and chain_id not in self._project_index[project_id]:
                    self._project_index[project_id].append(chain_id)
        logger.info(
            f"EffectChainStore: Rebuilt index with {self._store.count()} chains "
            f"across {len(self._project_index)} projects"
        )

    def get(self, chain_id: str) -> dict[str, Any] | None:
        """Get an effect chain by ID."""
        return self._store.get(chain_id)

    def save(self, chain: dict[str, Any]) -> str:
        """
        Save an effect chain.

        Args:
            chain: Chain dictionary (must include 'id' and 'project_id').

        Returns:
            Chain ID.
        """
        chain_id = chain.get("id", "")
        if not chain_id:
            import uuid

            chain_id = f"chain-{uuid.uuid4().hex[:8]}"
            chain["id"] = chain_id

        project_id = chain.get("project_id", "")

        # Save to store
        self._store.put(chain_id, chain)

        # Update project index
        if project_id:
            if project_id not in self._project_index:
                self._project_index[project_id] = []
            if chain_id not in self._project_index[project_id]:
                self._project_index[project_id].append(chain_id)

        logger.debug(f"EffectChainStore: Saved chain {chain_id}")
        return chain_id

    def delete(self, chain_id: str) -> bool:
        """Delete an effect chain."""
        chain = self._store.get(chain_id)
        if not chain:
            return False

        project_id = chain.get("project_id", "")

        # Remove from store
        result = self._store.delete(chain_id)

        # Update project index
        if project_id and project_id in self._project_index:
            if chain_id in self._project_index[project_id]:
                self._project_index[project_id].remove(chain_id)
            if not self._project_index[project_id]:
                del self._project_index[project_id]

        logger.debug(f"EffectChainStore: Deleted chain {chain_id}")
        return result

    def list_by_project(self, project_id: str) -> list[dict[str, Any]]:
        """List all effect chains for a project."""
        chain_ids = self._project_index.get(project_id, [])
        chains = []
        for chain_id in chain_ids:
            chain = self._store.get(chain_id)
            if chain:
                chains.append(chain)
        return chains

    def list_all(self) -> list[dict[str, Any]]:
        """List all effect chains."""
        return self._store.list()

    def count(self) -> int:
        """Get total number of chains."""
        return self._store.count()

    def count_by_project(self, project_id: str) -> int:
        """Get number of chains for a project."""
        return len(self._project_index.get(project_id, []))

    def exists(self, chain_id: str) -> bool:
        """Check if a chain exists."""
        return self._store.exists(chain_id)


class EffectPresetStore:
    """
    Disk-backed effect preset storage.

    Uses JsonFileStore for persistent storage with in-memory caching.
    Presets are organized by effect_type for efficient lookup.

    Storage layout:
        data/stores/effect_presets/{preset_id}.json
    """

    def __init__(self, max_presets: int = 1000):
        self._store = JsonFileStore("effect_presets", max_items=max_presets)
        self._type_index: dict[str, list[str]] = {}
        self._rebuild_type_index()

    def _rebuild_type_index(self) -> None:
        """Rebuild the effect_type-to-preset index from stored data."""
        self._type_index.clear()
        for preset in self._store.list():
            effect_type = preset.get("effect_type", "")
            if effect_type:
                if effect_type not in self._type_index:
                    self._type_index[effect_type] = []
                preset_id = preset.get("id", "")
                if preset_id and preset_id not in self._type_index[effect_type]:
                    self._type_index[effect_type].append(preset_id)
        logger.info(
            f"EffectPresetStore: Rebuilt index with {self._store.count()} presets "
            f"across {len(self._type_index)} effect types"
        )

    def get(self, preset_id: str) -> dict[str, Any] | None:
        """Get an effect preset by ID."""
        return self._store.get(preset_id)

    def save(self, preset: dict[str, Any]) -> str:
        """
        Save an effect preset.

        Args:
            preset: Preset dictionary (must include 'id' and 'effect_type').

        Returns:
            Preset ID.
        """
        preset_id = preset.get("id", "")
        if not preset_id:
            import uuid

            preset_id = f"preset-{uuid.uuid4().hex[:8]}"
            preset["id"] = preset_id

        effect_type = preset.get("effect_type", "")

        # Save to store
        self._store.put(preset_id, preset)

        # Update type index
        if effect_type:
            if effect_type not in self._type_index:
                self._type_index[effect_type] = []
            if preset_id not in self._type_index[effect_type]:
                self._type_index[effect_type].append(preset_id)

        logger.debug(f"EffectPresetStore: Saved preset {preset_id}")
        return preset_id

    def delete(self, preset_id: str) -> bool:
        """Delete an effect preset."""
        preset = self._store.get(preset_id)
        if not preset:
            return False

        effect_type = preset.get("effect_type", "")

        # Remove from store
        result = self._store.delete(preset_id)

        # Update type index
        if effect_type and effect_type in self._type_index:
            if preset_id in self._type_index[effect_type]:
                self._type_index[effect_type].remove(preset_id)
            if not self._type_index[effect_type]:
                del self._type_index[effect_type]

        logger.debug(f"EffectPresetStore: Deleted preset {preset_id}")
        return result

    def list_by_type(self, effect_type: str) -> list[dict[str, Any]]:
        """List all presets for an effect type."""
        preset_ids = self._type_index.get(effect_type, [])
        presets = []
        for preset_id in preset_ids:
            preset = self._store.get(preset_id)
            if preset:
                presets.append(preset)
        return presets

    def list_all(self) -> list[dict[str, Any]]:
        """List all effect presets."""
        return self._store.list()

    def count(self) -> int:
        """Get total number of presets."""
        return self._store.count()

    def exists(self, preset_id: str) -> bool:
        """Check if a preset exists."""
        return self._store.exists(preset_id)


# Singletons
_chain_store: EffectChainStore | None = None
_preset_store: EffectPresetStore | None = None


def get_effect_chain_store() -> EffectChainStore:
    """Get the global effect chain store singleton."""
    global _chain_store
    if _chain_store is None:
        _chain_store = EffectChainStore()
    return _chain_store


def get_effect_preset_store() -> EffectPresetStore:
    """Get the global effect preset store singleton."""
    global _preset_store
    if _preset_store is None:
        _preset_store = EffectPresetStore()
    return _preset_store
