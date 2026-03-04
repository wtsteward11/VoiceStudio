"""
Thread-safety tests for AudioRegistryDB.

Proves no sqlite3.ProgrammingError under multi-thread access.
"""

from __future__ import annotations

import concurrent.futures
from pathlib import Path

import pytest

from backend.services.audio_artifacts.registry_db import AudioRegistryDB
from backend.services.audio_artifacts.store import AudioArtifactStore

# Minimal valid WAV header for tests (same as test_audio_artifacts)
MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def test_concurrent_register_and_resolve(tmp_path: Path) -> None:
    """Multiple threads register and resolve; no thread-safety errors."""
    artifacts_root = tmp_path / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)
    store = AudioArtifactStore(artifacts_root=artifacts_root)
    db_path = tmp_path / "registry.db"
    registry = AudioRegistryDB(db_path=db_path, store_factory=lambda: store)

    audio_ids = [f"audio_{i}" for i in range(20)]

    def register_one(aid: str) -> None:
        registry.create_from_bytes(
            MINIMAL_WAV, ext="wav", audio_id=aid, created_by="thread_test"
        )

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        futures = [ex.submit(register_one, aid) for aid in audio_ids]
        concurrent.futures.wait(futures)
        for f in futures:
            f.result()

    def resolve_one(aid: str) -> Path:
        return registry.resolve_path(aid)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        futures = [ex.submit(resolve_one, aid) for aid in audio_ids]
        results = [f.result() for f in futures]

    assert len(results) == 20
    for i, path in enumerate(results):
        assert path.exists()
        assert path.suffix.lower() == ".wav"


def test_concurrent_mixed_register_and_resolve(tmp_path: Path) -> None:
    """Each thread registers then resolves its own id; no ProgrammingError."""
    artifacts_root = tmp_path / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)
    store = AudioArtifactStore(artifacts_root=artifacts_root)
    db_path = tmp_path / "registry.db"
    registry = AudioRegistryDB(db_path=db_path, store_factory=lambda: store)

    def register_and_resolve(i: int) -> Path:
        aid = f"mixed_{i}"
        registry.create_from_bytes(
            MINIMAL_WAV, ext="wav", audio_id=aid, created_by="mixed_test"
        )
        return registry.resolve_path(aid)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        futures = [ex.submit(register_and_resolve, i) for i in range(20)]
        results = [f.result() for f in futures]

    assert len(results) == 20
    for path in results:
        assert path.exists()
