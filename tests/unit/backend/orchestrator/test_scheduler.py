"""Tests for the job scheduler."""

from __future__ import annotations

import pytest

from backend.orchestrator.scheduler import JobScheduler, ScheduledJobStatus
from backend.orchestrator.gpu_tracker import GpuTracker
from backend.orchestrator.schemas import (
    OrchestrationPriority,
    OrchestrationRequest,
)


class TestJobScheduler:
    def setup_method(self):
        self.tracker = GpuTracker(poll_interval_s=999)
        self.scheduler = JobScheduler(
            max_concurrent=2, gpu_tracker=self.tracker
        )

    def test_submit_job(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        job = self.scheduler.submit("j1", req)
        assert job.status == ScheduledJobStatus.WAITING
        assert self.scheduler.get_queue_depth() == 1

    def test_schedule_next(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        self.scheduler.submit("j1", req)
        scheduled = self.scheduler.try_schedule_next()
        assert scheduled is not None
        assert scheduled.status == ScheduledJobStatus.RUNNING
        assert self.scheduler.get_queue_depth() == 0
        assert self.scheduler.get_running_count() == 1

    def test_concurrency_limit(self):
        for i in range(5):
            req = OrchestrationRequest(text=f"Job {i}", voice_profile_id="p1")
            self.scheduler.submit(f"j{i}", req)

        s1 = self.scheduler.try_schedule_next()
        s2 = self.scheduler.try_schedule_next()
        s3 = self.scheduler.try_schedule_next()
        assert s1 is not None
        assert s2 is not None
        assert s3 is None  # at max_concurrent=2

    def test_mark_completed(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        self.scheduler.submit("j1", req)
        self.scheduler.try_schedule_next()
        self.scheduler.mark_completed("j1", duration_ms=5000, engine_id="xtts")
        assert self.scheduler.get_running_count() == 0

    def test_cancel_queued(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        self.scheduler.submit("j1", req)
        assert self.scheduler.cancel("j1") is True
        assert self.scheduler.get_queue_depth() == 0

    def test_cancel_unknown(self):
        assert self.scheduler.cancel("nonexistent") is False

    def test_priority_ordering(self):
        batch_req = OrchestrationRequest(
            text="Batch", voice_profile_id="p1",
            priority=OrchestrationPriority.BATCH
        )
        realtime_req = OrchestrationRequest(
            text="Realtime", voice_profile_id="p1",
            priority=OrchestrationPriority.REALTIME
        )
        self.scheduler.submit("batch", batch_req)
        self.scheduler.submit("realtime", realtime_req)

        first = self.scheduler.try_schedule_next()
        assert first is not None
        assert first.job_id == "realtime"

    def test_estimate_wait(self):
        req = OrchestrationRequest(text="Hello", voice_profile_id="p1")
        self.scheduler.submit("j1", req)
        self.scheduler.submit("j2", req)
        wait = self.scheduler.estimate_wait("j2")
        assert wait is not None
        assert wait >= 0

    def test_get_status(self):
        status = self.scheduler.get_status()
        assert "queue_depth" in status
        assert "running" in status
        assert "max_concurrent" in status
