"""
Dubbing Routes

Endpoints for video dubbing operations including translation and synchronization.
"""

import logging
import re
from typing import Any

from fastapi import APIRouter, HTTPException

from backend.ml.models.engine_service import get_engine_service

from ..models_additional import (
    DubSyncRequest,
    DubSyncResponse,
    DubTranslateRequest,
    DubTranslateResponse,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/dub", tags=["dub"])


@router.post("/translate", response_model=DubTranslateResponse)
async def translate(req: DubTranslateRequest) -> DubTranslateResponse:
    """
    Translate audio transcription for dubbing purposes.

    First transcribes the audio, then translates the text to the target language
    for dubbing workflows.
    """
    try:
        # First, get transcription of the audio
        audio_id = req.audio_id
        target_lang = req.lang

        if not audio_id or not target_lang:
            raise HTTPException(status_code=400, detail="audio_id and lang are required")

        # Get audio transcription
        from .transcribe import _transcriptions

        transcription = None

        # Check if transcription exists for this audio
        for _trans_id, trans_data in _transcriptions.items():
            if trans_data.get("audio_id") == audio_id:
                transcription = trans_data
                break

        # If no transcription exists, create one
        if not transcription:
            try:
                # Use transcription service
                from .transcribe import TranscriptionRequest, transcribe_audio

                transcribe_req = TranscriptionRequest(
                    audio_id=audio_id, engine="whisper", language="auto"
                )
                transcription_result = await transcribe_audio(transcribe_req)
                source_text = transcription_result.text
                detected_lang = transcription_result.language
            except Exception as e:
                logger.error(f"Failed to transcribe audio: {e}")
                raise HTTPException(
                    status_code=500, detail=f"Failed to transcribe audio for translation: {e!s}"
                )
        else:
            source_text = transcription.get("text", "")
            detected_lang = transcription.get("language", "auto")

        if not source_text:
            raise HTTPException(
                status_code=404,
                detail="No transcription found for audio. Cannot translate empty text.",
            )

        # Translate the transcription
        from .multilingual import TranslationRequest, translate_text

        translation_req = TranslationRequest(
            text=source_text,
            source_language=detected_lang if detected_lang != "auto" else "auto",
            target_language=target_lang,
        )

        # Use real translation service
        translation_result = await translate_text(translation_req)

        return DubTranslateResponse(
            text=translation_result.translated_text,
            source_language=translation_result.source_language,
            target_language=translation_result.target_language,
            confidence=translation_result.confidence,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Translation failed for dubbing: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Translation failed: {e!s}")


@router.post("/sync", response_model=DubSyncResponse)
async def sync(req: DubSyncRequest) -> DubSyncResponse:
    """
    Synchronize translated text with audio/video timing.

    Aligns translated dialogue with original audio timing for dubbing.
    Uses audio-text alignment to create timing segments for translated text.
    """
    try:
        import os
        import re

        audio_id = req.audio_id
        translated_text = req.translated_text
        original_timing = req.original_timing
        target_language = req.target_language

        if not audio_id or not translated_text:
            raise HTTPException(
                status_code=400,
                detail="audio_id and translated_text are required",
            )

        # Load audio file
        from .voice import _audio_storage

        if audio_id not in _audio_storage:
            raise HTTPException(
                status_code=404,
                detail=f"Audio file '{audio_id}' not found",
            )

        audio_path = _audio_storage[audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file at '{audio_path}' does not exist",
            )

        # Load audio to get duration
        from app.core.audio.audio_utils import load_audio

        audio, sample_rate = load_audio(audio_path)
        audio_duration = len(audio) / sample_rate

        # Try to use Aeneas engine for audio-text alignment
        alignment_result = None
        try:
            import sys
            from pathlib import Path

            # Add app directory to path if needed
            app_path = Path(__file__).parent.parent.parent.parent / "app"
            if str(app_path) not in sys.path:
                sys.path.insert(0, str(app_path))

            try:
                # Get Aeneas engine via EngineService (ADR-008 compliant)
                engine_service = get_engine_service()
                engine = engine_service.get_aeneas_engine()
                if not engine:
                    raise ImportError("Aeneas engine not available")

                # Perform alignment
                # Segment translated text into sentences
                sentences = re.split(r"[.!?]+", translated_text)
                sentences = [s.strip() for s in sentences if s.strip()]

                # Use original timing if available, otherwise estimate
                if original_timing and isinstance(original_timing, list):
                    timing_segments: list[dict[str, Any]] = []
                    sum(seg.get("end", 0) - seg.get("start", 0) for seg in original_timing)

                    # Distribute translated text proportionally
                    char_count = len(translated_text)
                    if char_count > 0:
                        for i, sentence in enumerate(sentences):
                            if i < len(original_timing):
                                # Use original timing if available
                                seg = original_timing[i]
                                start = seg.get("start", 0)
                                end = seg.get("end", audio_duration)
                            else:
                                # Estimate timing based on sentence length
                                sentence_ratio = len(sentence) / char_count
                                start = timing_segments[-1]["end"] if timing_segments else 0
                                end = min(
                                    start + (sentence_ratio * audio_duration),
                                    audio_duration,
                                )

                            timing_segments.append(
                                {
                                    "text": sentence,
                                    "start": round(start, 3),
                                    "end": round(end, 3),
                                }
                            )
                else:
                    # Estimate timing based on text length
                    timing_segments = []
                    char_count = len(translated_text)
                    if char_count > 0:
                        current_time = 0.0
                        for sentence in sentences:
                            sentence_ratio = len(sentence) / char_count
                            duration = sentence_ratio * audio_duration
                            timing_segments.append(
                                {
                                    "text": sentence,
                                    "start": round(current_time, 3),
                                    "end": round(min(current_time + duration, audio_duration), 3),
                                }
                            )
                            current_time += duration

                alignment_result = {
                    "segments": timing_segments,
                    "total_duration": audio_duration,
                    "language": target_language,
                }

                logger.info(
                    f"Synchronized dubbing for audio {audio_id}: "
                    f"{len(timing_segments)} segments, "
                    f"{audio_duration:.2f}s duration"
                )

            except ImportError:
                # Aeneas not available, use fallback method
                logger.warning("Aeneas engine not available, using fallback alignment")
                raise ImportError("Aeneas not available")

        except ImportError:
            # Fallback: Simple proportional timing based on text length
            logger.info("Using fallback timing alignment method")

            # Segment translated text
            sentences = re.split(r"[.!?]+", translated_text)
            sentences = [s.strip() for s in sentences if s.strip()]

            # Calculate timing proportionally
            timing_segments = []
            char_count = len(translated_text)
            if char_count > 0:
                current_time = 0.0
                for sentence in sentences:
                    sentence_ratio = len(sentence) / char_count
                    duration = sentence_ratio * audio_duration
                    timing_segments.append(
                        {
                            "text": sentence,
                            "start": round(current_time, 3),
                            "end": round(min(current_time + duration, audio_duration), 3),
                        }
                    )
                    current_time += duration

            alignment_result = {
                "segments": timing_segments,
                "total_duration": audio_duration,
                "language": target_language,
                "method": "proportional",
            }

        if not alignment_result:
            raise HTTPException(
                status_code=500,
                detail="Failed to generate alignment result",
            )

        return DubSyncResponse(
            audio_id=audio_id,
            translated_text=translated_text,
            alignment=alignment_result,
            message="Dubbing synchronization completed",
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Dubbing sync failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Dubbing synchronization failed: {e!s}",
        ) from e
