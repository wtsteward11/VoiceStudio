"""
Disk-backed audio artifact registry for VoiceStudio backend.

Provides a durable mapping:
  audio_id -> cached_file_path (+ created_at)

This is used to make voice workflow artifacts recoverable across backend restarts.
"""

from __future__ import annotations

import json
import logging
import os
import threading
import time
from dataclasses import dataclass
from pathlib import Path

from backend.config.path_config import get_path
from backend.services.ContentAddressedAudioCache import get_audio_cache

logger = logging.getLogger(__name__)

ENV_AUDIO_REGISTRY_PATH = "VOICESTUDIO_AUDIO_REGISTRY_PATH"
DEFAULT_REGISTRY_FILENAME = "audio_registry.json"


@dataclass(frozen=True)
class AudioArtifactRecord:
    path: str
    created_at_epoch: float
    project_id: str | None = None
    source: str | None = None
    hash_value: str | None = None


class AudioArtifactRegistry:
    """
    Durable mapping from audio_id to cached audio file path.

    - Writes registry atomically via os.replace.
    - Stores audio into ContentAddressedAudioCache and records the cached path.
    """

    def __init__(self, registry_path: str | None = None):
        self._lock = threading.RLock()
        self._registry_path = self._resolve_registry_path(registry_path)
        self._records: dict[str, AudioArtifactRecord] = {}
        self._load()

    @staticmethod
    def _resolve_registry_path(registry_path: str | None) -> Path:
        if registry_path:
            return Path(registry_path)

        env_path = os.getenv(ENV_AUDIO_REGISTRY_PATH)
        if env_path:
            return Path(env_path)

        # Use centralized cache path
        return get_path("cache") / DEFAULT_REGISTRY_FILENAME

    @property
    def registry_path(self) -> Path:
        return self._registry_path

    def _atomic_write_json(self, path: Path, payload: dict) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = path.with_suffix(path.suffix + ".tmp")
        data = json.dumps(payload, indent=2, ensure_ascii=False)
        tmp_path.write_text(data, encoding="utf-8")
        os.replace(tmp_path, path)

    def _load(self) -> None:
        with self._lock:
            path = self._registry_path
            if not path.exists():
                self._records = {}
                return

            try:
                raw = json.loads(path.read_text(encoding="utf-8"))
            except Exception as e:
                logger.warning(f"Failed to read audio registry {path}: {e}")
                self._records = {}
                return

            records: dict[str, AudioArtifactRecord] = {}
            if isinstance(raw, dict):
                for audio_id, entry in raw.items():
                    try:
                        if not isinstance(entry, dict):
                            continue
                        file_path = str(entry.get("path", ""))
                        created = float(entry.get("created_at_epoch", 0.0))
                        project_id = entry.get("project_id")
                        source = entry.get("source")
                        hash_value = entry.get("hash_value")
                        if not file_path:
                            continue
                        if not os.path.exists(file_path):
                            continue
                        records[str(audio_id)] = AudioArtifactRecord(
                            path=file_path,
                            created_at_epoch=created or time.time(),
                            project_id=str(project_id) if project_id else None,
                            source=str(source) if source else None,
                            hash_value=str(hash_value) if hash_value else None,
                        )
                    except Exception:
                        continue

            self._records = records

    def _persist(self) -> None:
        with self._lock:
            payload = {}
            for audio_id, rec in self._records.items():
                entry = {
                    "path": rec.path,
                    "created_at_epoch": rec.created_at_epoch,
                }
                if rec.project_id:
                    entry["project_id"] = rec.project_id
                if rec.source:
                    entry["source"] = rec.source
                if rec.hash_value:
                    entry["hash_value"] = rec.hash_value
                payload[audio_id] = entry
            self._atomic_write_json(self._registry_path, payload)

    def to_dict(self) -> dict[str, str]:
        with self._lock:
            return {k: v.path for k, v in self._records.items()}

    def get(self, audio_id: str) -> str | None:
        with self._lock:
            rec = self._records.get(audio_id)
            return rec.path if rec else None

    def get_record(self, audio_id: str) -> AudioArtifactRecord | None:
        with self._lock:
            return self._records.get(audio_id)

    def remove(self, audio_id: str) -> None:
        with self._lock:
            if audio_id in self._records:
                del self._records[audio_id]
                self._persist()

    def register_file(
        self,
        audio_id: str,
        file_path: str,
        *,
        project_id: str | None = None,
        source: str | None = None,
    ) -> tuple[str, str]:
        """
        Register an audio file under audio_id.

        The file is stored in the content-addressed cache; the cached path is persisted.

        Returns:
            (cached_path, hash_value)
        """
        source_path = Path(file_path)
        if not source_path.exists():
            raise FileNotFoundError(f"Audio file not found: {file_path}")

        cache = get_audio_cache()
        cached_path, hash_value = cache.get_or_store(source_path)

        with self._lock:
            self._records[audio_id] = AudioArtifactRecord(
                path=str(cached_path),
                created_at_epoch=time.time(),
                project_id=project_id,
                source=source,
                hash_value=hash_value,
            )
            self._persist()

        return str(cached_path), hash_value


_service_instance: AudioArtifactRegistry | None = None


def get_audio_registry(registry_path: str | None = None) -> AudioArtifactRegistry:
    global _service_instance
    if _service_instance is None:
        _service_instance = AudioArtifactRegistry(registry_path=registry_path)
    return _service_instance


def reset_audio_registry() -> None:
    global _service_instance
    _service_instance = None
