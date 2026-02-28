"""
Model Performance Baselines — Phase 8 WS1

Persists per-model performance baselines (latency p95, RTF, throughput) and
detects degradation when current metrics exceed baseline by >20%.

Local-first: data/model_baselines.json
"""

from __future__ import annotations

import json
import logging
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)

DEGRADATION_THRESHOLD = 0.20  # 20% worse than baseline = degradation


@dataclass
class ModelBaseline:
    """Performance baseline for a model version."""

    engine_id: str
    model_name: str
    version: str
    latency_p95_ms: float = 0.0
    rtf_p95: float = 0.0
    throughput_p50_chars_per_sec: float = 0.0
    mos: float = 0.0  # Mean Opinion Score if available
    sample_count: int = 0
    updated_at: str = ""

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> ModelBaseline:
        """Create from dictionary."""
        return cls(
            engine_id=data.get("engine_id", ""),
            model_name=data.get("model_name", ""),
            version=data.get("version", "1.0"),
            latency_p95_ms=data.get("latency_p95_ms", 0.0),
            rtf_p95=data.get("rtf_p95", 0.0),
            throughput_p50_chars_per_sec=data.get("throughput_p50_chars_per_sec", 0.0),
            mos=data.get("mos", 0.0),
            sample_count=data.get("sample_count", 0),
            updated_at=data.get("updated_at", ""),
        )


class ModelBaselinesService:
    """
    Manages per-model performance baselines and degradation detection.

    - Persist baselines to data/model_baselines.json
    - Update baseline from current metrics (rolling average or replace)
    - Compare current metrics to baseline, emit warning when >20% worse
    """

    def __init__(self, data_dir: Path | None = None):
        self._data_dir = data_dir or get_path("data")
        self._baselines_path = self._data_dir / "model_baselines.json"
        self._baselines: dict[str, ModelBaseline] = {}  # key: engine_id:model_name:version
        self._load()

    def _key(self, engine_id: str, model_name: str, version: str) -> str:
        return f"{engine_id}:{model_name}:{version}"

    def _load(self) -> None:
        """Load baselines from disk."""
        if self._baselines_path.exists():
            try:
                with open(self._baselines_path, encoding="utf-8") as f:
                    data = json.load(f)
                self._baselines = {}
                for key, bl_data in data.get("baselines", {}).items():
                    parts = key.split(":", 2)
                    engine_id = parts[0] if len(parts) > 0 else ""
                    model_name = parts[1] if len(parts) > 1 else ""
                    version = parts[2] if len(parts) > 2 else "1.0"
                    self._baselines[key] = ModelBaseline.from_dict(
                        {
                            **bl_data,
                            "engine_id": engine_id,
                            "model_name": model_name,
                            "version": version,
                        }
                    )
                logger.debug(f"Loaded {len(self._baselines)} model baselines")
            except (json.JSONDecodeError, OSError) as e:
                logger.warning(f"Failed to load model baselines: {e}")
                self._baselines = {}
        else:
            self._baselines = {}

    def _save(self) -> None:
        """Save baselines to disk."""
        self._baselines_path.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = self._baselines_path.with_suffix(".json.tmp")
        try:
            data = {
                "version": "1.0",
                "updated_at": datetime.now(timezone.utc).isoformat(),
                "baselines": {k: v.to_dict() for k, v in self._baselines.items()},
            }
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            tmp_path.replace(self._baselines_path)
        except OSError as e:
            logger.error(f"Failed to save model baselines: {e}")
        finally:
            if tmp_path.exists():
                try:
                    tmp_path.unlink()
                except OSError as e:
                    logger.debug("Could not remove temp file %s: %s", tmp_path, e)

    def update_baseline(
        self,
        engine_id: str,
        model_name: str,
        version: str,
        latency_p95_ms: float = 0.0,
        rtf_p95: float = 0.0,
        throughput_p50_chars_per_sec: float = 0.0,
        mos: float = 0.0,
    ) -> ModelBaseline:
        """
        Update or create baseline for a model version.

        Uses exponential smoothing: new_baseline = 0.7 * old + 0.3 * current
        for first 10 samples, then stabilizes.
        """
        key = self._key(engine_id, model_name, version)
        now = datetime.now(timezone.utc).isoformat()

        if key in self._baselines:
            bl = self._baselines[key]
            alpha = 0.3 if bl.sample_count < 10 else 0.1
            bl.latency_p95_ms = (
                (1 - alpha) * bl.latency_p95_ms + alpha * latency_p95_ms
                if latency_p95_ms > 0
                else bl.latency_p95_ms
            )
            bl.rtf_p95 = (1 - alpha) * bl.rtf_p95 + alpha * rtf_p95 if rtf_p95 > 0 else bl.rtf_p95
            bl.throughput_p50_chars_per_sec = (
                (1 - alpha) * bl.throughput_p50_chars_per_sec + alpha * throughput_p50_chars_per_sec
                if throughput_p50_chars_per_sec > 0
                else bl.throughput_p50_chars_per_sec
            )
            bl.mos = (1 - alpha) * bl.mos + alpha * mos if mos > 0 else bl.mos
            bl.sample_count += 1
            bl.updated_at = now
        else:
            bl = ModelBaseline(
                engine_id=engine_id,
                model_name=model_name,
                version=version,
                latency_p95_ms=latency_p95_ms,
                rtf_p95=rtf_p95,
                throughput_p50_chars_per_sec=throughput_p50_chars_per_sec,
                mos=mos,
                sample_count=1,
                updated_at=now,
            )
            self._baselines[key] = bl

        self._save()
        return bl

    def get_baseline(self, engine_id: str, model_name: str, version: str) -> ModelBaseline | None:
        """Get baseline for a model version."""
        key = self._key(engine_id, model_name, version)
        return self._baselines.get(key)

    def check_degradation(
        self,
        engine_id: str,
        model_name: str,
        version: str,
        latency_p95_ms: float = 0.0,
        rtf_p95: float = 0.0,
        throughput_p50_chars_per_sec: float = 0.0,
        mos: float = 0.0,
    ) -> tuple[bool, list[str]]:
        """
        Compare current metrics to baseline. Emit warnings when >20% worse.

        Returns:
            (is_degraded, list of warning messages)
        """
        bl = self.get_baseline(engine_id, model_name, version)
        if bl is None:
            return (False, [])

        warnings: list[str] = []
        degraded = False

        if bl.latency_p95_ms > 0 and latency_p95_ms > 0:
            ratio = latency_p95_ms / bl.latency_p95_ms
            if ratio > (1 + DEGRADATION_THRESHOLD):
                degraded = True
                warnings.append(
                    f"Latency p95 degraded: {latency_p95_ms:.0f}ms vs baseline "
                    f"{bl.latency_p95_ms:.0f}ms (+{(ratio - 1) * 100:.0f}%)"
                )

        if bl.rtf_p95 > 0 and rtf_p95 > 0:
            ratio = rtf_p95 / bl.rtf_p95
            if ratio > (1 + DEGRADATION_THRESHOLD):
                degraded = True
                warnings.append(
                    f"RTF p95 degraded: {rtf_p95:.2f} vs baseline "
                    f"{bl.rtf_p95:.2f} (+{(ratio - 1) * 100:.0f}%)"
                )

        if bl.throughput_p50_chars_per_sec > 0 and throughput_p50_chars_per_sec > 0:
            ratio = bl.throughput_p50_chars_per_sec / throughput_p50_chars_per_sec
            if ratio > (1 + DEGRADATION_THRESHOLD):
                degraded = True
                warnings.append(
                    f"Throughput degraded: {throughput_p50_chars_per_sec:.0f} "
                    f"chars/s vs baseline {bl.throughput_p50_chars_per_sec:.0f} "
                    f"chars/s (-{(1 - 1 / ratio) * 100:.0f}%)"
                )

        for msg in warnings:
            logger.warning(
                "Model performance degradation [%s/%s v%s]: %s",
                engine_id,
                model_name,
                version,
                msg,
            )

        return (degraded, warnings)


_baselines_instance: ModelBaselinesService | None = None


def get_model_baselines_service(
    data_dir: Path | None = None,
) -> ModelBaselinesService:
    """Get or create the model baselines service singleton."""
    global _baselines_instance
    if _baselines_instance is None:
        _baselines_instance = ModelBaselinesService(data_dir=data_dir)
    return _baselines_instance
