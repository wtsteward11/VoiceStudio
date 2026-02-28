"""
API Key Persistence Store.

Phase 7 Sprint 2: Persist API keys to JSON file so they survive restart.
Uses encrypted storage; keys are encrypted before persistence.
"""

from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

DEFAULT_STORE_PATH = Path.home() / ".voicestudio" / "data" / "api_keys.json"


def _get_store_path() -> Path:
    """Get API key store path from env or default."""
    import os

    path = os.environ.get("VOICESTUDIO_API_KEYS_PATH")
    return Path(path) if path else DEFAULT_STORE_PATH


def load_api_keys() -> dict[str, dict[str, Any]]:
    """
    Load API keys from persistent store.

    Returns:
        Dict mapping key_id to key data (key_value is encrypted).
    """
    path = _get_store_path()
    if not path.exists():
        return {}

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return data.get("keys", {})
    except (json.JSONDecodeError, OSError) as e:
        logger.warning("Failed to load API keys from %s: %s", path, e)
        return {}


def save_api_keys(keys: dict[str, dict[str, Any]]) -> bool:
    """
    Save API keys to persistent store.

    Args:
        keys: Dict mapping key_id to key data.

    Returns:
        True if saved successfully.
    """
    path = _get_store_path()
    path.parent.mkdir(parents=True, exist_ok=True)

    try:
        data = {"keys": keys, "version": 1}
        path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        return True
    except OSError as e:
        logger.error("Failed to save API keys to %s: %s", path, e)
        return False
