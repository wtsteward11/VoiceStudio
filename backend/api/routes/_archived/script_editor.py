"""
Script Editor Routes

Endpoints for managing scripts and transcripts for voice synthesis.
"""

from __future__ import annotations

import logging
from datetime import datetime

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/script-editor", tags=["script-editor"])

# In-memory scripts storage (replace with database in production)
_scripts: dict[str, dict] = {}


class ScriptSegment(BaseModel):
    """A segment in a script."""

    id: str
    text: str
    start_time: float | None = None
    end_time: float | None = None
    speaker: str | None = None
    voice_profile_id: str | None = None
    prosody: dict | None = None
    phonemes: list[str] | None = None
    notes: str | None = None


class Script(BaseModel):
    """A script for voice synthesis."""

    id: str
    name: str
    description: str | None = None
    project_id: str
    segments: list[ScriptSegment] = []
    metadata: dict = {}
    created: str  # ISO datetime string
    modified: str  # ISO datetime string
    version: int = 1


class ScriptCreateRequest(BaseModel):
    """Request to create a script."""

    name: str
    description: str | None = None
    project_id: str
    segments: list[ScriptSegment] | None = None
    metadata: dict | None = None


class ScriptUpdateRequest(BaseModel):
    """Request to update a script."""

    name: str | None = None
    description: str | None = None
    segments: list[ScriptSegment] | None = None
    metadata: dict | None = None


@router.get("", response_model=list[Script])
async def get_scripts(
    project_id: str | None = Query(None),
    search: str | None = Query(None),
):
    """Get all scripts, optionally filtered."""
    try:
        scripts = list(_scripts.values())

        if project_id:
            scripts = [s for s in scripts if s.get("project_id") == project_id]

        if search:
            search_lower = search.lower()
            scripts = [
                s
                for s in scripts
                if search_lower in s.get("name", "").lower()
                or search_lower in s.get("description", "").lower()
            ]

        # Sort by name
        scripts.sort(key=lambda s: s.get("name", ""))

        logger.info(f"Retrieved {len(scripts)} scripts (project_id={project_id}, search={search})")
        return [
            Script(
                id=str(s.get("id", "")),
                name=str(s.get("name", "")),
                description=s.get("description"),
                project_id=str(s.get("project_id", "")),
                segments=[
                    ScriptSegment(
                        id=str(seg.get("id", "")),
                        text=str(seg.get("text", "")),
                        start_time=seg.get("start_time"),
                        end_time=seg.get("end_time"),
                        speaker=seg.get("speaker"),
                        voice_profile_id=seg.get("voice_profile_id"),
                        prosody=seg.get("prosody"),
                        phonemes=seg.get("phonemes"),
                        notes=seg.get("notes"),
                    )
                    for seg in s.get("segments", [])
                ],
                metadata=s.get("metadata", {}),
                created=str(s.get("created", "")),
                modified=str(s.get("modified", "")),
                version=s.get("version", 1),
            )
            for s in scripts
        ]
    except Exception as e:
        logger.error(
            f"Error getting scripts (project_id={project_id}, search={search}): {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to get scripts: {e!s}")


@router.get("/{script_id}", response_model=Script)
async def get_script(script_id: str):
    """Get a specific script."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")

        if script_id not in _scripts:
            logger.warning(f"Script not found: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        script = _scripts[script_id]
        logger.debug(f"Retrieved script: {script_id}")
        return Script(
            id=str(script.get("id", "")),
            name=str(script.get("name", "")),
            description=script.get("description"),
            project_id=str(script.get("project_id", "")),
            segments=[
                ScriptSegment(
                    id=str(seg.get("id", "")),
                    text=str(seg.get("text", "")),
                    start_time=seg.get("start_time"),
                    end_time=seg.get("end_time"),
                    speaker=seg.get("speaker"),
                    voice_profile_id=seg.get("voice_profile_id"),
                    prosody=seg.get("prosody"),
                    phonemes=seg.get("phonemes"),
                    notes=seg.get("notes"),
                )
                for seg in script.get("segments", [])
            ],
            metadata=script.get("metadata", {}),
            created=str(script.get("created", "")),
            modified=str(script.get("modified", "")),
            version=script.get("version", 1),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting script {script_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get script: {e!s}")


@router.post("", response_model=Script)
async def create_script(request: ScriptCreateRequest):
    """Create a new script."""
    try:
        import uuid

        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Script name is required")
        if not request.project_id or not request.project_id.strip():
            raise HTTPException(status_code=400, detail="Project ID is required")

        script_id = f"script-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        script = {
            "id": script_id,
            "name": request.name.strip(),
            "description": request.description,
            "project_id": request.project_id,
            "segments": [
                {
                    "id": seg.id,
                    "text": seg.text,
                    "start_time": seg.start_time,
                    "end_time": seg.end_time,
                    "speaker": seg.speaker,
                    "voice_profile_id": seg.voice_profile_id,
                    "prosody": seg.prosody,
                    "phonemes": seg.phonemes,
                    "notes": seg.notes,
                }
                for seg in (request.segments or [])
            ],
            "metadata": request.metadata or {},
            "created": now,
            "modified": now,
            "version": 1,
        }

        _scripts[script_id] = script
        logger.info(
            f"Created script: {script_id} ({request.name}) in project: {request.project_id}"
        )
        return Script(
            id=script_id,
            name=request.name,
            description=request.description,
            project_id=request.project_id,
            segments=request.segments or [],
            metadata=request.metadata or {},
            created=now,
            modified=now,
            version=1,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating script: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create script: {e!s}")


@router.put("/{script_id}", response_model=Script)
async def update_script(script_id: str, request: ScriptUpdateRequest):
    """Update a script."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")
        if request.name is not None and (not request.name or not request.name.strip()):
            raise HTTPException(status_code=400, detail="Script name cannot be empty")

        if script_id not in _scripts:
            logger.warning(f"Script not found for update: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        script = _scripts[script_id].copy()

        if request.name is not None:
            script["name"] = request.name.strip()
        if request.description is not None:
            script["description"] = request.description
        if request.segments is not None:
            script["segments"] = [
                {
                    "id": seg.id,
                    "text": seg.text,
                    "start_time": seg.start_time,
                    "end_time": seg.end_time,
                    "speaker": seg.speaker,
                    "voice_profile_id": seg.voice_profile_id,
                    "prosody": seg.prosody,
                    "phonemes": seg.phonemes,
                    "notes": seg.notes,
                }
                for seg in request.segments
            ]
        if request.metadata is not None:
            script["metadata"] = request.metadata

        script["modified"] = datetime.utcnow().isoformat()
        script["version"] = script.get("version", 1) + 1
        _scripts[script_id] = script

        logger.info(f"Updated script: {script_id}")
        return Script(
            id=str(script.get("id", "")),
            name=str(script.get("name", "")),
            description=script.get("description"),
            project_id=str(script.get("project_id", "")),
            segments=[
                ScriptSegment(
                    id=str(seg.get("id", "")),
                    text=str(seg.get("text", "")),
                    start_time=seg.get("start_time"),
                    end_time=seg.get("end_time"),
                    speaker=seg.get("speaker"),
                    voice_profile_id=seg.get("voice_profile_id"),
                    prosody=seg.get("prosody"),
                    phonemes=seg.get("phonemes"),
                    notes=seg.get("notes"),
                )
                for seg in script.get("segments", [])
            ],
            metadata=script.get("metadata", {}),
            created=str(script.get("created", "")),
            modified=str(script.get("modified", "")),
            version=script.get("version", 1),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating script {script_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update script: {e!s}")


@router.delete("/{script_id}")
async def delete_script(script_id: str):
    """Delete a script."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")

        if script_id not in _scripts:
            logger.warning(f"Script not found for deletion: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        del _scripts[script_id]
        logger.info(f"Deleted script: {script_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting script {script_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete script: {e!s}")


@router.post("/{script_id}/segments", response_model=Script)
async def add_segment_to_script(script_id: str, segment: ScriptSegment):
    """Add a segment to a script."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")
        if not segment.id or not segment.id.strip():
            raise HTTPException(status_code=400, detail="Segment ID is required")
        if not segment.text or not segment.text.strip():
            raise HTTPException(status_code=400, detail="Segment text is required")

        if script_id not in _scripts:
            logger.warning(f"Script not found for segment addition: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        script = _scripts[script_id].copy()

        segment_dict = {
            "id": segment.id,
            "text": segment.text,
            "start_time": segment.start_time,
            "end_time": segment.end_time,
            "speaker": segment.speaker,
            "voice_profile_id": segment.voice_profile_id,
            "prosody": segment.prosody,
            "phonemes": segment.phonemes,
            "notes": segment.notes,
        }

        script["segments"].append(segment_dict)
        script["modified"] = datetime.utcnow().isoformat()
        script["version"] = script.get("version", 1) + 1
        _scripts[script_id] = script

        logger.info(f"Added segment {segment.id} to script: {script_id}")
        return Script(
            id=str(script.get("id", "")),
            name=str(script.get("name", "")),
            description=script.get("description"),
            project_id=str(script.get("project_id", "")),
            segments=[
                ScriptSegment(
                    id=str(seg.get("id", "")),
                    text=str(seg.get("text", "")),
                    start_time=seg.get("start_time"),
                    end_time=seg.get("end_time"),
                    speaker=seg.get("speaker"),
                    voice_profile_id=seg.get("voice_profile_id"),
                    prosody=seg.get("prosody"),
                    phonemes=seg.get("phonemes"),
                    notes=seg.get("notes"),
                )
                for seg in script.get("segments", [])
            ],
            metadata=script.get("metadata", {}),
            created=str(script.get("created", "")),
            modified=str(script.get("modified", "")),
            version=script.get("version", 1),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error adding segment to script {script_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to add segment: {e!s}")


@router.delete("/{script_id}/segments/{segment_id}")
async def remove_segment_from_script(script_id: str, segment_id: str):
    """Remove a segment from a script."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")
        if not segment_id or not segment_id.strip():
            raise HTTPException(status_code=400, detail="Segment ID is required")

        if script_id not in _scripts:
            logger.warning(f"Script not found for segment removal: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        script = _scripts[script_id].copy()
        original_segment_count = len(script.get("segments", []))

        script["segments"] = [s for s in script.get("segments", []) if s.get("id") != segment_id]

        if len(script["segments"]) == original_segment_count:
            logger.warning(f"Segment not found for removal: {segment_id} in script: {script_id}")
            raise HTTPException(status_code=404, detail="Segment not found")

        script["modified"] = datetime.utcnow().isoformat()
        script["version"] = script.get("version", 1) + 1
        _scripts[script_id] = script

        logger.info(f"Removed segment {segment_id} from script: {script_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error removing segment {segment_id} from script {script_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to remove segment: {e!s}")


@router.post("/{script_id}/synthesize")
async def synthesize_script(script_id: str):
    """Synthesize a script to audio."""
    try:
        if not script_id or not script_id.strip():
            raise HTTPException(status_code=400, detail="Script ID is required")

        if script_id not in _scripts:
            logger.warning(f"Script not found for synthesis: {script_id}")
            raise HTTPException(status_code=404, detail="Script not found")

        script = _scripts[script_id]

        if not script.get("segments") or len(script.get("segments", [])) == 0:
            raise HTTPException(status_code=400, detail="Script has no segments to synthesize")

        # Import audio processing utilities
        try:
            import os
            import sys

            app_path = os.path.join(os.path.dirname(__file__), "..", "..", "..", "app")
            if os.path.exists(app_path) and app_path not in sys.path:
                sys.path.insert(0, app_path)

            import tempfile
            import uuid
            from pathlib import Path

            from core.audio import audio_utils

            # Get default engine via EngineService (ADR-008 compliant)
            engine_service = get_engine_service()
            available_engines = engine_service.list_engines()
            if not available_engines:
                raise HTTPException(status_code=503, detail="No voice synthesis engines available")

            engine_name = available_engines[0].get("id", available_engines[0].get("name", ""))
            engine = engine_service.get_engine(engine_name)

            if not engine:
                raise HTTPException(status_code=503, detail=f"Engine '{engine_name}' not available")

            # Synthesize each segment
            segment_audio_files = []
            for i, segment in enumerate(script.get("segments", [])):
                segment_text = segment.get("text", "")
                if not segment_text or not segment_text.strip():
                    continue

                voice_profile_id = segment.get("voice_profile_id")
                segment.get("prosody")

                # Prepare synthesis parameters
                synthesis_kwargs = {
                    "text": segment_text,
                    "language": "en",  # Default, can be enhanced
                }

                if voice_profile_id:
                    synthesis_kwargs["voice_profile_id"] = voice_profile_id

                # Synthesize segment
                try:
                    if hasattr(engine, "synthesize"):
                        segment_audio = engine.synthesize(**synthesis_kwargs)

                        # Save segment to temporary file
                        segment_file = tempfile.NamedTemporaryFile(
                            delete=False, suffix=".wav", dir=tempfile.gettempdir()
                        )
                        segment_file.close()

                        # Save audio to file
                        audio_utils.save_audio(segment_audio, segment_file.name)
                        segment_audio_files.append(segment_file.name)

                        logger.debug(f"Synthesized segment {i+1}/{len(script.get('segments', []))}")
                    else:
                        logger.warning(f"Engine {engine_name} does not support synthesize method")
                        continue
                except Exception as seg_error:
                    logger.error(f"Failed to synthesize segment {i+1}: {seg_error}")
                    continue

            if not segment_audio_files:
                raise HTTPException(status_code=500, detail="Failed to synthesize any segments")

            # Combine all segments into final audio
            combined_audio = None
            for seg_path in segment_audio_files:
                segment_audio_data = audio_utils.load_audio(seg_path)

                if combined_audio is None:
                    combined_audio = segment_audio_data
                else:
                    # Add small pause between segments (0.2 seconds of silence)
                    silence = audio_utils.generate_silence(
                        0.2, sample_rate=audio_utils.get_sample_rate(combined_audio)
                    )
                    combined_audio = audio_utils.concatenate(
                        [combined_audio, silence, segment_audio_data]
                    )

                # Clean up segment file
                try:
                    os.unlink(seg_path)
                except OSError as cleanup_err:
                    logger.debug(f"Failed to cleanup segment file {seg_path}: {cleanup_err}")

            # Save combined audio
            audio_id = f"script-{uuid.uuid4().hex[:8]}"
            output_dir = Path(tempfile.gettempdir()) / "voice_studio_audio"
            output_dir.mkdir(exist_ok=True)
            output_file = output_dir / f"{audio_id}.wav"

            audio_utils.save_audio(combined_audio, str(output_file))

            logger.info(f"Successfully synthesized script {script_id} to audio {audio_id}")

            return {
                "audio_id": audio_id,
                "audio_url": f"/api/audio/{audio_id}",
                "script_id": script_id,
                "segments_synthesized": len(segment_audio_files),
                "total_segments": len(script.get("segments", [])),
            }

        except HTTPException:
            raise
        except ImportError as import_error:
            logger.error(f"Failed to import required modules: {import_error}")
            raise HTTPException(status_code=503, detail="Voice synthesis engine not available")
        except Exception as e:
            logger.error(f"Error synthesizing script: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to synthesize script: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error synthesizing script {script_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to synthesize script: {e!s}")
