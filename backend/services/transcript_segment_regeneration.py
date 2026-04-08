"""
GAP-046: background worker for single-segment transcript regeneration.

Registers canonical job_history rows; runs SynthesisService; merges artifact metadata into job.
"""

from __future__ import annotations

import asyncio
import json
import logging
from types import SimpleNamespace
from typing import Any

from backend.api.models_additional import VoiceSynthesizeRequest
from backend.data.repositories.job_repository import get_job_repository
from backend.data.repositories.transcription_repository import get_transcription_repository
from backend.services.synthesis_service import SynthesisService

logger = logging.getLogger(__name__)


async def _merge_job_metadata(job_id: str, extra: dict[str, Any]) -> None:
    repo = get_job_repository()
    ent = await repo.get_by_id(job_id)
    if ent is None:
        return
    meta = ent.get_metadata()
    meta.update(extra)
    await repo.update(job_id, {"metadata": json.dumps(meta)})


async def run_transcript_segment_regeneration_job(
    job_id: str,
    *,
    project_id: str,
    track_id: str,
    clip_id: str,
    transcription_id: str,
    segment_id: str,
    replacement_text: str | None,
    profile_id: str,
    engine: str | None,
    track_store: Any,
) -> None:
    """Execute regeneration for one segment; updates job row on success/failure."""
    from backend.services.canonical_job_lifecycle import (
        complete_job,
        fail_job,
        mark_job_running,
        update_job_progress,
    )
    from backend.core.exceptions import ServiceError

    t_repo = get_transcription_repository()

    try:
        await mark_job_running(job_id)
        await update_job_progress(job_id, 0.08, "Validating targets")

        tdata = await t_repo.get_transcription(transcription_id)
        if not tdata:
            await fail_job(job_id, "Transcription not found.")
            return
        segs = tdata.get("segments") or []
        seg = next((s for s in segs if str(s.get("id", "")) == segment_id), None)
        if seg is None:
            await fail_job(job_id, "Segment not found on transcription.")
            return

        track_data = track_store.get_track(project_id, track_id)
        if track_data is None:
            await fail_job(job_id, "Track not found.")
            return
        clips = track_data.get("clips") or []
        clip = next((c for c in clips if str(c.get("id", "")) == clip_id), None)
        if clip is None:
            await fail_job(job_id, "Clip not found on track.")
            return

        text = (replacement_text if replacement_text is not None else seg.get("text") or "").strip()
        if not text:
            await fail_job(job_id, "Synthesis text is empty.")
            return

        await update_job_progress(job_id, 0.2, "Synthesizing")

        synth_req = VoiceSynthesizeRequest(
            profile_id=profile_id,
            text=text,
            engine=engine,
        )
        req = SimpleNamespace(state=SimpleNamespace(request_id=job_id, voice_policy=None))

        try:
            resp = await SynthesisService.synthesize(synth_req, req, None)
        except ServiceError as se:
            await fail_job(job_id, str(se.detail))
            return

        await _merge_job_metadata(
            job_id,
            {
                "audio_url": resp.audio_url,
                "duration_seconds": float(resp.duration),
                "project_id": project_id,
                "track_id": track_id,
                "clip_id": clip_id,
                "transcription_id": transcription_id,
                "segment_id": segment_id,
            },
        )
        await complete_job(job_id, result_id=resp.audio_id)
    except asyncio.CancelledError:
        raise
    except Exception as e:
        logger.error("regenerate segment job failed: %s", e, exc_info=True)
        await fail_job(job_id, f"Regeneration failed: {e!s}")
