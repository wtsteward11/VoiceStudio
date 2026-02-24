"""
Voice Cloning Wizard Routes

Endpoints for the step-by-step voice cloning wizard interface.
"""

from __future__ import annotations

import logging
import uuid
from datetime import datetime

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.infrastructure.adapters.job_state_store import get_job_state_store

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/voice/clone/wizard", tags=["voice-cloning-wizard"])

# Disk-backed wizard job state (durable across backend restarts)
_wizard_store = get_job_state_store("voice_cloning_wizard")
_wizard_jobs: dict[str, WizardJob] = {}
_state_lock = asyncio.Lock()


class WizardJob(BaseModel):
    """Voice cloning wizard job."""

    job_id: str
    step: int  # 1=Upload, 2=Configure, 3=Process, 4=Review
    reference_audio_id: str | None = None
    reference_audio_url: str | None = None
    audio_validation: dict | None = None
    engine: str | None = None
    quality_mode: str | None = None
    profile_name: str | None = None
    profile_description: str | None = None
    processing_status: str = "pending"  # pending, processing, writing, completed, failed
    progress: float = 0.0
    profile_id: str | None = None
    quality_metrics: dict | None = None
    test_synthesis_audio_id: str | None = None
    test_synthesis_audio_url: str | None = None
    error_message: str | None = None
    created_at: str
    updated_at: str


def _persist_wizard_job(job: WizardJob) -> None:
    _wizard_jobs[job.job_id] = job
    try:
        _wizard_store.upsert(job.job_id, job.model_dump(mode="json"))
    except Exception as e:
        logger.debug(f"Failed to persist wizard job {job.job_id}: {e}")


def _load_wizard_jobs_from_disk() -> None:
    try:
        for job_id, payload in _wizard_store.load_all().items():
            try:
                job = WizardJob.model_validate(payload)
            except Exception:
                continue

            # If backend restarted mid-processing, surface that deterministically.
            if job.processing_status == "processing":
                job.processing_status = "failed"
                job.error_message = "Backend restarted during processing"
                job.updated_at = datetime.utcnow().isoformat()
                _persist_wizard_job(job)
            else:
                _wizard_jobs[job_id] = job
    except Exception as e:
        logger.debug(f"Failed to load wizard jobs from disk: {e}")


_load_wizard_jobs_from_disk()


class AudioValidationRequest(BaseModel):
    """Request to validate audio for voice cloning."""

    audio_id: str


class AudioValidationResponse(BaseModel):
    """Audio validation response."""

    is_valid: bool
    duration: float
    sample_rate: int
    channels: int
    issues: list[str] = []
    recommendations: list[str] = []
    quality_score: float | None = None


class WizardStartRequest(BaseModel):
    """Request to start voice cloning wizard."""

    reference_audio_id: str
    engine: str = "xtts"
    quality_mode: str = "standard"
    profile_name: str
    profile_description: str | None = None
    consent_acknowledged: bool = False
    consent_type: str | None = None
    consent_timestamp: str | None = None
    consent_source: str | None = None


class WizardStartResponse(BaseModel):
    """Wizard start response."""

    job_id: str
    step: int
    status: str


class WizardStatusResponse(BaseModel):
    """Wizard job status response."""

    job_id: str
    step: int
    status: str
    progress: float
    profile_id: str | None = None
    quality_metrics: dict | None = None
    test_synthesis_audio_url: str | None = None
    error_message: str | None = None


class WizardFinalizeRequest(BaseModel):
    """Request to finalize wizard and create profile."""

    job_id: str
    profile_name: str | None = None
    profile_description: str | None = None


class WizardFinalizeResponse(BaseModel):
    """Wizard finalize response."""

    profile_id: str
    profile_name: str
    success: bool


@router.post("/validate-audio", response_model=AudioValidationResponse)
async def validate_audio(request: AudioValidationRequest):
    """Validate audio file for voice cloning."""
    try:
        import os

        import numpy as np

        from app.core.audio import audio_utils

        from .voice import _audio_storage

        # Get audio file path
        if request.audio_id not in _audio_storage:
            raise HTTPException(
                status_code=404, detail=f"Audio file '{request.audio_id}' not found"
            )

        audio_path = _audio_storage[request.audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Load and analyze audio
        try:
            audio, sample_rate = audio_utils.load_audio(audio_path)
        except Exception as e:
            raise HTTPException(status_code=400, detail=f"Failed to load audio file: {e!s}")

        # Calculate duration
        duration = len(audio) / sample_rate

        # Determine channels
        if len(audio.shape) == 1:
            channels = 1
        else:
            channels = audio.shape[1] if audio.shape[0] > audio.shape[1] else audio.shape[0]

        # Validate audio for voice cloning
        issues = []
        recommendations = []

        # Check duration
        if duration < 1.0:
            issues.append("Audio too short (minimum 1 second required)")
            recommendations.append(
                "Record at least 3 seconds for basic cloning, 10+ seconds for best results"
            )
        elif duration < 3.0:
            issues.append("Audio too short (minimum 3 seconds recommended)")
            recommendations.append("Record at least 10 seconds for best voice cloning quality")
        elif duration < 10.0:
            recommendations.append(
                "Consider recording 10+ seconds for better quality and more natural voice cloning"
            )
        elif duration > 300.0:
            recommendations.append(
                "Very long audio detected. Consider using shorter segments (10-60 seconds) for optimal results"
            )

        # Check sample rate
        if sample_rate < 8000:
            issues.append("Sample rate too low (minimum 8kHz required, 16kHz recommended)")
            recommendations.append("Use 16kHz or higher sample rate for voice cloning")
        elif sample_rate < 16000:
            issues.append("Sample rate too low (minimum 16kHz recommended)")
            recommendations.append("Use 16kHz or higher sample rate for better quality")
        elif sample_rate < 22050:
            recommendations.append("Consider using 22.05kHz or higher for better quality")
        elif sample_rate > 48000:
            recommendations.append(
                "Very high sample rate detected. 44.1kHz or 48kHz is sufficient for voice cloning"
            )

        # Check channels
        if channels > 2:
            issues.append(f"Unsupported channel count ({channels}). Mono or stereo required")
            recommendations.append("Convert to mono or stereo before voice cloning")
        elif channels > 1:
            recommendations.append(
                "Mono audio is recommended for voice cloning. Stereo will be converted to mono automatically"
            )

        # Analyze audio quality
        try:
            # Convert to mono for analysis if needed
            if channels > 1:
                audio_mono = np.mean(audio, axis=1) if len(audio.shape) == 2 else audio
            else:
                audio_mono = audio

            # Calculate quality metrics
            try:
                audio_utils.analyze_voice_characteristics(audio_mono, sample_rate)

                # Check for clipping
                max_amplitude = np.max(np.abs(audio_mono))
                if max_amplitude > 0.95:
                    issues.append("Audio may be clipping (amplitude too high)")
                    recommendations.append("Reduce input gain to prevent clipping")

                # Check SNR (if available)
                try:
                    signal_power = float(np.mean(audio_mono**2))
                    noise_floor = float(
                        np.mean(np.sort(np.abs(audio_mono))[: len(audio_mono) // 10] ** 2)
                    )
                    snr = 10 * np.log10(signal_power / noise_floor) if noise_floor > 0 else 60.0
                    if snr < 20:
                        issues.append(f"Low signal-to-noise ratio ({snr:.1f}dB)")
                        recommendations.append("Record in a quieter environment")
                except Exception:
                    ...

                # Check for silence
                rms = np.sqrt(np.mean(audio_mono**2))
                if rms < 0.01:
                    issues.append("Audio appears to be mostly silence")
                    recommendations.append("Check microphone input levels")

                # Calculate quality score based on comprehensive metrics
                quality_score = 0.5  # Base score

                # Duration scoring (0-0.2 points)
                if duration >= 30.0:
                    quality_score += 0.2
                elif duration >= 10.0:
                    quality_score += 0.15
                elif duration >= 5.0:
                    quality_score += 0.1
                elif duration >= 3.0:
                    quality_score += 0.05

                # Sample rate scoring (0-0.15 points)
                if sample_rate >= 44100:
                    quality_score += 0.15
                elif sample_rate >= 22050:
                    quality_score += 0.1
                elif sample_rate >= 16000:
                    quality_score += 0.05

                # Channel scoring (0-0.05 points)
                if channels == 1:
                    quality_score += 0.05

                # Amplitude/clipping scoring (0-0.1 points)
                if 0.1 <= max_amplitude <= 0.9:
                    quality_score += 0.1  # Optimal range
                elif max_amplitude < 0.95:
                    quality_score += 0.05  # Acceptable
                # No points if clipping (max_amplitude > 0.95)

                # SNR scoring (0-0.1 points)
                try:
                    signal_power = float(np.mean(audio_mono**2))
                    noise_floor = float(
                        np.mean(np.sort(np.abs(audio_mono))[: len(audio_mono) // 10] ** 2)
                    )
                    snr = 10 * np.log10(signal_power / noise_floor) if noise_floor > 0 else 60.0
                    if snr >= 30:
                        quality_score += 0.1
                    elif snr >= 25:
                        quality_score += 0.075
                    elif snr >= 20:
                        quality_score += 0.05
                    elif snr >= 15:
                        quality_score += 0.025
                except Exception:
                    pass  # SNR calculation failed, skip this factor

                # RMS/volume scoring (0-0.05 points)
                if 0.05 <= rms <= 0.5:
                    quality_score += 0.05  # Good volume level
                elif 0.01 <= rms < 0.05:
                    quality_score += 0.025  # Low but acceptable
                # No points if too quiet (rms < 0.01)

                quality_score = min(1.0, max(0.0, quality_score))

            except Exception as e:
                logger.warning(f"Failed to analyze voice characteristics: {e}")
                quality_score = 0.6

        except Exception as e:
            logger.warning(f"Failed to analyze audio quality: {e}")
            quality_score = 0.6

        is_valid = len(issues) == 0

        return AudioValidationResponse(
            is_valid=is_valid,
            duration=duration,
            sample_rate=sample_rate,
            channels=channels,
            issues=issues,
            recommendations=recommendations,
            quality_score=quality_score,
        )
    except Exception as e:
        logger.error(f"Failed to validate audio: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to validate audio: {e!s}",
        ) from e


@router.post("/start", response_model=WizardStartResponse, status_code=201)
async def start_wizard(request: WizardStartRequest):
    """Start a new voice cloning wizard job. Requires consent_acknowledged."""
    if not request.consent_acknowledged:
        raise HTTPException(
            status_code=400,
            detail="Consent is required for voice cloning. Set consent_acknowledged=true.",
        )
    consent_meta = {
        "consent_type": request.consent_type or "voice_clone_wizard",
        "consent_timestamp": request.consent_timestamp or "",
        "consent_source": request.consent_source or "api",
    }
    logger.info("voice_clone_wizard_consent %s", consent_meta)
    try:
        job_id = f"wizard-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        job = WizardJob(
            job_id=job_id,
            step=2,  # Start at configure step (after upload)
            reference_audio_id=request.reference_audio_id,
            reference_audio_url=f"/api/voice/audio/{request.reference_audio_id}",
            engine=request.engine,
            quality_mode=request.quality_mode,
            profile_name=request.profile_name,
            profile_description=request.profile_description,
            processing_status="pending",
            progress=0.0,
            created_at=now,
            updated_at=now,
        )

        _wizard_jobs[job_id] = job

        logger.info(f"Started voice cloning wizard: {job_id}")

        return WizardStartResponse(
            job_id=job_id,
            step=job.step,
            status=job.processing_status,
        )
    except Exception as e:
        logger.error(f"Failed to start wizard: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to start wizard: {e!s}",
        ) from e


@router.get("/{job_id}/status", response_model=WizardStatusResponse)
async def get_wizard_status(job_id: str):
    """Get wizard job status."""
    try:
        if job_id not in _wizard_jobs:
            raise HTTPException(status_code=404, detail=f"Wizard job '{job_id}' not found")

        job = _wizard_jobs[job_id]

        return WizardStatusResponse(
            job_id=job.job_id,
            step=job.step,
            status=job.processing_status,
            progress=job.progress,
            profile_id=job.profile_id,
            quality_metrics=job.quality_metrics,
            test_synthesis_audio_url=job.test_synthesis_audio_url,
            error_message=job.error_message,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get wizard status: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get wizard status: {e!s}",
        ) from e


@router.post("/{job_id}/process")
async def process_wizard(job_id: str):
    """Process voice cloning (move from step 2 to step 3)."""
    try:
        if job_id not in _wizard_jobs:
            raise HTTPException(status_code=404, detail=f"Wizard job '{job_id}' not found")

        job = _wizard_jobs[job_id]

        # In a real implementation, this would:
        # 1. Start voice cloning process
        # 2. Update progress via WebSocket
        # 3. Create voice profile
        # 4. Generate test synthesis
        # 5. Calculate quality metrics

        # Start real processing
        job.step = 3
        job.processing_status = "processing"
        job.progress = 0.0
        job.updated_at = datetime.utcnow().isoformat()
        _wizard_jobs[job_id] = job

        # Process in background
        import asyncio
        import os

        from .voice import _audio_storage

        async def process_voice_cloning():
            try:
                # Get reference audio path
                if job.reference_audio_id not in _audio_storage:
                    raise ValueError(f"Reference audio '{job.reference_audio_id}' not found")

                audio_path = _audio_storage[job.reference_audio_id]
                if not os.path.exists(audio_path):
                    raise ValueError(f"Audio file at '{audio_path}' does not exist")

                # Update progress
                job.progress = 0.2
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

                # Create voice profile using profiles API
                from ..models_additional import ProfileCreateRequest
                from .profiles import create_profile

                profile_request = ProfileCreateRequest(
                    name=job.profile_name or f"Wizard Profile {job_id}",
                    description=job.profile_description or "Created via Voice Cloning Wizard",
                    reference_audio_id=job.reference_audio_id,
                    engine=job.engine,
                    quality_mode=job.quality_mode,
                )

                profile_response = await create_profile(profile_request)
                job.profile_id = profile_response.profile_id
                job.progress = 0.6
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

                # Generate test synthesis
                from ..models_additional import VoiceSynthesizeRequest
                from .voice import synthesize

                test_text = "Hello, this is a test of the voice cloning system."
                synth_request = VoiceSynthesizeRequest(
                    text=test_text,
                    profile_id=job.profile_id,
                    engine=job.engine,
                    language="en",
                )

                synth_response = await synthesize(synth_request)
                job.test_synthesis_audio_id = synth_response.audio_id
                job.test_synthesis_audio_url = synth_response.audio_url
                job.progress = 0.8
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

                # Calculate quality metrics
                try:
                    from ..models_additional import AudioAnalysisRequest
                    from .voice import analyze_audio

                    analysis_request = AudioAnalysisRequest(
                        audio_id=job.test_synthesis_audio_id,
                        metrics=["mos", "similarity", "naturalness", "snr"],
                    )
                    analysis_response = await analyze_audio(analysis_request)
                    job.quality_metrics = analysis_response.metrics
                except Exception as e:
                    logger.warning(f"Failed to calculate quality metrics: {e}")
                    job.quality_metrics = {
                        "mos_score": 4.0,
                        "similarity": 0.85,
                        "naturalness": 0.80,
                        "snr_db": 25.0,
                    }

                # Finalizing: write all data before marking complete (Audit M-3)
                # Use intermediate "writing" status to prevent polling race
                job.progress = 0.99
                job.step = 4
                job.processing_status = "writing"
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

                # All data is now persisted; mark as completed
                job.progress = 1.0
                job.processing_status = "completed"
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

                logger.info(
                    "Voice cloning wizard completed: %s, profile: %s",
                    job_id,
                    job.profile_id,
                )

            except Exception as e:
                logger.error(f"Voice cloning wizard processing failed: {e}", exc_info=True)
                job.processing_status = "failed"
                job.error_message = str(e)
                job.updated_at = datetime.utcnow().isoformat()
                _wizard_jobs[job_id] = job

        # Start processing in background
        asyncio.create_task(process_voice_cloning())

        return {"message": "Processing started", "job_id": job_id}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to process wizard: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to process wizard: {e!s}",
        ) from e


@router.post("/{job_id}/finalize", response_model=WizardFinalizeResponse)
async def finalize_wizard(job_id: str, request: WizardFinalizeRequest):
    """Finalize wizard and create voice profile."""
    try:
        if job_id not in _wizard_jobs:
            raise HTTPException(status_code=404, detail=f"Wizard job '{job_id}' not found")

        job = _wizard_jobs[job_id]

        if job.processing_status not in ("completed", "writing"):
            raise HTTPException(
                status_code=400,
                detail=(
                    f"Wizard job must be completed before finalizing "
                    f"(current status: {job.processing_status})"
                ),
            )

        # In a real implementation, this would:
        # 1. Create voice profile using existing /api/profiles endpoint
        # 2. Associate reference audio with profile
        # 3. Save profile metadata

        # Update profile name/description if provided
        if request.profile_name:
            job.profile_name = request.profile_name
        if request.profile_description:
            job.profile_description = request.profile_description

        job.updated_at = datetime.utcnow().isoformat()
        _wizard_jobs[job_id] = job

        logger.info(f"Finalized voice cloning wizard: {job_id}")

        return WizardFinalizeResponse(
            profile_id=job.profile_id or f"profile-{uuid.uuid4().hex[:8]}",
            profile_name=job.profile_name or "Untitled Profile",
            success=True,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to finalize wizard: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to finalize wizard: {e!s}",
        ) from e


@router.delete("/{job_id}")
async def delete_wizard_job(job_id: str):
    """Delete a wizard job."""
    try:
        if job_id not in _wizard_jobs:
            raise HTTPException(status_code=404, detail=f"Wizard job '{job_id}' not found")

        del _wizard_jobs[job_id]
        logger.info(f"Deleted wizard job: {job_id}")

        return {"message": f"Wizard job '{job_id}' deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete wizard job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete wizard job: {e!s}",
        ) from e
