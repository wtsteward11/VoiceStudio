"""Usage stats wiring test (Task 10)."""
from __future__ import annotations


def test_usage_stats_file_is_readable():
    from backend.services.usage_stats import get_usage_stats

    stats = get_usage_stats()
    for key in ("synthesis_minutes", "exports_completed", "models_downloaded"):
        assert key in stats


def test_record_synthesis_minutes_increments():
    from backend.services.usage_stats import get_usage_stats, record_synthesis_minutes

    before = get_usage_stats()["synthesis_minutes"]
    record_synthesis_minutes(0.5)
    after = get_usage_stats()["synthesis_minutes"]
    assert after > before
