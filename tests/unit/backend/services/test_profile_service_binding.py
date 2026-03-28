"""
Binding tests for create_profile_from_request (GOV-VOICESTUDIO-VOICE-CLONING-INTEGRITY-01).

Ensures reference audio is copied to canonical profile storage before persist.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from backend.project.management import profile_store as profile_store_mod
from backend.services.path_service import PathService
from backend.services.profile_service import (
    create_profile_from_request,
    resolve_reference_audio_path,
)
from backend.services.profile_storage_service import exists_reference_audio


@pytest.fixture
def isolated_profile_root(tmp_path, monkeypatch):
    """Single profiles root for ProfileStore and PathService (must match)."""
    root = tmp_path / "profiles"
    root.mkdir(parents=True, exist_ok=True)
    store = profile_store_mod.ProfileStore(base_dir=str(root))
    monkeypatch.setattr(profile_store_mod, "get_profile_store", lambda: store)
    monkeypatch.setattr(PathService, "get_profiles_dir", staticmethod(lambda: root))
    return root


def test_create_profile_with_audio_copies_reference_wav(isolated_profile_root, tmp_path):
    src = tmp_path / "source.wav"
    src.write_bytes(b"RIFFfake-wav-content-for-test-binding")

    result = create_profile_from_request(
        name="Bound Voice",
        language="en",
        reference_audio_source=src,
    )
    pid = result["id"]
    assert result.get("reference_audio_bound") is True
    assert result.get("reference_audio_url")

    dest = isolated_profile_root / pid / "reference_audio.wav"
    assert dest.is_file()
    assert dest.read_bytes() == src.read_bytes()


def test_create_profile_without_audio_is_metadata_only(isolated_profile_root):
    result = create_profile_from_request(name="Meta Only", language="en")
    pid = result["id"]
    assert result.get("reference_audio_bound") is False
    wav = isolated_profile_root / pid / "reference_audio.wav"
    assert not wav.exists()


def test_create_profile_with_nonexistent_audio_raises(isolated_profile_root):
    with pytest.raises(ValueError, match="reference_audio_source"):
        create_profile_from_request(
            name="Bad",
            language="en",
            reference_audio_source=isolated_profile_root / "nope.wav",
        )


def test_resolve_reference_audio_path_finds_bound_file(isolated_profile_root, tmp_path):
    src = tmp_path / "in.wav"
    src.write_bytes(b"data")
    result = create_profile_from_request(
        name="Resolve Test",
        language="en",
        reference_audio_source=src,
    )
    pid = result["id"]
    resolved = resolve_reference_audio_path(pid)
    assert isinstance(resolved, Path)
    assert resolved.exists()
    assert exists_reference_audio(pid)
