"""Voice A/B testing routes - side-by-side synthesis comparison."""
# SAFETY: FastAPI router decorator lacks complete type stubs. Per STRICT_MYPY_BURNDOWN_SUBPLAN.
# mypy: disable-error-code="untyped-decorator"

from __future__ import annotations

import logging
import os
import uuid
from typing import Any

from fastapi import HTTPException, Request

from ...models_additional import (
    ABTestRequest,
    ABTestResponse,
    ABTestResult,
    VoiceSynthesizeRequest,
    VoiceSynthesizeResponse,
)
from . import _shared
from ._shared import router

logger = logging.getLogger(__name__)


@router.post("/ab-test", response_model=ABTestResponse)
async def ab_test(request: ABTestRequest, http_request: Request) -> ABTestResponse:
    """
    A/B test two synthesis configurations side-by-side.

    Implements IDEA 46: A/B Testing Interface for Quality Comparison.

    Synthesizes the same text with two different configurations (engines, emotions, etc.)
    and returns both results with quality metrics for comparison.
    """
    if not _shared.ENGINE_AVAILABLE or not _shared.engine_router:
        raise HTTPException(status_code=503, detail="Engine router not available")

    try:
        # Get profile
        from ..profiles import _profiles

        if request.profile_id not in _profiles:
            raise HTTPException(status_code=404, detail=f"Profile {request.profile_id} not found")

        profile = _profiles[request.profile_id]
        reference_audio_path = profile.get("reference_audio_url")
        if not reference_audio_path or not os.path.exists(reference_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Profile {request.profile_id} has no valid reference audio",
            )

        # Helper function to synthesize one sample
        async def synthesize_sample(
            engine_name: str, emotion: str | None, enhance_quality: bool, label: str
        ) -> ABTestResult:
            """Synthesize one sample for A/B test."""
            from backend.services.voice_synthesis_service import synthesize

            # Create synthesis request
            synth_req = VoiceSynthesizeRequest(
                engine=engine_name,
                profile_id=request.profile_id,
                text=request.text,
                language=request.language,
                emotion=emotion,
                enhance_quality=enhance_quality,
            )

            # Synthesize using existing endpoint logic
            result = await synthesize(synth_req, http_request, config_service=None)

            return ABTestResult(
                sample_label=label,
                audio_id=result.audio_id,
                audio_url=result.audio_url,
                duration=result.duration,
                engine=engine_name,
                emotion=emotion,
                quality_score=result.quality_score,
                quality_metrics=result.quality_metrics,
            )

        # Synthesize both samples
        sample_a = await synthesize_sample(
            request.engine_a, request.emotion_a, request.enhance_quality_a, "A"
        )

        sample_b = await synthesize_sample(
            request.engine_b, request.emotion_b, request.enhance_quality_b, "B"
        )

        # Build comparison metrics
        comparison = {}
        if sample_a.quality_metrics and sample_b.quality_metrics:
            qa = sample_a.quality_metrics
            qb = sample_b.quality_metrics

            comparison = {
                "mos_score": {
                    "a": qa.mos_score,
                    "b": qb.mos_score,
                    "winner": "A" if (qa.mos_score or 0) > (qb.mos_score or 0) else "B",
                },
                "similarity": {
                    "a": qa.similarity,
                    "b": qb.similarity,
                    "winner": ("A" if (qa.similarity or 0) > (qb.similarity or 0) else "B"),
                },
                "naturalness": {
                    "a": qa.naturalness,
                    "b": qb.naturalness,
                    "winner": ("A" if (qa.naturalness or 0) > (qb.naturalness or 0) else "B"),
                },
                "snr_db": {
                    "a": qa.snr_db,
                    "b": qb.snr_db,
                    "winner": "A" if (qa.snr_db or 0) > (qb.snr_db or 0) else "B",
                },
                "artifact_score": {
                    "a": qa.artifact_score,
                    "b": qb.artifact_score,
                    "winner": (
                        "A" if (qa.artifact_score or 0) < (qb.artifact_score or 0) else "B"
                    ),  # Lower is better
                },
                "overall_winner": (
                    "A" if (sample_a.quality_score or 0) > (sample_b.quality_score or 0) else "B"
                ),
            }

        # Generate test ID
        test_id = str(uuid.uuid4())

        return ABTestResponse(
            sample_a=sample_a, sample_b=sample_b, comparison=comparison, test_id=test_id
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"A/B test failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"A/B test failed: {e!s}")
