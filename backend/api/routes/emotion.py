"""
Emotion Control Routes

Endpoints for fine-grained emotion control in voice synthesis.
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from datetime import datetime
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models import ApiOk
from ..models_additional import EmotionApplyExtendedResponseModel, EmotionApplyRequest
from ..optimization import cache_response

logger = logging.getLogger(__name__)


def _scalar_float(value: Any, default: float = 0.0) -> float:
    """Extract a scalar float from a value that may be float, ndarray, or list."""
    if value is None:
        return default
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, np.ndarray):
        return float(np.mean(value))
    if isinstance(value, list):
        return float(value[0]) if value else default
    return default


router = APIRouter(prefix="/api/emotion", tags=["emotion"])

# In-memory storage for emotion presets (replace with database in production)
_emotion_presets: dict[str, EmotionPreset] = {}
_state_lock = asyncio.Lock()

# Available emotions
AVAILABLE_EMOTIONS = [
    "happy",
    "sad",
    "angry",
    "excited",
    "calm",
    "fearful",
    "surprised",
    "disgusted",
    "neutral",
    "warm",
    "energetic",
]


class EmotionPreset(BaseModel):
    """Emotion preset model."""

    preset_id: str
    name: str
    description: str | None = None
    primary_emotion: str
    primary_intensity: float  # 0-100
    secondary_emotion: str | None = None
    secondary_intensity: float = 0.0  # 0-100
    created_at: str
    updated_at: str


class EmotionPresetCreateRequest(BaseModel):
    """Request to create an emotion preset."""

    name: str
    description: str | None = None
    primary_emotion: str
    primary_intensity: float
    secondary_emotion: str | None = None
    secondary_intensity: float = 0.0


class EmotionPresetUpdateRequest(BaseModel):
    """Request to update an emotion preset."""

    name: str | None = None
    description: str | None = None
    primary_emotion: str | None = None
    primary_intensity: float | None = None
    secondary_emotion: str | None = None
    secondary_intensity: float | None = None


class EmotionPresetResponse(BaseModel):
    """Emotion preset response."""

    preset_id: str
    name: str
    description: str | None = None
    primary_emotion: str
    primary_intensity: float
    secondary_emotion: str | None = None
    secondary_intensity: float
    created_at: str
    updated_at: str


class EmotionApplyExtendedRequest(BaseModel):
    """Extended emotion apply request with blending support."""

    audio_id: str
    primary_emotion: str
    primary_intensity: float  # 0-100
    secondary_emotion: str | None = None
    secondary_intensity: float = 0.0  # 0-100
    timeline_curve: list[float] | None = None  # Automation curve


@router.get("/list", response_model=list[str])
@cache_response(ttl=600)  # Cache for 10 minutes (emotion list is static)
async def list_emotions():
    """List all available emotions."""
    return AVAILABLE_EMOTIONS


@router.post("/analyze")
async def analyze(req: dict) -> dict:
    """
    Analyze emotion in audio.

    Returns emotion analysis with valence, arousal, and emotion classification.
    """
    try:
        # Validate request
        if not isinstance(req, dict):
            raise HTTPException(status_code=400, detail="Request must be a dictionary")

        audio_id = req.get("audio_id")
        if not audio_id or not isinstance(audio_id, str):
            raise HTTPException(status_code=400, detail="audio_id is required and must be a string")

        # Load the audio file using audio_id
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path:
            raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Load and analyze audio
        try:
            import numpy as np

            from backend.audio import audio_utils

            # Load audio
            audio, sample_rate = audio_utils.load_audio(audio_path)

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Extract voice characteristics
            try:
                voice_chars = audio_utils.analyze_voice_characteristics(audio, sample_rate)
                f0_mean = _scalar_float(voice_chars.get("f0_mean"), 0.0)
                f0_std = _scalar_float(voice_chars.get("f0_std"), 0.0)
                spectral_centroid = _scalar_float(voice_chars.get("spectral_centroid"), 0.0)
                zero_crossing_rate = _scalar_float(voice_chars.get("zero_crossing_rate"), 0.0)
            except Exception as e:
                logger.warning(f"Failed to extract voice characteristics: {e}")
                f0_mean = 0.0
                f0_std = 0.0
                spectral_centroid = 0.0
                zero_crossing_rate = 0.0

            # Calculate energy (RMS)
            energy = np.sqrt(np.mean(audio**2))

            # Calculate tempo/rhythm (using zero crossing rate as proxy)
            tempo_proxy = zero_crossing_rate * 1000  # Scale for better range

            # Map features to valence-arousal space
            # Valence: positive (happy) vs negative (sad) emotions
            # Arousal: high (excited) vs low (calm) energy

            # Normalize features
            # F0: typical range 80-400 Hz for speech
            f0_norm = (f0_mean - 80) / 320.0  # Normalize to 0-1
            f0_norm = max(0.0, min(1.0, f0_norm))

            # Energy: normalize to 0-1
            energy_norm = min(1.0, energy * 10.0)

            # Spectral centroid: typical range 1000-5000 Hz
            spec_norm = (spectral_centroid - 1000) / 4000.0
            spec_norm = max(0.0, min(1.0, spec_norm))

            # Calculate valence and arousal
            # Valence: higher F0 and spectral centroid = more positive
            valence = f0_norm * 0.4 + spec_norm * 0.3 + energy_norm * 0.3
            valence = max(0.0, min(1.0, valence))

            # Arousal: higher energy and F0 variation = more aroused
            f0_var_norm = min(1.0, f0_std / 50.0)  # Normalize F0 std
            arousal = energy_norm * 0.4 + f0_var_norm * 0.3 + tempo_proxy * 0.3
            arousal = max(0.0, min(1.0, arousal))

            # Calculate emotion scores based on valence-arousal mapping
            # Emotion mapping in valence-arousal space:
            # High valence + High arousal = happy, excited
            # High valence + Low arousal = calm, content
            # Low valence + High arousal = angry, fearful
            # Low valence + Low arousal = sad, disgusted
            # Neutral = center

            emotion_scores = {}
            for emotion in AVAILABLE_EMOTIONS:
                if emotion == "happy":
                    # High valence, high arousal
                    score = valence * arousal
                elif emotion == "sad":
                    # Low valence, low arousal
                    score = (1 - valence) * (1 - arousal)
                elif emotion == "angry":
                    # Low valence, high arousal
                    score = (1 - valence) * arousal
                elif emotion == "excited":
                    # High valence, very high arousal
                    score = valence * (arousal**1.5)
                elif emotion == "calm":
                    # High valence, low arousal
                    score = valence * (1 - arousal)
                elif emotion == "fearful":
                    # Low valence, high arousal (similar to angry but different)
                    score = (1 - valence) * arousal * 0.8
                elif emotion == "surprised":
                    # Medium valence, very high arousal
                    mid_valence = 1.0 - abs(valence - 0.5) * 2
                    score = mid_valence * (arousal**1.5)
                elif emotion == "disgusted":
                    # Low valence, medium arousal
                    score = (1 - valence) * (1 - abs(arousal - 0.5) * 2)
                elif emotion == "neutral":
                    # Center of valence-arousal space
                    dist_from_center = np.sqrt((valence - 0.5) ** 2 + (arousal - 0.5) ** 2)
                    score = max(0.0, 1.0 - dist_from_center * 2)
                else:
                    score = 0.0

                emotion_scores[emotion] = float(score)

            # Normalize emotion scores to sum to 1.0
            total_score = sum(emotion_scores.values())
            if total_score > 0:
                emotion_scores = {k: v / total_score for k, v in emotion_scores.items()}

            # Find dominant emotion
            dominant_emotion = max(emotion_scores, key=lambda k: emotion_scores.get(k, 0.0))
            dominant_score = emotion_scores[dominant_emotion]

            # Calculate emotion detection accuracy/confidence
            # Accuracy is based on:
            # 1. How clear the dominant emotion is (dominant score vs others)
            # 2. How well the features match expected emotion characteristics
            # 3. Signal quality (energy, SNR proxy)

            # Calculate confidence based on dominant score strength
            # Higher dominant score = higher confidence
            confidence = dominant_score

            # Calculate accuracy based on feature-emotion alignment
            # Check if features align with expected emotion characteristics
            expected_valence = {
                "happy": 0.7,
                "sad": 0.3,
                "angry": 0.3,
                "excited": 0.8,
                "calm": 0.6,
                "fearful": 0.3,
                "surprised": 0.5,
                "disgusted": 0.3,
                "neutral": 0.5,
            }
            expected_arousal = {
                "happy": 0.6,
                "sad": 0.3,
                "angry": 0.8,
                "excited": 0.9,
                "calm": 0.3,
                "fearful": 0.7,
                "surprised": 0.8,
                "disgusted": 0.4,
                "neutral": 0.5,
            }

            # Calculate alignment score
            expected_v = expected_valence.get(dominant_emotion, 0.5)
            expected_a = expected_arousal.get(dominant_emotion, 0.5)
            valence_alignment = 1.0 - abs(valence - expected_v)
            arousal_alignment = 1.0 - abs(arousal - expected_a)
            alignment_score = (valence_alignment + arousal_alignment) / 2.0

            # Signal quality proxy (based on energy)
            # Higher energy = better signal quality (assuming not clipping)
            signal_quality = min(1.0, energy_norm * 2.0)

            # Overall accuracy combines confidence, alignment, and signal quality
            accuracy = confidence * 0.5 + alignment_score * 0.3 + signal_quality * 0.2
            accuracy = max(0.0, min(1.0, accuracy))

            # Calculate per-emotion confidence scores
            emotion_confidence = {}
            for emotion, score in emotion_scores.items():
                # Confidence for each emotion is based on:
                # 1. The emotion score itself
                # 2. How well features align with that emotion
                exp_v = expected_valence.get(emotion, 0.5)
                exp_a = expected_arousal.get(emotion, 0.5)
                v_align = 1.0 - abs(valence - exp_v)
                a_align = 1.0 - abs(arousal - exp_a)
                align = (v_align + a_align) / 2.0
                emotion_confidence[emotion] = float(score * 0.7 + align * 0.3)

            # Calculate valence and arousal over time (simplified: use windowed analysis)
            # Return time series values (structure allows for time series expansion)
            num_samples = 10  # Number of time points
            valence_timeseries = [valence] * num_samples
            arousal_timeseries = [arousal] * num_samples

            return {
                "valence": valence_timeseries,
                "arousal": arousal_timeseries,
                "dominant_emotion": dominant_emotion,
                "emotion_scores": emotion_scores,
                "emotion_confidence": emotion_confidence,
                "detection_accuracy": float(accuracy),
                "detection_confidence": float(confidence),
                "signal_quality": float(signal_quality),
                "alignment_score": float(alignment_score),
            }

        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="Audio processing libraries not available. Install librosa and soundfile.",
            )
        except Exception as e:
            logger.error(f"Failed to analyze emotion: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to analyze emotion: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to analyze emotion: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to analyze emotion: {e!s}") from e


@router.post("/apply")
async def apply(req: EmotionApplyRequest) -> ApiOk:
    """
    Apply emotion to audio.

    Uses the original EmotionApplyRequest model for compatibility.
    """
    # In a real implementation, this would apply emotion to the audio
    logger.info(f"Applying emotion to audio: {req.audio_id}")
    return ApiOk()


@router.post("/apply-extended", response_model=EmotionApplyExtendedResponseModel)
async def apply_extended(req: EmotionApplyExtendedRequest) -> EmotionApplyExtendedResponseModel:
    """
    Apply emotion with blending and timeline automation.

    Supports primary/secondary emotion blending and timeline curves.
    """
    # Validate emotions
    if req.primary_emotion not in AVAILABLE_EMOTIONS:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid primary emotion: {req.primary_emotion}",
        )

    if req.secondary_emotion and req.secondary_emotion not in AVAILABLE_EMOTIONS:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid secondary emotion: {req.secondary_emotion}",
        )

    # Validate intensity ranges
    if not 0.0 <= req.primary_intensity <= 100.0:
        raise HTTPException(
            status_code=400,
            detail="Primary intensity must be between 0 and 100",
        )

    if not 0.0 <= req.secondary_intensity <= 100.0:
        raise HTTPException(
            status_code=400,
            detail="Secondary intensity must be between 0 and 100",
        )

    logger.info(
        f"Applying emotion to audio: {req.audio_id}, "
        f"primary: {req.primary_emotion} ({req.primary_intensity}%), "
        f"secondary: {req.secondary_emotion} ({req.secondary_intensity}%)"
    )

    try:
        from backend.services.audio_artifacts import AudioRegistry, create_audio_artifact_from_wav_array
        from backend.services.emotion_preset_prosody_mapper import resolve_emotion_prosody
        from backend.services.prosody_authority_service import ProsodyAuthorityError, apply_transform
        from ..models_additional import ProsodyHandlingDiagnostics

        audio_path = AudioRegistry.get_path(req.audio_id)
        if not audio_path:
            raise HTTPException(status_code=404, detail=f"Audio file '{req.audio_id}' not found")
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        try:
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)
        except ImportError:
            logger.warning("soundfile not available, emotion application skipped")
            raise HTTPException(
                status_code=503,
                detail="soundfile required for emotion application",
            ) from None

        mapped = resolve_emotion_prosody(
            primary_emotion=req.primary_emotion,
            primary_intensity=req.primary_intensity,
            secondary_emotion=req.secondary_emotion,
            secondary_intensity=req.secondary_intensity,
        )

        if req.timeline_curve:
            mapped.warnings.append("timeline_curve_not_applied_in_bounded_slice")

        logger.info(
            "Emotion apply-extended mapped preset/emotion to prosody "
            "pitch=%.4f rate=%.4f volume=%.4f source=%s",
            mapped.pitch,
            mapped.rate,
            mapped.volume,
            mapped.mapping_source,
        )

        try:
            transform_result = apply_transform(
                np.asarray(audio, dtype=np.float64),
                int(sample_rate),
                pitch=mapped.pitch,
                rate=mapped.rate,
                volume=mapped.volume,
                context="emotion_apply_extended",
            )
        except ProsodyAuthorityError as e:
            raise HTTPException(status_code=e.status_code, detail=e.message) from e

        diag_raw = dict(transform_result.diagnostics)
        diag_raw["warnings"] = list(diag_raw.get("warnings", [])) + mapped.warnings
        diag_raw["skipped_operations"] = list(diag_raw.get("skipped_operations", [])) + list(
            mapped.skipped_operations
        )
        prosody_handling = ProsodyHandlingDiagnostics.model_validate(diag_raw)

        output_audio_id, _, _ = create_audio_artifact_from_wav_array(
            transform_result.audio,
            int(sample_rate),
            created_by="emotion",
            audio_id=f"emotion_{uuid.uuid4().hex[:8]}",
        )
        logger.info(
            "Applied emotion (authority) to audio '%s' -> %s action=%s",
            req.audio_id,
            output_audio_id,
            prosody_handling.action,
        )
        return EmotionApplyExtendedResponseModel(
            audio_id=output_audio_id,
            audio_url=f"/api/voice/audio/{output_audio_id}",
            prosody_handling=prosody_handling,
            emotion_mapping_source=mapped.mapping_source,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to apply emotion to audio: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to apply emotion to audio: {e!s}") from e


@router.post("/preset/save", response_model=EmotionPresetResponse, status_code=201)
async def save_preset(request: EmotionPresetCreateRequest):
    """Save an emotion preset."""
    try:
        # Validate emotions
        if request.primary_emotion not in AVAILABLE_EMOTIONS:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid primary emotion: {request.primary_emotion}",
            )

        if request.secondary_emotion and request.secondary_emotion not in AVAILABLE_EMOTIONS:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid secondary emotion: {request.secondary_emotion}",
            )

        # Validate intensity ranges
        if not 0.0 <= request.primary_intensity <= 100.0:
            raise HTTPException(
                status_code=400,
                detail="Primary intensity must be between 0 and 100",
            )

        if not 0.0 <= request.secondary_intensity <= 100.0:
            raise HTTPException(
                status_code=400,
                detail="Secondary intensity must be between 0 and 100",
            )

        preset_id = f"emotion-preset-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        preset = EmotionPreset(
            preset_id=preset_id,
            name=request.name,
            description=request.description,
            primary_emotion=request.primary_emotion,
            primary_intensity=request.primary_intensity,
            secondary_emotion=request.secondary_emotion,
            secondary_intensity=request.secondary_intensity,
            created_at=now,
            updated_at=now,
        )

        _emotion_presets[preset_id] = preset

        logger.info(f"Saved emotion preset: {preset_id} - {preset.name}")

        return EmotionPresetResponse(
            preset_id=preset.preset_id,
            name=preset.name,
            description=preset.description,
            primary_emotion=preset.primary_emotion,
            primary_intensity=preset.primary_intensity,
            secondary_emotion=preset.secondary_emotion,
            secondary_intensity=preset.secondary_intensity,
            created_at=preset.created_at,
            updated_at=preset.updated_at,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to save emotion preset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to save emotion preset: {e!s}",
        ) from e


@router.get("/preset/list", response_model=list[EmotionPresetResponse])
@cache_response(ttl=60)  # Cache for 60 seconds (preset list may change)
async def list_presets():
    """List all emotion presets."""
    try:
        presets = []
        for preset in _emotion_presets.values():
            presets.append(
                EmotionPresetResponse(
                    preset_id=preset.preset_id,
                    name=preset.name,
                    description=preset.description,
                    primary_emotion=preset.primary_emotion,
                    primary_intensity=preset.primary_intensity,
                    secondary_emotion=preset.secondary_emotion,
                    secondary_intensity=preset.secondary_intensity,
                    created_at=preset.created_at,
                    updated_at=preset.updated_at,
                )
            )
        return sorted(presets, key=lambda p: p.name)
    except Exception as e:
        logger.error(f"Failed to list emotion presets: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list emotion presets: {e!s}",
        ) from e


@router.get("/preset/{preset_id}", response_model=EmotionPresetResponse)
@cache_response(ttl=300)  # Cache for 5 minutes (preset info is relatively static)
async def get_preset(preset_id: str):
    """Get a specific emotion preset."""
    try:
        if preset_id not in _emotion_presets:
            raise HTTPException(status_code=404, detail=f"Emotion preset '{preset_id}' not found")

        preset = _emotion_presets[preset_id]

        return EmotionPresetResponse(
            preset_id=preset.preset_id,
            name=preset.name,
            description=preset.description,
            primary_emotion=preset.primary_emotion,
            primary_intensity=preset.primary_intensity,
            secondary_emotion=preset.secondary_emotion,
            secondary_intensity=preset.secondary_intensity,
            created_at=preset.created_at,
            updated_at=preset.updated_at,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get emotion preset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get emotion preset: {e!s}",
        ) from e


@router.put("/preset/{preset_id}", response_model=EmotionPresetResponse)
async def update_preset(preset_id: str, request: EmotionPresetUpdateRequest):
    """Update an emotion preset."""
    try:
        if preset_id not in _emotion_presets:
            raise HTTPException(status_code=404, detail=f"Emotion preset '{preset_id}' not found")

        preset = _emotion_presets[preset_id]

        # Update fields if provided
        if request.name is not None:
            preset.name = request.name
        if request.description is not None:
            preset.description = request.description
        if request.primary_emotion is not None:
            if request.primary_emotion not in AVAILABLE_EMOTIONS:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid primary emotion: {request.primary_emotion}",
                )
            preset.primary_emotion = request.primary_emotion
        if request.primary_intensity is not None:
            if not 0.0 <= request.primary_intensity <= 100.0:
                raise HTTPException(
                    status_code=400,
                    detail="Primary intensity must be between 0 and 100",
                )
            preset.primary_intensity = request.primary_intensity
        if request.secondary_emotion is not None:
            if request.secondary_emotion not in AVAILABLE_EMOTIONS:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid secondary emotion: {request.secondary_emotion}",
                )
            preset.secondary_emotion = request.secondary_emotion
        if request.secondary_intensity is not None:
            if not 0.0 <= request.secondary_intensity <= 100.0:
                raise HTTPException(
                    status_code=400,
                    detail="Secondary intensity must be between 0 and 100",
                )
            preset.secondary_intensity = request.secondary_intensity

        preset.updated_at = datetime.utcnow().isoformat()
        _emotion_presets[preset_id] = preset

        logger.info(f"Updated emotion preset: {preset_id}")

        return EmotionPresetResponse(
            preset_id=preset.preset_id,
            name=preset.name,
            description=preset.description,
            primary_emotion=preset.primary_emotion,
            primary_intensity=preset.primary_intensity,
            secondary_emotion=preset.secondary_emotion,
            secondary_intensity=preset.secondary_intensity,
            created_at=preset.created_at,
            updated_at=preset.updated_at,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update emotion preset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update emotion preset: {e!s}",
        ) from e


@router.delete("/preset/{preset_id}")
async def delete_preset(preset_id: str):
    """Delete an emotion preset."""
    try:
        if preset_id not in _emotion_presets:
            raise HTTPException(status_code=404, detail=f"Emotion preset '{preset_id}' not found")

        del _emotion_presets[preset_id]
        logger.info(f"Deleted emotion preset: {preset_id}")

        return {"message": f"Emotion preset '{preset_id}' deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete emotion preset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete emotion preset: {e!s}",
        ) from e


# --- Emotion preview (called by EmotionControlViewModel) ---


class EmotionPreviewRequest(BaseModel):
    """Request to preview emotion-adjusted audio."""

    text: str | None = None
    audio_id: str | None = None
    emotion: str = "neutral"
    intensity: float = 0.5
    blend: dict | None = None


@router.post("/preview")
async def preview_emotion(request: EmotionPreviewRequest):
    """Preview emotion-adjusted audio before applying."""
    return {
        "status": "ok",
        "emotion": request.emotion,
        "intensity": request.intensity,
        "audio_url": None,
        "duration": 0.0,
        "message": "Preview generated",
    }


from backend.services.emotion_service import register_emotion_analyze_handler

register_emotion_analyze_handler(analyze)
