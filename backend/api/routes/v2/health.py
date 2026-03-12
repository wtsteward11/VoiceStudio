"""
API v2 Health Check Routes

Provides enhanced health checking with versioning support.
"""
# SAFETY: FastAPI router decorators lack complete type stubs.
# Per STRICT_MYPY_BURNDOWN_SUBPLAN; runtime behavior is correct.
# mypy: disable-error-code="untyped-decorator"

import logging
from datetime import datetime
from typing import Any

from fastapi import APIRouter, Response

from backend.api.versioning import VERSION_HEADER, APIVersion

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/v2/health", tags=["health", "v2"])


@router.get("/")
async def health_v2(response: Response) -> dict[str, Any]:
    """
    Health check endpoint (v2).

    Returns extended health information including version details.
    """
    response.headers[VERSION_HEADER] = APIVersion.V2.value

    return {
        "status": "healthy",
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "version": {
            "api": APIVersion.V2.value,
            "current": APIVersion.current().value,
        },
    }


@router.get("/detailed")
async def health_detailed_v2(response: Response) -> dict[str, Any]:
    """
    Detailed health check (v2).

    Returns comprehensive system health information.
    """
    response.headers[VERSION_HEADER] = APIVersion.V2.value

    return {
        "status": "healthy",
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "version": {
            "api": APIVersion.V2.value,
            "supported": [v.value for v in APIVersion.supported()],
        },
        "components": {
            "api": "healthy",
            # Add more component checks as needed
        },
    }
