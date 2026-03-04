"""
Local-only usage counters for product decisions (Item 38).

Stores: synthesis minutes, exports completed, models downloaded, GPU hours used.
No network; data lives in data/usage_stats.json. Wired into analytics/telemetry
where synthesis, export, and model-download events are tracked.
"""

from __future__ import annotations

import json
import logging
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


def _stats_path() -> Path:
    from backend.config.path_config import get_path

    return get_path("data") / "usage_stats.json"


_LOCK = threading.Lock()
_DEFAULT = {
    "synthesis_minutes": 0.0,
    "exports_completed": 0,
    "models_downloaded": 0,
    "gpu_hours_used": 0.0,
    "last_updated": None,
}


def _load() -> dict:
    path = _stats_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        return _DEFAULT.copy()
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        for k in _DEFAULT:
            if k not in data and k != "last_updated":
                data[k] = _DEFAULT[k]
        return data
    except Exception as e:
        logger.warning("Could not load usage_stats: %s", e)
        return _DEFAULT.copy()


def _save(data: dict) -> None:
    data["last_updated"] = datetime.now(timezone.utc).isoformat()
    path = _stats_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def get_usage_stats() -> dict[str, Any]:
    """Return current usage stats (copy)."""
    with _LOCK:
        return dict(_load())


def record_synthesis_minutes(minutes: float) -> None:
    """Increment synthesis minutes (local only)."""
    with _LOCK:
        data = _load()
        data["synthesis_minutes"] = data.get("synthesis_minutes", 0) + minutes
        _save(data)


def record_export_completed() -> None:
    """Increment export count."""
    with _LOCK:
        data = _load()
        data["exports_completed"] = data.get("exports_completed", 0) + 1
        _save(data)


def record_model_downloaded() -> None:
    """Increment models downloaded count."""
    with _LOCK:
        data = _load()
        data["models_downloaded"] = data.get("models_downloaded", 0) + 1
        _save(data)


def record_gpu_hours(hours: float) -> None:
    """Add GPU hours used."""
    with _LOCK:
        data = _load()
        data["gpu_hours_used"] = data.get("gpu_hours_used", 0) + hours
        _save(data)
