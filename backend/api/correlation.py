"""
Correlation ID Logging Utility — Phase 5.4.1

Provides consistent correlation ID handling for all API routes.
Extracts correlation IDs from request headers and adds them to structured logs.

Usage in routes:
    from backend.api.correlation import (
        get_correlation_id, log_with_correlation
    )

    @router.get("/endpoint")
    async def my_endpoint(request: Request):
        cid = get_correlation_id(request)
        log_with_correlation(logger, "info", "Processing", cid)
        # ... route logic
"""

from __future__ import annotations

import logging
import uuid
from collections.abc import MutableMapping
from contextvars import ContextVar
from typing import Any

from fastapi import Request

# Context variable for correlation ID in async context
_correlation_id: ContextVar[str | None] = ContextVar("correlation_id", default=None)

# Header names (matching BackendClient.cs CorrelationIdHandler)
CORRELATION_ID_HEADER = "X-Correlation-Id"
TRACE_ID_HEADER = "X-Trace-Id"
SPAN_ID_HEADER = "X-Span-Id"
TRACEPARENT_HEADER = "traceparent"

logger = logging.getLogger(__name__)


def get_correlation_id(request: Request) -> str:
    """
    Extract or generate a correlation ID from the request.

    Priority:
    1. X-Correlation-Id header
    2. X-Trace-Id header
    3. traceparent header (extract trace_id portion)
    4. Generate new UUID

    Args:
        request: The FastAPI request object.

    Returns:
        The correlation ID string.
    """
    # Check X-Correlation-Id first
    correlation_id = request.headers.get(CORRELATION_ID_HEADER)
    if correlation_id:
        _correlation_id.set(correlation_id)
        return correlation_id

    # Fall back to X-Trace-Id
    trace_id = request.headers.get(TRACE_ID_HEADER)
    if trace_id:
        _correlation_id.set(trace_id)
        return trace_id

    # Try to extract from traceparent (format: version-trace_id-span_id-flags)
    traceparent = request.headers.get(TRACEPARENT_HEADER)
    if traceparent:
        parts = traceparent.split("-")
        if len(parts) >= 2:
            trace_id = parts[1]
            _correlation_id.set(trace_id)
            return trace_id

    # Generate new correlation ID
    new_id = uuid.uuid4().hex
    _correlation_id.set(new_id)
    return new_id


def get_current_correlation_id() -> str | None:
    """
    Get the correlation ID from the current async context.

    Returns:
        The correlation ID if set, otherwise None.
    """
    return _correlation_id.get()


def set_correlation_id(correlation_id: str) -> None:
    """
    Set the correlation ID in the current async context.

    Args:
        correlation_id: The correlation ID to set.
    """
    _correlation_id.set(correlation_id)


def log_with_correlation(
    log: logging.Logger,
    level: str,
    message: str,
    correlation_id: str | None = None,
    **extra: Any,
) -> None:
    """
    Log a message with correlation ID included.

    Args:
        log: The logger instance to use.
        level: Log level (debug, info, warning, error, critical).
        message: The log message.
        correlation_id: Optional correlation ID. If not provided, uses context.
        **extra: Additional key-value pairs to include in log.
    """
    cid = correlation_id or get_current_correlation_id()

    # Build structured log data
    log_data: dict[str, Any] = {"correlation_id": cid}
    log_data.update(extra)

    # Format message with correlation context
    formatted_msg = f"[{cid or 'no-cid'}] {message}"

    # Log at appropriate level
    log_method = getattr(log, level.lower(), log.info)
    log_method(formatted_msg, extra=log_data)


def structured_log(
    log: logging.Logger,
    level: str,
    event: str,
    correlation_id: str | None = None,
    **data: Any,
) -> None:
    """
    Create a structured log entry with correlation ID.

    Args:
        log: The logger instance.
        level: Log level.
        event: Event name/type.
        correlation_id: Optional correlation ID.
        **data: Additional structured data.
    """
    cid = correlation_id or get_current_correlation_id()

    log_data = {
        "event": event,
        "correlation_id": cid,
        **data,
    }

    log_method = getattr(log, level.lower(), log.info)
    log_method(f"[{cid or 'no-cid'}] {event}", extra=log_data)


class CorrelationLoggerAdapter(logging.LoggerAdapter):
    """
    Logger adapter that automatically includes correlation ID.

    Usage:
        adapter = CorrelationLoggerAdapter(logging.getLogger(__name__))
        adapter.info("Processing")  # Includes correlation_id
    """

    def process(
        self, msg: str, kwargs: MutableMapping[str, Any]
    ) -> tuple[str, MutableMapping[str, Any]]:
        """Process log message to include correlation ID."""
        correlation_id = get_current_correlation_id()

        # Add to extra dict
        extra = kwargs.get("extra", {})
        extra["correlation_id"] = correlation_id
        kwargs["extra"] = extra

        # Prefix message with correlation ID
        prefixed_msg = f"[{correlation_id or 'no-cid'}] {msg}"
        return prefixed_msg, kwargs


def get_correlation_logger(name: str) -> CorrelationLoggerAdapter:
    """
    Get a logger adapter that automatically includes correlation ID.

    Args:
        name: The logger name (typically __name__).

    Returns:
        A CorrelationLoggerAdapter instance.
    """
    return CorrelationLoggerAdapter(logging.getLogger(name), {})


# =============================================================================
# Middleware Integration Helper
# =============================================================================


async def extract_and_set_correlation_id(request: Request) -> str:
    """
    Extract correlation ID from request and set in context.

    Call this at the start of a route handler to ensure correlation ID
    is available for all subsequent logging.

    Args:
        request: The FastAPI request.

    Returns:
        The correlation ID.
    """
    return get_correlation_id(request)


# =============================================================================
# FastAPI Dependency for Injection
# =============================================================================


async def correlation_id_dependency(request: Request) -> str:
    """
    FastAPI dependency to inject correlation ID.

    Usage:
        @router.get("/endpoint")
        async def my_endpoint(
            request: Request,
            correlation_id: str = Depends(correlation_id_dependency)
        ):
            log_with_correlation(logger, "info", "Processing", correlation_id)
    """
    return get_correlation_id(request)
