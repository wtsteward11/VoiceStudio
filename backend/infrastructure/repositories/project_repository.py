"""
SQLite Project Repository.

Task 2.3: SQLite implementation of ProjectRepository.
"""

from __future__ import annotations

import json
import logging

from backend.domain.entities.project import Project
from backend.domain.repositories.project_repository import ProjectRepository
from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

logger = logging.getLogger(__name__)


class SqliteProjectRepository(ProjectRepository):
    """SQLite-backed project repository."""

    def __init__(self, db: DatabaseAdapter | None = None):
        self._db = db or get_database_adapter()

    async def get_by_id(self, project_id: str) -> Project | None:
        row = await self._db.fetch_one(
            "SELECT data FROM projects WHERE id = ?",
            (project_id,),
        )
        if not row:
            return None
        data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
        return Project.from_dict(data)

    async def save(self, project: Project) -> Project:
        project.touch()
        data = project.to_dict()
        data_json = json.dumps(data, default=str)
        await self._db.execute(
            """
            INSERT INTO projects (id, created_at, updated_at, data)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET updated_at = ?, data = ?
            """,
            (
                project.id,
                data["created_at"],
                data["updated_at"],
                data_json,
                data["updated_at"],
                data_json,
            ),
        )
        return project

    async def delete(self, project_id: str) -> bool:
        result = await self._db.execute(
            "DELETE FROM projects WHERE id = ?",
            (project_id,),
        )
        return result > 0

    async def list_all(
        self,
        limit: int = 100,
        offset: int = 0,
        status: str | None = None,
    ) -> list[Project]:
        query = "SELECT data FROM projects WHERE 1=1"
        params: list[object] = []
        if status:
            query += " AND json_extract(data, '$.status') = ?"
            params.append(status)
        query += " ORDER BY json_extract(data, '$.updated_at') DESC LIMIT ? OFFSET ?"
        params.extend([limit, offset])

        rows = await self._db.fetch_all(query, tuple(params))
        result: list[Project] = []
        for row in rows:
            data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
            result.append(Project.from_dict(data))
        return result

    async def count(self) -> int:
        row = await self._db.fetch_one("SELECT COUNT(*) as c FROM projects")
        return int(row["c"]) if row else 0


_project_repo: SqliteProjectRepository | None = None


def get_project_repository() -> SqliteProjectRepository:
    """Get or create the project repository singleton."""
    global _project_repo
    if _project_repo is None:
        _project_repo = SqliteProjectRepository()
    return _project_repo


def reset_project_repository_singleton() -> None:
    """Clear repository singleton (tests / process isolation)."""
    global _project_repo
    _project_repo = None
