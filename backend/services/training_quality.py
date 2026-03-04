"""
Training quality monitoring utilities (IDEA 54).
Provides quality metrics calculation, alert detection, and early stopping recommendations.
"""

from __future__ import annotations

from datetime import datetime


def calculate_quality_score_from_loss(
    training_loss: float,
    validation_loss: float | None = None,
    min_loss: float = 0.0,
    max_loss: float = 10.0,
) -> float:
    """
    Estimate quality score from training/validation loss.

    Lower loss = higher quality score.
    Uses inverse relationship: quality = 1.0 - normalized_loss

    Args:
        training_loss: Training loss value
        validation_loss: Optional validation loss (used if available)
        min_loss: Minimum expected loss value
        max_loss: Maximum expected loss value

    Returns:
        Quality score (0.0-1.0)
    """
    # Use validation loss if available, otherwise training loss
    loss = validation_loss if validation_loss is not None else training_loss

    # Normalize loss to 0-1 range
    normalized_loss = max(0.0, min(1.0, (loss - min_loss) / (max_loss - min_loss)))

    # Convert to quality score (inverse relationship)
    quality_score = 1.0 - normalized_loss

    # Clamp to valid range
    return max(0.0, min(1.0, quality_score))


def detect_quality_degradation(
    quality_history: list[dict], current_epoch: int, threshold: float = 0.05
) -> dict | None:
    """
    Detect quality degradation in training.

    Compares current quality with recent average quality.
    Alerts if quality dropped significantly.

    Args:
        quality_history: List of quality metrics dictionaries
        current_epoch: Current epoch number
        threshold: Degradation threshold (0.05 = 5% drop)

    Returns:
        Alert dictionary if degradation detected, None otherwise
    """
    if len(quality_history) < 5:
        return None  # Need at least 5 epochs to detect degradation

    # Get recent quality scores (last 5 epochs before current)
    recent_scores = [
        q.get("quality_score", 0.0)
        for q in quality_history[-5:]
        if q.get("quality_score") is not None
    ]

    if len(recent_scores) < 5:
        return None

    # Calculate average recent quality
    avg_recent_quality = sum(recent_scores) / len(recent_scores)

    # Get current quality
    current_quality = recent_scores[-1]

    # Check if quality dropped significantly
    if current_quality < avg_recent_quality - threshold:
        return {
            "type": "degradation",
            "severity": "warning",
            "message": f"Quality degraded by {((avg_recent_quality - current_quality) * 100):.1f}% at epoch {current_epoch}",
            "epoch": current_epoch,
            "timestamp": datetime.now().isoformat(),
        }

    return None


def detect_quality_plateau(
    quality_history: list[dict],
    current_epoch: int,
    plateau_epochs: int = 10,
    improvement_threshold: float = 0.01,
) -> dict | None:
    """
    Detect quality plateau in training.

    Alerts if quality hasn't improved significantly for several epochs.

    Args:
        quality_history: List of quality metrics dictionaries
        current_epoch: Current epoch number
        plateau_epochs: Number of epochs to check for plateau
        improvement_threshold: Minimum improvement to consider progress

    Returns:
        Alert dictionary if plateau detected, None otherwise
    """
    if len(quality_history) < plateau_epochs:
        return None

    # Get quality scores from recent epochs
    recent_scores = [
        q.get("quality_score", 0.0)
        for q in quality_history[-plateau_epochs:]
        if q.get("quality_score") is not None
    ]

    if len(recent_scores) < plateau_epochs:
        return None

    # Find best quality in recent epochs
    max(recent_scores)
    current_quality = recent_scores[-1]

    # Check if improvement is below threshold
    improvement = current_quality - recent_scores[0]

    if improvement < improvement_threshold:
        return {
            "type": "plateau",
            "severity": "info",
            "message": f"Quality plateau detected - no significant improvement for {plateau_epochs} epochs",
            "epoch": current_epoch,
            "timestamp": datetime.now().isoformat(),
        }

    return None


def detect_overfitting(
    quality_history: list[dict], current_epoch: int, check_epochs: int = 5
) -> dict | None:
    """
    Detect overfitting in training.

    Alerts if validation quality is decreasing while training quality improves.

    Args:
        quality_history: List of quality metrics dictionaries
        current_epoch: Current epoch number
        check_epochs: Number of epochs to check

    Returns:
        Alert dictionary if overfitting detected, None otherwise
    """
    if len(quality_history) < check_epochs:
        return None

    # Get recent metrics
    recent_metrics = quality_history[-check_epochs:]

    # Check if we have validation loss data
    has_validation_loss = any(m.get("validation_loss") is not None for m in recent_metrics)

    if not has_validation_loss:
        return None

    # Get training and validation losses
    training_losses: list[float] = [
        float(m["training_loss"]) for m in recent_metrics if m.get("training_loss") is not None
    ]
    validation_losses: list[float] = [
        float(m["validation_loss"]) for m in recent_metrics if m.get("validation_loss") is not None
    ]

    if len(training_losses) < check_epochs or len(validation_losses) < check_epochs:
        return None

    # Check trends
    training_improving = training_losses[-1] < training_losses[0]
    validation_worsening = validation_losses[-1] > validation_losses[0]

    if training_improving and validation_worsening:
        return {
            "type": "overfitting",
            "severity": "warning",
            "message": f"Overfitting detected - training improving but validation worsening at epoch {current_epoch}",
            "epoch": current_epoch,
            "timestamp": datetime.now().isoformat(),
        }

    return None


def recommend_early_stopping(
    quality_history: list[dict],
    current_epoch: int,
    total_epochs: int,
    plateau_epochs: int = 15,
    improvement_threshold: float = 0.01,
) -> dict:
    """
    Recommend early stopping based on quality metrics.

    Args:
        quality_history: List of quality metrics dictionaries
        current_epoch: Current epoch number
        total_epochs: Total epochs planned
        plateau_epochs: Number of epochs to check for plateau
        improvement_threshold: Minimum improvement to consider progress

    Returns:
        Early stopping recommendation dictionary
    """
    should_stop = False
    reason = ""
    confidence = 0.0
    best_epoch = None
    best_metrics = None

    if len(quality_history) < plateau_epochs:
        return {
            "should_stop": False,
            "reason": "Insufficient data for early stopping recommendation",
            "confidence": 0.0,
            "current_epoch": current_epoch,
            "best_epoch": None,
            "best_metrics": None,
        }

    # Find best quality epoch
    best_idx = 0
    best_quality = 0.0

    for i, metrics in enumerate(quality_history):
        quality = metrics.get("quality_score", 0.0) or 0.0
        if quality > best_quality:
            best_quality = quality
            best_idx = i
            best_metrics = metrics

    best_epoch = quality_history[best_idx].get("epoch") if best_epoch is None else best_idx + 1

    # Check for plateau
    recent_scores = [
        q.get("quality_score", 0.0)
        for q in quality_history[-plateau_epochs:]
        if q.get("quality_score") is not None
    ]

    if len(recent_scores) >= plateau_epochs:
        improvement = recent_scores[-1] - recent_scores[0]

        if improvement < improvement_threshold:
            should_stop = True
            reason = f"Quality plateau detected - no improvement for {plateau_epochs} epochs. Best quality at epoch {best_epoch}."
            confidence = 0.8

    # Check if best quality was recent (within last 5 epochs)
    if best_epoch and current_epoch - best_epoch > 5 and not should_stop:
        should_stop = True
        reason = f"Best quality was at epoch {best_epoch}, but quality has declined since then."
        confidence = 0.7

    return {
        "should_stop": should_stop,
        "reason": reason or "Continue training - quality still improving",
        "confidence": confidence,
        "current_epoch": current_epoch,
        "best_epoch": best_epoch,
        "best_metrics": best_metrics,
    }
