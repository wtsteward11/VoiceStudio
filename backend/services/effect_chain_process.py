"""
GAP-039: single in-memory authority for interactive effect-chain process/preview.

Export bake (GAP-029) uses ``apply_chain_model_to_audio`` via ``timeline_effect_bake``;
this module supports the REST process routes with explicit bypass semantics.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np
from fastapi import HTTPException

logger = logging.getLogger(__name__)


def process_chain_in_memory(
    chain: Any,
    audio: np.ndarray,
    sample_rate: int,
    *,
    bypass_chain: bool,
    strict_no_enabled: bool,
) -> tuple[np.ndarray, bool]:
    """
    Apply enabled chain effects to *audio* or return a dry copy.

    Returns ``(processed_audio, passthrough)``. When ``passthrough`` is True, callers
    should return the **input** artifact id (no new file). When False, persist
    *processed_audio* as a new artifact.

    :param bypass_chain: When True, never run DSP; passthrough is True.
    :param strict_no_enabled: When True and not *bypass_chain* and the chain has no
        enabled effects, raise HTTP 400 (legacy body-route contract).
    """
    if bypass_chain:
        return np.asarray(audio, dtype=np.float32).copy(), True

    enabled = [e for e in chain.effects if e.enabled]
    if not enabled:
        if strict_no_enabled:
            raise HTTPException(
                status_code=400,
                detail="Effect chain has no enabled effects",
            )
        return np.asarray(audio, dtype=np.float32).copy(), True

    from backend.api.routes.effects import apply_chain_model_to_audio

    out = apply_chain_model_to_audio(chain, audio, sample_rate)
    return out, False
