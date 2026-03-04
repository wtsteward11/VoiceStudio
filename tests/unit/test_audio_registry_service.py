"""
Unit tests for audio_registry_service (M9).

Verifies resolve_audio_path, remove_audio_id, purge_old_entries, ensure_cached.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from backend.services.audio_artifacts.registry_db import AudioRegistryDB
from backend.services.audio_artifacts.store import AudioArtifactStore
from backend.services.audio_registry_service import (
    ensure_cached,
    purge_old_entries,
    remove_audio_id,
    reset_registry_for_testing,
    resolve_audio_path,
)

MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


@pytest.fixture
def temp_artifacts(tmp_path: Path) -> Path:
    """Artifacts root under temp."""
    artifacts_root = tmp_path / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)
    return artifacts_root


@pytest.fixture
def store(temp_artifacts: Path) -> AudioArtifactStore:
    """Store with temp artifacts root."""
    return AudioArtifactStore(artifacts_root=temp_artifacts)


@pytest.fixture
def registry(temp_artifacts: Path, store: AudioArtifactStore) -> AudioRegistryDB:
    """Registry with temp db and store."""
    db_path = temp_artifacts.parent / "audio_registry.db"
    return AudioRegistryDB(db_path=db_path, store_factory=lambda: store)


@pytest.fixture(autouse=True)
def _isolate_registry(monkeypatch, registry: AudioRegistryDB):
    """Inject temp registry into service for test isolation."""
    import backend.services.audio_registry_service as svc

    monkeypatch.setattr(svc, "_registry_instance", registry)
    yield
    reset_registry_for_testing()


def test_resolve_audio_path_after_create(
    registry: AudioRegistryDB, temp_artifacts: Path
) -> None:
    """Create artifact via registry, resolve path via service."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    resolved = resolve_audio_path(artifact.audio_id)
    assert resolved is not None
    expected = temp_artifacts / "audio" / artifact.audio_id / f"{artifact.audio_id}.wav"
    assert Path(resolved).resolve() == expected.resolve()


def test_resolve_audio_path_not_found() -> None:
    """Resolve unknown audio_id returns None."""
    assert resolve_audio_path("nonexistent-id-12345") is None


def test_remove_audio_id_removes_registry_row(
    registry: AudioRegistryDB, temp_artifacts: Path
) -> None:
    """Remove via service; confirm registry row is gone."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    assert registry.exists(artifact.audio_id)
    remove_audio_id(artifact.audio_id)
    assert not registry.exists(artifact.audio_id)
    assert resolve_audio_path(artifact.audio_id) is None


def test_purge_old_entries_removes_registry_rows_only(
    registry: AudioRegistryDB, temp_artifacts: Path
) -> None:
    """Purge removes registry rows, not files."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    file_path = temp_artifacts / "audio" / artifact.audio_id / f"{artifact.audio_id}.wav"
    assert file_path.exists()
    removed = purge_old_entries(max_age_seconds=0, max_count=0)
    assert removed >= 1
    assert not registry.exists(artifact.audio_id)
    assert file_path.exists()


def test_ensure_cached_returns_path(tmp_path: Path) -> None:
    """ensure_cached returns path for existing file."""
    wav_file = tmp_path / "input.wav"
    wav_file.write_bytes(MINIMAL_WAV)
    cached = ensure_cached(wav_file)
    assert cached.exists()
    assert cached.read_bytes() == MINIMAL_WAV
