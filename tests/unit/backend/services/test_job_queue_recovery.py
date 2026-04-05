"""Tests for durable job_history startup reconciliation and schema parity."""

from __future__ import annotations

import tempfile
from datetime import datetime
from pathlib import Path

import aiosqlite
import pytest

from backend.data.migrations.v001_core_persistence_tables import (
    CorePersistenceTablesMigration,
)
from backend.data.migrations.v004_job_history_columns import JobHistoryColumnsMigration
from backend.data.repositories.job_repository import (
    InMemoryJobRepository,
    JobEntity,
    JobRepository,
    JobStatus,
    JobType,
)
from backend.data.repository_base import ConnectionConfig
from backend.services.job_queue_recovery import (
    RECOVERY_FAILED_PAUSED,
    RECOVERY_FAILED_RUNNING,
    reconcile_job_history_after_restart,
)


@pytest.mark.asyncio
async def test_reconcile_marks_running_and_paused_failed() -> None:
    repo = InMemoryJobRepository()
    await repo.create(
        JobEntity(
            id="run-1",
            job_type=JobType.BATCH.value,
            name="r",
            status=JobStatus.RUNNING.value,
            progress=0.5,
            created_at=datetime.now(),
            updated_at=datetime.now(),
        )
    )
    await repo.create(
        JobEntity(
            id="pause-1",
            job_type=JobType.BATCH.value,
            name="p",
            status=JobStatus.PAUSED.value,
            progress=0.2,
            created_at=datetime.now(),
            updated_at=datetime.now(),
        )
    )
    await repo.create(
        JobEntity(
            id="pend-1",
            job_type=JobType.BATCH.value,
            name="q",
            status=JobStatus.PENDING.value,
            progress=0.0,
            created_at=datetime.now(),
            updated_at=datetime.now(),
        )
    )

    n = await reconcile_job_history_after_restart(repo)
    assert n == 2

    failed_run = await repo.get_by_id("run-1")
    assert failed_run is not None
    assert failed_run.status == JobStatus.FAILED.value
    assert failed_run.error == RECOVERY_FAILED_RUNNING

    failed_pause = await repo.get_by_id("pause-1")
    assert failed_pause is not None
    assert failed_pause.status == JobStatus.FAILED.value
    assert failed_pause.error == RECOVERY_FAILED_PAUSED

    pending = await repo.get_by_id("pend-1")
    assert pending is not None
    assert pending.status == JobStatus.PENDING.value


@pytest.mark.asyncio
async def test_v004_columns_allow_name_and_progress_fields() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        db_path = str(Path(tmp) / "job_hist.sqlite")
        async with aiosqlite.connect(db_path) as conn:
            conn.row_factory = aiosqlite.Row
            await CorePersistenceTablesMigration().upgrade(conn)
            await JobHistoryColumnsMigration().upgrade(conn)

        cfg = ConnectionConfig(sqlite_path=db_path)
        repo = JobRepository(config=cfg)
        await repo.connect()
        try:
            await repo.create(
                JobEntity(
                    id="sqlite-job-1",
                    job_type=JobType.BATCH.value,
                    name="Named Batch",
                    status=JobStatus.PENDING.value,
                    progress=0.0,
                    created_at=datetime.now(),
                    updated_at=datetime.now(),
                )
            )
            loaded = await repo.get_by_id("sqlite-job-1")
            assert loaded is not None
            assert loaded.name == "Named Batch"

            await repo.update_progress("sqlite-job-1", 0.4, "synth", 3)
            await repo.mark_started("sqlite-job-1")
            await repo.mark_completed(
                "sqlite-job-1",
                result_path="/tmp/out.wav",
                result_id="audio-xyz",
            )
            done = await repo.get_by_id("sqlite-job-1")
            assert done is not None
            assert done.status == JobStatus.COMPLETED.value
            assert done.result_id == "audio-xyz"
            assert done.current_step_index == 3
        finally:
            await repo.disconnect()
