"""
Deterministic emotion preset / emotion label → prosody factors (GAP-050).

Maps canonical presets (Neutral, Warm, Energetic, Calm) and legacy emotion labels
to pitch/rate/volume multipliers for ProsodyAuthorityService.apply_transform.
Formant-like legacy parameters are not executed by the authority; callers receive
skip entries for diagnostics.
"""

from __future__ import annotations

from dataclasses import dataclass, field

# Canonical presets: case-insensitive keys → (pitch, rate, volume) at 100% intensity.
_CANONICAL_PRESETS: dict[str, tuple[float, float, float]] = {
    "neutral": (1.0, 1.0, 1.0),
    "warm": (1.04, 0.96, 1.05),
    "energetic": (1.12, 1.10, 1.06),
    "calm": (0.96, 0.92, 0.98),
}

# Legacy table: librosa-era pitch_shift (semitones/12), tempo multiplier, formant shift (ignored by authority).
_LEGACY_EMOTION_DELTAS: dict[str, tuple[float, float, float]] = {
    "happy": (0.3, 1.1, 0.1),
    "sad": (-0.4, 0.9, -0.1),
    "angry": (0.2, 1.15, 0.15),
    "excited": (0.5, 1.2, 0.2),
    "calm": (-0.2, 0.95, -0.05),
    "fearful": (0.4, 1.1, 0.1),
    "surprised": (0.6, 1.05, 0.15),
    "disgusted": (-0.3, 0.9, -0.1),
    "neutral": (0.0, 1.0, 0.0),
    "warm": (0.08, 0.97, 0.04),
    "energetic": (0.45, 1.15, 0.08),
}

_PITCH_MIN = 0.82
_PITCH_MAX = 1.28
_RATE_MIN = 0.82
_RATE_MAX = 1.28
_VOL_MIN = 0.75
_VOL_MAX = 1.2


def _norm_key(label: str) -> str:
    return label.strip().lower()


def is_canonical_preset(label: str) -> bool:
    return _norm_key(label) in _CANONICAL_PRESETS


@dataclass
class EmotionProsodyFactors:
    """Resolved factors for apply_transform."""

    pitch: float
    rate: float
    volume: float
    mapping_source: str
    skipped_operations: list[dict[str, str]] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def _clamp_factors(pitch: float, rate: float, volume: float) -> tuple[float, float, float]:
    return (
        max(_PITCH_MIN, min(_PITCH_MAX, pitch)),
        max(_RATE_MIN, min(_RATE_MAX, rate)),
        max(_VOL_MIN, min(_VOL_MAX, volume)),
    )


def _factors_from_canonical(preset_key: str, intensity_pct: float) -> tuple[float, float, float, list[str]]:
    key = _norm_key(preset_key)
    base_p, base_r, base_v = _CANONICAL_PRESETS[key]
    w = max(0.0, min(100.0, intensity_pct)) / 100.0
    pitch = 1.0 + (base_p - 1.0) * w
    rate = 1.0 + (base_r - 1.0) * w
    volume = 1.0 + (base_v - 1.0) * w
    warnings: list[str] = []
    cp, cr, cv = _clamp_factors(pitch, rate, volume)
    if (cp, cr, cv) != (pitch, rate, volume):
        warnings.append("emotion_factors_clamped_to_safe_bounds")
    return cp, cr, cv, warnings


def _factors_from_legacy(
    emotion_key: str, intensity_pct: float
) -> tuple[float, float, float, list[dict[str, str]], list[str]]:
    key = _norm_key(emotion_key)
    pitch_shift, tempo, formant = _LEGACY_EMOTION_DELTAS.get(
        key, _LEGACY_EMOTION_DELTAS["neutral"]
    )
    w = max(0.0, min(100.0, intensity_pct)) / 100.0
    # pitch_shift is additive to 1.0 in multiplier space (matches GAP-023 semitone mapping).
    pitch = 1.0 + pitch_shift * w
    rate = 1.0 + (tempo - 1.0) * w
    volume = 1.0
    skipped: list[dict[str, str]] = []
    warnings: list[str] = []
    if abs(formant) > 1e-9 and abs(w) > 1e-9:
        skipped.append(
            {
                "operation": "formant_shift",
                "reason": "not_supported_by_prosody_authority",
            }
        )
        warnings.append("legacy_formant_shift_not_applied")
    cp, cr, cv = _clamp_factors(pitch, rate, volume)
    if (cp, cr, cv) != (pitch, rate, volume):
        warnings.append("emotion_factors_clamped_to_safe_bounds")
    return cp, cr, cv, skipped, warnings


def resolve_emotion_prosody(
    *,
    primary_emotion: str,
    primary_intensity: float,
    secondary_emotion: str | None,
    secondary_intensity: float,
) -> EmotionProsodyFactors:
    """
    Resolve blended emotion selection to pitch/rate/volume.

    Canonical presets take precedence when the label matches a canonical key
    (case-insensitive). Otherwise legacy emotion deltas are used.
    """
    pri_key = _norm_key(primary_emotion)
    sec_key = _norm_key(secondary_emotion) if secondary_emotion else None

    def resolve_one(label: str, intensity: float) -> tuple[float, float, float, str, list[dict[str, str]], list[str]]:
        if _norm_key(label) in _CANONICAL_PRESETS:
            p, r, v, warn = _factors_from_canonical(label, intensity)
            return p, r, v, "canonical_preset", [], warn
        p, r, v, skipped, warn = _factors_from_legacy(label, intensity)
        return p, r, v, "legacy_emotion", skipped, warn

    p1, r1, v1, src1, skip1, w1 = resolve_one(primary_emotion, primary_intensity)

    if not sec_key or secondary_intensity <= 0:
        skipped = list(skip1)
        warnings = list(w1)
        return EmotionProsodyFactors(
            pitch=p1,
            rate=r1,
            volume=v1,
            mapping_source=src1,
            skipped_operations=skipped,
            warnings=warnings,
        )

    p2, r2, v2, src2, skip2, w2 = resolve_one(secondary_emotion or "", secondary_intensity)
    w_pri = max(0.0, min(100.0, primary_intensity)) / 100.0
    w_sec = max(0.0, min(100.0, secondary_intensity)) / 100.0
    total = w_pri + w_sec
    if total <= 0:
        return EmotionProsodyFactors(
            pitch=1.0,
            rate=1.0,
            volume=1.0,
            mapping_source="none",
            skipped_operations=skip1 + skip2,
            warnings=w1 + w2,
        )

    def blend(a: float, b: float) -> float:
        # Blend deltas from identity, matching prior route weighted average of shifts.
        d1 = a - 1.0
        d2 = b - 1.0
        return 1.0 + (d1 * w_pri + d2 * w_sec) / total

    pitch = blend(p1, p2)
    rate = blend(r1, r2)
    volume = blend(v1, v2)
    pitch, rate, volume = _clamp_factors(pitch, rate, volume)

    mapping_source = "blended"
    if src1 == src2 == "canonical_preset":
        mapping_source = "canonical_blended"
    elif src1 == src2 == "legacy_emotion":
        mapping_source = "legacy_blended"
    elif src1 == "canonical_preset" or src2 == "canonical_preset":
        mapping_source = "canonical_legacy_blended"

    warnings = list(w1) + list(w2)
    skipped = skip1 + skip2
    return EmotionProsodyFactors(
        pitch=pitch,
        rate=rate,
        volume=volume,
        mapping_source=mapping_source,
        skipped_operations=skipped,
        warnings=warnings,
    )
