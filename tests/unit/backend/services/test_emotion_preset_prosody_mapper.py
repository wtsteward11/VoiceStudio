"""Unit tests for emotion_preset_prosody_mapper (GAP-050)."""

from __future__ import annotations

import pytest

from backend.services.emotion_preset_prosody_mapper import (
    is_canonical_preset,
    resolve_emotion_prosody,
)


def test_canonical_neutral_identity() -> None:
    r = resolve_emotion_prosody(
        primary_emotion="Neutral",
        primary_intensity=100.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    assert r.pitch == pytest.approx(1.0)
    assert r.rate == pytest.approx(1.0)
    assert r.volume == pytest.approx(1.0)
    assert r.mapping_source == "canonical_preset"


def test_canonical_warm_intensity_scales() -> None:
    full = resolve_emotion_prosody(
        primary_emotion="warm",
        primary_intensity=100.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    half = resolve_emotion_prosody(
        primary_emotion="warm",
        primary_intensity=50.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    assert full.pitch > 1.0
    assert half.pitch < full.pitch
    assert half.pitch > 1.0


def test_canonical_calm() -> None:
    r = resolve_emotion_prosody(
        primary_emotion="Calm",
        primary_intensity=100.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    assert r.pitch < 1.0
    assert r.rate < 1.0
    assert r.mapping_source == "canonical_preset"


def test_energetic_preset() -> None:
    r = resolve_emotion_prosody(
        primary_emotion="energetic",
        primary_intensity=100.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    assert r.pitch > 1.0
    assert r.rate > 1.0


def test_legacy_happy_has_formant_skip() -> None:
    r = resolve_emotion_prosody(
        primary_emotion="happy",
        primary_intensity=100.0,
        secondary_emotion=None,
        secondary_intensity=0.0,
    )
    assert r.mapping_source == "legacy_emotion"
    assert any(s.get("operation") == "formant_shift" for s in r.skipped_operations)


def test_blended_primary_secondary() -> None:
    r = resolve_emotion_prosody(
        primary_emotion="warm",
        primary_intensity=80.0,
        secondary_emotion="calm",
        secondary_intensity=40.0,
    )
    assert "blended" in r.mapping_source


def test_is_canonical_preset() -> None:
    assert is_canonical_preset("Warm") is True
    assert is_canonical_preset("happy") is False
