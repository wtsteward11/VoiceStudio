"""
Artifact Reference Counter for VoiceStudio (Phase 13.3.1-13.3.3)

Tracks how many projects/clips reference each audio artifact.
When the reference count drops to zero, the artifact is eligible
for cleanup.
"""

from __future__ import annotations

import json
import logging
import os
import threading
from pathlib import Path

logger = logging.getLogger(__name__)


class ArtifactRefCounter:
    """
    Reference counter for audio artifacts.

    Tracks which projects and clips reference each artifact.
    Artifacts with zero references are eligible for cleanup.
    """

    def __init__(self, data_dir: str | None = None):
        self._data_dir = Path(
            data_dir
            or os.getenv("VOICESTUDIO_CACHE_PATH", "")
            or Path.home() / ".voicestudio" / "cache"
        )
        self._data_dir.mkdir(parents=True, exist_ok=True)
        self._refs: dict[str, set[str]] = {}  # artifact_id -> set of referrer IDs
        self._lock = threading.RLock()
        self._load()

    def increment(self, artifact_id: str, referrer_id: str) -> int:
        """
        Increment reference count for an artifact.

        Args:
            artifact_id: The audio artifact ID.
            referrer_id: The ID of the project/clip referencing it.

        Returns:
            New reference count.
        """
        with self._lock:
            if artifact_id not in self._refs:
                self._refs[artifact_id] = set()
            self._refs[artifact_id].add(referrer_id)
            count = len(self._refs[artifact_id])
            self._save()
        return count

    def decrement(self, artifact_id: str, referrer_id: str) -> int:
        """
        Decrement reference count for an artifact.

        Args:
            artifact_id: The audio artifact ID.
            referrer_id: The ID to remove.

        Returns:
            New reference count.
        """
        with self._lock:
            if artifact_id in self._refs:
                self._refs[artifact_id].discard(referrer_id)
                count = len(self._refs[artifact_id])
                if count == 0:
                    del self._refs[artifact_id]
            else:
                count = 0
            self._save()
        return count

    def get_count(self, artifact_id: str) -> int:
        """Get the current reference count."""
        with self._lock:
            return len(self._refs.get(artifact_id, set()))

    def get_referrers(self, artifact_id: str) -> list[str]:
        """Get list of referrer IDs for an artifact."""
        with self._lock:
            return list(self._refs.get(artifact_id, set()))

    def get_zero_ref_artifacts(self) -> list[str]:
        """Get artifacts with zero references (cleanup candidates)."""
        with self._lock:
            # Return artifacts that were tracked but now have empty referrer sets
            return [k for k, v in self._refs.items() if len(v) == 0]

    def get_references(self, artifact_id: str) -> list[str]:
        """Alias for get_referrers for compatibility."""
        return self.get_referrers(artifact_id)

    def clear_artifact(self, artifact_id: str) -> None:
        """Clear all references for an artifact."""
        with self._lock:
            if artifact_id in self._refs:
                del self._refs[artifact_id]
                self._save()

    def get_all_tracked(self) -> dict[str, int]:
        """Get all tracked artifacts and their counts."""
        with self._lock:
            return {k: len(v) for k, v in self._refs.items()}

    def cleanup_zero_refs(self) -> list[str]:
        """Remove and return artifacts with zero references."""
        with self._lock:
            zero_refs = [k for k, v in self._refs.items() if len(v) == 0]
            for artifact_id in zero_refs:
                del self._refs[artifact_id]
            if zero_refs:
                self._save()
        return zero_refs

    def _load(self) -> None:
        """Load reference data from disk."""
        ref_file = self._data_dir / "artifact_refs.json"
        if not ref_file.exists():
            return
        try:
            with open(ref_file, encoding="utf-8") as f:
                data = json.load(f)
            self._refs = {k: set(v) for k, v in data.items()}
            logger.info(f"Loaded artifact refs: {len(self._refs)} artifacts tracked")
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning(f"Failed to load artifact refs: {exc}")

    def _save(self) -> None:
        """Save reference data to disk atomically."""
        ref_file = self._data_dir / "artifact_refs.json"
        tmp_file = ref_file.with_suffix(".tmp")
        try:
            data = {k: list(v) for k, v in self._refs.items()}
            with open(tmp_file, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            os.replace(str(tmp_file), str(ref_file))
        except OSError as exc:
            logger.warning(f"Failed to save artifact refs: {exc}")


# Singleton
_counter: ArtifactRefCounter | None = None


def get_ref_counter() -> ArtifactRefCounter:
    """Get the global reference counter singleton."""
    global _counter
    if _counter is None:
        _counter = ArtifactRefCounter()
    return _counter
