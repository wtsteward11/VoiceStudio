"""
Quality dashboard aggregation service (IDEA 49).

Aggregates quality history into dashboard data (overview, trends, distribution, alerts).
Service layer must not depend on API layer.
"""

from __future__ import annotations

from datetime import datetime, timedelta
from typing import Any


def get_dashboard_data(
    entries: list[Any],
    project_id: str | None = None,
    days: int = 30,
) -> dict[str, Any]:
    """
    Build quality dashboard data from entries.

    Args:
        entries: List of quality history entries (objects with .timestamp, .metrics, .project_id, .metadata)
        project_id: Optional project ID to filter by
        days: Number of days to include in trends

    Returns:
        Dashboard dict with overview, trends, distribution, alerts, insights
    """
    # Filter by date range
    cutoff_date = datetime.utcnow() - timedelta(days=days)
    recent_entries = [
        e
        for e in entries
        if datetime.fromisoformat(getattr(e, "timestamp", "").replace("Z", "+00:00")) >= cutoff_date
    ]

    # Filter by project if specified
    if project_id:
        filtered = []
        for entry in recent_entries:
            if getattr(entry, "project_id", None) == project_id:
                filtered.append(entry)
            elif hasattr(entry, "metadata") and isinstance(entry.metadata, dict):
                if entry.metadata.get("project_id") == project_id:
                    filtered.append(entry)
        recent_entries = filtered

    if not recent_entries:
        return {
            "overview": {
                "total_samples": 0,
                "average_mos": 0.0,
                "average_similarity": 0.0,
                "average_naturalness": 0.0,
            },
            "trends": {
                "mos_trend": [],
                "similarity_trend": [],
                "naturalness_trend": [],
            },
            "distribution": {
                "mos_distribution": {},
                "quality_tiers": {"excellent": 0, "good": 0, "fair": 0, "poor": 0},
            },
            "alerts": [],
            "insights": [],
        }

    mos_scores = [
        e.metrics.get("mos_score", 0)
        for e in recent_entries
        if hasattr(e, "metrics") and e.metrics.get("mos_score")
    ]
    similarity_scores = [
        e.metrics.get("similarity", 0)
        for e in recent_entries
        if hasattr(e, "metrics") and e.metrics.get("similarity")
    ]
    naturalness_scores = [
        e.metrics.get("naturalness", 0)
        for e in recent_entries
        if hasattr(e, "metrics") and e.metrics.get("naturalness")
    ]

    avg_mos = sum(mos_scores) / len(mos_scores) if mos_scores else 0.0
    avg_similarity = sum(similarity_scores) / len(similarity_scores) if similarity_scores else 0.0
    avg_naturalness = (
        sum(naturalness_scores) / len(naturalness_scores) if naturalness_scores else 0.0
    )

    daily_data: dict[Any, dict[str, list[Any]]] = {}
    for entry in recent_entries:
        ts = getattr(entry, "timestamp", "")
        entry_date = datetime.fromisoformat(ts.replace("Z", "+00:00")).date()
        if entry_date not in daily_data:
            daily_data[entry_date] = {"mos": [], "similarity": [], "naturalness": []}
        m = getattr(entry, "metrics", {}) or {}
        if m.get("mos_score"):
            daily_data[entry_date]["mos"].append(m["mos_score"])
        if m.get("similarity"):
            daily_data[entry_date]["similarity"].append(m["similarity"])
        if m.get("naturalness"):
            daily_data[entry_date]["naturalness"].append(m["naturalness"])

    sorted_dates = sorted(daily_data.keys())
    mos_trend = [
        {
            "date": str(d),
            "value": (
                sum(daily_data[d]["mos"]) / len(daily_data[d]["mos"])
                if daily_data[d]["mos"]
                else 0.0
            ),
        }
        for d in sorted_dates
    ]
    similarity_trend = [
        {
            "date": str(d),
            "value": (
                sum(daily_data[d]["similarity"]) / len(daily_data[d]["similarity"])
                if daily_data[d]["similarity"]
                else 0.0
            ),
        }
        for d in sorted_dates
    ]
    naturalness_trend = [
        {
            "date": str(d),
            "value": (
                sum(daily_data[d]["naturalness"]) / len(daily_data[d]["naturalness"])
                if daily_data[d]["naturalness"]
                else 0.0
            ),
        }
        for d in sorted_dates
    ]

    mos_distribution: dict[int, int] = {}
    for score in mos_scores:
        bucket = int(score)
        mos_distribution[bucket] = mos_distribution.get(bucket, 0) + 1

    excellent = sum(1 for s in mos_scores if s >= 4.5)
    good = sum(1 for s in mos_scores if 3.5 <= s < 4.5)
    fair = sum(1 for s in mos_scores if 2.5 <= s < 3.5)
    poor = sum(1 for s in mos_scores if s < 2.5)

    alerts: list[dict[str, Any]] = []
    if avg_mos < 3.0:
        alerts.append(
            {
                "type": "warning",
                "message": f"Average MOS score ({avg_mos:.2f}) is below acceptable threshold (3.0)",
                "severity": "high",
            }
        )
    if avg_similarity < 0.7:
        alerts.append(
            {
                "type": "warning",
                "message": f"Average similarity ({avg_similarity:.2f}) is below target (0.7)",
                "severity": "medium",
            }
        )

    insights: list[dict[str, Any]] = []
    if len(recent_entries) > 10:
        insights.append(
            {
                "type": "statistic",
                "message": f"Analyzed {len(recent_entries)} quality samples over the last {days} days",
            }
        )
    if avg_mos >= 4.0:
        insights.append(
            {"type": "positive", "message": "Quality metrics are above target thresholds"}
        )

    return {
        "overview": {
            "total_samples": len(recent_entries),
            "average_mos": round(avg_mos, 2),
            "average_similarity": round(avg_similarity, 2),
            "average_naturalness": round(avg_naturalness, 2),
        },
        "trends": {
            "mos_trend": mos_trend,
            "similarity_trend": similarity_trend,
            "naturalness_trend": naturalness_trend,
        },
        "distribution": {
            "mos_distribution": mos_distribution,
            "quality_tiers": {
                "excellent": excellent,
                "good": good,
                "fair": fair,
                "poor": poor,
            },
        },
        "alerts": alerts,
        "insights": insights,
    }
