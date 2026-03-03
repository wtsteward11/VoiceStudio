# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
FastAPI Dependencies for VoiceStudio API.

Provides reusable dependency injection functions for routes.
GAP-I08: Request context dependency for correlation and tracing.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any

from fastapi import Depends, HTTPException, Request, status

from backend.api.middleware.correlation_id import get_correlation_id

logger = logging.getLogger(__name__)


@dataclass
class RequestContext:
    """
    Request context containing correlation and tracing IDs.

    GAP-I08: Provides unified context for logging and tracing.
    """

    correlation_id: str
    trace_id: str | None
    span_id: str | None

    def to_log_extra(self) -> dict[str, Any]:
        """Return dict suitable for logging extra parameter."""
        return {
            "correlation_id": self.correlation_id,
            "trace_id": self.trace_id or "N/A",
            "span_id": self.span_id or "N/A",
        }

    def __repr__(self) -> str:
        return f"RequestContext(correlation_id={self.correlation_id[:8]}...)"


def get_request_context(request: Request) -> RequestContext:
    """
    FastAPI dependency that provides request context for logging and tracing.

    GAP-I08: Enables consistent correlation across all route handlers.

    Usage:
        @router.post("/synthesize")
        async def synthesize(
            request: SynthesizeRequest,
            ctx: RequestContext = Depends(get_request_context)
        ):
            logger.info("Starting synthesis", extra=ctx.to_log_extra())

    Args:
        request: The FastAPI request object

    Returns:
        RequestContext with correlation_id, trace_id, and span_id
    """
    # Get correlation ID from context var (set by middleware)
    correlation_id = get_correlation_id() or getattr(request.state, "correlation_id", "unknown")

    # Get trace/span IDs from request state (set by tracing middleware)
    trace_id = getattr(request.state, "trace_id", None)
    span_id = getattr(request.state, "span_id", None)

    return RequestContext(
        correlation_id=str(correlation_id or ""),
        trace_id=trace_id,
        span_id=span_id,
    )


def get_correlation_id_header(request: Request) -> str:
    """
    Simple dependency to get just the correlation ID.

    Usage:
        @router.get("/status")
        async def get_status(correlation_id: str = Depends(get_correlation_id_header)):
            return {"correlation_id": correlation_id}
    """
    return str(get_correlation_id() or getattr(request.state, "correlation_id", "unknown"))


# ===== I-2: Synthesis Policy Choke Point =====

from typing import Any


def _is_unsafe_content(text: str) -> bool:
    """
    Check if text contains unsafe content.

    Fail-open: returns False (safe) when scanner unavailable.
    """
    try:
        import re

        from backend.api.routes.safety import _SAFETY_PATTERNS

        text_lower = text.lower()
        for _category, patterns in _SAFETY_PATTERNS.items():
            for pattern in patterns:
                if re.search(pattern, text_lower):
                    return True
        return False
    except Exception as exc:
        logger.debug("Safety scan unavailable, failing open: %s", exc)
        return False


def _extract_voice_id(body: dict[str, Any] | None) -> str | None:
    """Extract voice_id from request body (voice_id or profile_id as fallback)."""
    if not body or not isinstance(body, dict):
        return None
    return body.get("voice_id") or body.get("profile_id")


async def require_synthesis_clearance(request: Request) -> None:
    """
    FastAPI dependency enforcing synthesis policy (I-2).

    Checks:
    1. Rate limit (raises 429 if exceeded)
    2. Consent (raises 403 if no active consent for voice)
    3. Safety (raises 400 if text is unsafe; fail-open when scanner unavailable)

    Returns None on success.
    """
    from backend.api.rate_limiting import synthesis_rate_limiter

    # 1. Rate limit
    try:
        synthesis_rate_limiter.check_rate_limit(request)
    except HTTPException:
        raise

    # 2. Consent
    body: dict[str, Any] | None = None
    try:
        body = await request.json()
    except Exception as exc:
        logger.debug("Could not parse request body for consent check: %s", exc)
        body = None  # Fail-open: skip consent when body unparseable

    voice_id = _extract_voice_id(body)
    if voice_id:
        from backend.services.security_service import get_security_service

        consents = get_security_service().consent.get_consents(voice_id)
        has_active = any(c.is_valid for c in consents)
        if not has_active:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="No active consent for voice",
            )

    # 3. Safety (fail-open when text not present or scanner unavailable)
    text = (body or {}).get("text") if isinstance(body, dict) else None
    if text and isinstance(text, str) and _is_unsafe_content(text):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Content failed safety check",
        )
