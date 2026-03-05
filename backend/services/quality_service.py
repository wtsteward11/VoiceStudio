"""
Quality service for quality analysis, optimization, presets, and benchmarks.

Owns quality logic; routes stay thin (parse -> call service -> return).
Service layer must not depend on API layer (routes or ws).
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)

# Optional quality facade (may not be available if app.core.engines not installed)
_QualityOptimizer = None
_optimize_synthesis_for_quality = None
_list_quality_presets = None
_get_quality_preset = None
_get_preset_description = None
_get_preset_target_metrics = None
_get_synthesis_params_from_preset = None
_QualityComparison = None

try:
    from backend.engines.quality_facade import (
        QualityComparison as _QualityComparison,
    )
    from backend.engines.quality_facade import (
        QualityOptimizer as _QualityOptimizer,
    )
    from backend.engines.quality_facade import (
        get_preset_description as _get_preset_description,
    )
    from backend.engines.quality_facade import (
        get_preset_target_metrics as _get_preset_target_metrics,
    )
    from backend.engines.quality_facade import (
        get_quality_preset as _get_quality_preset,
    )
    from backend.engines.quality_facade import (
        get_synthesis_params_from_preset as _get_synthesis_params_from_preset,
    )
    from backend.engines.quality_facade import (
        list_quality_presets as _list_quality_presets,
    )
    from backend.engines.quality_facade import (
        optimize_synthesis_for_quality as _optimize_synthesis_for_quality,
    )
# ALLOWED: bare except - optional dependency, import failure acceptable
except ImportError:
    pass


def has_quality_optimization() -> bool:
    """Return True if quality optimization (presets, optimizer) is available."""
    return _QualityOptimizer is not None and _list_quality_presets is not None


def has_quality_comparison() -> bool:
    """Return True if quality comparison is available."""
    return _QualityComparison is not None


class QualityAnalysisRequest:
    """Request for quality analysis."""

    def __init__(
        self,
        mos_score: float | None = None,
        similarity: float | None = None,
        naturalness: float | None = None,
        snr_db: float | None = None,
        target_tier: str = "standard",
    ):
        self.mos_score = mos_score
        self.similarity = similarity
        self.naturalness = naturalness
        self.snr_db = snr_db
        self.target_tier = target_tier


class QualityAnalysisResult:
    """Result from quality analysis (with metrics for assistant_run compatibility)."""

    def __init__(
        self,
        meets_target: bool,
        quality_score: float,
        deficiencies: list[dict[str, Any]],
        recommendations: list[dict[str, Any]],
        metrics: dict[str, Any] | None = None,
    ):
        self.meets_target = meets_target
        self.quality_score = quality_score
        self.deficiencies = deficiencies
        self.recommendations = recommendations
        self.metrics = metrics or {}


async def analyze_quality(req: QualityAnalysisRequest) -> QualityAnalysisResult:
    """
    Analyze quality metrics and determine if optimization is needed.

    Performs analysis in-service; no route delegation.
    """
    if not has_quality_optimization():
        raise RuntimeError("Quality optimization not available")

    metrics = {
        k: v
        for k, v in [
            ("mos_score", req.mos_score),
            ("similarity", req.similarity),
            ("naturalness", req.naturalness),
            ("snr_db", req.snr_db),
        ]
        if v is not None
    }

    optimizer = _QualityOptimizer(target_tier=req.target_tier)
    analysis = optimizer.analyze_quality(metrics)

    result_metrics = dict(metrics)
    result_metrics.setdefault("quality_score", analysis["quality_score"])

    return QualityAnalysisResult(
        meets_target=analysis["meets_target"],
        quality_score=analysis["quality_score"],
        deficiencies=analysis["deficiencies"],
        recommendations=analysis["recommendations"],
        metrics=result_metrics,
    )


def optimize_quality(
    metrics: dict[str, Any],
    current_params: dict[str, Any],
    target_tier: str = "standard",
) -> tuple[dict[str, Any], dict[str, Any]]:
    """
    Optimize synthesis parameters based on quality metrics.

    Returns (optimized_params, analysis).
    """
    if not has_quality_optimization() or _optimize_synthesis_for_quality is None:
        raise RuntimeError("Quality optimization not available")

    return _optimize_synthesis_for_quality(
        metrics=metrics,
        current_params=current_params,
        target_tier=target_tier,
    )


def list_presets() -> dict[str, Any]:
    """List all available quality presets (name -> config)."""
    if not has_quality_optimization() or _list_quality_presets is None:
        raise RuntimeError("Quality presets not available")

    return _list_quality_presets()


def list_presets_with_details() -> dict[str, dict[str, Any]]:
    """List presets with full details (description, target_metrics, parameters)."""
    presets = list_presets()
    result = {}
    for name, config in presets.items():
        params = _get_synthesis_params_from_preset(name) if _get_synthesis_params_from_preset else {}
        desc = config.get("description", "")
        if not desc and _get_preset_description:
            desc = _get_preset_description(name)
        tgt = config.get("target_metrics", {})
        if not tgt and _get_preset_target_metrics:
            tgt = _get_preset_target_metrics(name)
        result[name] = {
            "name": name,
            "description": desc,
            "target_metrics": tgt,
            "parameters": params,
        }
    return result


def get_preset(preset_name: str) -> dict[str, Any] | None:
    """Get a specific quality preset by name."""
    if not has_quality_optimization() or _get_quality_preset is None:
        raise RuntimeError("Quality presets not available")

    return _get_quality_preset(preset_name)


def get_preset_info(preset_name: str) -> dict[str, Any]:
    """Get preset description, target metrics, and synthesis params."""
    if not has_quality_optimization():
        raise RuntimeError("Quality presets not available")

    preset = _get_quality_preset(preset_name)
    if not preset:
        raise ValueError(f"Preset '{preset_name}' not found")

    params = _get_synthesis_params_from_preset(preset_name)
    return {
        "name": preset_name,
        "description": _get_preset_description(preset_name),
        "target_metrics": _get_preset_target_metrics(preset_name),
        "parameters": params,
    }


def suggest_engine(
    target_tier: str = "standard",
    target_metrics: dict[str, float] | None = None,
) -> str:
    """Get recommended engine based on quality requirements."""
    if not has_quality_optimization():
        raise RuntimeError("Quality optimization not available")

    optimizer = _QualityOptimizer(target_tier=target_tier)
    return optimizer.suggest_engine(target_metrics)


def compare_quality_samples(
    samples: list[tuple[str, str, str | None, dict[str, Any]]],
) -> dict[str, Any]:
    """
    Compare quality across audio samples.

    Args:
        samples: List of (name, audio_path, reference_path_or_none, metadata)

    Returns:
        Comparison result with rankings, statistics, best_samples, comparison_table
    """
    if not has_quality_comparison() or _QualityComparison is None:
        raise RuntimeError("Quality comparison not available")

    comparison = _QualityComparison()
    for name, audio_path, ref_path, metadata in samples:
        comparison.add_sample(
            name=name,
            audio=audio_path,
            reference_audio=ref_path,
            metadata=metadata,
        )
    return comparison.compare()
