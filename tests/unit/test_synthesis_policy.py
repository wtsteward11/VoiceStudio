"""
Unit tests for require_synthesis_clearance dependency (I-2).
"""
from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

pytestmark = [pytest.mark.unit]


@pytest.fixture
def mock_request():
    """Minimal Request mock for policy tests."""
    req = MagicMock()
    req.client = MagicMock()
    req.client.host = "127.0.0.1"
    req.json = AsyncMock(return_value={"voice_id": "test-voice", "text": "Hello"})
    return req


@pytest.mark.asyncio
async def test_rate_limit_exceeded_raises_429(mock_request):
    """When rate limit is exceeded, dependency raises 429."""
    from fastapi import HTTPException

    with patch(
        "backend.api.rate_limiting.synthesis_rate_limiter"
    ) as mock_limiter:
        mock_limiter.check_rate_limit.side_effect = HTTPException(
            status_code=429, detail="Rate limit exceeded"
        )
        from backend.api.dependencies import require_synthesis_clearance

        with pytest.raises(HTTPException) as exc_info:
            await require_synthesis_clearance(mock_request)

        assert exc_info.value.status_code == 429


@pytest.mark.asyncio
async def test_no_active_consent_raises_403(mock_request):
    """When no active consent exists for voice, dependency raises 403."""
    from fastapi import HTTPException

    mock_consent = MagicMock()
    mock_consent.get_consents.return_value = []  # No consents

    mock_svc = MagicMock()
    mock_svc.consent = mock_consent

    with patch(
        "backend.services.security_service.get_security_service",
        return_value=mock_svc,
    ):
        from backend.api.dependencies import require_synthesis_clearance

        with pytest.raises(HTTPException) as exc_info:
            await require_synthesis_clearance(mock_request)

        assert exc_info.value.status_code == 403
        assert "consent" in exc_info.value.detail.lower()


@pytest.mark.asyncio
async def test_active_consent_passes(mock_request):
    """When active consent exists, dependency passes (returns None)."""
    mock_record = MagicMock()
    mock_record.is_valid = True

    mock_consent = MagicMock()
    mock_consent.get_consents.return_value = [mock_record]

    mock_svc = MagicMock()
    mock_svc.consent = mock_consent

    with patch(
        "backend.services.security_service.get_security_service",
        return_value=mock_svc,
    ):
        from backend.api.dependencies import require_synthesis_clearance

        result = await require_synthesis_clearance(mock_request)

        assert result is None
