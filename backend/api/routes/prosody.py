"""
Prosody & Phoneme Control Routes

Endpoints for prosody and phoneme-level control of speech synthesis.
Enhanced with pyrubberband for high-quality pitch/rate modification and Phonemizer for phoneme analysis.
"""

from __future__ import annotations

import logging
import asyncio
import uuid

import numpy as np
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

# Try importing audio processing utilities
try:
    from app.core.audio.audio_utils import pitch_shift_audio, time_stretch_audio

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    logger.debug(
        "Audio utilities not available. Prosody modifications will use basic implementations."
    )

# Try importing Phonemizer for better phoneme analysis
try:
    from ..voice_speech import Phonemizer

    HAS_PHONEMIZER = True
except ImportError:
    HAS_PHONEMIZER = False
    logger.debug("Phonemizer not available. Phoneme analysis will use fallback methods.")

router = APIRouter(prefix="/api/prosody", tags=["prosody"])

# In-memory prosody configurations (replace with database in production)
_prosody_configs: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class ProsodyConfig(BaseModel):
    """Prosody configuration for speech synthesis."""

    config_id: str
    name: str
    pitch: float = 1.0  # 0.5 to 2.0
    rate: float = 1.0  # 0.5 to 2.0
    volume: float = 1.0  # 0.0 to 1.0
    emphasis: dict[str, float] | None = None  # Word-level emphasis
    pauses: list[dict] | None = None  # Pause positions and durations
    intonation: str | None = None  # rising, falling, flat, etc.


class PhonemeMapping(BaseModel):
    """Phoneme-level mapping and control."""

    text: str
    phonemes: list[str]
    timings: list[float]  # Duration for each phoneme
    stress: list[int] | None = None  # Stress levels (0-2)
    pitch_curve: list[float] | None = None  # Pitch per phoneme


class ProsodyApplyRequest(BaseModel):
    """Request to apply prosody configuration."""

    config_id: str
    text: str
    voice_profile_id: str
    engine: str | None = None
    language: str | None = None


class ProsodyCreateRequest(BaseModel):
    """Request to create a prosody configuration."""

    name: str
    pitch: float = 1.0
    rate: float = 1.0
    volume: float = 1.0
    emphasis: dict[str, float] | None = None
    pauses: list[dict] | None = None
    intonation: str | None = None


@router.post("/configs", response_model=ProsodyConfig)
async def create_prosody_config(request: ProsodyCreateRequest):
    """Create a new prosody configuration."""
    import uuid

    try:
        config_id = f"prosody-{uuid.uuid4().hex[:8]}"

        config = ProsodyConfig(
            config_id=config_id,
            name=request.name,
            pitch=request.pitch,
            rate=request.rate,
            volume=request.volume,
            emphasis=request.emphasis,
            pauses=request.pauses,
            intonation=request.intonation,
        )

        _prosody_configs[config_id] = config.model_dump()
        logger.info(f"Created prosody config: {config_id}")

        return config
    except Exception as e:
        logger.error(f"Failed to create prosody config: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create config: {e!s}",
        ) from e


@router.get("/configs", response_model=list[ProsodyConfig])
@cache_response(ttl=60)  # Cache for 60 seconds (config list doesn't change frequently)
async def list_prosody_configs():
    """List all prosody configurations."""
    return [ProsodyConfig(**c) for c in _prosody_configs.values()]


@router.get("/configs/{config_id}", response_model=ProsodyConfig)
@cache_response(ttl=300)  # Cache for 5 minutes (config info is relatively static)
async def get_prosody_config(config_id: str):
    """Get a prosody configuration by ID."""
    if config_id not in _prosody_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    return ProsodyConfig(**_prosody_configs[config_id])


@router.put("/configs/{config_id}", response_model=ProsodyConfig)
async def update_prosody_config(config_id: str, request: ProsodyCreateRequest):
    """Update a prosody configuration."""
    if config_id not in _prosody_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    config = ProsodyConfig(
        config_id=config_id,
        name=request.name,
        pitch=request.pitch,
        rate=request.rate,
        volume=request.volume,
        emphasis=request.emphasis,
        pauses=request.pauses,
        intonation=request.intonation,
    )

    _prosody_configs[config_id] = config.model_dump()
    logger.info(f"Updated prosody config: {config_id}")

    return config


@router.delete("/configs/{config_id}")
async def delete_prosody_config(config_id: str):
    """Delete a prosody configuration."""
    if config_id not in _prosody_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    del _prosody_configs[config_id]
    logger.info(f"Deleted prosody config: {config_id}")
    return {"success": True}


@router.post("/phonemes/analyze")
async def analyze_phonemes(text: str, language: str = "en"):
    """
    Analyze text to extract phonemes.

    Uses Phonemizer (phonemizer/gruut) if available for highest quality,
    then espeak-ng, otherwise falls back to lexicon estimation.
    """
    if not text or not text.strip():
        raise HTTPException(status_code=400, detail="Text cannot be empty")

    # Try Phonemizer first (highest quality)
    if HAS_PHONEMIZER:
        try:
            phonemizer = Phonemizer()
            phonemes = phonemizer.phonemize_with_phonemizer(text, language=language)
            if phonemes:
                return {
                    "text": text,
                    "language": language,
                    "phonemes": phonemes,
                    "method": "phonemizer",
                }
        except Exception as e:
            logger.debug(f"Phonemizer failed: {e}, trying fallback methods")

    # Try to use espeak-ng for phoneme analysis
    try:
        import subprocess

        result = subprocess.run(
            ["espeak", "-q", "--ipa", "-v", language, text],
            capture_output=True,
            text=True,
            timeout=2,
        )
        if result.returncode == 0 and result.stdout.strip():
            phonemes = result.stdout.strip()
            return {
                "text": text,
                "language": language,
                "phonemes": phonemes,
                "method": "espeak-ng",
            }
    except (FileNotFoundError, subprocess.TimeoutExpired, Exception) as e:
        logger.debug(f"espeak-ng not available: {e}")

    # Fallback: Use lexicon route for phoneme estimation
    try:
        from .lexicon import PhonemeEstimateRequest, estimate_phonemes

        # Split text into words and estimate phonemes for each
        words = text.split()
        phoneme_results = []
        for word in words:
            try:
                phoneme_req = PhonemeEstimateRequest(word=word, language=language)
                phoneme_result = await estimate_phonemes(phoneme_req)
                if phoneme_result and phoneme_result.get("pronunciation"):
                    phoneme_results.append(phoneme_result["pronunciation"])
            except Exception:
                phoneme_results.append(f"/{word}/")

        return {
            "text": text,
            "language": language,
            "phonemes": " ".join(phoneme_results),
            "method": "lexicon-estimation",
        }
    except Exception as e:
        logger.warning(f"Phoneme estimation failed: {e}")
        raise HTTPException(status_code=503, detail=f"Phoneme analysis unavailable: {e!s}")


@router.post("/apply")
async def apply_prosody(request: ProsodyApplyRequest):
    """
    Apply prosody configuration to text synthesis.

    Applies prosody settings (pitch, rate, volume, intonation) to a
    voice synthesis request and returns the synthesized audio.
    """
    if request.config_id not in _prosody_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    config = ProsodyConfig(**_prosody_configs[request.config_id])

    # Apply prosody by calling voice synthesis with prosody parameters
    try:
        from ..models_additional import VoiceSynthesizeRequest
        from .voice import synthesize

        # Map prosody config to synthesis parameters
        speed = config.rate  # Rate maps to speed
        emotion = config.intonation if config.intonation else None

        synth_request = VoiceSynthesizeRequest(
            text=request.text,
            profile_id=request.voice_profile_id,
            engine=request.engine or "xtts",
            language=request.language or "en",
            emotion=emotion,
        )

        synth_response = await synthesize(synth_request)

        # Apply pitch and volume modifications in post-processing
        import os
        import tempfile

        from app.core.audio.audio_utils import load_audio, save_audio

        from .voice import _audio_storage, _register_audio_file

        # Load synthesized audio
        if synth_response.audio_id not in _audio_storage:
            raise HTTPException(
                status_code=404,
                detail=(f"Synthesized audio " f"'{synth_response.audio_id}' not found"),
            )

        audio_path = _audio_storage[synth_response.audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file at '{audio_path}' does not exist",
            )

        audio, sample_rate = load_audio(audio_path)

        # Apply pitch modification if needed (use pyrubberband for higher quality)
        if config.pitch != 1.0:
            try:
                # config.pitch: 0.5 to 2.0 (1.0 = no change)
                # Convert to semitones: log2(pitch) * 12
                semitones = 12 * (config.pitch - 1.0)

                if HAS_AUDIO_UTILS:
                    # Use audio_utils which has pyrubberband support
                    audio = pitch_shift_audio(audio, sample_rate, semitones)
                    logger.info(f"Applied pitch shift using audio_utils: {semitones:.2f} semitones")
                else:
                    # Fallback to librosa
                    import librosa

                    audio = librosa.effects.pitch_shift(
                        audio,
                        sr=sample_rate,
                        n_steps=semitones,
                    )
                    logger.info(f"Applied pitch shift using librosa: {semitones:.2f} semitones")
            except ImportError:
                logger.warning(
                    "Audio processing libraries not available, skipping pitch modification"
                )
            except Exception as e:
                logger.warning(f"Pitch shift failed: {e}, continuing without it")

        # Apply rate modification if needed (use pyrubberband for higher quality)
        if config.rate != 1.0:
            try:
                if HAS_AUDIO_UTILS:
                    # Use audio_utils which has pyrubberband support for time-stretching
                    audio = time_stretch_audio(
                        audio, sample_rate, rate=config.rate, preserve_pitch=True
                    )
                    logger.info(f"Applied rate modification using audio_utils: {config.rate:.2f}x")
                else:
                    # Fallback to librosa
                    import librosa

                    audio = librosa.effects.time_stretch(audio, rate=config.rate)
                    logger.info(f"Applied rate modification using librosa: {config.rate:.2f}x")
            except ImportError:
                logger.warning(
                    "Audio processing libraries not available, skipping rate modification"
                )
            except Exception as e:
                logger.warning(f"Rate modification failed: {e}, continuing without it")

        # Apply volume modification if needed
        if config.volume != 1.0:
            # Volume/gain adjustment: multiply by volume factor
            audio = audio * config.volume
            # Prevent clipping
            max_val = np.max(np.abs(audio))
            if max_val > 1.0:
                audio = audio / max_val
            logger.info(f"Applied volume adjustment: {config.volume:.2f}")

        # Save modified audio
        output_path = tempfile.mktemp(suffix=".wav")
        save_audio(audio, sample_rate, output_path)

        # Register modified audio
        modified_audio_id = f"prosody_{uuid.uuid4().hex[:8]}"
        _register_audio_file(modified_audio_id, output_path)

        # Calculate duration
        duration = len(audio) / sample_rate

        return {
            "audio_id": modified_audio_id,
            "original_audio_id": synth_response.audio_id,
            "audio_url": f"/api/audio/{modified_audio_id}",
            "duration": duration,
            "prosody_applied": True,
            "config_applied": {
                "pitch": config.pitch,
                "rate": config.rate,
                "volume": config.volume,
                "intonation": config.intonation,
            },
        }
    except Exception as e:
        logger.error(f"Prosody application failed: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to apply prosody: {e!s}")
