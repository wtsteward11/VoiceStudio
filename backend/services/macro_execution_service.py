"""
Macro execution service for scheduled tasks and routes.

Provides execute_macro without route self-import. Macros route registers its handler.
"""

from __future__ import annotations

from typing import Any, Callable

_execute_handler: Callable[[str], dict[str, bool]] | None = None


def register_execute_macro_handler(handler: Callable[[str], dict[str, bool]]) -> None:
    """Register the execute_macro handler (called by macros route at load)."""
    global _execute_handler
    _execute_handler = handler


def execute_macro(macro_id: str) -> dict[str, bool]:
    """Execute a macro via registered handler."""
    if _execute_handler is None:
        raise RuntimeError(
            "Macro execute handler not registered. Ensure macros route is loaded."
        )
    return _execute_handler(macro_id)
