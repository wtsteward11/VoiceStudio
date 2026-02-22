"""
Training Progress Monitor Module for VoiceStudio
Real-time training progress monitoring and metrics tracking

Compatible with:
- Python 3.10+
"""

from __future__ import annotations

import importlib
import logging
from collections import deque
from collections.abc import Callable
from datetime import datetime
from pathlib import Path
from typing import Any, cast

logger = logging.getLogger(__name__)

HAS_TENSORBOARD = False
SummaryWriter: Any = None
try:
    _tb_mod = importlib.import_module("torch.utils.tensorboard")
    SummaryWriter = _tb_mod.SummaryWriter
    HAS_TENSORBOARD = True
except ImportError:
    try:
        _tb_mod = importlib.import_module("tensorboard")
        SummaryWriter = _tb_mod.SummaryWriter
        HAS_TENSORBOARD = True
    except (ImportError, AttributeError):
        logger.debug("tensorboard not installed. Training visualization will be limited.")

try:
    import wandb

    HAS_WANDB = True
except ImportError:
    HAS_WANDB = False
    wandb = cast(Any, None)
    logger.debug("wandb not installed. Experiment tracking will be limited.")


class TrainingProgressMonitor:
    """
    Training Progress Monitor for real-time progress tracking.

    Supports:
    - Real-time progress updates
    - Metrics tracking (loss, accuracy, etc.)
    - Epoch-level monitoring
    - History tracking
    - Progress callbacks
    - Status reporting
    """

    def __init__(
        self,
        max_history: int = 1000,
        log_dir: str | Path | None = None,
        enable_tensorboard: bool = True,
    ):
        """
        Initialize Training Progress Monitor.

        Args:
            max_history: Maximum number of history entries to keep
            log_dir: Directory for tensorboard logs (if None, uses default)
            enable_tensorboard: Whether to enable tensorboard logging
        """
        self.max_history = max_history
        self.current_status: dict[str, Any] = {}
        self.history: deque = deque(maxlen=max_history)
        self.metrics_history: dict[str, list[float]] = {}
        self.callbacks: list[Callable[[dict], None]] = []

        # TensorBoard writer
        self.tensorboard_writer: Any | None = None
        if enable_tensorboard and HAS_TENSORBOARD:
            try:
                log_dir = Path("logs/tensorboard") if log_dir is None else Path(log_dir)
                log_dir.mkdir(parents=True, exist_ok=True)
                self.tensorboard_writer = SummaryWriter(str(log_dir))
                logger.info(f"TensorBoard logging enabled: {log_dir}")
            except Exception as e:
                logger.warning(f"Failed to initialize TensorBoard: {e}")
                self.tensorboard_writer = None

        # WandB run (optional, initialized in start_monitoring)
        self.wandb_run: Any | None = None
        self.enable_wandb: bool = False

    def start_monitoring(
        self,
        training_id: str,
        total_epochs: int,
        initial_params: dict | None = None,
        enable_wandb: bool = False,
        wandb_project: str | None = None,
        wandb_config: dict | None = None,
    ):
        """
        Start monitoring a training session.

        Args:
            training_id: Unique training identifier
            total_epochs: Total number of epochs
            initial_params: Initial training parameters
            enable_wandb: Whether to enable Weights & Biases tracking
            wandb_project: WandB project name (defaults to "voicestudio-training")
            wandb_config: WandB configuration dictionary
        """
        self.current_status = {
            "training_id": training_id,
            "status": "running",
            "current_epoch": 0,
            "total_epochs": total_epochs,
            "progress": 0.0,
            "started_at": datetime.utcnow().isoformat(),
            "updated_at": datetime.utcnow().isoformat(),
            "metrics": {},
            "params": initial_params or {},
        }

        # Initialize metrics history
        self.metrics_history = {
            "loss": [],
            "epoch": [],
        }

        # Initialize WandB if requested
        self.enable_wandb = enable_wandb
        if enable_wandb and HAS_WANDB:
            try:
                wandb_config_dict = wandb_config or {}
                wandb_config_dict.update(initial_params or {})
                wandb_config_dict["training_id"] = training_id
                wandb_config_dict["total_epochs"] = total_epochs

                self.wandb_run = wandb.init(
                    project=wandb_project or "voicestudio-training",
                    name=training_id,
                    config=wandb_config_dict,
                    reinit=True,
                )
                logger.info(f"WandB tracking enabled for training: {training_id}")
            except Exception as e:
                logger.warning(f"Failed to initialize WandB: {e}")
                self.wandb_run = None
                self.enable_wandb = False

        logger.info(f"Started monitoring training: {training_id}")

    def update_progress(
        self,
        epoch: int,
        metrics: dict[str, float] | None = None,
        additional_info: dict | None = None,
    ):
        """
        Update training progress.

        Args:
            epoch: Current epoch number
            metrics: Dictionary of metrics (e.g., {"loss": 0.5, "accuracy": 0.9})
            additional_info: Additional information to store
        """
        if not self.current_status:
            logger.warning("No active training session to update")
            return

        total_epochs = self.current_status.get("total_epochs", 1)
        progress = min(1.0, epoch / total_epochs) if total_epochs > 0 else 0.0

        # Update current status
        self.current_status["current_epoch"] = epoch
        self.current_status["progress"] = progress
        self.current_status["updated_at"] = datetime.utcnow().isoformat()

        if metrics:
            self.current_status["metrics"].update(metrics)

            # Track metrics history
            for metric_name, metric_value in metrics.items():
                if metric_name not in self.metrics_history:
                    self.metrics_history[metric_name] = []
                self.metrics_history[metric_name].append(metric_value)

        # Track epoch history
        self.metrics_history["epoch"].append(epoch)

        # Add to history
        history_entry = {
            "timestamp": datetime.utcnow().isoformat(),
            "epoch": epoch,
            "progress": progress,
            "metrics": metrics or {},
            "additional_info": additional_info or {},
        }
        self.history.append(history_entry)

        # Log to TensorBoard if available
        if self.tensorboard_writer and metrics:
            try:
                for metric_name, metric_value in metrics.items():
                    self.tensorboard_writer.add_scalar(
                        f"Metrics/{metric_name}", metric_value, epoch
                    )
            except Exception as e:
                logger.warning(f"TensorBoard logging failed: {e}")

        # Log to WandB if available
        if self.enable_wandb and self.wandb_run and metrics:
            try:
                log_dict = {f"metrics/{k}": v for k, v in metrics.items()}
                log_dict["epoch"] = epoch
                log_dict["progress"] = progress
                self.wandb_run.log(log_dict)
            except Exception as e:
                logger.warning(f"WandB logging failed: {e}")

        # Notify callbacks
        self._notify_callbacks(self.current_status.copy())

        logger.debug(f"Progress updated: epoch {epoch}/{total_epochs}, progress {progress:.2%}")

    def complete_training(
        self,
        final_metrics: dict[str, float] | None = None,
        success: bool = True,
        error_message: str | None = None,
    ):
        """
        Mark training as complete.

        Args:
            final_metrics: Final metrics dictionary
            success: Whether training completed successfully
            error_message: Error message if training failed
        """
        if not self.current_status:
            logger.warning("No active training session to complete")
            return

        self.current_status["status"] = "completed" if success else "failed"
        self.current_status["progress"] = 1.0
        self.current_status["completed_at"] = datetime.utcnow().isoformat()
        self.current_status["updated_at"] = datetime.utcnow().isoformat()

        if final_metrics:
            self.current_status["metrics"].update(final_metrics)

        if error_message:
            self.current_status["error"] = error_message

        # Notify callbacks
        self._notify_callbacks(self.current_status.copy())

        logger.info(
            f"Training {self.current_status.get('training_id')} completed: {self.current_status['status']}"
        )

    def cancel_training(self, reason: str | None = None):
        """
        Mark training as cancelled.

        Args:
            reason: Optional cancellation reason
        """
        if not self.current_status:
            logger.warning("No active training session to cancel")
            return

        self.current_status["status"] = "cancelled"
        self.current_status["cancelled_at"] = datetime.utcnow().isoformat()
        self.current_status["updated_at"] = datetime.utcnow().isoformat()

        if reason:
            self.current_status["cancellation_reason"] = reason

        # Notify callbacks
        self._notify_callbacks(self.current_status.copy())

        logger.info(f"Training {self.current_status.get('training_id')} cancelled: {reason}")

    def get_current_status(self) -> dict[str, Any]:
        """
        Get current training status.

        Returns:
            Dictionary with current status information
        """
        return self.current_status.copy()

    def get_progress(self) -> float:
        """
        Get current training progress (0.0 to 1.0).

        Returns:
            Progress value between 0.0 and 1.0
        """
        return float(self.current_status.get("progress", 0.0))

    def get_metrics(self) -> dict[str, float]:
        """
        Get current metrics.

        Returns:
            Dictionary of current metrics
        """
        metrics: dict[str, float] = self.current_status.get("metrics", {})
        return metrics.copy()

    def get_metrics_history(self, metric_name: str | None = None) -> dict[str, list[float]]:
        """
        Get metrics history.

        Args:
            metric_name: Optional specific metric name to retrieve

        Returns:
            Dictionary of metrics history or list for specific metric
        """
        if metric_name:
            return {metric_name: self.metrics_history.get(metric_name, [])}
        return self.metrics_history.copy()

    def get_history(self, limit: int | None = None) -> list[dict[str, Any]]:
        """
        Get training history.

        Args:
            limit: Optional limit on number of entries to return

        Returns:
            List of history entries
        """
        history_list = list(self.history)
        if limit:
            return history_list[-limit:]
        return history_list

    def register_callback(self, callback: Callable[[dict], None]):
        """
        Register a callback for progress updates.

        Args:
            callback: Callback function that receives status dictionary
        """
        if callback not in self.callbacks:
            self.callbacks.append(callback)
            logger.debug("Progress callback registered")

    def unregister_callback(self, callback: Callable[[dict], None]):
        """
        Unregister a progress callback.

        Args:
            callback: Callback function to remove
        """
        if callback in self.callbacks:
            self.callbacks.remove(callback)
            logger.debug("Progress callback unregistered")

    def _notify_callbacks(self, status: dict[str, Any]):
        """Notify all registered callbacks of status update."""
        for callback in self.callbacks:
            try:
                callback(status)
            except Exception as e:
                logger.warning(f"Callback notification failed: {e}")

    def get_summary(self) -> dict[str, Any]:
        """
        Get training summary.

        Returns:
            Dictionary with training summary information
        """
        if not self.current_status:
            return {"message": "No active training session"}

        summary = {
            "training_id": self.current_status.get("training_id"),
            "status": self.current_status.get("status"),
            "progress": self.current_status.get("progress", 0.0),
            "current_epoch": self.current_status.get("current_epoch", 0),
            "total_epochs": self.current_status.get("total_epochs", 0),
            "started_at": self.current_status.get("started_at"),
            "updated_at": self.current_status.get("updated_at"),
            "metrics": self.current_status.get("metrics", {}),
        }

        # Add completion/cancellation info if available
        if "completed_at" in self.current_status:
            summary["completed_at"] = self.current_status["completed_at"]
        if "cancelled_at" in self.current_status:
            summary["cancelled_at"] = self.current_status["cancelled_at"]
        if "error" in self.current_status:
            summary["error"] = self.current_status["error"]

        # Add metrics statistics
        if self.metrics_history.get("loss"):
            losses = self.metrics_history["loss"]
            summary["loss_statistics"] = {
                "current": losses[-1] if losses else None,
                "min": min(losses) if losses else None,
                "max": max(losses) if losses else None,
                "average": sum(losses) / len(losses) if losses else None,
            }

        return summary

    def reset(self):
        """Reset monitor state."""
        # Close TensorBoard writer if open
        if self.tensorboard_writer:
            try:
                self.tensorboard_writer.close()
            except Exception as e:
                logger.warning(f"Failed to close TensorBoard writer: {e}")
            self.tensorboard_writer = None

        # Finish WandB run if active
        if self.wandb_run:
            try:
                self.wandb_run.finish()
            except Exception as e:
                logger.warning(f"Failed to finish WandB run: {e}")
            self.wandb_run = None
            self.enable_wandb = False

        self.current_status = {}
        self.history.clear()
        self.metrics_history = {}
        logger.debug("Progress monitor reset")


def create_training_progress_monitor(
    max_history: int = 1000,
    log_dir: str | Path | None = None,
    enable_tensorboard: bool = True,
) -> TrainingProgressMonitor:
    """
    Factory function to create a Training Progress Monitor instance.

    Args:
        max_history: Maximum number of history entries to keep
        log_dir: Directory for tensorboard logs
        enable_tensorboard: Whether to enable tensorboard logging

    Returns:
        Initialized TrainingProgressMonitor instance
    """
    return TrainingProgressMonitor(
        max_history=max_history,
        log_dir=log_dir,
        enable_tensorboard=enable_tensorboard,
    )
