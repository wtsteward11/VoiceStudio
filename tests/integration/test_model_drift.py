"""
Integration tests for model drift detection — Phase 9 Sprint 2.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from backend.services.model_drift_detector import (
    ModelDriftDetector,
    _compute_psi,
    _values_to_bins,
    get_model_drift_detector,
)


def test_psi_no_drift() -> None:
    """PSI should be low when distributions are similar."""
    baseline = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] * 10
    current = [1.1, 2.1, 3.1, 4.1, 5.1, 6.1, 7.1, 8.1, 9.1, 10.1] * 10
    b_bins = _values_to_bins(baseline)
    c_bins = _values_to_bins(current)
    psi = _compute_psi(b_bins, c_bins)
    assert psi < 0.2


def test_psi_significant_drift() -> None:
    """PSI should be high when distributions differ significantly."""
    baseline = [1] * 50 + [10] * 50  # bimodal low
    current = [5] * 50 + [5] * 50  # unimodal middle
    b_bins = _values_to_bins(baseline)
    c_bins = _values_to_bins(current)
    psi = _compute_psi(b_bins, c_bins)
    assert psi > 0.1


def test_values_to_bins() -> None:
    """Binning should produce correct count."""
    values = [1.0, 2.0, 3.0, 4.0, 5.0]
    bins = _values_to_bins(values, num_bins=5)
    assert sum(bins) == 5
    assert len(bins) == 5


def test_drift_detector_baseline_and_status(tmp_path: Path) -> None:
    """Set baseline, report observations, get status."""
    detector = ModelDriftDetector(data_dir=tmp_path)
    detector.set_baseline("xtts", "latency_ms", [100.0, 120.0, 110.0, 130.0, 115.0] * 4)
    for _ in range(20):
        detector.report_observation("xtts", "latency_ms", 105.0)

    statuses = detector.get_status(engine_id="xtts")
    assert len(statuses) == 1
    assert statuses[0].engine_id == "xtts"
    assert statuses[0].has_baseline
    assert len(statuses[0].metrics) >= 1
    m = statuses[0].metrics[0]
    assert m.metric_name == "latency_ms"
    assert m.baseline_sample_count == 20
    assert m.current_sample_count == 20


def test_drift_detector_set_baseline_requires_values(tmp_path: Path) -> None:
    """Baseline with few values still stored but may not compute PSI."""
    detector = ModelDriftDetector(data_dir=tmp_path)
    detector.set_baseline("whisper", "text_length", [10.0, 20.0, 30.0])
    statuses = detector.get_status()
    assert len(statuses) == 0 or all(len(s.metrics) == 0 for s in statuses)


def test_get_model_drift_detector_singleton() -> None:
    """get_model_drift_detector returns same instance."""
    d1 = get_model_drift_detector()
    d2 = get_model_drift_detector()
    assert d1 is d2
