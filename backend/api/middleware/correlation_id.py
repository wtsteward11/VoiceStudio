# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Correlation ID Middleware for VoiceStudio API.

Adds correlation IDs to all requests for distributed tracing and debugging.
The correlation ID is:
- Accepted from X-Correlation-ID header if provided
- Generated as a new UUID if not provided
- Attached to request.state.correlation_id
- Returned in X-Correlation-ID response header
- Included in all log messages for the request

This enables end-to-end tracing from UI click to engine result.
"""
from __future__ import annotations

import logging
import uuid
from contextvars import ContextVar

from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import Response

# Context variables for correlation, trace, and span IDs (thread-safe)
# GAP-I08: Enhanced with trace and span context for distributed tracing
correlation_id_var: ContextVar[str | None] = ContextVar("correlation_id", default=None)
trace_id_var: ContextVar[str | None] = ContextVar("trace_id", default=None)
span_id_var: ContextVar[str | None] = ContextVar("span_id", default=None)

CORRELATION_ID_HEADER = "X-Correlation-ID"
TRACE_ID_HEADER = "X-Trace-ID"
SPAN_ID_HEADER = "X-Span-ID"

logger = logging.getLogger(__name__)


def get_correlation_id() -> str | None:
    """Get the current correlation ID from context."""
    return correlation_id_var.get()


def get_trace_id() -> str | None:
    """Get the current trace ID from context. GAP-I08."""
    return trace_id_var.get()


def get_span_id() -> str | None:
    """Get the current span ID from context. GAP-I08."""
    return span_id_var.get()


def set_trace_context(trace_id: str | None, span_id: str | None) -> tuple:
    """
    Set trace and span IDs in context. Returns tokens for reset.

    GAP-I08: Called by tracing middleware to propagate IDs.
    """
    trace_token = trace_id_var.set(trace_id)
    span_token = span_id_var.set(span_id)
    return trace_token, span_token


def reset_trace_context(trace_token, span_token) -> None:
    """Reset trace and span context vars. GAP-I08."""
    trace_id_var.reset(trace_token)
    span_id_var.reset(span_token)


class CorrelationIdFilter(logging.Filter):
    """
    Logging filter that adds correlation_id, trace_id, and span_id to log records.

    GAP-I08: Enhanced to include full tracing context.

    Add to logging configuration:
        filter = CorrelationIdFilter()
        handler.addFilter(filter)

    Then use in format:
        "[%(correlation_id)s] [%(trace_id)s/%(span_id)s] %(message)s"
    """

    def filter(self, record: logging.LogRecord) -> bool:
        # Always set these attributes to avoid KeyError in formatters
        if not hasattr(record, "correlation_id"):
            record.correlation_id = get_correlation_id() or "no-correlation-id"
        if not hasattr(record, "trace_id"):
            record.trace_id = get_trace_id() or "N/A"
        if not hasattr(record, "span_id"):
            record.span_id = get_span_id() or "N/A"
        return True


def ensure_correlation_attributes(record: logging.LogRecord) -> None:
    """Ensure correlation attributes exist on a log record (for test contexts)."""
    if not hasattr(record, "correlation_id"):
        record.correlation_id = "no-correlation-id"
    if not hasattr(record, "trace_id"):
        record.trace_id = "N/A"
    if not hasattr(record, "span_id"):
        record.span_id = "N/A"


# Monkey-patch logging.LogRecord factory to ensure attributes always exist
_original_log_record_factory = logging.getLogRecordFactory()


def _safe_log_record_factory(*args, **kwargs):
    record = _original_log_record_factory(*args, **kwargs)
    ensure_correlation_attributes(record)
    return record


logging.setLogRecordFactory(_safe_log_record_factory)


class CorrelationIdMiddleware(BaseHTTPMiddleware):
    """
    Middleware that manages correlation IDs for request tracing.

    Usage:
        from backend.api.middleware.correlation_id import CorrelationIdMiddleware

        app = FastAPI()
        app.add_middleware(CorrelationIdMiddleware)
    """

    async def dispatch(self, request: Request, call_next) -> Response:
        # Get correlation ID from header or generate new one
        correlation_id = request.headers.get(CORRELATION_ID_HEADER)

        if not correlation_id:
            correlation_id = str(uuid.uuid4())

        # Set in context variable (for logging)
        token = correlation_id_var.set(correlation_id)

        # Set on request state (for handlers)
        request.state.correlation_id = correlation_id

        try:
            # Log request with correlation ID (correlation_id comes from factory/filter)
            logger.info(
                "Request started",
                extra={
                    "method": request.method,
                    "path": request.url.path,
                    "client_ip": request.client.host if request.client else "unknown",
                },
            )

            # Process request
            response: Response = await call_next(request)

            # Add correlation ID to response headers
            response.headers[CORRELATION_ID_HEADER] = correlation_id

            # Log response
            logger.info(
                "Request completed",
                extra={
                    "status_code": response.status_code,
                    "method": request.method,
                    "path": request.url.path,
                },
            )

            return response

        except Exception as e:
            # Log exception (correlation_id comes from factory/filter)
            logger.error(
                f"Request failed: {e}",
                extra={
                    "method": request.method,
                    "path": request.url.path,
                    "exception_type": type(e).__name__,
                },
                exc_info=True,
            )
            raise

        finally:
            # Reset context variable
            correlation_id_var.reset(token)


def setup_correlation_logging():
    """
    Configure the root logger to include correlation IDs.

    Call this during application startup:
        from backend.api.middleware.correlation_id import setup_correlation_logging
        setup_correlation_logging()
    """
    # Add filter to root logger
    root_logger = logging.getLogger()

    # Check if filter already added
    for f in root_logger.filters:
        if isinstance(f, CorrelationIdFilter):
            return

    root_logger.addFilter(CorrelationIdFilter())

    # Update formatter to include correlation ID
    for handler in root_logger.handlers:
        if handler.formatter:
            # Prepend correlation ID to format
            current_format = handler.formatter._fmt
            if "correlation_id" not in current_format:
                new_format = "[%(correlation_id)s] " + current_format
                handler.setFormatter(logging.Formatter(new_format))
