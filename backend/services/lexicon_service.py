"""
Lexicon service for phoneme estimation.

Prosody route needs estimate_phonemes. Lexicon route registers its handler.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Awaitable, Callable

_estimate_handler: Callable[..., Awaitable[Any]] | None = None


@dataclass
class PhonemeEstimateRequest:
    """Request to estimate phonemes."""

    word: str | None = None
    audio_id: str | None = None
    language: str = "en"


def register_estimate_phonemes_handler(handler: Callable[..., Awaitable[Any]]) -> None:
    """Register the estimate_phonemes handler (called by lexicon route at load)."""
    global _estimate_handler
    _estimate_handler = handler


async def estimate_phonemes(request: PhonemeEstimateRequest) -> Any:
    """Estimate phonemes via registered handler."""
    if _estimate_handler is None:
        raise RuntimeError(
            "Estimate phonemes handler not registered. Ensure lexicon route is loaded."
        )
    return await _estimate_handler(request)
