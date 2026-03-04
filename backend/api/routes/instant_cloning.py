"""
Instant Voice Cloning Routes

Phase 9.1: Expose InstantCloningService via REST API.
Provides endpoints for zero-shot cloning, embedding extraction,
instant preview synthesis, and clone quality estimation.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from backend.api.dependencies import require_synthesis_clearance

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/instant-cloning", tags=["instant-cloning"])


def calculate_embedding_quality(
    embedding: Any | None,
    metadata: dict[str, Any],
) -> float:
    """
    Calculate quality score from embedding analysis and audio metrics.

    Quality factors:
    1. Embedding variance - Higher variance indicates more distinctive voice features
    2. Embedding norm - Proper normalization indicates model confidence
    3. Duration - Optimal range 3-10 seconds for speaker encoders
    4. SNR (Signal-to-Noise Ratio) - Cleaner audio produces better embeddings
    5. Clipping - Audio clipping degrades embedding quality

    Returns:
        Quality score between 0.0 and 1.0
    """
    import numpy as np

    # Base score from duration
    duration_seconds = metadata.get("duration_seconds", 0)
    if duration_seconds >= 5.0:
        duration_score = 1.0
    elif duration_seconds >= 3.0:
        duration_score = 0.85
    elif duration_seconds >= 1.0:
        duration_score = 0.7
    else:
        duration_score = 0.5

    # Embedding quality metrics
    embedding_score = 0.8  # Default if no embedding

    if embedding is not None:
        try:
            emb = np.asarray(embedding)

            # Variance score - embeddings should have meaningful variance
            variance = np.var(emb)
            if variance > 0.01:
                variance_score = min(1.0, variance / 0.1)  # Normalize to expected range
            else:
                variance_score = 0.5  # Low variance = less distinctive

            # Norm score - check if embedding is properly normalized
            norm = np.linalg.norm(emb)
            if 0.5 < norm < 2.0:  # Typical range for normalized embeddings
                norm_score = 1.0
            else:
                norm_score = max(0.5, 1.0 - abs(1.0 - float(norm)) * 0.3)

            # Entropy score - measure information content
            # Higher entropy = more information preserved
            abs_emb = np.abs(emb) + 1e-10
            prob = abs_emb / abs_emb.sum()
            entropy = -np.sum(prob * np.log(prob))
            max_entropy = np.log(len(emb))
            entropy_score = min(1.0, entropy / max_entropy) if max_entropy > 0 else 0.5

            # Combine embedding metrics
            embedding_score = 0.4 * variance_score + 0.3 * norm_score + 0.3 * entropy_score

        except Exception as e:
            logger.debug(f"Embedding quality calculation error: {e}")
            embedding_score = 0.7  # Fallback

    # Audio quality from metadata
    audio_score = 0.8  # Default

    snr = metadata.get("snr_db")
    if snr is not None:
        # SNR > 20 dB is good, > 30 dB is excellent
        if snr >= 30:
            audio_score = 1.0
        elif snr >= 20:
            audio_score = 0.9
        elif snr >= 10:
            audio_score = 0.75
        else:
            audio_score = 0.6

    # Clipping penalty
    clipping_ratio = metadata.get("clipping_ratio", 0)
    if clipping_ratio > 0.1:
        audio_score *= 0.7  # Heavy clipping
    elif clipping_ratio > 0.01:
        audio_score *= 0.85  # Some clipping

    # Weighted combination
    quality_score = 0.35 * embedding_score + 0.35 * duration_score + 0.30 * audio_score

    # Clamp to valid range
    return max(0.1, min(1.0, quality_score))


# Audio upload directory - must match audio.py (outside repo)
from backend.config.path_config import get_path

UPLOAD_DIR = str(get_path("audio_uploads"))


def resolve_audio_id_to_path(audio_id: str) -> str:
    """
    Resolve an audio_id to its file path in the uploads directory.

    Args:
        audio_id: UUID of the uploaded audio file

    Returns:
        Full path to the audio file

    Raises:
        HTTPException: If file not found
    """
    if not audio_id:
        raise HTTPException(status_code=400, detail="audio_id is required")

    # Search for file with matching ID prefix
    upload_path = Path(UPLOAD_DIR)
    if not upload_path.exists():
        raise HTTPException(
            status_code=404, detail="Audio uploads directory not found. Upload audio first."
        )

    # Look for files starting with the audio_id
    for ext in [".wav", ".mp3", ".flac", ".m4a", ".ogg", ".aac"]:
        candidate = upload_path / f"{audio_id}{ext}"
        if candidate.exists():
            return str(candidate)

    # Also check if it's a direct path that exists
    if os.path.isabs(audio_id) and os.path.exists(audio_id):
        return audio_id

    raise HTTPException(
        status_code=404,
        detail=f"Audio file with ID '{audio_id}' not found. Please upload the audio first.",
    )


# --- Request/Response Models ---


class ZeroShotCloneRequest(BaseModel):
    """Request for zero-shot voice cloning."""

    audio_id: str = Field(..., description="ID of reference audio (3-10 seconds)")
    text: str = Field(..., description="Text to synthesize")
    language: str = Field("en", description="Language code")


class ZeroShotCloneResponse(BaseModel):
    """Response for zero-shot voice cloning."""

    output_audio_id: str
    embedding_id: str
    quality_score: float
    duration_seconds: float


class EmbeddingExtractionRequest(BaseModel):
    """Request for speaker embedding extraction."""

    audio_id: str = Field(..., description="Reference audio ID")


class EmbeddingExtractionResponse(BaseModel):
    """Response for embedding extraction."""

    embedding_id: str
    embedding_dimension: int
    quality_score: float


class InstantPreviewRequest(BaseModel):
    """Request for instant preview synthesis."""

    embedding_id: str = Field(..., description="Speaker embedding ID")
    text: str = Field(..., description="Text for preview")
    max_duration_seconds: float = Field(5.0, description="Max preview duration")


class InstantPreviewResponse(BaseModel):
    """Response for instant preview."""

    audio_id: str
    duration_seconds: float
    latency_ms: float


class QualityEstimationRequest(BaseModel):
    """Request for clone quality estimation."""

    reference_audio_id: str
    cloned_audio_id: str


class QualityEstimationResponse(BaseModel):
    """Response for quality estimation."""

    overall_score: float
    similarity_score: float
    naturalness_score: float
    intelligibility_score: float
    recommendation: str


# --- API Endpoints ---


@router.post("/zero-shot", response_model=ZeroShotCloneResponse)
async def zero_shot_clone(request: ZeroShotCloneRequest):
    """
    Perform zero-shot voice cloning with minimal reference audio.

    Phase 9.1.1: Clone voices from 3-10 seconds of audio.

    Args:
        request: Zero-shot cloning request with audio_id and text

    Returns:
        Cloned audio with quality metrics
    """
    try:
        # Resolve audio_id to file path
        audio_path = resolve_audio_id_to_path(request.audio_id)
        logger.info(f"Zero-shot clone: audio_id={request.audio_id} -> path={audio_path}")

        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()

        # Use instant_clone method which takes a file path
        result = await service.instant_clone(
            audio_path=audio_path,
            profile_name=f"instant_{request.audio_id[:8]}",
            profile_description=f"Zero-shot clone from {request.audio_id}",
            generate_preview=True,
            engine="xtts",
        )

        if not result.success:
            raise HTTPException(
                status_code=500, detail=result.error_message or "Zero-shot cloning failed"
            )

        return ZeroShotCloneResponse(
            output_audio_id=result.preview_audio_path or result.profile_id or request.audio_id,
            embedding_id=result.profile_id or request.audio_id,
            quality_score=result.quality_estimate.overall_score if result.quality_estimate else 0.0,
            duration_seconds=result.processing_time_ms / 1000.0,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Zero-shot cloning failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Zero-shot cloning failed: {e!s}") from e


@router.post("/extract-embedding", response_model=EmbeddingExtractionResponse)
async def extract_embedding(request: EmbeddingExtractionRequest):
    """
    Extract speaker embedding from reference audio.

    Phase 9.1.2: One-click speaker embedding extraction.

    Args:
        request: Audio ID to extract embedding from

    Returns:
        Embedding ID and quality metrics
    """
    try:
        # Resolve audio_id to file path
        audio_path = resolve_audio_id_to_path(request.audio_id)
        logger.info(f"Extract embedding: audio_id={request.audio_id} -> path={audio_path}")

        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()
        embedding, metadata = await service.extract_speaker_embedding(audio_path)

        if embedding is None:
            raise HTTPException(
                status_code=500, detail=metadata.get("error", "Embedding extraction failed")
            )

        # Calculate quality score from embedding analysis and audio metrics
        quality_score = metadata.get("quality_score")
        if quality_score is None:
            quality_score = calculate_embedding_quality(embedding=embedding, metadata=metadata)

        return EmbeddingExtractionResponse(
            embedding_id=request.audio_id,  # Use audio_id as embedding reference
            embedding_dimension=metadata.get(
                "embedding_dim", len(embedding) if embedding is not None else 256
            ),
            quality_score=quality_score,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Embedding extraction failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Embedding extraction failed: {e!s}") from e


@router.post("/preview", response_model=InstantPreviewResponse)
async def instant_preview(
    request: InstantPreviewRequest,
    _policy: None = Depends(require_synthesis_clearance),
):
    """
    Generate instant preview synthesis.

    Phase 9.1.3: Quick preview with embedded voice.

    Args:
        request: Embedding ID and preview text

    Returns:
        Preview audio ID and latency
    """
    try:
        # Resolve embedding_id (which is audio_id) to file path
        audio_path = resolve_audio_id_to_path(request.embedding_id)
        logger.info(f"Preview: embedding_id={request.embedding_id} -> path={audio_path}")

        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()
        preview_path, metadata = await service.generate_instant_preview(
            audio_path=audio_path,
            preview_text=request.text,
            engine="xtts",
        )

        if preview_path is None:
            raise HTTPException(
                status_code=500, detail=metadata.get("error", "Preview synthesis failed")
            )

        return InstantPreviewResponse(
            audio_id=preview_path,
            duration_seconds=metadata.get("duration_seconds", 3.0),
            latency_ms=metadata.get("processing_time_ms", 0.0),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Instant preview failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Instant preview failed: {e!s}") from e


@router.post("/estimate-quality", response_model=QualityEstimationResponse)
async def estimate_quality(request: QualityEstimationRequest):
    """
    Estimate quality of cloned voice.

    Phase 9.1.4: Clone quality estimation.

    Args:
        request: Reference and cloned audio IDs

    Returns:
        Quality scores and recommendation
    """
    try:
        # Resolve audio_id to file path
        audio_path = resolve_audio_id_to_path(request.reference_audio_id)
        logger.info(f"Estimate quality: audio_id={request.reference_audio_id} -> path={audio_path}")

        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()
        quality_estimate = await service.estimate_clone_quality(audio_path)

        # Generate recommendation based on score
        if quality_estimate.overall_score >= 0.85:
            recommendation = "Excellent quality - ready for professional use"
        elif quality_estimate.overall_score >= 0.70:
            recommendation = "Good quality - suitable for most applications"
        elif quality_estimate.overall_score >= 0.50:
            recommendation = "Acceptable quality - may need improvement for critical use"
        else:
            recommendation = "Low quality - consider re-recording with better audio"

        return QualityEstimationResponse(
            overall_score=quality_estimate.overall_score,
            similarity_score=quality_estimate.voice_clarity,
            naturalness_score=quality_estimate.audio_quality,
            intelligibility_score=1.0 - quality_estimate.noise_level,
            recommendation=recommendation,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Quality estimation failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Quality estimation failed: {e!s}") from e


@router.get("/embeddings")
async def list_embeddings():
    """List all stored speaker embeddings."""
    try:
        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()
        embeddings = service.list_embeddings()

        return {"embeddings": embeddings}

    except Exception as e:
        logger.error(f"Failed to list embeddings: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list embeddings: {e!s}") from e


@router.delete("/embeddings/{embedding_id}")
async def delete_embedding(embedding_id: str):
    """Delete a stored speaker embedding."""
    try:
        from backend.voice.cloning.instant_cloning_service import get_instant_cloning_service

        service = get_instant_cloning_service()
        success = service.delete_embedding(embedding_id)

        if not success:
            raise HTTPException(status_code=404, detail=f"Embedding '{embedding_id}' not found")

        return {"success": True, "message": f"Embedding '{embedding_id}' deleted"}

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete embedding: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete embedding: {e!s}") from e
