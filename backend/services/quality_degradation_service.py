"""
Quality degradation detection service.

Detects quality degradation in voice profiles by analyzing quality metrics over time.
Service layer must not depend on API layer.
"""

from __future__ import annotations

import logging
from datetime import datetime, timedelta
from typing import Any

logger = logging.getLogger(__name__)


class QualityDegradationAlert:
    """Represents a quality degradation alert."""

    def __init__(
        self,
        severity: str,
        degradation_percentage: float,
        metric_name: str,
        current_value: float,
        baseline_value: float,
        time_window_days: int,
        recommendation: str,
        confidence: float = 0.0,
    ):
        self.severity = severity
        self.degradation_percentage = degradation_percentage
        self.metric_name = metric_name
        self.current_value = current_value
        self.baseline_value = baseline_value
        self.time_window_days = time_window_days
        self.recommendation = recommendation
        self.confidence = confidence

    def to_dict(self) -> dict[str, Any]:
        """Convert alert to dictionary."""
        return {
            "severity": self.severity,
            "degradation_percentage": self.degradation_percentage,
            "metric_name": self.metric_name,
            "current_value": self.current_value,
            "baseline_value": self.baseline_value,
            "time_window_days": self.time_window_days,
            "recommendation": self.recommendation,
            "confidence": self.confidence,
        }


class QualityBaseline:
    """Represents quality baseline for a voice profile."""

    def __init__(
        self,
        profile_id: str,
        baseline_metrics: dict[str, float],
        baseline_quality_score: float,
        sample_count: int,
        time_period_days: int,
        calculated_at: str,
    ):
        self.profile_id = profile_id
        self.baseline_metrics = baseline_metrics
        self.baseline_quality_score = baseline_quality_score
        self.sample_count = sample_count
        self.time_period_days = time_period_days
        self.calculated_at = calculated_at

    def to_dict(self) -> dict[str, Any]:
        """Convert baseline to dictionary."""
        return {
            "profile_id": self.profile_id,
            "baseline_metrics": self.baseline_metrics,
            "baseline_quality_score": self.baseline_quality_score,
            "sample_count": self.sample_count,
            "time_period_days": self.time_period_days,
            "calculated_at": self.calculated_at,
        }


def calculate_quality_baseline(
    quality_history: list[dict[str, Any]],
    time_period_days: int = 30,
    min_samples: int = 5,
) -> QualityBaseline | None:
    """
    Calculate quality baseline from quality history.

    Args:
        quality_history: List of quality history entries (dicts with timestamp, metrics, quality_score)
        time_period_days: Time period to use for baseline (default: 30 days)
        min_samples: Minimum number of samples required (default: 5)

    Returns:
        QualityBaseline object or None if insufficient data
    """
    if not quality_history:
        logger.warning("No quality history provided for baseline calculation")
        return None

    cutoff_date = (datetime.utcnow() - timedelta(days=time_period_days)).isoformat() + "Z"
    recent_entries = [e for e in quality_history if e.get("timestamp", "") >= cutoff_date]

    if len(recent_entries) < min_samples:
        logger.warning("Insufficient samples for baseline: %s < %s", len(recent_entries), min_samples)
        return None

    metrics_to_track = ["mos_score", "similarity", "naturalness", "snr_db"]
    baseline_metrics: dict[str, float] = {}
    quality_scores: list[float] = []

    for metric in metrics_to_track:
        values = []
        for entry in recent_entries:
            value = None
            if metric in entry.get("metrics", {}):
                val = entry["metrics"][metric]
                if isinstance(val, (int, float)):
                    value = float(val)
            if value is not None:
                values.append(value)
        if values:
            baseline_metrics[metric] = sum(values) / len(values)

    for entry in recent_entries:
        if "quality_score" in entry:
            quality_scores.append(float(entry["quality_score"]))

    baseline_quality_score = sum(quality_scores) / len(quality_scores) if quality_scores else 0.0
    profile_id = recent_entries[0].get("profile_id", "unknown")

    return QualityBaseline(
        profile_id=profile_id,
        baseline_metrics=baseline_metrics,
        baseline_quality_score=baseline_quality_score,
        sample_count=len(recent_entries),
        time_period_days=time_period_days,
        calculated_at=datetime.utcnow().isoformat() + "Z",
    )


def detect_quality_degradation(
    quality_history: list[dict[str, Any]],
    baseline: QualityBaseline | None = None,
    time_window_days: int = 7,
    degradation_threshold_percent: float = 10.0,
    critical_threshold_percent: float = 25.0,
) -> list[QualityDegradationAlert]:
    """
    Detect quality degradation in voice profile.

    Args:
        quality_history: List of quality history entries
        baseline: Quality baseline (if None, will be calculated)
        time_window_days: Time window to analyze (default: 7 days)
        degradation_threshold_percent: Warning threshold percentage drop (default: 10%)
        critical_threshold_percent: Critical threshold percentage drop (default: 25%)

    Returns:
        List of QualityDegradationAlert objects
    """
    if not quality_history:
        return []

    if baseline is None:
        baseline = calculate_quality_baseline(quality_history, time_period_days=30)
        if baseline is None:
            logger.warning("Could not calculate baseline for degradation detection")
            return []

    cutoff_date = (datetime.utcnow() - timedelta(days=time_window_days)).isoformat() + "Z"
    recent_entries = [e for e in quality_history if e.get("timestamp", "") >= cutoff_date]

    if not recent_entries:
        return []

    metrics_to_track = ["mos_score", "similarity", "naturalness", "snr_db"]
    current_metrics: dict[str, float] = {}
    current_quality_scores: list[float] = []

    for metric in metrics_to_track:
        values = []
        for entry in recent_entries:
            value = None
            if metric in entry.get("metrics", {}):
                val = entry["metrics"][metric]
                if isinstance(val, (int, float)):
                    value = float(val)
            if value is not None:
                values.append(value)
        if values:
            current_metrics[metric] = sum(values) / len(values)

    for entry in recent_entries:
        if "quality_score" in entry:
            current_quality_scores.append(float(entry["quality_score"]))

    current_quality_score = (
        sum(current_quality_scores) / len(current_quality_scores) if current_quality_scores else 0.0
    )

    alerts: list[QualityDegradationAlert] = []

    if baseline.baseline_quality_score > 0:
        degradation = (
            (baseline.baseline_quality_score - current_quality_score)
            / baseline.baseline_quality_score
            * 100.0
        )
        if degradation >= critical_threshold_percent:
            alerts.append(
                QualityDegradationAlert(
                    severity="critical",
                    degradation_percentage=degradation,
                    metric_name="quality_score",
                    current_value=current_quality_score,
                    baseline_value=baseline.baseline_quality_score,
                    time_window_days=time_window_days,
                    recommendation=(
                        f"Quality score has dropped {degradation:.1f}% from baseline. "
                        "Consider retraining the voice model or using a different engine."
                    ),
                    confidence=min(1.0, degradation / critical_threshold_percent),
                )
            )
        elif degradation >= degradation_threshold_percent:
            alerts.append(
                QualityDegradationAlert(
                    severity="warning",
                    degradation_percentage=degradation,
                    metric_name="quality_score",
                    current_value=current_quality_score,
                    baseline_value=baseline.baseline_quality_score,
                    time_window_days=time_window_days,
                    recommendation=(
                        f"Quality score has dropped {degradation:.1f}% from baseline. "
                        "Monitor quality trends and consider optimization."
                    ),
                    confidence=min(1.0, degradation / degradation_threshold_percent),
                )
            )

    for metric in metrics_to_track:
        if metric not in baseline.baseline_metrics or metric not in current_metrics:
            continue
        baseline_value = baseline.baseline_metrics[metric]
        current_value = current_metrics[metric]
        if baseline_value <= 0:
            continue
        degradation = (baseline_value - current_value) / baseline_value * 100.0
        recommendation = _get_metric_recommendation(metric, degradation)
        if degradation >= critical_threshold_percent:
            alerts.append(
                QualityDegradationAlert(
                    severity="critical",
                    degradation_percentage=degradation,
                    metric_name=metric,
                    current_value=current_value,
                    baseline_value=baseline_value,
                    time_window_days=time_window_days,
                    recommendation=recommendation,
                    confidence=min(1.0, degradation / critical_threshold_percent),
                )
            )
        elif degradation >= degradation_threshold_percent:
            alerts.append(
                QualityDegradationAlert(
                    severity="warning",
                    degradation_percentage=degradation,
                    metric_name=metric,
                    current_value=current_value,
                    baseline_value=baseline_value,
                    time_window_days=time_window_days,
                    recommendation=recommendation,
                    confidence=min(1.0, degradation / degradation_threshold_percent),
                )
            )

    return alerts


def _get_metric_recommendation(metric_name: str, degradation: float) -> str:
    recommendations = {
        "mos_score": (
            f"MOS score has dropped {degradation:.1f}%. "
            "Consider using a higher quality engine or enabling quality enhancement."
        ),
        "similarity": (
            f"Voice similarity has dropped {degradation:.1f}%. "
            "Consider retraining the voice model with new reference audio."
        ),
        "naturalness": (
            f"Naturalness has dropped {degradation:.1f}%. "
            "Consider adjusting synthesis parameters or using a different engine."
        ),
        "snr_db": (
            f"Signal-to-noise ratio has dropped {degradation:.1f}%. "
            "Check audio processing pipeline and enable noise reduction."
        ),
    }
    return recommendations.get(
        metric_name,
        f"Metric {metric_name} has degraded by {degradation:.1f}%. "
        "Review synthesis settings and engine configuration.",
    )
