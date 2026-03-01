"""Tests for OrchestrationService — core orchestration logic."""

from __future__ import annotations

import pytest

from backend.orchestrator.schemas import (
    OrchestrationRequest,
    OrchestrationStatus,
    OrchestrationStrategy,
    QualityThresholdPolicy,
    OrchestrationQualityMetrics,
)
from backend.orchestrator.service import OrchestrationService


class TestOrchestrationService:
    def setup_method(self):
        self.service = OrchestrationService()

    def test_submit_async_returns_queued(self):
        request = OrchestrationRequest(
            text="Hello world", voice_profile_id="test_profile"
        )
        response = self.service.submit_async(request)
        assert response.status == OrchestrationStatus.QUEUED
        assert response.job_id

    def test_get_status_returns_none_for_unknown(self):
        status = self.service.get_status("nonexistent-job")
        assert status is None

    def test_get_status_returns_state(self):
        request = OrchestrationRequest(
            text="Test", voice_profile_id="p1"
        )
        response = self.service.submit_async(request)
        status = self.service.get_status(response.job_id)
        assert status is not None
        assert status.job_id == response.job_id

    def test_cancel_unknown_returns_false(self):
        assert self.service.cancel("nonexistent") is False

    def test_cancel_existing_returns_true(self):
        request = OrchestrationRequest(
            text="Test", voice_profile_id="p1"
        )
        response = self.service.submit_async(request)
        assert self.service.cancel(response.job_id) is True
        status = self.service.get_status(response.job_id)
        assert status is not None
        assert status.status == OrchestrationStatus.CANCELLED

    def test_get_debug_info_returns_none_for_unknown(self):
        assert self.service.get_debug_info("nonexistent") is None

    def test_build_default_chain_auto(self):
        chain = self.service._build_default_chain(OrchestrationStrategy.AUTO)
        assert len(chain.steps) == 3
        assert str(chain.steps[0].type) == "synthesis"
        assert str(chain.steps[1].type) == "quality_evaluation"
        assert str(chain.steps[2].type) == "post_processing"

    def test_build_default_chain_speed_first(self):
        chain = self.service._build_default_chain(OrchestrationStrategy.SPEED_FIRST)
        assert len(chain.steps) == 2

    def test_quality_meets_threshold_pass(self):
        metrics = OrchestrationQualityMetrics(
            mos_score=4.0, similarity=0.9, naturalness=0.85, snr_db=35
        )
        policy = QualityThresholdPolicy()
        assert self.service._quality_meets_threshold(metrics, policy) is True

    def test_quality_meets_threshold_fail_mos(self):
        metrics = OrchestrationQualityMetrics(mos_score=2.0)
        policy = QualityThresholdPolicy(min_mos=3.5)
        assert self.service._quality_meets_threshold(metrics, policy) is False

    def test_quality_meets_threshold_fail_similarity(self):
        metrics = OrchestrationQualityMetrics(mos_score=4.0, similarity=0.3)
        policy = QualityThresholdPolicy(min_similarity=0.7)
        assert self.service._quality_meets_threshold(metrics, policy) is False

    def test_quality_none_values_pass(self):
        metrics = OrchestrationQualityMetrics()
        policy = QualityThresholdPolicy()
        assert self.service._quality_meets_threshold(metrics, policy) is True


class TestEventEmission:
    def test_events_emitted_on_submit(self):
        service = OrchestrationService()
        events = []
        service.emitter.add_listener(lambda e: events.append(e))

        request = OrchestrationRequest(
            text="Test", voice_profile_id="p1"
        )
        service.submit_async(request)
        assert len(events) == 1
        assert str(events[0].event_type) == "job_queued"
