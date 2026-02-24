"""
SQLite Job Repository.

Task 2.3: SQLite implementation of JobRepository.
"""

from __future__ import annotations

import json
import logging
from typing import TYPE_CHECKING

from backend.domain.entities.job import Job
from backend.domain.repositories.job_repository import JobRepository
from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

if TYPE_CHECKING:
    pass

logger = logging.getLogger(__name__)


class SqliteJobRepository(JobRepository):
    """SQLite-backed job repository. Supports namespace for multi-tenant job state."""

    def __init__(self, db: DatabaseAdapter | None = None):
        self._db = db or get_database_adapter()

    async def get_by_id(self, job_id: str, namespace: str = "default") -> Job | None:
        row = await self._db.fetch_one(
            "SELECT data FROM jobs WHERE id = ? AND namespace = ?",
            (job_id, namespace),
        )
        if not row:
            return None
        data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
        return Job.from_dict(data)

    async def save(self, job: Job) -> Job:
        job.touch()
        data = job.to_dict()
        data_json = json.dumps(data, default=str)
        await self._db.execute(
            """
            INSERT INTO jobs (id, namespace, created_at, updated_at, data)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(id, namespace) DO UPDATE SET
                updated_at = excluded.updated_at,
                data = excluded.data
            """,
            (
                job.id,
                job.namespace,
                data["created_at"],
                data["updated_at"],
                data_json,
            ),
        )
        return job

    async def delete(self, job_id: str, namespace: str = "default") -> bool:
        result = await self._db.execute(
            "DELETE FROM jobs WHERE id = ? AND namespace = ?",
            (job_id, namespace),
        )
        return result > 0

    async def list_all(
        self,
        namespace: str = "default",
        limit: int = 100,
        offset: int = 0,
        status: str | None = None,
    ) -> list[Job]:
        query = "SELECT data FROM jobs WHERE namespace = ?"
        params: list[object] = [namespace]
        if status:
            query += " AND json_extract(data, '$.status') = ?"
            params.append(status)
        query += " ORDER BY json_extract(data, '$.created_at') DESC LIMIT ? OFFSET ?"
        params.extend([limit, offset])

        rows = await self._db.fetch_all(query, tuple(params))
        result: list[Job] = []
        for row in rows:
            data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
            result.append(Job.from_dict(data))
        return result

    async def count(self, namespace: str = "default") -> int:
        row = await self._db.fetch_one(
            "SELECT COUNT(*) as c FROM jobs WHERE namespace = ?",
            (namespace,),
        )
        return int(row["c"]) if row else 0


_job_repo: SqliteJobRepository | None = None


def get_job_repository() -> SqliteJobRepository:
    """Get or create the job repository singleton."""
    global _job_repo
    if _job_repo is None:
        _job_repo = SqliteJobRepository()
    return _job_repo
