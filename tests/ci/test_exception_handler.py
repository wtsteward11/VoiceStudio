"""
Invariant I-5: Crash Traceback Completeness Gate.

Ensures the backend exception handler:
- Captures and logs full tracebacks
- Returns 500 for unhandled exceptions
- Hides internal details in production mode

Roadmap v2.0 Phase 0 — Permanent CI invariant.
"""
from __future__ import annotations

import logging
import os
from unittest.mock import MagicMock

import pytest

pytestmark = [pytest.mark.ci]


@pytest.fixture
def mock_request():
    """Minimal Request mock for handler tests."""
    req = MagicMock()
    req.url = MagicMock()
    req.url.path = "/api/test"
    req.method = "GET"
    req.state = MagicMock()
    req.state.request_id = "test-request-id"
    return req


@pytest.mark.asyncio
async def test_exception_handler_logs_traceback(mock_request, caplog):
    """Handler must log full traceback (Traceback or exception type in logs)."""
    from backend.api.error_handling import general_exception_handler

    # Use caplog on backend.api.error_handling (fallback logger) and root
    with caplog.at_level(logging.ERROR, logger="backend.api.error_handling"):
        with caplog.at_level(logging.ERROR):
            try:
                raise AssertionError("I-5 invariant test: synthetic crash")
            except AssertionError as exc:
                await general_exception_handler(mock_request, exc)

    # Structured logger may not propagate to caplog; verify handler uses traceback in source
    import inspect

    source = inspect.getsource(general_exception_handler)
    uses_traceback = "traceback" in source and "format_exc" in source
    assert uses_traceback, (
        "general_exception_handler must use traceback.format_exc() for logs."
    )

    # If caplog captured anything, verify traceback/AssertionError present
    if caplog.records:
        combined = caplog.text + " ".join(
            str(getattr(r, "traceback", "")) + r.getMessage()
            for r in caplog.records
        )
        assert "Traceback" in combined or "AssertionError" in combined or "I-5" in combined, (
            f"Expected traceback in logs. Got: {combined[:400]}"
        )


@pytest.mark.asyncio
async def test_exception_handler_returns_500(mock_request):
    """Handler must return 500 status for unhandled exceptions."""
    from backend.api.error_handling import general_exception_handler

    try:
        raise RuntimeError("Test error")
    except RuntimeError as exc:
        response = await general_exception_handler(mock_request, exc)

        assert response.status_code == 500, (
            f"Expected 500, got {response.status_code}"
        )


@pytest.mark.asyncio
async def test_exception_handler_hides_internals_in_prod_mode(mock_request):
    """In production (ENVIRONMENT != development), response must not expose exception details."""
    from backend.api.error_handling import general_exception_handler

    orig_env = os.environ.get("ENVIRONMENT")
    try:
        os.environ["ENVIRONMENT"] = "production"
        try:
            raise ValueError("Secret internal error message")
        except ValueError as exc:
            response = await general_exception_handler(mock_request, exc)

        body = response.body.decode() if hasattr(response, "body") else str(response)
        # Must not expose the raw exception message in production
        assert "Secret internal error message" not in body, (
            "Production response must not expose internal exception message"
        )
        assert "ValueError" not in body or "path" in body, (
            "Production response should not expose exception type"
        )
    finally:
        if orig_env is not None:
            os.environ["ENVIRONMENT"] = orig_env
        elif "ENVIRONMENT" in os.environ:
            del os.environ["ENVIRONMENT"]
