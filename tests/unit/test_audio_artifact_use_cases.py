"""
Unit tests for audio artifact use-case helpers (Milestone 4).

Verifies create_audio_artifact_from_wav_array and create_audio_artifact_from_file
produce registered artifacts resolvable via AudioRegistry.get_path.
"""

from __future__ import annotations

from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np
import pytest

from backend.services.audio_artifacts.store import AudioArtifactStore
from backend.services.audio_artifacts.use_cases import (
    create_audio_artifact_from_file,
    create_audio_artifact_from_wav_array,
)

# Minimal valid WAV bytes for store tests
MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def test_create_audio_artifact_from_wav_array_calls_store(tmp_path: Path) -> None:
    """create_audio_artifact_from_wav_array converts array to bytes and calls store.store_from_bytes."""
    mock_store = MagicMock(spec=AudioArtifactStore)
    mock_store.store_from_bytes.return_value = ("aid-123", str(tmp_path / "aid-123.wav"), {})

    with patch(
        "backend.services.audio_artifacts.use_cases.get_audio_artifact_store",
        return_value=mock_store,
    ):
        audio = np.zeros(16000, dtype=np.float32)  # 1 sec at 16kHz
        aid, path, meta = create_audio_artifact_from_wav_array(
            audio, 16000, created_by="test"
        )

    assert aid == "aid-123"
    mock_store.store_from_bytes.assert_called_once()
    call_args, call_kw = mock_store.store_from_bytes.call_args
    assert len(call_args) >= 1
    assert isinstance(call_args[0], bytes)
    assert call_kw["model_used"] == "test"
    assert call_kw["write_provenance"] is True


def test_create_audio_artifact_from_wav_array_with_audio_id(tmp_path: Path) -> None:
    """create_audio_artifact_from_wav_array passes audio_id when provided."""
    mock_store = MagicMock(spec=AudioArtifactStore)
    mock_store.store_from_bytes.return_value = ("custom-id", str(tmp_path / "custom-id.wav"), {})

    with patch(
        "backend.services.audio_artifacts.use_cases.get_audio_artifact_store",
        return_value=mock_store,
    ):
        audio = np.zeros(8000, dtype=np.float32)
        aid, _, _ = create_audio_artifact_from_wav_array(
            audio, 8000, created_by="effects", audio_id="custom-id"
        )

    assert aid == "custom-id"
    call_kw = mock_store.store_from_bytes.call_args[1]
    assert call_kw["audio_id"] == "custom-id"


def test_create_audio_artifact_from_file_calls_store(tmp_path: Path) -> None:
    """create_audio_artifact_from_file calls store.store_from_file with correct params."""
    src = tmp_path / "source.wav"
    src.write_bytes(MINIMAL_WAV)

    mock_store = MagicMock(spec=AudioArtifactStore)
    mock_store.store_from_file.return_value = ("aid-456", str(tmp_path / "cached.wav"), {})

    with patch(
        "backend.services.audio_artifacts.use_cases.get_audio_artifact_store",
        return_value=mock_store,
    ):
        aid, path, meta = create_audio_artifact_from_file(
            src, created_by="rvc", delete_source=False
        )

    assert aid == "aid-456"
    mock_store.store_from_file.assert_called_once()
    call_args = mock_store.store_from_file.call_args
    assert str(call_args[0][0]) == str(src) or call_args[0][0] == src
    call_kw = call_args[1]
    assert call_kw["model_used"] == "rvc"
    assert call_kw["write_provenance"] is True


def test_create_audio_artifact_from_file_delete_source(tmp_path: Path) -> None:
    """create_audio_artifact_from_file with delete_source=True removes source after success."""
    src = tmp_path / "temp_output.wav"
    src.write_bytes(MINIMAL_WAV)
    assert src.exists()

    mock_store = MagicMock(spec=AudioArtifactStore)
    mock_store.store_from_file.return_value = ("aid-789", str(tmp_path / "cached.wav"), {})

    with patch(
        "backend.services.audio_artifacts.use_cases.get_audio_artifact_store",
        return_value=mock_store,
    ):
        create_audio_artifact_from_file(src, created_by="style_transfer", delete_source=True)

    assert not src.exists()


def test_create_audio_artifact_from_file_with_audio_id(tmp_path: Path) -> None:
    """create_audio_artifact_from_file passes audio_id when provided."""
    src = tmp_path / "source.wav"
    src.write_bytes(MINIMAL_WAV)

    mock_store = MagicMock(spec=AudioArtifactStore)
    mock_store.store_from_file.return_value = ("batch-abc", str(tmp_path / "cached.wav"), {})

    with patch(
        "backend.services.audio_artifacts.use_cases.get_audio_artifact_store",
        return_value=mock_store,
    ):
        aid, _, _ = create_audio_artifact_from_file(
            src, created_by="batch", audio_id="batch-abc", delete_source=False
        )

    assert aid == "batch-abc"
    call_kw = mock_store.store_from_file.call_args[1]
    assert call_kw["audio_id"] == "batch-abc"
