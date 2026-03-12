"""
Domain exceptions for the service layer.

Services raise these instead of HTTPException to keep the service layer
independent of FastAPI. Routes (or a global handler) convert to HTTP responses.

GAP-007: Move HTTPException to routes.
"""

from __future__ import annotations


class ServiceError(Exception):
    """
    Service-layer exception with HTTP semantics.

    Use when a service needs to signal an error that maps to an HTTP response.
    Routes or the global exception handler convert to HTTPException/JSONResponse.
    """

    def __init__(self, status_code: int, detail: str | object):
        self.status_code = status_code
        self.detail = detail
        super().__init__(str(detail))
