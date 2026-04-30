"""
Dialogue Timeline Regeneration v1.1 — transcript segment contract, edit,
synchronous regeneration with library + timeline linkage, and batch
``POST /api/dialogue/transcripts/{id}/create-timeline-clips``.
"""

from __future__ import annotations

from typing import Annotated, Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import Field

from backend.api.middleware.auth_middleware import require_auth_if_enabled
from backend.api.models import VoiceStudioBaseModel
from backend.core.exceptions import ServiceError
from backend.project.timeline.session_repository import DEFAULT_SESSION_ID
from backend.services.dialogue_segment_workflow import (
    RegenerateOutcome,
    append_dialogue_segment,
    create_timeline_clips_from_transcript,
    edit_dialogue_segment,
    get_dialogue_segment,
    regenerate_dialogue_segment,
)

router = APIRouter(prefix="/api/dialogue", tags=["dialogue"])

TranscriptIdQuery = Annotated[
    str,
    Query(
        min_length=1,
        description="Transcription / transcript row id that owns the segment.",
    ),
]


class DialogueSegment(VoiceStudioBaseModel):
    """API view of a persisted transcript segment including dialogue extensions."""

    transcript_id: str
    id: str
    text: str
    edited_text: str | None = None
    start: float = 0.0
    end: float = 0.0
    speaker: str | None = None
    words: list[Any] | None = None
    status: str = "raw"
    error_message: str | None = None
    last_failure_stage: str | None = None
    last_failed_at: str | None = None
    audio_id: str | None = None
    generated_audio_id: str | None = None
    library_asset_id: str | None = None
    timeline_clip_id: str | None = None
    profile_id: str | None = None
    engine: str | None = None
    routed_engine: str | None = None
    dialogue_provenance: dict[str, Any] | None = None
    project_id: str | None = None
    session_id: str | None = None
    source_audio_id: str | None = None
    source_path: str | None = None


class CreateDialogueSegmentRequest(VoiceStudioBaseModel):
    transcript_id: str = Field(min_length=1)
    text: str = Field(min_length=1, max_length=10000)
    start: float = 0.0
    end: float = 1.0
    speaker: str | None = None
    project_id: str | None = Field(default=None, max_length=100)
    session_id: str | None = Field(default=None, max_length=100)


class EditDialogueSegmentBody(VoiceStudioBaseModel):
    edited_text: str = Field(min_length=1, max_length=10000)


class RegenerateDialogueSegmentBody(VoiceStudioBaseModel):
    transcript_id: str = Field(min_length=1)
    profile_id: str = Field(min_length=1, max_length=100)
    track_id: str | None = Field(default=None, max_length=100)
    engine: str | None = Field(default=None, max_length=50)
    project_id: str | None = Field(default=None, max_length=100)
    session_id: str | None = Field(default=None, max_length=100)
    replace_existing_clip: bool = False
    edited_text: str | None = Field(default=None, max_length=10000)


class RegenerateDialogueSegmentResponse(VoiceStudioBaseModel):
    project_id: str | None = None
    session_id: str = ""
    transcript_id: str = ""
    segment_id: str = ""
    status: str = ""
    audio_id: str
    generated_audio_id: str | None
    library_asset_id: str
    timeline_clip_id: str
    routed_engine: str
    duration: float
    segment: DialogueSegment


class CreateTimelineClipsFromTranscriptBody(VoiceStudioBaseModel):
    track_id: str = Field(min_length=1)
    session_id: str | None = Field(default=None, max_length=100)
    project_id: str | None = Field(default=None, max_length=100)
    replace_existing: bool = False


class CreateTimelineClipsFromTranscriptResponse(VoiceStudioBaseModel):
    transcript_id: str
    session_id: str
    track_id: str
    created_clip_ids: list[str]
    segment_count: int
    status: str


def _map_lookup(exc: LookupError) -> HTTPException:
    key = str(exc.args[0]) if exc.args else ""
    if key == "transcription_not_found":
        return HTTPException(status_code=404, detail="Transcription not found.")
    if key == "segment_not_found":
        return HTTPException(status_code=404, detail="Segment not found.")
    if key == "track_not_found":
        return HTTPException(status_code=404, detail="Timeline track not found.")
    if key == "clip_not_found_on_timeline":
        return HTTPException(status_code=404, detail="Timeline clip not found on session timeline.")
    return HTTPException(status_code=404, detail="Not found.")


@router.post(
    "/segments",
    response_model=DialogueSegment,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def create_dialogue_segment(body: CreateDialogueSegmentRequest):
    """Append a dialogue segment to an existing transcription."""
    try:
        row = await append_dialogue_segment(
            transcript_id=body.transcript_id,
            text=body.text,
            start=body.start,
            end=body.end,
            speaker=body.speaker,
            project_id=body.project_id,
            session_id=body.session_id,
        )
        return DialogueSegment.model_validate(row)
    except LookupError as e:
        raise _map_lookup(e) from e


@router.get(
    "/segments/{segment_id}",
    response_model=DialogueSegment,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def read_dialogue_segment(segment_id: str, transcript_id: TranscriptIdQuery):
    """Fetch one segment with dialogue linkage fields."""
    try:
        row = await get_dialogue_segment(transcript_id=transcript_id, segment_id=segment_id)
        return DialogueSegment.model_validate(row)
    except LookupError as e:
        raise _map_lookup(e) from e


@router.put(
    "/segments/{segment_id}/edit",
    response_model=DialogueSegment,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def put_dialogue_segment_edit(
    segment_id: str,
    body: EditDialogueSegmentBody,
    transcript_id: TranscriptIdQuery,
):
    """Set edited_text and status=edited without changing original text or timing."""
    try:
        row = await edit_dialogue_segment(
            transcript_id=transcript_id,
            segment_id=segment_id,
            edited_text=body.edited_text,
        )
        return DialogueSegment.model_validate(row)
    except LookupError as e:
        raise _map_lookup(e) from e


@router.post(
    "/segments/{segment_id}/regenerate",
    response_model=RegenerateDialogueSegmentResponse,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def post_dialogue_segment_regenerate(
    segment_id: str,
    body: RegenerateDialogueSegmentBody,
    request: Request,
):
    """
    Synchronously regenerate audio for a segment, create a library asset,
    insert (or replace) a timeline clip, and persist linkage on the segment.
    """
    session = (body.session_id or "").strip() or DEFAULT_SESSION_ID
    raw_track = (body.track_id or "").strip() or None
    try:
        out: RegenerateOutcome = await regenerate_dialogue_segment(
            transcript_id=body.transcript_id,
            segment_id=segment_id,
            profile_id=body.profile_id,
            engine=body.engine,
            track_id=raw_track,
            project_id=body.project_id,
            session_id=session,
            replace_existing_clip=body.replace_existing_clip,
            http_request=request,
            edited_text_override=body.edited_text,
            raw_request_track_id=raw_track,
        )
        return RegenerateDialogueSegmentResponse(
            project_id=out.project_id,
            session_id=out.session_id,
            transcript_id=out.transcript_id,
            segment_id=out.segment_id,
            status=out.status,
            audio_id=out.audio_id,
            generated_audio_id=out.generated_audio_id,
            library_asset_id=out.library_asset_id,
            timeline_clip_id=out.timeline_clip_id,
            routed_engine=out.routed_engine,
            duration=out.duration,
            segment=DialogueSegment.model_validate(out.segment),
        )
    except LookupError as e:
        raise _map_lookup(e) from e
    except ValueError as e:
        if str(e) == "empty_synthesis_text":
            raise HTTPException(
                status_code=422,
                detail="Segment has no non-empty text or edited_text for synthesis.",
            ) from e
        if str(e) == "blank_edited_text_in_regenerate":
            raise HTTPException(
                status_code=422,
                detail="edited_text in regenerate must be non-empty when provided.",
            ) from e
        if str(e) == "track_id_required_for_new_dialogue_clip":
            raise HTTPException(
                status_code=422,
                detail={
                    "code": "track_id_required_for_new_dialogue_clip",
                    "message": "track_id is required unless replace_existing_clip is true and the segment has a timeline_clip_id to derive the track.",
                },
            ) from e
        if str(e) == "cross_track_dialogue_replace_not_supported":
            raise HTTPException(
                status_code=422,
                detail={
                    "code": "cross_track_dialogue_replace_not_supported",
                    "message": "Replacing a dialogue clip on a different track than where the clip exists is not supported.",
                },
            ) from e
        raise
    except ServiceError as se:
        raise HTTPException(status_code=se.status_code, detail=se.detail) from se


@router.post(
    "/transcripts/{transcript_id}/create-timeline-clips",
    response_model=CreateTimelineClipsFromTranscriptResponse,
    dependencies=[Depends(require_auth_if_enabled)],
)
async def post_create_timeline_clips_from_transcript(
    transcript_id: str,
    body: CreateTimelineClipsFromTranscriptBody,
):
    """Create one timeline clip per transcript segment (placeholder when no audio path)."""
    session = (body.session_id or "").strip() or DEFAULT_SESSION_ID
    try:
        row = await create_timeline_clips_from_transcript(
            transcript_id=transcript_id,
            track_id=body.track_id,
            session_id=session,
            project_id=body.project_id,
            replace_existing=body.replace_existing,
        )
        return CreateTimelineClipsFromTranscriptResponse.model_validate(row)
    except LookupError as e:
        raise _map_lookup(e) from e
    except HTTPException:
        raise
