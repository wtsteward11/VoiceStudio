"""
Compatibility re-export — canonical implementation: `backend.project.management.project_store_service`.
"""

from backend.project.management.project_store_service import (
    CURRENT_PROJECT_SCHEMA_VERSION,
    ENV_PROJECTS_DIR,
    PROJECT_META_FILENAME,
    ProjectRecord,
    ProjectStoreService,
    UnsupportedProjectPayloadError,
    get_project_store_service,
    reset_project_store_service,
)

__all__ = [
    "CURRENT_PROJECT_SCHEMA_VERSION",
    "ENV_PROJECTS_DIR",
    "PROJECT_META_FILENAME",
    "ProjectRecord",
    "ProjectStoreService",
    "UnsupportedProjectPayloadError",
    "get_project_store_service",
    "reset_project_store_service",
]
