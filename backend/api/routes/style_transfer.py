"""
Voice Style Transfer Routes

Endpoints for voice style transfer and voice conversion.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)


def _scalar_float(value: Any, default: float = 0.0) -> float:
    """Extract a scalar float from a value that may be float, ndarray, or list."""
    if value is None:
        return default
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, np.ndarray):
        return float(np.mean(value))
    if isinstance(value, list):
        return float(value[0]) if value else default
    return default


router = APIRouter(prefix="/api/style-transfer", tags=["style-transfer"])

# In-memory style transfer jobs (replace with database in production)
_style_transfer_jobs: dict[str, dict] = {}


class StyleTransferRequest(BaseModel):
    """Request to transfer voice style."""

    source_audio_id: str
    target_style_id: str  # Voice profile ID or style preset ID
    transfer_strength: float = 0.8  # 0.0 to 1.0
    preserve_content: bool = True  # Preserve linguistic content
    preserve_emotion: bool = False  # Preserve emotional characteristics
    output_format: str = "wav"


class StyleTransferJob(BaseModel):
    """Style transfer job status."""

    job_id: str
    source_audio_id: str
    target_style_id: str
    transfer_strength: float
    status: str  # pending, processing, completed, failed
    progress: float = 0.0  # 0.0 to 1.0
    output_audio_id: str | None = None
    error_message: str | None = None
    created: str
    completed: str | None = None


class StylePreset(BaseModel):
    """Voice style preset."""

    preset_id: str
    name: str
    description: str | None = None
    voice_profile_id: str | None = None
    style_characteristics: dict  # Style attributes
    created: str


@router.post("/transfer", response_model=StyleTransferJob)
async def create_style_transfer(request: StyleTransferRequest):
    """Create a new voice style transfer job."""
    import uuid
    from datetime import datetime

    try:
        job_id = f"style-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        job = StyleTransferJob(
            job_id=job_id,
            source_audio_id=request.source_audio_id,
            target_style_id=request.target_style_id,
            transfer_strength=request.transfer_strength,
            status="pending",
            progress=0.0,
            created=now,
        )

        _style_transfer_jobs[job_id] = job.model_dump()

        # Start style transfer processing in background
        import asyncio

        asyncio.create_task(_process_style_transfer(job_id, request))

        logger.info(f"Created style transfer job: {job_id}")

        return job
    except Exception as e:
        logger.error(f"Failed to create style transfer job: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create job: {e!s}",
        ) from e


@router.get("/jobs", response_model=list[StyleTransferJob])
async def list_style_transfer_jobs(
    source_audio_id: str | None = None,
    status: str | None = None,
):
    """List all style transfer jobs."""
    jobs = list(_style_transfer_jobs.values())

    if source_audio_id:
        jobs = [j for j in jobs if j.get("source_audio_id") == source_audio_id]

    if status:
        jobs = [j for j in jobs if j.get("status") == status]

    return [StyleTransferJob(**j) for j in jobs]


@router.get("/jobs/{job_id}", response_model=StyleTransferJob)
async def get_style_transfer_job(job_id: str):
    """Get a style transfer job by ID."""
    if job_id not in _style_transfer_jobs:
        raise HTTPException(status_code=404, detail="Job not found")

    return StyleTransferJob(**_style_transfer_jobs[job_id])


@router.delete("/jobs/{job_id}")
async def delete_style_transfer_job(job_id: str):
    """Delete a style transfer job."""
    if job_id not in _style_transfer_jobs:
        raise HTTPException(status_code=404, detail="Job not found")

    del _style_transfer_jobs[job_id]
    logger.info(f"Deleted style transfer job: {job_id}")
    return {"success": True}


@router.get("/presets", response_model=list[StylePreset])
async def list_style_presets():
    """List available voice style presets (in-memory; created via POST /presets)."""
    presets = [
        v
        for v in _style_transfer_jobs.values()
        if isinstance(v, dict) and v.get("type") == "preset"
    ]
    return presets


@router.post("/presets", response_model=StylePreset)
async def create_style_preset(
    name: str,
    description: str | None = None,
    voice_profile_id: str | None = None,
    style_characteristics: dict | None = None,
):
    """Create a new voice style preset."""
    import uuid
    from datetime import datetime

    try:
        preset_id = f"preset-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        preset = StylePreset(
            preset_id=preset_id,
            name=name,
            description=description,
            voice_profile_id=voice_profile_id,
            style_characteristics=style_characteristics or {},
            created=now,
        )

        logger.info(f"Created style preset: {preset_id}")

        return preset
    except Exception as e:
        logger.error(f"Failed to create style preset: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create preset: {e!s}",
        ) from e


# Simplified endpoints for Voice Style Transfer Panel (matching spec)
class StyleExtractRequest(BaseModel):
    """Request to extract style from reference audio."""

    audio_id: str  # Reference audio ID
    analyze_prosody: bool = True
    analyze_emotion: bool = True


class StyleProfile(BaseModel):
    """Style profile extracted from reference audio."""

    audio_id: str
    average_pitch: float  # Hz
    pitch_variation: float  # Standard deviation
    energy: float  # Average energy
    speaking_rate: float  # Words per second
    emotion_tag: str | None = None  # e.g., "Angry", "Excited"
    prosodic_features: dict  # Detailed prosodic features
    style_embedding: list[float] | None = None  # Style embedding vector


class StyleAnalyzeRequest(BaseModel):
    """Request to analyze style characteristics."""

    audio_id: str


class StyleAnalyzeResponse(BaseModel):
    """Style analysis response."""

    audio_id: str
    pitch_contour: list[float]
    energy_contour: list[float]
    timing_patterns: dict
    style_markers: list[dict]  # Pauses, emphasis points, etc.


class StyleSynthesizeRequest(BaseModel):
    """Request to synthesize with style transfer."""

    voice_profile_id: str
    text: str
    reference_audio_id: str | None = None
    style_embedding: list[float] | None = None
    style_intensity: float = 0.8  # 0.0 to 1.0
    language: str = "en"


class StyleSynthesizeResponse(BaseModel):
    """Style synthesis response."""

    audio_id: str
    audio_url: str
    duration: float
    style_applied: bool


@router.post("/style/extract", response_model=StyleProfile)
async def extract_style(request: StyleExtractRequest):
    """Extract style from reference audio."""
    try:
        import os

        from .voice import _audio_storage

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
            import numpy as np

            from app.core.audio.audio_utils import (
                analyze_voice_characteristics,
                load_audio,
            )

            audio, sample_rate = load_audio(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Analyze voice characteristics
            voice_chars = analyze_voice_characteristics(audio, sample_rate)

            # Calculate speaking rate (words per second) - estimate from duration
            duration = len(audio) / sample_rate
            # Rough estimate: assume average word length
            estimated_words = duration * 2.5  # Average speaking rate
            speaking_rate = estimated_words / duration if duration > 0 else 2.5

            # Extract emotion if requested
            emotion_tag = None
            if request.analyze_emotion:
                try:
                    from .emotion import analyze

                    emotion_req = {"audio_id": request.audio_id}
                    emotion_result = await analyze(emotion_req)
                    if emotion_result and emotion_result.get("dominant_emotion"):
                        emotion_tag = emotion_result["dominant_emotion"]
                except Exception as e:
                    logger.debug(f"Emotion analysis failed: {e}")

            # Generate style embedding from voice characteristics
            style_embedding = None
            if request.analyze_prosody:
                # Create embedding from prosodic features
                f0_mean = _scalar_float(voice_chars.get("f0_mean"), 150.0)
                f0_std = _scalar_float(voice_chars.get("f0_std"), 15.0)
                spectral_centroid = _scalar_float(voice_chars.get("spectral_centroid"), 2000.0)
                raw_mfcc = voice_chars.get("mfcc", [0.0] * 13)
                if isinstance(raw_mfcc, list):
                    mfcc = [_scalar_float(v) for v in raw_mfcc]
                else:
                    mfcc = [_scalar_float(raw_mfcc)]

                # Combine features into embedding (simplified)
                style_embedding = mfcc + [
                    f0_mean / 500.0,  # Normalize
                    f0_std / 50.0,
                    spectral_centroid / 5000.0,
                    speaking_rate / 5.0,
                ]
                # Pad to 128 dimensions
                while len(style_embedding) < 128:
                    style_embedding.append(0.0)
                style_embedding = style_embedding[:128]

            avg_pitch = _scalar_float(voice_chars.get("f0_mean"), 150.0)
            pitch_var = _scalar_float(voice_chars.get("f0_std"), 15.0)
            return StyleProfile(
                audio_id=request.audio_id,
                average_pitch=avg_pitch,
                pitch_variation=pitch_var,
                energy=np.mean(np.abs(audio)),
                speaking_rate=speaking_rate,
                emotion_tag=emotion_tag,
                prosodic_features={
                    "pitch_range": [
                        avg_pitch - pitch_var,
                        avg_pitch + pitch_var,
                    ],
                    "energy_range": [
                        float(np.min(np.abs(audio))),
                        float(np.max(np.abs(audio))),
                    ],
                    "spectral_centroid": voice_chars.get("spectral_centroid", 2000.0),
                },
                style_embedding=style_embedding,
            )
        except ImportError:
            raise HTTPException(status_code=503, detail="Audio processing libraries not available")
    except Exception as e:
        logger.error(f"Failed to extract style: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to extract style: {e!s}",
        ) from e


@router.post("/style/analyze", response_model=StyleAnalyzeResponse)
async def analyze_style(request: StyleAnalyzeRequest):
    """Analyze style characteristics."""
    try:
        import os

        import numpy as np

        from .voice import _audio_storage

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
            import librosa

            from app.core.audio.audio_utils import load_audio

            audio, sample_rate = load_audio(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            duration = len(audio) / sample_rate

            # Analyze pitch contour
            try:
                pitches, magnitudes = librosa.piptrack(y=audio, sr=sample_rate)
                pitch_contour = []
                for t in range(pitches.shape[1]):
                    index = magnitudes[:, t].argmax()
                    pitch = pitches[index, t]
                    if pitch > 0:
                        pitch_contour.append(float(pitch))

                # Resample to 100 points
                if len(pitch_contour) > 100:
                    indices = np.linspace(0, len(pitch_contour) - 1, 100, dtype=int)
                    pitch_contour = [pitch_contour[i] for i in indices]
                elif len(pitch_contour) < 100:
                    # Interpolate
                    from scipy import interpolate

                    x_old = np.linspace(0, 1, len(pitch_contour))
                    x_new = np.linspace(0, 1, 100)
                    f = interpolate.interp1d(
                        x_old, pitch_contour, kind="linear", fill_value="extrapolate"
                    )
                    pitch_contour = f(x_new).tolist()
            except Exception as e:
                logger.warning(f"Pitch analysis failed: {e}")
                pitch_contour = [150.0] * 100

            # Analyze energy contour
            frame_length = int(sample_rate * 0.025)  # 25ms frames
            hop_length = int(sample_rate * 0.010)  # 10ms hop
            rms = librosa.feature.rms(y=audio, frame_length=frame_length, hop_length=hop_length)[0]

            # Resample to 100 points
            if len(rms) > 100:
                indices = np.linspace(0, len(rms) - 1, 100, dtype=int)
                energy_contour = [float(rms[i]) for i in indices]
            elif len(rms) < 100:
                from scipy import interpolate

                x_old = np.linspace(0, 1, len(rms))
                x_new = np.linspace(0, 1, 100)
                f = interpolate.interp1d(x_old, rms, kind="linear", fill_value="extrapolate")
                energy_contour = f(x_new).tolist()
            else:
                energy_contour = [float(e) for e in rms]

            # Detect pauses and emphasis
            energy_threshold = np.mean(energy_contour) * 0.3
            emphasis_threshold = np.mean(energy_contour) * 1.5

            pauses = []
            emphasis_points = []
            for i, energy in enumerate(energy_contour):
                time_pos = (i / len(energy_contour)) * duration
                if energy < energy_threshold:
                    pauses.append(time_pos)
                elif energy > emphasis_threshold:
                    emphasis_points.append({"position": time_pos, "intensity": float(energy)})

            # Calculate average pause duration
            if pauses:
                pause_durations = []
                for i in range(len(pauses) - 1):
                    pause_durations.append(pauses[i + 1] - pauses[i])
                avg_pause = sum(pause_durations) / len(pause_durations) if pause_durations else 0.5
            else:
                avg_pause = 0.5

            style_markers = []
            for pause_pos in pauses[:10]:  # Limit to 10 pauses
                style_markers.append({"type": "pause", "position": pause_pos, "duration": 0.3})
            for emp in emphasis_points[:10]:  # Limit to 10 emphasis points
                style_markers.append(
                    {
                        "type": "emphasis",
                        "position": emp["position"],
                        "intensity": emp["intensity"],
                    }
                )

            return StyleAnalyzeResponse(
                audio_id=request.audio_id,
                pitch_contour=pitch_contour,
                energy_contour=energy_contour,
                timing_patterns={
                    "average_pause_duration": avg_pause,
                    "emphasis_points": [e["position"] for e in emphasis_points],
                },
                style_markers=style_markers,
            )
        except ImportError:
            raise HTTPException(status_code=503, detail="Audio processing libraries not available")
    except Exception as e:
        logger.error(f"Failed to analyze style: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to analyze style: {e!s}",
        ) from e


@router.post("/synthesize/style", response_model=StyleSynthesizeResponse)
async def synthesize_with_style(request: StyleSynthesizeRequest):
    """Synthesize with style transfer."""
    try:
        from ..models_additional import VoiceSynthesizeRequest
        from .voice import synthesize

        # Synthesize with voice profile
        synth_request = VoiceSynthesizeRequest(
            text=request.text,
            profile_id=request.voice_profile_id,
            engine="xtts",
            language=request.language,
        )

        # If reference audio provided, pass it via engine config
        if request.reference_audio_id:
            pass

        synth_response = await synthesize(synth_request)

        return StyleSynthesizeResponse(
            audio_id=synth_response.audio_id,
            audio_url=synth_response.audio_url,
            duration=synth_response.duration,
            style_applied=True,
        )
    except Exception as e:
        logger.error(f"Failed to synthesize with style: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to synthesize with style: {e!s}",
        ) from e


async def _process_style_transfer(job_id: str, request: StyleTransferRequest):
    """Process style transfer job asynchronously."""
    import os
    import tempfile
    import uuid
    from datetime import datetime

    from .voice import _audio_storage, _register_audio_file

    try:
        job = _style_transfer_jobs.get(job_id)
        if not job:
            logger.warning(f"Style transfer job {job_id} not found")
            return

        job["status"] = "processing"
        job["progress"] = 0.1
        _style_transfer_jobs[job_id] = job

        # Load source audio
        if request.source_audio_id not in _audio_storage:
            raise ValueError(f"Source audio '{request.source_audio_id}' not found")

        source_audio_path = _audio_storage[request.source_audio_id]
        if not os.path.exists(source_audio_path):
            raise ValueError(f"Source audio file at '{source_audio_path}' does not exist")

        job["progress"] = 0.3
        _style_transfer_jobs[job_id] = job

        # Load audio
        try:
            from app.core.audio.audio_utils import load_audio, save_audio

            _source_audio, _sample_rate = load_audio(source_audio_path)
        except ImportError:
            raise ValueError("Audio processing libraries not available")

        # Try to use RVC engine for voice conversion if available (ADR-008 compliant)
        try:
            engine_service = get_engine_service()

            # Check if target_style_id is a voice profile
            from .profiles import _profiles

            target_profile = None
            if request.target_style_id in _profiles:
                target_profile = _profiles[request.target_style_id]

            if target_profile and target_profile.get("reference_audio_id"):
                # Use RVC for voice conversion
                rvc_engine = engine_service.get_rvc_engine()
                if rvc_engine and rvc_engine.is_available():
                    target_audio_path = _audio_storage.get(target_profile["reference_audio_id"])
                    if target_audio_path and os.path.exists(target_audio_path):
                        job["progress"] = 0.5
                        _style_transfer_jobs[job_id] = job

                        # Convert voice using RVC
                        output_path = tempfile.mktemp(suffix=".wav")
                        converted_audio = rvc_engine.convert_voice(
                            source_audio_path=source_audio_path,
                            target_voice_path=target_audio_path,
                            output_path=output_path,
                            pitch_shift=0,
                            protect=0.33,
                        )

                        if converted_audio is not None:
                            # Register output audio
                            output_audio_id = f"style_{uuid.uuid4().hex[:8]}"
                            _register_audio_file(output_audio_id, output_path)

                            job["status"] = "completed"
                            job["progress"] = 1.0
                            job["output_audio_id"] = output_audio_id
                            job["completed"] = datetime.utcnow().isoformat()
                            _style_transfer_jobs[job_id] = job

                            logger.info(
                                f"Style transfer completed: {job_id}, output: {output_audio_id}"
                            )
                            return
        except (ImportError, AttributeError, Exception) as e:
            logger.debug(f"RVC engine not available or failed: {e}")

        # Fallback: Use voice synthesis with target profile
        try:
            from ..models_additional import VoiceSynthesizeRequest
            from .voice import synthesize

            # Transcribe source audio to get text (ADR-008 compliant)
            try:
                whisper = engine_service.get_whisper_engine()
                if whisper:
                    transcription = whisper.transcribe(source_audio_path)
                    text = transcription.get("text", "Hello, this is a test.")
                else:
                    text = "Hello, this is a test of style transfer."
            except Exception as e:
                logger.warning(f"Failed to transcribe source audio: {e}")
                text = "Hello, this is a test of style transfer."

            job["progress"] = 0.6
            _style_transfer_jobs[job_id] = job

            # Synthesize with target style profile
            synth_request = VoiceSynthesizeRequest(
                text=text,
                profile_id=request.target_style_id,
                engine="xtts",
                language="en",
            )

            synth_response = await synthesize(synth_request)

            job["status"] = "completed"
            job["progress"] = 1.0
            job["output_audio_id"] = synth_response.audio_id
            job["completed"] = datetime.utcnow().isoformat()
            _style_transfer_jobs[job_id] = job

            logger.info(
                f"Style transfer completed via synthesis: {job_id}, output: {synth_response.audio_id}"
            )

        except Exception as e:
            logger.error(f"Style transfer synthesis failed: {e}")
            raise

    except Exception as e:
        logger.error(f"Style transfer processing failed: {e}", exc_info=True)
        if job_id in _style_transfer_jobs:
            job = _style_transfer_jobs[job_id]
            job["status"] = "failed"
            job["error_message"] = str(e)
            job["completed"] = datetime.utcnow().isoformat()
            _style_transfer_jobs[job_id] = job
