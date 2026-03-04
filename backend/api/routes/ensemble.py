"""
Ensemble Synthesis Routes

Endpoints for multi-voice synthesis (synthesizing multiple voices simultaneously)
and multi-engine ensemble synthesis (IDEA 55).
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException, Request

from backend.api.dependencies import require_synthesis_clearance
from backend.api.models import ApiOk
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/ensemble", tags=["ensemble"])

# In-memory ensemble jobs storage (replace with database in production)
_ensemble_jobs: dict[str, dict] = {}

# In-memory multi-engine ensemble jobs storage (IDEA 55)
_multi_engine_ensemble_jobs: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class EnsembleVoice(BaseModel):
    """A voice in an ensemble synthesis."""

    profile_id: str
    text: str
    engine: str | None = None
    language: str | None = None
    emotion: str | None = None


class EnsembleSynthesisRequest(BaseModel):
    """Request for ensemble synthesis."""

    voices: list[EnsembleVoice]
    project_id: str | None = None
    mix_mode: str = "sequential"  # sequential, parallel, layered
    output_format: str = "wav"  # wav, mp3, flac


class EnsembleSynthesisResponse(BaseModel):
    """Response from ensemble synthesis."""

    job_id: str
    status: str  # queued, processing, completed, failed
    audio_ids: list[str] = []
    message: str


class EnsembleJobStatus(BaseModel):
    """Status of an ensemble synthesis job."""

    job_id: str
    status: str
    progress: float  # 0.0 to 1.0
    completed_voices: int
    total_voices: int
    audio_ids: list[str] = []
    error: str | None = None
    created: str
    updated: str


# Multi-Engine Ensemble Models (IDEA 55)
class MultiEngineEnsembleRequest(BaseModel):
    """Request for multi-engine ensemble synthesis (IDEA 55)."""

    text: str
    profile_id: str
    engines: list[str]  # ["xtts_v2", "chatterbox", "tortoise"]
    language: str | None = "en"
    emotion: str | None = None
    selection_mode: str = "voting"  # "voting", "hybrid", "fusion"
    fusion_strategy: str | None = None  # "quality_weighted", "equal", "best_segment"
    segment_size: float = 0.5  # seconds
    quality_threshold: float = 0.85  # Minimum quality for selection


class EngineQualityResult(BaseModel):
    """Quality metrics for a single engine output."""

    engine: str
    audio_id: str
    quality_score: float | None = None
    mos_score: float | None = None
    similarity: float | None = None
    naturalness: float | None = None
    error: str | None = None


class MultiEngineEnsembleResponse(BaseModel):
    """Response from multi-engine ensemble synthesis."""

    job_id: str
    status: str  # queued, processing, completed, failed
    engines: list[str]
    message: str


class MultiEngineEnsembleStatus(BaseModel):
    """Status of a multi-engine ensemble job."""

    job_id: str
    status: str
    progress: float  # 0.0 to 1.0
    engines: list[str]
    engine_outputs: dict[str, str] = {}  # engine -> audio_id
    engine_qualities: dict[str, dict] = {}  # engine -> quality metrics
    ensemble_audio_id: str | None = None
    ensemble_quality: dict | None = None
    error: str | None = None
    created: str
    updated: str


@router.post("", response_model=EnsembleSynthesisResponse)
async def create_ensemble_synthesis(
    request: EnsembleSynthesisRequest,
    http_request: Request,
    _policy: None = Depends(require_synthesis_clearance),
):
    """Create a new ensemble synthesis job."""
    import uuid

    if not request.voices:
        raise HTTPException(status_code=400, detail="At least one voice is required")

    if len(request.voices) > 10:
        raise HTTPException(status_code=400, detail="Maximum 10 voices per ensemble")

    try:
        job_id = f"ensemble-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        job = {
            "job_id": job_id,
            "status": "queued",
            "progress": 0.0,
            "completed_voices": 0,
            "total_voices": len(request.voices),
            "audio_ids": [],
            "voices": [v.dict() for v in request.voices],
            "project_id": request.project_id,
            "mix_mode": request.mix_mode,
            "output_format": request.output_format,
            "error": None,
            "created": now,
            "updated": now,
        }

        _ensemble_jobs[job_id] = job

        # Queue job for asynchronous processing
        import asyncio

        asyncio.create_task(_process_ensemble_job(job_id, request, http_request))

        logger.info(f"Ensemble synthesis job created: {job_id}")

        return EnsembleSynthesisResponse(
            job_id=job_id,
            status="queued",
            audio_ids=[],
            message="Ensemble synthesis job queued",
        )
    except Exception as e:
        logger.error(f"Failed to create ensemble synthesis: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to create ensemble: {e!s}") from e


@router.get("/{job_id}", response_model=EnsembleJobStatus)
@cache_response(ttl=5)  # Cache for 5 seconds (status changes frequently during synthesis)
async def get_ensemble_status(job_id: str):
    """Get the status of an ensemble synthesis job."""
    if job_id not in _ensemble_jobs:
        raise HTTPException(status_code=404, detail="Job not found")

    job = _ensemble_jobs[job_id]
    return EnsembleJobStatus(
        job_id=job["job_id"],
        status=job["status"],
        progress=job["progress"],
        completed_voices=job["completed_voices"],
        total_voices=job["total_voices"],
        audio_ids=job["audio_ids"],
        error=job.get("error"),
        created=job["created"],
        updated=job["updated"],
    )


@router.get("", response_model=list[EnsembleJobStatus])
async def list_ensemble_jobs(
    project_id: str | None = None,
    status: str | None = None,
):
    """List ensemble synthesis jobs."""
    jobs = list(_ensemble_jobs.values())

    if project_id:
        jobs = [j for j in jobs if j.get("project_id") == project_id]

    if status:
        jobs = [j for j in jobs if j.get("status") == status]

    # Sort by created date (newest first)
    jobs.sort(key=lambda j: j.get("created", ""), reverse=True)

    return [
        EnsembleJobStatus(
            job_id=j["job_id"],
            status=j["status"],
            progress=j["progress"],
            completed_voices=j["completed_voices"],
            total_voices=j["total_voices"],
            audio_ids=j["audio_ids"],
            error=j.get("error"),
            created=j["created"],
            updated=j["updated"],
        )
        for j in jobs
    ]


@router.delete("/{job_id}", response_model=ApiOk)
async def delete_ensemble_job(job_id: str):
    """Delete an ensemble synthesis job."""
    if job_id not in _ensemble_jobs:
        raise HTTPException(status_code=404, detail="Job not found")

    del _ensemble_jobs[job_id]
    logger.info(f"Deleted ensemble job: {job_id}")
    return ApiOk(ok=True)


async def _process_ensemble_job(
    job_id: str, request: EnsembleSynthesisRequest, http_request: Request
):
    """
    Process ensemble synthesis job asynchronously.

    Synthesizes each voice in the ensemble and updates job status.
    """
    try:
        job = _ensemble_jobs.get(job_id)
        if not job:
            logger.warning(f"Ensemble job {job_id} not found")
            return

        job["status"] = "processing"
        job["progress"] = 0.0
        job["updated"] = datetime.utcnow().isoformat()

        # Try to use voice synthesis service
        try:
            from ..models_additional import VoiceSynthesizeRequest

            total_voices = len(request.voices)
            completed = 0

            # Process voices based on mix_mode
            if request.mix_mode == "parallel":
                # Synthesize all voices in parallel
                tasks = []
                for voice in request.voices:
                    synth_req = VoiceSynthesizeRequest(
                        profile_id=voice.profile_id,
                        text=voice.text,
                        engine=voice.engine or "xtts_v2",
                        language=voice.language or "en",
                        emotion=voice.emotion,
                    )
                    tasks.append(_synthesize_voice(synth_req, job_id, http_request))

                # Wait for all voices to complete
                results = await asyncio.gather(*tasks, return_exceptions=True)

                for result in results:
                    if isinstance(result, Exception):
                        logger.error(f"Voice synthesis failed: {result}")
                        job["error"] = str(result)
                    else:
                        if result:
                            job["audio_ids"].append(result)
                            completed += 1
                            job["completed_voices"] = completed
                            job["progress"] = completed / total_voices
                            job["updated"] = datetime.utcnow().isoformat()
            else:
                # Sequential or layered: process one at a time
                for voice in request.voices:
                    synth_req = VoiceSynthesizeRequest(
                        profile_id=voice.profile_id,
                        text=voice.text,
                        engine=voice.engine or "xtts_v2",
                        language=voice.language or "en",
                        emotion=voice.emotion,
                    )

                    try:
                        audio_id = await _synthesize_voice(synth_req, job_id, http_request)
                        if audio_id:
                            job["audio_ids"].append(audio_id)
                            completed += 1
                            job["completed_voices"] = completed
                            job["progress"] = completed / total_voices
                            job["updated"] = datetime.utcnow().isoformat()
                    except Exception as e:
                        logger.error(f"Voice synthesis failed for {voice.profile_id}: {e}")
                        job["error"] = f"Failed to synthesize voice {voice.profile_id}: {e!s}"
                        break

            # Mark as completed if all voices processed
            if completed == total_voices:
                job["status"] = "completed"
                job["progress"] = 1.0
            else:
                job["status"] = "failed"
                if not job.get("error"):
                    job["error"] = f"Only {completed}/{total_voices} voices synthesized"

            job["updated"] = datetime.utcnow().isoformat()
            logger.info(
                f"Ensemble synthesis job {job_id} completed: " f"{completed}/{total_voices} voices"
            )

        except ImportError:
            logger.error("Voice synthesis service not available for ensemble")
            job["status"] = "failed"
            job["error"] = "Voice synthesis service not available"
            job["updated"] = datetime.utcnow().isoformat()
        except Exception as e:
            logger.error(f"Ensemble synthesis job {job_id} failed: {e}", exc_info=True)
            job["status"] = "failed"
            job["error"] = str(e)
            job["updated"] = datetime.utcnow().isoformat()

    except Exception as e:
        logger.error(f"Error processing ensemble job {job_id}: {e}", exc_info=True)
        if job_id in _ensemble_jobs:
            job = _ensemble_jobs[job_id]
            job["status"] = "failed"
            job["error"] = str(e)
            job["updated"] = datetime.utcnow().isoformat()


async def _synthesize_voice(
    synth_req, job_id: str, http_request: Request
) -> str | None:
    """
    Synthesize a single voice for ensemble.

    Returns audio_id if successful, None otherwise.
    """
    try:
        from backend.voice.services.synthesis_service import SynthesisService

        # Call voice synthesis endpoint
        result = await SynthesisService.synthesize(synth_req, http_request, config_service=None)
        return result.audio_id if result else None

    except Exception as e:
        logger.error(f"Voice synthesis failed in ensemble: {e}")
        return None


# Multi-Engine Ensemble Endpoints (IDEA 55)
@router.post("/multi-engine", response_model=MultiEngineEnsembleResponse)
async def create_multi_engine_ensemble(
    request: MultiEngineEnsembleRequest,
    http_request: Request,
    _policy: None = Depends(require_synthesis_clearance),
):
    """
    Create a multi-engine ensemble synthesis job (IDEA 55).

    Synthesizes text with multiple engines, evaluates quality,
    and combines the best segments for maximum quality output.
    """
    import uuid

    if not request.text or not request.text.strip():
        raise HTTPException(status_code=400, detail="Text is required for ensemble synthesis")

    if not request.profile_id or not request.profile_id.strip():
        raise HTTPException(status_code=400, detail="Profile ID is required for ensemble synthesis")

    if not request.engines or len(request.engines) == 0:
        raise HTTPException(status_code=400, detail="At least one engine is required")

    if len(request.engines) > 5:
        raise HTTPException(status_code=400, detail="Maximum 5 engines per ensemble")

    # Validate selection mode
    valid_selection_modes = ["voting", "hybrid", "fusion"]
    if request.selection_mode not in valid_selection_modes:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid selection_mode. Must be one of: {', '.join(valid_selection_modes)}",
        )

    try:
        job_id = f"multi-engine-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        job = {
            "job_id": job_id,
            "status": "queued",
            "progress": 0.0,
            "text": request.text,
            "profile_id": request.profile_id,
            "engines": request.engines,
            "language": request.language or "en",
            "emotion": request.emotion,
            "selection_mode": request.selection_mode,
            "fusion_strategy": request.fusion_strategy,
            "segment_size": request.segment_size,
            "quality_threshold": request.quality_threshold,
            "engine_outputs": {},
            "engine_qualities": {},
            "ensemble_audio_id": None,
            "ensemble_quality": None,
            "error": None,
            "created": now,
            "updated": now,
        }

        _multi_engine_ensemble_jobs[job_id] = job

        # Queue job for asynchronous processing
        asyncio.create_task(_process_multi_engine_ensemble_job(job_id, request, http_request))

        logger.info(f"Multi-engine ensemble job created: {job_id}")

        return MultiEngineEnsembleResponse(
            job_id=job_id,
            status="queued",
            engines=request.engines,
            message="Multi-engine ensemble synthesis job queued",
        )
    except Exception as e:
        logger.error(f"Failed to create multi-engine ensemble: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to create ensemble: {e!s}") from e


@router.get("/multi-engine/{job_id}", response_model=MultiEngineEnsembleStatus)
@cache_response(ttl=5)  # Cache for 5 seconds (status changes frequently during synthesis)
async def get_multi_engine_ensemble_status(job_id: str):
    """Get the status of a multi-engine ensemble synthesis job."""
    if job_id not in _multi_engine_ensemble_jobs:
        raise HTTPException(status_code=404, detail="Job not found")

    job = _multi_engine_ensemble_jobs[job_id]
    return MultiEngineEnsembleStatus(
        job_id=job["job_id"],
        status=job["status"],
        progress=job["progress"],
        engines=job["engines"],
        engine_outputs=job.get("engine_outputs", {}),
        engine_qualities=job.get("engine_qualities", {}),
        ensemble_audio_id=job.get("ensemble_audio_id"),
        ensemble_quality=job.get("ensemble_quality"),
        error=job.get("error"),
        created=job["created"],
        updated=job["updated"],
    )


async def _process_multi_engine_ensemble_job(
    job_id: str, request: MultiEngineEnsembleRequest, http_request: Request
):
    """
    Process multi-engine ensemble synthesis job asynchronously (IDEA 55).

    Synthesizes with multiple engines in parallel, evaluates quality,
    and combines best segments.
    """
    try:
        job = _multi_engine_ensemble_jobs.get(job_id)
        if not job:
            logger.warning(f"Multi-engine ensemble job {job_id} not found")
            return

        job["status"] = "processing"
        job["progress"] = 0.0
        job["updated"] = datetime.utcnow().isoformat()

        # Step 1: Synthesize with all engines in parallel
        logger.info(f"Starting parallel synthesis for {len(request.engines)} engines")

        synthesis_tasks = []
        for engine in request.engines:
            task = _synthesize_with_engine(engine, request, job_id, http_request)
            synthesis_tasks.append((engine, task))

        # Wait for all engines to complete (with timeout)
        engine_results = {}
        completed = 0
        total_engines = len(request.engines)

        for engine, task in synthesis_tasks:
            try:
                result = await asyncio.wait_for(task, timeout=300.0)  # 5 minute timeout
                engine_results[engine] = result
                completed += 1
                job["progress"] = completed / (total_engines * 2)  # 50% for synthesis
                job["updated"] = datetime.utcnow().isoformat()

                if result and result.get("audio_id"):
                    job["engine_outputs"][engine] = result["audio_id"]
                    if result.get("quality"):
                        job["engine_qualities"][engine] = result["quality"]
            except asyncio.TimeoutError:
                logger.error(f"Engine {engine} synthesis timed out")
                engine_results[engine] = {"error": "Synthesis timed out"}
            except Exception as e:
                logger.error(f"Engine {engine} synthesis failed: {e}")
                engine_results[engine] = {"error": str(e)}

        # Check if we have any successful results
        successful_engines = [
            engine
            for engine, result in engine_results.items()
            if result and result.get("audio_id") and not result.get("error")
        ]

        if len(successful_engines) == 0:
            job["status"] = "failed"
            job["error"] = "All engine syntheses failed"
            job["updated"] = datetime.utcnow().isoformat()
            return

        # Step 2: Combine outputs based on selection mode
        logger.info(
            f"Combining outputs from {len(successful_engines)} engines using {request.selection_mode} mode"
        )

        # Implement voting mode (select best quality engine)
        # Segment-level selection and fusion can be added as enhancements

        if request.selection_mode == "voting":
            # Select engine with best quality score
            best_engine = None
            best_quality = -1.0

            for engine in successful_engines:
                quality = job["engine_qualities"].get(engine, {})
                quality_score = quality.get("quality_score") or quality.get("mos_score") or 0.0

                if quality_score > best_quality:
                    best_quality = quality_score
                    best_engine = engine

            if best_engine:
                job["ensemble_audio_id"] = job["engine_outputs"][best_engine]
                job["ensemble_quality"] = job["engine_qualities"].get(best_engine, {})
                job["progress"] = 1.0
                job["status"] = "completed"
                logger.info(f"Selected best engine: {best_engine} with quality {best_quality:.3f}")
            else:
                job["status"] = "failed"
                job["error"] = "Failed to select best engine"
        elif request.selection_mode == "hybrid":
            # Hybrid mode: Select best segments from different engines
            try:
                import numpy as np

                from backend.audio import audio_utils
                from backend.services.audio_artifacts import AudioRegistry

                # Load all engine outputs
                engine_audios = {}
                sample_rates = {}
                max_duration: float = 0.0

                for engine in successful_engines:
                    audio_id = job["engine_outputs"][engine]
                    audio_path = AudioRegistry.get_path(audio_id)
                    if audio_path:
                        try:
                            audio, sr = audio_utils.load_audio(audio_path)
                            if len(audio.shape) > 1:
                                audio = np.mean(audio, axis=1)  # Convert to mono
                            engine_audios[engine] = audio
                            sample_rates[engine] = sr
                            max_duration = max(max_duration, len(audio) / sr)
                        except Exception as e:
                            logger.warning(f"Failed to load audio for {engine}: {e}")

                if not engine_audios:
                    job["status"] = "failed"
                    job["error"] = "Failed to load any engine outputs for hybrid mode"
                    job["updated"] = datetime.utcnow().isoformat()
                    return

                # Segment-based selection: choose best quality segment from each engine
                segment_size_samples = int(
                    request.segment_size * sample_rates[next(iter(sample_rates.keys()))]
                )
                segments = []
                max_samples = max(len(audio) for audio in engine_audios.values())

                for i in range(0, max_samples, segment_size_samples):
                    segment_end = min(i + segment_size_samples, max_samples)
                    best_engine_segment = None
                    best_quality_segment = -1.0

                    for engine in engine_audios:
                        audio = engine_audios[engine]
                        if i < len(audio):
                            segment = audio[i:segment_end]
                            # Get quality for this engine
                            quality = job["engine_qualities"].get(engine, {})
                            quality_score = (
                                quality.get("quality_score") or quality.get("mos_score") or 0.0
                            )

                            if quality_score > best_quality_segment:
                                best_quality_segment = quality_score
                                best_engine_segment = segment

                    if best_engine_segment is not None:
                        segments.append(best_engine_segment)

                # Combine segments
                if segments:
                    combined_audio = np.concatenate(segments)
                    target_sr = sample_rates[next(iter(sample_rates.keys()))]
                    from backend.services.audio_artifacts import (
                        create_audio_artifact_from_wav_array,
                    )

                    ensemble_audio_id, _, _ = create_audio_artifact_from_wav_array(
                        combined_audio,
                        target_sr,
                        created_by="ensemble_hybrid",
                        audio_id=f"ensemble_{job_id}",
                    )

                    job["ensemble_audio_id"] = ensemble_audio_id
                    job["ensemble_quality"] = {"quality_score": best_quality_segment}
                    job["progress"] = 1.0
                    job["status"] = "completed"
                    logger.info(
                        f"Hybrid mode: Combined {len(segments)} segments from {len(engine_audios)} engines"
                    )
                else:
                    job["status"] = "failed"
                    job["error"] = "Failed to create hybrid audio"
            except Exception as e:
                logger.error(f"Hybrid mode failed: {e}", exc_info=True)
                job["status"] = "failed"
                job["error"] = f"Hybrid mode failed: {e!s}"

        elif request.selection_mode == "fusion":
            # Fusion mode: Weighted combination of all engines
            try:
                import numpy as np

                from backend.audio import audio_utils
                from backend.services.audio_artifacts import AudioRegistry

                # Load all engine outputs
                engine_audios = {}
                sample_rates = {}
                engine_weights = {}

                for engine in successful_engines:
                    audio_id = job["engine_outputs"][engine]
                    audio_path = AudioRegistry.get_path(audio_id)
                    if audio_path:
                        try:
                            audio, sr = audio_utils.load_audio(audio_path)
                            if len(audio.shape) > 1:
                                audio = np.mean(audio, axis=1)  # Convert to mono
                            engine_audios[engine] = audio
                            sample_rates[engine] = sr

                            # Calculate weight based on quality
                            quality = job["engine_qualities"].get(engine, {})
                            quality_score = (
                                quality.get("quality_score") or quality.get("mos_score") or 0.5
                            )
                            engine_weights[engine] = quality_score
                        except Exception as e:
                            logger.warning(f"Failed to load audio for {engine}: {e}")

                if not engine_audios:
                    job["status"] = "failed"
                    job["error"] = "Failed to load any engine outputs for fusion mode"
                    job["updated"] = datetime.utcnow().isoformat()
                    return

                # Normalize weights
                total_weight = sum(engine_weights.values())
                if total_weight > 0:
                    engine_weights = {k: v / total_weight for k, v in engine_weights.items()}
                else:
                    # Equal weights if no quality scores
                    engine_weights = {k: 1.0 / len(engine_audios) for k in engine_audios}

                # Resample all to same rate and length
                target_sr = sample_rates[next(iter(sample_rates.keys()))]
                max_length = max(len(audio) for audio in engine_audios.values())

                fused_audio = np.zeros(max_length)
                for engine, audio in engine_audios.items():
                    # Resample if needed
                    if sample_rates[engine] != target_sr:
                        audio = audio_utils.resample_audio(audio, sample_rates[engine], target_sr)

                    # Pad or truncate to max_length
                    if len(audio) < max_length:
                        audio = np.pad(audio, (0, max_length - len(audio)), mode="constant")
                    elif len(audio) > max_length:
                        audio = audio[:max_length]

                    # Weighted sum
                    weight = engine_weights[engine]
                    fused_audio += audio * weight

                # Normalize to prevent clipping
                max_amp = np.max(np.abs(fused_audio))
                if max_amp > 0.95:
                    fused_audio = fused_audio * (0.95 / max_amp)

                # Save and register fused audio via artifact spine
                from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

                ensemble_audio_id, _, _ = create_audio_artifact_from_wav_array(
                    fused_audio,
                    target_sr,
                    created_by="ensemble_fusion",
                    audio_id=f"ensemble_{job_id}",
                )

                # Calculate average quality
                avg_quality = sum(
                    engine_weights[engine]
                    * (job["engine_qualities"].get(engine, {}).get("quality_score") or 0.5)
                    for engine in engine_audios
                )

                job["ensemble_audio_id"] = ensemble_audio_id
                job["ensemble_quality"] = {"quality_score": avg_quality}
                job["progress"] = 1.0
                job["status"] = "completed"
                logger.info(
                    f"Fusion mode: Combined {len(engine_audios)} engines with weighted average"
                )
            except Exception as e:
                logger.error(f"Fusion mode failed: {e}", exc_info=True)
                job["status"] = "failed"
                job["error"] = f"Fusion mode failed: {e!s}"
        else:
            # Fallback to voting for unknown modes
            logger.warning(f"Unknown selection mode '{request.selection_mode}', using voting")
            best_engine = successful_engines[0]
            job["ensemble_audio_id"] = job["engine_outputs"][best_engine]
            job["ensemble_quality"] = job["engine_qualities"].get(best_engine, {})
            job["progress"] = 1.0
            job["status"] = "completed"

        job["updated"] = datetime.utcnow().isoformat()
        logger.info(f"Multi-engine ensemble job {job_id} completed")

    except Exception as e:
        logger.error(f"Multi-engine ensemble job {job_id} failed: {e}", exc_info=True)
        if job_id in _multi_engine_ensemble_jobs:
            job = _multi_engine_ensemble_jobs[job_id]
            job["status"] = "failed"
            job["error"] = str(e)
            job["updated"] = datetime.utcnow().isoformat()


async def _synthesize_with_engine(
    engine: str,
    request: MultiEngineEnsembleRequest,
    job_id: str,
    http_request: Request,
) -> dict:
    """
    Synthesize with a specific engine for multi-engine ensemble.

    Returns dict with audio_id, quality metrics, or error.
    """
    try:
        from backend.voice.services.synthesis_service import SynthesisService

        from ..models_additional import VoiceSynthesizeRequest

        synth_req = VoiceSynthesizeRequest(
            profile_id=request.profile_id,
            text=request.text,
            engine=engine,
            language=request.language or "en",
            emotion=request.emotion,
            enhance_quality=True,  # Always enhance for ensemble
        )

        # Call voice synthesis
        result = await SynthesisService.synthesize(synth_req, http_request, config_service=None)

        if result and result.audio_id:
            return {
                "audio_id": result.audio_id,
                "quality": (result.quality_metrics.dict() if result.quality_metrics else {}),
            }
        else:
            return {"error": "Synthesis returned no audio"}

    except Exception as e:
        logger.error(f"Engine {engine} synthesis failed: {e}")
        return {"error": str(e)}
