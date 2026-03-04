"""
Effects Management Routes

CRUD operations for effect chains and presets.
Enhanced with PostFXProcessor for professional-quality audio effects.
"""

from __future__ import annotations

import logging
import os
import tempfile
import uuid
from datetime import datetime

import numpy as np
from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

# Try importing PostFXProcessor for professional effects
from typing import Any as _AnyType

_PostFXProcessor: _AnyType = None
_create_post_fx_processor: _AnyType = None
HAS_POST_FX = False

try:
    from backend.audio.post_fx import PostFXProcessor as _PFX
    from backend.audio.post_fx import create_post_fx_processor as _create_pfx

    _PostFXProcessor = _PFX
    _create_post_fx_processor = _create_pfx
    HAS_POST_FX = True
except ImportError:
    logger.debug("PostFXProcessor not available. Effects will use basic implementations.")

router = APIRouter(prefix="/api/effects", tags=["effects"])


# Pydantic models
class EffectParameter(BaseModel):
    name: str
    value: float
    min_value: float = 0.0
    max_value: float = 1.0
    unit: str | None = None


class Effect(BaseModel):
    id: str
    type: str
    name: str
    enabled: bool = True
    order: int
    parameters: list[EffectParameter] = []


class EffectChain(BaseModel):
    id: str
    name: str
    description: str | None = None
    project_id: str
    effects: list[Effect] = []
    created: str
    modified: str


class EffectChainCreateRequest(BaseModel):
    name: str
    description: str | None = None
    effects: list[Effect] | None = None


class EffectChainUpdateRequest(BaseModel):
    name: str | None = None
    description: str | None = None
    effects: list[Effect] | None = None


class EffectPreset(BaseModel):
    id: str
    effect_type: str
    name: str
    description: str | None = None
    parameters: list[EffectParameter] = []
    created: str
    modified: str


class EffectPresetCreateRequest(BaseModel):
    effect_type: str
    name: str
    description: str | None = None
    parameters: list[EffectParameter] | None = None


class EffectProcessRequest(BaseModel):
    audio_id: str
    output_filename: str | None = None


class EffectProcessResponse(BaseModel):
    success: bool
    output_audio_id: str | None = None
    message: str


# GAP-BE-001: Persistent storage (migrated from in-memory Dict to JsonFileStore)
from backend.audio.effects.effect_chain_store import (
    get_effect_chain_store,
    get_effect_preset_store,
)

_MAX_CHAINS = 1000
_MAX_PRESETS = 500


def _get_effect_chains() -> dict[str, EffectChain]:
    """Get all effect chains as a dict (compatibility wrapper)."""
    store = get_effect_chain_store()
    return {chain["id"]: EffectChain(**chain) for chain in store.list_all()}


def _get_chain(chain_id: str) -> EffectChain | None:
    """Get an effect chain by ID."""
    store = get_effect_chain_store()
    chain_data = store.get(chain_id)
    if chain_data:
        return EffectChain(**chain_data)
    return None


def _save_chain(chain: EffectChain) -> None:
    """Save an effect chain."""
    store = get_effect_chain_store()
    store.save(chain.model_dump())


def _delete_chain(chain_id: str) -> bool:
    """Delete an effect chain."""
    store = get_effect_chain_store()
    return store.delete(chain_id)


def _get_chains_for_project(project_id: str) -> list[EffectChain]:
    """Get all effect chains for a project."""
    store = get_effect_chain_store()
    return [EffectChain(**chain) for chain in store.list_by_project(project_id)]


def _get_effect_presets() -> dict[str, EffectPreset]:
    """Get all effect presets as a dict (compatibility wrapper)."""
    store = get_effect_preset_store()
    return {preset["id"]: EffectPreset(**preset) for preset in store.list_all()}


def _get_preset(preset_id: str) -> EffectPreset | None:
    """Get an effect preset by ID."""
    store = get_effect_preset_store()
    preset_data = store.get(preset_id)
    if preset_data:
        return EffectPreset(**preset_data)
    return None


def _save_preset(preset: EffectPreset) -> None:
    """Save an effect preset."""
    store = get_effect_preset_store()
    store.save(preset.model_dump())


def _delete_preset(preset_id: str) -> bool:
    """Delete an effect preset."""
    store = get_effect_preset_store()
    return store.delete(preset_id)


def _validate_project_id(project_id: str) -> None:
    """Validate that project_id is not empty."""
    if not project_id or not project_id.strip():
        raise HTTPException(status_code=400, detail="Project ID is required")


def _validate_chain_id(chain_id: str) -> None:
    """Validate that chain_id is not empty."""
    if not chain_id or not chain_id.strip():
        raise HTTPException(status_code=400, detail="Chain ID is required")


def _validate_preset_id(preset_id: str) -> None:
    """Validate that preset_id is not empty."""
    if not preset_id or not preset_id.strip():
        raise HTTPException(status_code=400, detail="Preset ID is required")


@router.get("/chains", response_model=list[EffectChain])
@cache_response(ttl=30)  # Cache for 30 seconds (effect chains may change frequently)
def list_effect_chains(project_id: str = Query(..., description="Project ID")) -> list[EffectChain]:
    """
    List all effect chains for a project.
    """
    try:
        _validate_project_id(project_id)

        # Filter chains by project_id - using persistent store
        chains = _get_chains_for_project(project_id)

        logger.info(f"Listed {len(chains)} effect chains for project {project_id}")
        return chains
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error listing effect chains for project {project_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to list effect chains: {e!s}")


@router.get("/chains/{chain_id}", response_model=EffectChain)
@cache_response(ttl=60)  # Cache for 60 seconds (effect chain info is relatively static)
def get_effect_chain(
    chain_id: str, project_id: str = Query(..., description="Project ID")
) -> EffectChain:
    """
    Get a specific effect chain.
    """
    try:
        _validate_chain_id(chain_id)
        _validate_project_id(project_id)

        chain = _get_chain(chain_id)
        if chain is None:
            logger.warning(f"Effect chain not found: {chain_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Verify project_id matches
        if chain.project_id != project_id:
            logger.warning(f"Effect chain {chain_id} does not belong to project {project_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        logger.info(f"Retrieved effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error retrieving effect chain {chain_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to retrieve effect chain: {e!s}")


@router.post("/chains", response_model=EffectChain)
def create_effect_chain(
    request: EffectChainCreateRequest,
    project_id: str = Query(..., description="Project ID"),
) -> EffectChain:
    """
    Create a new effect chain.
    """
    try:
        _validate_project_id(project_id)

        # Validate name
        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Chain name is required")

        # Check limit using persistent store
        store = get_effect_chain_store()
        if store.count_by_project(project_id) >= _MAX_CHAINS:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of effect chains ({_MAX_CHAINS}) reached for this project",
            )

        # Generate ID
        chain_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        # Create chain
        chain = EffectChain(
            id=chain_id,
            name=request.name.strip(),
            description=request.description.strip() if request.description else None,
            project_id=project_id,
            effects=request.effects or [],
            created=now,
            modified=now,
        )

        _save_chain(chain)

        logger.info(f"Created effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating effect chain: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create effect chain: {e!s}")


@router.put("/chains/{chain_id}", response_model=EffectChain)
def update_effect_chain(
    chain_id: str,
    request: EffectChainUpdateRequest,
    project_id: str = Query(..., description="Project ID"),
) -> EffectChain:
    """
    Update an existing effect chain.
    """
    try:
        _validate_chain_id(chain_id)
        _validate_project_id(project_id)

        chain = _get_chain(chain_id)
        if chain is None:
            logger.warning(f"Effect chain not found: {chain_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Verify project_id matches
        if chain.project_id != project_id:
            logger.warning(f"Effect chain {chain_id} does not belong to project {project_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Update fields
        if request.name is not None:
            if not request.name.strip():
                raise HTTPException(status_code=400, detail="Chain name cannot be empty")
            chain.name = request.name.strip()

        if request.description is not None:
            chain.description = request.description.strip() if request.description else None

        if request.effects is not None:
            chain.effects = request.effects

        chain.modified = datetime.utcnow().isoformat()
        _save_chain(chain)

        logger.info(f"Updated effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating effect chain {chain_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update effect chain: {e!s}")


@router.delete("/chains/{chain_id}")
def delete_effect_chain(
    chain_id: str, project_id: str = Query(..., description="Project ID")
) -> dict[str, bool]:
    """
    Delete an effect chain.
    """
    try:
        _validate_chain_id(chain_id)
        _validate_project_id(project_id)

        chain = _get_chain(chain_id)
        if chain is None:
            logger.warning(f"Effect chain not found: {chain_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Verify project_id matches
        if chain.project_id != project_id:
            logger.warning(f"Effect chain {chain_id} does not belong to project {project_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        _delete_chain(chain_id)

        logger.info(f"Deleted effect chain {chain_id} for project {project_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting effect chain {chain_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete effect chain: {e!s}")


@router.post("/chains/{chain_id}/process", response_model=EffectProcessResponse)
def process_audio_with_chain(
    chain_id: str,
    request: EffectProcessRequest,
    project_id: str = Query(..., description="Project ID"),
) -> EffectProcessResponse:
    """
    Process audio with an effect chain.

    Applies all enabled effects in the chain sequentially to the audio file.
    """
    try:
        _validate_chain_id(chain_id)
        _validate_project_id(project_id)

        if not request.audio_id or not request.audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")

        chain = _get_chain(chain_id)
        if chain is None:
            logger.warning(f"Effect chain not found: {chain_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Verify project_id matches
        if chain.project_id != project_id:
            logger.warning(f"Effect chain {chain_id} does not belong to project {project_id}")
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Check if chain has effects
        enabled_effects = [e for e in chain.effects if e.enabled]
        if not enabled_effects:
            raise HTTPException(status_code=400, detail="Effect chain has no enabled effects")

        # Load audio file
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(request.audio_id)
        if not audio_path:
            raise HTTPException(
                status_code=404, detail=f"Audio file '{request.audio_id}' not found"
            )
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Load audio
        try:
            from backend.audio.audio_utils import load_audio

            audio, sample_rate = load_audio(audio_path)
        except ImportError:
            raise HTTPException(status_code=503, detail="Audio processing libraries not available")
        except Exception as e:
            logger.error(f"Failed to load audio: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to load audio file: {e!s}")

        # Apply effects in order
        processed_audio = audio.copy()

        # Sort effects by order
        sorted_effects = sorted(enabled_effects, key=lambda e: e.order)

        # Try using PostFXProcessor for better quality effects (if available)
        if HAS_POST_FX and _PostFXProcessor is not None:
            try:
                processor = _create_post_fx_processor(sample_rate=sample_rate)
                # Convert effects to PostFXProcessor format
                effects_list = []
                for effect in sorted_effects:
                    params = {p.name: p.value for p in effect.parameters}
                    # Enable pedalboard for professional effects
                    params["use_pedalboard"] = True
                    effects_list.append(
                        {
                            "type": effect.type,
                            "enabled": effect.enabled,
                            "params": params,
                            "order": effect.order,
                        }
                    )
                processed_audio = processor.process(
                    processed_audio, sample_rate=sample_rate, effects=effects_list
                )
                logger.debug(
                    f"Processed audio with PostFXProcessor ({len(sorted_effects)} effects)"
                )
            except Exception as e:
                logger.warning(f"PostFXProcessor failed: {e}. Falling back to basic effects.")
                # Fall back to basic effects
                for effect in sorted_effects:
                    try:
                        processed_audio = _apply_effect(processed_audio, sample_rate, effect)
                    except Exception as e2:
                        logger.warning(f"Failed to apply effect {effect.name}: {e2}")
        else:
            # Use basic effect implementations
            for effect in sorted_effects:
                try:
                    processed_audio = _apply_effect(processed_audio, sample_rate, effect)
                except Exception as e:
                    logger.warning(f"Failed to apply effect {effect.name}: {e}")
                    # Continue with next effect

        # Save and register via artifact spine
        try:
            from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

            output_audio_id, _, _ = create_audio_artifact_from_wav_array(
                processed_audio,
                sample_rate,
                created_by="effects",
                audio_id=f"effect_{uuid.uuid4().hex[:8]}",
            )

            logger.info(
                f"Processed audio {request.audio_id} with chain {chain_id} "
                f"(project {project_id}), output: {output_audio_id}"
            )

            return EffectProcessResponse(
                success=True,
                output_audio_id=output_audio_id,
                message=f"Audio processed successfully with {len(sorted_effects)} effects",
            )
        except Exception as e:
            logger.error(f"Failed to save processed audio: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to save processed audio: {e!s}")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error processing audio with chain {chain_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")


@router.get("/presets", response_model=list[EffectPreset])
def list_effect_presets(
    effect_type: str | None = Query(None, description="Filter by effect type")
) -> list[EffectPreset]:
    """
    List all effect presets, optionally filtered by effect type.
    """
    try:
        store = get_effect_preset_store()

        # Filter by effect type if provided
        if effect_type:
            effect_type = effect_type.strip().lower()
            presets = [EffectPreset(**p) for p in store.list_by_type(effect_type)]
        else:
            presets = [EffectPreset(**p) for p in store.list_all()]

        logger.info(
            f"Listed {len(presets)} effect presets"
            + (f" (type: {effect_type})" if effect_type else "")
        )
        return presets
    except Exception as e:
        logger.error(f"Error listing effect presets: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list effect presets: {e!s}")


@router.post("/presets", response_model=EffectPreset)
def create_effect_preset(request: EffectPresetCreateRequest) -> EffectPreset:
    """
    Create a new effect preset.
    """
    try:
        # Validate required fields
        if not request.effect_type or not request.effect_type.strip():
            raise HTTPException(status_code=400, detail="Effect type is required")

        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Preset name is required")

        # Check limit using persistent store
        store = get_effect_preset_store()
        if store.count() >= _MAX_PRESETS:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of effect presets ({_MAX_PRESETS}) reached",
            )

        # Generate ID
        preset_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        # Create preset
        preset = EffectPreset(
            id=preset_id,
            effect_type=request.effect_type.strip(),
            name=request.name.strip(),
            description=request.description.strip() if request.description else None,
            parameters=request.parameters or [],
            created=now,
            modified=now,
        )

        _save_preset(preset)

        logger.info(f"Created effect preset {preset_id} (type: {request.effect_type})")
        return preset
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating effect preset: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create effect preset: {e!s}")


@router.delete("/presets/{preset_id}")
def delete_effect_preset(preset_id: str) -> dict[str, bool]:
    """
    Delete an effect preset.
    """
    try:
        _validate_preset_id(preset_id)

        if not _delete_preset(preset_id):
            logger.warning(f"Effect preset not found: {preset_id}")
            raise HTTPException(status_code=404, detail=f"Effect preset not found: {preset_id}")

        logger.info(f"Deleted effect preset {preset_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting effect preset {preset_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete effect preset: {e!s}")


def _apply_effect(audio: np.ndarray, sample_rate: int, effect: Effect) -> np.ndarray:
    """Apply a single effect to audio."""
    try:
        # Extract parameters
        params = {p.name: p.value for p in effect.parameters}

        effect_type = effect.type.lower()

        if effect_type == "eq":
            return _apply_eq(audio, sample_rate, params)
        elif effect_type == "compressor":
            return _apply_compressor(audio, sample_rate, params)
        elif effect_type == "reverb":
            return _apply_reverb(audio, sample_rate, params)
        elif effect_type == "delay":
            return _apply_delay(audio, sample_rate, params)
        elif effect_type == "filter":
            return _apply_filter(audio, sample_rate, params)
        else:
            logger.warning(f"Unknown effect type: {effect_type}")
            return audio
    except Exception as e:
        logger.error(f"Failed to apply effect {effect.name}: {e}")
        return audio


def _apply_eq(audio: np.ndarray, sample_rate: int, params: dict[str, float]) -> np.ndarray:
    """Apply EQ (3-band equalizer) to audio."""
    try:
        from scipy import signal

        low_gain = params.get("low_gain", 0.0)
        mid_gain = params.get("mid_gain", 0.0)
        high_gain = params.get("high_gain", 0.0)

        nyquist = sample_rate / 2
        processed = audio.copy()

        # Low shelf (below 500 Hz)
        if low_gain != 0.0:
            low_freq = 500.0 / nyquist
            b, a = signal.iirfilter(4, low_freq, btype="low", ftype="butter")
            low_band = signal.filtfilt(b, a, processed, axis=0)
            gain_db = low_gain
            gain_linear = 10 ** (gain_db / 20.0)
            processed = processed + (low_band * (gain_linear - 1.0))

        # Mid band (500-5000 Hz, centered at 2000 Hz)
        if mid_gain != 0.0:
            mid_low = 500.0 / nyquist
            mid_high = 5000.0 / nyquist
            b, a = signal.iirfilter(4, [mid_low, mid_high], btype="band", ftype="butter")
            mid_band = signal.filtfilt(b, a, processed, axis=0)
            gain_db = mid_gain
            gain_linear = 10 ** (gain_db / 20.0)
            processed = processed + (mid_band * (gain_linear - 1.0))

        # High shelf (above 5000 Hz)
        if high_gain != 0.0:
            high_freq = 5000.0 / nyquist
            b, a = signal.iirfilter(4, high_freq, btype="high", ftype="butter")
            high_band = signal.filtfilt(b, a, processed, axis=0)
            gain_db = high_gain
            gain_linear = 10 ** (gain_db / 20.0)
            processed = processed + (high_band * (gain_linear - 1.0))

        return processed
    except ImportError:
        logger.warning("scipy not available for EQ processing")
        return audio


def _apply_compressor(audio: np.ndarray, sample_rate: int, params: dict[str, float]) -> np.ndarray:
    """Apply compressor to audio."""
    try:
        threshold_db = params.get("threshold", -12.0)
        ratio = params.get("ratio", 4.0)
        attack_ms = params.get("attack", 5.0)
        release_ms = params.get("release", 50.0)

        threshold_linear = 10 ** (threshold_db / 20.0)
        int(sample_rate * attack_ms / 1000.0)
        int(sample_rate * release_ms / 1000.0)

        processed = audio.copy()

        # Simple RMS-based compression
        window_size = int(sample_rate * 0.01)  # 10ms window
        if len(processed.shape) == 1:
            # Mono
            rms = np.sqrt(
                np.convolve(processed**2, np.ones(window_size) / window_size, mode="same")
            )
            gain_reduction = np.ones_like(rms)
            above_threshold = rms > threshold_linear
            if np.any(above_threshold):
                excess = rms[above_threshold] - threshold_linear
                gain_reduction[above_threshold] = threshold_linear + excess / ratio
                gain_reduction[above_threshold] = (
                    gain_reduction[above_threshold] / rms[above_threshold]
                )
            processed = processed * gain_reduction
        else:
            # Stereo - process each channel
            for ch in range(processed.shape[1]):
                rms = np.sqrt(
                    np.convolve(
                        processed[:, ch] ** 2,
                        np.ones(window_size) / window_size,
                        mode="same",
                    )
                )
                gain_reduction = np.ones_like(rms)
                above_threshold = rms > threshold_linear
                if np.any(above_threshold):
                    excess = rms[above_threshold] - threshold_linear
                    gain_reduction[above_threshold] = threshold_linear + excess / ratio
                    gain_reduction[above_threshold] = (
                        gain_reduction[above_threshold] / rms[above_threshold]
                    )
                processed[:, ch] = processed[:, ch] * gain_reduction

        return processed
    except Exception as e:
        logger.warning(f"Compressor processing failed: {e}")
        return audio


def _apply_reverb(audio: np.ndarray, sample_rate: int, params: dict[str, float]) -> np.ndarray:
    """Apply reverb to audio."""
    try:
        room_size = params.get("room_size", 0.5)
        damping = params.get("damping", 0.5)
        wet_level = params.get("wet_level", 0.3)

        # Simple reverb using multiple delay taps
        reverb_length = int(sample_rate * room_size * 0.5)
        impulse = np.exp(-np.linspace(0, 5 * (1.0 - damping), reverb_length))
        impulse = impulse / np.sum(impulse)

        processed = audio.copy()
        if len(processed.shape) == 1:
            reverb = np.convolve(processed, impulse, mode="same")
            processed = processed * (1.0 - wet_level) + reverb * wet_level
        else:
            for ch in range(processed.shape[1]):
                reverb = np.convolve(processed[:, ch], impulse, mode="same")
                processed[:, ch] = processed[:, ch] * (1.0 - wet_level) + reverb * wet_level

        return processed
    except Exception as e:
        logger.warning(f"Reverb processing failed: {e}")
        return audio


def _apply_delay(audio: np.ndarray, sample_rate: int, params: dict[str, float]) -> np.ndarray:
    """Apply delay to audio."""
    try:
        delay_ms = params.get("delay_time", 250.0)
        feedback = params.get("feedback", 0.3)
        mix = params.get("mix", 0.5)

        delay_samples = int(sample_rate * delay_ms / 1000.0)

        processed = audio.copy()
        if len(processed.shape) == 1:
            delayed = np.pad(processed, (delay_samples, 0))[:-delay_samples]
            # Simple feedback (single iteration)
            delayed = delayed + processed * feedback
            processed = processed * (1.0 - mix) + delayed * mix
        else:
            for ch in range(processed.shape[1]):
                delayed = np.pad(processed[:, ch], (delay_samples, 0))[:-delay_samples]
                delayed = delayed + processed[:, ch] * feedback
                processed[:, ch] = processed[:, ch] * (1.0 - mix) + delayed * mix

        return processed
    except Exception as e:
        logger.warning(f"Delay processing failed: {e}")
        return audio


def _apply_filter(audio: np.ndarray, sample_rate: int, params: dict[str, float]) -> np.ndarray:
    """Apply filter (lowpass/highpass/bandpass) to audio."""
    try:
        from scipy import signal

        cutoff = params.get("cutoff", 1000.0)
        filter_type = int(params.get("filter_type", 0))  # 0=lowpass, 1=highpass, 2=bandpass

        nyquist = sample_rate / 2
        normalized_cutoff = cutoff / nyquist

        if filter_type == 0:  # Lowpass
            b, a = signal.butter(4, normalized_cutoff, btype="low")
        elif filter_type == 1:  # Highpass
            b, a = signal.butter(4, normalized_cutoff, btype="high")
        else:  # Bandpass
            low = max(20.0, cutoff * 0.5) / nyquist
            high = min(nyquist - 1, cutoff * 1.5) / nyquist
            b, a = signal.butter(4, [low, high], btype="band")

        processed = audio.copy()
        if len(processed.shape) == 1:
            processed = signal.filtfilt(b, a, processed)
        else:
            for ch in range(processed.shape[1]):
                processed[:, ch] = signal.filtfilt(b, a, processed[:, ch])

        return processed
    except ImportError:
        logger.warning("scipy not available for filter processing")
        return audio


# =============================================================================
# Project-scoped effect chain routes (path-based for frontend compatibility)
# Frontend expects: /api/effects/chains/{projectId}/{chainId}
# =============================================================================

project_effects_router = APIRouter(prefix="/api/effects/chains", tags=["project-effects"])


@project_effects_router.get("/{project_id}", response_model=list[EffectChain])
@cache_response(ttl=30)
async def list_project_effect_chains(project_id: str) -> list[EffectChain]:
    """
    List all effect chains for a project.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)
        chains = _get_chains_for_project(project_id)
        logger.info(f"Listed {len(chains)} effect chains for project {project_id}")
        return chains
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error listing effect chains for project {project_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list effect chains: {e!s}")


@project_effects_router.get("/{project_id}/{chain_id}", response_model=EffectChain)
@cache_response(ttl=60)
async def get_project_effect_chain(project_id: str, chain_id: str) -> EffectChain:
    """
    Get a specific effect chain for a project.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)
        _validate_chain_id(chain_id)

        chain = _get_chain(chain_id)
        if chain is None:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        if chain.project_id != project_id:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        logger.info(f"Retrieved effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error getting effect chain {chain_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get effect chain: {e!s}")


@project_effects_router.post("/{project_id}", response_model=EffectChain)
async def create_project_effect_chain(
    project_id: str,
    request: EffectChainCreateRequest,
) -> EffectChain:
    """
    Create a new effect chain for a project.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)

        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Chain name is required")

        store = get_effect_chain_store()
        if store.count_by_project(project_id) >= _MAX_CHAINS:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of effect chains ({_MAX_CHAINS}) reached for this project",
            )

        chain_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        chain = EffectChain(
            id=chain_id,
            name=request.name.strip(),
            description=request.description.strip() if request.description else None,
            project_id=project_id,
            effects=request.effects or [],
            created=now,
            modified=now,
        )

        _save_chain(chain)
        logger.info(f"Created effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating effect chain: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create effect chain: {e!s}")


@project_effects_router.put("/{project_id}/{chain_id}", response_model=EffectChain)
async def update_project_effect_chain(
    project_id: str,
    chain_id: str,
    request: EffectChainUpdateRequest,
) -> EffectChain:
    """
    Update an existing effect chain.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)
        _validate_chain_id(chain_id)

        chain = _get_chain(chain_id)
        if chain is None:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        if chain.project_id != project_id:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        if request.name is not None:
            if not request.name.strip():
                raise HTTPException(status_code=400, detail="Chain name cannot be empty")
            chain.name = request.name.strip()

        if request.description is not None:
            chain.description = request.description.strip() if request.description else None

        if request.effects is not None:
            chain.effects = request.effects

        chain.modified = datetime.utcnow().isoformat()
        _save_chain(chain)

        logger.info(f"Updated effect chain {chain_id} for project {project_id}")
        return chain
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating effect chain {chain_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update effect chain: {e!s}")


@project_effects_router.delete("/{project_id}/{chain_id}")
async def delete_project_effect_chain(project_id: str, chain_id: str) -> dict[str, bool]:
    """
    Delete an effect chain.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)
        _validate_chain_id(chain_id)

        chain = _get_chain(chain_id)
        if chain is None:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        if chain.project_id != project_id:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        _delete_chain(chain_id)
        logger.info(f"Deleted effect chain {chain_id} for project {project_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting effect chain {chain_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete effect chain: {e!s}")


@project_effects_router.post(
    "/{project_id}/{chain_id}/process", response_model=EffectProcessResponse
)
async def process_project_effect_chain(
    project_id: str,
    chain_id: str,
    audio_id: str = Query(..., description="Audio file ID to process"),
    output_filename: str | None = Query(None, description="Output filename"),
) -> EffectProcessResponse:
    """
    Process audio through a project's effect chain.
    Path-based route for frontend compatibility.
    """
    try:
        _validate_project_id(project_id)
        _validate_chain_id(chain_id)

        if not audio_id or not audio_id.strip():
            raise HTTPException(status_code=400, detail="Audio ID is required")

        chain = _get_chain(chain_id)
        if chain is None:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        if chain.project_id != project_id:
            raise HTTPException(status_code=404, detail=f"Effect chain not found: {chain_id}")

        # Get enabled effects
        enabled_effects = [e for e in chain.effects if e.enabled]
        if not enabled_effects:
            return EffectProcessResponse(
                success=True,
                output_audio_id=audio_id,
                message="No enabled effects in chain, audio unchanged",
            )

        # Attempt to load and process audio
        try:
            from backend.services.audio_artifacts import AudioRegistry

            audio_path = AudioRegistry.get_path(audio_id)
            if not audio_path:
                raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")
            if not os.path.exists(audio_path):
                raise HTTPException(
                    status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
                )

            from backend.audio.audio_utils import load_audio
            from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

            audio, sample_rate = load_audio(audio_path)

            import time

            start_time = time.time()

            processed_audio = audio.copy()
            sorted_effects = sorted(enabled_effects, key=lambda e: e.order)

            for effect in sorted_effects:
                params = {p.name: p.value for p in effect.parameters}
                if effect.type == "reverb":
                    processed_audio = _apply_reverb(processed_audio, sample_rate, params)
                elif effect.type == "eq":
                    processed_audio = _apply_eq(processed_audio, sample_rate, params)
                elif effect.type == "compressor":
                    processed_audio = _apply_compressor(processed_audio, sample_rate, params)
                elif effect.type == "delay":
                    processed_audio = _apply_delay(processed_audio, sample_rate, params)
                elif effect.type == "filter":
                    processed_audio = _apply_filter(processed_audio, sample_rate, params)

            # Save and register via artifact spine
            output_audio_id, _, _ = create_audio_artifact_from_wav_array(
                processed_audio,
                sample_rate,
                created_by="effects",
                audio_id=f"effect_{uuid.uuid4().hex[:8]}",
            )
            processing_time = time.time() - start_time

            logger.info(
                f"Processed audio with {len(sorted_effects)} effects in {processing_time:.2f}s"
            )
            return EffectProcessResponse(
                success=True,
                output_audio_id=output_audio_id,
                message=f"Applied {len(sorted_effects)} effects successfully",
            )

        except ImportError as ie:
            logger.warning(f"Audio processing not available: {ie}")
            raise HTTPException(status_code=503, detail="Audio processing libraries not available")
        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Audio processing failed: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail=f"Failed to process audio: {e!s}")

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error processing effect chain {chain_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to process effect chain: {e!s}")
