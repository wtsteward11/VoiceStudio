"""
Quality trends computation for voice profiles.

Computes time-series trends, statistics (avg, min, max, linear regression slope),
and best/worst entries. Owns all trends logic; routes delegate here.
"""

from __future__ import annotations

from datetime import datetime, timedelta, timezone
from typing import Any


def get_quality_trends(profile_id: str, time_range: str = "30d") -> dict[str, Any]:
    """
    Compute quality trends for a voice profile.

    Args:
        profile_id: Voice profile ID
        time_range: Time range for trends (7d, 30d, 90d, 1y, all)

    Returns:
        Dict with profile_id, time_range, trends, statistics, best_entry,
        worst_entry. Compatible with QualityTrendsResponse.
    """
    from backend.services.quality_history_service import get_entries

    entries = get_entries(profile_id)

    if not entries:
        return {
            "profile_id": profile_id,
            "time_range": time_range,
            "trends": {},
            "statistics": {},
            "best_entry": None,
            "worst_entry": None,
        }

    days = {"7d": 7, "30d": 30, "90d": 90, "1y": 365, "all": 999999}.get(
        time_range, 30
    )
    cutoff_date = (
        datetime.now(timezone.utc) - timedelta(days=days)
    ).isoformat().replace("+00:00", "Z")
    filtered_entries = [
        e for e in entries
        if getattr(e, "timestamp", "") >= cutoff_date or time_range == "all"
    ]

    if not filtered_entries:
        return {
            "profile_id": profile_id,
            "time_range": time_range,
            "trends": {},
            "statistics": {},
            "best_entry": None,
            "worst_entry": None,
        }

    metrics_to_track = ["mos_score", "similarity", "naturalness", "quality_score"]
    trends: dict[str, list[dict[str, Any]]] = {}
    statistics: dict[str, dict[str, float]] = {}

    for metric in metrics_to_track:
        metric_values: list[dict[str, Any]] = []
        for entry in filtered_entries:
            value = None
            if metric == "quality_score":
                value = getattr(entry, "quality_score", None)
            else:
                m = getattr(entry, "metrics", None)
                if isinstance(m, dict) and metric in m:
                    val = m[metric]
                    if isinstance(val, (int, float)):
                        value = float(val)

            if value is not None:
                ts = getattr(entry, "timestamp", "")
                metric_values.append({"timestamp": ts, "value": value})

        metric_values.sort(key=lambda x: str(x["timestamp"]))
        trends[metric] = metric_values

        if metric_values:
            values = [float(v["value"]) for v in metric_values]
            avg = sum(values) / len(values)
            min_val = min(values)
            max_val = max(values)

            trend = 0.0
            if len(values) > 1:
                x_mean = (len(values) - 1) / 2.0
                y_mean = avg
                numerator = sum(
                    (i - x_mean) * (values[i] - y_mean)
                    for i in range(len(values))
                )
                denominator = sum(
                    (i - x_mean) ** 2 for i in range(len(values))
                )
                if denominator != 0:
                    trend = numerator / denominator

            statistics[metric] = {
                "avg": avg,
                "min": min_val,
                "max": max_val,
                "trend": trend,
            }

    best_entry = max(
        filtered_entries,
        key=lambda e: getattr(e, "quality_score", 0.0),
        default=None,
    )
    worst_entry = min(
        filtered_entries,
        key=lambda e: getattr(e, "quality_score", 0.0),
        default=None,
    )

    return {
        "profile_id": profile_id,
        "time_range": time_range,
        "trends": trends,
        "statistics": statistics,
        "best_entry": best_entry,
        "worst_entry": worst_entry,
    }
