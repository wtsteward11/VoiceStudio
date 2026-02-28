"""
Model Data Drift Detection — Phase 9 Sprint 2

Statistical drift detection for model inputs and outputs using
Population Stability Index (PSI). Alerts when PSI exceeds threshold.

Tracks:
- Input: text length, language, reference audio characteristics
- Output: MOS scores, SNR, latency

Local-first: data/model_drift.json
"""

from __future__ import annotations

import json
import logging
import math
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)

PSI_THRESHOLD = 0.2  # PSI > 0.2 = significant drift
DEFAULT_BINS = 10


@dataclass
class DriftMetric:
    """Single drift metric for an engine."""

    engine_id: str
    metric_name: str  # e.g. "latency_ms", "text_length", "mos"
    psi: float
    baseline_sample_count: int
    current_sample_count: int
    last_updated: str
    is_drifted: bool


@dataclass
class DriftStatus:
    """Drift status for an engine."""

    engine_id: str
    has_baseline: bool
    metrics: list[DriftMetric] = field(default_factory=list)
    any_drifted: bool = False
    last_checked: str = ""


def _compute_psi(
    baseline_counts: list[int],
    current_counts: list[int],
    num_bins: int = DEFAULT_BINS,
) -> float:
    """
    Compute Population Stability Index between baseline and current distributions.

    PSI = sum((actual_pct - expected_pct) * ln(actual_pct / expected_pct))
    Uses small epsilon to avoid log(0). Returns 0 if insufficient data.
    """
    total_baseline = sum(baseline_counts)
    total_current = sum(current_counts)
    if total_baseline == 0 or total_current == 0:
        return 0.0

    epsilon = 1e-6
    psi = 0.0
    for i in range(num_bins):
        expected_pct = (baseline_counts[i] / total_baseline) + epsilon
        actual_pct = (current_counts[i] / total_current) + epsilon
        psi += (actual_pct - expected_pct) * math.log(actual_pct / expected_pct)
    return psi


def _values_to_bins(values: list[float], num_bins: int = DEFAULT_BINS) -> list[int]:
    """Bin numeric values into histogram counts."""
    if not values:
        return [0] * num_bins
    lo, hi = min(values), max(values)
    if lo == hi:
        return [len(values)] + [0] * (num_bins - 1)
    width = (hi - lo) / num_bins or 1e-9
    counts = [0] * num_bins
    for v in values:
        idx = min(int((v - lo) / width), num_bins - 1)
        counts[idx] += 1
    return counts


class ModelDriftDetector:
    """
    Detects statistical drift in model inputs/outputs using PSI.

    - Store baseline distributions per engine/metric from A/B test or manual set
    - Accumulate current observations from synthesis/transcription
    - Compute PSI when comparing current vs baseline
    - Alert when PSI > threshold
    """

    def __init__(self, data_dir: Path | None = None):
        self._data_dir = data_dir or get_path("data")
        self._drift_path = self._data_dir / "model_drift.json"
        self._baselines: dict[str, list[float]] = {}  # key: engine_id:metric_name -> values
        self._current: dict[str, list[float]] = {}
        self._load()

    def _key(self, engine_id: str, metric_name: str) -> str:
        return f"{engine_id}:{metric_name}"

    @staticmethod
    def _parse_list_float_dict(raw: dict[str, Any]) -> dict[str, list[float]]:
        """Convert JSON-loaded dict to dict[str, list[float]]."""
        result: dict[str, list[float]] = {}
        for k, v in raw.items():
            if isinstance(v, list):
                result[k] = [float(x) for x in v if isinstance(x, (int, float))]
            else:
                result[k] = []
        return result

    def _load(self) -> None:
        """Load baselines and current from disk."""
        if self._drift_path.exists():
            try:
                with open(self._drift_path, encoding="utf-8") as f:
                    data = json.load(f)
                self._baselines = self._parse_list_float_dict(data.get("baselines", {}))
                self._current = self._parse_list_float_dict(data.get("current", {}))
                logger.debug(
                    "Loaded drift data: %d baselines, %d current",
                    len(self._baselines),
                    len(self._current),
                )
            except (json.JSONDecodeError, OSError) as e:
                logger.warning("Failed to load model drift data: %s", e)
                self._baselines = {}
                self._current = {}
        else:
            self._baselines = {}
            self._current = {}

    def _save(self) -> None:
        """Save to disk."""
        self._drift_path.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = self._drift_path.with_suffix(".json.tmp")
        try:
            data = {
                "version": "1.0",
                "updated_at": datetime.now(timezone.utc).isoformat(),
                "baselines": self._baselines,
                "current": self._current,
            }
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            tmp_path.replace(self._drift_path)
        except OSError as e:
            logger.error("Failed to save model drift data: %s", e)
        finally:
            if tmp_path.exists():
                try:
                    tmp_path.unlink()
                except OSError as e:
                    logger.debug("Could not remove temp file %s: %s", tmp_path, e)

    def set_baseline(
        self,
        engine_id: str,
        metric_name: str,
        values: list[float],
    ) -> None:
        """Set baseline distribution for a metric."""
        key = self._key(engine_id, metric_name)
        self._baselines[key] = list(values)
        self._save()
        logger.info("Set drift baseline for %s: %d samples", key, len(values))

    def report_observation(
        self,
        engine_id: str,
        metric_name: str,
        value: float,
    ) -> None:
        """Record a single observation for current distribution."""
        key = self._key(engine_id, metric_name)
        if key not in self._current:
            self._current[key] = []
        self._current[key].append(value)
        # Keep last 1000 samples to limit memory
        if len(self._current[key]) > 1000:
            self._current[key] = self._current[key][-1000:]
        self._save()

    def get_status(self, engine_id: str | None = None) -> list[DriftStatus]:
        """
        Get drift status per engine.

        Returns list of DriftStatus. If engine_id given, filter to that engine.
        """
        now = datetime.now(timezone.utc).isoformat()
        result: list[DriftStatus] = []
        engines_seen: set[str] = set()

        for key in set(self._baselines.keys()) | set(self._current.keys()):
            parts = key.split(":", 1)
            eng = parts[0] if len(parts) > 0 else ""
            metric_name = parts[1] if len(parts) > 1 else ""
            if engine_id is not None and eng != engine_id:
                continue
            if eng:
                engines_seen.add(eng)

        for eng in sorted(engines_seen):
            if engine_id is not None and eng != engine_id:
                continue
            metrics: list[DriftMetric] = []
            any_drifted = False
            for key, baseline_vals in self._baselines.items():
                if not key.startswith(eng + ":"):
                    continue
                metric_name = key.split(":", 1)[1]
                current_vals: list[float] = self._current.get(key, [])
                if len(baseline_vals) < 5:
                    continue
                baseline_bins = _values_to_bins(baseline_vals)
                current_bins = _values_to_bins(current_vals) if current_vals else [0] * DEFAULT_BINS
                psi = _compute_psi(baseline_bins, current_bins)
                is_drifted = psi > PSI_THRESHOLD
                if is_drifted:
                    any_drifted = True
                metrics.append(
                    DriftMetric(
                        engine_id=eng,
                        metric_name=metric_name,
                        psi=round(psi, 4),
                        baseline_sample_count=len(baseline_vals),
                        current_sample_count=len(current_vals),
                        last_updated=now,
                        is_drifted=is_drifted,
                    )
                )
            if not metrics:
                # Engine with baseline but no computed metrics yet
                has_baseline = any(k.startswith(eng + ":") for k in self._baselines)
                if has_baseline:
                    metrics = []
            result.append(
                DriftStatus(
                    engine_id=eng,
                    has_baseline=any(k.startswith(eng + ":") for k in self._baselines),
                    metrics=metrics,
                    any_drifted=any_drifted,
                    last_checked=now,
                )
            )

        return result

    def get_history(
        self,
        engine_id: str | None = None,
        metric_name: str | None = None,
        limit: int = 100,
    ) -> list[dict[str, Any]]:
        """
        Get drift history (stub for future time-series storage).

        For now returns current status snapshot as single history entry.
        """
        statuses = self.get_status(engine_id)
        history: list[dict[str, Any]] = []
        for s in statuses:
            for m in s.metrics:
                if metric_name is not None and m.metric_name != metric_name:
                    continue
                history.append(
                    {
                        "engine_id": s.engine_id,
                        "metric_name": m.metric_name,
                        "psi": m.psi,
                        "is_drifted": m.is_drifted,
                        "timestamp": s.last_checked,
                    }
                )
                if len(history) >= limit:
                    return history
        return history


_drift_instance: ModelDriftDetector | None = None


def get_model_drift_detector() -> ModelDriftDetector:
    """Get or create the model drift detector singleton."""
    global _drift_instance
    if _drift_instance is None:
        _drift_instance = ModelDriftDetector()
    return _drift_instance
