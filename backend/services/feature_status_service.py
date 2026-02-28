"""
Feature Status Service

Provides feature availability status for the health API.
Encapsulates engine checks to maintain architecture boundaries (API should not import engines directly).

GAP-ARCH-001: Routes should access engines via services, not direct imports.
"""

import logging
from typing import Any

logger = logging.getLogger(__name__)


async def _check_rvc_status() -> dict[str, Any]:
    """Check voice conversion (RVC) feature status."""
    try:
        from backend.voice.rvc.engine import RVCEngine

        engine = RVCEngine()
        await engine.load()
        is_available = engine.rvc_available()
        return {
            "status": "fully_functional" if is_available else "placeholder",
            "message": (
                "Voice conversion ready"
                if is_available
                else "RVC model not loaded - using basic processing"
            ),
            "requires_model": True,
        }
    except Exception as e:
        logger.debug(f"RVC engine check failed: {e}")
        return {
            "status": "unavailable",
            "message": f"RVC engine unavailable: {e!s}",
            "requires_model": True,
        }


async def _check_emotion_status() -> tuple[dict[str, Any], dict[str, Any]]:
    """Check emotion detection/synthesis feature status."""
    try:
        from backend.voice.emotion.engine import EmotionEngine

        engine = EmotionEngine()
        await engine.load()

        detection_available = engine.emotion_detection_available()
        synthesis_available = engine.emotion_synthesis_available()

        detection = {
            "status": "fully_functional" if detection_available else "placeholder",
            "message": (
                "Emotion detection ready"
                if detection_available
                else "Using rule-based emotion detection"
            ),
            "requires_model": True,
        }
        synthesis = {
            "status": "fully_functional" if synthesis_available else "placeholder",
            "message": (
                "Emotion synthesis ready"
                if synthesis_available
                else "Using basic DSP for emotion effects"
            ),
            "requires_model": True,
        }
        return detection, synthesis
    except Exception as e:
        logger.debug(f"Emotion engine check failed: {e}")
        error_response = {
            "status": "unavailable",
            "message": f"Emotion engine unavailable: {e!s}",
            "requires_model": True,
        }
        return error_response, error_response


async def _check_translation_status() -> dict[str, Any]:
    """Check translation feature status."""
    try:
        from backend.voice.translation.engine import TranslationEngine

        engine = TranslationEngine()
        await engine.load()
        is_available = engine.translation_available()
        return {
            "status": "fully_functional" if is_available else "placeholder",
            "message": (
                "Translation ready"
                if is_available
                else "SeamlessM4T not loaded - limited translation"
            ),
            "requires_model": True,
        }
    except Exception as e:
        logger.debug(f"Translation engine check failed: {e}")
        return {
            "status": "unavailable",
            "message": f"Translation engine unavailable: {e!s}",
            "requires_model": True,
        }


async def get_all_feature_statuses() -> dict[str, Any]:
    """
    Get status of all features.

    Returns a dictionary with feature names as keys and status info as values.
    Each status includes:
    - status: "fully_functional", "placeholder", or "unavailable"
    - message: Human-readable status message
    - requires_model: Whether the feature needs a model to be loaded
    """
    features = {}

    # Voice conversion (RVC)
    features["voice_conversion"] = await _check_rvc_status()

    # Emotion detection/synthesis
    emotion_detection, emotion_synthesis = await _check_emotion_status()
    features["emotion_detection"] = emotion_detection
    features["emotion_synthesis"] = emotion_synthesis

    # Translation
    features["translation"] = await _check_translation_status()

    # Core synthesis features (always available)
    features["voice_synthesis"] = {
        "status": "fully_functional",
        "message": "Voice synthesis ready (XTTS, Piper, etc.)",
        "requires_model": True,
    }

    features["audio_processing"] = {
        "status": "fully_functional",
        "message": "Audio processing ready",
        "requires_model": False,
    }

    features["profile_management"] = {
        "status": "fully_functional",
        "message": "Profile management ready",
        "requires_model": False,
    }

    features["timeline_editing"] = {
        "status": "fully_functional",
        "message": "Timeline editing ready",
        "requires_model": False,
    }

    # Integration features (resolved 2026-02-13)
    features["cloud_sync"] = {
        "status": "local_only",
        "message": "Local-first policy (ADR-010). Use /api/backup for project transfer.",
        "requires_model": False,
    }

    features["workflow_automation"] = {
        "status": "fully_functional",
        "message": "Workflow automation via job queue dispatch",
        "requires_model": False,
    }

    features["batch_processing"] = {
        "status": "fully_functional",
        "message": "Batch processing via job queue with progress tracking",
        "requires_model": False,
    }

    return features
