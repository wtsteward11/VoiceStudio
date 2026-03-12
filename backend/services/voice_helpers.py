"""
Shared voice helpers for synthesis and clone routes.

Extracted from voice.py to avoid service-to-route dependency.
Used by SynthesisService and voice routes.
"""

from __future__ import annotations

from typing import Any

# Backward-compatible engine aliases
_ENGINE_ID_ALIASES: dict[str, str] = {
    "xtts": "xtts_v2",
}


def normalize_engine_id(engine_id: str) -> str:
    """Normalize engine ID (e.g. xtts -> xtts_v2)."""
    engine_norm = (engine_id or "").strip().lower()
    return _ENGINE_ID_ALIASES.get(engine_norm, engine_norm)


def check_consent_required(profile_id: str, request: Any = None) -> bool:
    """
    Returns True if consent is required for this profile.

    Uses real ownership (owner_user_id) when available.
    """
    from backend.project.management.profile_store import get_profile_store

    current_user_id = "local"
    if request is not None and hasattr(request, "headers"):
        hdr = getattr(request, "headers", None)
        if hdr:
            uid = hdr.get("X-User-ID", "").strip()
            if uid:
                current_user_id = uid

    profile = get_profile_store().get(profile_id)
    if profile is None:
        return True

    owner = profile.get("owner_user_id")
    if owner and owner == current_user_id:
        return False
    return True


def ensure_tts_assets(engine_id: str) -> None:
    """Ensure required TTS assets exist (auto-download when allowed)."""
    from backend.ml.models.model_preflight import (
        PreflightError,
        ensure_piper,
        ensure_xtts,
    )

    try:
        if engine_id in ("xtts", "xtts_v2"):
            ensure_xtts(auto_download=True)
        elif engine_id == "piper":
            ensure_piper(auto_download=True)
    except PreflightError:
        raise  # GAP-007: Let PreflightError propagate; global handler converts to HTTP
