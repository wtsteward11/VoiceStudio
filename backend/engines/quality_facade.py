"""
Backend facade for app.core.engines quality modules.

Routes must import from backend.engines.quality_facade, not app.core.engines.*.
"""

from __future__ import annotations

from app.core.engines.quality_comparison import QualityComparison
from app.core.engines.quality_optimizer import QualityOptimizer, optimize_synthesis_for_quality
from app.core.engines.quality_presets import (
    get_preset_description,
    get_preset_target_metrics,
    get_quality_preset,
    get_synthesis_params_from_preset,
    list_quality_presets,
)

__all__ = [
    "QualityComparison",
    "QualityOptimizer",
    "get_preset_description",
    "get_preset_target_metrics",
    "get_quality_preset",
    "get_synthesis_params_from_preset",
    "list_quality_presets",
    "optimize_synthesis_for_quality",
]
