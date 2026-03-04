"""
Shared engine availability for voice, synthesis, and text_speech_editor.

Provides ENGINE_AVAILABLE and engine_router. Lives in services layer so both
routes and services can import without violating boundaries.
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)

ENGINE_AVAILABLE = False
engine_router: Any = None
_voice_engine_service: Any = None


def _ensure_engine_router() -> None:
    """Lazy initialization of engine router - called at request time."""
    global engine_router, ENGINE_AVAILABLE, _voice_engine_service

    if engine_router is not None:
        return

    try:
        from backend.ml.models.engine_service import get_engine_service

        if _voice_engine_service is None:
            _voice_engine_service = get_engine_service()

        engine_router = _voice_engine_service.get_engine_router()

        if engine_router is not None:
            engines = engine_router.list_engines()
            if not engines:
                engine_router.load_all_engines("engines")
                engines = engine_router.list_engines()

            ENGINE_AVAILABLE = len(engines) > 0
            if ENGINE_AVAILABLE:
                logger.info(f"Voice engine router initialized with {len(engines)} engines")
        else:
            ENGINE_AVAILABLE = False
            logger.warning("Engine router not available from service")
    except Exception as e:
        logger.warning(f"Failed to initialize engine router: {e}")
        ENGINE_AVAILABLE = False
