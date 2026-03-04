"""
Unit tests for provenance and usage postconditions (Milestone 5).

Verifies BEST_EFFORT vs STRICT policy behavior and usage recording.
"""

from __future__ import annotations

from pathlib import Path
from unittest.mock import patch

import pytest

# Minimal valid WAV for duration
MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def test_provenance_adapter_calls_canonical(tmp_path: Path) -> None:
    """provenance.write_provenance delegates to write_provenance_sidecar."""
    from backend.services.audio_artifacts.provenance import write_provenance

    wav_path = tmp_path / "test.wav"
    wav_path.write_bytes(MINIMAL_WAV)
    write_provenance(
        wav_path,
        audio_id="test-id",
        created_by="test",
        metadata={},
    )
    sidecar = wav_path.with_suffix(wav_path.suffix + ".provenance.json")
    assert sidecar.exists()
    content = sidecar.read_text()
    assert "model_used" in content
    assert "test" in content


def test_usage_adapter_records_when_duration_provided(tmp_path: Path) -> None:
    """usage.record_usage calls record_synthesis_minutes when duration > 0."""
    stats_path = tmp_path / "usage_stats.json"
    stats_path.write_text('{"synthesis_minutes": 0.0, "exports_completed": 0, "models_downloaded": 0, "gpu_hours_used": 0.0, "last_updated": null}')

    with patch("backend.services.usage_stats._stats_path", return_value=stats_path):
        from backend.services.audio_artifacts.usage import record_usage
        from backend.services.usage_stats import get_usage_stats

        before = get_usage_stats().get("synthesis_minutes", 0.0)
        record_usage(60.0, created_by="test", kind="audio")
        after = get_usage_stats().get("synthesis_minutes", 0.0)
    assert after >= before + 1.0


def test_usage_adapter_skips_when_duration_none(tmp_path: Path) -> None:
    """usage.record_usage does nothing when duration_sec is None."""
    stats_path = tmp_path / "usage_stats.json"
    stats_path.write_text('{"synthesis_minutes": 5.0, "exports_completed": 0, "models_downloaded": 0, "gpu_hours_used": 0.0, "last_updated": null}')

    with patch("backend.services.usage_stats._stats_path", return_value=stats_path):
        from backend.services.audio_artifacts.usage import record_usage
        from backend.services.usage_stats import get_usage_stats

        before = get_usage_stats().get("synthesis_minutes", 0.0)
        record_usage(None, created_by="test", kind="audio")
        after = get_usage_stats().get("synthesis_minutes", 0.0)
    assert after == before


def test_best_effort_provenance_failure_artifact_still_created(
    tmp_path: Path, monkeypatch
) -> None:
    """BEST_EFFORT: When provenance fails, artifact creation still succeeds."""
    data_dir = tmp_path / "data"
    temp_dir = tmp_path / "temp"
    data_dir.mkdir(parents=True)
    temp_dir.mkdir(parents=True)

    from backend.config.path_config import get_path as _get_path

    def _patched_get_path(path_type: str):
        if path_type.lower() == "data":
            return data_dir
        if path_type.lower() == "temp":
            return temp_dir
        return _get_path(path_type)

    from backend.services.audio_artifacts.store import AudioArtifactStore
    from backend.services.audio_registry_service import reset_registry_for_testing
    from backend.services.provenance_policy import ProvenancePolicy

    reset_registry_for_testing()

    with (
        patch("backend.services.provenance_policy.POLICY", ProvenancePolicy.BEST_EFFORT),
        patch("backend.services.artifact_provenance.POLICY", ProvenancePolicy.BEST_EFFORT),
        patch(
            "backend.services.security_service.write_provenance_sidecar",
            side_effect=RuntimeError("Provenance failed"),
        ),
        patch("backend.config.path_config.get_path", _patched_get_path),
        patch("backend.services.audio_artifacts.store.get_path", _patched_get_path),
        patch("backend.services.audio_artifacts.registry_db.get_path", _patched_get_path),
        patch(
            "backend.services.audio_registry_service.get_path",
            _patched_get_path,
        ),
    ):
        store = AudioArtifactStore(artifacts_root=tmp_path / "artifacts")
        aid, path, _ = store.store_from_bytes(
            MINIMAL_WAV,
            model_used="test",
            write_provenance=True,
        )

    assert aid
    assert path
    assert Path(path).exists()


def test_strict_provenance_failure_rolls_back(tmp_path: Path, monkeypatch) -> None:
    """STRICT: When provenance fails, artifact is rolled back and error raised."""
    data_dir = tmp_path / "data"
    temp_dir = tmp_path / "temp"
    data_dir.mkdir(parents=True)
    temp_dir.mkdir(parents=True)

    from backend.config.path_config import get_path as _get_path

    def _patched_get_path(path_type: str):
        if path_type.lower() == "data":
            return data_dir
        if path_type.lower() == "temp":
            return temp_dir
        return _get_path(path_type)

    from backend.services.audio_artifacts.store import AudioArtifactStore
    from backend.services.audio_registry_service import (
        get_registry,
        reset_registry_for_testing,
    )
    from backend.services.provenance_policy import ProvenancePolicy

    reset_registry_for_testing()

    with (
        patch("backend.services.provenance_policy.POLICY", ProvenancePolicy.STRICT),
        patch("backend.services.artifact_provenance.POLICY", ProvenancePolicy.STRICT),
        patch(
            "backend.services.security_service.write_provenance_sidecar",
            side_effect=RuntimeError("Provenance failed"),
        ),
        patch("backend.config.path_config.get_path", _patched_get_path),
        patch("backend.services.audio_artifacts.store.get_path", _patched_get_path),
        patch("backend.services.audio_artifacts.registry_db.get_path", _patched_get_path),
        patch(
            "backend.services.audio_registry_service.get_path",
            _patched_get_path,
        ),
    ):
        store = AudioArtifactStore(artifacts_root=tmp_path / "artifacts")

        with pytest.raises(RuntimeError, match="Provenance failed"):
            store.store_from_bytes(
                MINIMAL_WAV,
                model_used="test",
                write_provenance=True,
            )

    # Registry should not contain the artifact (rollback)
    entries = get_registry().list_entries_sorted_by_age()
    assert not entries, f"Rollback should have removed registry entry, found: {entries}"
    # Artifact file should not exist (rollback deleted it)
    artifact_wavs = list((tmp_path / "artifacts").rglob("*.wav"))
    assert not artifact_wavs, (
        f"Rollback should have removed artifact files, found: {artifact_wavs}"
    )
