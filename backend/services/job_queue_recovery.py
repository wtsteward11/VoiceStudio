"""
Startup reconciliation for durable job_history rows.

After backend restart, in-flight jobs cannot be trusted as still executing.
Non-terminal rows in `running` or `paused` are marked `failed` with an explicit
recovery reason so API/UI truth matches SQLite authority.

GOV-VOICESTUDIO-DURABLE-JOB-QUEUE-01
"""

from __future__ import annotations

import logging
from typing import Any

from backend.data.repositories.job_repository import JobStatus as RepoJobStatus

logger = logging.getLogger(__name__)

RECOVERY_FAILED_RUNNING = (
    "RECOVERY_BACKEND_RESTART: job was running when the backend stopped; "
    "marked failed — re-queue or retry from the client if still needed."
)
RECOVERY_FAILED_PAUSED = (
    "RECOVERY_BACKEND_RESTART: job was paused when the backend stopped; "
    "marked failed — resume is not restored across restarts."
)


async def reconcile_job_history_after_restart(repo: Any) -> int:
    """
    Mark all non-deleted `running` and `paused` jobs as failed with recovery text.

    Returns the number of jobs updated.
    """
    updated = 0
    for status, message in (
        (RepoJobStatus.RUNNING, RECOVERY_FAILED_RUNNING),
        (RepoJobStatus.PAUSED, RECOVERY_FAILED_PAUSED),
    ):
        active = await repo.find({"status": status.value})
        for job in active:
            await repo.mark_failed(job.id, message)
            updated += 1
            logger.info(
                "Job queue recovery: job_id=%s status=%s -> failed (restart reconciliation)",
                job.id,
                status.value,
            )
    return updated
