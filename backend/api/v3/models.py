"""
API v3 - Standard Response Models.

Defines the standard response envelope format for all v3 API endpoints.
This provides consistent structure for success and error responses.

Phase 4A: Quality Improvement Implementation Plan
"""

from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Any, Generic, TypeVar

from pydantic import BaseModel, Field

# Generic type variable for response data
T = TypeVar("T")


class ResponseStatus(str, Enum):
    """Standard status values for API responses."""

    SUCCESS = "success"
    ERROR = "error"
    PARTIAL = "partial"  # For batch operations with some failures


class ErrorSeverity(str, Enum):
    """Severity levels for error responses. Aligns with shared/schemas/error-envelope.schema.json."""

    INFO = "info"
    WARNING = "warning"
    ERROR = "error"
    CRITICAL = "critical"


class ErrorDetail(BaseModel):
    """
    Unified error envelope for API responses.

    Canonical format: code, message, details, severity.
    Aligns with shared/schemas/error-envelope.schema.json and ERROR_HANDLING_GUIDE.
    """

    code: str = Field(..., description="Machine-readable error code")
    message: str = Field(..., description="Human-readable error message")
    field: str | None = Field(None, description="Field that caused the error (for validation)")
    details: dict[str, Any] | None = Field(None, description="Additional error context")
    severity: ErrorSeverity | str = Field(
        default=ErrorSeverity.ERROR,
        description="Error severity (info, warning, error, critical)",
    )
    recovery_suggestion: str | None = Field(
        None,
        description="Suggested action to resolve the error",
    )


class PaginationMeta(BaseModel):
    """Cursor-based pagination metadata."""

    cursor: str | None = Field(None, description="Cursor for next page")
    has_more: bool = Field(False, description="Whether more results exist")
    total_count: int | None = Field(None, description="Total count if available")
    page_size: int = Field(20, description="Number of items per page")


class RequestMeta(BaseModel):
    """Request metadata for debugging and tracing."""

    request_id: str | None = Field(None, description="Unique request identifier")
    correlation_id: str | None = Field(None, description="Correlation ID for distributed tracing")
    timestamp: datetime = Field(default_factory=datetime.utcnow, description="Response timestamp")
    duration_ms: int | None = Field(None, description="Request processing duration in ms")


class StandardResponse(BaseModel, Generic[T]):
    """
    Standard response envelope for all v3 API endpoints.

    Provides consistent structure for:
    - Success responses with typed data
    - Error responses with detailed error information
    - Pagination metadata
    - Request tracing metadata

    Usage:
        # Success response
        return StandardResponse(
            status=ResponseStatus.SUCCESS,
            data={"result": "value"},
            message="Operation completed"
        )

        # Error response
        return StandardResponse(
            status=ResponseStatus.ERROR,
            message="Validation failed",
            errors=[ErrorDetail(code="INVALID_INPUT", message="Text is required")]
        )

        # Paginated response
        return StandardResponse(
            status=ResponseStatus.SUCCESS,
            data=items,
            pagination=PaginationMeta(cursor="abc123", has_more=True)
        )
    """

    status: ResponseStatus = Field(..., description="Response status")
    message: str | None = Field(None, description="Human-readable status message")
    data: T | None = Field(None, description="Response payload")
    errors: list[ErrorDetail] | None = Field(None, description="Error details (when status=error)")
    pagination: PaginationMeta | None = Field(
        None, description="Pagination info for list responses"
    )
    meta: RequestMeta | None = Field(None, description="Request metadata for debugging")

    class Config:
        json_schema_extra = {
            "examples": [
                {
                    "status": "success",
                    "message": "Voice profile created successfully",
                    "data": {"id": "voice_abc123", "name": "My Voice"},
                    "meta": {"request_id": "req_123", "timestamp": "2026-02-13T12:00:00Z"},
                },
                {
                    "status": "error",
                    "message": "Validation failed",
                    "errors": [
                        {"code": "REQUIRED_FIELD", "message": "Name is required", "field": "name"}
                    ],
                },
            ]
        }


# Type aliases for common response patterns
class SuccessResponse(StandardResponse[T], Generic[T]):
    """Convenience alias for successful responses."""

    status: ResponseStatus = ResponseStatus.SUCCESS


class ErrorResponse(StandardResponse[None]):
    """Convenience alias for error responses."""

    status: ResponseStatus = ResponseStatus.ERROR
    data: None = None


# Helper functions for creating responses
def success_response(
    data: T,
    message: str | None = None,
    request_id: str | None = None,
    correlation_id: str | None = None,
    duration_ms: int | None = None,
) -> StandardResponse[T]:
    """Create a standard success response."""
    meta = RequestMeta(
        request_id=request_id,
        correlation_id=correlation_id,
        duration_ms=duration_ms,
    )
    return StandardResponse(
        status=ResponseStatus.SUCCESS,
        message=message,
        data=data,
        errors=None,
        pagination=None,
        meta=meta,
    )


def error_response(
    message: str,
    errors: list[ErrorDetail] | None = None,
    request_id: str | None = None,
    correlation_id: str | None = None,
) -> StandardResponse[None]:
    """Create a standard error response."""
    meta = RequestMeta(
        request_id=request_id,
        correlation_id=correlation_id,
        duration_ms=None,
    )
    return StandardResponse(
        status=ResponseStatus.ERROR,
        message=message,
        data=None,
        errors=errors or [],
        pagination=None,
        meta=meta,
    )


def paginated_response(
    data: list[T],
    cursor: str | None = None,
    has_more: bool = False,
    total_count: int | None = None,
    page_size: int = 20,
    message: str | None = None,
    request_id: str | None = None,
) -> StandardResponse[list[T]]:
    """Create a standard paginated response."""
    pagination = PaginationMeta(
        cursor=cursor,
        has_more=has_more,
        total_count=total_count,
        page_size=page_size,
    )
    meta = RequestMeta(
        request_id=request_id,
        correlation_id=None,
        duration_ms=None,
    )
    return StandardResponse(
        status=ResponseStatus.SUCCESS,
        message=message,
        data=data,
        errors=None,
        pagination=pagination,
        meta=meta,
    )


# Standard error codes
class ErrorCode:
    """Standard error codes for v3 API."""

    # Validation errors (4xx)
    INVALID_INPUT = "INVALID_INPUT"
    REQUIRED_FIELD = "REQUIRED_FIELD"
    INVALID_FORMAT = "INVALID_FORMAT"
    OUT_OF_RANGE = "OUT_OF_RANGE"

    # Resource errors (4xx)
    NOT_FOUND = "NOT_FOUND"
    ALREADY_EXISTS = "ALREADY_EXISTS"
    CONFLICT = "CONFLICT"

    # Authentication/Authorization errors (4xx)
    UNAUTHORIZED = "UNAUTHORIZED"
    FORBIDDEN = "FORBIDDEN"
    TOKEN_EXPIRED = "TOKEN_EXPIRED"

    # Rate limiting (4xx)
    RATE_LIMITED = "RATE_LIMITED"
    QUOTA_EXCEEDED = "QUOTA_EXCEEDED"

    # Server errors (5xx)
    INTERNAL_ERROR = "INTERNAL_ERROR"
    SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE"
    ENGINE_ERROR = "ENGINE_ERROR"
    TIMEOUT = "TIMEOUT"

    # Engine-specific errors
    ENGINE_NOT_AVAILABLE = "ENGINE_NOT_AVAILABLE"
    MODEL_NOT_LOADED = "MODEL_NOT_LOADED"
    SYNTHESIS_FAILED = "SYNTHESIS_FAILED"
    CLONING_FAILED = "CLONING_FAILED"
