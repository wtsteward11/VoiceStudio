"""
Voice & Speech Processing Routes

Endpoints for voice activity detection, phonemization, and speech recognition.
"""

from __future__ import annotations

import logging
import os
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response
from ..voice_speech import (
    Phonemizer,
    SpeechRecognizer,
    VoiceActivityDetector,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/voice-speech", tags=["voice-speech"])


# Request/Response Models
class VoiceActivityResult(BaseModel):
    """Voice activity detection result."""

    segments: list[dict[str, float]]  # List of {start, end} dicts
    voice_ratio: float
    total_duration: float


class PhonemizationRequest(BaseModel):
    """Request for phonemization."""

    text: str
    language: str = "en-us"
    backend: str = "phonemizer"  # "phonemizer" or "gruut"


class PhonemizationResponse(BaseModel):
    """Response from phonemization."""

    phonemes: str
    words: list[dict[str, Any]] | None = None
    backend: str


class SpeechRecognitionRequest(BaseModel):
    """Request for speech recognition."""

    audio_id: str
    model_path: str | None = None


class SpeechRecognitionResponse(BaseModel):
    """Response from speech recognition."""

    text: str
    words: list[dict[str, Any]]
    confidence: float


@router.get("/{audio_id}/voice-activity")
@cache_response(ttl=300)
async def detect_voice_activity(
    audio_id: str,
    threshold: float = Query(0.5, ge=0.0, le=1.0, description="Detection threshold"),
):
    """Detect voice activity in an audio file."""
    from backend.services.audio_path_resolver import resolve_audio_path

    audio_path = resolve_audio_path(audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

    try:
        import soundfile as sf

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if stereo
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Initialize VAD
        vad = VoiceActivityDetector()
        segments = vad.detect_voice_activity(audio, sample_rate, threshold)
        voice_ratio = vad.get_voice_ratio(audio, sample_rate, threshold)

        return VoiceActivityResult(
            segments=[{"start": start, "end": end} for start, end in segments],
            voice_ratio=voice_ratio,
            total_duration=len(audio) / sample_rate,
        )
    except ImportError as e:
        raise HTTPException(
            status_code=503,
            detail=f"Voice activity detection not available: {e!s}",
        )
    except Exception as e:
        logger.error(f"Error in voice activity detection: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to detect voice activity: {e!s}",
        )


@router.post("/phonemize", response_model=PhonemizationResponse)
async def phonemize_text(request: PhonemizationRequest):
    """Convert text to phonemes."""
    try:
        phonemizer = Phonemizer()

        if request.backend == "phonemizer" and phonemizer.phonemizer_available:
            phonemes = phonemizer.phonemize_with_phonemizer(
                request.text,
                language=request.language,
            )
            return PhonemizationResponse(phonemes=phonemes, backend="phonemizer")
        elif request.backend == "gruut" and phonemizer.gruut_available:
            words = phonemizer.phonemize_with_gruut(
                request.text,
                language=request.language,
            )
            phonemes_str = " ".join([w.get("phonemes_str", "") for w in words])
            return PhonemizationResponse(phonemes=phonemes_str, words=words, backend="gruut")
        else:
            available = phonemizer.get_available_backends()
            raise HTTPException(
                status_code=400,
                detail=f"Backend '{request.backend}' not available. "
                f"Available: {', '.join(available)}",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in phonemization: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to phonemize text: {e!s}")


@router.post("/recognize", response_model=SpeechRecognitionResponse)
async def recognize_speech(request: SpeechRecognitionRequest):
    """Recognize speech in an audio file."""
    from backend.services.audio_path_resolver import resolve_audio_path

    audio_path = resolve_audio_path(request.audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(
            status_code=404,
            detail=f"Audio file not found: {request.audio_id}",
        )

    try:
        import soundfile as sf

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if stereo
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Initialize recognizer
        recognizer = SpeechRecognizer(model_path=request.model_path)
        result = recognizer.recognize(audio, sample_rate)

        return SpeechRecognitionResponse(
            text=result["text"],
            words=result.get("words", []),
            confidence=result.get("confidence", 0.0),
        )
    except ImportError as e:
        raise HTTPException(
            status_code=503,
            detail=f"Speech recognition not available: {e!s}",
        )
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=f"Model configuration error: {e!s}",
        )
    except Exception as e:
        logger.error(f"Error in speech recognition: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to recognize speech: {e!s}",
        )


@router.get("/backends")
@cache_response(ttl=600)
async def get_available_backends():
    """Get list of available backends for voice & speech processing."""
    phonemizer = Phonemizer()
    vad = VoiceActivityDetector()
    recognizer = SpeechRecognizer()

    return {
        "phonemization_backends": phonemizer.get_available_backends(),
        "vad_available": vad.silero_available,
        "speech_recognition_available": recognizer.vosk_available,
    }
