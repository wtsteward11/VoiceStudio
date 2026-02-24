"""
Translation Integration Routes

Phase 10.3: Expose TranslationService via REST API.
Provides endpoints for Whisper transcription, translation,
and timing-preserving subtitle generation.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/translation", tags=["translation"])


# --- Request/Response Models ---


class TranscriptionRequest(BaseModel):
    """Request for audio transcription."""

    audio_id: str = Field(..., description="Audio file ID")
    model: str = Field(
        "base", description="Whisper model: tiny, base, small, medium, large, large-v3"
    )
    language: str | None = Field(None, description="Source language (auto-detect if not specified)")
    word_timestamps: bool = Field(True, description="Include word-level timestamps")


class TranscriptionSegment(BaseModel):
    """A transcription segment."""

    segment_id: str
    start_time: float
    end_time: float
    text: str
    language: str
    confidence: float


class TranscriptionResponse(BaseModel):
    """Response for transcription."""

    project_id: str
    segments: list[TranscriptionSegment]
    detected_language: str
    total_duration: float


class TranslationRequest(BaseModel):
    """Request for text translation."""

    project_id: str = Field(..., description="Translation project ID")
    target_language: str = Field(..., description="Target language code")
    preserve_timing: bool = Field(True, description="Adjust translations for timing")


class TranslatedSegment(BaseModel):
    """A translated segment."""

    segment_id: str
    original_text: str
    translated_text: str
    start_time: float
    end_time: float
    timing_adjusted: bool


class TranslationResponse(BaseModel):
    """Response for translation."""

    project_id: str
    source_language: str
    target_language: str
    segments: list[TranslatedSegment]


class ProjectCreateRequest(BaseModel):
    """Request to create a translation project."""

    name: str = Field(..., description="Project name")
    audio_id: str = Field(..., description="Source audio file ID")
    target_language: str = Field(..., description="Target language code")
    source_language: str | None = Field(None, description="Source language (auto-detect if None)")
    transcription_model: str = Field("base", description="Whisper model")
    translation_provider: str = Field("local_nllb", description="Translation provider")


class ProjectInfo(BaseModel):
    """Translation project information."""

    project_id: str
    name: str
    source_language: str | None
    target_language: str
    status: str
    progress: float
    segment_count: int


class SubtitleExportRequest(BaseModel):
    """Request for subtitle export."""

    project_id: str
    format: str = Field("srt", description="Subtitle format: srt, vtt, ass")
    use_translation: bool = Field(True, description="Use translated text if available")


class SubtitleExportResponse(BaseModel):
    """Response for subtitle export."""

    file_path: str
    format: str
    segment_count: int


class LanguageInfo(BaseModel):
    """Language information."""

    code: str
    name: str


# --- API Endpoints ---


@router.post("/projects", response_model=ProjectInfo)
async def create_project(request: ProjectCreateRequest):
    """
    Create a new translation project.

    Phase 10.3.1: Whisper integration.

    Args:
        request: Project creation parameters

    Returns:
        Created project info
    """
    try:
        from backend.voice.translation.translation_service import (
            TranscriptionModel,
            TranslationProvider,
            get_translation_service,
        )

        service = get_translation_service()

        # Map model string to enum
        model_map = {
            "tiny": TranscriptionModel.WHISPER_TINY,
            "base": TranscriptionModel.WHISPER_BASE,
            "small": TranscriptionModel.WHISPER_SMALL,
            "medium": TranscriptionModel.WHISPER_MEDIUM,
            "large": TranscriptionModel.WHISPER_LARGE,
            "large-v3": TranscriptionModel.WHISPER_LARGE_V3,
            "vosk": TranscriptionModel.VOSK,
        }

        provider_map = {
            "local_nllb": TranslationProvider.LOCAL_NLLB,
            "local_opus": TranslationProvider.LOCAL_OPUS,
            "libretranslate": TranslationProvider.LIBRETRANSLATE,
            "argos": TranslationProvider.ARGOS,
        }

        model = model_map.get(request.transcription_model, TranscriptionModel.WHISPER_BASE)
        provider = provider_map.get(request.translation_provider, TranslationProvider.LOCAL_NLLB)

        project = await service.create_project(
            name=request.name,
            source_audio_path=request.audio_id,  # Will be resolved by service
            target_language=request.target_language,
            source_language=request.source_language,
            transcription_model=model,
            translation_provider=provider,
        )

        return ProjectInfo(
            project_id=project.project_id,
            name=project.name,
            source_language=project.source_language,
            target_language=project.target_language,
            status=project.status,
            progress=project.progress,
            segment_count=len(project.transcribed_segments),
        )

    except Exception as e:
        logger.error(f"Failed to create translation project: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create project: {e!s}") from e


@router.post("/projects/{project_id}/transcribe", response_model=TranscriptionResponse)
async def transcribe_project(project_id: str, word_timestamps: bool = True):
    """
    Transcribe audio for a project.

    Phase 10.3.1: Whisper integration with word timestamps.

    Args:
        project_id: Project ID
        word_timestamps: Include word-level timestamps

    Returns:
        Transcription segments
    """
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        segments = await service.transcribe(project_id, word_timestamps=word_timestamps)

        if not segments:
            project = service.get_project(project_id)
            if not project:
                raise HTTPException(status_code=404, detail=f"Project '{project_id}' not found")
            if project.status == "failed":
                raise HTTPException(status_code=500, detail="Transcription failed")

        project = service.get_project(project_id)
        if project is None:
            raise HTTPException(status_code=404, detail="Project not found")

        return TranscriptionResponse(
            project_id=project_id,
            segments=[
                TranscriptionSegment(
                    segment_id=s.segment_id,
                    start_time=s.start_time,
                    end_time=s.end_time,
                    text=s.text,
                    language=s.language,
                    confidence=s.confidence,
                )
                for s in segments
            ],
            detected_language=project.source_language or "unknown",
            total_duration=segments[-1].end_time if segments else 0.0,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Transcription failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Transcription failed: {e!s}") from e


@router.post("/projects/{project_id}/translate", response_model=TranslationResponse)
async def translate_project(project_id: str, preserve_timing: bool = True):
    """
    Translate transcribed segments.

    Phase 10.3.2: Translation API hookup.
    Phase 10.3.3: Timing preservation.

    Args:
        project_id: Project ID
        preserve_timing: Adjust translations for timing

    Returns:
        Translated segments
    """
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        segments = await service.translate(project_id, preserve_timing=preserve_timing)

        project = service.get_project(project_id)
        if not project:
            raise HTTPException(status_code=404, detail=f"Project '{project_id}' not found")

        if not segments and project.status == "failed":
            raise HTTPException(status_code=500, detail="Translation failed")

        return TranslationResponse(
            project_id=project_id,
            source_language=project.source_language or "unknown",
            target_language=project.target_language,
            segments=[
                TranslatedSegment(
                    segment_id=s.segment_id,
                    original_text=s.original_text,
                    translated_text=s.translated_text,
                    start_time=s.start_time,
                    end_time=s.end_time,
                    timing_adjusted=s.timing_adjusted,
                )
                for s in segments
            ],
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Translation failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Translation failed: {e!s}") from e


@router.post("/projects/{project_id}/export-subtitles", response_model=SubtitleExportResponse)
async def export_subtitles(project_id: str, request: SubtitleExportRequest):
    """
    Export subtitles in various formats.

    Args:
        project_id: Project ID
        request: Export parameters

    Returns:
        Path to exported subtitle file
    """
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()

        if request.project_id != project_id:
            raise HTTPException(status_code=400, detail="Project ID mismatch")

        file_path = await service.export_subtitles(
            project_id=project_id,
            format=request.format,
            use_translation=request.use_translation,
        )

        if not file_path:
            raise HTTPException(status_code=500, detail="Subtitle export failed")

        project = service.get_project(project_id)
        if project is None:
            raise HTTPException(status_code=404, detail="Project not found")
        segment_count = len(
            project.translated_segments if request.use_translation else project.transcribed_segments
        )

        return SubtitleExportResponse(
            file_path=file_path,
            format=request.format,
            segment_count=segment_count,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Subtitle export failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Subtitle export failed: {e!s}") from e


@router.get("/projects", response_model=list[ProjectInfo])
async def list_projects():
    """List all translation projects."""
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        projects = service.list_projects()

        return [
            ProjectInfo(
                project_id=p.project_id,
                name=p.name,
                source_language=p.source_language,
                target_language=p.target_language,
                status=p.status,
                progress=p.progress,
                segment_count=len(p.transcribed_segments),
            )
            for p in projects
        ]

    except Exception as e:
        logger.error(f"Failed to list projects: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list projects: {e!s}") from e


@router.get("/projects/{project_id}", response_model=ProjectInfo)
async def get_project(project_id: str):
    """Get translation project details."""
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        project = service.get_project(project_id)

        if not project:
            raise HTTPException(status_code=404, detail=f"Project '{project_id}' not found")

        return ProjectInfo(
            project_id=project.project_id,
            name=project.name,
            source_language=project.source_language,
            target_language=project.target_language,
            status=project.status,
            progress=project.progress,
            segment_count=len(project.transcribed_segments),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get project: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get project: {e!s}") from e


@router.delete("/projects/{project_id}")
async def delete_project(project_id: str):
    """Delete a translation project."""
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        success = service.delete_project(project_id)

        if not success:
            raise HTTPException(status_code=404, detail=f"Project '{project_id}' not found")

        return {"success": True, "message": f"Project '{project_id}' deleted"}

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete project: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete project: {e!s}") from e


@router.get("/languages", response_model=list[LanguageInfo])
async def list_languages():
    """List supported languages."""
    try:
        from backend.voice.translation.translation_service import get_translation_service

        service = get_translation_service()
        languages = service.get_supported_languages()

        return [LanguageInfo(code=code, name=name) for code, name in languages.items()]

    except Exception as e:
        logger.error(f"Failed to list languages: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list languages: {e!s}") from e


@router.get("/models")
async def list_transcription_models():
    """List available transcription models."""
    return {
        "models": [
            {"id": "tiny", "name": "Whisper Tiny", "size": "~39MB", "speed": "fastest"},
            {"id": "base", "name": "Whisper Base", "size": "~74MB", "speed": "fast"},
            {"id": "small", "name": "Whisper Small", "size": "~244MB", "speed": "medium"},
            {"id": "medium", "name": "Whisper Medium", "size": "~769MB", "speed": "slow"},
            {"id": "large", "name": "Whisper Large", "size": "~1.5GB", "speed": "slowest"},
            {"id": "large-v3", "name": "Whisper Large V3", "size": "~1.5GB", "speed": "slowest"},
            {"id": "vosk", "name": "Vosk (Offline)", "size": "~50MB", "speed": "fast"},
        ]
    }


@router.get("/providers")
async def list_translation_providers():
    """List available translation providers."""
    return {
        "providers": [
            {
                "id": "local_nllb",
                "name": "Meta NLLB (Local)",
                "description": "No Language Left Behind - 200+ languages",
                "offline": True,
            },
            {
                "id": "local_opus",
                "name": "OPUS-MT (Local)",
                "description": "Helsinki NLP translation models",
                "offline": True,
            },
            {
                "id": "libretranslate",
                "name": "LibreTranslate",
                "description": "Self-hosted translation server",
                "offline": True,
            },
            {
                "id": "argos",
                "name": "Argos Translate",
                "description": "Offline translation library",
                "offline": True,
            },
        ]
    }
