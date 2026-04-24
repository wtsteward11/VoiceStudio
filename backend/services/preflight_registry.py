"""
Registry of engine preflight callables (Slice 29).

Central map from ``engine_id`` to :func:`backend.services.model_preflight` ``ensure_*``
functions so probes and tooling can resolve checks without duplicating long if/elif chains.

Health routes may still wrap individual engines for HTTP-specific error shaping; this
module is the single **discovery** source for which engines have a public ensure API.
"""

from __future__ import annotations

from collections.abc import Callable
from typing import Any


def get_engine_preflight_callables() -> dict[str, Callable[..., dict[str, Any]]]:
    """Return a fresh dict mapping engine_id -> ensure_* callable."""
    from backend.services import model_preflight as mp

    return {
        "xtts_v2": mp.ensure_xtts,
        "piper": mp.ensure_piper,
        "espeak_ng": mp.ensure_espeak_ng,
        "rhvoice": mp.ensure_rhvoice,
        "silero": mp.ensure_silero,
        "chatterbox": mp.ensure_chatterbox,
        "tortoise": mp.ensure_tortoise,
        "openvoice": mp.ensure_openvoice,
        "whisper": mp.ensure_whisper,
        "whisper_cpp": mp.ensure_whisper_cpp,
        "faster_whisper": mp.ensure_faster_whisper,
        "vosk": mp.ensure_vosk,
        "parakeet": mp.ensure_parakeet,
        "gpt_sovits": mp.ensure_sovits,
    }


def run_registered_preflight(
    engine_id: str,
    *,
    auto_download: bool = False,
) -> dict[str, Any] | None:
    """
    Run the registered preflight for ``engine_id`` if one exists.

    Returns ``None`` when no registered checker exists for the id.
    """
    fn = get_engine_preflight_callables().get(engine_id)
    if fn is None:
        return None
    return fn(auto_download=auto_download)
