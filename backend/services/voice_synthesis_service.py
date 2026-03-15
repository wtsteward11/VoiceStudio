"""
Voice synthesis service for routes that need to synthesize.

Thin wrapper: delegates to SynthesisService (canonical synthesis logic).
No handler registration; explicit dependency on SynthesisService.
"""

from __future__ import annotations

from typing import Any


async def synthesize(request: Any, http_request: Any = None, config_service: Any = None) -> Any:
    """Synthesize via SynthesisService (canonical entry point)."""
    from backend.voice.services.synthesis_service import SynthesisService

    return await SynthesisService.synthesize(request, http_request, config_service)
