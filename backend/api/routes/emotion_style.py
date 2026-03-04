"""
Emotion & Style Control Routes

Endpoints for controlling emotion and style in voice synthesis.
"""

from __future__ import annotations

import asyncio
import logging

from fastapi import APIRouter
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/emotion-style", tags=["emotion-style"])

# In-memory emotion/style presets (replace with database in production)
from backend.services.persistent_store import PersistentStore

_emotion_presets: PersistentStore = PersistentStore("emotion_presets")
_style_presets: PersistentStore = PersistentStore("style_presets")
_MAX_EMOTION_PRESETS = 200  # Maximum number of emotion presets
_MAX_STYLE_PRESETS = 200  # Maximum number of style presets
_emotion_preset_timestamps: PersistentStore = PersistentStore("emotion_preset_timestamps")
_style_preset_timestamps: PersistentStore = PersistentStore("style_preset_timestamps")
_state_lock = asyncio.Lock()


def _cleanup_old_presets():
    """
    Clean up old emotion/style presets from storage.

    Removes presets beyond MAX_EMOTION_PRESETS/MAX_STYLE_PRESETS (oldest first).
    """
    # Clean up emotion presets
    if len(_emotion_presets) > _MAX_EMOTION_PRESETS:
        sorted_presets = sorted(
            _emotion_preset_timestamps.items(),
            key=lambda x: x[1],
        )
        excess = len(_emotion_presets) - _MAX_EMOTION_PRESETS
        for preset_id, _ in sorted_presets[:excess]:
            _emotion_presets.pop(preset_id, None)
            _emotion_preset_timestamps.pop(preset_id, None)
        logger.info(f"Cleaned up {excess} old emotion presets from storage")

    # Clean up style presets
    if len(_style_presets) > _MAX_STYLE_PRESETS:
        sorted_presets = sorted(
            _style_preset_timestamps.items(),
            key=lambda x: x[1],
        )
        excess = len(_style_presets) - _MAX_STYLE_PRESETS
        for preset_id, _ in sorted_presets[:excess]:
            _style_presets.pop(preset_id, None)
            _style_preset_timestamps.pop(preset_id, None)
        logger.info(f"Cleaned up {excess} old style presets from storage")


class EmotionPreset(BaseModel):
    """An emotion preset."""

    id: str
    name: str
    emotion: str  # happy, sad, angry, neutral, etc.
    intensity: float  # 0.0 to 1.0
    parameters: dict[str, float] = {}
    created: str  # ISO datetime string


class StylePreset(BaseModel):
    """A style preset."""

    id: str
    name: str
    style: str  # formal, casual, narrative, etc.
    parameters: dict[str, float] = {}
    created: str  # ISO datetime string


class EmotionStyleApplyRequest(BaseModel):
    """Request to apply emotion/style to synthesis."""

    profile_id: str
    text: str
    emotion_preset_id: str | None = None
    style_preset_id: str | None = None
    emotion: str | None = None
    style: str | None = None
    intensity: float | None = None


class EmotionStyleApplyResponse(BaseModel):
    """Response from emotion/style application."""

    audio_id: str
    message: str


@router.get("/emotions", response_model=list[EmotionPreset])
async def get_emotion_presets():
    """Get all emotion presets."""
    return [
        EmotionPreset(
            id=str(p.get("id", "")),
            name=str(p.get("name", "")),
            emotion=str(p.get("emotion", "")),
            intensity=p.get("intensity", 0.5),
            parameters=p.get("parameters", {}),
            created=str(p.get("created", "")),
        )
        for p in _emotion_presets.values()
    ]


@router.get("/styles", response_model=list[StylePreset])
async def get_style_presets():
    """Get all style presets."""
    return [
        StylePreset(
            id=str(p.get("id", "")),
            name=str(p.get("name", "")),
            style=str(p.get("style", "")),
            parameters=p.get("parameters", {}),
            created=str(p.get("created", "")),
        )
        for p in _style_presets.values()
    ]


@router.post("/apply", response_model=EmotionStyleApplyResponse)
async def apply_emotion_style(request: EmotionStyleApplyRequest):
    """Apply emotion/style to voice synthesis."""
    import uuid

    # In a real implementation, this would:
    # 1. Load emotion/style presets if provided
    # 2. Apply parameters to synthesis
    # 3. Synthesize with emotion/style
    # 4. Return audio ID

    audio_id = f"audio-{uuid.uuid4().hex[:8]}"

    return EmotionStyleApplyResponse(
        audio_id=audio_id,
        message="Emotion/style applied successfully",
    )
