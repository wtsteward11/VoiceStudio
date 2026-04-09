"""
GAP-015 slice 3: append timing samples for SLO baseline proof generation.

When ``VOICESTUDIO_SLO_TIMING_JSON`` is set to a filesystem path, samples are
appended to a JSON document: ``{"samples": [{workflow_id, endpoint, seconds}, ...]}``.

This module is not a pytest test file; it is imported from CI tests only.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any


def append_slo_timing_sample(
    workflow_id: str,
    endpoint: str,
    seconds: float,
    *,
    extra: dict[str, Any] | None = None,
) -> None:
    """Append one timing sample if ``VOICESTUDIO_SLO_TIMING_JSON`` is set."""
    raw = os.environ.get("VOICESTUDIO_SLO_TIMING_JSON")
    if not raw:
        return
    path = Path(raw)
    path.parent.mkdir(parents=True, exist_ok=True)
    data: dict[str, Any] = {"samples": []}
    if path.exists():
        try:
            loaded = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(loaded, dict) and isinstance(loaded.get("samples"), list):
                data = loaded
            else:
                data = {"samples": []}
        except json.JSONDecodeError:
            data = {"samples": []}
    samples = data.setdefault("samples", [])
    record: dict[str, Any] = {
        "workflow_id": workflow_id,
        "endpoint": endpoint,
        "seconds": float(seconds),
    }
    if extra:
        record.update(extra)
    samples.append(record)
    path.write_text(json.dumps(data, indent=2, sort_keys=True), encoding="utf-8")
