"""
Unit tests for canonical audio registry persistence (M9).

Verifies registry DB persists across restart (new instance loads from disk).
"""

from __future__ import annotations

from pathlib import Path

from backend.services.audio_artifacts.registry_db import AudioRegistryDB
from backend.services.audio_artifacts.store import AudioArtifactStore

MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def test_registry_persists_across_restart(tmp_path: Path) -> None:
    """Registry DB persists across restart (new instance loads from disk)."""
    db_path = tmp_path / "audio_registry.db"
    artifacts_root = tmp_path / "artifacts"
    artifacts_root.mkdir(parents=True, exist_ok=True)
    store = AudioArtifactStore(artifacts_root=artifacts_root)

    # First instance: register file
    registry1 = AudioRegistryDB(db_path=db_path, store_factory=lambda: store)
    source = tmp_path / "source.wav"
    source.write_bytes(MINIMAL_WAV)
    artifact = registry1.create_from_path(source, created_by="test")
    audio_id = artifact.audio_id
    assert Path(artifact.path).exists()

    # Second instance (simulate restart): load from same db, resolve
    registry2 = AudioRegistryDB(db_path=db_path, store_factory=lambda: store)
    resolved = registry2.resolve_path(audio_id)
    assert resolved.exists()
    assert resolved.read_bytes() == MINIMAL_WAV
