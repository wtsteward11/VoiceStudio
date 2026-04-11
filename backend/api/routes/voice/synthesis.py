from __future__ import annotations

from fastapi import Depends, HTTPException, Request

from backend.api.dependencies import require_synthesis_clearance
from backend.api.deps import EngineConfigServiceDep
from backend.api.models_additional import (
    LongFormSynthesisRequest,
    LongFormSynthesisResponse,
    MultiPassSynthesisRequest,
    MultiPassSynthesisResponse,
    VoiceSynthesizeRequest,
    VoiceSynthesizeResponse,
)
from backend.core.exceptions import ServiceError
from backend.services.synthesis_service import SynthesisService

from ._shared import router


def _raise_synthesis_service_error(exc: ServiceError) -> None:
    raise HTTPException(
        status_code=exc.status_code,
        detail=exc.detail,
    ) from exc


async def synthesize(
    req: VoiceSynthesizeRequest,
    request: Request,
    _policy: None = Depends(require_synthesis_clearance),
    config_service: EngineConfigServiceDep | None = None,
) -> VoiceSynthesizeResponse:
    """
    Synthesize audio from text using a voice profile.

    Delegates to SynthesisService (canonical synthesis authority).
    """
    try:
        return await SynthesisService.synthesize(
            req,
            request,
            config_service,
        )
    except ServiceError as e:
        _raise_synthesis_service_error(e)


async def synthesize_multipass(
    req: MultiPassSynthesisRequest,
    request: Request,
    _policy: None = Depends(require_synthesis_clearance),
) -> MultiPassSynthesisResponse:
    """Multi-pass synthesis; delegates to SynthesisService."""
    try:
        return await SynthesisService.synthesize_multipass(
            req,
            request,
            None,
        )
    except ServiceError as e:
        _raise_synthesis_service_error(e)


async def synthesize_long_form(
    req: LongFormSynthesisRequest,
    request: Request,
    _policy: None = Depends(require_synthesis_clearance),
    config_service: EngineConfigServiceDep | None = None,
) -> LongFormSynthesisResponse:
    """Long-form chunked synthesis; delegates to SynthesisService."""
    try:
        return await SynthesisService.synthesize_long_form(
            req,
            request,
            config_service,
        )
    except ServiceError as e:
        _raise_synthesis_service_error(e)


async def synthesize_with_style(
    request: Request,
    text: str,
    profile_id: str,
    engine: str = "openvoice",
    language: str = "en",
    emotion: str | None = None,
    accent: str | None = None,
    rhythm: float | None = None,
    pauses: str | None = None,
    pitch_shift: float | None = None,
    pitch_variance: float | None = None,
    energy: float | None = None,
    enhance_quality: bool = True,
    calculate_quality: bool = True,
    _policy: None = Depends(require_synthesis_clearance),
) -> VoiceSynthesizeResponse:
    """Style synthesis; delegates to SynthesisService."""
    try:
        return await SynthesisService.synthesize_with_style(
            _request=request,
            text=text,
            profile_id=profile_id,
            engine=engine,
            language=language,
            emotion=emotion,
            accent=accent,
            rhythm=rhythm,
            pauses=pauses,
            pitch_shift=pitch_shift,
            pitch_variance=pitch_variance,
            energy=energy,
            enhance_quality=enhance_quality,
            calculate_quality=calculate_quality,
        )
    except ServiceError as e:
        _raise_synthesis_service_error(e)


async def synthesize_cross_lingual(
    request: Request,
    text: str,
    profile_id: str,
    source_language: str = "en",
    target_language: str = "es",
    engine: str = "openvoice",
    enhance_quality: bool = True,
    calculate_quality: bool = True,
    _policy: None = Depends(require_synthesis_clearance),
) -> VoiceSynthesizeResponse:
    """Cross-lingual synthesis; delegates to SynthesisService."""
    try:
        return await SynthesisService.synthesize_cross_lingual(
            _request=request,
            text=text,
            profile_id=profile_id,
            source_language=source_language,
            target_language=target_language,
            engine=engine,
            enhance_quality=enhance_quality,
            calculate_quality=calculate_quality,
        )
    except ServiceError as e:
        _raise_synthesis_service_error(e)


router.add_api_route(
    "/synthesize",
    synthesize,
    methods=["POST"],
    response_model=VoiceSynthesizeResponse,
)
router.add_api_route(
    "/synthesize/multipass",
    synthesize_multipass,
    methods=["POST"],
    response_model=MultiPassSynthesisResponse,
)
router.add_api_route(
    "/synthesize/long-form",
    synthesize_long_form,
    methods=["POST"],
    response_model=LongFormSynthesisResponse,
)
router.add_api_route(
    "/synthesize/style",
    synthesize_with_style,
    methods=["POST"],
    response_model=VoiceSynthesizeResponse,
)
router.add_api_route(
    "/synthesize/cross-lingual",
    synthesize_cross_lingual,
    methods=["POST"],
    response_model=VoiceSynthesizeResponse,
)
