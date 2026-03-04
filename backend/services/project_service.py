"""
Project service for routes that need project directory or store access.

Provides ensure_project_dir and get_project_store without route-to-route imports.
"""

from __future__ import annotations

import os


def get_project_store():
    """Get the project store service."""
    from backend.project.management.project_store_service import get_project_store_service

    return get_project_store_service()


def ensure_project_dir(project_id: str) -> str:
    """Ensure project directory exists and return its path."""
    store = get_project_store()
    project_dir = os.path.join(str(store.projects_dir), project_id)
    os.makedirs(project_dir, exist_ok=True)
    os.makedirs(os.path.join(project_dir, "audio"), exist_ok=True)
    return project_dir
