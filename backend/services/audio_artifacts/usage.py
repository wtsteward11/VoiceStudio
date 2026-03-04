"""
Usage recording adapter for the artifact spine.

Calls the canonical usage stats API (record_synthesis_minutes).
Does not import route modules.
"""

from __future__ import annotations


def record_usage(
    duration_sec: float | None,
    *,
    created_by: str,
    kind: str = "audio",
    metadata: dict | None = None,
) -> None:
    """
    Record usage minutes for an audio artifact.

    Only records when duration_sec is not None and > 0.
    Delegates to record_synthesis_minutes.
    """
    if duration_sec is None or duration_sec <= 0:
        return
    minutes = max(duration_sec, 0.0) / 60.0
    from backend.services.usage_stats import record_synthesis_minutes

    record_synthesis_minutes(minutes)
