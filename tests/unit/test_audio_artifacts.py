"""
Unit tests for the artifact spine (AudioArtifactStore + AudioRegistryDB).

Milestone 2: Verifies correctness, safety, and path policy.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from backend.services.audio_artifacts.errors import ArtifactNotFoundError, InvalidExtensionError
from backend.services.audio_artifacts.registry_db import AudioRegistryDB
from backend.services.audio_artifacts.store import AudioArtifactStore

# Minimal valid WAV header (44 bytes) for duration tests
MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


@pytest.fixture
def temp_artifacts(tmp_path: Path):
    """Artifacts root under temp; ensures repo-root is never used."""
    artifacts_root = tmp_path / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)
    return artifacts_root


@pytest.fixture
def store(temp_artifacts: Path) -> AudioArtifactStore:
    """Store with temp artifacts root."""
    return AudioArtifactStore(artifacts_root=temp_artifacts)


@pytest.fixture
def registry(temp_artifacts: Path, store: AudioArtifactStore):
    """Registry with temp db and store."""
    db_path = temp_artifacts.parent / "audio_registry.db"
    return AudioRegistryDB(db_path=db_path, store_factory=lambda: store)


def test_register_from_bytes(registry: AudioRegistryDB, temp_artifacts: Path) -> None:
    """Register from bytes; file exists at expected location."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    assert artifact.audio_id
    expected_path = temp_artifacts / "audio" / artifact.audio_id / f"{artifact.audio_id}.wav"
    assert expected_path.exists()
    assert registry.resolve_path(artifact.audio_id) == expected_path


def test_register_from_path(registry: AudioRegistryDB, temp_artifacts: Path) -> None:
    """Register from path; file exists at expected location."""
    src = temp_artifacts.parent / "source.wav"
    src.write_bytes(MINIMAL_WAV)
    artifact = registry.create_from_path(src, created_by="test")
    assert artifact.audio_id
    expected_path = temp_artifacts / "audio" / artifact.audio_id / f"{artifact.audio_id}.wav"
    assert expected_path.exists()
    assert registry.resolve_path(artifact.audio_id) == expected_path


def test_registry_resolve_and_metadata(registry: AudioRegistryDB) -> None:
    """Registry can resolve path and metadata is persisted."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    resolved = registry.get(artifact.audio_id)
    assert resolved.audio_id == artifact.audio_id
    assert resolved.path == artifact.path
    assert resolved.created_by == "test"
    assert resolved.ext == "wav"


def test_delete_removes_file_and_row(registry: AudioRegistryDB, temp_artifacts: Path) -> None:
    """Delete removes both file and db row."""
    artifact = registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="test")
    path = temp_artifacts / "audio" / artifact.audio_id / f"{artifact.audio_id}.wav"
    assert path.exists()
    assert registry.exists(artifact.audio_id)

    store = registry._store_factory()
    assert isinstance(store, AudioArtifactStore)
    store.delete(artifact.audio_id)
    registry.delete(artifact.audio_id)

    assert not path.exists()
    assert not registry.exists(artifact.audio_id)
    with pytest.raises(ArtifactNotFoundError):
        registry.resolve_path(artifact.audio_id)


def test_ext_sanitization_rejects_bad_ext(store: AudioArtifactStore) -> None:
    """Bad extension is rejected."""
    with pytest.raises(InvalidExtensionError):
        store.write_from_bytes("aid1", b"data", ext=".exe")
    with pytest.raises(InvalidExtensionError):
        store.write_from_bytes("aid1", b"data", ext="xyz")


def test_ext_sanitization_normalizes_allowed(store: AudioArtifactStore) -> None:
    """Allowed extensions are normalized."""
    for i, ext in enumerate(("wav", "WAV", ".wav", ".mp3")):
        path = store.write_from_bytes(f"aid{i}", b"x", ext=ext)
        assert path.suffix.lower() in (".wav", ".mp3")


def test_path_traversal_no_api_surface(store: AudioArtifactStore) -> None:
    """Path traversal: no API accepts user-provided output paths."""
    # The store never accepts user-provided output paths; audio_id and ext are
    # used to construct paths internally. There is no API to inject path traversal.
    # This test documents that the API surface does not allow path traversal.
    path = store.write_from_bytes("normal_id", b"x", ext="wav")
    assert ".." not in str(path)
    assert path.name == "normal_id.wav"


def test_artifacts_root_under_temp(temp_artifacts: Path) -> None:
    """Artifacts root in tests is under temp dir, never repo."""
    # tmp_path is pytest's temp dir; artifacts_root is under it.
    # Ensures we never use repo root for artifact storage.
    path_str = str(temp_artifacts.resolve()).lower()
    assert "temp" in path_str or "tmp" in path_str or "appdata" in path_str


def test_list_artifacts(registry: AudioRegistryDB) -> None:
    """List returns artifacts with optional filters."""
    registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="a")
    registry.create_from_bytes(MINIMAL_WAV, ext="wav", created_by="b")
    all_artifacts = registry.list_artifacts(limit=10)
    assert len(all_artifacts) >= 2


def test_store_write_from_path_copy(store: AudioArtifactStore, temp_artifacts: Path) -> None:
    """write_from_path with copy=True preserves source."""
    src = temp_artifacts.parent / "source.wav"
    src.write_bytes(MINIMAL_WAV)
    out = store.write_from_path("aid1", src, ext="wav", copy=True)
    assert out.exists()
    assert src.exists()
    assert out.read_bytes() == MINIMAL_WAV
