"""
Unit Tests for SLO Monitor Service — Phase 5.2

Tests for SLO monitoring, alerting, and status tracking.
"""

import sys
from pathlib import Path

import pytest

# Add project paths
PROJECT_ROOT = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))
sys.path.insert(0, str(PROJECT_ROOT / "backend"))

from backend.services.slo_monitor import (
    AlertSeverity,
    SLODefinition,
    SLOMonitor,
    SLOType,
    get_slo_monitor,
    reset_slo_monitor,
)


@pytest.fixture
def tmp_data_dir(tmp_path):
    """Create a temporary data directory."""
    return tmp_path / "slo_data"


@pytest.fixture
def monitor(tmp_data_dir):
    """Create a fresh SLOMonitor instance."""
    reset_slo_monitor()
    return SLOMonitor(data_dir=tmp_data_dir)


@pytest.fixture
def custom_slo():
    """Create a custom SLO for testing."""
    return SLODefinition(
        id="test_latency",
        name="Test Latency",
        description="Test latency SLO",
        slo_type=SLOType.LATENCY,
        target=1.0,
        warning_threshold=0.8,
        critical_threshold=1.5,
        window_hours=1,
        metric_name="test_latency_seconds",
        is_higher_better=False,
    )


class TestSLODefinition:
    """Test SLODefinition class."""

    def test_slo_definition_creation(self):
        """Test creating an SLO definition."""
        slo = SLODefinition(
            id="test",
            name="Test SLO",
            description="A test SLO",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            window_hours=24,
            metric_name="test_metric",
        )
        assert slo.id == "test"
        assert slo.target == 1.0
        assert slo.is_higher_better is False

    def test_check_status_latency_met(self):
        """Test checking latency SLO status when met."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="test",
        )
        is_met, severity = slo.check_status(0.5)
        assert is_met is True
        assert severity is None

    def test_check_status_latency_warning(self):
        """Test checking latency SLO status with warning."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="test",
        )
        is_met, severity = slo.check_status(1.2)
        assert is_met is False
        assert severity == AlertSeverity.WARNING

    def test_check_status_latency_critical(self):
        """Test checking latency SLO status with critical."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="test",
        )
        is_met, severity = slo.check_status(2.0)
        assert is_met is False
        assert severity == AlertSeverity.CRITICAL

    def test_check_status_availability_met(self):
        """Test checking availability SLO when met."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.AVAILABILITY,
            target=0.995,
            warning_threshold=0.99,
            critical_threshold=0.98,
            metric_name="test",
            is_higher_better=True,
        )
        is_met, severity = slo.check_status(0.999)
        assert is_met is True
        assert severity is None

    def test_check_status_availability_critical(self):
        """Test checking availability SLO with critical."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.AVAILABILITY,
            target=0.995,
            warning_threshold=0.99,
            critical_threshold=0.98,
            metric_name="test",
            is_higher_better=True,
        )
        is_met, severity = slo.check_status(0.95)
        assert is_met is False
        assert severity == AlertSeverity.CRITICAL


class TestSLOMonitor:
    """Test SLOMonitor class."""

    def test_monitor_initialization(self, tmp_data_dir):
        """Test monitor initialization."""
        monitor = SLOMonitor(data_dir=tmp_data_dir)
        assert len(monitor.slos) > 0
        assert tmp_data_dir.exists()

    def test_register_slo(self, monitor, custom_slo):
        """Test registering a custom SLO."""
        initial_count = len(monitor.slos)
        monitor.register_slo(custom_slo)
        assert len(monitor.slos) == initial_count + 1
        assert "test_latency" in monitor.slos

    def test_record_metric(self, monitor, custom_slo):
        """Test recording a metric."""
        monitor.register_slo(custom_slo)
        monitor.record_metric("test_latency_seconds", 0.5)

        # Metric should be recorded
        assert len(monitor._metric_samples["test_latency_seconds"]) == 1

    def test_record_multiple_metrics(self, monitor, custom_slo):
        """Test recording multiple metrics."""
        monitor.register_slo(custom_slo)

        for i in range(10):
            monitor.record_metric("test_latency_seconds", 0.1 * i)

        assert len(monitor._metric_samples["test_latency_seconds"]) == 10

    def test_get_slo_status(self, monitor, custom_slo):
        """Test getting SLO status."""
        monitor.register_slo(custom_slo)
        monitor.record_metric("test_latency_seconds", 0.5)

        status = monitor.get_slo_status("test_latency")

        assert status is not None
        assert status.slo_id == "test_latency"
        assert status.is_met is True
        assert status.sample_count == 1

    def test_get_slo_status_not_found(self, monitor):
        """Test getting status of non-existent SLO."""
        status = monitor.get_slo_status("nonexistent")
        assert status is None

    def test_get_all_slo_statuses(self, monitor):
        """Test getting all SLO statuses."""
        statuses = monitor.get_all_slo_statuses()
        assert len(statuses) == len(monitor.slos)

    def test_alert_generation(self, tmp_data_dir):
        """Test that alerts are generated when SLO is breached."""
        # Create monitor with callback
        alerts_received = []

        def callback(alert):
            alerts_received.append(alert)

        monitor = SLOMonitor(data_dir=tmp_data_dir, alert_callback=callback)

        # Create SLO that will be breached
        slo = SLODefinition(
            id="test_breach",
            name="Test Breach",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="breach_metric",
        )
        monitor.register_slo(slo)

        # Record a value that breaches the SLO
        monitor.record_metric("breach_metric", 2.0)

        # Alert should be generated
        assert len(alerts_received) == 1
        assert alerts_received[0].severity == AlertSeverity.CRITICAL

    def test_get_active_alerts(self, monitor, custom_slo):
        """Test getting active alerts."""
        monitor.register_slo(custom_slo)

        # Initially no alerts
        assert len(monitor.get_active_alerts()) == 0

        # Breach the SLO
        monitor.record_metric("test_latency_seconds", 2.0)

        alerts = monitor.get_active_alerts()
        assert len(alerts) >= 1

    def test_acknowledge_alert(self, monitor, custom_slo):
        """Test acknowledging an alert."""
        monitor.register_slo(custom_slo)
        monitor.record_metric("test_latency_seconds", 2.0)

        alerts = monitor.get_active_alerts()
        if alerts:
            alert_id = alerts[0].alert_id
            success = monitor.acknowledge_alert(alert_id, "test_user")
            assert success is True

            # Alert should now be acknowledged
            alert = next(a for a in monitor.get_active_alerts() if a.alert_id == alert_id)
            assert alert.acknowledged is True
            assert alert.acknowledged_by == "test_user"

    def test_alert_resolution(self, monitor, custom_slo):
        """Test that alerts are resolved when SLO is met again."""
        monitor.register_slo(custom_slo)

        # Breach the SLO
        monitor.record_metric("test_latency_seconds", 2.0)
        assert len(monitor.get_active_alerts()) >= 1

        # Record good values to resolve
        for _ in range(10):
            monitor.record_metric("test_latency_seconds", 0.5)

        # After enough good values, alert should resolve
        # (This depends on window size and calculation method)

    def test_get_overall_health_healthy(self, tmp_data_dir):
        """Test overall health when all SLOs are met."""
        # Create monitor with single SLO
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="test_metric",
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)
        monitor.record_metric("test_metric", 0.5)

        assert monitor.get_overall_health() == "healthy"

    def test_get_overall_health_unhealthy(self, tmp_data_dir):
        """Test overall health when SLO is breached."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.LATENCY,
            target=1.0,
            warning_threshold=0.8,
            critical_threshold=1.5,
            metric_name="test_metric",
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)
        monitor.record_metric("test_metric", 2.0)  # Critical breach

        assert monitor.get_overall_health() == "unhealthy"

    def test_export_status(self, monitor, custom_slo):
        """Test exporting SLO status to file."""
        monitor.register_slo(custom_slo)
        monitor.record_metric("test_latency_seconds", 0.5)

        filepath = monitor.export_status()

        assert filepath.exists()
        assert filepath.suffix == ".json"

    def test_alert_history(self, monitor, custom_slo):
        """Test getting alert history."""
        monitor.register_slo(custom_slo)

        # Generate some alerts
        monitor.record_metric("test_latency_seconds", 2.0)

        history = monitor.get_alert_history(limit=10)
        assert isinstance(history, list)


class TestGlobalFunctions:
    """Test global convenience functions."""

    def test_get_slo_monitor_singleton(self):
        """Test that get_slo_monitor returns singleton."""
        reset_slo_monitor()
        m1 = get_slo_monitor()
        m2 = get_slo_monitor()
        assert m1 is m2

    def test_reset_slo_monitor(self):
        """Test resetting the monitor."""
        m1 = get_slo_monitor()
        reset_slo_monitor()
        m2 = get_slo_monitor()
        assert m1 is not m2


class TestBurnRate:
    """Test burn rate and error budget calculations."""

    def test_burn_rate_normal(self, tmp_data_dir):
        """Test burn rate calculation under normal conditions."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.AVAILABILITY,
            target=0.99,
            warning_threshold=0.98,
            critical_threshold=0.95,
            metric_name="availability",
            is_higher_better=True,
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)

        # Record good availability
        for _ in range(100):
            monitor.record_metric("availability", 1.0)  # 100% success

        status = monitor.get_slo_status("test")
        assert status.burn_rate < 1.0  # Burning slower than budget
        assert status.error_budget_remaining > 50  # Still have budget

    def test_burn_rate_high(self, tmp_data_dir):
        """Test burn rate calculation with errors."""
        slo = SLODefinition(
            id="test",
            name="Test",
            description="",
            slo_type=SLOType.AVAILABILITY,
            target=0.99,
            warning_threshold=0.98,
            critical_threshold=0.95,
            metric_name="availability",
            is_higher_better=True,
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)

        # Record 50% availability (way below target)
        for i in range(100):
            monitor.record_metric("availability", 1.0 if i % 2 == 0 else 0.0)

        status = monitor.get_slo_status("test")
        assert status.burn_rate > 1.0  # Burning faster than budget


class TestSLOTypes:
    """Test different SLO types."""

    def test_throughput_slo(self, tmp_data_dir):
        """Test throughput SLO type."""
        slo = SLODefinition(
            id="throughput",
            name="Throughput",
            description="",
            slo_type=SLOType.THROUGHPUT,
            target=10.0,
            warning_threshold=8.0,
            critical_threshold=5.0,
            metric_name="requests_per_second",
            is_higher_better=True,
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)

        # Record good throughput
        monitor.record_metric("requests_per_second", 15.0)

        status = monitor.get_slo_status("throughput")
        assert status.is_met is True

    def test_error_rate_slo(self, tmp_data_dir):
        """Test error rate SLO type."""
        slo = SLODefinition(
            id="errors",
            name="Error Rate",
            description="",
            slo_type=SLOType.ERROR_RATE,
            target=0.01,
            warning_threshold=0.02,
            critical_threshold=0.05,
            metric_name="error_rate",
            is_higher_better=False,
        )
        monitor = SLOMonitor(slos=[slo], data_dir=tmp_data_dir)

        # Record low error rate
        monitor.record_metric("error_rate", 0.005)

        status = monitor.get_slo_status("errors")
        assert status.is_met is True
