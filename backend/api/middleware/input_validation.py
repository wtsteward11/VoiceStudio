"""
Input Validation Security Middleware

Provides additional security-focused input validation beyond Pydantic
validation. Protects against common injection attacks, path traversal, and
malicious input.
"""

from __future__ import annotations

import logging
import re
import unicodedata
from typing import Any
from urllib.parse import unquote

from fastapi import HTTPException, Request, status
from starlette.middleware.base import BaseHTTPMiddleware

logger = logging.getLogger(__name__)

# Patterns for potentially malicious input
PATH_TRAVERSAL_PATTERNS = [
    r"\.\./",  # Path traversal
    r"\.\.\\",  # Windows path traversal
    r"\.\.",  # General path traversal
    r"//",  # Double slash (can bypass some filters)
    r"\\\\",  # Windows UNC path
]

INJECTION_PATTERNS = [
    r"[;&|`$]",  # Command injection characters
    r"<script",  # XSS attempts
    r"javascript:",  # JavaScript protocol
    r"on\w+\s*=",  # Event handlers (onclick, onerror, etc.)
    r"data:text/html",  # Data URI with HTML
    r"vbscript:",  # VBScript protocol
]

SQL_INJECTION_PATTERNS = [
    r"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE)\b)",
    r"(--|#|/\*|\*/)",  # SQL comments
    r"(\b(UNION|OR|AND)\b.*\b(SELECT|INSERT|UPDATE|DELETE)\b)",
]

MAX_STRING_LENGTH = 10000  # Maximum string length for input fields
MAX_PATH_LENGTH = 260  # Windows MAX_PATH limit

_FORBIDDEN_UNICODE_CATEGORIES = frozenset({"Cf", "Cc", "Cs", "Zl", "Zp"})


def _path_contains_unicode_control(path: str) -> bool:
    """Return True if *path* contains any Unicode control/format/surrogate codepoint."""
    return any(unicodedata.category(ch) in _FORBIDDEN_UNICODE_CATEGORIES for ch in path)


class InputValidationMiddleware(BaseHTTPMiddleware):
    """
    Middleware for security-focused input validation.

    Validates request parameters, query strings, and path parameters
    for potentially malicious content.
    """

    def __init__(
        self,
        app,
        enabled: bool = True,
        strict_mode: bool = False,
        skip_paths: list[str] | None = None,
    ):
        """
        Initialize input validation middleware.

        Args:
            app: FastAPI application
            enabled: Enable input validation
            strict_mode: Enable strict validation (more aggressive)
            skip_paths: Paths to skip validation
        """
        super().__init__(app)
        self.enabled = enabled
        self.strict_mode = strict_mode
        self.skip_paths = skip_paths or [
            "/health",
            "/api/health",
            "/docs",
            "/openapi.json",
            "/redoc",
        ]

    def _check_path_traversal(self, value: str) -> bool:
        """Check for path traversal attempts."""
        if not isinstance(value, str):
            return False

        value_lower = value.lower()
        for pattern in PATH_TRAVERSAL_PATTERNS:
            if re.search(pattern, value_lower, re.IGNORECASE):
                return True
        return False

    def _check_injection(self, value: str) -> bool:
        """Check for injection attempts."""
        if not isinstance(value, str):
            return False

        value_lower = value.lower()
        return any(re.search(pattern, value_lower, re.IGNORECASE) for pattern in INJECTION_PATTERNS)

    def _check_sql_injection(self, value: str) -> bool:
        """Check for SQL injection attempts."""
        if not isinstance(value, str):
            return False

        value_upper = value.upper()
        for pattern in SQL_INJECTION_PATTERNS:
            if re.search(pattern, value_upper, re.IGNORECASE):
                return True
        return False

    def _check_length(self, value: str, max_length: int = MAX_STRING_LENGTH) -> bool:
        """Check if string exceeds maximum length."""
        if not isinstance(value, str):
            return False
        return len(value) > max_length

    def _validate_string(self, value: Any, field_name: str = "field") -> None:
        """
        Validate a string value for security issues.

        Raises:
            HTTPException: If validation fails
        """
        if not isinstance(value, str):
            return

        # Check length
        if self._check_length(value):
            logger.warning(f"Input validation failed: {field_name} exceeds max length")
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=f"Input field '{field_name}' exceeds maximum length",
            )

        # Check path traversal
        if self._check_path_traversal(value):
            logger.warning(f"Path traversal attempt in {field_name}")
            detail = f"Invalid input in field '{field_name}': " "Path traversal detected"
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=detail,
            )

        # Check injection patterns
        if self._check_injection(value):
            logger.warning(f"Injection attempt in {field_name}")
            detail = (
                f"Invalid input in field '{field_name}': " "Potentially malicious content detected"
            )
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=detail,
            )

        # Check SQL injection (strict mode or specific fields)
        if self.strict_mode or field_name.lower() in ["query", "sql", "filter"]:
            if self._check_sql_injection(value):
                logger.warning(f"SQL injection attempt in {field_name}")
                detail = f"Invalid input in field '{field_name}': " "SQL injection attempt detected"
                raise HTTPException(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    detail=detail,
                )

    def _validate_dict(self, data: dict[str, Any], prefix: str = "") -> None:
        """Recursively validate dictionary values."""
        for key, value in data.items():
            field_name = f"{prefix}.{key}" if prefix else key

            if isinstance(value, str):
                self._validate_string(value, field_name)
            elif isinstance(value, dict):
                self._validate_dict(value, field_name)
            elif isinstance(value, list):
                for i, item in enumerate(value):
                    item_name = f"{field_name}[{i}]"
                    if isinstance(item, str):
                        self._validate_string(item, item_name)
                    elif isinstance(item, dict):
                        self._validate_dict(item, item_name)

    def _validate_query_params(self, request: Request) -> None:
        """Validate query parameters."""
        for key, value in request.query_params.items():
            # Decode URL-encoded values
            try:
                decoded_value = unquote(str(value))
                self._validate_string(decoded_value, f"query.{key}")
            except Exception as e:
                logger.debug(f"Error validating query param {key}: {e}")

    def _validate_path_params(self, request: Request) -> None:
        """Validate path parameters."""
        for key, value in request.path_params.items():
            if isinstance(value, str):
                # Decode URL-encoded values
                try:
                    decoded_value = unquote(value)
                    self._validate_string(decoded_value, f"path.{key}")
                except Exception as e:
                    logger.debug(f"Error validating path param {key}: {e}")

    async def dispatch(self, request: Request, call_next):
        """Process request with input validation."""
        path = request.url.path
        if _path_contains_unicode_control(path):
            from fastapi.responses import JSONResponse

            logger.warning(
                "Rejected request with Unicode control chars in path: %r",
                path.encode("unicode_escape").decode("ascii"),
            )
            return JSONResponse(
                status_code=400,
                content={"error": "INVALID_PATH", "message": "Path contains invalid characters."},
            )

        # Skip validation for certain paths
        if not self.enabled or request.url.path in self.skip_paths:
            return await call_next(request)

        try:
            # Validate query parameters
            self._validate_query_params(request)

            # Validate path parameters
            self._validate_path_params(request)

            # Validate request body for POST/PUT/PATCH
            if request.method in ["POST", "PUT", "PATCH"]:
                # Note: Body validation happens after Pydantic validation
                # This is a secondary security check
                # We don't read the body here to avoid consuming it
                # Body validation should be done in route handlers if needed
                ...

        except HTTPException:
            # Re-raise HTTP exceptions (validation failures)
            raise
        except Exception as e:
            # Log unexpected errors but don't block the request
            logger.error(f"Input validation error: {e}", exc_info=True)
            # In production, you might want to block on unexpected errors
            # For now, we'll allow the request to proceed

        # Process request
        response = await call_next(request)

        return response


def setup_input_validation(app, enabled: bool = True, strict_mode: bool = False):
    """
    Setup input validation middleware.

    Args:
        app: FastAPI application
        enabled: Enable input validation
        strict_mode: Enable strict validation
    """
    app.add_middleware(
        InputValidationMiddleware,
        enabled=enabled,
        strict_mode=strict_mode,
    )
    logger.info(f"Input validation middleware enabled (strict_mode={strict_mode})")
