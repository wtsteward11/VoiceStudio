"""
Enhanced Unit Tests for Resource Manager
Tests all optimizations: priority queues, VRAM admission control, circuit breaker,
exponential backoff, GPU monitoring, resource predictions, and alerts.
"""

import gc
import sys
import time
from pathlib import Path
from unittest.mock import Mock, patch

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import modules
try:
    from app.core.runtime.resource_manager import (
        GPUMonitor,
        Job,
        JobPriority,
        JobStatus,
        ResourceManager,
        ResourceRequirement,
    )
    from app.core.runtime.resource_manager_enhanced import (
        EnhancedResourceManager,
        ResourcePrediction,
        ResourceUsage,
    )
except ImportError as e:
    pytest.skip(f"Could not import resource_manager modules: {e}", allow_module_level=True)


@pytest.fixture(autouse=True)
def _shutdown_leaked_enhanced_resource_managers():
    """Stop EnhancedResourceManager monitoring after each test (prevents post-suite hang)."""
    yield
    _erm = EnhancedResourceManager
    for obj in gc.get_objects():
        try:
            if type(obj) is _erm and getattr(obj, "running", False):
                obj.shutdown()
        except (ReferenceError, TypeError, RuntimeError):
            pass


class TestGPUMonitor:
    """Test GPU monitoring with caching."""

    def test_gpu_monitor_initialization(self):
        """Test GPUMonitor initializes correctly."""
        monitor = GPUMonitor()
        assert monitor._has_gpu is False or monitor._has_gpu is True
        assert monitor._check_interval == 5.0
        assert monitor._last_check == 0.0

    def test_gpu_monitor_caching(self):
        """Test GPU monitor caches results."""
        monitor = GPUMonitor()

        # Mock GPU info
        with patch.object(monitor, "_update_gpu_info") as mock_update:
            monitor._has_gpu = True
            monitor._total_vram_gb = 8.0
            monitor._available_vram_gb = 4.0
            monitor._used_vram_gb = 4.0
            monitor._last_check = time.time()

            # First call should use cache
            result1 = monitor.get_available_vram_gb(force_check=False)
            mock_update.assert_not_called()
            assert result1 == 4.0

            # Force check should update
            monitor.get_available_vram_gb(force_check=True)
            mock_update.assert_called_once()

    def test_has_sufficient_vram(self):
        """Test VRAM sufficiency check."""
        monitor = GPUMonitor()
        monitor._has_gpu = True
        monitor._total_vram_gb = 8.0
        monitor._available_vram_gb = 4.0
        monitor._last_check = time.time()  # Set last check to avoid update

        # Mock get_available_vram_gb to return our test value
        with patch.object(monitor, "get_available_vram_gb", return_value=4.0):
            # Should have enough for 2GB (4GB available >= 2GB + 1GB headroom = 3GB)
            assert monitor.has_sufficient_vram(2.0, 1.0) is True

            # Should not have enough for 5GB (4GB available < 5GB + 1GB headroom = 6GB)
            assert monitor.has_sufficient_vram(5.0, 1.0) is False

    def test_get_vram_info(self):
        """Test VRAM info retrieval."""
        monitor = GPUMonitor()
        monitor._has_gpu = True
        monitor._total_vram_gb = 8.0
        monitor._used_vram_gb = 4.0
        monitor._available_vram_gb = 4.0

        # Mock get_available_vram_gb to avoid real GPU query
        with patch.object(monitor, "get_available_vram_gb", return_value=4.0):
            info = monitor.get_vram_info()
            assert info["has_gpu"] is True
            assert info["total_gb"] == 8.0
            assert info["used_gb"] == 4.0
            assert info["available_gb"] == 4.0


class TestResourceManager:
    """Test base ResourceManager optimizations."""

    def test_resource_manager_initialization(self):
        """Test ResourceManager initializes correctly."""
        manager = ResourceManager(vram_headroom_gb=1.0)
        assert manager.vram_headroom_gb == 1.0
        assert manager.gpu_monitor is not None
        assert manager.realtime_queue is not None
        assert manager.interactive_queue is not None
        assert manager.batch_queue is not None
        assert len(manager.active_jobs) == 0
        assert len(manager.job_history) == 0

    def test_priority_queue_submission(self):
        """Test jobs are queued in correct priority queues."""
        manager = ResourceManager()

        # Mock GPU monitor to allow all jobs
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)

        # Submit realtime job
        job1 = manager.submit_job("job1", "engine1", "synthesize", JobPriority.REALTIME, req, {})
        assert job1 is True
        assert manager.realtime_queue.qsize() == 1

        # Submit interactive job
        job2 = manager.submit_job("job2", "engine1", "synthesize", JobPriority.INTERACTIVE, req, {})
        assert job2 is True
        assert manager.interactive_queue.qsize() == 1

        # Submit batch job
        job3 = manager.submit_job("job3", "engine1", "synthesize", JobPriority.BATCH, req, {})
        assert job3 is True
        assert manager.batch_queue.qsize() == 1

    def test_priority_queue_ordering(self):
        """Test jobs are retrieved in priority order."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)

        # Submit jobs in reverse priority order
        manager.submit_job("batch1", "engine1", "task", JobPriority.BATCH, req, {})
        time.sleep(0.01)  # Small delay to ensure different timestamps
        manager.submit_job("interactive1", "engine1", "task", JobPriority.INTERACTIVE, req, {})
        time.sleep(0.01)
        manager.submit_job("realtime1", "engine1", "task", JobPriority.REALTIME, req, {})

        # Should retrieve realtime first
        job = manager.get_next_job()
        assert job is not None
        assert job.job_id == "realtime1"
        assert job.priority == JobPriority.REALTIME

        # Then interactive
        job = manager.get_next_job()
        assert job is not None
        assert job.job_id == "interactive1"
        assert job.priority == JobPriority.INTERACTIVE

        # Then batch
        job = manager.get_next_job()
        assert job is not None
        assert job.job_id == "batch1"
        assert job.priority == JobPriority.BATCH

    def test_vram_admission_control(self):
        """Test VRAM admission control."""
        manager = ResourceManager(vram_headroom_gb=1.0)

        # Mock insufficient VRAM
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=False)

        req = ResourceRequirement(vram_gb=10.0)
        result = manager.submit_job("job1", "engine1", "task", JobPriority.BATCH, req, {})

        # Job should still be queued (will wait for resources)
        assert result is True
        assert manager.batch_queue.qsize() == 1

        # But get_next_job should return None if insufficient VRAM
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=False)
        job = manager.get_next_job()
        assert job is None

    def test_circuit_breaker(self):
        """Test circuit breaker functionality."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        # Activate circuit breaker
        manager.engine_circuit_breaker["engine1"] = True

        req = ResourceRequirement(vram_gb=1.0)
        result = manager.submit_job("job1", "engine1", "task", JobPriority.REALTIME, req, {})

        # Job should be rejected
        assert result is False

        # Circuit breaker should prevent job retrieval
        manager.submit_job("job2", "engine1", "task", JobPriority.REALTIME, req, {})
        job = manager.get_next_job()
        assert job is None

    def test_exponential_backoff(self):
        """Test exponential backoff on failures."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)
        job = Job("job1", "engine1", "task", JobPriority.REALTIME, req, {})

        # Fail job 3 times
        for _i in range(3):
            manager.start_job(job)
            manager.complete_job("job1", success=False, error="Test error")

        # Check backoff is set
        backoff_until = manager.engine_backoff.get("engine1", 0.0)
        assert backoff_until > time.time()

        # Job should be rejected during backoff
        result = manager.submit_job("job2", "engine1", "task", JobPriority.REALTIME, req, {})
        assert result is False

    def test_circuit_breaker_activation(self):
        """Test circuit breaker activates after 5 failures."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)
        job = Job("job1", "engine1", "task", JobPriority.REALTIME, req, {})

        # Fail job 5 times
        for _i in range(5):
            manager.start_job(job)
            manager.complete_job("job1", success=False, error="Test error")

        # Circuit breaker should be activated
        assert manager.engine_circuit_breaker.get("engine1", False) is True

    def test_job_lifecycle(self):
        """Test job lifecycle (start, complete)."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=2.0)
        job = Job("job1", "engine1", "task", JobPriority.REALTIME, req, {})

        # Start job
        manager.start_job(job)
        assert job.status == JobStatus.RUNNING
        assert job.started_at is not None
        assert "job1" in manager.active_jobs
        assert manager.allocated_vram_gb["job1"] == 2.0

        # Complete job
        manager.complete_job("job1", success=True)
        assert job.status == JobStatus.COMPLETED
        assert job.completed_at is not None
        assert "job1" not in manager.active_jobs
        assert "job1" not in manager.allocated_vram_gb
        assert len(manager.job_history) == 1

    def test_job_history_limiting(self):
        """Test job history is limited."""
        manager = ResourceManager()
        manager.max_history = 5
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)

        # Create 10 jobs
        for i in range(10):
            job = Job(f"job{i}", "engine1", "task", JobPriority.BATCH, req, {})
            manager.start_job(job)
            manager.complete_job(f"job{i}", success=True)

        # History should be limited to 5
        assert len(manager.job_history) == 5
        # Should contain the last 5 jobs
        assert manager.job_history[-1].job_id == "job9"

    def test_resource_status(self):
        """Test resource status retrieval."""
        manager = ResourceManager()
        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 4.0,
                "available_gb": 4.0,
            }
        )

        req = ResourceRequirement(vram_gb=1.0)
        manager.submit_job("job1", "engine1", "task", JobPriority.REALTIME, req, {})

        status = manager.get_resource_status()
        assert "gpu" in status
        assert "active_jobs" in status
        assert "queued_jobs" in status
        assert status["queued_jobs"]["realtime"] == 1
        assert "engine_status" in status

    def test_job_callback(self):
        """Test job callback execution."""
        manager = ResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        callback_called = []

        def callback(job):
            callback_called.append(job.job_id)

        req = ResourceRequirement(vram_gb=1.0)
        job = Job("job1", "engine1", "task", JobPriority.REALTIME, req, {}, callback=callback)

        manager.start_job(job)
        manager.complete_job("job1", success=True)

        assert len(callback_called) == 1
        assert callback_called[0] == "job1"


class TestEnhancedResourceManager:
    """Test EnhancedResourceManager optimizations."""

    def test_enhanced_initialization(self):
        """Test EnhancedResourceManager initializes correctly."""
        manager = EnhancedResourceManager(
            vram_headroom_gb=1.0,
            history_window_seconds=3600.0,
            prediction_enabled=True,
            monitoring_interval=1.0,
        )
        assert manager.history_window_seconds == 3600.0
        assert manager.prediction_enabled is True
        assert manager.monitoring_interval == 1.0
        assert len(manager.resource_history) == 0
        assert manager.monitoring_thread is not None

    def test_resource_history_tracking(self):
        """Test resource usage history tracking."""
        manager = EnhancedResourceManager(monitoring_interval=0.1)
        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 4.0,
                "available_gb": 4.0,
            }
        )

        # Manually trigger collection to avoid waiting for thread
        manager._collect_resource_usage()

        # Should have collected history
        assert len(manager.resource_history) > 0
        latest = manager.resource_history[-1]
        assert latest.vram_used_gb == 4.0
        assert latest.vram_available_gb == 4.0

    def test_resource_prediction(self):
        """Test resource usage prediction."""
        manager = EnhancedResourceManager(
            prediction_enabled=True,
            monitoring_interval=0.1,
        )
        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 4.0,
                "available_gb": 4.0,
            }
        )

        # Collect some history
        for _i in range(15):
            manager._collect_resource_usage()
            time.sleep(0.01)

        # Update predictions
        manager._update_predictions()

        # Should have predictions
        assert "vram" in manager.resource_predictions
        prediction = manager.resource_predictions["vram"]
        assert isinstance(prediction, ResourcePrediction)
        assert prediction.predicted_vram_gb >= 0.0
        assert 0.0 <= prediction.confidence <= 1.0

    def test_resource_alerts(self):
        """Test resource usage alerts."""
        manager = EnhancedResourceManager(
            monitoring_interval=0.1,
        )
        manager.alert_thresholds["vram_usage_percent"] = 50.0  # Lower threshold for testing

        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 5.0,  # 62.5% usage
                "available_gb": 3.0,
            }
        )

        # Collect usage and check alerts
        manager._collect_resource_usage()
        manager._check_resource_alerts()

        # Should have triggered VRAM alert (CPU may also trigger on busy systems)
        vram_alerts = [a for a in manager.resource_alerts if a["type"] == "vram_high"]
        assert len(vram_alerts) > 0, f"Expected vram_high alert, got: {manager.resource_alerts}"
        alert = vram_alerts[0]
        assert "VRAM usage" in alert["message"]

    def test_statistics_tracking(self):
        """Test statistics tracking."""
        manager = EnhancedResourceManager()
        manager.gpu_monitor.has_sufficient_vram = Mock(return_value=True)

        req = ResourceRequirement(vram_gb=1.0)

        # Submit and complete jobs
        manager.submit_job("job1", "engine1", "task", JobPriority.REALTIME, req, {})
        job = manager.get_next_job()
        if job:
            manager.start_job(job)
            time.sleep(0.01)
            manager.complete_job("job1", success=True)

        # Check statistics
        assert manager.stats["total_jobs_submitted"] >= 1
        assert manager.stats["total_jobs_completed"] >= 1

    def test_peak_usage_tracking(self):
        """Test peak resource usage tracking."""
        manager = EnhancedResourceManager(monitoring_interval=0.1)
        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 6.0,  # High usage
                "available_gb": 2.0,
            }
        )

        manager._collect_resource_usage()

        # Check peak is tracked
        assert manager.stats["peak_vram_usage_gb"] >= 6.0

    def test_predict_resource_usage(self):
        """Test resource usage prediction for job."""
        manager = EnhancedResourceManager(prediction_enabled=True)
        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 4.0,
                "available_gb": 4.0,
            }
        )

        # Collect some history
        for _i in range(10):
            manager._collect_resource_usage()

        req = ResourceRequirement(vram_gb=2.0)
        prediction = manager.predict_resource_usage(req, window_seconds=60.0)

        assert isinstance(prediction, ResourcePrediction)
        assert prediction.predicted_vram_gb >= 0.0
        assert 0.0 <= prediction.confidence <= 1.0

    def test_monitoring_thread_stops(self):
        """Test monitoring thread stops when manager stops."""
        manager = EnhancedResourceManager(monitoring_interval=0.1)
        thread = manager.monitoring_thread
        assert thread.is_alive()

        manager.shutdown()

        assert manager.running is False
        if thread is not None:
            assert not thread.is_alive()

    def test_alert_history_limiting(self):
        """Test alert history is limited."""
        manager = EnhancedResourceManager()
        manager.alert_thresholds["vram_usage_percent"] = 0.0  # Always trigger

        manager.gpu_monitor.get_vram_info = Mock(
            return_value={
                "has_gpu": True,
                "total_gb": 8.0,
                "used_gb": 8.0,
                "available_gb": 0.0,
            }
        )

        # Trigger many alerts
        for i in range(150):
            manager._trigger_alert("test", f"Alert {i}", {})

        # Should be limited to 100
        assert len(manager.resource_alerts) == 100
        assert manager.resource_alerts[-1]["message"] == "Alert 149"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
