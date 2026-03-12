"""
Unit tests for Engine Performance Metrics

Tests cover:
- Synthesis time tracking
- Cache hit/miss tracking
- Error rate tracking
- Thread-safe operations
- Metrics collector integration
- History management
- Statistics aggregation
"""

import contextlib
import time
from collections import defaultdict
from unittest.mock import MagicMock, patch

import pytest

# Try to import the performance metrics
try:
    from app.core.engines.performance_metrics import (
        EnginePerformanceMetrics,
        get_engine_metrics,
    )

    HAS_PERFORMANCE_METRICS = True
except ImportError:
    HAS_PERFORMANCE_METRICS = False

pytestmark = pytest.mark.skipif(
    not HAS_PERFORMANCE_METRICS,
    reason="Performance metrics not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def metrics():
    """Create an EnginePerformanceMetrics instance for testing."""
    if not HAS_PERFORMANCE_METRICS:
        pytest.skip("Performance metrics not available")

    metrics = EnginePerformanceMetrics()
    yield metrics
    with contextlib.suppress(Exception):
        metrics.clear()


class TestEnginePerformanceMetricsImports:
    """Test that performance metrics can be imported."""

    def test_import_metrics(self):
        """Test that EnginePerformanceMetrics can be imported."""
        if not HAS_PERFORMANCE_METRICS:
            pytest.skip("Performance metrics not available")
        from app.core.engines.performance_metrics import EnginePerformanceMetrics

        assert EnginePerformanceMetrics is not None

    def test_import_get_function(self):
        """Test that get_engine_metrics can be imported."""
        if not HAS_PERFORMANCE_METRICS:
            pytest.skip("Performance metrics not available")
        from app.core.engines.performance_metrics import get_engine_metrics

        assert get_engine_metrics is not None


class TestEnginePerformanceMetricsStructure:
    """Test EnginePerformanceMetrics class structure."""

    def test_metrics_initialization(self, metrics):
        """Test that metrics initializes correctly."""
        assert metrics is not None
        assert hasattr(metrics, "_lock")
        assert hasattr(metrics, "_synthesis_times")
        assert hasattr(metrics, "_cache_hits")
        assert hasattr(metrics, "_cache_misses")
        assert hasattr(metrics, "_errors")
        assert hasattr(metrics, "_total_requests")

    def test_metrics_has_defaultdict(self, metrics):
        """Test that metrics use defaultdict for tracking."""
        assert isinstance(metrics._synthesis_times, defaultdict)
        assert isinstance(metrics._cache_hits, defaultdict)
        assert isinstance(metrics._cache_misses, defaultdict)
        assert isinstance(metrics._errors, defaultdict)
        assert isinstance(metrics._total_requests, defaultdict)

    def test_metrics_has_max_history(self, metrics):
        """Test that metrics has max timing history limit."""
        assert hasattr(metrics, "_max_timing_history")
        assert metrics._max_timing_history == 1000


class TestEnginePerformanceMetricsSynthesisTime:
    """Test synthesis time tracking."""

    def test_record_synthesis_time(self, metrics):
        """Test recording synthesis time."""
        metrics.record_synthesis_time("xtts", 0.5, cached=False)

        assert "xtts" in metrics._synthesis_times
        assert len(metrics._synthesis_times["xtts"]) == 1
        assert metrics._synthesis_times["xtts"][0] == 0.5
        assert metrics._total_requests["xtts"] == 1

    def test_record_multiple_synthesis_times(self, metrics):
        """Test recording multiple synthesis times."""
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("xtts", 0.6, cached=True)
        metrics.record_synthesis_time("xtts", 0.7, cached=False)

        assert len(metrics._synthesis_times["xtts"]) == 3
        assert metrics._total_requests["xtts"] == 3

    def test_record_cached_synthesis(self, metrics):
        """Test recording cached synthesis."""
        metrics.record_synthesis_time("xtts", 0.1, cached=True)

        assert metrics._cache_hits["xtts"] == 1
        assert metrics._cache_misses["xtts"] == 0

    def test_record_uncached_synthesis(self, metrics):
        """Test recording uncached synthesis."""
        metrics.record_synthesis_time("xtts", 0.5, cached=False)

        assert metrics._cache_hits["xtts"] == 0
        assert metrics._cache_misses["xtts"] == 1

    def test_timing_history_limit(self, metrics):
        """Test that timing history is limited."""
        # Add more than max history
        for i in range(1500):
            metrics.record_synthesis_time("xtts", 0.5 + i * 0.001, cached=False)

        # Should be limited to max_timing_history
        assert len(metrics._synthesis_times["xtts"]) <= metrics._max_timing_history


class TestEnginePerformanceMetricsCacheTracking:
    """Test cache hit/miss tracking."""

    def test_record_cache_hit(self, metrics):
        """Test recording cache hit."""
        metrics.record_cache_hit("xtts")

        assert metrics._cache_hits["xtts"] == 1
        assert metrics._cache_misses["xtts"] == 0

    def test_record_cache_miss(self, metrics):
        """Test recording cache miss."""
        metrics.record_cache_miss("xtts")

        assert metrics._cache_hits["xtts"] == 0
        assert metrics._cache_misses["xtts"] == 1

    def test_multiple_cache_operations(self, metrics):
        """Test multiple cache operations."""
        metrics.record_cache_hit("xtts")
        metrics.record_cache_hit("xtts")
        metrics.record_cache_miss("xtts")
        metrics.record_cache_hit("xtts")

        assert metrics._cache_hits["xtts"] == 3
        assert metrics._cache_misses["xtts"] == 1


class TestEnginePerformanceMetricsErrorTracking:
    """Test error tracking."""

    def test_record_error(self, metrics):
        """Test recording error."""
        metrics.record_error("xtts")

        assert metrics._errors["xtts"] == 1

    def test_multiple_errors(self, metrics):
        """Test recording multiple errors."""
        metrics.record_error("xtts")
        metrics.record_error("xtts")
        metrics.record_error("whisper")

        assert metrics._errors["xtts"] == 2
        assert metrics._errors["whisper"] == 1


class TestEnginePerformanceMetricsStatistics:
    """Test statistics retrieval."""

    def test_get_engine_stats(self, metrics):
        """Test getting engine statistics."""
        # Record some metrics
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("xtts", 0.6, cached=True)
        metrics.record_synthesis_time("xtts", 0.7, cached=False)
        metrics.record_error("xtts")

        stats = metrics.get_engine_stats("xtts")

        assert isinstance(stats, dict)
        assert "total_requests" in stats
        assert "cache_hits" in stats
        assert "cache_misses" in stats
        assert "cache_hit_rate" in stats
        assert "errors" in stats
        assert "synthesis_times" in stats
        if stats["synthesis_times"]:
            assert "min" in stats["synthesis_times"]
            assert "max" in stats["synthesis_times"]
            assert "mean" in stats["synthesis_times"]

        assert stats["total_requests"] == 3
        assert stats["cache_hits"] == 1
        assert stats["cache_misses"] == 2
        assert stats["errors"] == 1

    def test_cache_hit_rate_calculation(self, metrics):
        """Test cache hit rate calculation."""
        # 3 hits, 2 misses = 60% hit rate
        metrics.record_cache_hit("xtts")
        metrics.record_cache_hit("xtts")
        metrics.record_cache_hit("xtts")
        metrics.record_cache_miss("xtts")
        metrics.record_cache_miss("xtts")

        stats = metrics.get_engine_stats("xtts")

        assert stats["cache_hit_rate"] == "60.00%"

    def test_cache_hit_rate_no_requests(self, metrics):
        """Test cache hit rate with no requests."""
        stats = metrics.get_engine_stats("nonexistent")

        assert stats["cache_hit_rate"] == "0.00%"

    def test_synthesis_time_statistics(self, metrics):
        """Test synthesis time statistics."""
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("xtts", 0.6, cached=False)
        metrics.record_synthesis_time("xtts", 0.7, cached=False)

        stats = metrics.get_engine_stats("xtts")

        assert stats["synthesis_times"] is not None
        assert stats["synthesis_times"]["min"] == 0.5
        assert stats["synthesis_times"]["max"] == 0.7
        assert 0.5 <= stats["synthesis_times"]["mean"] <= 0.7


class TestEnginePerformanceMetricsAggregation:
    """Test statistics aggregation."""

    def test_get_all_engines_stats(self, metrics):
        """Test getting statistics for all engines."""
        # Record metrics for multiple engines
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("whisper", 0.3, cached=True)
        metrics.record_synthesis_time("rvc", 0.4, cached=False)

        all_stats = metrics.get_all_stats()

        assert isinstance(all_stats, dict)
        assert "xtts" in all_stats
        assert "whisper" in all_stats
        assert "rvc" in all_stats

    def test_get_overall_stats(self, metrics):
        """Test getting overall statistics."""
        # Record metrics for multiple engines
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("whisper", 0.3, cached=True)
        metrics.record_cache_hit("xtts")
        metrics.record_cache_miss("whisper")

        overall = metrics.get_summary()

        assert isinstance(overall, dict)
        assert "total_engines" in overall
        assert "total_cache_hits" in overall
        assert "total_cache_misses" in overall
        assert "overall_cache_hit_rate" in overall

        assert overall["total_engines"] >= 2
        assert overall["total_cache_hits"] >= 1
        assert overall["total_cache_misses"] >= 1


class TestEnginePerformanceMetricsContextManager:
    """Test context manager for timing synthesis."""

    def test_time_synthesis_context_manager(self, metrics):
        """Test time_synthesis context manager."""
        with metrics.time_synthesis("xtts", cached=False):
            time.sleep(0.1)  # Simulate synthesis

        # Check that time was recorded
        assert "xtts" in metrics._synthesis_times
        assert len(metrics._synthesis_times["xtts"]) == 1
        assert metrics._synthesis_times["xtts"][0] > 0

    def test_time_synthesis_cached(self, metrics):
        """Test time_synthesis with cached=True."""
        with metrics.time_synthesis("xtts", cached=True):
            time.sleep(0.05)

        assert metrics._cache_hits["xtts"] == 1
        assert metrics._cache_misses["xtts"] == 0

    def test_time_synthesis_uncached(self, metrics):
        """Test time_synthesis with cached=False."""
        with metrics.time_synthesis("xtts", cached=False):
            time.sleep(0.05)

        assert metrics._cache_hits["xtts"] == 0
        assert metrics._cache_misses["xtts"] == 1


class TestEnginePerformanceMetricsCleanup:
    """Test cleanup operations."""

    def test_clear_engine(self, metrics):
        """Test clearing metrics for a specific engine."""
        # Add metrics
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_cache_hit("xtts")
        metrics.record_error("xtts")

        # Clear specific engine
        metrics.clear(engine_name="xtts")

        # Should be empty
        assert len(metrics._synthesis_times.get("xtts", [])) == 0
        assert metrics._cache_hits.get("xtts", 0) == 0
        assert metrics._cache_misses.get("xtts", 0) == 0
        assert metrics._errors.get("xtts", 0) == 0

    def test_clear_all(self, metrics):
        """Test clearing all metrics."""
        # Add metrics for multiple engines
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_synthesis_time("whisper", 0.3, cached=True)

        # Clear all (no engine_name specified)
        metrics.clear()

        # Should be empty
        assert len(metrics._synthesis_times) == 0
        assert len(metrics._cache_hits) == 0
        assert len(metrics._cache_misses) == 0


class TestEnginePerformanceMetricsThreadSafety:
    """Test thread safety."""

    def test_thread_safe_operations(self, metrics):
        """Test that operations are thread-safe."""
        import threading

        def record_metrics(engine_name):
            for _ in range(10):
                metrics.record_synthesis_time(engine_name, 0.5, cached=False)
                metrics.record_cache_hit(engine_name)

        # Create multiple threads
        threads = []
        for i in range(5):
            thread = threading.Thread(target=record_metrics, args=(f"engine_{i}",))
            threads.append(thread)
            thread.start()

        # Wait for all threads
        for thread in threads:
            thread.join()

        # Check that all metrics were recorded
        for i in range(5):
            engine_name = f"engine_{i}"
            assert metrics._total_requests[engine_name] == 10
            assert metrics._cache_hits[engine_name] == 10


class TestEnginePerformanceMetricsIntegration:
    """Test metrics collector integration."""

    @patch("app.core.engines.performance_metrics.get_metrics_collector")
    def test_metrics_collector_integration(self, mock_get_collector, metrics):
        """Test integration with metrics collector."""
        # Mock metrics collector
        mock_collector = MagicMock()
        mock_get_collector.return_value = mock_collector

        # Reinitialize metrics to use mocked collector
        metrics._metrics_collector = mock_collector

        # Record metrics
        metrics.record_synthesis_time("xtts", 0.5, cached=False)
        metrics.record_cache_hit("xtts")

        # Check that collector was called
        assert mock_collector.timer.called
        assert mock_collector.increment.called


class TestEnginePerformanceMetricsGetFunction:
    """Test the get_engine_metrics function."""

    def test_get_engine_metrics(self):
        """Test getting global metrics instance."""
        if not HAS_PERFORMANCE_METRICS:
            pytest.skip("Performance metrics not available")

        from app.core.engines.performance_metrics import get_engine_metrics

        metrics1 = get_engine_metrics()
        metrics2 = get_engine_metrics()

        # Should return same instance (singleton)
        assert metrics1 is metrics2
