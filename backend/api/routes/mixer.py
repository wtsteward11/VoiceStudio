"""
Mixer Management Routes

CRUD operations for mixer state, routing, sends/returns, sub-groups, and presets.
"""

from __future__ import annotations

import asyncio
import logging
import time
import uuid
from datetime import datetime
from enum import Enum
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models import ApiOk
from ..optimization import cache_response

logger = logging.getLogger(__name__)

# Import WebSocket broadcasting
try:
    from ..ws import realtime

    HAS_WEBSOCKET = True
except ImportError:
    HAS_WEBSOCKET = False
    logger.warning("WebSocket realtime module not available")

router = APIRouter(prefix="/api/mixer", tags=["mixer"])

# In-memory storage (replace with database in production)
from backend.api.routes._persistent_store import PersistentStore

_mixer_states: PersistentStore = PersistentStore("mixer_states")
_mixer_presets: PersistentStore = PersistentStore("mixer_presets")
_state_lock = asyncio.Lock()


def _validate_effect_chain_id(effect_chain_id: str | None, project_id: str) -> None:
    """
    Validate that an effect_chain_id exists if provided.

    Args:
        effect_chain_id: The effect chain ID to validate (can be None)
        project_id: The project context for the effect chain

    Raises:
        HTTPException: 400 if effect_chain_id is provided but doesn't exist
    """
    if not effect_chain_id:
        return  # None is valid - no effect chain assigned

    try:
        from backend.audio.effects.effect_chain_store import get_effect_chain_store

        store = get_effect_chain_store()
        chain = store.get(effect_chain_id)
        if not chain:
            raise HTTPException(
                status_code=400, detail=f"Effect chain '{effect_chain_id}' not found"
            )
    except ImportError:
        # Effects module not available - skip validation
        logger.debug("Effects module not available for validation")
    except HTTPException:
        raise
    except Exception as e:
        logger.warning(f"Failed to validate effect_chain_id: {e}")
        # Continue without validation on error


# Pydantic models matching C# models
class MixerSend(BaseModel):
    id: str
    name: str
    bus_number: int
    volume: float = 1.0
    is_enabled: bool = True


class MixerReturn(BaseModel):
    id: str
    name: str
    bus_number: int
    volume: float = 1.0
    pan: float = 0.0
    is_enabled: bool = True
    effect_chain_id: str | None = None


class MixerSubGroup(BaseModel):
    id: str
    name: str
    bus_number: int
    volume: float = 1.0
    pan: float = 0.0
    is_muted: bool = False
    is_soloed: bool = False
    effect_chain_id: str | None = None
    channel_ids: list[str] = []


class MixerMaster(BaseModel):
    id: str = "master"
    volume: float = 1.0
    pan: float = 0.0
    is_muted: bool = False
    effect_chain_id: str | None = None


class RoutingDestination(str, Enum):
    Master = "Master"
    SubGroup = "SubGroup"


class ChannelRouting(BaseModel):
    channel_id: str
    main_destination: str = "Master"  # "Master" or "SubGroup"
    sub_group_id: str | None = None
    send_levels: dict[str, float] = {}  # send_id -> level (0.0-1.0)
    send_enabled: dict[str, bool] = {}  # send_id -> enabled


class MixerChannel(BaseModel):
    id: str
    channel_number: int
    name: str
    peak_level: float = 0.0
    rms_level: float = 0.0
    volume: float = 1.0
    pan: float = 0.0
    is_muted: bool = False
    is_soloed: bool = False
    main_destination: str = "Master"
    sub_group_id: str | None = None
    send_levels: dict[str, float] = {}
    send_enabled: dict[str, bool] = {}


class MixerState(BaseModel):
    id: str
    project_id: str
    channels: list[MixerChannel] = []
    channel_routing: list[ChannelRouting] = []
    sends: list[MixerSend] = []
    returns: list[MixerReturn] = []
    sub_groups: list[MixerSubGroup] = []
    master: MixerMaster = MixerMaster()
    created: str
    modified: str


class MixerPreset(BaseModel):
    id: str
    name: str
    description: str | None = None
    project_id: str
    state: MixerState
    created: str
    modified: str


# Mixer State Endpoints


@router.get("/state/{project_id}", response_model=MixerState)
@cache_response(ttl=5)  # Cache for 5 seconds (mixer state changes frequently)
async def get_mixer_state(project_id: str):
    """Get mixer state for a project."""
    try:
        if project_id not in _mixer_states:
            # Return default mixer state
            return _create_default_mixer_state(project_id)
        return _mixer_states[project_id]
    except Exception as e:
        logger.error(f"Failed to get mixer state: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/state/{project_id}", response_model=MixerState)
async def update_mixer_state(project_id: str, state: MixerState):
    """Update mixer state for a project."""
    try:
        state_dict = state.model_dump()
        state_dict["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state_dict

        # Broadcast meter updates via WebSocket if available
        if HAS_WEBSOCKET and state.channels:
            for channel in state.channels:
                await realtime.broadcast_meter_updates(
                    project_id=project_id,
                    channel_id=channel.id,
                    meter_data={
                        "peak_level": channel.peak_level,
                        "rms_level": channel.rms_level,
                    },
                )

        logger.info(f"Updated mixer state for project: {project_id}")
        return state_dict
    except Exception as e:
        logger.error(f"Failed to update mixer state: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/state/{project_id}/reset", response_model=MixerState)
async def reset_mixer_state(project_id: str):
    """Reset mixer state to defaults for a project."""
    try:
        default_state = _create_default_mixer_state(project_id)
        _mixer_states[project_id] = default_state.model_dump()
        logger.info(f"Reset mixer state for project: {project_id}")
        return default_state
    except Exception as e:
        logger.error(f"Failed to reset mixer state: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Send/Return Endpoints


@router.post("/state/{project_id}/sends", response_model=MixerSend)
async def create_send(project_id: str, send: MixerSend):
    """Create a new send bus."""
    try:
        if project_id not in _mixer_states:
            _mixer_states[project_id] = _create_default_mixer_state(project_id).model_dump()

        state = _mixer_states[project_id]
        send_dict = send.model_dump()
        if not send_dict.get("id"):
            send_dict["id"] = str(uuid.uuid4())

        state["sends"].append(send_dict)
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Created send bus: {send_dict['id']}")
        return send_dict
    except Exception as e:
        logger.error(f"Failed to create send: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/state/{project_id}/sends/{send_id}", response_model=MixerSend)
async def update_send(project_id: str, send_id: str, send: MixerSend):
    """Update a send bus."""
    try:
        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        sends = state.get("sends", [])

        for i, s in enumerate(sends):
            if s.get("id") == send_id:
                send_dict = send.model_dump()
                send_dict["id"] = send_id
                sends[i] = send_dict
                state["modified"] = datetime.utcnow().isoformat()
                _mixer_states[project_id] = state
                logger.info(f"Updated send bus: {send_id}")
                return send_dict

        raise HTTPException(status_code=404, detail="Send bus not found")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update send: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/state/{project_id}/sends/{send_id}", response_model=ApiOk)
async def delete_send(project_id: str, send_id: str):
    """Delete a send bus."""
    try:
        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        sends = state.get("sends", [])
        state["sends"] = [s for s in sends if s.get("id") != send_id]
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Deleted send bus: {send_id}")
        return ApiOk()
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete send: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/state/{project_id}/returns", response_model=MixerReturn)
async def create_return(project_id: str, return_bus: MixerReturn):
    """Create a new return bus."""
    try:
        # Validate effect_chain_id if provided
        _validate_effect_chain_id(return_bus.effect_chain_id, project_id)

        if project_id not in _mixer_states:
            _mixer_states[project_id] = _create_default_mixer_state(project_id).model_dump()

        state = _mixer_states[project_id]
        return_dict = return_bus.model_dump()
        if not return_dict.get("id"):
            return_dict["id"] = str(uuid.uuid4())

        state["returns"].append(return_dict)
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Created return bus: {return_dict['id']}")
        return return_dict
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create return: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/state/{project_id}/returns/{return_id}", response_model=MixerReturn)
async def update_return(project_id: str, return_id: str, return_bus: MixerReturn):
    """Update a return bus."""
    try:
        # Validate effect_chain_id if provided
        _validate_effect_chain_id(return_bus.effect_chain_id, project_id)

        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        returns = state.get("returns", [])

        for i, r in enumerate(returns):
            if r.get("id") == return_id:
                return_dict = return_bus.model_dump()
                return_dict["id"] = return_id
                returns[i] = return_dict
                state["modified"] = datetime.utcnow().isoformat()
                _mixer_states[project_id] = state
                logger.info(f"Updated return bus: {return_id}")
                return return_dict

        raise HTTPException(status_code=404, detail="Return bus not found")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update return: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/state/{project_id}/returns/{return_id}", response_model=ApiOk)
async def delete_return(project_id: str, return_id: str):
    """Delete a return bus."""
    try:
        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        returns = state.get("returns", [])
        state["returns"] = [r for r in returns if r.get("id") != return_id]
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Deleted return bus: {return_id}")
        return ApiOk()
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete return: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Sub-Group Endpoints


@router.post("/state/{project_id}/subgroups", response_model=MixerSubGroup)
async def create_subgroup(project_id: str, subgroup: MixerSubGroup):
    """Create a new sub-group bus."""
    try:
        # Validate effect_chain_id if provided
        _validate_effect_chain_id(subgroup.effect_chain_id, project_id)

        if project_id not in _mixer_states:
            _mixer_states[project_id] = _create_default_mixer_state(project_id).model_dump()

        state = _mixer_states[project_id]
        subgroup_dict = subgroup.model_dump()
        if not subgroup_dict.get("id"):
            subgroup_dict["id"] = str(uuid.uuid4())

        state["sub_groups"].append(subgroup_dict)
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Created sub-group: {subgroup_dict['id']}")
        return subgroup_dict
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create sub-group: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/state/{project_id}/subgroups/{subgroup_id}", response_model=MixerSubGroup)
async def update_subgroup(project_id: str, subgroup_id: str, subgroup: MixerSubGroup):
    """Update a sub-group bus."""
    try:
        # Validate effect_chain_id if provided
        _validate_effect_chain_id(subgroup.effect_chain_id, project_id)

        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        subgroups = state.get("sub_groups", [])

        for i, sg in enumerate(subgroups):
            if sg.get("id") == subgroup_id:
                subgroup_dict = subgroup.model_dump()
                subgroup_dict["id"] = subgroup_id
                subgroups[i] = subgroup_dict
                state["modified"] = datetime.utcnow().isoformat()
                _mixer_states[project_id] = state
                logger.info(f"Updated sub-group: {subgroup_id}")
                return subgroup_dict

        raise HTTPException(status_code=404, detail="Sub-group not found")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update sub-group: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/state/{project_id}/subgroups/{subgroup_id}", response_model=ApiOk)
async def delete_subgroup(project_id: str, subgroup_id: str):
    """Delete a sub-group bus."""
    try:
        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Mixer state not found")

        state = _mixer_states[project_id]
        subgroups = state.get("sub_groups", [])
        state["sub_groups"] = [sg for sg in subgroups if sg.get("id") != subgroup_id]
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Deleted sub-group: {subgroup_id}")
        return ApiOk()
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete sub-group: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Master Bus Endpoints


@router.put("/state/{project_id}/master", response_model=MixerMaster)
async def update_master(project_id: str, master: MixerMaster):
    """Update master bus settings."""
    try:
        # Validate effect_chain_id if provided
        _validate_effect_chain_id(master.effect_chain_id, project_id)

        if project_id not in _mixer_states:
            _mixer_states[project_id] = _create_default_mixer_state(project_id).model_dump()

        state = _mixer_states[project_id]
        master_dict = master.model_dump()
        master_dict["id"] = "master"
        state["master"] = master_dict
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Updated master bus for project: {project_id}")
        return master_dict
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update master: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Channel Routing Endpoints


@router.put("/state/{project_id}/channels/{channel_id}/routing", response_model=ChannelRouting)
async def update_channel_routing(project_id: str, channel_id: str, routing: ChannelRouting):
    """Update routing for a specific channel."""
    try:
        if project_id not in _mixer_states:
            _mixer_states[project_id] = _create_default_mixer_state(project_id).model_dump()

        state = _mixer_states[project_id]
        routing_list = state.get("channel_routing", [])

        # Find existing routing or create new
        routing_dict = routing.model_dump()
        routing_dict["channel_id"] = channel_id

        found = False
        for i, r in enumerate(routing_list):
            if r.get("channel_id") == channel_id:
                routing_list[i] = routing_dict
                found = True
                break

        if not found:
            routing_list.append(routing_dict)

        state["channel_routing"] = routing_list
        state["modified"] = datetime.utcnow().isoformat()
        _mixer_states[project_id] = state

        logger.info(f"Updated routing for channel: {channel_id}")
        return routing_dict
    except Exception as e:
        logger.error(f"Failed to update channel routing: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Mixer Preset Endpoints


@router.get("/presets/{project_id}", response_model=list[MixerPreset])
@cache_response(ttl=60)  # Cache for 60 seconds (presets may change)
async def list_presets(project_id: str):
    """List all mixer presets for a project."""
    try:
        presets = [p for p in _mixer_presets.values() if p.get("project_id") == project_id]
        return presets
    except Exception as e:
        logger.error(f"Failed to list presets: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/presets/{project_id}/{preset_id}", response_model=MixerPreset)
@cache_response(ttl=300)  # Cache for 5 minutes (preset info is relatively static)
async def get_preset(project_id: str, preset_id: str):
    """Get a specific mixer preset."""
    if preset_id not in _mixer_presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    preset = _mixer_presets[preset_id]
    if preset.get("project_id") != project_id:
        raise HTTPException(status_code=404, detail="Preset not found")

    return preset


@router.post("/presets/{project_id}", response_model=MixerPreset)
async def create_preset(project_id: str, preset: MixerPreset):
    """Create a new mixer preset."""
    try:
        preset_dict = preset.model_dump()
        if not preset_dict.get("id"):
            preset_dict["id"] = str(uuid.uuid4())

        preset_dict["project_id"] = project_id
        preset_dict["created"] = datetime.utcnow().isoformat()
        preset_dict["modified"] = datetime.utcnow().isoformat()

        _mixer_presets[preset_dict["id"]] = preset_dict
        logger.info(f"Created mixer preset: {preset_dict['id']}")
        return preset_dict
    except Exception as e:
        logger.error(f"Failed to create preset: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/presets/{project_id}/{preset_id}", response_model=MixerPreset)
async def update_preset(project_id: str, preset_id: str, preset: MixerPreset):
    """Update a mixer preset."""
    if preset_id not in _mixer_presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    try:
        preset_dict = preset.model_dump()
        preset_dict["id"] = preset_id
        preset_dict["project_id"] = project_id
        preset_dict["modified"] = datetime.utcnow().isoformat()

        _mixer_presets[preset_id] = preset_dict
        logger.info(f"Updated mixer preset: {preset_id}")
        return preset_dict
    except Exception as e:
        logger.error(f"Failed to update preset: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/presets/{project_id}/{preset_id}", response_model=ApiOk)
async def delete_preset(project_id: str, preset_id: str):
    """Delete a mixer preset."""
    if preset_id not in _mixer_presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    try:
        del _mixer_presets[preset_id]
        logger.info(f"Deleted mixer preset: {preset_id}")
        return ApiOk()
    except Exception as e:
        logger.error(f"Failed to delete preset: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/presets/{project_id}/{preset_id}/apply", response_model=MixerState)
async def apply_preset(project_id: str, preset_id: str):
    """Apply a mixer preset to the current mixer state."""
    if preset_id not in _mixer_presets:
        raise HTTPException(status_code=404, detail="Preset not found")

    try:
        preset = _mixer_presets[preset_id]
        if preset.get("project_id") != project_id:
            raise HTTPException(status_code=404, detail="Preset not found")

        # Apply preset state to mixer state
        state = preset.get("state", {})
        state["id"] = str(uuid.uuid4())
        state["project_id"] = project_id
        state["modified"] = datetime.utcnow().isoformat()

        _mixer_states[project_id] = state
        logger.info(f"Applied preset {preset_id} to project {project_id}")
        return state
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to apply preset: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# Helper Functions


def _create_default_mixer_state(project_id: str) -> MixerState:
    """Create a default mixer state with 4 channels."""
    now = datetime.utcnow().isoformat()

    channels: list[MixerChannel] = []
    for i in range(1, 5):
        channels.append(
            MixerChannel(
                id=str(uuid.uuid4()),
                channel_number=i,
                name=f"Ch {i}",
            )
        )

    return MixerState(
        id=str(uuid.uuid4()),
        project_id=project_id,
        channels=channels,
        channel_routing=[],
        sends=[],
        returns=[],
        sub_groups=[],
        master=MixerMaster(),
        created=now,
        modified=now,
    )


@router.get("/meters/{project_id}", response_model=dict[str, Any])
@cache_response(ttl=1)  # Cache for 1 second (meters update very frequently)
async def get_mixer_meters(project_id: str):
    """
    Get real-time meter readings for all channels in a project.
    Returns current peak and RMS levels for each channel.
    """
    try:
        if project_id not in _mixer_states:
            return {"channels": []}

        state = _mixer_states[project_id]
        channels = state.get("channels", [])

        # Extract meter data from channels
        meter_data = {
            "project_id": project_id,
            "channels": [
                {
                    "channel_id": ch.get("id"),
                    "channel_number": ch.get("channel_number"),
                    "peak_level": ch.get("peak_level", 0.0),
                    "rms_level": ch.get("rms_level", 0.0),
                }
                for ch in channels
            ],
            "timestamp": datetime.utcnow().isoformat(),
        }

        return meter_data
    except Exception as e:
        logger.error(f"Failed to get mixer meters: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/meters/{project_id}/simulate")
async def simulate_meter_updates(project_id: str, duration: int = 10):
    """
    Simulate meter updates for testing WebSocket streaming.
    Updates meters every 100ms for the specified duration.
    """
    if not HAS_WEBSOCKET:
        return {
            "message": (
                "WebSocket streaming not available in this environment. "
                "Use GET /api/mixer/meters/{project_id} for polling instead."
            ),
            "fallback": f"/api/mixer/meters/{project_id}",
        }

    try:
        if project_id not in _mixer_states:
            raise HTTPException(status_code=404, detail="Project not found")

        state = _mixer_states[project_id]
        channels = state.get("channels", [])

        if not channels:
            return {"message": "No channels to simulate"}

        # Start background task for simulation
        asyncio.create_task(_simulate_meters(project_id, channels, duration))

        return {"message": f"Simulating meter updates for {duration} seconds"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to simulate meters: {e}")
        raise HTTPException(status_code=500, detail=str(e))


async def _simulate_meters(project_id: str, channels: list[dict], duration: int):
    """Background task to simulate meter updates."""
    import math
    import random

    start_time = time.time()
    update_count = 0

    try:
        while (time.time() - start_time) < duration:
            for channel in channels:
                # Simulate realistic meter values (sine wave with noise)
                t = time.time() * 2.0  # Speed up oscillation
                base_level = 0.3 + 0.2 * math.sin(t + channel.get("channel_number", 0))
                noise = random.uniform(-0.1, 0.1)

                peak_level = max(0.0, min(1.0, base_level + abs(noise)))
                rms_level = max(0.0, min(1.0, base_level * 0.7 + abs(noise) * 0.5))

                # Update channel in state
                channel["peak_level"] = peak_level
                channel["rms_level"] = rms_level

                # Broadcast via WebSocket
                await realtime.broadcast_meter_updates(
                    project_id=project_id,
                    channel_id=str(channel.get("id", "")),
                    meter_data={"peak_level": peak_level, "rms_level": rms_level},
                )

            update_count += 1
            await asyncio.sleep(0.1)  # 10fps updates

        logger.info(f"Simulated {update_count} meter updates for project {project_id}")
    except Exception as e:
        logger.error(f"Meter simulation error: {e}", exc_info=True)
