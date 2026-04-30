"""
Dialogue Timeline Regeneration v1 — segment persistence, synthesis linkage,
library asset creation, and timeline clip insert/replace.

Orchestrates transcription JSON segments (no DB schema migration) with
SQLite-backed session timeline (D-001).
"""

from __future__ import annotations

import json
import logging
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from types import SimpleNamespace
from typing import Any

from backend.api.models_additional import VoiceSynthesizeRequest
from backend.core.exceptions import ServiceError
from backend.data.repositories.library_repository import (
    AssetType,
    LibraryAssetEntity,
    get_library_asset_repository,
)
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.services.audio_artifacts import AudioRegistry
from backend.services.synthesis_service import SynthesisService

logger = logging.getLogger(__name__)


def _find_segment_index(segments: list[Any], segment_id: str) -> int | None:
    for i, raw in enumerate(segments):
        if not isinstance(raw, dict):
            continue
        if str(raw.get("id", "")) == segment_id:
            return i
    return None


def segment_to_public_dict(*, transcript_id: str, segment: dict[str, Any]) -> dict[str, Any]:
    """Map persisted segment JSON to DialogueSegment API shape."""
    return {
        "transcript_id": transcript_id,
        "id": str(segment.get("id", "")),
        "text": segment.get("text", "") or "",
        "edited_text": segment.get("edited_text"),
        "start": float(segment.get("start", 0.0) or 0.0),
        "end": float(segment.get("end", 0.0) or 0.0),
        "speaker": segment.get("speaker"),
        "words": segment.get("words"),
        "status": segment.get("status") or "raw",
        "error_message": segment.get("error_message"),
        "audio_id": segment.get("audio_id"),
        "generated_audio_id": segment.get("generated_audio_id"),
        "library_asset_id": segment.get("library_asset_id"),
        "timeline_clip_id": segment.get("timeline_clip_id"),
        "profile_id": segment.get("profile_id"),
        "engine": segment.get("engine"),
        "routed_engine": segment.get("routed_engine"),
        "dialogue_provenance": segment.get("dialogue_provenance"),
    }


async def append_dialogue_segment(
    *,
    transcript_id: str,
    text: str,
    start: float,
    end: float,
    speaker: str | None,
) -> dict[str, Any]:
    repo = get_transcription_repository()
    t = await repo.get_transcription(transcript_id)
    if not t:
        raise LookupError("transcription_not_found")
    segments = [s for s in (t.get("segments") or []) if isinstance(s, dict)]
    seg_id = str(uuid.uuid4())
    row: dict[str, Any] = {
        "id": seg_id,
        "text": text,
        "start": start,
        "end": end,
        "status": "raw",
    }
    if speaker:
        row["speaker"] = speaker
    segments.append(row)
    updated = await repo.update_transcription(transcript_id, segments=segments)
    if not updated:
        raise LookupError("transcription_not_found")
    return segment_to_public_dict(transcript_id=transcript_id, segment=row)


async def get_dialogue_segment(*, transcript_id: str, segment_id: str) -> dict[str, Any]:
    repo = get_transcription_repository()
    t = await repo.get_transcription(transcript_id)
    if not t:
        raise LookupError("transcription_not_found")
    segments = t.get("segments") or []
    idx = _find_segment_index(segments, segment_id)
    if idx is None:
        raise LookupError("segment_not_found")
    seg = segments[idx]
    assert isinstance(seg, dict)
    return segment_to_public_dict(transcript_id=transcript_id, segment=seg)


async def edit_dialogue_segment(
    *,
    transcript_id: str,
    segment_id: str,
    edited_text: str,
) -> dict[str, Any]:
    repo = get_transcription_repository()
    t = await repo.get_transcription(transcript_id)
    if not t:
        raise LookupError("transcription_not_found")
    segments = [dict(s) if isinstance(s, dict) else s for s in (t.get("segments") or [])]
    idx = _find_segment_index(segments, segment_id)
    if idx is None:
        raise LookupError("segment_not_found")
    seg = segments[idx]
    if not isinstance(seg, dict):
        raise LookupError("segment_not_found")
    original_start = float(seg.get("start", 0.0) or 0.0)
    original_end = float(seg.get("end", 0.0) or 0.0)
    seg["edited_text"] = edited_text.strip()
    seg["status"] = "edited"
    seg["start"] = original_start
    seg["end"] = original_end
    segments[idx] = seg
    updated = await repo.update_transcription(transcript_id, segments=segments)
    if not updated:
        raise LookupError("transcription_not_found")
    return segment_to_public_dict(transcript_id=transcript_id, segment=seg)


async def _delete_timeline_clip(clip_id: str, session_id: str) -> None:
    from backend.api.routes import timeline as timeline_routes

    timeline, undo, redo, base_rev = await timeline_routes._hydrate(session_id)
    timeline_routes._push_undo_before_mutate(timeline, undo, redo)
    removed = False
    for track in timeline.tracks:
        before = len(track.clips)
        track.clips = [c for c in track.clips if c.id != clip_id]
        if len(track.clips) < before:
            removed = True
            break
    if removed:
        timeline_routes._update_timeline_duration(timeline)
        await timeline_routes._persist(timeline, undo, redo, session_id, base_rev)


async def _insert_timeline_clip(
    *,
    track_id: str,
    source_path: str,
    start_time: float,
    duration: float,
    name: str,
    metadata: dict[str, Any],
    session_id: str,
) -> str:
    from backend.api.routes import timeline as timeline_routes

    timeline, undo, redo, base_rev = await timeline_routes._hydrate(session_id)
    timeline_routes._push_undo_before_mutate(timeline, undo, redo)
    track = next((t for t in timeline.tracks if t.id == track_id), None)
    if track is None:
        raise LookupError("track_not_found")
    clip = timeline_routes.Clip(
        track_id=track_id,
        source_path=source_path,
        start_time=start_time,
        end_time=start_time + duration,
        name=name,
        metadata=metadata,
    )
    track.clips.append(clip)
    timeline_routes._update_timeline_duration(timeline)
    await timeline_routes._persist(timeline, undo, redo, session_id, base_rev)
    return clip.id


@dataclass(frozen=True)
class RegenerateOutcome:
    """Structured result from synchronous dialogue regeneration."""

    audio_id: str
    generated_audio_id: str | None
    library_asset_id: str
    timeline_clip_id: str
    routed_engine: str
    duration: float
    segment: dict[str, Any]


async def regenerate_dialogue_segment(
    *,
    transcript_id: str,
    segment_id: str,
    profile_id: str,
    engine: str | None,
    track_id: str,
    project_id: str | None,
    session_id: str,
    replace_existing_clip: bool,
    http_request: Any,
) -> RegenerateOutcome:
    """
    Run synthesis for segment edited text; persist provenance; library asset; timeline clip.
    """
    repo = get_transcription_repository()
    t = await repo.get_transcription(transcript_id)
    if not t:
        raise LookupError("transcription_not_found")
    segments = [dict(s) if isinstance(s, dict) else s for s in (t.get("segments") or [])]
    idx = _find_segment_index(segments, segment_id)
    if idx is None:
        raise LookupError("segment_not_found")
    seg = segments[idx]
    if not isinstance(seg, dict):
        raise LookupError("segment_not_found")

    text = (seg.get("edited_text") or seg.get("text") or "").strip()
    if not text:
        raise ValueError("empty_synthesis_text")

    synth_req = VoiceSynthesizeRequest(
        profile_id=profile_id,
        text=text,
        engine=engine,
        project_id=project_id,
        session_id=session_id,
    )
    req = SimpleNamespace(state=SimpleNamespace(request_id=str(uuid.uuid4()), voice_policy=None))

    try:
        resp = await SynthesisService.synthesize(synth_req, http_request, None)
    except ServiceError as se:
        seg["status"] = "failed"
        seg["error_message"] = str(se.detail)
        segments[idx] = seg
        await repo.update_transcription(transcript_id, segments=segments)
        raise

    audio_id = str(resp.audio_id)
    generated = str(resp.generated_audio_id or resp.audio_id)
    routed = str(resp.routed_engine or (engine or ""))
    duration = float(resp.duration or 0.0)

    resolved = AudioRegistry.get_path(audio_id)
    if not resolved:
        seg["status"] = "failed"
        seg["error_message"] = "synthesis_succeeded_but_audio_not_registered"
        segments[idx] = seg
        await repo.update_transcription(transcript_id, segments=segments)
        raise ServiceError(
            502,
            "Synthesis returned audio_id but AudioRegistry has no file path (registration gap).",
        )

    created_at = datetime.now(timezone.utc).isoformat()
    provenance: dict[str, Any] = {
        "project_id": project_id,
        "session_id": session_id,
        "transcript_id": transcript_id,
        "segment_id": segment_id,
        "profile_id": profile_id,
        "requested_engine": engine,
        "routed_engine": routed,
        "source_text": seg.get("text"),
        "edited_text": seg.get("edited_text"),
        "created_at": created_at,
        "audio_id": audio_id,
        "generated_audio_id": generated,
    }

    asset_repo = get_library_asset_repository()
    asset_id = str(uuid.uuid4())
    now = datetime.now()
    meta = {
        "source": "dialogue_segment_regeneration",
        "transcript_id": transcript_id,
        "segment_id": segment_id,
        "generated_audio_id": generated,
        "profile_id": profile_id,
        "routed_engine": routed,
        "requested_engine": engine,
        "edited_text": seg.get("edited_text"),
        "original_text": seg.get("text"),
    }
    entity = LibraryAssetEntity(
        id=asset_id,
        name=f"dialogue_{segment_id[:12]}.wav",
        type=AssetType.AUDIO.value,
        path=resolved,
        folder_id=None,
        tags=json.dumps([]),
        metadata=json.dumps(meta),
        size=0,
        duration=duration,
        thumbnail_url=None,
        created_at=now,
        updated_at=now,
        modified_at=now,
    )
    await asset_repo.create(entity)

    old_clip = str(seg.get("timeline_clip_id") or "").strip()
    if replace_existing_clip and old_clip:
        await _delete_timeline_clip(old_clip, session_id)

    clip_meta: dict[str, Any] = {
        "transcript_id": transcript_id,
        "segment_id": segment_id,
        "generated_audio_id": generated,
        "library_asset_id": asset_id,
        "audio_id": audio_id,
        "source": "dialogue_segment_regeneration",
    }
    start_time = float(seg.get("start", 0.0) or 0.0)
    clip_duration = duration if duration > 0 else max(0.01, float(seg.get("end", 0.0) or 0.0) - start_time)
    clip_id = await _insert_timeline_clip(
        track_id=track_id,
        source_path=resolved,
        start_time=start_time,
        duration=clip_duration,
        name=f"Dialogue {segment_id[:8]}",
        metadata=clip_meta,
        session_id=session_id,
    )

    seg["audio_id"] = audio_id
    seg["generated_audio_id"] = generated
    seg["library_asset_id"] = asset_id
    seg["timeline_clip_id"] = clip_id
    seg["profile_id"] = profile_id
    seg["engine"] = engine
    seg["routed_engine"] = routed
    seg["status"] = "regenerated"
    seg["error_message"] = None
    seg["dialogue_provenance"] = provenance
    segments[idx] = seg
    await repo.update_transcription(transcript_id, segments=segments)

    refreshed = await repo.get_transcription(transcript_id)
    assert refreshed is not None
    segs2 = refreshed.get("segments") or []
    j = _find_segment_index(segs2, segment_id)
    assert j is not None
    final_seg = segs2[j]
    assert isinstance(final_seg, dict)

    return RegenerateOutcome(
        audio_id=audio_id,
        generated_audio_id=generated,
        library_asset_id=asset_id,
        timeline_clip_id=clip_id,
        routed_engine=routed,
        duration=duration,
        segment=segment_to_public_dict(transcript_id=transcript_id, segment=final_seg),
    )
