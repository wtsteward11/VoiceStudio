"""
Project context router.

Task 2.4: Aggregates project management, tracks, timeline routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Project"])


def _register() -> None:
    from backend.api.routes import (
        library,
        markers,
        projects,
        scenes,
        templates,
        timeline,
        tracks,
        workflows,
    )

    router.include_router(projects.router)
    router.include_router(tracks.router)
    router.include_router(timeline.router)
    router.include_router(markers.router)
    router.include_router(scenes.router)
    router.include_router(library.router)
    router.include_router(templates.router)
    router.include_router(workflows.router)
