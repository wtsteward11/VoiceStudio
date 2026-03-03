"""Middleware configuration for the VoiceStudio API."""

from __future__ import annotations

import logging
import os
from typing import Any

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from starlette.middleware.base import BaseHTTPMiddleware

from .error_handling import add_request_id_middleware
from .versioning import get_version_from_request, get_version_headers

logger = logging.getLogger(__name__)

API_VERSION_PREFIX = "/api/v1"
LEGACY_API_PREFIX = "/api"
API_SUNSET_DATE = "2026-06-30"

# Deferred heavy imports
_PerformanceMonitoringMiddleware = None
_CompressionMiddleware = None
_get_response_cache_fn = None
_response_cache_middleware_fn = None

# Module-level state
_app_ref: FastAPI | None = None
_performance_middleware = None
_request_size_middleware = None
_rate_limit_middleware_loaded = False
_compression_middleware_loaded = False


def _lazy_import_performance_middleware():
    """Lazy import of performance monitoring middleware."""
    global _PerformanceMonitoringMiddleware
    if _PerformanceMonitoringMiddleware is None:
        from .middleware.performance_monitoring import PerformanceMonitoringMiddleware

        _PerformanceMonitoringMiddleware = PerformanceMonitoringMiddleware
    return _PerformanceMonitoringMiddleware


def _lazy_import_compression_middleware():
    """Lazy import of compression middleware."""
    global _CompressionMiddleware
    if _CompressionMiddleware is None:
        from .optimization import CompressionMiddleware

        _CompressionMiddleware = CompressionMiddleware
    return _CompressionMiddleware


def _lazy_import_response_cache():
    """Lazy import of response cache."""
    global _get_response_cache_fn, _response_cache_middleware_fn
    if _get_response_cache_fn is None:
        from .response_cache import get_response_cache, response_cache_middleware

        _get_response_cache_fn = get_response_cache
        _response_cache_middleware_fn = response_cache_middleware
    return _get_response_cache_fn, _response_cache_middleware_fn


class RequestSizeLimitMiddleware(BaseHTTPMiddleware):
    """Middleware to limit request body size."""

    def __init__(self, app, max_size_mb: float = 100.0):
        super().__init__(app)
        self.max_size_bytes = int(max_size_mb * 1024 * 1024)

    async def dispatch(self, request: Request, call_next):
        # Check Content-Length header if present
        content_length = request.headers.get("content-length")
        if content_length:
            try:
                size = int(content_length)
                if size > self.max_size_bytes:
                    logger.warning(
                        f"Request too large: {size} bytes " f"(max: {self.max_size_bytes} bytes)"
                    )
                    from fastapi import HTTPException

                    raise HTTPException(
                        status_code=413,
                        detail=(
                            f"Request body too large. "
                            f"Maximum size: "
                            f"{self.max_size_bytes / (1024*1024):.1f}MB"
                        ),
                    )
            except ValueError:
                logger.debug(
                    "Invalid content-length header '%s', letting request proceed",
                    content_length,
                )

        return await call_next(request)


def get_performance_middleware():
    """Get or lazily initialize the performance monitoring middleware."""
    global _performance_middleware
    if _performance_middleware is None and _app_ref is not None:
        PerformanceMonitoringMiddleware = _lazy_import_performance_middleware()
        _performance_middleware = PerformanceMonitoringMiddleware(_app_ref, enabled=True)
    return _performance_middleware


def _get_request_size_middleware():
    """Lazy initialization of request size middleware."""
    global _request_size_middleware
    if _request_size_middleware is None and _app_ref is not None:
        _request_size_middleware = RequestSizeLimitMiddleware(_app_ref)
    return _request_size_middleware


def lazy_import_response_cache():
    """Public accessor for response cache helpers (used by observability)."""
    return _lazy_import_response_cache()


def _initialize_rate_limiting(app: FastAPI) -> None:
    """Lazy initialization of rate limiting middleware."""
    global _rate_limit_middleware_loaded
    if _rate_limit_middleware_loaded:
        return

    # Skip rate limiting in test mode
    if os.environ.get("VOICESTUDIO_TEST_MODE", "").lower() in ("1", "true", "yes"):
        logger.info("Test mode: rate limiting disabled")
        _rate_limit_middleware_loaded = True
        return

    try:
        from .rate_limiting_enhanced import RateLimitMiddleware

        app.add_middleware(
            RateLimitMiddleware,
            skip_paths=[
                "/health",
                "/api/health",
                f"{API_VERSION_PREFIX}/health",
                "/",
                "/docs",
                f"{API_VERSION_PREFIX}/docs",
                "/openapi.json",
                f"{API_VERSION_PREFIX}/openapi.json",
            ],
        )
        logger.info("Enhanced rate limiting middleware enabled")
        _rate_limit_middleware_loaded = True
    except ImportError:
        logger.warning("Enhanced rate limiting not available, using basic rate limiting")
        # Fallback to basic rate limiting
        from .rate_limiting import rate_limit_middleware

        @app.middleware("http")
        async def basic_rate_limit_middleware(request: Request, call_next):
            return await rate_limit_middleware(request, call_next)

        _rate_limit_middleware_loaded = True


def _initialize_compression_middleware(app: FastAPI) -> None:
    """Lazy initialization of compression middleware."""
    global _compression_middleware_loaded
    if not _compression_middleware_loaded:
        CompressionMiddleware = _lazy_import_compression_middleware()
        app.add_middleware(CompressionMiddleware, min_size=1024)
        _compression_middleware_loaded = True


def setup_middleware(app: FastAPI) -> None:
    """Apply all middleware to the FastAPI application."""
    global _app_ref
    _app_ref = app

    app_config: Any = None
    try:
        from backend.settings import config

        app_config = config
    except (ImportError, AttributeError):
        pass

    # --- Inline HTTP middleware ---

    # Add performance profiling middleware (lazy initialization)
    @app.middleware("http")
    async def performance_profiling_middleware(request: Request, call_next):
        try:
            middleware = get_performance_middleware()
            if middleware is None:
                return await call_next(request)
            return await middleware.dispatch(request, call_next)
        except Exception as e:
            logger.warning(f"Performance middleware error: {e}", exc_info=True)
            raise

    # Add request size limit middleware (lazy initialization)
    @app.middleware("http")
    async def request_size_limit_middleware(request: Request, call_next):
        try:
            middleware = _get_request_size_middleware()
            if middleware is None:
                return await call_next(request)
            return await middleware.dispatch(request, call_next)
        except Exception as e:
            logger.warning(f"Request size middleware error: {e}", exc_info=True)
            raise

    # Add request ID middleware (must be first)
    @app.middleware("http")
    async def request_id_middleware(request: Request, call_next):
        return await add_request_id_middleware(request, call_next)

    # API versioning middleware with enhanced version negotiation
    @app.middleware("http")
    async def api_versioning_middleware(request: Request, call_next):
        path = request.scope.get("path", "")

        # Negotiate API version from request (path, headers, or default)
        negotiated_version = get_version_from_request(request)

        # Store negotiated version in request state for endpoint access
        request.state.api_version = negotiated_version
        request.state.api_version_warnings = []  # Warnings can be added if deprecated

        if path.startswith(API_VERSION_PREFIX):
            versioned_path = path[len(API_VERSION_PREFIX) :] or "/"
            request.scope["path"] = versioned_path
            request.scope["root_path"] = API_VERSION_PREFIX
            response = await call_next(request)
        else:
            response = await call_next(request)
            if path.startswith(LEGACY_API_PREFIX):
                response.headers["Deprecation"] = "true"
                response.headers["Sunset"] = API_SUNSET_DATE
                response.headers["Link"] = (
                    f'<{API_VERSION_PREFIX}{path[len(LEGACY_API_PREFIX):]}>; rel="alternate"'
                )

        # Add version headers to all responses
        version_headers = get_version_headers(negotiated_version)
        for header_name, header_value in version_headers.items():
            response.headers[header_name] = header_value

        return response

    # Middleware to disable service worker registration in Swagger UI
    @app.middleware("http")
    async def disable_swagger_service_worker_middleware(request: Request, call_next):
        """
        Inject JavaScript to prevent service worker registration in Swagger UI.
        This fixes the 'InvalidStateError: Failed to register a ServiceWorker' error.
        """
        response = await call_next(request)

        # Only modify responses from /docs endpoint (Swagger UI)
        if request.url.path in {"/docs", f"{API_VERSION_PREFIX}/docs"} and response.status_code == 200:
            # Check if response is HTML
            content_type = response.headers.get("content-type", "")
            if "text/html" in content_type:
                # Read the response body
                body = b""
                async for chunk in response.body_iterator:
                    body += chunk

                # Decode to string
                try:
                    html_content = body.decode("utf-8")
                except UnicodeDecodeError:
                    # If decoding fails, return original response
                    import io

                    from starlette.responses import StreamingResponse

                    return StreamingResponse(
                        io.BytesIO(body),
                        status_code=response.status_code,
                        headers=dict(response.headers),
                        media_type=content_type,
                    )

                # Inject script to disable service worker registration before closing </body> tag
                script = """
<script>
// Disable service worker registration to prevent InvalidStateError
if ('serviceWorker' in navigator) {
    // Override register method to prevent registration
    navigator.serviceWorker.register = function() {
        console.log('[Swagger UI] Service worker registration disabled to prevent errors');
        return Promise.reject(new Error('Service worker registration disabled'));
    };

    // Also unregister any existing service workers
    navigator.serviceWorker.getRegistrations().then(function(registrations) {
        for(let registration of registrations) {
            registration.unregister();
        }
    });
}
</script>
"""
                # Insert script before </body> tag
                body_tag = "</body>"
                if body_tag in html_content:
                    html_content = html_content.replace(body_tag, script + body_tag, 1)
                    # Create new response with modified content
                    from fastapi.responses import HTMLResponse

                    # Copy headers but exclude content-length since we're changing the body size
                    headers = {
                        k: v for k, v in response.headers.items() if k.lower() != "content-length"
                    }
                    return HTMLResponse(
                        content=html_content,
                        status_code=response.status_code,
                        headers=headers,
                        media_type=content_type,
                    )
                else:
                    # If no body_tag found, return original response
                    import io

                    from starlette.responses import StreamingResponse

                    return StreamingResponse(
                        io.BytesIO(body),
                        status_code=response.status_code,
                        headers=dict(response.headers),
                        media_type=content_type,
                    )

        return response

    # Add response caching middleware (after request ID, before rate limiting)
    @app.middleware("http")
    async def api_response_cache_middleware(request: Request, call_next):
        try:
            _, middleware_func = _lazy_import_response_cache()
            if middleware_func is None:
                # Fallback if import failed
                return await call_next(request)
            return await middleware_func(request, call_next)
        except Exception as e:
            logger.warning(f"Response cache middleware error: {e}")
            return await call_next(request)

    # --- Class-based middleware ---

    # Initialize rate limiting middleware
    _initialize_rate_limiting(app)

    # Add CORS middleware (essential, load immediately)
    # Configure CORS with security best practices
    # CORS Configuration
    # Security: Restrict origins. In production, set CORS_ALLOWED_ORIGINS explicitly.
    _cors_env = app_config.cors.allowed_origins if app_config else None
    if _cors_env:
        allowed_origins = [origin.strip() for origin in _cors_env.split(",")]
    elif app_config and app_config.cors.environment == "production":
        # Production without explicit origins: restrictive default
        allowed_origins = ["http://localhost:8001"]
        logger.warning("CORS_ALLOWED_ORIGINS not set in production; using restrictive default")
    else:
        # Development: allow common local origins
        allowed_origins = [
            "http://localhost:8000",
            "http://localhost:8001",
            "http://127.0.0.1:8000",
            "http://127.0.0.1:8001",
        ]

    app.add_middleware(
        CORSMiddleware,
        allow_origins=allowed_origins,
        allow_credentials=True,
        allow_methods=["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"],
        allow_headers=["*", "X-Correlation-ID", "X-API-Version"],
        expose_headers=[
            "X-Request-ID",
            "X-Correlation-ID",
            "X-RateLimit-Remaining",
            "X-RateLimit-Limit",
            "X-RateLimit-Reset",
            # API versioning headers
            "X-API-Version",
            "X-Min-Version",
            "X-Deprecated",
            "X-Sunset-Date",
            "X-API-Version-Warnings",
        ],
        max_age=3600,  # Cache preflight requests for 1 hour
    )

    # Initialize correlation ID middleware for request tracing
    try:
        from backend.api.middleware.correlation_id import (
            CorrelationIdMiddleware,
            setup_correlation_logging,
        )

        app.add_middleware(CorrelationIdMiddleware)
        setup_correlation_logging()
        logger.info("Correlation ID middleware initialized")
    except ImportError as e:
        logger.warning(f"Correlation ID middleware not available: {e}")

    # Initialize middleware before app starts (must be done before startup_event)
    # Initialize validation optimization middleware
    try:
        from .validation_middleware import setup_validation_optimization

        setup_validation_optimization(app)
        logger.info("Validation optimization initialized")
    except Exception as e:
        logger.warning(f"Failed to initialize validation optimization: {e}")

    # Initialize telemetry middleware if enabled
    if os.environ.get("VOICESTUDIO_TELEMETRY", "").lower() in ("1", "true", "yes"):
        try:
            from backend.api.middleware.telemetry_middleware import TelemetryMiddleware

            app.add_middleware(TelemetryMiddleware, enabled=True)
            logger.info("Telemetry middleware initialized")
        except ImportError as e:
            logger.debug(f"Telemetry middleware not available: {e}")

    # Add input validation middleware for security
    try:
        from backend.api.middleware.input_validation import InputValidationMiddleware

        app.add_middleware(
            InputValidationMiddleware,
            enabled=True,
            strict_mode=False,  # Enable strict_mode for SQL injection checks
            skip_paths=["/health", "/api/health", "/docs", "/openapi.json", "/redoc"],
        )
        logger.info("Input validation middleware initialized")
    except ImportError as e:
        logger.debug(f"Input validation middleware not available: {e}")

    # Add deprecation headers middleware
    try:
        from backend.api.middleware.deprecation import DeprecationMiddleware

        app.add_middleware(DeprecationMiddleware, log_deprecation_warnings=True)
        logger.info("Deprecation middleware initialized")
    except ImportError as e:
        logger.debug(f"Deprecation middleware not available: {e}")

    # Add compression middleware for large responses (lazy initialization)
    _initialize_compression_middleware(app)
