"""
Canonical job lifecycle operations (SQLite job_history).

Extracted from the jobs route module so callers import services, not routes
(route boundary gate).
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Optional, cast

from backend.api.optimization import invalidate_api_response_cache
from backend.data.repositories.job_repository import JobEntity, get_job_repository
from backend.data.repositories.job_repository import JobStatus as RepoJobStatus

logger = logging.getLogger(__name__)


def _invalidate_jobs_cache() -> None:
    """Clear response cache so /api/jobs and related cached routes refresh."""
    try:
        invalidate_api_response_cache()
    except Exception as e:
        logger.debug("Jobs cache invalidation skipped: %s", e)


async def create_job(
    job_id: str,
    job_type: str,
    name: str,
    metadata: dict | None = None,
    user_id: str | None = None,
) -> JobEntity:
    """Create a new job in the database (called by routes and domain adapters)."""
    repo = get_job_repository()

    entity = JobEntity(
        id=job_id,
        job_type=job_type,
        name=name,
        status=RepoJobStatus.PENDING.value,
        progress=0.0,
        metadata="{}" if metadata is None else __import__("json").dumps(metadata),
        user_id=user_id,
        created_at=datetime.now(),
        updated_at=datetime.now(),
    )

    created = cast(JobEntity, await repo.create(entity))
    _invalidate_jobs_cache()
    return created


async def update_job_progress(
    job_id: str,
    progress: float,
    current_step: str | None = None,
    current_step_index: int | None = None,
) -> JobEntity | None:
    """Update job progress (does not invalidate full API cache — TTL covers polling)."""
    repo = get_job_repository()
    return cast(
        Optional[JobEntity],
        await repo.update_progress(job_id, progress, current_step, current_step_index),
    )


async def complete_job(
    job_id: str,
    result_path: str | None = None,
    result_id: str | None = None,
) -> JobEntity | None:
    """Mark job as completed."""
    repo = get_job_repository()
    entity = cast(
        Optional[JobEntity],
        await repo.mark_completed(job_id, result_path, result_id),
    )
    _invalidate_jobs_cache()
    return entity


async def fail_job(
    job_id: str,
    error: str,
) -> JobEntity | None:
    """Mark job as failed."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_failed(job_id, error))
    _invalidate_jobs_cache()
    return entity


async def mark_job_running(job_id: str) -> JobEntity | None:
    """Mark a pending job as running (canonical store)."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_started(job_id))
    _invalidate_jobs_cache()
    return entity


async def cancel_canonical_job(job_id: str) -> JobEntity | None:
    """Mark a job cancelled in the canonical store (used by adapters e.g. batch)."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_cancelled(job_id))
    _invalidate_jobs_cache()
    return entity


async def soft_delete_canonical_job(job_id: str) -> bool:
    """Soft-delete a job row if present (e.g. when a domain-specific job is removed)."""
    repo = get_job_repository()
    existing = await repo.get_by_id(job_id)
    if not existing:
        return False
    await repo.delete(job_id, soft=True)
    _invalidate_jobs_cache()
    return True
