"""
GAP-053: Single resolver for TTS engine fallback priority (user settings → YAML → defaults).
"""

from __future__ import annotations

import logging
import re
from typing import Any

logger = logging.getLogger(__name__)

_ENGINE_ID_RE = re.compile(r"^[a-z0-9_-]+$")

# Hardcoded last-resort TTS chain (manifest ids); must match router defaults.
DEFAULT_TTS_PRIORITY: list[str] = ["xtts_v2", "openvoice", "piper", "espeak"]


def is_valid_engine_priority_token(engine_id: str) -> bool:
    """True if *engine_id* matches allowed characters for persisted priority entries."""
    return bool(isinstance(engine_id, str) and _ENGINE_ID_RE.fullmatch(engine_id))


def resolve_engine_priority(task_type: str = "tts") -> tuple[list[str], str]:
    """
    Returns (ordered_engine_ids, source) where source is 'user' | 'yaml' | 'default'.

    Does not filter to installed engines; consumers walk the list against valid_engines.
    """
    # Local import: settings.load_settings must stay lazy for router import safety.
    from backend.api.routes.settings import load_settings

    loaded = load_settings()
    if (
        loaded
        and loaded.engine
        and loaded.engine.engine_priority_order
    ):
        return list(loaded.engine.engine_priority_order), "user"

    chain: list[str] = []
    if task_type == "tts":
        try:
            from backend.platform.config.unified_config import get_config

            cfg = get_config()
            chain = list(cfg.get_fallback_chain(task_type) or [])
        except Exception as e:
            logger.debug("YAML fallback chain unavailable for %s: %s", task_type, e)

    if chain:
        return chain, "yaml"

    if task_type == "tts":
        return list(DEFAULT_TTS_PRIORITY), "default"

    return [], "default"


def list_valid_engine_ids() -> list[str]:
    """Best-effort list of loaded engine ids from the shared router (may be empty in CI)."""
    try:
        from backend.services.engine_shared import (
            ENGINE_AVAILABLE,
            _ensure_engine_router,
            engine_router,
        )

        _ensure_engine_router()
        if ENGINE_AVAILABLE and engine_router:
            out = engine_router.list_engines()
            return list(out) if out else []
    except Exception as e:
        logger.debug("list_valid_engine_ids failed: %s", e)
    return []


def build_effective_engine_priority_payload(task_type: str = "tts") -> dict[str, Any]:
    """
    Build the JSON body for GET /api/settings/engine-priority/effective.

    - order: resolved priority before filtering to installed engines
    - available: entries from *order* that exist in the router list, in order
    - skipped: entries from *order* missing from the router list
    """
    order, source = resolve_engine_priority(task_type)
    valid = list_valid_engine_ids()
    valid_set = set(valid)
    available = [eid for eid in order if eid in valid_set]
    skipped = [eid for eid in order if eid not in valid_set]
    return {
        "task_type": task_type,
        "source": source,
        "order": order,
        "available": available,
        "skipped": skipped,
        "registered_engines": valid,
    }
