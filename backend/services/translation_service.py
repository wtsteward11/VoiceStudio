"""
Translation service for routes that need translation without route-to-route imports.

The multilingual route registers its handler at load; other routes call via this service.
"""

from __future__ import annotations

from typing import Any


class TranslationRequest:
    """Request for text translation."""

    def __init__(
        self,
        text: str,
        source_language: str,
        target_language: str,
    ):
        self.text = text
        self.source_language = source_language
        self.target_language = target_language


_translate_handler: Any = None


def register_translate_handler(handler: Any) -> None:
    """Register the translate handler (called by multilingual route at load)."""
    global _translate_handler
    _translate_handler = handler


async def translate_text(request: TranslationRequest) -> Any:
    """Execute translation via registered handler."""
    if _translate_handler is None:
        raise RuntimeError(
            "Translate handler not registered. Ensure multilingual route is loaded."
        )
    return await _translate_handler(request)
