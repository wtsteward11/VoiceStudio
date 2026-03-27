"""
API v3 - Voice Management Endpoints.

Task 3.4.1: Voice profile management with cursor-based pagination.
Phase 4A: Updated to use StandardResponse envelope format.
"""

from __future__ import annotations

import logging
from pathlib import Path

from fastapi import APIRouter, HTTPException, Query, Request
from fastapi.responses import FileResponse
from pydantic import BaseModel, ConfigDict, Field

from .models import (
    StandardResponse,
    paginated_response,
    success_response,
)

router = APIRouter(prefix="/voices", tags=["voices"])
logger = logging.getLogger(__name__)


def _get_request_id(request: Request) -> str | None:
    """Extract request ID from request state if available."""
    return getattr(request.state, "request_id", None)


# Sample cache directory
VOICE_SAMPLES_DIR = Path("data/voice_samples")
SAMPLE_TEXT = "Hello, this is a sample of my voice."


# --- Request/Response Models ---


class VoiceProfile(BaseModel):
    """Voice profile information."""

    id: str
    name: str
    description: str | None = None
    language: str = "en"
    gender: str | None = None
    age_group: str | None = None
    style: str | None = None
    sample_url: str | None = None
    is_custom: bool = False
    engine_id: str | None = None
    created_at: str | None = None

    model_config = ConfigDict(
        json_schema_extra={
            "example": {
                "id": "voice_abc123",
                "name": "Professional Male",
                "description": "Clear, professional male voice",
                "language": "en",
                "gender": "male",
                "age_group": "adult",
                "style": "professional",
                "is_custom": False,
            }
        }
    )


class VoiceListResponse(BaseModel):
    """Paginated voice list response (cursor-based)."""

    voices: list[VoiceProfile]
    cursor: str | None = None
    has_more: bool = False
    total_count: int | None = None


class CreateVoiceRequest(BaseModel):
    """Request to create a custom voice."""

    name: str = Field(..., min_length=1, max_length=100)
    description: str | None = Field(None, max_length=500)
    language: str = Field(default="en")
    reference_audio_url: str | None = None


class CreateVoiceResponse(BaseModel):
    """Response after creating a voice."""

    id: str
    name: str
    status: str
    training_job_id: str | None = None


# --- Endpoints ---


@router.get(
    "",
    response_model=StandardResponse[list[VoiceProfile]],
    summary="List voices",
    description="Get available voices with cursor-based pagination.",
)
async def list_voices(
    request: Request,
    engine_id: str | None = Query(None, description="Filter by engine"),
    language: str | None = Query(None, description="Filter by language"),
    gender: str | None = Query(None, description="Filter by gender"),
    custom_only: bool = Query(False, description="Only show custom voices"),
    cursor: str | None = Query(None, description="Pagination cursor"),
    limit: int = Query(20, ge=1, le=100, description="Maximum results"),
):
    """List available voices with StandardResponse envelope."""
    # Mock implementation - would query actual voice database
    voices = [
        VoiceProfile(
            id="default",
            name="Default Voice",
            language="en",
            gender="neutral",
            is_custom=False,
        ),
        VoiceProfile(
            id="professional_male",
            name="Professional Male",
            language="en",
            gender="male",
            style="professional",
            is_custom=False,
        ),
        VoiceProfile(
            id="warm_female",
            name="Warm Female",
            language="en",
            gender="female",
            style="warm",
            is_custom=False,
        ),
    ]

    # Apply filters
    if language:
        voices = [v for v in voices if v.language == language]
    if gender:
        voices = [v for v in voices if v.gender == gender]
    if custom_only:
        voices = [v for v in voices if v.is_custom]

    return paginated_response(
        data=voices[:limit],
        has_more=len(voices) > limit,
        total_count=len(voices),
        page_size=limit,
        request_id=_get_request_id(request),
    )


@router.get(
    "/{voice_id}",
    response_model=StandardResponse[VoiceProfile],
    summary="Get voice details",
    description="Get detailed information about a specific voice.",
)
async def get_voice(request: Request, voice_id: str):
    """Get voice by ID with StandardResponse envelope."""
    # Mock implementation
    if voice_id == "default":
        voice = VoiceProfile(
            id="default",
            name="Default Voice",
            language="en",
            gender="neutral",
            is_custom=False,
        )
        return success_response(
            data=voice,
            message="Voice profile retrieved",
            request_id=_get_request_id(request),
        )

    raise HTTPException(status_code=404, detail=f"Voice not found: {voice_id}")


@router.post(
    "",
    response_model=StandardResponse[CreateVoiceResponse],
    summary="Create custom voice",
    description="Create a new custom voice profile.",
)
async def create_voice(request: Request, create_request: CreateVoiceRequest):
    """Create a custom voice with StandardResponse envelope."""
    import uuid

    voice_id = f"voice_{uuid.uuid4().hex[:12]}"

    result = CreateVoiceResponse(
        id=voice_id,
        name=create_request.name,
        status="created",
        training_job_id=None,
    )

    return success_response(
        data=result,
        message="Voice profile created successfully",
        request_id=_get_request_id(request),
    )


class DeleteVoiceResult(BaseModel):
    """Result of voice deletion."""

    voice_id: str
    deleted: bool = True


@router.delete(
    "/{voice_id}",
    response_model=StandardResponse[DeleteVoiceResult],
    summary="Delete voice",
    description="Delete a custom voice profile.",
)
async def delete_voice(request: Request, voice_id: str):
    """Delete a voice with StandardResponse envelope."""
    # Would verify ownership and delete
    result = DeleteVoiceResult(voice_id=voice_id, deleted=True)
    return success_response(
        data=result,
        message="Voice profile deleted",
        request_id=_get_request_id(request),
    )


class VoiceSampleResponse(BaseModel):
    """Response containing voice sample info."""

    voice_id: str
    sample_url: str
    duration_seconds: float
    format: str = "wav"


@router.get(
    "/{voice_id}/sample",
    summary="Get voice sample",
    description="Get a sample audio of the voice.",
    responses={
        200: {
            "description": "Audio sample file",
            "content": {"audio/wav": {}},
        },
        404: {"description": "Voice or sample not found"},
    },
)
async def get_voice_sample(
    voice_id: str,
    generate: bool = Query(False, description="Generate sample if not cached"),
):
    """
    Get voice sample audio.

    Returns a pre-recorded or generated sample audio file for the specified voice.
    Set `generate=true` to create a new sample if one doesn't exist.
    """
    # Ensure samples directory exists
    VOICE_SAMPLES_DIR.mkdir(parents=True, exist_ok=True)

    # Check for cached sample
    sample_path = VOICE_SAMPLES_DIR / f"{voice_id}.wav"

    if sample_path.exists():
        logger.info(f"Returning cached voice sample: {voice_id}")
        return FileResponse(
            path=str(sample_path),
            media_type="audio/wav",
            filename=f"{voice_id}_sample.wav",
        )

    # Generate sample if requested
    if generate:
        try:
            generated_path = await _generate_voice_sample(voice_id, sample_path)
            if generated_path and generated_path.exists():
                return FileResponse(
                    path=str(generated_path),
                    media_type="audio/wav",
                    filename=f"{voice_id}_sample.wav",
                )
        except Exception as e:
            logger.error(f"Failed to generate voice sample: {e}")
            raise HTTPException(
                status_code=500,
                detail=f"Failed to generate voice sample: {e!s}",
            ) from e

    # No sample available
    raise HTTPException(
        status_code=404,
        detail=f"No sample for voice '{voice_id}'. Set generate=true.",
    )


async def _generate_voice_sample(voice_id: str, output_path: Path) -> Path | None:
    """
    Generate a voice sample using the synthesis engine.

    Returns the path to the generated sample, or None if generation failed.
    """
    try:
        # Import synthesis service
        from backend.ml.models.engine_service import get_engine_service

        engine_service = get_engine_service()

        # Generate sample audio
        result = engine_service.synthesize(
            engine_id="default",
            text=SAMPLE_TEXT,
            voice_id=voice_id,
            output_path=str(output_path),
        )

        if getattr(result, "success", False) and output_path.exists():
            logger.info(f"Generated voice sample for {voice_id}")
            return output_path

        return None

    except ImportError:
        logger.warning("Engine service not available for sample generation")
        return None
    except Exception as e:
        logger.error(f"Error generating voice sample: {e}")
        raise
