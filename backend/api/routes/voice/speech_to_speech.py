# mypy: disable-error-code="untyped-decorator"
# SAFETY: FastAPI router decorators lack complete type stubs; route handlers are correctly typed.
"""Thin routes for speech-to-speech conversion (GAP-051)."""

from __future__ import annotations

from fastapi import Depends, HTTPException, Request

from backend.api.auth import User
from backend.api.dependencies import require_synthesis_clearance
from backend.api.middleware.auth_middleware import require_user_role_for_trust_surfaces
from backend.api.models_additional import SpeechToSpeechRequest, SpeechToSpeechResponse
from backend.core.exceptions import ServiceError
from backend.services.speech_to_speech_service import SpeechToSpeechService

from ._shared import router


def _raise_service_error(exc: ServiceError) -> None:
    raise HTTPException(status_code=exc.status_code, detail=exc.detail) from exc


@router.post("/sts/convert", response_model=SpeechToSpeechResponse)
async def speech_to_speech_convert(
    req: SpeechToSpeechRequest,
    http_request: Request,
    user: User = Depends(require_user_role_for_trust_surfaces),
    _policy: None = Depends(require_synthesis_clearance),
) -> SpeechToSpeechResponse:
    """Convert source speech to target voice via RVC; delegates to SpeechToSpeechService."""
    corr = getattr(http_request.state, "correlation_id", None)
    auth_subj = user.user_id
    try:
        return await SpeechToSpeechService.convert(
            req,
            auth_subject=auth_subj,
            correlation_id=corr,
            user_role=user.role.value,
        )
    except ServiceError as e:
        _raise_service_error(e)
