"""
Path service: single source of truth for persistent paths.

Backend Spine Migration: Routes must use PathService (or get_path) instead of
inventing paths. All paths resolve outside the repo via path_config.
"""

from __future__ import annotations

from pathlib import Path
from typing import cast

from backend.config.path_config import get_path


class PathService:
    """Thin wrapper around path_config for route use."""

    @staticmethod
    def get_audio_uploads_dir() -> Path:
        return cast(Path, get_path("audio_uploads"))

    @staticmethod
    def get_backups_dir() -> Path:
        return cast(Path, get_path("backups"))

    @staticmethod
    def get_recordings_dir() -> Path:
        return cast(Path, get_path("recordings"))

    @staticmethod
    def get_temp_dir() -> Path:
        return cast(Path, get_path("temp"))

    @staticmethod
    def get_output_dir() -> Path:
        return cast(Path, get_path("output"))

    @staticmethod
    def get_data_dir() -> Path:
        return cast(Path, get_path("data"))

    @staticmethod
    def get_artifacts_dir() -> Path:
        return cast(Path, get_path("artifacts"))

    @staticmethod
    def get_models_dir() -> Path:
        return cast(Path, get_path("models"))

    @staticmethod
    def get_profiles_dir() -> Path:
        return cast(Path, get_path("profiles"))

    @staticmethod
    def get_projects_dir() -> Path:
        return cast(Path, get_path("data")) / "projects"
