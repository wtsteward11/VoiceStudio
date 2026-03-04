"""
M4: Engine runtime service wrapper.

Provides facade for engine runtime / subprocess operations.
Routes import from backend.services.runtime_service instead of app.core directly.
"""

from __future__ import annotations

from typing import Any


def run_engine(engine_id: str, **kwargs: Any) -> Any:
    """Run an engine with given kwargs. Delegates to engine router."""
    from backend.ml.models.engine_service import get_engine_service

    svc = get_engine_service()
    if not svc:
        return None
    router = svc.get_engine_router()
    if not router:
        return None
    engine = router.get_engine(engine_id)
    if not engine or not hasattr(engine, "synthesize"):
        return None
    return engine.synthesize(**kwargs)


def get_engine_status(engine_id: str | None = None) -> dict[str, Any]:
    """Get engine runtime status. Delegates to engine service."""
    from backend.ml.models.engine_service import get_engine_service

    svc = get_engine_service()
    if not svc:
        return {"available": False, "engines": []}
    router = svc.get_engine_router()
    if not router:
        return {"available": False, "engines": []}
    engines = router.list_engines()
    return {"available": len(engines) > 0, "engines": engines}
