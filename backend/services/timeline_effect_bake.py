"""Apply project effect chains to timeline mixdown exports (GAP-029 export authority)."""

from __future__ import annotations

import logging

import numpy as np
from fastapi import HTTPException

logger = logging.getLogger(__name__)


def apply_timeline_export_effect_chain(
    *,
    chain_id: str,
    project_id: str,
    audio: np.ndarray,
    sample_rate: int,
) -> np.ndarray:
    """
    Validate chain ownership and apply enabled effects to export audio.

    Raises HTTPException on client errors (never returns silent dry audio when invoked).
    """
    from backend.api.routes.effects import (
        _get_chain,
        _validate_chain_id,
        _validate_project_id,
        apply_chain_model_to_audio,
    )

    _validate_chain_id(chain_id)
    _validate_project_id(project_id)

    chain = _get_chain(chain_id)
    if chain is None:
        logger.warning("Effect chain not found for export: %s", chain_id)
        raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")
    if chain.project_id != project_id:
        logger.warning(
            "Effect chain %s does not belong to project %s",
            chain_id,
            project_id,
        )
        raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

    return apply_chain_model_to_audio(chain, audio, sample_rate)
