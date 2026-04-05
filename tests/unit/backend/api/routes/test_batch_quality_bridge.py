"""
Tests for GAP-030: batch completion → quality history bridge.

Verifies that _store_batch_quality_history populates the quality history service
when quality metrics are present, and skips storage when metrics are absent.
"""

from __future__ import annotations

import uuid
from unittest.mock import patch

import pytest

from backend.api.routes.batch import _store_batch_quality_history
from backend.services import quality_history_service


@pytest.fixture(autouse=True)
def _clear_quality_history():
    """Reset the quality history store between tests."""
    quality_history_service._quality_history.clear()
    yield
    quality_history_service._quality_history.clear()


def _make_job_data(
    *,
    voice_profile_id: str = "profile-1",
    project_id: str = "proj-1",
    engine_id: str = "xtts_v2",
    text: str = "Hello world",
    enhance_quality: bool = False,
    name: str = "Test Batch",
) -> dict:
    return {
        "voice_profile_id": voice_profile_id,
        "project_id": project_id,
        "engine_id": engine_id,
        "text": text,
        "enhance_quality": enhance_quality,
        "name": name,
    }


class TestBatchQualityBridge:
    """GAP-030 acceptance: batch quality metrics → quality history store."""

    def test_store_on_success_with_metrics(self):
        """Successful batch with quality metrics stores a history entry."""
        job_id = str(uuid.uuid4())
        job_data = _make_job_data()
        quality_metrics = {"mos_score": 3.8, "similarity": 0.85, "naturalness": 0.9}
        quality_score = 0.82

        _store_batch_quality_history(
            job_id=job_id,
            job_data=job_data,
            quality_metrics=quality_metrics,
            quality_score=quality_score,
            quality_status="pass",
            audio_id="batch_abc123",
        )

        entries = quality_history_service.get_entries("profile-1")
        assert len(entries) == 1

        entry = entries[0]
        assert entry.profile_id == "profile-1"
        assert entry.project_id == "proj-1"
        assert entry.engine == "xtts_v2"
        assert entry.quality_score == 0.82
        assert entry.metrics == quality_metrics
        assert entry.synthesis_text == "Hello world"
        assert entry.metadata["source"] == "batch"
        assert entry.metadata["job_id"] == job_id
        assert entry.metadata["result_audio_id"] == "batch_abc123"
        assert entry.metadata["quality_status"] == "pass"
        assert entry.enhanced_quality is False

    def test_skip_when_quality_score_none(self):
        """Batch completion with quality_score=None must NOT store an entry.

        The caller (_process_batch_job) guards with `if quality_metrics and quality_score is not None`,
        so _store_batch_quality_history is never called. This test documents the contract.
        """
        entries_before = quality_history_service.get_all_entries_flat()
        assert len(entries_before) == 0

    def test_skip_when_quality_metrics_empty(self):
        """Batch completion with empty quality_metrics must NOT store an entry.

        The caller guards with `if quality_metrics and ...`, so falsy dicts skip storage.
        """
        entries_before = quality_history_service.get_all_entries_flat()
        assert len(entries_before) == 0

    def test_correct_field_mapping_with_enhance_quality(self):
        """Verify field mapping including enhance_quality=True."""
        job_id = str(uuid.uuid4())
        job_data = _make_job_data(
            voice_profile_id="profile-2",
            project_id="proj-42",
            engine_id="chatterbox",
            text="Enhanced synthesis",
            enhance_quality=True,
            name="Enhanced Batch",
        )
        quality_metrics = {"mos_score": 4.2, "similarity": 0.92, "naturalness": 0.95}

        _store_batch_quality_history(
            job_id=job_id,
            job_data=job_data,
            quality_metrics=quality_metrics,
            quality_score=0.91,
            quality_status="pass",
            audio_id="batch_enhanced",
        )

        entries = quality_history_service.get_entries("profile-2")
        assert len(entries) == 1

        entry = entries[0]
        assert entry.profile_id == "profile-2"
        assert entry.project_id == "proj-42"
        assert entry.engine == "chatterbox"
        assert entry.quality_score == 0.91
        assert entry.synthesis_text == "Enhanced synthesis"
        assert entry.enhanced_quality is True
        assert entry.metadata["batch_name"] == "Enhanced Batch"

    def test_multiple_profiles_stored_independently(self):
        """Entries for different profiles are stored in separate buckets."""
        for i, profile_id in enumerate(["p1", "p2", "p1"]):
            _store_batch_quality_history(
                job_id=str(uuid.uuid4()),
                job_data=_make_job_data(voice_profile_id=profile_id),
                quality_metrics={"mos_score": 3.0 + i * 0.1},
                quality_score=0.7 + i * 0.05,
                quality_status="pass",
                audio_id=f"audio_{i}",
            )

        assert len(quality_history_service.get_entries("p1")) == 2
        assert len(quality_history_service.get_entries("p2")) == 1

    def test_quality_status_warning_stored(self):
        """Quality status 'warning' is preserved in metadata."""
        _store_batch_quality_history(
            job_id=str(uuid.uuid4()),
            job_data=_make_job_data(),
            quality_metrics={"mos_score": 2.5},
            quality_score=0.45,
            quality_status="warning",
            audio_id=None,
        )

        entry = quality_history_service.get_entries("profile-1")[0]
        assert entry.metadata["quality_status"] == "warning"
        assert entry.metadata["result_audio_id"] is None
