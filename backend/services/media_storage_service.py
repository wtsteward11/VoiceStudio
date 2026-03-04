"""
Media storage for image and video generation routes.

Provides shared storage access without route-to-route imports.
"""

from __future__ import annotations

_image_storage: dict[str, str] = {}
_video_storage: dict[str, str] = {}


def get_image_storage() -> dict[str, str]:
    """Get image storage (image_id -> file_path)."""
    return _image_storage


def get_video_storage() -> dict[str, str]:
    """Get video storage (video_id -> file_path)."""
    return _video_storage
