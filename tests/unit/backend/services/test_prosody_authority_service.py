"""Unit tests for ProsodyAuthorityService (GAP-023)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest

project_root = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(project_root))

pytest.importorskip("numpy")

from backend.services.prosody_authority_service import (
    ProsodyAuthorityError,
    apply_transform,
    prosody_control_request_factors,
)


def test_identity_returns_none_action():
    audio = np.zeros(80, dtype=np.float32)
    result = apply_transform(audio, 16000, pitch=1.0, rate=1.0, volume=1.0)
    assert result.diagnostics["action"] == "none"
    assert result.diagnostics["applied_operations"] == []
    np.testing.assert_array_equal(result.audio, audio)


def test_volume_only_applies_gain():
    audio = np.ones(40, dtype=np.float32) * 0.5
    result = apply_transform(audio, 16000, pitch=1.0, rate=1.0, volume=0.5)
    assert "gain" in result.diagnostics["applied_operations"]
    assert result.diagnostics["action"] == "applied"
    assert result.audio.shape == audio.shape


def test_pitch_requested_import_error_is_503():
    audio = np.zeros(40, dtype=np.float32)
    with patch(
        "backend.services.prosody_authority_service.pitch_shift_audio",
        side_effect=ImportError("no librosa"),
    ):
        with pytest.raises(ProsodyAuthorityError) as ei:
            apply_transform(audio, 16000, pitch=1.2, rate=1.0, volume=1.0)
    assert ei.value.status_code == 503


def test_pitch_runtime_error_is_500():
    audio = np.zeros(40, dtype=np.float32)
    with patch(
        "backend.services.prosody_authority_service.pitch_shift_audio",
        side_effect=RuntimeError("dsp boom"),
    ):
        with pytest.raises(ProsodyAuthorityError) as ei:
            apply_transform(audio, 16000, pitch=1.2, rate=1.0, volume=1.0)
    assert ei.value.status_code == 500


def test_prosody_control_factors_mean_and_rhythm():
    p, r = prosody_control_request_factors([1.0, 1.2], {"rate": 1.1})
    assert abs(p - 1.1) < 1e-9
    assert r == 1.1
    p2, r2 = prosody_control_request_factors(None, {"tempo": 0.9})
    assert p2 == 1.0
    assert r2 == 0.9
