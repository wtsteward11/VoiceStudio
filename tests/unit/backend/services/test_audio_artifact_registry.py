import os
from pathlib import Path

from backend.services.AudioArtifactRegistry import get_audio_registry, reset_audio_registry
from backend.services.ContentAddressedAudioCache import reset_audio_cache


def test_audio_artifact_registry_persists_across_restart(tmp_path: Path, monkeypatch):
    cache_dir = tmp_path / "cache"
    registry_path = tmp_path / "cache" / "audio_registry.json"
    cache_dir.mkdir(parents=True, exist_ok=True)

    # Route cache + registry to temp paths
    monkeypatch.setenv("VOICESTUDIO_CACHE_DIR", str(cache_dir))
    monkeypatch.setenv("VOICESTUDIO_AUDIO_REGISTRY_PATH", str(registry_path))

    # Reset singletons for isolation
    reset_audio_cache()
    reset_audio_registry()

    # Create a tiny WAV-like file (content doesn't matter for hashing/copy)
    source = tmp_path / "source.wav"
    source.write_bytes(b"RIFFxxxxWAVEfmt " + b"\x00" * 64)

    registry = get_audio_registry()
    audio_id = "audio_test_1"
    cached_path, hash_value = registry.register_file(audio_id, str(source))

    assert hash_value
    assert os.path.exists(cached_path)

    # Simulate restart: new registry instance should load from disk
    reset_audio_registry()
    registry2 = get_audio_registry()

    assert registry2.get(audio_id) == cached_path
