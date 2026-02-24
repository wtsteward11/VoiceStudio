"""
Spatial Audio Routes

Endpoints for spatial audio positioning and 3D audio effects.
"""

from __future__ import annotations

import logging
import os
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.ml.models.engine_service import get_engine_service

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/spatial-audio", tags=["spatial-audio"])

# In-memory spatial audio configurations (replace with database in production)
_spatial_configs: dict[str, dict] = {}


class SpatialPosition(BaseModel):
    """3D spatial position for audio."""

    x: float = 0.0  # -1.0 to 1.0 (left to right)
    y: float = 0.0  # -1.0 to 1.0 (back to front)
    z: float = 0.0  # -1.0 to 1.0 (down to up)
    distance: float = 1.0  # Distance from listener (0.0 to 10.0)


class SpatialConfig(BaseModel):
    """Spatial audio configuration."""

    config_id: str
    name: str
    audio_id: str
    position: SpatialPosition
    room_size: float = 1.0  # Room size multiplier
    reverb_amount: float = 0.0  # 0.0 to 1.0
    occlusion: float = 0.0  # Occlusion factor (0.0 to 1.0)
    doppler: bool = False  # Enable Doppler effect
    hrtf: bool = True  # Use HRTF (Head-Related Transfer Function)


class SpatialMovement(BaseModel):
    """Movement path for spatial audio."""

    start_position: SpatialPosition
    end_position: SpatialPosition
    duration: float  # Duration in seconds
    easing: str = "linear"  # linear, ease-in, ease-out, ease-in-out


class SpatialConfigCreateRequest(BaseModel):
    """Request to create a spatial audio configuration."""

    name: str
    audio_id: str
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    distance: float = 1.0
    room_size: float = 1.0
    reverb_amount: float = 0.0
    occlusion: float = 0.0
    doppler: bool = False
    hrtf: bool = True


class SpatialApplyRequest(BaseModel):
    """Request to apply spatial audio to audio file."""

    config_id: str
    output_format: str = "wav"


@router.post("/configs", response_model=SpatialConfig)
async def create_spatial_config(request: SpatialConfigCreateRequest):
    """Create a new spatial audio configuration."""
    import uuid

    try:
        config_id = f"spatial-{uuid.uuid4().hex[:8]}"

        position = SpatialPosition(
            x=request.x,
            y=request.y,
            z=request.z,
            distance=request.distance,
        )

        config = SpatialConfig(
            config_id=config_id,
            name=request.name,
            audio_id=request.audio_id,
            position=position,
            room_size=request.room_size,
            reverb_amount=request.reverb_amount,
            occlusion=request.occlusion,
            doppler=request.doppler,
            hrtf=request.hrtf,
        )

        _spatial_configs[config_id] = config.model_dump()
        logger.info(f"Created spatial config: {config_id}")

        return config
    except Exception as e:
        logger.error(f"Failed to create spatial config: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create config: {e!s}",
        ) from e


@router.get("/configs", response_model=list[SpatialConfig])
@cache_response(ttl=60)  # Cache for 60 seconds (config list may change)
async def list_spatial_configs(audio_id: str | None = None):
    """List all spatial audio configurations."""
    configs = list(_spatial_configs.values())
    if audio_id:
        configs = [c for c in configs if c.get("audio_id") == audio_id]
    return [SpatialConfig(**c) for c in configs]


@router.get("/configs/{config_id}", response_model=SpatialConfig)
@cache_response(ttl=300)  # Cache for 5 minutes (config info is relatively static)
async def get_spatial_config(config_id: str):
    """Get a spatial audio configuration by ID."""
    if config_id not in _spatial_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    return SpatialConfig(**_spatial_configs[config_id])


@router.put("/configs/{config_id}", response_model=SpatialConfig)
async def update_spatial_config(config_id: str, request: SpatialConfigCreateRequest):
    """Update a spatial audio configuration."""
    if config_id not in _spatial_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    position = SpatialPosition(
        x=request.x,
        y=request.y,
        z=request.z,
        distance=request.distance,
    )

    config = SpatialConfig(
        config_id=config_id,
        name=request.name,
        audio_id=request.audio_id,
        position=position,
        room_size=request.room_size,
        reverb_amount=request.reverb_amount,
        occlusion=request.occlusion,
        doppler=request.doppler,
        hrtf=request.hrtf,
    )

    _spatial_configs[config_id] = config.model_dump()
    logger.info(f"Updated spatial config: {config_id}")

    return config


@router.delete("/configs/{config_id}")
async def delete_spatial_config(config_id: str):
    """Delete a spatial audio configuration."""
    if config_id not in _spatial_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    del _spatial_configs[config_id]
    logger.info(f"Deleted spatial config: {config_id}")
    return {"success": True}


@router.post("/apply")
async def apply_spatial_audio(request: SpatialApplyRequest):
    """
    Apply spatial audio configuration to audio file.

    Applies spatial audio effects including:
    - Distance attenuation (inverse square law)
    - Stereo panning based on X position
    - Reverb processing based on room characteristics
    - Occlusion filtering (low-pass) for obstacles
    - Doppler effect simulation for moving sources
    """
    if request.config_id not in _spatial_configs:
        raise HTTPException(status_code=404, detail="Config not found")

    config = SpatialConfig(**_spatial_configs[request.config_id])

    logger.info(f"Applying spatial audio to audio '{config.audio_id}' with config '{config.name}'")

    # Implement real spatial audio processing
    try:
        import os
        import uuid

        import numpy as np

        from .voice import _audio_storage, _register_audio_file

        # Get audio file path
        if config.audio_id not in _audio_storage:
            raise HTTPException(status_code=404, detail=f"Audio file '{config.audio_id}' not found")

        audio_path = _audio_storage[config.audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Load audio
        try:
            import librosa
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)  # Convert to mono for processing
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="Audio processing libraries (soundfile, librosa) not available",
            )

        # Apply spatial audio effects
        processed_audio = audio.copy()

        # 1. Apply distance attenuation
        distance = config.position.distance
        if distance > 0:
            # Inverse square law for distance attenuation
            attenuation = 1.0 / (distance**2)
            processed_audio = processed_audio * min(1.0, attenuation)

        # 2. Apply panning based on x position (-1.0 to 1.0)
        x_pos = config.position.x
        if len(audio.shape) == 1:
            # Convert to stereo for panning
            left_gain = max(0.0, min(1.0, (1.0 - x_pos) / 2.0))
            right_gain = max(0.0, min(1.0, (1.0 + x_pos) / 2.0))
            processed_audio = np.array(
                [processed_audio * left_gain, processed_audio * right_gain]
            ).T

        # 3. Apply reverb based on room_size and reverb_amount
        if config.reverb_amount > 0:
            try:
                # Simple reverb using convolution with impulse response
                reverb_length = int(sample_rate * config.room_size * config.reverb_amount)
                impulse = np.exp(-np.linspace(0, 5, reverb_length))
                impulse = impulse / np.sum(impulse)

                # Apply reverb to each channel
                if len(processed_audio.shape) > 1:
                    for ch in range(processed_audio.shape[1]):
                        processed_audio[:, ch] = np.convolve(
                            processed_audio[:, ch], impulse, mode="same"
                        )
                else:
                    processed_audio = np.convolve(processed_audio, impulse, mode="same")
            except Exception as e:
                logger.warning(f"Reverb processing failed: {e}")

        # 4. Apply occlusion filtering (low-pass filter)
        if config.occlusion > 0:
            try:
                from scipy import signal

                # Low-pass filter based on occlusion amount
                cutoff = 20000 * (1.0 - config.occlusion)  # Reduce high frequencies
                nyquist = sample_rate / 2
                normalized_cutoff = cutoff / nyquist
                b, a = signal.butter(4, normalized_cutoff, btype="low")

                if len(processed_audio.shape) > 1:
                    for ch in range(processed_audio.shape[1]):
                        processed_audio[:, ch] = signal.filtfilt(b, a, processed_audio[:, ch])
                else:
                    processed_audio = signal.filtfilt(b, a, processed_audio)
            except (ImportError, Exception) as e:
                logger.warning(f"Occlusion filtering failed: {e}")

        # 5. Apply Doppler effect if enabled
        if config.doppler:
            # Simplified Doppler: pitch shift based on z position
            # Positive z = moving away (lower pitch), negative z = moving closer (higher pitch)
            z_pos = config.position.z
            pitch_shift = z_pos * 0.1  # Small pitch shift
            if pitch_shift != 0:
                try:
                    processed_audio = librosa.effects.pitch_shift(
                        processed_audio, sr=sample_rate, n_steps=pitch_shift * 12
                    )
                except Exception as e:
                    logger.warning(f"Doppler effect failed: {e}")

        # Save processed audio
        import tempfile

        output_path = tempfile.mktemp(suffix=f".{request.output_format}")
        sf.write(output_path, processed_audio, sample_rate)

        # Register new audio file
        output_audio_id = f"spatial_{uuid.uuid4().hex[:8]}"
        _register_audio_file(output_audio_id, output_path)

        # Calculate quality metrics (ADR-008 compliant)
        quality_metrics: dict[str, Any] = {}
        try:
            engine_service = get_engine_service()

            # Calculate metrics on processed audio
            if len(processed_audio.shape) > 1:
                # Use first channel for metrics
                mono_audio = processed_audio[:, 0]
            else:
                mono_audio = processed_audio

            # Normalize for metrics calculation
            if np.max(np.abs(mono_audio)) > 0:
                mono_audio_normalized = mono_audio / np.max(np.abs(mono_audio))
            else:
                mono_audio_normalized = mono_audio

            quality_metrics = {
                "mos_score": float(engine_service.calculate_mos_score(mono_audio_normalized)),
                "snr_db": float(engine_service.calculate_snr(mono_audio_normalized)),
                "dynamic_range": float(np.max(mono_audio) - np.min(mono_audio)),
                "rms_level": float(np.sqrt(np.mean(mono_audio**2))),
                "peak_level": float(np.max(np.abs(mono_audio))),
            }

            # Calculate spatial accuracy metrics
            if config.hrtf:
                # ITD accuracy (should match expected ITD based on position)
                azimuth_rad = np.arctan2(
                    config.position.x,
                    np.sqrt(config.position.y**2 + config.position.z**2),
                )
                expected_itd = np.sin(azimuth_rad) * 0.0006  # Max ITD in seconds
                quality_metrics["hrtf_enabled"] = True
                quality_metrics["expected_itd_ms"] = float(expected_itd * 1000)
            else:
                quality_metrics["hrtf_enabled"] = False

            # Distance accuracy (attenuation should match inverse square law)
            expected_attenuation = 1.0 / (config.position.distance**2)
            actual_attenuation = (
                np.max(np.abs(processed_audio)) / np.max(np.abs(audio))
                if np.max(np.abs(audio)) > 0
                else 1.0
            )
            quality_metrics["distance_accuracy"] = float(
                1.0 - abs(expected_attenuation - actual_attenuation)
            )

        except Exception as e:
            logger.warning(f"Quality metrics calculation failed: {e}")
            quality_metrics = {"error": str(e)}

        logger.info(f"Spatial audio applied, output: {output_audio_id}")

        return {
            "success": True,
            "audio_id": output_audio_id,
            "config_id": request.config_id,
            "quality_metrics": quality_metrics,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to apply spatial audio: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to apply spatial audio: {e!s}")


@router.post("/preview")
async def preview_spatial_audio(
    audio_id: str,
    x: float = 0.0,
    y: float = 0.0,
    z: float = 0.0,
    distance: float = 1.0,
):
    """
    Preview spatial audio position in real-time.

    Applies spatial audio effects and returns processed audio for preview.
    """
    try:
        import tempfile
        import uuid

        import numpy as np

        from .voice import _audio_storage, _register_audio_file

        # Get audio file path
        if audio_id not in _audio_storage:
            raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")

        audio_path = _audio_storage[audio_id]
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Load audio
        try:
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="Audio processing library (soundfile) not available",
            )

        # Apply spatial effects (same as apply_spatial_audio but for preview)
        processed_audio = audio.copy()

        # Distance attenuation
        if distance > 0:
            attenuation = 1.0 / (distance**2)
            processed_audio = processed_audio * min(1.0, attenuation)

        # Panning
        if len(audio.shape) == 1:
            left_gain = max(0.0, min(1.0, (1.0 - x) / 2.0))
            right_gain = max(0.0, min(1.0, (1.0 + x) / 2.0))
            processed_audio = np.array(
                [processed_audio * left_gain, processed_audio * right_gain]
            ).T

        # Save preview audio
        output_path = tempfile.mktemp(suffix=".wav")
        sf.write(output_path, processed_audio, sample_rate)

        # Register preview audio
        preview_audio_id = f"spatial_preview_{uuid.uuid4().hex[:8]}"
        _register_audio_file(preview_audio_id, output_path)

        return {
            "audio_id": preview_audio_id,
            "position": {"x": x, "y": y, "z": z, "distance": distance},
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to preview spatial audio: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to preview spatial audio: {e!s}")


# Simplified endpoints for Spatial Audio Panel (matching spec)
class SpatialPositionRequest(BaseModel):
    """Request to set voice position in 3D space."""

    audio_id: str
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    distance: float = 1.0


class SpatialEnvironmentRequest(BaseModel):
    """Request to configure room/environment settings."""

    room_size: float = 1.0
    material: str = "concrete"  # concrete, wood, carpet, metal, etc.
    reverb_amount: float = 0.0
    doppler: bool = False


class SpatialProcessRequest(BaseModel):
    """Request to process audio with spatial effects."""

    audio_id: str
    config_id: str | None = None
    position: SpatialPosition | None = None
    environment: SpatialEnvironmentRequest | None = None
    output_format: str = "wav"


class SpatialBinauralRequest(BaseModel):
    """Request to generate binaural audio for headphones."""

    audio_id: str
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    distance: float = 1.0
    hrtf: bool = True


@router.post("/position")
async def set_voice_position(request: SpatialPositionRequest):
    """Set voice position in 3D space (simplified endpoint for panel)."""
    try:
        # Create or update config for this audio
        config_name = f"Position for {request.audio_id}"
        position = SpatialPosition(
            x=request.x,
            y=request.y,
            z=request.z,
            distance=request.distance,
        )

        # Find existing config or create new
        existing_config_id = None
        for config_id, config_data in _spatial_configs.items():
            if config_data.get("audio_id") == request.audio_id:
                existing_config_id = config_id
                break

        if existing_config_id:
            # Update existing
            config_data = _spatial_configs[existing_config_id]
            config_data["position"] = position.model_dump()
            config = SpatialConfig(**config_data)
            _spatial_configs[existing_config_id] = config.model_dump()
            return config
        else:
            # Create new
            create_request = SpatialConfigCreateRequest(
                name=config_name,
                audio_id=request.audio_id,
                x=request.x,
                y=request.y,
                z=request.z,
                distance=request.distance,
            )
            return await create_spatial_config(create_request)
    except Exception as e:
        logger.error(f"Failed to set voice position: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to set voice position: {e!s}",
        ) from e


@router.post("/environment")
async def configure_environment(request: SpatialEnvironmentRequest):
    """Configure room/environment settings (simplified endpoint for panel)."""
    try:
        # Return environment settings (these would be applied to active configs)
        return {
            "room_size": request.room_size,
            "material": request.material,
            "reverb_amount": request.reverb_amount,
            "doppler": request.doppler,
        }
    except Exception as e:
        logger.error(f"Failed to configure environment: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to configure environment: {e!s}",
        ) from e


@router.post("/process")
async def process_spatial_audio(request: SpatialProcessRequest):
    """Process audio with spatial effects (simplified endpoint for panel)."""
    try:
        if request.config_id:
            # Use existing config
            apply_request = SpatialApplyRequest(
                config_id=request.config_id,
                output_format=request.output_format,
            )
            return await apply_spatial_audio(apply_request)
        else:
            # Create temporary config and apply
            if not request.position:
                raise HTTPException(
                    status_code=400,
                    detail="Position or config_id required",
                )

            create_request = SpatialConfigCreateRequest(
                name=f"Process {request.audio_id}",
                audio_id=request.audio_id,
                x=request.position.x,
                y=request.position.y,
                z=request.position.z,
                distance=request.position.distance,
                room_size=request.environment.room_size if request.environment else 1.0,
                reverb_amount=(request.environment.reverb_amount if request.environment else 0.0),
                doppler=request.environment.doppler if request.environment else False,
            )
            config = await create_spatial_config(create_request)

            apply_request = SpatialApplyRequest(
                config_id=config.config_id,
                output_format=request.output_format,
            )
            return await apply_spatial_audio(apply_request)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to process spatial audio: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to process spatial audio: {e!s}",
        ) from e


@router.post("/binaural")
async def generate_binaural_audio(request: SpatialBinauralRequest):
    """Generate binaural audio for headphones."""
    try:
        import tempfile
        import uuid

        import numpy as np

        from .voice import _audio_storage, _register_audio_file

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

        # Load audio
        try:
            import soundfile as sf

            audio, sample_rate = sf.read(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail="Audio processing library (soundfile) not available",
            )

        # Generate binaural audio
        # Simplified HRTF: use interaural time difference (ITD) and level difference (ILD)
        # Full HRTF would require HRTF dataset, but we can approximate with delays and filters

        processed_audio = audio.copy()

        # Calculate ITD (Interaural Time Difference) based on azimuth (x position)
        # ITD ranges from ~0.6ms (90 degrees) to 0ms (0 degrees)
        azimuth_rad = np.arctan2(request.x, np.sqrt(request.y**2 + request.z**2))
        itd_samples = int(np.sin(azimuth_rad) * sample_rate * 0.0006)

        # Apply ITD by delaying one channel
        if request.hrtf:
            # Create stereo output with ITD
            left_channel = processed_audio.copy()
            right_channel = processed_audio.copy()

            if itd_samples > 0:
                # Source to the right, delay left ear
                left_channel = np.pad(left_channel, (abs(itd_samples), 0))[: -abs(itd_samples)]
            elif itd_samples < 0:
                # Source to the left, delay right ear
                right_channel = np.pad(right_channel, (abs(itd_samples), 0))[: -abs(itd_samples)]

            # Apply ILD (Interaural Level Difference) - simple panning
            left_gain = max(0.0, min(1.0, (1.0 - request.x) / 2.0))
            right_gain = max(0.0, min(1.0, (1.0 + request.x) / 2.0))

            left_channel = left_channel * left_gain
            right_channel = right_channel * right_gain

            # Distance attenuation
            if request.distance > 0:
                attenuation = 1.0 / (request.distance**2)
                left_channel = left_channel * min(1.0, attenuation)
                right_channel = right_channel * min(1.0, attenuation)

            # Combine to stereo
            binaural_audio = np.array([left_channel, right_channel]).T
        else:
            # No HRTF, just stereo panning
            left_gain = max(0.0, min(1.0, (1.0 - request.x) / 2.0))
            right_gain = max(0.0, min(1.0, (1.0 + request.x) / 2.0))
            binaural_audio = np.array([processed_audio * left_gain, processed_audio * right_gain]).T

        # Save binaural audio
        output_path = tempfile.mktemp(suffix=".wav")
        sf.write(output_path, binaural_audio, sample_rate)

        # Register binaural audio
        binaural_audio_id = f"binaural_{uuid.uuid4().hex[:8]}"
        _register_audio_file(binaural_audio_id, output_path)

        # Calculate quality metrics for binaural audio
        quality_metrics: dict[str, Any] = {}
        try:
            engine_service = get_engine_service()

            # Calculate metrics on binaural audio (use left channel)
            mono_audio = binaural_audio[:, 0]

            # Normalize for metrics calculation
            if np.max(np.abs(mono_audio)) > 0:
                mono_audio_normalized = mono_audio / np.max(np.abs(mono_audio))
            else:
                mono_audio_normalized = mono_audio

            quality_metrics = {
                "mos_score": float(engine_service.calculate_mos_score(mono_audio_normalized)),
                "snr_db": float(engine_service.calculate_snr(mono_audio_normalized)),
                "dynamic_range": float(np.max(mono_audio) - np.min(mono_audio)),
                "rms_level": float(np.sqrt(np.mean(mono_audio**2))),
                "peak_level": float(np.max(np.abs(mono_audio))),
            }

            # Calculate HRTF-specific metrics
            if request.hrtf:
                azimuth_rad = np.arctan2(request.x, np.sqrt(request.y**2 + request.z**2))
                expected_itd = np.sin(azimuth_rad) * 0.0006
                quality_metrics["hrtf_enabled"] = True
                quality_metrics["expected_itd_ms"] = float(expected_itd * 1000)
                quality_metrics["itd_samples"] = int(np.sin(azimuth_rad) * sample_rate * 0.0006)

                # Calculate interaural coherence (should be high for good binaural)
                if len(binaural_audio.shape) > 1:
                    correlation = np.corrcoef(binaural_audio[:, 0], binaural_audio[:, 1])[0, 1]
                    quality_metrics["interaural_coherence"] = float(correlation)
            else:
                quality_metrics["hrtf_enabled"] = False

            # Calculate spatial positioning accuracy
            # Panning accuracy (left/right balance should match x position)
            if len(binaural_audio.shape) > 1:
                left_rms = np.sqrt(np.mean(binaural_audio[:, 0] ** 2))
                right_rms = np.sqrt(np.mean(binaural_audio[:, 1] ** 2))
                total_rms = left_rms + right_rms
                if total_rms > 0:
                    left_balance = left_rms / total_rms
                    expected_left_balance = max(0.0, min(1.0, (1.0 - request.x) / 2.0))
                    panning_accuracy = 1.0 - abs(left_balance - expected_left_balance)
                    quality_metrics["panning_accuracy"] = float(panning_accuracy)

        except Exception as e:
            logger.warning(f"Binaural quality metrics calculation failed: {e}")
            quality_metrics = {"error": str(e)}

        return {
            "audio_id": binaural_audio_id,
            "position": {
                "x": request.x,
                "y": request.y,
                "z": request.z,
                "distance": request.distance,
            },
            "hrtf": request.hrtf,
            "quality_metrics": quality_metrics,
        }
    except Exception as e:
        logger.error(f"Failed to generate binaural audio: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to generate binaural audio: {e!s}",
        ) from e
