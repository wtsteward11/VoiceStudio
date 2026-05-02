"""
Training engines for VoiceStudio
"""

from . import unified_trainer as unified_trainer  # keep submodule reachable for mock.patch targets
from .auto_trainer import AutoTrainer, create_auto_trainer
from .parameter_optimizer import ParameterOptimizer, create_parameter_optimizer
from .training_progress_monitor import (
    TrainingProgressMonitor,
    create_training_progress_monitor,
)
from .unified_trainer import UnifiedTrainer, create_unified_trainer
from .xtts_trainer import XTTSTrainer

__all__ = [
    "AutoTrainer",
    "ParameterOptimizer",
    "TrainingProgressMonitor",
    "UnifiedTrainer",
    "XTTSTrainer",
    "create_auto_trainer",
    "create_parameter_optimizer",
    "create_training_progress_monitor",
    "create_unified_trainer",
]
