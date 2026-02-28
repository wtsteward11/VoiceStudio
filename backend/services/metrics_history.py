"""
Metrics History Service — Phase 8 WS4

Stores hourly snapshots of key metrics for trend charts.
Local-first: .voicestudio/metrics/hourly/
"""

from __future__ import annotations

import json
import logging
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

METRICS_HOURLY_DIR = Path(".voicestudio/metrics/hourly")


def _ensure_dir() -> None:
    METRICS_HOURLY_DIR.mkdir(parents=True, exist_ok=True)


def _hour_key(dt: datetime | None = None) -> str:
    """Get hour key for filename (YYYY-MM-DD-HH)."""
    dt = dt or datetime.now(timezone.utc)
    return dt.strftime("%Y-%m-%d-%H")


def record_hourly_snapshot(metrics: dict[str, Any]) -> Path:
    """
    Record a snapshot of current metrics to hourly file.

    Returns:
        Path to the written file.
    """
    _ensure_dir()
    key = _hour_key()
    path = METRICS_HOURLY_DIR / f"{key}.json"
    data = {
        "hour": key,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "metrics": metrics,
    }
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
    except OSError as e:
        logger.warning(f"Failed to write metrics snapshot: {e}")
    return path


def get_metrics_history(window_hours: int = 24) -> list[dict[str, Any]]:
    """
    Get metrics history for the last N hours.

    Returns:
        List of snapshot dicts, newest first.
    """
    _ensure_dir()
    cutoff = datetime.now(timezone.utc) - timedelta(hours=window_hours)
    result: list[dict[str, Any]] = []
    for path in sorted(METRICS_HOURLY_DIR.glob("*.json"), reverse=True):
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
            ts = data.get("timestamp", "")
            if ts:
                try:
                    dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
                    if dt < cutoff:
                        break
                except (ValueError, TypeError):
                    pass
            result.append(data)
            if len(result) >= window_hours:
                break
        except (json.JSONDecodeError, OSError) as e:
            logger.debug(f"Skip metrics file {path}: {e}")
    return result
