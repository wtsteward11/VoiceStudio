"""Unit tests for timeline export LUFS preset resolver (GAP-041)."""

from __future__ import annotations

import numpy as np
import pytest

from backend.services.timeline_export_loudness import (
    ExportLoudnessError,
    apply_timeline_export_loudness,
    normalize_lufs_for_export,
    resolve_export_lufs_preset,
)


def test_resolve_neutral_disables_normalization() -> None:
    target, enabled = resolve_export_lufs_preset("neutral")
    assert target is None
    assert enabled is False


def test_resolve_podcast_stereo_target() -> None:
    target, enabled = resolve_export_lufs_preset("podcast_stereo")
    assert target == -16.0
    assert enabled is True


def test_resolve_unknown_preset_raises() -> None:
    with pytest.raises(ExportLoudnessError):
        resolve_export_lufs_preset("unknown_preset_x")


def test_apply_neutral_does_not_call_normalize(monkeypatch: pytest.MonkeyPatch) -> None:
    called: list[object] = []

    def _stub(*_args: object, **_kwargs: object) -> np.ndarray:
        called.append(True)
        raise AssertionError("normalize must not run for neutral")

    monkeypatch.setattr(
        "backend.services.timeline_export_loudness.normalize_lufs_for_export",
        _stub,
    )
    audio = np.array([0.1, -0.1], dtype=np.float32)
    out = apply_timeline_export_loudness(audio, 48000, "neutral")
    assert called == []
    assert np.array_equal(out, audio)


def test_apply_broadcast_invokes_normalize_wrapper(monkeypatch: pytest.MonkeyPatch) -> None:
    captured: list[float] = []

    def _fake_norm(audio: np.ndarray, _sample_rate: int, target_lufs: float) -> np.ndarray:
        captured.append(target_lufs)
        return audio * 2.0

    monkeypatch.setattr(
        "backend.services.timeline_export_loudness.normalize_lufs_for_export",
        _fake_norm,
    )
    audio = np.ones(8, dtype=np.float32) * 0.01
    out = apply_timeline_export_loudness(audio, 48000, "broadcast")
    assert captured == [-23.0]
    assert out[0] == pytest.approx(0.02)


def test_normalize_lufs_for_export_delegates_to_audio_utils(monkeypatch: pytest.MonkeyPatch) -> None:
    """Smoke: default wrapper calls backend.audio.audio_utils.normalize_lufs (patched)."""

    def _fake(audio: np.ndarray, _sr: int, target_lufs: float = -23.0, **_kwargs: float) -> np.ndarray:
        return audio + target_lufs * 0.0

    monkeypatch.setattr("backend.audio.audio_utils.normalize_lufs", _fake)
    a = np.ones(4, dtype=np.float32)
    out = normalize_lufs_for_export(a, 48000, -16.0)
    assert out.shape == a.shape
