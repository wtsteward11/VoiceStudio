"""
Voice policy choke point (Task 1).
Single place for demo mode gate, rate limiting, and context injection.
Wired into the voice router in Task 2.
"""
from __future__ import annotations

import logging
import os
from dataclasses import dataclass

from fastapi import HTTPException, Request

logger = logging.getLogger(__name__)

# All paths that produce generated audio — rate limited
_SYNTHESIS_PREFIX = "/api/voice/synthesize"
_CLONE_PATH = "/api/voice/clone"


@dataclass
class VoicePolicyContext:
    demo_mode: bool = False
    user_key: str = ""


async def enforce_voice_policy(request: Request) -> VoicePolicyContext:
    ctx = VoicePolicyContext()
    ctx.demo_mode = os.environ.get("VOICESTUDIO_DEMO_MODE", "").lower() in (
        "true",
        "1",
        "yes",
    )
    client = getattr(request, "client", None)
    ctx.user_key = request.headers.get(
        "X-API-Key", str(client.host) if client else "anonymous"
    )

    path = request.url.path
    is_rate_limited = path.startswith(_SYNTHESIS_PREFIX) or path == _CLONE_PATH
    if is_rate_limited:
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
            logger.warning(
                "abuse_prevention unavailable — rate limiting skipped: %s", e
            )

    request.state.voice_policy = ctx
    return ctx
