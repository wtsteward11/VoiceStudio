"""
Voice policy choke point (Task 1).
Single place for demo mode gate, rate limiting, and context injection.
Wired into the voice router in Task 2.
Accepts Request or WebSocket for router-level dependency compatibility.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from typing import Union

from fastapi import HTTPException, Request
from starlette.websockets import WebSocket

logger = logging.getLogger(__name__)

_SYNTHESIS_PREFIX = "/api/voice/synthesize"
_CLONE_PATH = "/api/voice/clone"


@dataclass
class VoicePolicyContext:
    demo_mode: bool = False
    user_key: str = ""


def _get_path_and_headers(conn: Union[Request, WebSocket]) -> tuple[str, dict]:
    """Extract path and headers from Request or WebSocket."""
    if isinstance(conn, Request):
        path = conn.url.path
        headers = dict(conn.headers) if conn.headers else {}
    else:
        scope = getattr(conn, "scope", {})
        path = scope.get("path", "")
        raw_headers = scope.get("headers", [])
        headers = {k.decode(): v.decode() for k, v in raw_headers} if raw_headers else {}
    return path, headers


def _get_client_host(conn: Union[Request, WebSocket]) -> str:
    """Get client host for user_key fallback."""
    if isinstance(conn, Request):
        client = getattr(conn, "client", None)
        return str(client.host) if client and hasattr(client, "host") else "anonymous"
    scope = getattr(conn, "scope", {})
    client = scope.get("client")
    if client and isinstance(client, (list, tuple)) and len(client) >= 1:
        return str(client[0])
    return "anonymous"


async def _enforce_voice_policy_impl(conn: Union[Request, WebSocket]) -> VoicePolicyContext:
    """Shared implementation for HTTP and WebSocket policy enforcement."""
    ctx = VoicePolicyContext()
    ctx.demo_mode = os.environ.get("VOICESTUDIO_DEMO_MODE", "").lower() in (
        "true",
        "1",
        "yes",
    )
    path, headers = _get_path_and_headers(conn)
    ctx.user_key = headers.get("X-API-Key", _get_client_host(conn))

    # Store on state for HTTP; WebSocket handlers typically don't use voice_policy
    if isinstance(conn, Request):
        conn.state.voice_policy = ctx

    is_synthesis = path.startswith(_SYNTHESIS_PREFIX)
    is_clone = path == _CLONE_PATH
    if is_synthesis:
        try:
            from backend.services.abuse_prevention import (
                check_synthesis_rate_limit,
                record_synthesis_attempt,
            )

            allowed, msg = check_synthesis_rate_limit(ctx.user_key)
            if not allowed:
                raise HTTPException(status_code=429, detail=msg)
            record_synthesis_attempt(ctx.user_key)
        except HTTPException:
            raise
        except Exception as e:
            logger.warning("abuse_prevention unavailable — synthesis rate limiting skipped: %s", e)
    elif is_clone:
        try:
            from backend.services.abuse_prevention import (
                check_clone_rate_limit,
                record_clone_attempt,
            )

            allowed, msg = check_clone_rate_limit(ctx.user_key)
            if not allowed:
                raise HTTPException(status_code=429, detail=msg)
            record_clone_attempt(ctx.user_key)
        except HTTPException:
            raise
        except Exception as e:
            logger.warning("abuse_prevention unavailable — rate limiting skipped: %s", e)

    return ctx


async def enforce_voice_policy(conn: Union[Request, WebSocket]) -> VoicePolicyContext:
    """Legacy: use enforce_voice_policy_http or enforce_voice_policy_ws in router dependencies."""
    return await _enforce_voice_policy_impl(conn)


async def enforce_voice_policy_http(request: Request) -> VoicePolicyContext:
    """HTTP-only policy dependency. Use in voice_http_router."""
    return await _enforce_voice_policy_impl(request)


async def enforce_voice_policy_ws(websocket: WebSocket) -> VoicePolicyContext:
    """WebSocket-only policy dependency. Use in voice_ws_router."""
    return await _enforce_voice_policy_impl(websocket)
