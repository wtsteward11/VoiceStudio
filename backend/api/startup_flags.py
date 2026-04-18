"""Process-wide startup readiness flags (thread-safe, lightweight imports)."""

from __future__ import annotations

import threading
from typing import Any

_ENGINES_READY = threading.Event()
_BASELINE_DEPS_VALID = True
_BASELINE_DEPS_FAILURES: list[dict[str, str]] = []
_lock = threading.Lock()


def set_engines_ready() -> None:
    """Mark engine manifest load as complete (called from on_startup_heavy)."""

    _ENGINES_READY.set()


def get_engines_ready() -> bool:
    """True after load_all_engines completes successfully in deferred startup."""

    return _ENGINES_READY.is_set()


def set_baseline_deps_result(valid: bool, failures: list[dict[str, str]]) -> None:
    """Record baseline dependency validation outcome (called from on_startup_prepare)."""
    global _BASELINE_DEPS_VALID, _BASELINE_DEPS_FAILURES
    with _lock:
        _BASELINE_DEPS_VALID = valid
        _BASELINE_DEPS_FAILURES = list(failures)


def get_baseline_deps_valid() -> bool:
    """True if all baseline dependencies imported successfully at startup."""
    with _lock:
        return _BASELINE_DEPS_VALID


def get_baseline_deps_failures() -> list[dict[str, str]]:
    """List of baseline dependency import failures, each with 'name' and 'reason'."""
    with _lock:
        return list(_BASELINE_DEPS_FAILURES)
