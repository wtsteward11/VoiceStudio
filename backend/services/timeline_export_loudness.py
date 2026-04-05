"""LUFS preset resolution and application for canonical timeline export (GAP-041)."""

from __future__ import annotations

from typing import Final

import numpy as np

__all__ = [
    "ALLOWED_LUFS_PRESET_IDS",
    "ExportLoudnessError",
    "LufsNormalizationUnavailableError",
    "apply_timeline_export_loudness",
    "normalize_lufs_for_export",
    "resolve_export_lufs_preset",
]

ALLOWED_LUFS_PRESET_IDS: Final[frozenset[str]] = frozenset(
    {
        "podcast_stereo",
        "podcast_mono",
        "broadcast",
        "streaming",
        "neutral",
    }
)

# Target integrated LUFS per preset (professional obvious set; see lane doc).
_PRESET_TARGET_LUFS: Final[dict[str, float]] = {
    "podcast_stereo": -16.0,
    "podcast_mono": -19.0,
    "broadcast": -23.0,
    "streaming": -14.0,
}


class ExportLoudnessError(ValueError):
    """Invalid export loudness / preset (422)."""


class LufsNormalizationUnavailableError(Exception):
    """Normalization requested but pyloudnorm / LUFS path unavailable (503)."""


def resolve_export_lufs_preset(preset_id: str) -> tuple[float | None, bool]:
    """Return (target_lufs, normalize_enabled)."""
    pid = preset_id.strip().lower()
    if pid not in ALLOWED_LUFS_PRESET_IDS:
        raise ExportLoudnessError(f"Unknown lufs_preset '{preset_id}'")
    if pid == "neutral":
        return None, False
    return _PRESET_TARGET_LUFS[pid], True


def normalize_lufs_for_export(
    audio: np.ndarray, sample_rate: int, target_lufs: float
) -> np.ndarray:
    """Route-boundary wrapper patchable in tests."""
    from backend.audio.audio_utils import normalize_lufs

    return normalize_lufs(audio, sample_rate, target_lufs=target_lufs)


def apply_timeline_export_loudness(
    audio: np.ndarray, sample_rate: int, preset_id: str | None
) -> np.ndarray:
    """Apply frozen LUFS preset after mixdown / effect bake, or pass through for neutral."""
    default = "podcast_stereo"
    pid = (preset_id or default).strip()
    target_lufs, enabled = resolve_export_lufs_preset(pid)
    if not enabled:
        return audio
    try:
        return normalize_lufs_for_export(audio, sample_rate, target_lufs)
    except ImportError as e:
        raise LufsNormalizationUnavailableError(
            "LUFS normalization requires pyloudnorm (install pyloudnorm==0.1.1)"
        ) from e
