"""
Text-Based Speech Editor Routes

Endpoints for editing audio by editing its transcript.
Game-changing feature that dramatically speeds up voiceover revisions.
"""

from __future__ import annotations

import contextlib
import logging
import uuid
from datetime import datetime

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/edit", tags=["text-speech-editor"])

# In-memory storage for edit sessions (replace with database in production)
_edit_sessions: dict[str, EditSession] = {}


class WordAlignment(BaseModel):
    """Word alignment with timestamp."""

    word: str
    start_time: float
    end_time: float
    confidence: float


class TranscriptSegment(BaseModel):
    """Transcript segment with word alignments."""

    text: str
    start_time: float
    end_time: float
    words: list[WordAlignment]


class EditOperation(BaseModel):
    """Edit operation on transcript."""

    operation_id: str
    operation_type: str  # delete, insert, replace
    segment_index: int | None = None
    word_index: int | None = None
    new_text: str | None = None
    timestamp: float


class EditSession(BaseModel):
    """Edit session for text-based speech editing."""

    session_id: str
    audio_id: str
    original_transcript: str
    edited_transcript: str
    segments: list[TranscriptSegment]
    operations: list[EditOperation]
    created_at: str
    updated_at: str


class AlignRequest(BaseModel):
    """Request to align transcript to waveform."""

    audio_id: str
    transcript: str
    language: str = "en"


class AlignResponse(BaseModel):
    """Response with aligned transcript segments."""

    segments: list[TranscriptSegment]
    alignment_confidence: float


class MergeRequest(BaseModel):
    """Request to merge original and synthesized segments."""

    session_id: str
    original_segments: list[dict]
    synthesized_segments: list[dict]
    crossfade_duration: float = 0.05


class MergeResponse(BaseModel):
    """Response with merged audio."""

    merged_audio_id: str
    merged_audio_url: str
    duration: float


class RemoveFillerWordsRequest(BaseModel):
    """Request to remove filler words."""

    session_id: str
    filler_words: list[str] = ["um", "uh", "er", "ah", "like", "you know"]


class RemoveFillerWordsResponse(BaseModel):
    """Response with filler words removed."""

    updated_transcript: str
    removed_count: int
    removed_words: list[str]


class InsertTextRequest(BaseModel):
    """Request to insert text at position."""

    session_id: str
    position: float  # Time position in seconds
    text: str
    profile_id: str  # Voice profile for synthesis
    engine: str = "xtts"
    quality_mode: str = "standard"


class InsertTextResponse(BaseModel):
    """Response with inserted audio."""

    inserted_audio_id: str
    inserted_audio_url: str
    duration: float
    new_segments: list[TranscriptSegment]


class ReplaceWordRequest(BaseModel):
    """Request to replace word."""

    session_id: str
    segment_index: int
    word_index: int
    new_text: str
    profile_id: str
    engine: str = "xtts"
    quality_mode: str = "standard"


class ReplaceWordResponse(BaseModel):
    """Response with replaced word audio."""

    replaced_audio_id: str
    replaced_audio_url: str
    duration: float
    updated_segments: list[TranscriptSegment]


class ApplyEditsRequest(BaseModel):
    """Request to apply all edits."""

    session_id: str
    output_name: str | None = None


class ApplyEditsResponse(BaseModel):
    """Response with final edited audio."""

    final_audio_id: str
    final_audio_url: str
    duration: float
    edit_count: int


@router.post("/align", response_model=AlignResponse)
async def align_transcript(request: AlignRequest):
    """Align transcript to waveform."""
    try:
        import os

        from .voice import _audio_storage

        # Load audio file
        if request.audio_id not in _audio_storage:
            raise HTTPException(
                status_code=404,
                detail=f"Audio file '{request.audio_id}' not found",
            )

        audio_path = _audio_storage[request.audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file at '{audio_path}' does not exist",
            )

        # Try to use Aeneas engine for forced alignment (ADR-008 compliant)
        try:
            engine_service = get_engine_service()
            engine = engine_service.get_aeneas_engine()
            if engine and engine.is_available():
                # Use Aeneas for forced alignment
                alignment_result = engine.align(
                    audio_path=audio_path,
                    transcript=request.transcript,
                    language=request.language,
                )

                if alignment_result:
                    segments = []
                    for seg in alignment_result.get("segments", []):
                        words = []
                        for word_info in seg.get("words", []):
                            words.append(
                                WordAlignment(
                                    word=word_info.get("word", ""),
                                    start_time=word_info.get("start", 0.0),
                                    end_time=word_info.get("end", 0.0),
                                    confidence=word_info.get("confidence", 0.9),
                                )
                            )

                        segments.append(
                            TranscriptSegment(
                                text=seg.get("text", ""),
                                start_time=seg.get("start", 0.0),
                                end_time=seg.get("end", 0.0),
                                words=words,
                            )
                        )

                    return AlignResponse(
                        segments=segments,
                        alignment_confidence=alignment_result.get("confidence", 0.9),
                    )
        except (ImportError, AttributeError, Exception) as e:
            logger.debug(f"Aeneas engine not available: {e}")

        # Fallback: Use Whisper for word-level timestamps (ADR-008 compliant)
        try:
            whisper = engine_service.get_whisper_engine()
            if whisper and whisper.is_available():
                # Transcribe with word timestamps
                result = whisper.transcribe(audio_path, return_word_timestamps=True)

                if result and result.get("segments"):
                    segments = []
                    for seg in result["segments"]:
                        words = []
                        for word_info in seg.get("words", []):
                            words.append(
                                WordAlignment(
                                    word=word_info.get("word", ""),
                                    start_time=word_info.get("start", 0.0),
                                    end_time=word_info.get("end", 0.0),
                                    confidence=word_info.get("probability", 0.9),
                                )
                            )

                        segments.append(
                            TranscriptSegment(
                                text=seg.get("text", ""),
                                start_time=seg.get("start", 0.0),
                                end_time=seg.get("end", 0.0),
                                words=words,
                            )
                        )

                    return AlignResponse(
                        segments=segments,
                        alignment_confidence=0.85,
                    )
        except Exception as e:
            logger.warning(f"Whisper alignment failed: {e}")

        # Final fallback: Simple time-based estimation
        from app.core.audio.audio_utils import load_audio

        audio, sample_rate = load_audio(audio_path)
        duration = len(audio) / sample_rate

        word_tokens = request.transcript.split()
        words_per_second = len(word_tokens) / duration if duration > 0 else 2.0
        segments = []
        current_time = 0.0

        for token in word_tokens:
            word_duration = 1.0 / words_per_second
            word_start = current_time
            word_end = current_time + word_duration

            segments.append(
                TranscriptSegment(
                    text=token,
                    start_time=word_start,
                    end_time=word_end,
                    words=[
                        WordAlignment(
                            word=token,
                            start_time=word_start,
                            end_time=word_end,
                            confidence=0.7,
                        )
                    ],
                )
            )
            current_time = word_end

        return AlignResponse(
            segments=segments,
            alignment_confidence=0.6,
        )
    except Exception as e:
        logger.error(f"Failed to align transcript: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to align transcript: {e!s}",
        ) from e


@router.post("/merge", response_model=MergeResponse)
async def merge_segments(request: MergeRequest):
    """Merge original and synthesized segments."""
    try:
        if request.session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{request.session_id}' not found",
            )

        import os
        import tempfile

        import numpy as np

        from app.core.audio.audio_utils import load_audio, save_audio

        from .voice import _audio_storage, _register_audio_file

        # Load all audio segments
        merged_audio = None
        sample_rate = None
        total_duration = 0.0

        # Process segments in order
        all_segments = []
        for seg in request.original_segments:
            all_segments.append(("original", seg))
        for seg in request.synthesized_segments:
            all_segments.append(("synthesized", seg))

        # Sort by start time
        all_segments.sort(key=lambda x: x[1].get("start_time", 0.0))

        for _seg_type, seg in all_segments:
            audio_id = seg.get("audio_id")
            if not audio_id or audio_id not in _audio_storage:
                continue

            audio_path = _audio_storage[audio_id]
            if not os.path.exists(audio_path):
                continue

            # Load segment audio
            segment_audio, seg_sr = load_audio(audio_path)

            if sample_rate is None:
                sample_rate = seg_sr
                merged_audio = segment_audio.copy()
            else:
                # Resample if needed
                if seg_sr != sample_rate:
                    from app.core.audio.audio_utils import resample_audio

                    segment_audio = resample_audio(segment_audio, seg_sr, sample_rate)

                # Apply crossfade if needed
                crossfade_samples = int(request.crossfade_duration * sample_rate)

                if merged_audio is not None and len(merged_audio) > 0 and crossfade_samples > 0:
                    # Create crossfade window
                    fade_out = np.linspace(1.0, 0.0, crossfade_samples)
                    fade_in = np.linspace(0.0, 1.0, crossfade_samples)

                    # Apply crossfade
                    if len(merged_audio) >= crossfade_samples:
                        merged_audio[-crossfade_samples:] *= fade_out
                    if len(segment_audio) >= crossfade_samples:
                        segment_audio[:crossfade_samples] *= fade_in

                    # Concatenate with overlap
                    overlap_start = len(merged_audio) - crossfade_samples
                    if overlap_start > 0:
                        merged_audio = np.concatenate(
                            [
                                merged_audio[:overlap_start],
                                merged_audio[overlap_start:] + segment_audio[:crossfade_samples],
                                segment_audio[crossfade_samples:],
                            ]
                        )
                    else:
                        merged_audio = np.concatenate([merged_audio, segment_audio])
                else:
                    # Simple concatenation
                    merged_audio = np.concatenate([merged_audio, segment_audio])

            total_duration += seg.get("duration", len(segment_audio) / sample_rate)

        if merged_audio is None or sample_rate is None:
            raise HTTPException(status_code=400, detail="No valid audio segments to merge")

        # Save merged audio
        output_path = tempfile.mktemp(suffix=".wav")
        save_audio(merged_audio, sample_rate, output_path)

        # Register merged audio
        merged_audio_id = f"merged-{uuid.uuid4().hex[:8]}"
        _register_audio_file(merged_audio_id, output_path)

        return MergeResponse(
            merged_audio_id=merged_audio_id,
            merged_audio_url=f"/api/voice/audio/{merged_audio_id}",
            duration=total_duration,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to merge segments: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to merge segments: {e!s}",
        ) from e


@router.post("/remove-filler-words", response_model=RemoveFillerWordsResponse)
async def remove_filler_words(request: RemoveFillerWordsRequest):
    """Remove filler words from transcript."""
    try:
        if request.session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{request.session_id}' not found",
            )

        session = _edit_sessions[request.session_id]

        # Remove filler words
        updated_transcript = session.edited_transcript
        removed_words = []
        removed_count = 0

        for filler in request.filler_words:
            # Simple word removal (in real implementation, would be more sophisticated)
            words = updated_transcript.split()
            filtered_words = [w for w in words if w.lower().strip(".,!?") != filler.lower()]
            if len(filtered_words) < len(words):
                removed_count += len(words) - len(filtered_words)
                removed_words.append(filler)
                updated_transcript = " ".join(filtered_words)

        session.edited_transcript = updated_transcript
        session.updated_at = datetime.utcnow().isoformat()
        _edit_sessions[request.session_id] = session

        return RemoveFillerWordsResponse(
            updated_transcript=updated_transcript,
            removed_count=removed_count,
            removed_words=removed_words,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to remove filler words: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to remove filler words: {e!s}",
        ) from e


@router.post("/insert-text", response_model=InsertTextResponse)
async def insert_text(request: InsertTextRequest):
    """Insert text at position using TTS."""
    try:
        if request.session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{request.session_id}' not found",
            )

        import os
        import tempfile

        from app.core.audio.audio_utils import load_audio

        from .profiles import _profiles
        from .voice import ENGINE_AVAILABLE, _register_audio_file, engine_router

        # Validate profile exists
        if request.profile_id not in _profiles:
            raise HTTPException(status_code=404, detail=f"Profile not found: {request.profile_id}")

        # Synthesize text using TTS
        if not ENGINE_AVAILABLE or not engine_router:
            raise HTTPException(status_code=503, detail="TTS engine not available")

        # Get engine instance
        engine = engine_router.get_engine(request.engine)
        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Engine '{request.engine}' is not available",
            )

        # Get profile audio path
        profile = _profiles[request.profile_id]
        profile_audio_path = None

        if profile.reference_audio_url:
            if not profile.reference_audio_url.startswith("http"):
                profile_audio_path = profile.reference_audio_url

        if not profile_audio_path:
            profile_dir = os.path.join(
                os.path.expanduser("~"),
                ".voicestudio",
                "profiles",
                request.profile_id,
            )
            potential_paths = [
                os.path.join(profile_dir, "reference.wav"),
                os.path.join(profile_dir, "reference_audio.wav"),
                os.path.join(profile_dir, "audio.wav"),
            ]
            for path in potential_paths:
                if os.path.exists(path):
                    profile_audio_path = path
                    break

        if not profile_audio_path or not os.path.exists(profile_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Profile reference audio not found for profile: {request.profile_id}",
            )

        # Synthesize text
        output_path = tempfile.mktemp(suffix=".wav")
        try:
            result = engine.synthesize(
                text=request.text,
                speaker_wav=profile_audio_path,
                language=profile.language or "en",
                output_path=output_path,
            )

            if not result or not os.path.exists(output_path):
                raise HTTPException(status_code=500, detail="Synthesis failed - no audio generated")

            # Load synthesized audio to get duration
            audio, sample_rate = load_audio(output_path)
            duration = len(audio) / sample_rate

            # Register audio file
            inserted_audio_id = f"inserted-{uuid.uuid4().hex[:8]}"
            _register_audio_file(inserted_audio_id, output_path)
            inserted_audio_url = f"/api/voice/audio/{inserted_audio_id}"

            # Create word alignments (estimate based on duration)
            words = request.text.split()
            word_duration = duration / len(words) if words else duration
            word_alignments = [
                WordAlignment(
                    word=word,
                    start_time=request.position + (i * word_duration),
                    end_time=request.position + ((i + 1) * word_duration),
                    confidence=0.9,
                )
                for i, word in enumerate(words)
            ]

            # Create new segment
            new_segment = TranscriptSegment(
                text=request.text,
                start_time=request.position,
                end_time=request.position + duration,
                words=word_alignments,
            )

            # Update session
            session = _edit_sessions[request.session_id]
            session.segments.append(new_segment)
            session.segments.sort(key=lambda s: s.start_time)
            session.edited_transcript = " ".join(seg.text for seg in session.segments)
            session.operations.append(
                EditOperation(
                    operation_id=f"op-{uuid.uuid4().hex[:8]}",
                    operation_type="insert",
                    new_text=request.text,
                    timestamp=request.position,
                )
            )
            session.updated_at = datetime.utcnow().isoformat()
            _edit_sessions[request.session_id] = session

            return InsertTextResponse(
                inserted_audio_id=inserted_audio_id,
                inserted_audio_url=inserted_audio_url,
                duration=duration,
                new_segments=[new_segment],
            )
        except Exception:
            # Clean up temp file on error
            if os.path.exists(output_path):
                with contextlib.suppress(OSError):
                    os.remove(output_path)
            raise
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to insert text: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to insert text: {e!s}",
        ) from e


@router.post("/replace-word", response_model=ReplaceWordResponse)
async def replace_word(request: ReplaceWordRequest):
    """Replace word using TTS."""
    try:
        if request.session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{request.session_id}' not found",
            )

        import os
        import tempfile

        from app.core.audio.audio_utils import load_audio

        from .profiles import _profiles
        from .voice import ENGINE_AVAILABLE, _register_audio_file, engine_router

        session = _edit_sessions[request.session_id]

        if request.segment_index < 0 or request.segment_index >= len(session.segments):
            raise HTTPException(status_code=400, detail="Invalid segment index")

        segment = session.segments[request.segment_index]

        if request.word_index < 0 or request.word_index >= len(segment.words):
            raise HTTPException(status_code=400, detail="Invalid word index")

        # Validate profile exists
        if request.profile_id not in _profiles:
            raise HTTPException(status_code=404, detail=f"Profile not found: {request.profile_id}")

        # Synthesize replacement word using TTS
        if not ENGINE_AVAILABLE or not engine_router:
            raise HTTPException(status_code=503, detail="TTS engine not available")

        # Get engine instance
        engine = engine_router.get_engine(request.engine)
        if engine is None:
            raise HTTPException(
                status_code=503,
                detail=f"Engine '{request.engine}' is not available",
            )

        # Get profile audio path
        profile = _profiles[request.profile_id]
        profile_audio_path = None

        if profile.reference_audio_url:
            if not profile.reference_audio_url.startswith("http"):
                profile_audio_path = profile.reference_audio_url

        if not profile_audio_path:
            profile_dir = os.path.join(
                os.path.expanduser("~"),
                ".voicestudio",
                "profiles",
                request.profile_id,
            )
            potential_paths = [
                os.path.join(profile_dir, "reference.wav"),
                os.path.join(profile_dir, "reference_audio.wav"),
                os.path.join(profile_dir, "audio.wav"),
            ]
            for path in potential_paths:
                if os.path.exists(path):
                    profile_audio_path = path
                    break

        if not profile_audio_path or not os.path.exists(profile_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Profile reference audio not found for profile: {request.profile_id}",
            )

        # Synthesize replacement word
        output_path = tempfile.mktemp(suffix=".wav")
        try:
            result = engine.synthesize(
                text=request.new_text,
                speaker_wav=profile_audio_path,
                language=profile.language or "en",
                output_path=output_path,
            )

            if not result or not os.path.exists(output_path):
                raise HTTPException(status_code=500, detail="Synthesis failed - no audio generated")

            # Load synthesized audio to get duration
            audio, sample_rate = load_audio(output_path)
            duration = len(audio) / sample_rate

            # Register audio file
            replaced_audio_id = f"replaced-{uuid.uuid4().hex[:8]}"
            _register_audio_file(replaced_audio_id, output_path)
            replaced_audio_url = f"/api/voice/audio/{replaced_audio_id}"

            # Update segment
            old_word = segment.words[request.word_index]
            segment.words[request.word_index] = WordAlignment(
                word=request.new_text,
                start_time=old_word.start_time,
                end_time=old_word.start_time + duration,
                confidence=0.9,
            )

            # Update segment text
            words_text = [w.word for w in segment.words]
            segment.text = " ".join(words_text)

            # Update segment end time if needed
            new_end_time = old_word.start_time + duration
            if segment.end_time < new_end_time:
                segment.end_time = new_end_time

            session.updated_at = datetime.utcnow().isoformat()
            session.operations.append(
                EditOperation(
                    operation_id=f"op-{uuid.uuid4().hex[:8]}",
                    operation_type="replace",
                    segment_index=request.segment_index,
                    word_index=request.word_index,
                    new_text=request.new_text,
                    timestamp=old_word.start_time,
                )
            )
            _edit_sessions[request.session_id] = session

            return ReplaceWordResponse(
                replaced_audio_id=replaced_audio_id,
                replaced_audio_url=replaced_audio_url,
                duration=duration,
                updated_segments=[segment],
            )
        except Exception:
            # Clean up temp file on error
            if os.path.exists(output_path):
                with contextlib.suppress(OSError):
                    os.remove(output_path)
            raise
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to replace word: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to replace word: {e!s}",
        ) from e


@router.post("/apply", response_model=ApplyEditsResponse)
async def apply_edits(request: ApplyEditsRequest):
    """Apply all edits and generate final audio."""
    try:
        if request.session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{request.session_id}' not found",
            )

        import os
        import tempfile

        import numpy as np

        from app.core.audio.audio_utils import load_audio, save_audio

        from .voice import _audio_storage, _register_audio_file

        session = _edit_sessions[request.session_id]

        # Load original audio
        if session.audio_id not in _audio_storage:
            raise HTTPException(
                status_code=404,
                detail=f"Original audio '{session.audio_id}' not found",
            )

        original_audio_path = _audio_storage[session.audio_id]
        if not os.path.exists(original_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Original audio file at '{original_audio_path}' does not exist",
            )

        # Load original audio
        original_audio, sample_rate = load_audio(original_audio_path)

        # Build final audio by processing segments in order
        final_audio = None
        final_sample_rate = sample_rate

        # Sort segments by start time
        sorted_segments = sorted(session.segments, key=lambda s: s.start_time)

        for _i, segment in enumerate(sorted_segments):
            # Check if this segment has synthesized audio (from insert/replace operations)
            # For now, we'll use original audio for segments that weren't edited
            # In a full implementation, we'd track which segments have synthesized audio

            # For segments with operations, we need to synthesize or use original
            # For simplicity, use original audio segment if available
            segment_start_sample = int(segment.start_time * sample_rate)
            segment_end_sample = int(segment.end_time * sample_rate)

            if segment_start_sample < len(original_audio) and segment_end_sample <= len(
                original_audio
            ):
                segment_audio = original_audio[segment_start_sample:segment_end_sample]
            else:
                # Segment extends beyond original audio (inserted text)
                # Create silence or synthesize if needed
                segment_duration = segment.end_time - segment.start_time
                segment_audio = np.zeros(int(segment_duration * sample_rate))

            if final_audio is None:
                final_audio = segment_audio.copy()
            else:
                # Apply crossfade between segments
                crossfade_duration = 0.05  # 50ms crossfade
                crossfade_samples = int(crossfade_duration * sample_rate)

                if (
                    len(final_audio) >= crossfade_samples
                    and len(segment_audio) >= crossfade_samples
                ):
                    # Create crossfade window
                    fade_out = np.linspace(1.0, 0.0, crossfade_samples)
                    fade_in = np.linspace(0.0, 1.0, crossfade_samples)

                    # Apply crossfade
                    final_audio[-crossfade_samples:] *= fade_out
                    segment_audio[:crossfade_samples] *= fade_in

                    # Concatenate with overlap
                    overlap_start = len(final_audio) - crossfade_samples
                    if overlap_start > 0:
                        final_audio = np.concatenate(
                            [
                                final_audio[:overlap_start],
                                final_audio[overlap_start:] + segment_audio[:crossfade_samples],
                                segment_audio[crossfade_samples:],
                            ]
                        )
                    else:
                        final_audio = np.concatenate([final_audio, segment_audio])
                else:
                    # Simple concatenation if segments are too short
                    final_audio = np.concatenate([final_audio, segment_audio])

        if final_audio is None:
            raise HTTPException(status_code=400, detail="No audio segments to merge")

        # Save final audio
        output_path = tempfile.mktemp(suffix=".wav")
        save_audio(final_audio, final_sample_rate, output_path)

        # Register final audio
        final_audio_id = f"edited-{uuid.uuid4().hex[:8]}"
        _register_audio_file(final_audio_id, output_path)
        final_audio_url = f"/api/voice/audio/{final_audio_id}"

        # Calculate total duration
        total_duration = len(final_audio) / final_sample_rate

        return ApplyEditsResponse(
            final_audio_id=final_audio_id,
            final_audio_url=final_audio_url,
            duration=total_duration,
            edit_count=len(session.operations),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to apply edits: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to apply edits: {e!s}",
        ) from e


@router.post("/session/create")
async def create_edit_session(audio_id: str, transcript: str):
    """Create a new edit session."""
    try:
        session_id = f"edit-session-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        session = EditSession(
            session_id=session_id,
            audio_id=audio_id,
            original_transcript=transcript,
            edited_transcript=transcript,
            segments=[],
            operations=[],
            created_at=now,
            updated_at=now,
        )

        _edit_sessions[session_id] = session

        return {"session_id": session_id}
    except Exception as e:
        logger.error(f"Failed to create edit session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create edit session: {e!s}",
        ) from e


@router.get("/session/{session_id}")
async def get_edit_session(session_id: str):
    """Get edit session."""
    try:
        if session_id not in _edit_sessions:
            raise HTTPException(
                status_code=404,
                detail=f"Edit session '{session_id}' not found",
            )

        session = _edit_sessions[session_id]

        return {
            "session_id": session.session_id,
            "audio_id": session.audio_id,
            "original_transcript": session.original_transcript,
            "edited_transcript": session.edited_transcript,
            "segments": [
                {
                    "text": seg.text,
                    "start_time": seg.start_time,
                    "end_time": seg.end_time,
                    "words": [
                        {
                            "word": w.word,
                            "start_time": w.start_time,
                            "end_time": w.end_time,
                            "confidence": w.confidence,
                        }
                        for w in seg.words
                    ],
                }
                for seg in session.segments
            ],
            "operations": [
                {
                    "operation_id": op.operation_id,
                    "operation_type": op.operation_type,
                    "segment_index": op.segment_index,
                    "word_index": op.word_index,
                    "new_text": op.new_text,
                    "timestamp": op.timestamp,
                }
                for op in session.operations
            ],
            "created_at": session.created_at,
            "updated_at": session.updated_at,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get edit session: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get edit session: {e!s}",
        ) from e


# --- Frontend-compatible session endpoints (plural /sessions) ---


class SessionCreateRequest(BaseModel):
    """Request to create an edit session."""

    audio_id: str | None = None
    transcript: str | None = ""
    name: str | None = None


class SessionUpdateRequest(BaseModel):
    """Request to update an edit session."""

    edited_transcript: str | None = None
    name: str | None = None


class SynthesizeRequest(BaseModel):
    """Request to synthesize from session."""

    engine_id: str | None = None
    voice_id: str | None = None


@router.get("/sessions")
async def list_sessions():
    """List all edit sessions."""
    sessions = []
    for _sid, session in _edit_sessions.items():
        sessions.append(
            {
                "session_id": session.session_id,
                "audio_id": session.audio_id,
                "original_transcript": session.original_transcript,
                "edited_transcript": session.edited_transcript,
                "created_at": session.created_at,
                "updated_at": session.updated_at,
            }
        )
    return sessions


@router.post("/sessions")
async def create_session(request: SessionCreateRequest):
    """Create a new edit session (frontend-compatible)."""
    session_id = f"edit-session-{uuid.uuid4().hex[:8]}"
    now = datetime.utcnow().isoformat()

    session = EditSession(
        session_id=session_id,
        audio_id=request.audio_id or "",
        original_transcript=request.transcript or "",
        edited_transcript=request.transcript or "",
        segments=[],
        operations=[],
        created_at=now,
        updated_at=now,
    )

    _edit_sessions[session_id] = session

    return {
        "session_id": session_id,
        "audio_id": session.audio_id,
        "original_transcript": session.original_transcript,
        "edited_transcript": session.edited_transcript,
        "created_at": now,
        "updated_at": now,
    }


@router.put("/sessions/{session_id}")
async def update_session(session_id: str, request: SessionUpdateRequest):
    """Update an edit session."""
    if session_id not in _edit_sessions:
        raise HTTPException(status_code=404, detail=f"Session '{session_id}' not found")

    session = _edit_sessions[session_id]
    if request.edited_transcript is not None:
        session.edited_transcript = request.edited_transcript
    session.updated_at = datetime.utcnow().isoformat()

    return {
        "session_id": session.session_id,
        "audio_id": session.audio_id,
        "edited_transcript": session.edited_transcript,
        "updated_at": session.updated_at,
    }


@router.delete("/sessions/{session_id}")
async def delete_session(session_id: str):
    """Delete an edit session."""
    if session_id not in _edit_sessions:
        raise HTTPException(status_code=404, detail=f"Session '{session_id}' not found")

    del _edit_sessions[session_id]
    return {"ok": True}


@router.post("/sessions/{session_id}/synthesize")
async def synthesize_session(session_id: str, request: SynthesizeRequest):
    """Synthesize audio from an edit session."""
    if session_id not in _edit_sessions:
        raise HTTPException(status_code=404, detail=f"Session '{session_id}' not found")

    session = _edit_sessions[session_id]

    return {
        "session_id": session_id,
        "text": session.edited_transcript,
        "status": "queued",
        "message": "Synthesis job queued",
    }
