"""
Standardized Error Handling for VoiceStudio Backend API

Provides:
- Standardized error response format
- Request ID tracking
- Enhanced error logging
- Validation error formatting
- v3 StandardResponse envelope adapters (Phase 2: API Contract Hardening)
"""

from __future__ import annotations

import logging
import traceback
import uuid
from datetime import datetime
from typing import Any

from fastapi import HTTPException, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel

# v3 StandardResponse imports for unified error format
from backend.api.v3.models import ErrorCode as V3ErrorCode
from backend.api.v3.models import (
    ErrorDetail,
    RequestMeta,
    ResponseStatus,
    StandardResponse,
)

# Try to import structured logging and error tracking
try:
    from app.core.monitoring.error_tracking import ErrorSeverity, get_error_tracker
    from app.core.monitoring.structured_logging import get_structured_logger

    HAS_MONITORING = True
except ImportError:
    HAS_MONITORING = False

# Try to import resilience features
try:
    from app.core.resilience.circuit_breaker import CircuitBreaker, CircuitState
    from app.core.resilience.retry import RetryStrategy

    HAS_RESILIENCE = True
except ImportError:
    HAS_RESILIENCE = False

logger = logging.getLogger(__name__)


class StandardErrorResponse(BaseModel):
    """Standardized error response format."""

    error: bool = True
    error_code: str
    message: str
    request_id: str | None = None
    timestamp: str
    details: dict[str, Any] | None = None
    path: str | None = None
    recovery_suggestion: str | None = None


class ErrorCodes:
    """Standard error codes for the API."""

    # Validation errors (4xx)
    VALIDATION_ERROR = "VALIDATION_ERROR"
    INVALID_INPUT = "INVALID_INPUT"
    MISSING_REQUIRED_FIELD = "MISSING_REQUIRED_FIELD"
    INVALID_FORMAT = "INVALID_FORMAT"
    INVALID_RANGE = "INVALID_RANGE"
    INVALID_TYPE = "INVALID_TYPE"
    INVALID_ENUM_VALUE = "INVALID_ENUM_VALUE"

    # Authentication/Authorization (4xx)
    AUTHENTICATION_FAILED = "AUTHENTICATION_FAILED"
    AUTHORIZATION_FAILED = "AUTHORIZATION_FAILED"
    TOKEN_EXPIRED = "TOKEN_EXPIRED"
    TOKEN_INVALID = "TOKEN_INVALID"
    INSUFFICIENT_PERMISSIONS = "INSUFFICIENT_PERMISSIONS"

    # Resource errors (4xx)
    RESOURCE_NOT_FOUND = "RESOURCE_NOT_FOUND"
    RESOURCE_ALREADY_EXISTS = "RESOURCE_ALREADY_EXISTS"
    RESOURCE_CONFLICT = "RESOURCE_CONFLICT"
    RESOURCE_LOCKED = "RESOURCE_LOCKED"
    RESOURCE_DELETED = "RESOURCE_DELETED"

    # Request errors (4xx)
    BAD_REQUEST = "BAD_REQUEST"
    REQUEST_TOO_LARGE = "REQUEST_TOO_LARGE"
    UNSUPPORTED_MEDIA_TYPE = "UNSUPPORTED_MEDIA_TYPE"
    METHOD_NOT_ALLOWED = "METHOD_NOT_ALLOWED"

    # Rate limiting (4xx)
    RATE_LIMIT_EXCEEDED = "RATE_LIMIT_EXCEEDED"
    THROTTLE_EXCEEDED = "THROTTLE_EXCEEDED"

    # Engine-specific errors (4xx/5xx)
    ENGINE_ERROR = "ENGINE_ERROR"
    ENGINE_UNAVAILABLE = "ENGINE_UNAVAILABLE"
    ENGINE_TIMEOUT = "ENGINE_TIMEOUT"
    ENGINE_INITIALIZATION_FAILED = "ENGINE_INITIALIZATION_FAILED"
    ENGINE_PROCESSING_ERROR = "ENGINE_PROCESSING_ERROR"

    # Audio processing errors (5xx)
    AUDIO_PROCESSING_ERROR = "AUDIO_PROCESSING_ERROR"
    AUDIO_FORMAT_ERROR = "AUDIO_FORMAT_ERROR"
    AUDIO_TOO_LARGE = "AUDIO_TOO_LARGE"
    AUDIO_CORRUPTED = "AUDIO_CORRUPTED"

    # Storage errors (4xx/5xx)
    STORAGE_ERROR = "STORAGE_ERROR"
    STORAGE_LIMIT_EXCEEDED = "STORAGE_LIMIT_EXCEEDED"
    FILE_NOT_FOUND = "FILE_NOT_FOUND"
    FILE_TOO_LARGE = "FILE_TOO_LARGE"
    FILE_UPLOAD_FAILED = "FILE_UPLOAD_FAILED"

    # Server errors (5xx)
    INTERNAL_SERVER_ERROR = "INTERNAL_SERVER_ERROR"
    SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE"
    PROCESSING_ERROR = "PROCESSING_ERROR"
    TIMEOUT_ERROR = "TIMEOUT_ERROR"
    DATABASE_ERROR = "DATABASE_ERROR"
    CONFIGURATION_ERROR = "CONFIGURATION_ERROR"

    # Network errors (5xx)
    NETWORK_ERROR = "NETWORK_ERROR"
    EXTERNAL_SERVICE_ERROR = "EXTERNAL_SERVICE_ERROR"
    EXTERNAL_SERVICE_TIMEOUT = "EXTERNAL_SERVICE_TIMEOUT"


# ============================================================================
# Phase 2: v3 StandardResponse Adapters
# ============================================================================

# Error code mapping from legacy ErrorCodes to v3 ErrorCode
LEGACY_TO_V3_ERROR_CODE: dict[str, str] = {
    ErrorCodes.VALIDATION_ERROR: V3ErrorCode.INVALID_INPUT,
    ErrorCodes.INVALID_INPUT: V3ErrorCode.INVALID_INPUT,
    ErrorCodes.MISSING_REQUIRED_FIELD: V3ErrorCode.REQUIRED_FIELD,
    ErrorCodes.INVALID_FORMAT: V3ErrorCode.INVALID_FORMAT,
    ErrorCodes.INVALID_RANGE: V3ErrorCode.OUT_OF_RANGE,
    ErrorCodes.RESOURCE_NOT_FOUND: V3ErrorCode.NOT_FOUND,
    ErrorCodes.RESOURCE_ALREADY_EXISTS: V3ErrorCode.ALREADY_EXISTS,
    ErrorCodes.RESOURCE_CONFLICT: V3ErrorCode.CONFLICT,
    ErrorCodes.AUTHENTICATION_FAILED: V3ErrorCode.UNAUTHORIZED,
    ErrorCodes.AUTHORIZATION_FAILED: V3ErrorCode.FORBIDDEN,
    ErrorCodes.TOKEN_EXPIRED: V3ErrorCode.TOKEN_EXPIRED,
    ErrorCodes.RATE_LIMIT_EXCEEDED: V3ErrorCode.RATE_LIMITED,
    ErrorCodes.INTERNAL_SERVER_ERROR: V3ErrorCode.INTERNAL_ERROR,
    ErrorCodes.SERVICE_UNAVAILABLE: V3ErrorCode.SERVICE_UNAVAILABLE,
    ErrorCodes.ENGINE_ERROR: V3ErrorCode.ENGINE_ERROR,
    ErrorCodes.ENGINE_UNAVAILABLE: V3ErrorCode.ENGINE_NOT_AVAILABLE,
    ErrorCodes.TIMEOUT_ERROR: V3ErrorCode.TIMEOUT,
}


def map_legacy_error_code(legacy_code: str) -> str:
    """Map legacy error code to v3 error code format."""
    return LEGACY_TO_V3_ERROR_CODE.get(legacy_code, legacy_code)


def to_v3_error_response(
    error_code: str,
    message: str,
    request_id: str | None = None,
    correlation_id: str | None = None,
    details: dict[str, Any] | None = None,
    field: str | None = None,
    recovery_suggestion: str | None = None,
) -> StandardResponse[None]:
    """
    Convert a legacy error to v3 StandardResponse format.

    This adapter function allows gradual migration from legacy error responses
    to the v3 StandardResponse envelope format.

    Args:
        error_code: Legacy or v3 error code
        message: Human-readable error message
        request_id: Request ID for tracing
        correlation_id: Correlation ID for distributed tracing
        details: Additional error context
        field: Field that caused the error (for validation errors)
        recovery_suggestion: Suggestion for resolving the error

    Returns:
        StandardResponse in v3 format with error status
    """
    # Map legacy error code to v3 format
    v3_code = map_legacy_error_code(error_code)

    # Build error details (ErrorEnvelope: code, message, details, severity, recovery_suggestion)
    error_detail = ErrorDetail(
        code=v3_code,
        message=message,
        field=field,
        details=details,
        recovery_suggestion=recovery_suggestion,
    )

    # Build request metadata
    meta = RequestMeta(
        request_id=request_id,
        correlation_id=correlation_id,
        duration_ms=None,
    )

    return StandardResponse(
        status=ResponseStatus.ERROR,
        message=message,
        data=None,
        errors=[error_detail],
        pagination=None,
        meta=meta,
    )


def to_v3_validation_errors(
    validation_errors: list[dict[str, Any]],
    request_id: str | None = None,
) -> StandardResponse[None]:
    """
    Convert Pydantic validation errors to v3 StandardResponse format.

    Args:
        validation_errors: List of validation error dicts from RequestValidationError
        request_id: Request ID for tracing

    Returns:
        StandardResponse with validation error details
    """
    errors = []
    for err in validation_errors:
        errors.append(
            ErrorDetail(
                code=V3ErrorCode.INVALID_INPUT,
                message=err.get("message", str(err.get("msg", "Invalid value"))),
                field=err.get("field"),
                details={"type": err.get("type")} if err.get("type") else None,
            )
        )

    meta = RequestMeta(request_id=request_id, correlation_id=None, duration_ms=None)

    return StandardResponse(
        status=ResponseStatus.ERROR,
        message="Request validation failed",
        data=None,
        errors=errors,
        pagination=None,
        meta=meta,
    )


def create_v3_error_json_response(
    error_code: str,
    message: str,
    status_code: int = 400,
    request_id: str | None = None,
    details: dict[str, Any] | None = None,
    recovery_suggestion: str | None = None,
) -> JSONResponse:
    """
    Create a JSONResponse with v3 StandardResponse error format.

    This is the primary function for creating error responses in v3 format.

    Args:
        error_code: Error code (legacy or v3)
        message: Human-readable error message
        status_code: HTTP status code
        request_id: Request ID for tracing
        details: Additional error context
        recovery_suggestion: Suggestion for resolving the error

    Returns:
        JSONResponse with v3 StandardResponse body
    """
    response = to_v3_error_response(
        error_code=error_code,
        message=message,
        request_id=request_id,
        details=details,
        recovery_suggestion=recovery_suggestion,
    )

    return JSONResponse(
        status_code=status_code,
        content=response.model_dump(exclude_none=True, mode="json"),
    )


def generate_request_id() -> str:
    """Generate a unique request ID for tracking."""
    return str(uuid.uuid4())


class VoiceStudioHTTPException(HTTPException):
    """HTTPException subclass with additional VoiceStudio-specific fields."""

    def __init__(
        self,
        status_code: int,
        detail: str,
        error_code: str | None = None,
        recovery_suggestion: str | None = None,
        context: dict[str, Any] | None = None,
    ):
        super().__init__(status_code=status_code, detail=detail)
        self.error_code: str | None = error_code
        self.recovery_suggestion: str | None = recovery_suggestion
        self.context: dict[str, Any] = context or {}


def raise_standardized_error(
    error_code: str,
    message: str,
    status_code: int = 400,
    details: dict[str, Any] | None = None,
    recovery_suggestion: str | None = None,
) -> VoiceStudioHTTPException:
    """
    Raise a standardized HTTPException with error code and context.

    This is a convenience function to ensure all errors follow the standardized format.
    Use this instead of raising HTTPException directly.

    Args:
        error_code: Standard error code from ErrorCodes
        message: Human-readable error message
        status_code: HTTP status code (default: 400)
        details: Additional error context (optional)
        recovery_suggestion: Suggestion for resolving the error (optional)

    Returns:
        VoiceStudioHTTPException with standardized format

    Example:
        ```python
        from .error_handling import raise_standardized_error, ErrorCodes

        if not profile:
            raise_standardized_error(
                ErrorCodes.RESOURCE_NOT_FOUND,
                f"Profile '{profile_id}' not found",
                status_code=404,
                details={"profile_id": profile_id},
                recovery_suggestion="Please verify the profile ID exists or create a new profile."
            )
        ```
    """
    return VoiceStudioHTTPException(
        status_code=status_code,
        detail=message,
        error_code=error_code,
        recovery_suggestion=recovery_suggestion,
        context=details,
    )


def create_error_response(
    error_code: str,
    message: str,
    request_id: str | None = None,
    status_code: int = 400,
    details: dict[str, Any] | None = None,
    path: str | None = None,
    recovery_suggestion: str | None = None,
) -> JSONResponse:
    """Create a standardized error response."""
    response_data = StandardErrorResponse(
        error=True,
        error_code=error_code,
        message=message,
        request_id=request_id or generate_request_id(),
        timestamp=datetime.utcnow().isoformat(),
        details=details,
        path=path,
        recovery_suggestion=recovery_suggestion,
    )

    return JSONResponse(status_code=status_code, content=response_data.dict(exclude_none=True))


async def validation_exception_handler(
    request: Request, exc: RequestValidationError
) -> JSONResponse:
    """
    Handle Pydantic validation errors.

    Returns v3 StandardResponse format for unified error handling.
    """
    request_id = getattr(request.state, "request_id", generate_request_id())

    # Format validation errors for user-friendly display
    # Build v3 ErrorDetail objects
    error_details: list[ErrorDetail] = []
    for error in exc.errors():
        field_path = " -> ".join(str(loc) for loc in error["loc"])
        error_details.append(
            ErrorDetail(
                code=V3ErrorCode.INVALID_INPUT,
                message=error["msg"],
                field=field_path,
                details={"type": error["type"]} if error.get("type") else None,
            )
        )

    logger.warning(
        f"Validation error on {request.url.path}: {len(error_details)} errors",
        extra={"request_id": request_id},
    )

    # Build v3 StandardResponse
    meta = RequestMeta(request_id=request_id, correlation_id=None, duration_ms=None)
    response = StandardResponse[None](
        status=ResponseStatus.ERROR,
        message="Request validation failed. Please check your input.",
        data=None,
        errors=error_details,
        pagination=None,
        meta=meta,
    )

    return JSONResponse(
        status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
        content=response.model_dump(exclude_none=True, mode="json"),
    )


async def http_exception_handler(request: Request, exc: HTTPException) -> JSONResponse:
    """Handle HTTP exceptions with standardized format."""
    request_id = getattr(request.state, "request_id", generate_request_id())

    # Check if this is a VoiceStudioException with additional fields
    recovery_suggestion = None
    error_code = None
    details = None

    # Check if exception has custom attributes (VoiceStudioException)
    if hasattr(exc, "error_code"):
        error_code = exc.error_code
    if hasattr(exc, "recovery_suggestion"):
        recovery_suggestion = exc.recovery_suggestion
    if hasattr(exc, "context"):
        details = exc.context

    # Map status codes to error codes if not already set
    if not error_code:
        status_to_code = {
            400: ErrorCodes.INVALID_INPUT,
            401: ErrorCodes.AUTHENTICATION_FAILED,
            403: ErrorCodes.AUTHORIZATION_FAILED,
            404: ErrorCodes.RESOURCE_NOT_FOUND,
            409: ErrorCodes.RESOURCE_CONFLICT,
            422: ErrorCodes.VALIDATION_ERROR,
            429: ErrorCodes.RATE_LIMIT_EXCEEDED,
            500: ErrorCodes.INTERNAL_SERVER_ERROR,
            503: ErrorCodes.SERVICE_UNAVAILABLE,
            504: ErrorCodes.TIMEOUT_ERROR,
        }
        error_code = status_to_code.get(exc.status_code, ErrorCodes.INTERNAL_SERVER_ERROR)

    # Enhanced logging with structured logger if available
    if HAS_MONITORING:
        try:
            structured_logger = get_structured_logger()
            structured_logger.error(
                f"HTTP {exc.status_code} on {request.url.path}",
                status_code=exc.status_code,
                error_message=str(exc.detail),
                request_id=request_id,
                path=request.url.path,
                method=request.method,
                context=details,
            )
        except Exception:
            # Fallback to standard logging
            log_message = f"HTTP {exc.status_code} on {request.url.path}: {exc.detail}"
            log_extra = {"request_id": request_id, "status_code": exc.status_code}
            if details:
                log_extra["context"] = details
            logger.error(log_message, extra=log_extra)
    else:
        log_message = f"HTTP {exc.status_code} on {request.url.path}: {exc.detail}"
        log_extra = {"request_id": request_id, "status_code": exc.status_code}
        if details:
            log_extra["context"] = details
        logger.error(log_message, extra=log_extra)

    # Track error with error tracker if available (for non-2xx/3xx status codes)
    if HAS_MONITORING and exc.status_code >= 400:
        try:
            error_tracker = get_error_tracker()
            # Determine severity based on status code
            if exc.status_code >= 500:
                error_severity = ErrorSeverity.HIGH
            elif exc.status_code >= 400:
                error_severity = ErrorSeverity.MEDIUM
            else:
                error_severity = ErrorSeverity.LOW

            # Create a temporary exception for tracking
            temp_exc = HTTPException(status_code=exc.status_code, detail=str(exc.detail))
            error_tracker.record_error(
                temp_exc,
                severity=error_severity,
                context={
                    "request_id": request_id,
                    "path": request.url.path,
                    "method": request.method,
                    "error_code": error_code,
                    "details": details,
                },
            )
        except Exception:
            pass  # Error tracking is optional

    # Return v3 StandardResponse format
    return create_v3_error_json_response(
        error_code=error_code,
        message=str(exc.detail),
        status_code=exc.status_code,
        request_id=request_id,
        details=(
            {
                **(details or {}),
                "path": request.url.path,
            }
            if details
            else {"path": request.url.path}
        ),
        recovery_suggestion=recovery_suggestion,
    )


async def service_error_handler(request: Request, exc: Exception) -> JSONResponse:
    """
    Handle service-layer exceptions (ServiceError, PreflightError).

    GAP-007: Services raise these instead of HTTPException; this handler
    converts to v3 StandardResponse format. Keeps service layer independent of FastAPI.
    """
    status_code = getattr(exc, "status_code", 500)
    detail = getattr(exc, "detail", str(exc))
    request_id = getattr(request.state, "request_id", generate_request_id())

    # Map status code to error code
    status_to_code = {
        400: ErrorCodes.INVALID_INPUT,
        401: ErrorCodes.AUTHENTICATION_FAILED,
        403: ErrorCodes.AUTHORIZATION_FAILED,
        404: ErrorCodes.RESOURCE_NOT_FOUND,
        409: ErrorCodes.RESOURCE_CONFLICT,
        422: ErrorCodes.VALIDATION_ERROR,
        429: ErrorCodes.RATE_LIMIT_EXCEEDED,
        500: ErrorCodes.INTERNAL_SERVER_ERROR,
        503: ErrorCodes.SERVICE_UNAVAILABLE,
        504: ErrorCodes.TIMEOUT_ERROR,
    }
    error_code = status_to_code.get(status_code, ErrorCodes.INTERNAL_SERVER_ERROR)

    logger.warning(
        "Service error on %s: %s (%s)",
        request.url.path,
        detail,
        status_code,
        extra={"request_id": request_id},
    )

    return create_v3_error_json_response(
        error_code=error_code,
        message=str(detail),
        status_code=status_code,
        request_id=request_id,
        details={"path": request.url.path},
    )


async def general_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    """Handle unexpected exceptions with enhanced logging and error tracking."""
    request_id = getattr(request.state, "request_id", generate_request_id())

    # Log full traceback for debugging
    error_traceback = traceback.format_exc()
    exception_type = type(exc).__name__
    exception_message = str(exc)

    # Enhanced logging with structured logger if available
    if HAS_MONITORING:
        try:
            structured_logger = get_structured_logger()
            structured_logger.error(
                f"Unhandled exception on {request.url.path}",
                exception_type=exception_type,
                exception_message=exception_message,
                request_id=request_id,
                path=request.url.path,
                traceback=error_traceback,
            )
        except Exception:
            # Fallback to standard logging
            logger.error(
                f"Unhandled exception on {request.url.path}: {exception_message}\n{error_traceback}",
                extra={"request_id": request_id},
            )
    else:
        logger.error(
            f"Unhandled exception on {request.url.path}: {exception_message}\n{error_traceback}",
            extra={"request_id": request_id},
        )

    # Track error with error tracker if available
    if HAS_MONITORING:
        try:
            error_tracker = get_error_tracker()
            # Determine severity based on exception type
            severity = (
                ErrorSeverity.CRITICAL
                if isinstance(exc, (SystemError, MemoryError))
                else ErrorSeverity.HIGH
            )
            error_tracker.record_error(
                exc,
                severity=severity,
                context={
                    "request_id": request_id,
                    "path": request.url.path,
                    "method": request.method,
                },
            )
        except Exception:
            pass  # Error tracking is optional

    # Don't expose internal errors in production
    message = "An internal server error occurred. Please try again later."
    details: dict[str, Any] | None = None

    # In development, include more details
    import os

    if os.getenv("ENVIRONMENT", "production") == "development":
        details = {
            "exception_type": exception_type,
            "exception_message": exception_message,
            "path": request.url.path,
        }
    else:
        details = {"path": request.url.path}

    # Return v3 StandardResponse format
    return create_v3_error_json_response(
        error_code=ErrorCodes.INTERNAL_SERVER_ERROR,
        message=message,
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        request_id=request_id,
        details=details,
        recovery_suggestion="Please try again later. If the issue persists, contact support with the request ID.",
    )


# Simple in-memory error tracking for metrics
_error_counts: dict[str, int] = {}
_recent_errors: list[dict[str, Any]] = []
_MAX_RECENT_ERRORS = 100


def get_error_metrics() -> dict[str, Any]:
    """
    Get current error metrics summary.

    Returns:
        Dictionary with error counts by type and recent errors.
    """
    return {
        "total": sum(_error_counts.values()),
        "by_type": dict(_error_counts),
        "recent_count": len(_recent_errors),
    }


def _track_error(error_type: str, message: str) -> None:
    """Track an error for metrics."""
    _error_counts[error_type] = _error_counts.get(error_type, 0) + 1
    _recent_errors.append(
        {
            "type": error_type,
            "message": message[:200],  # Truncate long messages
            "timestamp": datetime.utcnow().isoformat(),
        }
    )
    # Keep only recent errors
    if len(_recent_errors) > _MAX_RECENT_ERRORS:
        _recent_errors.pop(0)


async def add_request_id_middleware(request: Request, call_next):
    """Middleware to add request ID to all requests."""
    request_id = generate_request_id()
    request.state.request_id = request_id

    try:
        # Add request ID to response headers
        response = await call_next(request)
        response.headers["X-Request-ID"] = request_id

        # Log request completion with structured logger if available
        if HAS_MONITORING:
            try:
                structured_logger = get_structured_logger()
                structured_logger.info(
                    f"{request.method} {request.url.path}",
                    request_id=request_id,
                    method=request.method,
                    path=request.url.path,
                    status_code=response.status_code,
                )
            except Exception:
                pass  # Structured logging is optional

        return response
    except Exception as exc:
        # Log exception to audit system
        try:
            from app.core.audit import get_audit_logger

            audit_logger = get_audit_logger()
            audit_logger.log_runtime_exception(
                exception=exc,
                context={
                    "request_id": request_id,
                    "path": str(request.url.path),
                    "method": request.method,
                    "task_id": request.headers.get("X-Task-ID"),
                    "subsystem": "Backend.API",
                },
            )
        except ImportError:
            pass  # Audit system is optional
        except Exception:
            pass  # Don't let audit logging break the request
        raise
