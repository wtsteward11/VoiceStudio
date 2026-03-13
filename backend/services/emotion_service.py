"""
Emotion analysis service for style transfer.

Style transfer needs emotion analyze. Emotion route registers its handler.
"""

from __future__ import annotations

from typing import Any, Awaitable, Callable, cast

_analyze_handler: Callable[..., Awaitable[Any]] | None = None


def register_emotion_analyze_handler(handler: Callable[..., Awaitable[Any]]) -> None:
    """Register the emotion analyze handler (called by emotion route at load)."""
    global _analyze_handler
    _analyze_handler = handler


async def analyze(req: dict[str, Any]) -> dict[str, Any]:
    """Analyze emotion in audio via registered handler."""
    if _analyze_handler is None:
        raise RuntimeError(
            "Emotion analyze handler not registered. Ensure emotion route is loaded."
        )
    return cast(dict[str, Any], await _analyze_handler(req))
