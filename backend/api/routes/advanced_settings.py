"""
Advanced Settings API Routes

Endpoints for comprehensive application settings and advanced configuration.
"""

from __future__ import annotations

import json
import logging
from pathlib import Path

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/advanced-settings", tags=["advanced-settings"])

# Settings file path (use path_config to avoid repo writes)
from backend.config.path_config import get_path

SETTINGS_FILE = get_path("data") / "advanced_settings.json"

# Cache for settings
_settings_cache: dict | None = None
_cache_timestamp: float = 0.0
_cache_ttl: float = 60.0


class UISettings(BaseModel):
    """UI customization settings."""

    theme: str = "Dark"
    accent_color: str = "#0078D4"
    font_size: str = "Medium"  # Small, Medium, Large
    ui_scale: float = 1.0  # 0.75 to 2.0
    animation_enabled: bool = True
    transparency_enabled: bool = False
    compact_mode: bool = False


class PerformanceSettings(BaseModel):
    """Performance optimization settings."""

    cache_enabled: bool = True
    cache_size_mb: int = 512
    max_threads: int = 4
    gpu_enabled: bool = True
    gpu_device: str | None = None
    memory_limit_mb: int | None = None
    background_processing: bool = True
    preload_engines: bool = False


class AudioProcessingSettings(BaseModel):
    """Advanced audio processing settings."""

    default_sample_rate: int = 44100
    default_bit_depth: int = 16
    dither_enabled: bool = True
    normalization_enabled: bool = False
    auto_fade_in: bool = True
    auto_fade_out: bool = True
    fade_duration_ms: int = 10
    resampling_quality: str = "High"  # Low, Medium, High


class EngineAdvancedSettings(BaseModel):
    """Advanced engine configuration."""

    auto_fallback: bool = True
    timeout_seconds: int = 300
    retry_attempts: int = 3
    batch_size: int = 1
    enable_quality_enhancement: bool = True
    quality_threshold: float = 0.7
    model_cache_enabled: bool = True


class SystemIntegrationSettings(BaseModel):
    """System integration settings."""

    file_associations: dict[str, bool] = {}
    context_menu_enabled: bool = False
    auto_start: bool = False
    minimize_to_tray: bool = False
    check_for_updates: bool = True
    update_channel: str = "Stable"  # Stable, Beta, Dev


class AdvancedSettingsData(BaseModel):
    """Complete advanced settings data."""

    ui: UISettings = UISettings()
    performance: PerformanceSettings = PerformanceSettings()
    audio_processing: AudioProcessingSettings = AudioProcessingSettings()
    engine: EngineAdvancedSettings = EngineAdvancedSettings()
    system: SystemIntegrationSettings = SystemIntegrationSettings()


@router.get("", response_model=AdvancedSettingsData)
@cache_response(ttl=60)  # Cache for 60 seconds (settings change infrequently)
async def get_advanced_settings():
    """Get all advanced settings."""
    try:
        # Check cache
        import time

        current_time = time.time()
        if _settings_cache is not None and (current_time - _cache_timestamp) < _cache_ttl:
            return AdvancedSettingsData(**_settings_cache)

        # Load from file
        if SETTINGS_FILE.exists():
            with open(SETTINGS_FILE, encoding="utf-8") as f:
                data = json.load(f)
                _settings_cache = data
                _cache_timestamp = current_time
                return AdvancedSettingsData(**data)

        # Return defaults
        defaults = AdvancedSettingsData()
        _settings_cache = defaults.model_dump()
        _cache_timestamp = current_time
        return defaults
    except Exception as e:
        logger.error(f"Failed to load advanced settings: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to load settings: {e!s}",
        ) from e


@router.put("", response_model=AdvancedSettingsData)
async def update_advanced_settings(
    settings: AdvancedSettingsData,
):
    """Update advanced settings atomically (tmp + replace)."""
    import os
    import time

    tmp_path = None
    try:
        # Ensure directory exists
        SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)

        # Save to file atomically
        tmp_path = SETTINGS_FILE.with_suffix(SETTINGS_FILE.suffix + ".tmp")
        with open(tmp_path, "w", encoding="utf-8") as f:
            json.dump(settings.model_dump(), f, indent=2)
        os.replace(tmp_path, SETTINGS_FILE)

        # Update cache
        global _settings_cache, _cache_timestamp
        _settings_cache = settings.model_dump()
        _cache_timestamp = time.time()

        logger.info("Advanced settings updated")
        return settings
    except Exception as e:
        logger.error(f"Failed to save advanced settings: {e}")
        if tmp_path and tmp_path.exists():
            try:
                tmp_path.unlink()
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except Exception:
                pass
        raise HTTPException(
            status_code=500,
            detail=f"Failed to save settings: {e!s}",
        ) from e


@router.post("/reset")
async def reset_advanced_settings():
    """Reset all advanced settings to defaults."""
    try:
        if SETTINGS_FILE.exists():
            SETTINGS_FILE.unlink()

        global _settings_cache, _cache_timestamp
        _settings_cache = None
        _cache_timestamp = 0.0

        logger.info("Advanced settings reset to defaults")
        return {"success": True, "message": "Settings reset to defaults"}
    except Exception as e:
        logger.error(f"Failed to reset advanced settings: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to reset settings: {e!s}",
        ) from e


@router.get("/category/{category}")
@cache_response(ttl=60)  # Cache for 60 seconds (settings change infrequently)
async def get_settings_category(category: str):
    """Get settings for a specific category."""
    try:
        settings = await get_advanced_settings()
        category_map = {
            "ui": settings.ui,
            "performance": settings.performance,
            "audio_processing": settings.audio_processing,
            "engine": settings.engine,
            "system": settings.system,
        }

        if category not in category_map:
            raise HTTPException(status_code=404, detail=f"Category '{category}' not found")

        return category_map[category]
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get category settings: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get category: {e!s}",
        ) from e
