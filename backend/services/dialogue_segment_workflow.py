"""
Dialogue Timeline Regeneration v1.1 — segment identity, synthesis linkage,
library asset creation, timeline clip insert/replace, and transcript batch clips.

Orchestrates transcription JSON segments (no DB schema migration) with
SQLite-backed session timeline (D-001).
"""

from __future__ import annotations

import hashlib
import json
import logging
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from fastapi import HTTPException

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


def _detail_to_str(detail: object) -> str:
    if isinstance(detail, str):
        return detail
    try:
        return json.dumps(detail, default=str)
    except (TypeError, ValueError):
        return str(detail)


def _artifact_fingerprint(resolved: str) -> tuple[str, int, str]:
    """Return (absolute_path, size_bytes, sha256_hex)."""
    p = Path(resolved)
    data = p.read_bytes()
    return str(p.resolve()), len(data), hashlib.sha256(data).hexdigest()


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
        "project_id": segment.get("project_id"),
        "session_id": segment.get("session_id"),
        "source_audio_id": segment.get("source_audio_id"),
        "source_path": segment.get("source_path"),
        "last_failure_stage": segment.get("last_failure_stage"),
        "last_failed_at": segment.get("last_failed_at"),
    }


def _inherit_transcription_context(
    t: dict[str, Any], row: dict[str, Any], *, project_id: str | None, session_id: str | None
) -> None:
    """Fill segment identity from request + transcription row."""
    tp = t.get("project_id")
    eff_p = (project_id or "").strip() or (str(tp).strip() if tp else "") or None
    if eff_p:
        row["project_id"] = eff_p
    if session_id and str(session_id).strip():
        row["session_id"] = str(session_id).strip()
    aid = str(t.get("audio_id") or "").strip()
    if aid and not row.get("source_audio_id"):
        row["source_audio_id"] = aid


async def append_dialogue_segment(
    *,
    transcript_id: str,
    text: str,
    start: float,
    end: float,
    speaker: str | None,
    project_id: str | None,
    session_id: str | None,
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
    _inherit_transcription_context(t, row, project_id=project_id, session_id=session_id)
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


def _find_track_id_for_clip_on_timeline(timeline: Any, clip_id: str) -> str | None:
    """Return track id containing the clip, or None if not found."""
    for tr in timeline.tracks:
        for c in tr.clips:
            if c.id == clip_id:
                return tr.id
    return None


async def _derive_track_id_for_clip(session_id: str, clip_id: str) -> str | None:
    from backend.api.routes import timeline as timeline_routes

    timeline, _, _, _ = await timeline_routes._hydrate(session_id)
    return _find_track_id_for_clip_on_timeline(timeline, clip_id)


async def _replace_timeline_clip_atomic(
    *,
    session_id: str,
    resolved_track_id: str,
    old_clip_id: str,
    source_path: str | None,
    start_time: float,
    clip_duration: float,
    name: str,
    metadata: dict[str, Any],
) -> str:
    """Remove old clip and append replacement in one hydrate + single persist."""
    from backend.api.routes import timeline as timeline_routes

    timeline, undo, redo, base_rev = await timeline_routes._hydrate(session_id)
    timeline_routes._push_undo_before_mutate(timeline, undo, redo)
    old_tid = _find_track_id_for_clip_on_timeline(timeline, old_clip_id)
    if old_tid is None:
        raise LookupError("clip_not_found_on_timeline")
    if old_tid != resolved_track_id:
        raise ValueError("cross_track_dialogue_replace_not_supported")
    old_track = next((t for t in timeline.tracks if t.id == old_tid), None)
    if old_track is None:
        raise LookupError("track_not_found")
    old_track.clips = [c for c in old_track.clips if c.id != old_clip_id]
    clip = timeline_routes.Clip(
        track_id=resolved_track_id,
        source_path=source_path,
        start_time=start_time,
        end_time=start_time + clip_duration,
        name=name,
        metadata=metadata,
    )
    old_track.clips.append(clip)
    timeline_routes._update_timeline_duration(timeline)
    await timeline_routes._persist(timeline, undo, redo, session_id, base_rev)
    return clip.id


async def _insert_timeline_clip(
    *,
    track_id: str,
    source_path: str | None,
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


async def _persist_segment_failed(
    repo: Any,
    transcript_id: str,
    segments: list[Any],
    idx: int,
    seg: dict[str, Any],
    message: str,
    *,
    clear_timeline_clip_id: bool,
    restore_linkage: dict[str, Any] | None = None,
    last_failure_stage: str | None = None,
) -> None:
    seg["status"] = "failed"
    seg["error_message"] = message
    now_iso = datetime.now(timezone.utc).isoformat()
    if last_failure_stage:
        seg["last_failure_stage"] = last_failure_stage
        seg["last_failed_at"] = now_iso
    if restore_linkage:
        for k, v in restore_linkage.items():
            seg[k] = v
    elif clear_timeline_clip_id:
        seg["timeline_clip_id"] = None
    segments[idx] = seg
    await repo.update_transcription(transcript_id, segments=segments)


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
    project_id: str | None
    session_id: str
    transcript_id: str
    segment_id: str
    status: str


async def regenerate_dialogue_segment(
    *,
    transcript_id: str,
    segment_id: str,
    profile_id: str,
    engine: str | None,
    track_id: str | None,
    project_id: str | None,
    session_id: str,
    replace_existing_clip: bool,
    http_request: Any,
    edited_text_override: str | None = None,
    raw_request_track_id: str | None = None,
) -> RegenerateOutcome:
    """
    Run synthesis; persist provenance; library asset; timeline clip.
    Fails closed: does not mark regenerated if library or timeline steps fail.
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

    if edited_text_override is not None:
        stripped = edited_text_override.strip()
        if not stripped:
            raise ValueError("blank_edited_text_in_regenerate")
        seg["edited_text"] = stripped
        seg["status"] = "edited"

    _eff = (project_id or "").strip() or str(seg.get("project_id") or "").strip()
    _eff = _eff or str(t.get("project_id") or "").strip()
    eff_project: str | None = _eff if _eff else None
    if eff_project:
        seg["project_id"] = eff_project
    if session_id and str(session_id).strip():
        seg["session_id"] = str(session_id).strip()

    linkage_snap: dict[str, Any] = {
        "timeline_clip_id": seg.get("timeline_clip_id"),
        "library_asset_id": seg.get("library_asset_id"),
        "generated_audio_id": seg.get("generated_audio_id"),
        "audio_id": seg.get("audio_id"),
    }

    req_track = (track_id or "").strip() or None
    raw_track = (raw_request_track_id or "").strip() or None
    old_clip = str(seg.get("timeline_clip_id") or "").strip() or None

    resolved_track_id: str | None = None
    if req_track:
        resolved_track_id = req_track
    elif replace_existing_clip and old_clip:
        derived = await _derive_track_id_for_clip(session_id, old_clip)
        if not derived:
            raise ValueError("track_id_required_for_new_dialogue_clip")
        resolved_track_id = derived
    else:
        raise ValueError("track_id_required_for_new_dialogue_clip")

    if replace_existing_clip and old_clip and raw_track:
        old_track_for_clip = await _derive_track_id_for_clip(session_id, old_clip)
        if old_track_for_clip and raw_track != old_track_for_clip:
            raise ValueError("cross_track_dialogue_replace_not_supported")

    synth_body = (seg.get("edited_text") or seg.get("text") or "").strip()
    if not synth_body:
        raise ValueError("empty_synthesis_text")

    synth_req = VoiceSynthesizeRequest(
        profile_id=profile_id,
        text=synth_body,
        engine=engine,
        project_id=eff_project,
        session_id=session_id,
    )

    try:
        resp = await SynthesisService.synthesize(synth_req, http_request, None)
    except ServiceError as se:
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            _detail_to_str(se.detail),
            clear_timeline_clip_id=False,
            last_failure_stage="synthesis",
        )
        raise

    audio_id = str(resp.audio_id)
    generated = str(resp.generated_audio_id or resp.audio_id)
    routed = str(resp.routed_engine or (engine or ""))
    duration = float(resp.duration or 0.0)

    resolved = AudioRegistry.get_path(audio_id)
    if not resolved:
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            "synthesis_succeeded_but_audio_not_registered",
            clear_timeline_clip_id=False,
            last_failure_stage="registry",
        )
        raise ServiceError(
            502,
            "Synthesis returned audio_id but AudioRegistry has no file path (registration gap).",
        )

    artifact_path, artifact_size_bytes, artifact_sha256 = _artifact_fingerprint(resolved)
    created_at = datetime.now(timezone.utc).isoformat()
    source_audio_id = str(seg.get("source_audio_id") or "").strip() or None
    source_path_val = seg.get("source_path")
    source_path_str = str(source_path_val).strip() if source_path_val else None

    provenance: dict[str, Any] = {
        "project_id": eff_project,
        "session_id": session_id,
        "transcript_id": transcript_id,
        "segment_id": segment_id,
        "source_audio_id": source_audio_id,
        "source_path": source_path_str,
        "profile_id": profile_id,
        "requested_engine": engine,
        "routed_engine": routed,
        "source_text": seg.get("text"),
        "edited_text": seg.get("edited_text"),
        "audio_id": audio_id,
        "generated_audio_id": generated,
        "artifact_path": artifact_path,
        "artifact_sha256": artifact_sha256,
        "artifact_size_bytes": artifact_size_bytes,
        "duration_seconds": duration,
        "created_at": created_at,
    }

    asset_repo = get_library_asset_repository()
    asset_id = str(uuid.uuid4())
    now = datetime.now()
    lib_meta: dict[str, Any] = {
        "source": "dialogue_segment_regeneration",
        "project_id": eff_project,
        "session_id": session_id,
        "transcript_id": transcript_id,
        "segment_id": segment_id,
        "source_audio_id": source_audio_id,
        "generated_audio_id": generated,
        "audio_id": audio_id,
        "profile_id": profile_id,
        "requested_engine": engine,
        "routed_engine": routed,
        "edited_text": seg.get("edited_text"),
        "original_text": seg.get("text"),
        "artifact_sha256": artifact_sha256,
        "artifact_size_bytes": artifact_size_bytes,
        "duration_seconds": duration,
    }
    entity = LibraryAssetEntity(
        id=asset_id,
        name=f"dialogue_{segment_id[:12]}.wav",
        type=AssetType.AUDIO.value,
        path=resolved,
        folder_id=None,
        tags=json.dumps([]),
        metadata=json.dumps(lib_meta),
        size=int(artifact_size_bytes),
        duration=duration,
        thumbnail_url=None,
        created_at=now,
        updated_at=now,
        modified_at=now,
    )
    try:
        await asset_repo.create(entity)
    except Exception as ex:
        logger.exception("library create failed: %s", ex)
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            f"library_create_failed:{ex!s}",
            clear_timeline_clip_id=False,
            last_failure_stage="library",
        )
        raise ServiceError(500, {"code": "LIBRARY_CREATE_FAILED", "message": str(ex)}) from ex

    clip_meta: dict[str, Any] = {
        **lib_meta,
        "library_asset_id": asset_id,
    }
    start_time = float(seg.get("start", 0.0) or 0.0)
    clip_duration = duration if duration > 0 else max(0.01, float(seg.get("end", 0.0) or 0.0) - start_time)
    assert resolved_track_id is not None
    try:
        if replace_existing_clip and old_clip:
            clip_id = await _replace_timeline_clip_atomic(
                session_id=session_id,
                resolved_track_id=resolved_track_id,
                old_clip_id=old_clip,
                source_path=resolved,
                start_time=start_time,
                clip_duration=clip_duration,
                name=f"Dialogue {segment_id[:8]}",
                metadata=clip_meta,
            )
        else:
            clip_id = await _insert_timeline_clip(
                track_id=resolved_track_id,
                source_path=resolved,
                start_time=start_time,
                duration=clip_duration,
                name=f"Dialogue {segment_id[:8]}",
                metadata=clip_meta,
                session_id=session_id,
            )
    except LookupError as le:
        await asset_repo.delete(asset_id, soft=True)
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            f"timeline_insert_failed:{le!s}",
            clear_timeline_clip_id=False,
            restore_linkage=linkage_snap,
            last_failure_stage="timeline",
        )
        raise
    except HTTPException as he:
        logger.warning("timeline insert HTTP error: %s", he.detail)
        await asset_repo.delete(asset_id, soft=True)
        msg = _detail_to_str(he.detail) if hasattr(he, "detail") else str(he)
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            f"timeline_insert_failed:{msg}",
            clear_timeline_clip_id=False,
            restore_linkage=linkage_snap,
            last_failure_stage="timeline",
        )
        raise ServiceError(he.status_code, {"code": "TIMELINE_INSERT_FAILED", "message": msg}) from he
    except Exception as ex:
        logger.exception("timeline insert failed: %s", ex)
        await asset_repo.delete(asset_id, soft=True)
        await _persist_segment_failed(
            repo,
            transcript_id,
            segments,
            idx,
            seg,
            f"timeline_insert_failed:{ex!s}",
            clear_timeline_clip_id=False,
            restore_linkage=linkage_snap,
            last_failure_stage="timeline",
        )
        raise ServiceError(500, {"code": "TIMELINE_INSERT_FAILED", "message": str(ex)}) from ex

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

    async def _persist_segment_success() -> None:
        await repo.update_transcription(transcript_id, segments=segments)

    try:
        await _persist_segment_success()
    except Exception as ex:
        logger.exception("transcription update after timeline success (primary): %s", ex)
        try:
            await _persist_segment_success()
        except Exception as ex2:
            logger.exception("transcription update retry failed: %s", ex2)
            raise ServiceError(
                500,
                {
                    "code": "SEGMENT_PERSIST_FAILED",
                    "message": str(ex2),
                    "detail": "Timeline clip was updated; segment JSON may be stale until transcription is repaired.",
                },
            ) from ex2

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
        project_id=eff_project,
        session_id=session_id,
        transcript_id=transcript_id,
        segment_id=segment_id,
        status=str(final_seg.get("status") or "regenerated"),
    )


def _resolve_clip_source_path(seg: dict[str, Any]) -> str | None:
    """Pick first registry-backed path from segment audio ids."""
    for key in ("audio_id", "generated_audio_id", "source_audio_id"):
        aid = str(seg.get(key) or "").strip()
        if not aid:
            continue
        p = AudioRegistry.get_path(aid)
        if p:
            return p
    return None


async def create_timeline_clips_from_transcript(
    *,
    transcript_id: str,
    track_id: str | None,
    session_id: str,
    project_id: str | None,
    replace_existing: bool,
    auto_create_track: bool = False,
) -> dict[str, Any]:
    """Create one timeline clip per transcript segment (placeholder if no audio).

    Single hydrate + persist so timeline state stays consistent on conflict.
    """
    from backend.api.routes import timeline as timeline_routes

    repo = get_transcription_repository()
    t = await repo.get_transcription(transcript_id)
    if not t:
        raise LookupError("transcription_not_found")

    timeline, undo, redo, base_rev = await timeline_routes._hydrate(session_id)
    timeline_routes._push_undo_before_mutate(timeline, undo, redo)

    resolved_track_id = (track_id or "").strip() or None
    track: timeline_routes.Track | None = None
    if resolved_track_id:
        track = next((tr for tr in timeline.tracks if tr.id == resolved_track_id), None)
        if track is None:
            raise LookupError("track_not_found")
    elif auto_create_track:
        dialogue_name = "Dialogue"
        track = next(
            (tr for tr in timeline.tracks if (tr.name or "").strip() == dialogue_name),
            None,
        )
        if track is None:
            new_track = timeline_routes.Track(
                name=dialogue_name,
                type="audio",
                order=len(timeline.tracks),
            )
            timeline.tracks.append(new_track)
            track = new_track
        resolved_track_id = track.id
    else:
        raise LookupError("track_id_required")

    segments = [dict(s) if isinstance(s, dict) else s for s in (t.get("segments") or [])]
    created: list[str] = []
    eff_project = (project_id or "").strip() or str(t.get("project_id") or "").strip() or None
    src_audio = str(t.get("audio_id") or "").strip() or None

    for i, seg in enumerate(segments):
        if not isinstance(seg, dict):
            continue
        sid = str(seg.get("id", ""))
        if not sid:
            continue
        st = float(seg.get("start", 0.0) or 0.0)
        en = float(seg.get("end", 0.0) or 0.0)
        dur = max(0.01, en - st)
        if eff_project:
            seg["project_id"] = eff_project
        if session_id:
            seg["session_id"] = session_id
        if src_audio and not seg.get("source_audio_id"):
            seg["source_audio_id"] = src_audio

        old_clip = str(seg.get("timeline_clip_id") or "").strip()
        if replace_existing and old_clip:
            track.clips = [c for c in track.clips if c.id != old_clip]
            seg["timeline_clip_id"] = None

        path = _resolve_clip_source_path(seg)
        playable = path is not None
        meta: dict[str, Any] = {
            "source": "dialogue_transcript_timeline_batch",
            "kind": "transcript_region",
            "playable": playable,
            "transcript_id": transcript_id,
            "segment_id": sid,
            "project_id": eff_project,
            "session_id": session_id,
            "source_audio_id": seg.get("source_audio_id"),
        }
        if not playable:
            meta["note"] = "no_registry_audio_for_segment"

        clip = timeline_routes.Clip(
            track_id=resolved_track_id,
            source_path=path,
            start_time=st,
            end_time=st + dur,
            name=f"Transcript {sid[:8]}",
            metadata=meta,
        )
        track.clips.append(clip)
        created.append(clip.id)
        seg["timeline_clip_id"] = clip.id
        segments[i] = seg

    timeline_routes._update_timeline_duration(timeline)
    await timeline_routes._persist(timeline, undo, redo, session_id, base_rev)
    await repo.update_transcription(transcript_id, segments=segments)
    return {
        "transcript_id": transcript_id,
        "session_id": session_id,
        "track_id": resolved_track_id,
        "created_clip_ids": created,
        "segment_count": len(created),
        "status": "ok",
    }
