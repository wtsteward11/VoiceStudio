"""
API v3 - Unified Synthesis Endpoints.

Task 3.4.1: Breaking change - unified synthesis endpoint.
Replaces separate TTS and cloning endpoints from v1/v2.
Phase 4A: Updated to use StandardResponse envelope format.
"""

from __future__ import annotations

import logging
from enum import Enum

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field

from backend.ml.models.engine_service import IEngineService, get_engine_service

from .models import StandardResponse, success_response

router = APIRouter(prefix="/synthesis", tags=["synthesis"])
logger = logging.getLogger(__name__)


def _get_request_id(request: Request) -> str | None:
    """Extract request ID from request state if available."""
    return getattr(request.state, "request_id", None)


# --- Enums ---


class SynthesisMode(str, Enum):
    """Synthesis modes."""

    STANDARD = "standard"  # Standard TTS
    CLONING = "cloning"  # Voice cloning
    CONVERSION = "conversion"  # Voice conversion


class OutputFormat(str, Enum):
    """Audio output formats."""

    WAV = "wav"
    MP3 = "mp3"
    FLAC = "flac"
    OGG = "ogg"


# --- Request/Response Models ---


class SynthesisRequest(BaseModel):
    """Unified synthesis request."""

    text: str = Field(..., min_length=1, max_length=10000, description="Text to synthesize")
    engine_id: str = Field(default="xtts_v2", description="Engine to use")
    mode: SynthesisMode = Field(default=SynthesisMode.STANDARD, description="Synthesis mode")
    voice_id: str | None = Field(None, description="Voice ID for standard mode")
    reference_audio_url: str | None = Field(None, description="URL to reference audio for cloning")
    language: str = Field(default="en", description="Language code")
    output_format: OutputFormat = Field(default=OutputFormat.WAV, description="Output format")
    sample_rate: int = Field(default=24000, ge=8000, le=48000, description="Sample rate in Hz")
    streaming: bool = Field(default=False, description="Enable streaming response")

    # Quality parameters
    speed: float = Field(default=1.0, ge=0.5, le=2.0, description="Speech speed multiplier")
    pitch: float = Field(default=1.0, ge=0.5, le=2.0, description="Pitch multiplier")

    class Config:
        json_schema_extra = {
            "example": {
                "text": "Hello, this is a test synthesis.",
                "engine_id": "xtts_v2",
                "mode": "standard",
                "voice_id": "default",
                "language": "en",
                "output_format": "wav",
                "sample_rate": 24000,
                "streaming": False,
            }
        }


class SynthesisResponse(BaseModel):
    """Synthesis response metadata."""

    job_id: str
    status: str
    audio_url: str | None = None
    duration_seconds: float | None = None
    characters_processed: int
    engine_used: str
    processing_time_ms: float | None = None

    class Config:
        json_schema_extra = {
            "example": {
                "job_id": "syn_abc123",
                "status": "completed",
                "audio_url": "/api/v3/synthesis/syn_abc123/audio",
                "duration_seconds": 2.5,
                "characters_processed": 35,
                "engine_used": "xtts_v2",
                "processing_time_ms": 450.5,
            }
        }


class BatchSynthesisRequest(BaseModel):
    """Batch synthesis request."""

    items: list[SynthesisRequest] = Field(..., min_length=1, max_length=100)
    parallel: bool = Field(default=True, description="Process items in parallel")


class BatchSynthesisResponse(BaseModel):
    """Batch synthesis response."""

    batch_id: str
    total_items: int
    completed: int
    failed: int
    results: list[SynthesisResponse]


# --- Endpoints ---


@router.post(
    "",
    response_model=StandardResponse[SynthesisResponse],
    summary="Synthesize speech",
    description="Unified speech synthesis endpoint supporting TTS, cloning, and conversion.",
)
async def synthesize(
    http_request: Request,
    request: SynthesisRequest,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """
    Synthesize speech from text with StandardResponse envelope.

    Supports multiple modes:
    - **standard**: Use a pre-existing voice
    - **cloning**: Clone voice from reference audio
    - **conversion**: Convert existing audio to target voice
    """
    import time
    import uuid

    job_id = f"syn_{uuid.uuid4().hex[:12]}"
    start_time = time.time()

    try:
        if request.mode == SynthesisMode.CLONING:
            if not request.reference_audio_url:
                raise HTTPException(
                    status_code=400, detail="reference_audio_url is required for cloning mode"
                )

            result = engine_service.clone_voice(
                engine_id=request.engine_id,
                reference_audio=request.reference_audio_url,
                text=request.text,
                language=request.language,
            )
        else:
            result = engine_service.synthesize(
                engine_id=request.engine_id,
                text=request.text,
                voice_id=request.voice_id,
                language=request.language,
            )

        processing_time = (time.time() - start_time) * 1000

        synthesis_result = SynthesisResponse(
            job_id=job_id,
            status="completed",
            audio_url=f"/api/v3/synthesis/{job_id}/audio",
            duration_seconds=result.get("duration"),
            characters_processed=len(request.text),
            engine_used=request.engine_id,
            processing_time_ms=processing_time,
        )

        return success_response(
            data=synthesis_result,
            message="Synthesis completed successfully",
            request_id=_get_request_id(http_request),
            duration_ms=int(processing_time),
        )

    except Exception as e:
        logger.error(f"Synthesis failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post(
    "/stream",
    summary="Stream synthesis",
    description="Stream synthesized audio in real-time chunks.",
)
async def synthesize_stream(
    request: SynthesisRequest,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Stream synthesized audio."""

    async def audio_generator():
        """Generate audio chunks."""
        try:
            result = engine_service.synthesize(
                engine_id=request.engine_id,
                text=request.text,
                voice_id=request.voice_id,
                language=request.language,
            )

            audio_data = result.get("audio_data", b"")
            chunk_size = 4096

            for i in range(0, len(audio_data), chunk_size):
                yield audio_data[i : i + chunk_size]

        except Exception as e:
            logger.error(f"Streaming synthesis failed: {e}")
            raise

    media_type = {
        OutputFormat.WAV: "audio/wav",
        OutputFormat.MP3: "audio/mpeg",
        OutputFormat.FLAC: "audio/flac",
        OutputFormat.OGG: "audio/ogg",
    }.get(request.output_format, "audio/wav")

    return StreamingResponse(
        audio_generator(),
        media_type=media_type,
        headers={"X-Streaming": "true"},
    )


@router.post(
    "/with-reference",
    response_model=StandardResponse[SynthesisResponse],
    summary="Synthesize with reference audio upload",
    description="Clone voice from uploaded reference audio.",
)
async def synthesize_with_reference(
    http_request: Request,
    text: str = Form(..., description="Text to synthesize"),
    reference_audio: UploadFile = File(..., description="Reference audio file"),
    engine_id: str = Form(default="xtts_v2"),
    language: str = Form(default="en"),
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Synthesize with uploaded reference audio with StandardResponse envelope."""
    import os
    import tempfile
    import time
    import uuid

    job_id = f"syn_{uuid.uuid4().hex[:12]}"
    start_time = time.time()

    # Save uploaded file temporarily
    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
        content = await reference_audio.read()
        tmp.write(content)
        tmp_path = tmp.name

    try:
        result = engine_service.clone_voice(
            engine_id=engine_id,
            reference_audio=tmp_path,
            text=text,
            language=language,
        )

        processing_time = (time.time() - start_time) * 1000

        synthesis_result = SynthesisResponse(
            job_id=job_id,
            status="completed",
            audio_url=f"/api/v3/synthesis/{job_id}/audio",
            duration_seconds=result.get("duration"),
            characters_processed=len(text),
            engine_used=engine_id,
            processing_time_ms=processing_time,
        )

        return success_response(
            data=synthesis_result,
            message="Synthesis with reference completed",
            request_id=_get_request_id(http_request),
            duration_ms=int(processing_time),
        )

    finally:
        # Clean up temp file
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)


@router.post(
    "/batch",
    response_model=StandardResponse[BatchSynthesisResponse],
    summary="Batch synthesis",
    description="Process multiple synthesis requests in a batch.",
)
async def batch_synthesize(
    http_request: Request,
    request: BatchSynthesisRequest,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Process batch synthesis requests with StandardResponse envelope."""
    import time
    import uuid

    start_time = time.time()
    batch_id = f"batch_{uuid.uuid4().hex[:12]}"
    results = []
    completed = 0
    failed = 0

    for item in request.items:
        try:
            # Call internal synthesis logic (returns StandardResponse, extract data)
            response = await synthesize(http_request, item, engine_service)
            if response.data:
                results.append(response.data)
                completed += 1
            else:
                failed += 1
        except Exception:
            failed += 1
            results.append(
                SynthesisResponse(
                    job_id=f"syn_{uuid.uuid4().hex[:12]}",
                    status="failed",
                    characters_processed=len(item.text),
                    engine_used=item.engine_id,
                )
            )

    processing_time = (time.time() - start_time) * 1000

    batch_result = BatchSynthesisResponse(
        batch_id=batch_id,
        total_items=len(request.items),
        completed=completed,
        failed=failed,
        results=results,
    )

    return success_response(
        data=batch_result,
        message=f"Batch synthesis completed: {completed}/{len(request.items)} successful",
        request_id=_get_request_id(http_request),
        duration_ms=int(processing_time),
    )
