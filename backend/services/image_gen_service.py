"""
Image generation service for routes that need to generate images.

Img_sampler calls generate_image. Image_gen route registers its handler.
"""

from __future__ import annotations

from typing import Any, Awaitable, Callable

_generate_handler: Callable[..., Awaitable[Any]] | None = None


def register_generate_image_handler(handler: Callable[..., Awaitable[Any]]) -> None:
    """Register the generate_image handler (called by image_gen route at load)."""
    global _generate_handler
    _generate_handler = handler


async def generate_image(request: Any) -> Any:
    """Generate image via registered handler."""
    if _generate_handler is None:
        raise RuntimeError(
            "Generate image handler not registered. Ensure image_gen route is loaded."
        )
    return await _generate_handler(request)
