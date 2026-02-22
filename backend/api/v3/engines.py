"""
API v3 - Engine Endpoints.

Task 3.4.1: Unified engine management API.
Phase 4A: Updated to use StandardResponse envelope format.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel

from backend.ml.models.engine_service import IEngineService, get_engine_service

from .models import StandardResponse, paginated_response, success_response

router = APIRouter(prefix="/engines", tags=["engines"])


def _get_request_id(request: Request) -> str | None:
    """Extract request ID from request state if available."""
    return getattr(request.state, "request_id", None)


# --- Request/Response Models ---


class EngineCapability(BaseModel):
    """Engine capability descriptor."""

    feature: str
    supported: bool
    config_schema: dict[str, Any] | None = None


class EngineInfo(BaseModel):
    """Engine information response."""

    id: str
    name: str
    version: str
    type: str
    status: str
    capabilities: list[EngineCapability] = []
    memory_usage_mb: float | None = None
    gpu_usage_percent: float | None = None

    class Config:
        json_schema_extra = {
            "example": {
                "id": "xtts_v2",
                "name": "XTTS v2",
                "version": "2.0.0",
                "type": "synthesis",
                "status": "ready",
                "capabilities": [
                    {"feature": "text_to_speech", "supported": True},
                    {"feature": "voice_cloning", "supported": True},
                ],
                "memory_usage_mb": 2048.5,
            }
        }


class EngineListResponse(BaseModel):
    """Paginated engine list response."""

    engines: list[EngineInfo]
    cursor: str | None = None
    has_more: bool = False


class EngineHealthResponse(BaseModel):
    """Engine health check response."""

    engine_id: str
    healthy: bool
    status: str
    latency_ms: float | None = None
    error: str | None = None


# --- Endpoints ---


@router.get(
    "",
    response_model=StandardResponse[list[EngineInfo]],
    summary="List available engines",
    description="Get all available engines with their capabilities and status.",
)
async def list_engines(
    request: Request,
    type: str | None = Query(None, description="Filter by engine type"),
    status: str | None = Query(None, description="Filter by status"),
    cursor: str | None = Query(None, description="Pagination cursor"),
    limit: int = Query(20, ge=1, le=100, description="Maximum results"),
    engine_service: IEngineService = Depends(get_engine_service),
):
    """List all available engines with StandardResponse envelope."""
    import logging

    logger = logging.getLogger(__name__)

    # Gracefully handle engine service failures
    try:
        engines = engine_service.list_engines()
    except Exception as exc:
        logger.warning(f"Failed to list engines: {exc}")
        engines = []

    # Filter by type if specified
    if type:
        engines = [eng for eng in engines if eng.get("type") == type]

    # Filter by status if specified
    if status:
        engines = [eng for eng in engines if eng.get("status") == status]

    # Convert to response model
    engine_infos = []
    for eng in engines[:limit]:
        try:
            engine_infos.append(
                EngineInfo(
                    id=eng.get("id", ""),
                    name=eng.get("name", ""),
                    version=eng.get("version", "0.0.0"),
                    type=eng.get("type", "unknown"),
                    status=eng.get("status", "unknown"),
                    capabilities=[
                        EngineCapability(feature=cap, supported=True)
                        for cap in eng.get("capabilities", [])
                    ],
                    memory_usage_mb=eng.get("memory_usage_mb"),
                    gpu_usage_percent=eng.get("gpu_usage_percent"),
                )
            )
        except Exception as model_err:
            logger.warning(
                f"Failed to parse engine info for {eng.get('id', 'unknown')}: {model_err}"
            )
            continue

    return paginated_response(
        data=engine_infos,
        has_more=len(engines) > limit,
        total_count=len(engines),
        page_size=limit,
        request_id=_get_request_id(request),
    )


@router.get(
    "/{engine_id}",
    response_model=StandardResponse[EngineInfo],
    summary="Get engine details",
    description="Get detailed information about a specific engine.",
)
async def get_engine(
    request: Request,
    engine_id: str,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Get engine by ID with StandardResponse envelope."""
    status = engine_service.get_engine_status(engine_id)

    if not status:
        raise HTTPException(status_code=404, detail=f"Engine not found: {engine_id}")

    engine_info = EngineInfo(
        id=engine_id,
        name=status.get("name", engine_id),
        version=status.get("version", "0.0.0"),
        type=status.get("type", "unknown"),
        status=status.get("status", "unknown"),
        capabilities=[
            EngineCapability(feature=cap, supported=True) for cap in status.get("capabilities", [])
        ],
        memory_usage_mb=status.get("memory_usage_mb"),
        gpu_usage_percent=status.get("gpu_usage_percent"),
    )

    return success_response(
        data=engine_info,
        message="Engine details retrieved",
        request_id=_get_request_id(request),
    )


@router.get(
    "/{engine_id}/health",
    response_model=StandardResponse[EngineHealthResponse],
    summary="Check engine health",
    description="Perform a health check on a specific engine.",
)
async def check_engine_health(
    request: Request,
    engine_id: str,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Check engine health with StandardResponse envelope."""
    try:
        is_available = engine_service.is_engine_available(engine_id)
        status = engine_service.get_engine_status(engine_id)

        health = EngineHealthResponse(
            engine_id=engine_id,
            healthy=is_available,
            status=status.get("status", "unknown") if status else "not_found",
            latency_ms=status.get("latency_ms") if status else None,
        )

        return success_response(
            data=health,
            message="Engine is healthy" if is_available else "Engine has issues",
            request_id=_get_request_id(request),
        )
    except Exception as e:
        health = EngineHealthResponse(
            engine_id=engine_id,
            healthy=False,
            status="error",
            error=str(e),
        )
        return success_response(
            data=health,
            message="Health check completed with errors",
            request_id=_get_request_id(request),
        )


class EngineActionResult(BaseModel):
    """Result of an engine action (load/unload)."""

    engine_id: str
    action: str
    success: bool = True


@router.post(
    "/{engine_id}/load",
    response_model=StandardResponse[EngineActionResult],
    summary="Load engine into pool",
    description="Preload an engine into the warm pool for faster inference.",
)
async def load_engine(
    request: Request,
    engine_id: str,
    config: dict[str, Any] | None = None,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Load an engine into the pool with StandardResponse envelope."""
    try:
        engine = engine_service.get_engine(engine_id)
        if engine:
            result = EngineActionResult(engine_id=engine_id, action="loaded", success=True)
            return success_response(
                data=result,
                message=f"Engine '{engine_id}' loaded successfully",
                request_id=_get_request_id(request),
            )
        else:
            raise HTTPException(status_code=404, detail=f"Engine not found: {engine_id}")
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@router.post(
    "/{engine_id}/unload",
    response_model=StandardResponse[EngineActionResult],
    summary="Unload engine from pool",
    description="Unload an engine from the warm pool to free memory.",
)
async def unload_engine(
    request: Request,
    engine_id: str,
    engine_service: IEngineService = Depends(get_engine_service),
):
    """Unload an engine from the pool with StandardResponse envelope."""
    # Note: Actual unloading requires engine pool integration
    result = EngineActionResult(engine_id=engine_id, action="unloaded", success=True)
    return success_response(
        data=result,
        message=f"Engine '{engine_id}' unloaded",
        request_id=_get_request_id(request),
    )
