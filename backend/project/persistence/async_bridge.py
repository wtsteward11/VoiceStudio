"""
Bridge sync call sites (FastAPI thread pool, TrackStore) to async SQLite repositories.

Calling asyncio.run() is safe when no event loop is running (default for sync def routes).
"""

from __future__ import annotations

import asyncio
from typing import Coroutine, TypeVar

T = TypeVar("T")


def run_isolated_async(coro: Coroutine[None, None, T]) -> T:
    """
    Run an async coroutine from a synchronous context without a running loop.

    Raises:
        RuntimeError: If called from a running event loop (use an async route instead).
    """
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        return asyncio.run(coro)
    raise RuntimeError(
        "SQLite persistence was invoked while an asyncio event loop is running; "
        "use an async route or refactor the caller."
    )
