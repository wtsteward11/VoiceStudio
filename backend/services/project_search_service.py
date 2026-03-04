"""
Project search service for global search.

Provides dict-like project access for search without route-to-route imports.
"""

from __future__ import annotations

from typing import Any


def get_projects_for_search() -> dict[str, Any]:
    """
    Get projects as a dict-like structure for search iteration.

    Returns {project_id: {name, description}} for search.
    """
    from backend.project.management.project_store_service import get_project_store_service

    store = get_project_store_service()
    result: dict[str, Any] = {}
    for record in store.list_projects():
        result[record.id] = {
            "name": record.name or "",
            "description": record.description or "",
        }
    return result
