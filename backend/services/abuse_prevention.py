"""
Abuse prevention policy (Item 25: Trust and Safety).

- Rate limiting on clone attempts (wire into existing rate limiting)
- Similarity threshold warnings on export
- High-risk content flag for manual review prompt in UI
"""

from __future__ import annotations

import logging
import time
from collections import defaultdict

logger = logging.getLogger(__name__)

# In-memory rate limit: clone attempts per key (e.g. IP or user) per window
_clone_attempts: dict[str, list[float]] = defaultdict(list)
CLONE_RATE_LIMIT = 30  # max clone attempts
CLONE_RATE_WINDOW_SEC = 3600  # per hour

# Synthesis rate limit (separate from clone for correct metrics)
_synthesis_attempts: dict[str, list[float]] = defaultdict(list)
SYNTHESIS_RATE_LIMIT = 120  # max synthesis requests per hour
SYNTHESIS_RATE_WINDOW_SEC = 3600  # per hour


def check_synthesis_rate_limit(key: str) -> tuple[bool, str]:
    """
    Check if synthesis is allowed under rate limit.
    Returns (allowed, message).
    """
    now = time.time()
    window_start = now - SYNTHESIS_RATE_WINDOW_SEC
    attempts = _synthesis_attempts[key]
    attempts[:] = [t for t in attempts if t > window_start]
    if len(attempts) >= SYNTHESIS_RATE_LIMIT:
        return False, (
            f"Synthesis rate limit exceeded ({SYNTHESIS_RATE_LIMIT} per hour). "
            "Please try again later."
        )
    return True, ""


def record_synthesis_attempt(key: str) -> None:
    """Record a synthesis attempt for rate limiting."""
    now = time.time()
    _synthesis_attempts[key][:] = [
        t for t in _synthesis_attempts[key] if t > now - SYNTHESIS_RATE_WINDOW_SEC
    ]
    _synthesis_attempts[key].append(now)


def check_clone_rate_limit(key: str) -> tuple[bool, str]:
    """
    Check if clone is allowed under rate limit.
    Returns (allowed, message).
    """
    now = time.time()
    window_start = now - CLONE_RATE_WINDOW_SEC
    attempts = _clone_attempts[key]
    attempts[:] = [t for t in attempts if t > window_start]
    if len(attempts) >= CLONE_RATE_LIMIT:
        return False, (
            f"Clone rate limit exceeded ({CLONE_RATE_LIMIT} per hour). "
            "Please try again later."
        )
    attempts.append(now)
    return True, ""


def record_clone_attempt(key: str) -> None:
    """Record a clone attempt for rate limiting."""
    now = time.time()
    _clone_attempts[key][:] = [t for t in _clone_attempts[key] if t > now - CLONE_RATE_WINDOW_SEC]
    _clone_attempts[key].append(now)


def similarity_threshold_warning(similarity: float, threshold: float = 0.95) -> str | None:
    """
    Return a warning message if similarity is above threshold (possible deepfake risk).
    For UI to show before export.
    """
    if similarity >= threshold:
        return (
            f"High similarity score ({similarity:.2f}). "
            "Ensure you have consent for this voice. Export may require review."
        )
    return None


def is_high_risk_content(similarity: float, duration_seconds: float) -> bool:
    """
    Flag content that may require manual review.
    High similarity + long duration = higher risk.
    """
    if similarity >= 0.92 and duration_seconds > 60:
        return True
    if similarity >= 0.97:
        return True
    return False
