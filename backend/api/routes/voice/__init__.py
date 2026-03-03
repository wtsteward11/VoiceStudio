"""Voice routes package — single source of truth for /api/voice endpoints.

Imports all submodules to register their routes on the shared router,
then re-exports `router` for use by route_registry.py.

Phase A3: replaces the 139 KB voice.py god-route.
"""
from backend.api.routes.voice._shared import router

from backend.api.routes.voice import (  # noqa: F401
    analysis,
    audio,
    cloning,
    processing,
    streaming,
    synthesis,
    testing,
)

__all__ = ["router"]
