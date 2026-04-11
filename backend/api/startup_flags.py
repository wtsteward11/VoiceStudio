"""Process-wide startup readiness flags (thread-safe, lightweight imports)."""

from __future__ import annotations

import threading

_ENGINES_READY = threading.Event()


def set_engines_ready() -> None:
    """Mark engine manifest load as complete (called from on_startup_heavy)."""

    _ENGINES_READY.set()


def get_engines_ready() -> bool:
    """True after load_all_engines completes successfully in deferred startup."""

    return _ENGINES_READY.is_set()
