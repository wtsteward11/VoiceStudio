"""
Real-Time Voice Settings API Routes.

Task 4.1.5: Quality/latency tradeoff control for real-time processing.
Provides user-adjustable settings for real-time voice conversion.
"""

from __future__ import annotations

import logging
from enum import Enum

from fastapi import APIRouter, Depends
from pydantic import BaseModel, ConfigDict, Field

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/realtime-settings", tags=["Real-Time Settings"])


# ============================================================================
# Enums
# ============================================================================


class QualityMode(str, Enum):
    """Quality mode presets."""

    ULTRA_LOW_LATENCY = "ultra_low_latency"  # <20ms, lowest quality
    LOW_LATENCY = "low_latency"  # <50ms, good quality
    BALANCED = "balanced"  # <100ms, better quality
    HIGH_QUALITY = "high_quality"  # <200ms, best quality


class F0Method(str, Enum):
    """Pitch detection methods."""

    RMVPE = "rmvpe"
    CREPE = "crepe"
    HARVEST = "harvest"
    PM = "pm"
    DIO = "dio"


# ============================================================================
# Request/Response Models
# ============================================================================


class RealtimeSettings(BaseModel):
    """Current real-time processing settings."""

    quality_mode: QualityMode = Field(
        default=QualityMode.BALANCED,
        description="Quality/latency tradeoff preset",
    )
    chunk_size: int = Field(
        default=1024,
        ge=256,
        le=4096,
        description="Audio chunk size in samples",
    )
    buffer_size: int = Field(
        default=4,
        ge=1,
        le=16,
        description="Number of chunks to buffer",
    )
    sample_rate: int = Field(
        default=16000,
        description="Audio sample rate in Hz",
    )
    use_gpu: bool = Field(
        default=True,
        description="Use GPU acceleration if available",
    )
    half_precision: bool = Field(
        default=True,
        description="Use FP16 for faster GPU inference",
    )
    f0_method: F0Method = Field(
        default=F0Method.RMVPE,
        description="Pitch detection method",
    )
    enable_cuda_graphs: bool = Field(
        default=True,
        description="Use CUDA graphs for lower latency",
    )
    denoise_input: bool = Field(
        default=False,
        description="Apply noise reduction to input",
    )
    normalize_output: bool = Field(
        default=True,
        description="Normalize output audio levels",
    )

    model_config = ConfigDict(
        json_schema_extra={
            "example": {
                "quality_mode": "balanced",
                "chunk_size": 1024,
                "buffer_size": 4,
                "sample_rate": 16000,
                "use_gpu": True,
                "half_precision": True,
                "f0_method": "rmvpe",
                "enable_cuda_graphs": True,
                "denoise_input": False,
                "normalize_output": True,
            }
        }
    )


class QualityModeInfo(BaseModel):
    """Information about a quality mode."""

    mode: QualityMode
    name: str
    description: str
    target_latency_ms: float
    recommended_chunk_size: int
    recommended_buffer_size: int


class LatencyEstimate(BaseModel):
    """Estimated latency for current settings."""

    estimated_latency_ms: float
    breakdown: dict[str, float]
    meets_target: bool
    recommendations: list[str]


class DeviceCapabilities(BaseModel):
    """GPU/Device capabilities."""

    has_gpu: bool
    gpu_name: str | None = None
    compute_capability: str | None = None
    supports_fp16: bool = False
    supports_cuda_graphs: bool = False
    available_memory_gb: float = 0.0
    recommended_mode: QualityMode = QualityMode.BALANCED


# ============================================================================
# In-memory settings storage (replace with database in production)
# ============================================================================

_current_settings = RealtimeSettings()


def get_current_settings() -> RealtimeSettings:
    """Dependency to get current settings."""
    return _current_settings


# ============================================================================
# Routes
# ============================================================================


@router.get(
    "",
    response_model=RealtimeSettings,
    summary="Get current real-time settings",
)
async def get_settings() -> RealtimeSettings:
    """Get the current real-time processing settings."""
    return _current_settings


@router.put(
    "",
    response_model=RealtimeSettings,
    summary="Update real-time settings",
)
async def update_settings(settings: RealtimeSettings) -> RealtimeSettings:
    """
    Update real-time processing settings.

    Changes take effect immediately for new processing sessions.
    """
    global _current_settings
    _current_settings = settings
    logger.info(f"Updated real-time settings: mode={settings.quality_mode}")
    return _current_settings


@router.post(
    "/apply-mode/{mode}",
    response_model=RealtimeSettings,
    summary="Apply a quality mode preset",
)
async def apply_quality_mode(mode: QualityMode) -> RealtimeSettings:
    """
    Apply a quality mode preset.

    This updates multiple settings optimized for the selected mode.
    """
    global _current_settings

    mode_presets = {
        QualityMode.ULTRA_LOW_LATENCY: {
            "chunk_size": 256,
            "buffer_size": 2,
            "f0_method": F0Method.DIO,
            "half_precision": True,
            "enable_cuda_graphs": True,
        },
        QualityMode.LOW_LATENCY: {
            "chunk_size": 512,
            "buffer_size": 3,
            "f0_method": F0Method.RMVPE,
            "half_precision": True,
            "enable_cuda_graphs": True,
        },
        QualityMode.BALANCED: {
            "chunk_size": 1024,
            "buffer_size": 4,
            "f0_method": F0Method.RMVPE,
            "half_precision": True,
            "enable_cuda_graphs": False,
        },
        QualityMode.HIGH_QUALITY: {
            "chunk_size": 2048,
            "buffer_size": 6,
            "f0_method": F0Method.CREPE,
            "half_precision": False,
            "enable_cuda_graphs": False,
        },
    }

    preset = mode_presets.get(mode, {})

    _current_settings = RealtimeSettings(
        quality_mode=mode,
        chunk_size=int(str(preset.get("chunk_size", 1024))),
        buffer_size=int(str(preset.get("buffer_size", 4))),
        sample_rate=_current_settings.sample_rate,
        use_gpu=_current_settings.use_gpu,
        half_precision=bool(preset.get("half_precision", True)),
        f0_method=F0Method(preset.get("f0_method", F0Method.RMVPE)),
        enable_cuda_graphs=bool(preset.get("enable_cuda_graphs", False)),
        denoise_input=_current_settings.denoise_input,
        normalize_output=_current_settings.normalize_output,
    )

    logger.info(f"Applied quality mode: {mode}")
    return _current_settings


@router.get(
    "/modes",
    response_model=list[QualityModeInfo],
    summary="List available quality modes",
)
async def list_quality_modes() -> list[QualityModeInfo]:
    """Get information about all available quality modes."""
    return [
        QualityModeInfo(
            mode=QualityMode.ULTRA_LOW_LATENCY,
            name="Ultra Low Latency",
            description="Minimal latency for live performance. May sacrifice quality.",
            target_latency_ms=20.0,
            recommended_chunk_size=256,
            recommended_buffer_size=2,
        ),
        QualityModeInfo(
            mode=QualityMode.LOW_LATENCY,
            name="Low Latency",
            description="Good balance for real-time conversation.",
            target_latency_ms=50.0,
            recommended_chunk_size=512,
            recommended_buffer_size=3,
        ),
        QualityModeInfo(
            mode=QualityMode.BALANCED,
            name="Balanced",
            description="Recommended for most use cases.",
            target_latency_ms=100.0,
            recommended_chunk_size=1024,
            recommended_buffer_size=4,
        ),
        QualityModeInfo(
            mode=QualityMode.HIGH_QUALITY,
            name="High Quality",
            description="Best quality for recording or post-processing.",
            target_latency_ms=200.0,
            recommended_chunk_size=2048,
            recommended_buffer_size=6,
        ),
    ]


@router.get(
    "/latency-estimate",
    response_model=LatencyEstimate,
    summary="Estimate latency for current settings",
)
async def estimate_latency(
    settings: RealtimeSettings = Depends(get_current_settings),
) -> LatencyEstimate:
    """
    Estimate the processing latency for current settings.

    Returns a breakdown of where time is spent and recommendations.
    """
    # Calculate chunk duration
    chunk_duration_ms = (settings.chunk_size / settings.sample_rate) * 1000
    buffer_latency_ms = chunk_duration_ms * settings.buffer_size

    # Estimate processing time based on settings
    f0_times = {
        F0Method.DIO: 2.0,
        F0Method.PM: 3.0,
        F0Method.HARVEST: 15.0,
        F0Method.RMVPE: 5.0,
        F0Method.CREPE: 20.0,
    }
    f0_time = f0_times.get(settings.f0_method, 5.0)

    # GPU acceleration factor
    if settings.use_gpu:
        f0_time *= 0.3 if settings.half_precision else 0.5

    # Conversion time estimate
    conversion_time = 10.0
    if settings.use_gpu:
        conversion_time *= 0.2 if settings.enable_cuda_graphs else 0.4

    # Total estimate
    total_latency = buffer_latency_ms + f0_time + conversion_time

    # Target based on mode
    targets = {
        QualityMode.ULTRA_LOW_LATENCY: 20.0,
        QualityMode.LOW_LATENCY: 50.0,
        QualityMode.BALANCED: 100.0,
        QualityMode.HIGH_QUALITY: 200.0,
    }
    target = targets.get(settings.quality_mode, 100.0)

    # Recommendations
    recommendations = []
    if total_latency > target:
        if settings.chunk_size > 512:
            recommendations.append("Reduce chunk size for lower latency")
        if not settings.use_gpu:
            recommendations.append("Enable GPU acceleration")
        if not settings.half_precision and settings.use_gpu:
            recommendations.append("Enable half precision (FP16)")
        if settings.f0_method in [F0Method.CREPE, F0Method.HARVEST]:
            recommendations.append("Use RMVPE or DIO for faster pitch detection")

    return LatencyEstimate(
        estimated_latency_ms=round(total_latency, 1),
        breakdown={
            "buffer_latency_ms": round(buffer_latency_ms, 1),
            "f0_extraction_ms": round(f0_time, 1),
            "conversion_ms": round(conversion_time, 1),
        },
        meets_target=total_latency <= target,
        recommendations=recommendations,
    )


@router.get(
    "/device-capabilities",
    response_model=DeviceCapabilities,
    summary="Get device capabilities",
)
async def get_device_capabilities() -> DeviceCapabilities:
    """Get GPU/device capabilities for real-time processing."""
    has_gpu = False
    gpu_name = None
    compute_capability = None
    supports_fp16 = False
    supports_cuda_graphs = False
    available_memory_gb = 0.0

    try:
        import torch

        if torch.cuda.is_available():
            has_gpu = True
            props = torch.cuda.get_device_properties(0)
            gpu_name = props.name
            compute_capability = f"{props.major}.{props.minor}"
            supports_fp16 = props.major >= 7
            supports_cuda_graphs = props.major >= 7
            available_memory_gb = (props.total_memory - torch.cuda.memory_allocated()) / (1024**3)
    except ImportError:
        logger.debug("PyTorch not available for CUDA capability detection")
    except Exception as e:
        logger.warning(f"Failed to get device capabilities: {e}")

    # Determine recommended mode
    if has_gpu and supports_cuda_graphs:
        recommended = QualityMode.LOW_LATENCY
    elif has_gpu:
        recommended = QualityMode.BALANCED
    else:
        recommended = QualityMode.HIGH_QUALITY

    return DeviceCapabilities(
        has_gpu=has_gpu,
        gpu_name=gpu_name,
        compute_capability=compute_capability,
        supports_fp16=supports_fp16,
        supports_cuda_graphs=supports_cuda_graphs,
        available_memory_gb=round(available_memory_gb, 2),
        recommended_mode=recommended,
    )


@router.post(
    "/reset",
    response_model=RealtimeSettings,
    summary="Reset to default settings",
)
async def reset_settings() -> RealtimeSettings:
    """Reset all real-time settings to defaults."""
    global _current_settings
    _current_settings = RealtimeSettings()
    logger.info("Reset real-time settings to defaults")
    return _current_settings
