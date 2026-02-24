"""
Media context router.

Task 2.4: Aggregates image, video, lip-sync, face-swap routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Media"])


def _register() -> None:
    from backend.api.routes import (
        ai_enhancement,
        face_swap,
        image_gen,
        image_search,
        img_sampler,
        lip_sync,
        style_transfer,
        upscaling,
        video_edit,
        video_enhance,
        video_gen,
    )

    router.include_router(image_gen.router)
    router.include_router(video_gen.router)
    router.include_router(video_edit.router)
    router.include_router(video_enhance.router)
    router.include_router(lip_sync.router)
    router.include_router(face_swap.router)
    router.include_router(upscaling.router)
    router.include_router(style_transfer.router)
    router.include_router(img_sampler.router)
    router.include_router(image_search.router)
    router.include_router(ai_enhancement.router)
