"""
Plugins context router.

Task 2.4: Aggregates plugin gallery, marketplace routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Plugins"])


def _register() -> None:
    from backend.api.routes import (
        marketplace,
        plugin_gallery,
        plugin_health,
        plugins,
    )

    router.include_router(plugins.router)
    router.include_router(plugin_gallery.router)
    router.include_router(plugin_health.router)
    router.include_router(marketplace.router)
