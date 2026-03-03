"""Voice audio retrieval routes - serve synthesized audio files."""

from __future__ import annotations

import logging
import os

from fastapi import HTTPException
from fastapi.responses import FileResponse

from backend.services.audio_artifacts import AudioRegistry

from ...optimization import cache_response
from ._shared import router

logger = logging.getLogger(__name__)


@router.get("/audio/{audio_id}")
@cache_response(ttl=300)  # Cache for 5 minutes (audio files are static once created)
async def get_audio(audio_id: str):
    """
    Retrieve synthesized audio file.

    Returns the audio file as a WAV stream for playback.
    """
    file_path = AudioRegistry.get_path(audio_id)
    if not file_path:
        raise HTTPException(status_code=404, detail=f"Audio not found: {audio_id}")

    if not os.path.exists(file_path):
        AudioRegistry.remove(audio_id)
        raise HTTPException(status_code=404, detail="Audio file not found on disk")

    return FileResponse(
        file_path,
        media_type="audio/wav",
        filename=f"{audio_id}.wav",
        headers={"Content-Disposition": (f'attachment; filename="{audio_id}.wav"')},
    )
