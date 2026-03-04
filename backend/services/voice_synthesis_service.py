"""
Voice synthesis service for routes that need to synthesize.

Voice testing route needs synthesize. Synthesis route registers its handler.
"""

from __future__ import annotations

from typing import Any, Awaitable, Callable

_synthesize_handler: Callable[..., Awaitable[Any]] | None = None


def register_synthesize_handler(handler: Callable[..., Awaitable[Any]]) -> None:
    """Register the synthesize handler (called by synthesis route at load)."""
    global _synthesize_handler
    _synthesize_handler = handler


async def synthesize(request: Any, http_request: Any = None, config_service: Any = None) -> Any:
    """Synthesize via registered handler."""
    if _synthesize_handler is None:
        raise RuntimeError(
            "Synthesize handler not registered. Ensure voice/synthesis route is loaded."
        )
    return await _synthesize_handler(request, http_request, config_service)
