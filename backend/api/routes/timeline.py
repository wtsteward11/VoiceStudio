"""
Timeline API Routes

Endpoints for managing timeline state, tracks, clips, and editing operations.
This API supports the frontend TimelineUseCase component.

GAP-API-001: Implements /api/timeline/* endpoints expected by TimelineUseCase.cs
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any, Dict, List, Optional
from uuid import uuid4

import numpy as np
from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from ..deps import TrackStoreDep
from ..middleware.auth_middleware import require_auth_if_enabled

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/timeline", tags=["timeline"])


# ============================================================================
# Models
# ============================================================================


class Clip(BaseModel):
    """A clip within a track."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    track_id: str = ""
    start_time: float = 0.0  # seconds
    end_time: float = 1.0  # seconds
    source_path: Optional[str] = None
    source_start: float = 0.0  # source offset
    fade_in_seconds: float = 0.0
    fade_out_seconds: float = 0.0
    name: str = "Untitled Clip"
    color: Optional[str] = None
    volume: float = 1.0
    muted: bool = False
    locked: bool = False
    metadata: Dict[str, Any] = Field(default_factory=dict)


class Track(BaseModel):
    """A track in the timeline."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    name: str = "Track"
    type: str = "audio"  # audio, video, subtitle
    order: int = 0
    color: Optional[str] = None
    volume: float = 1.0
    pan: float = 0.0
    muted: bool = False
    solo: bool = False
    locked: bool = False
    clips: List[Clip] = Field(default_factory=list)
    metadata: Dict[str, Any] = Field(default_factory=dict)


class TimelineState(BaseModel):
    """Complete timeline state."""

    id: str = Field(default_factory=lambda: str(uuid4()))
    name: str = "Untitled Timeline"
    duration: float = 0.0  # seconds
    sample_rate: int = 48000
    tracks: list[Track] = Field(default_factory=list)
    playhead_position: float = 0.0
    loop_start: float | None = None
    loop_end: float | None = None
    zoom_level: float = 1.0
    scroll_offset: float = 0.0
    created_at: str = Field(default_factory=lambda: datetime.now().isoformat())
    updated_at: str = Field(default_factory=lambda: datetime.now().isoformat())


class CreateTimelineOptions(BaseModel):
    """Options for creating a new timeline."""

    name: str | None = "Untitled Timeline"
    sample_rate: int | None = 48000


class AddTrackRequest(BaseModel):
    """Request to add a track."""

    name: str | None = "Track"
    type: str | None = "audio"


class DeleteRequest(BaseModel):
    """Generic delete request."""

    id: str


class DeleteResponse(BaseModel):
    """Generic delete response."""

    success: bool
    deleted_id: str


class AddClipRequest(BaseModel):
    """Request to add a clip."""

    track_id: str
    source_path: str | None = None
    start_time: float = 0.0
    duration: float = 1.0
    name: str | None = "Clip"


class MoveClipRequest(BaseModel):
    """Request to move a clip."""

    new_start_time: float
    new_track_id: str | None = None


class TrimClipRequest(BaseModel):
    """Request to trim a clip."""

    new_start: float | None = None
    new_end: float | None = None


class SplitClipRequest(BaseModel):
    """Request to split a clip."""

    split_position: float


class SplitClipResponse(BaseModel):
    """Response after splitting a clip."""

    clip_before: Clip
    clip_after: Clip


class SetClipFadeRequest(BaseModel):
    """Request to set linear fade-in/out on a clip (export/mixdown)."""

    fade_in_seconds: float = 0.0
    fade_out_seconds: float = 0.0


class PlayheadRequest(BaseModel):
    """Request to set playhead position."""

    Position: float


class LoopRequest(BaseModel):
    """Request to set loop region."""

    Start: float
    End: float


class ExportRequest(BaseModel):
    """Request to export timeline."""

    output_path: str
    format: str = "wav"
    sample_rate: int | None = None
    project_id: str | None = None
    apply_effects: bool = False
    effect_chain_id: str | None = None
    fallback_project_audio_id: str | None = None
    lufs_preset: str = "podcast_stereo"


class ImportProjectRequest(BaseModel):
    """Rebuild in-memory timeline mix graph from persisted project tracks (GAP-031)."""

    project_id: str = Field(..., min_length=1)


class UpdateTimelineTrackRequest(BaseModel):
    """Partial update for an in-memory timeline track (mix controls)."""

    name: str | None = None
    muted: bool | None = None
    solo: bool | None = None
    volume: float | None = None
    pan: float | None = None


class ExportResponse(BaseModel):
    """Response after export."""

    success: bool
    output_path: str
    duration: float


class UndoResponse(BaseModel):
    """Response after undo/redo."""

    success: bool
    operation: str | None = None


class UndoRedoState(BaseModel):
    """Current undo/redo state."""

    can_undo: bool = False
    can_redo: bool = False
    undo_description: str | None = None
    redo_description: str | None = None


# ============================================================================
# Session timeline persistence (D-001 — SQLite, shared across workers)
# ============================================================================


async def _hydrate() -> tuple[TimelineState, list[TimelineState], list[TimelineState]]:
    """Load timeline + undo/redo stacks from SQLite (or empty defaults)."""
    from backend.project.timeline.session_repository import (
        DEFAULT_SESSION_ID,
        load_session_timeline_raw,
    )

    raw = await load_session_timeline_raw(DEFAULT_SESSION_ID)
    if raw is None:
        return TimelineState(), [], []
    state = TimelineState.model_validate(raw["timeline"])
    undo = [TimelineState.model_validate(x) for x in raw["undo"]]
    redo = [TimelineState.model_validate(x) for x in raw["redo"]]
    return state, undo, redo


async def _persist(
    state: TimelineState,
    undo_stack: list[TimelineState],
    redo_stack: list[TimelineState],
) -> None:
    """Write timeline + stacks to SQLite."""
    from backend.project.timeline.session_repository import (
        DEFAULT_SESSION_ID,
        save_session_timeline_raw,
    )

    state.updated_at = datetime.now().isoformat()
    await save_session_timeline_raw(
        state.model_dump(mode="json"),
        [x.model_dump(mode="json") for x in undo_stack],
        [x.model_dump(mode="json") for x in redo_stack],
        session_id=DEFAULT_SESSION_ID,
    )


def _push_undo_before_mutate(
    current: TimelineState,
    undo_stack: list[TimelineState],
    redo_stack: list[TimelineState],
) -> None:
    """Snapshot current timeline before a mutating operation."""
    undo_stack.append(current.model_copy(deep=True))
    redo_stack.clear()
    if len(undo_stack) > 50:
        undo_stack.pop(0)


def _update_timeline_duration(timeline: TimelineState) -> None:
    """Update timeline duration based on clips."""
    max_end = 0.0
    for track in timeline.tracks:
        for clip in track.clips:
            if clip.end_time > max_end:
                max_end = clip.end_time
    timeline.duration = max_end


async def _render_timeline_audio(timeline: TimelineState, sample_rate: int) -> np.ndarray | None:
    """Render all timeline clips into a single audio array.

    Args:
        timeline: The timeline state to render
        sample_rate: Target sample rate for output

    Returns:
        Numpy array with mixed audio, or None if no clips
    """
    import os

    import numpy as np

    # Calculate total duration in samples
    if timeline.duration <= 0:
        return None

    total_samples = int(timeline.duration * sample_rate)
    if total_samples <= 0:
        return None

    # Initialize output buffer (mono for simplicity; stereo would need 2D array)
    output = np.zeros(total_samples, dtype=np.float32)
    has_audio = False

    sorted_tracks = sorted(timeline.tracks, key=lambda t: (t.order, t.id))
    audio_tracks = [t for t in sorted_tracks if t.type == "audio"]
    any_solo = any(t.solo for t in audio_tracks)

    for track in sorted_tracks:
        if track.type != "audio":
            continue
        if track.muted:
            continue
        if any_solo and not track.solo:
            continue

        track_volume = track.volume

        for clip in track.clips:
            # Skip muted clips or clips without source
            if clip.muted or not clip.source_path:
                continue

            # Check if source file exists
            if not os.path.exists(clip.source_path):
                logger.warning(f"Clip source not found: {clip.source_path}")
                continue

            # Load clip audio
            try:
                clip_audio = await _load_audio_file(clip.source_path, sample_rate)
                if clip_audio is None:
                    continue

                # Calculate positions
                clip_start_sample = int(clip.start_time * sample_rate)
                clip_end_sample = int(clip.end_time * sample_rate)
                source_start_sample = int(clip.source_start * sample_rate)

                # Get the portion of the source audio we need
                source_end_sample = source_start_sample + (clip_end_sample - clip_start_sample)
                source_audio = clip_audio[source_start_sample:source_end_sample]

                # Apply clip and track volume
                source_audio = source_audio * clip.volume * track_volume

                # Linear fade-in / fade-out (GAP-037)
                clip_len = int(source_audio.shape[0])
                if clip_len > 0:
                    fin = int(min(max(clip.fade_in_seconds, 0.0) * sample_rate, clip_len / 2))
                    fout = int(min(max(clip.fade_out_seconds, 0.0) * sample_rate, clip_len / 2))
                    if fin > 0:
                        ramp = np.linspace(0.0, 1.0, fin, endpoint=False, dtype=np.float32)
                        source_audio[:fin] *= ramp
                    if fout > 0:
                        ramp = np.linspace(1.0, 0.0, fout, endpoint=False, dtype=np.float32)
                        source_audio[-fout:] *= ramp

                # Mix into output buffer
                actual_length = min(len(source_audio), total_samples - clip_start_sample)
                if actual_length > 0:
                    output[clip_start_sample : clip_start_sample + actual_length] += source_audio[
                        :actual_length
                    ]
                    has_audio = True

            except Exception as e:
                logger.warning(f"Failed to load clip {clip.id}: {e}")
                continue

    return output if has_audio else None


async def _load_audio_file(path: str, target_sample_rate: int) -> np.ndarray | None:
    """Load an audio file and resample to target rate.

    Args:
        path: Path to audio file
        target_sample_rate: Target sample rate

    Returns:
        Mono audio array resampled to target rate, or None on error
    """
    import numpy as np

    try:
        import soundfile as sf

        audio, sr = sf.read(path, dtype="float32")

        # Convert stereo to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Resample if needed
        if sr != target_sample_rate:
            try:
                import librosa

                audio = librosa.resample(audio, orig_sr=sr, target_sr=target_sample_rate)
            except ImportError:
                # Simple linear interpolation as fallback
                import scipy.signal

                num_samples = int(len(audio) * target_sample_rate / sr)
                audio = scipy.signal.resample(audio, num_samples).astype(np.float32)

        return np.asarray(audio)

    except ImportError:
        logger.warning("soundfile not available, audio loading disabled")
        return None
    except Exception as e:
        logger.error(f"Failed to load audio {path}: {e}")
        return None


async def _write_audio_output(
    audio: np.ndarray, output_path: str, sample_rate: int, format: str
) -> None:
    """Write audio array to file.

    Args:
        audio: Audio data as numpy array
        output_path: Path to write to
        sample_rate: Sample rate of audio
        format: Output format (wav, mp3, flac, etc.)
    """
    import os
    import tempfile
    from pathlib import Path

    # Ensure output directory exists
    os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)

    # Normalize audio to prevent clipping
    import numpy as np

    max_val = np.max(np.abs(audio))
    if max_val > 0:
        audio = audio / max(max_val, 1.0)

    try:
        from backend.services.audio_artifacts.use_cases import wav_array_to_bytes

        # soundfile supports wav, flac, ogg natively; avoid sf.write to path
        sf_format = format.upper()
        if sf_format == "MP3":
            # soundfile doesn't support mp3, write wav to temp then convert
            wav_bytes = wav_array_to_bytes(audio, sample_rate)
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                tmp.write(wav_bytes)
                wav_path = tmp.name
            try:
                await _convert_to_format(wav_path, output_path, format)
            finally:
                if os.path.exists(wav_path):
                    os.remove(wav_path)
        else:
            wav_bytes = wav_array_to_bytes(
                audio, sample_rate, format=sf_format
            )
            Path(output_path).write_bytes(wav_bytes)

    except ImportError:
        # Fallback to scipy for wav
        if format.lower() == "wav":
            from scipy.io import wavfile

            audio_int16 = (audio * 32767).astype(np.int16)
            wavfile.write(output_path, sample_rate, audio_int16)
        else:
            raise ValueError(f"Format {format} requires soundfile library")


async def _convert_to_format(input_path: str, output_path: str, format: str) -> None:
    """Convert audio file to specified format using ffmpeg.

    Args:
        input_path: Source audio file
        output_path: Destination path
        format: Target format
    """
    import asyncio
    import os
    import shutil

    # Find ffmpeg
    ffmpeg_env = os.environ.get("VOICESTUDIO_FFMPEG_PATH", "ffmpeg")
    ffmpeg_path: str | None = ffmpeg_env if shutil.which(ffmpeg_env) else shutil.which("ffmpeg")

    if ffmpeg_path is None:
        raise RuntimeError("ffmpeg not found for format conversion")

    cmd = [ffmpeg_path, "-y", "-i", input_path, output_path]

    proc = await asyncio.create_subprocess_exec(
        *cmd, stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.PIPE
    )
    _, stderr = await proc.communicate()

    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg conversion failed: {stderr.decode()}")


# ============================================================================
# Endpoints
# ============================================================================


@router.get("/state", response_model=TimelineState, dependencies=[Depends(require_auth_if_enabled)])
async def get_timeline_state():
    """Get the current timeline state."""
    state, _, _ = await _hydrate()
    return state


@router.post(
    "/create", response_model=TimelineState, dependencies=[Depends(require_auth_if_enabled)]
)
async def create_timeline(options: CreateTimelineOptions):
    """Create a new timeline."""
    state, undo, redo = await _hydrate()
    _push_undo_before_mutate(state, undo, redo)

    new_state = TimelineState(
        name=options.name or "Untitled Timeline",
        sample_rate=options.sample_rate or 48000,
    )
    await _persist(new_state, undo, redo)
    logger.info(f"Created new timeline: {new_state.name}")
    return new_state


@router.post("/tracks", response_model=Track, dependencies=[Depends(require_auth_if_enabled)])
async def add_track(request: AddTrackRequest):
    """Add a track to the timeline."""
    state, undo, redo = await _hydrate()
    _push_undo_before_mutate(state, undo, redo)

    track = Track(
        name=request.name or f"Track {len(state.tracks) + 1}",
        type=request.type or "audio",
        order=len(state.tracks),
    )
    state.tracks.append(track)
    await _persist(state, undo, redo)
    logger.info(f"Added track: {track.name}")
    return track


@router.put(
    "/tracks/{track_id}",
    response_model=Track,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def update_timeline_track(track_id: str, request: UpdateTimelineTrackRequest):
    """Update mix/rendering fields on an in-memory timeline track (GAP-031)."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)
    track = next((t for t in timeline.tracks if t.id == track_id), None)
    if track is None:
        raise HTTPException(status_code=404, detail=f"Track {track_id} not found")
    if request.name is not None:
        track.name = request.name
    if request.muted is not None:
        track.muted = request.muted
    if request.solo is not None:
        track.solo = request.solo
    if request.volume is not None:
        track.volume = request.volume
    if request.pan is not None:
        track.pan = request.pan
    await _persist(timeline, undo, redo)
    return track


@router.post(
    "/import-from-project",
    response_model=TimelineState,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def import_timeline_from_project(body: ImportProjectRequest, track_store: TrackStoreDep):
    """Load persisted project tracks into the timeline mix graph used by export (GAP-031)."""
    from backend.services.audio_artifacts import AudioRegistry

    prior, undo, redo = await _hydrate()
    _push_undo_before_mutate(prior, undo, redo)
    project_id = body.project_id.strip()
    track_data_list = track_store.list_tracks(project_id)
    tracks: list[Track] = []
    max_end = 0.0

    for order_idx, td in enumerate(track_data_list):
        raw_tid = td.get("id")
        tid = str(raw_tid) if raw_tid else str(uuid4())
        clips_out: list[Clip] = []
        for clip_data in td.get("clips", []):
            audio_id = str(clip_data.get("audio_id", "") or "").strip()
            src = AudioRegistry.get_path(audio_id) if audio_id else None
            if not src:
                logger.warning(
                    "Skipping clip %s: no registry path for audio_id=%s",
                    clip_data.get("id"),
                    audio_id,
                )
                continue
            st = float(clip_data.get("start_time", 0.0))
            dur = float(clip_data.get("duration_seconds", 0.0))
            if dur <= 0:
                continue
            raw_cid = clip_data.get("id")
            cid = str(raw_cid) if raw_cid else str(uuid4())
            end_t = st + dur
            max_end = max(max_end, end_t)
            src_off = float(clip_data.get("source_start_seconds", 0.0) or 0.0)
            fin = float(clip_data.get("fade_in_seconds", 0.0) or 0.0)
            fout = float(clip_data.get("fade_out_seconds", 0.0) or 0.0)
            clips_out.append(
                Clip(
                    id=cid,
                    track_id=tid,
                    start_time=st,
                    end_time=end_t,
                    source_path=src,
                    source_start=max(0.0, src_off),
                    fade_in_seconds=max(0.0, fin),
                    fade_out_seconds=max(0.0, fout),
                    name=str(clip_data.get("name", "Clip") or "Clip"),
                    volume=float(clip_data.get("volume", 1.0) or 1.0),
                    muted=bool(clip_data.get("muted", False)),
                )
            )
        tracks.append(
            Track(
                id=tid,
                name=str(td.get("name", "Track") or "Track"),
                type="audio",
                order=int(td.get("track_number", order_idx) or order_idx),
                volume=1.0,
                muted=bool(td.get("is_muted", False)),
                solo=bool(td.get("is_solo", False)),
                clips=clips_out,
            )
        )

    timeline = TimelineState(
        name=f"Project {project_id}",
        duration=max(max_end, 0.0),
        sample_rate=48000,
        tracks=tracks,
        updated_at=datetime.now().isoformat(),
    )
    await _persist(timeline, undo, redo)
    logger.info(
        "Imported timeline from project %s (%s tracks, duration=%ss)",
        project_id,
        len(tracks),
        timeline.duration,
    )
    return timeline


@router.post(
    "/tracks/delete", response_model=DeleteResponse, dependencies=[Depends(require_auth_if_enabled)]
)
async def delete_track(request: DeleteRequest):
    """Delete a track from the timeline."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    original_count = len(timeline.tracks)
    timeline.tracks = [t for t in timeline.tracks if t.id != request.id]

    if len(timeline.tracks) == original_count:
        raise HTTPException(status_code=404, detail=f"Track {request.id} not found")

    # Re-order remaining tracks
    for i, track in enumerate(timeline.tracks):
        track.order = i

    _update_timeline_duration(timeline)
    await _persist(timeline, undo, redo)
    logger.info(f"Deleted track: {request.id}")
    return DeleteResponse(success=True, deleted_id=request.id)


@router.post("/clips", response_model=Clip, dependencies=[Depends(require_auth_if_enabled)])
async def add_clip(request: AddClipRequest):
    """Add a clip to a track."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    track = next((t for t in timeline.tracks if t.id == request.track_id), None)
    if not track:
        raise HTTPException(status_code=404, detail=f"Track {request.track_id} not found")

    clip = Clip(
        track_id=request.track_id,
        source_path=request.source_path,
        start_time=request.start_time,
        end_time=request.start_time + request.duration,
        name=request.name or "Clip",
    )
    track.clips.append(clip)
    _update_timeline_duration(timeline)
    await _persist(timeline, undo, redo)
    logger.info(f"Added clip: {clip.name} to track {track.name}")
    return clip


@router.post(
    "/clips/delete", response_model=DeleteResponse, dependencies=[Depends(require_auth_if_enabled)]
)
async def delete_clip(request: DeleteRequest):
    """Delete a clip from the timeline."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    for track in timeline.tracks:
        original_count = len(track.clips)
        track.clips = [c for c in track.clips if c.id != request.id]
        if len(track.clips) < original_count:
            _update_timeline_duration(timeline)
            await _persist(timeline, undo, redo)
            logger.info(f"Deleted clip: {request.id}")
            return DeleteResponse(success=True, deleted_id=request.id)

    raise HTTPException(status_code=404, detail=f"Clip {request.id} not found")


@router.put(
    "/clips/{clip_id}/move", response_model=Clip, dependencies=[Depends(require_auth_if_enabled)]
)
async def move_clip(clip_id: str, request: MoveClipRequest):
    """Move a clip to a new position or track."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    clip = None
    source_track = None

    # Find the clip
    for track in timeline.tracks:
        for c in track.clips:
            if c.id == clip_id:
                clip = c
                source_track = track
                break
        if clip:
            break

    if not clip or not source_track:
        raise HTTPException(status_code=404, detail=f"Clip {clip_id} not found")

    duration = clip.end_time - clip.start_time
    clip.start_time = request.new_start_time
    clip.end_time = request.new_start_time + duration

    # Move to different track if specified
    if request.new_track_id and request.new_track_id != source_track.id:
        target_track = next((t for t in timeline.tracks if t.id == request.new_track_id), None)
        if not target_track:
            raise HTTPException(
                status_code=404, detail=f"Target track {request.new_track_id} not found"
            )
        source_track.clips.remove(clip)
        clip.track_id = request.new_track_id
        target_track.clips.append(clip)

    _update_timeline_duration(timeline)
    await _persist(timeline, undo, redo)
    logger.info(f"Moved clip: {clip_id}")
    return clip


@router.put(
    "/clips/{clip_id}/trim", response_model=Clip, dependencies=[Depends(require_auth_if_enabled)]
)
async def trim_clip(clip_id: str, request: TrimClipRequest):
    """Trim a clip's start or end time."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    clip = None

    for track in timeline.tracks:
        for c in track.clips:
            if c.id == clip_id:
                clip = c
                break
        if clip:
            break

    if not clip:
        raise HTTPException(status_code=404, detail=f"Clip {clip_id} not found")

    if request.new_start is not None:
        delta = request.new_start - clip.start_time
        clip.start_time = request.new_start
        clip.source_start = max(0.0, clip.source_start + delta)
    if request.new_end is not None:
        clip.end_time = request.new_end

    _update_timeline_duration(timeline)
    await _persist(timeline, undo, redo)
    logger.info(f"Trimmed clip: {clip_id}")
    return clip


@router.post(
    "/clips/{clip_id}/split",
    response_model=SplitClipResponse,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def split_clip(clip_id: str, request: SplitClipRequest):
    """Split a clip at a given position."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    clip = None
    track = None

    for t in timeline.tracks:
        for c in t.clips:
            if c.id == clip_id:
                clip = c
                track = t
                break
        if clip:
            break

    if not clip or not track:
        raise HTTPException(status_code=404, detail=f"Clip {clip_id} not found")

    if request.split_position <= clip.start_time or request.split_position >= clip.end_time:
        raise HTTPException(status_code=400, detail="Split position must be within clip bounds")

    # Create the second clip
    clip_after = Clip(
        track_id=clip.track_id,
        start_time=request.split_position,
        end_time=clip.end_time,
        source_path=clip.source_path,
        source_start=clip.source_start + (request.split_position - clip.start_time),
        name=f"{clip.name} (2)",
        color=clip.color,
        volume=clip.volume,
        muted=clip.muted,
        locked=clip.locked,
        fade_in_seconds=clip.fade_in_seconds,
        fade_out_seconds=clip.fade_out_seconds,
    )

    # Update original clip
    clip.end_time = request.split_position
    clip_before = clip.model_copy()

    track.clips.append(clip_after)
    _update_timeline_duration(timeline)
    await _persist(timeline, undo, redo)
    logger.info(f"Split clip: {clip_id} at {request.split_position}")
    return SplitClipResponse(clip_before=clip_before, clip_after=clip_after)


@router.put(
    "/clips/{clip_id}/fade",
    response_model=Clip,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def set_clip_fade(clip_id: str, request: SetClipFadeRequest):
    """Set linear fade-in/out in seconds (applied at mixdown)."""
    timeline, undo, redo = await _hydrate()
    _push_undo_before_mutate(timeline, undo, redo)

    clip = None
    for track in timeline.tracks:
        for c in track.clips:
            if c.id == clip_id:
                clip = c
                break
        if clip:
            break

    if not clip:
        raise HTTPException(status_code=404, detail=f"Clip {clip_id} not found")

    clip.fade_in_seconds = max(0.0, float(request.fade_in_seconds))
    clip.fade_out_seconds = max(0.0, float(request.fade_out_seconds))
    await _persist(timeline, undo, redo)
    logger.info(
        "Set fade clip=%s in=%ss out=%ss", clip_id, clip.fade_in_seconds, clip.fade_out_seconds
    )
    return clip


@router.post("/playhead", dependencies=[Depends(require_auth_if_enabled)])
async def set_playhead(request: PlayheadRequest):
    """Set the playhead position."""
    timeline, undo, redo = await _hydrate()
    timeline.playhead_position = max(0.0, request.Position)
    await _persist(timeline, undo, redo)
    logger.debug(f"Set playhead to: {request.Position}")
    return {"success": True}


@router.post("/loop", dependencies=[Depends(require_auth_if_enabled)])
async def set_loop(request: LoopRequest):
    """Set the loop region."""
    timeline, undo, redo = await _hydrate()
    timeline.loop_start = request.Start
    timeline.loop_end = request.End
    await _persist(timeline, undo, redo)
    logger.debug(f"Set loop: {request.Start} - {request.End}")
    return {"success": True}


def _resolve_export_path(output_path: str, format: str) -> str:
    """Resolve export path, refusing repo-relative or unsafe paths (Cursor brick prevention)."""
    from pathlib import Path

    from backend.config.path_config import get_path

    path = Path(output_path)
    # Refuse relative paths (e.g. ".", "output.wav")
    if not path.is_absolute():
        safe_dir = get_path("artifacts")
        safe_dir.mkdir(parents=True, exist_ok=True)
        import uuid
        fallback = safe_dir / f"timeline_export_{uuid.uuid4().hex[:8]}.{format}"
        logger.warning(
            "Refusing relative export path '%s'; using safe path: %s",
            output_path,
            fallback,
        )
        return str(fallback)
    # Refuse paths under repo root
    try:
        repo_root = Path(__file__).resolve().parents[3]
        if path.resolve().is_relative_to(repo_root):
            safe_dir = get_path("artifacts")
            safe_dir.mkdir(parents=True, exist_ok=True)
            import uuid
            fallback = safe_dir / f"timeline_export_{uuid.uuid4().hex[:8]}.{format}"
            logger.warning(
                "Refusing export path inside repo '%s'; using safe path: %s",
                output_path,
                fallback,
            )
            return str(fallback)
    # ALLOWED: bare except - best effort, failure acceptable
    except (ValueError, OSError):
        pass
    return output_path


@router.post(
    "/export", response_model=ExportResponse, dependencies=[Depends(require_auth_if_enabled)]
)
async def export_timeline(request: ExportRequest):
    """Export the timeline to a file.

    Renders all audio clips from the timeline, mixing them according to their
    positions and volume settings, then exports to the specified format.
    """
    if request.apply_effects:
        if not request.project_id or not str(request.project_id).strip():
            raise HTTPException(
                status_code=422,
                detail="project_id is required when apply_effects is true",
            )
        if not request.effect_chain_id or not str(request.effect_chain_id).strip():
            raise HTTPException(
                status_code=422,
                detail="effect_chain_id is required when apply_effects is true",
            )

    timeline, _, _ = await _hydrate()
    sample_rate = request.sample_rate or timeline.sample_rate

    output_path = _resolve_export_path(request.output_path, request.format)
    logger.info(f"Export requested: {output_path} as {request.format}")

    # Render the timeline audio
    rendered_audio = await _render_timeline_audio(timeline, sample_rate)

    if rendered_audio is None and request.fallback_project_audio_id:
        import os

        from backend.services.audio_artifacts import AudioRegistry

        fb_path = AudioRegistry.get_path(request.fallback_project_audio_id.strip())
        if fb_path and os.path.exists(fb_path):
            fb_audio = await _load_audio_file(fb_path, sample_rate)
            if fb_audio is not None:
                rendered_audio = fb_audio
                logger.info(
                    "Timeline export using fallback_project_audio_id=%s",
                    request.fallback_project_audio_id,
                )

    if rendered_audio is None:
        raise HTTPException(
            status_code=400,
            detail="Timeline has no audible audio to export. Add clips with registered audio "
            "to the project timeline before exporting, or provide a valid fallback_project_audio_id.",
        )

    if request.apply_effects:
        from backend.services.timeline_effect_bake import apply_timeline_export_effect_chain

        rendered_audio = apply_timeline_export_effect_chain(
            chain_id=request.effect_chain_id.strip(),  # type: ignore[union-attr]
            project_id=request.project_id.strip(),
            audio=rendered_audio,
            sample_rate=sample_rate,
        )

    try:
        from backend.services.timeline_export_loudness import (
            ExportLoudnessError,
            LufsNormalizationUnavailableError,
            apply_timeline_export_loudness,
        )

        rendered_audio = apply_timeline_export_loudness(
            rendered_audio, sample_rate, request.lufs_preset
        )
    except ExportLoudnessError as e:
        raise HTTPException(status_code=422, detail=str(e)) from e
    except LufsNormalizationUnavailableError as e:
        raise HTTPException(status_code=503, detail=str(e)) from e

    # Write output file
    try:
        await _write_audio_output(rendered_audio, output_path, sample_rate, request.format)
        logger.info(f"Exported timeline to {output_path}")
    except Exception as e:
        logger.error(f"Failed to export timeline: {e}")
        raise HTTPException(status_code=500, detail=f"Export failed: {e}")

    return ExportResponse(
        success=True,
        output_path=output_path,
        duration=len(rendered_audio) / sample_rate if len(rendered_audio) > 0 else 0.0,
    )


@router.post("/undo", response_model=UndoResponse, dependencies=[Depends(require_auth_if_enabled)])
async def undo():
    """Undo the last operation."""
    state, undo_stack, redo_stack = await _hydrate()

    if not undo_stack:
        return UndoResponse(success=False, operation=None)

    # Save current state to redo stack
    redo_stack.append(state.model_copy(deep=True))

    # Restore previous state
    state = undo_stack.pop()
    await _persist(state, undo_stack, redo_stack)
    logger.info("Undo performed")
    return UndoResponse(success=True, operation="undo")


@router.post("/redo", response_model=UndoResponse, dependencies=[Depends(require_auth_if_enabled)])
async def redo():
    """Redo the last undone operation."""
    state, undo_stack, redo_stack = await _hydrate()

    if not redo_stack:
        return UndoResponse(success=False, operation=None)

    # Save current state to undo stack
    undo_stack.append(state.model_copy(deep=True))

    # Restore redo state
    state = redo_stack.pop()
    await _persist(state, undo_stack, redo_stack)
    logger.info("Redo performed")
    return UndoResponse(success=True, operation="redo")


@router.get(
    "/undo-redo-state",
    response_model=UndoRedoState,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def get_undo_redo_state():
    """Get the current undo/redo state."""
    _, undo_stack, redo_stack = await _hydrate()
    return UndoRedoState(
        can_undo=len(undo_stack) > 0,
        can_redo=len(redo_stack) > 0,
        undo_description="Previous state" if undo_stack else None,
        redo_description="Next state" if redo_stack else None,
    )
