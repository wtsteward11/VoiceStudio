"""
Backend facade for app.core.training.

Routes must import from backend.training.facade, not app.core.training.*.
"""

from __future__ import annotations

from app.core.training import (
    AutoTrainer,
    ParameterOptimizer,
    TrainingProgressMonitor,
    UnifiedTrainer,
    XTTSTrainer,
)
from app.core.training.training_module_audit import TrainingModuleAuditor

__all__ = [
    "AutoTrainer",
    "ParameterOptimizer",
    "TrainingModuleAuditor",
    "TrainingProgressMonitor",
    "UnifiedTrainer",
    "XTTSTrainer",
]
