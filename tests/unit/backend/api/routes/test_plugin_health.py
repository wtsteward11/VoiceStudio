"""
Unit tests for plugin health REST API.

Phase 5D M3: Plugin health REST endpoints for metrics and audit visualization.
"""

from __future__ import annotations

import sys
from datetime import datetime
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from backend.api.routes.plugin_health import (
        ExportFormat,
        HealthStatus,
        MetricRecord,
        MetricSummary,
        PluginHealthResponse,
        SystemHealthResponse,
        router,
    )
except ImportError:
    pytest.skip("Could not import plugin_health route module", allow_module_level=True)


@pytest.fixture
def app() -> FastAPI:
    """Create a test app with the plugin health router."""
    app = FastAPI()
    app.include_router(router)
    return app


@pytest.fixture
def client(app: FastAPI) -> TestClient:
    """Create a test client."""
    return TestClient(app)


class TestHealthStatus:
    """Tests for HealthStatus enum."""

    def test_all_statuses_defined(self) -> None:
        """All health statuses should be defined."""
        statuses = [s.value for s in HealthStatus]
        assert "healthy" in statuses
        assert "degraded" in statuses
        assert "unhealthy" in statuses
        assert "unknown" in statuses


class TestResponseModels:
    """Tests for response models."""

    def test_metric_summary(self) -> None:
        """MetricSummary should have correct structure."""
        summary = MetricSummary(
            count=100,
            sum=500.0,
            min=1.0,
            max=10.0,
            avg=5.0,
        )
        assert summary.count == 100
        assert summary.avg == 5.0

    def test_plugin_health_response(self) -> None:
        """PluginHealthResponse should have correct structure."""
        response = PluginHealthResponse(
            plugin_id="test-plugin",
            status=HealthStatus.HEALTHY,
            uptime_seconds=3600.0,
            total_calls=1000,
            total_errors=5,
            error_rate=0.5,
            avg_latency_ms=50.0,
        )
        assert response.plugin_id == "test-plugin"
        assert response.status == HealthStatus.HEALTHY
        assert response.error_rate == 0.5

    def test_system_health_response(self) -> None:
        """SystemHealthResponse should have correct structure."""
        response = SystemHealthResponse(
            total_plugins=10,
            healthy_plugins=8,
            degraded_plugins=1,
            unhealthy_plugins=1,
            total_calls=10000,
            timestamp=datetime.now().isoformat(),
        )
        assert response.total_plugins == 10
        assert response.healthy_plugins == 8

    def test_metric_record(self) -> None:
        """MetricRecord should have correct structure."""
        record = MetricRecord(
            id=1,
            plugin_id="test-plugin",
            metric_type="execution.duration_ms",
            value=123.45,
            timestamp="2025-01-01T12:00:00",
            labels={"method": "process"},
        )
        assert record.plugin_id == "test-plugin"
        assert record.value == 123.45


class TestSystemHealthEndpoint:
    """Tests for /system endpoint."""

    def test_get_system_health_no_aggregator(self, client: TestClient) -> None:
        """Should return empty response when aggregator not available."""
        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/system")
            assert response.status_code == 200
            data = response.json()
            assert data["total_plugins"] == 0
            assert "timestamp" in data

    def test_get_system_health_with_aggregator(self, client: TestClient) -> None:
        """Should return health data when aggregator available."""
        mock_health = MagicMock()
        mock_health.total_plugins = 5
        mock_health.healthy_plugins = 4
        mock_health.degraded_plugins = 1
        mock_health.unhealthy_plugins = 0
        mock_health.total_calls = 1000
        mock_health.total_errors = 10
        mock_health.system_error_rate = 1.0
        mock_health.avg_latency_ms = 25.0
        mock_health.total_memory_bytes = 104857600

        mock_aggregator = MagicMock()
        mock_aggregator.get_system_health.return_value = mock_health

        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=mock_aggregator,
        ):
            response = client.get("/api/plugins/health/system")
            assert response.status_code == 200
            data = response.json()
            assert data["total_plugins"] == 5
            assert data["healthy_plugins"] == 4
            assert data["total_calls"] == 1000


class TestPluginListEndpoint:
    """Tests for /plugins endpoint."""

    def test_list_plugins_empty(self, client: TestClient) -> None:
        """Should return empty list when no plugins."""
        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/plugins")
            assert response.status_code == 200
            assert response.json() == []

    def test_list_plugins_with_data(self, client: TestClient) -> None:
        """Should return list of plugin health."""
        mock_ph1 = MagicMock()
        mock_ph1.plugin_id = "plugin-1"
        mock_ph1.error_rate = 0.5
        mock_ph1.crash_count = 0
        mock_ph1.uptime_seconds = 3600.0
        mock_ph1.total_calls = 100
        mock_ph1.total_errors = 1
        mock_ph1.avg_latency_ms = 10.0
        mock_ph1.memory_bytes = 1024
        mock_ph1.last_active = datetime.now()

        mock_aggregator = MagicMock()
        mock_aggregator.get_all_plugin_health.return_value = [mock_ph1]

        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=mock_aggregator,
        ):
            response = client.get("/api/plugins/health/plugins")
            assert response.status_code == 200
            data = response.json()
            assert len(data) == 1
            assert data[0]["plugin_id"] == "plugin-1"


class TestPluginDetailEndpoint:
    """Tests for /plugins/{plugin_id} endpoint."""

    def test_get_plugin_not_found(self, client: TestClient) -> None:
        """Should return 404 when plugin not found."""
        mock_aggregator = MagicMock()
        mock_aggregator.get_plugin_health.return_value = None

        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=mock_aggregator,
        ):
            response = client.get("/api/plugins/health/plugins/nonexistent")
            assert response.status_code == 404

    def test_get_plugin_found(self, client: TestClient) -> None:
        """Should return plugin health when found."""
        mock_ph = MagicMock()
        mock_ph.plugin_id = "test-plugin"
        mock_ph.error_rate = 1.0
        mock_ph.crash_count = 0
        mock_ph.uptime_seconds = 7200.0
        mock_ph.total_calls = 500
        mock_ph.total_errors = 5
        mock_ph.avg_latency_ms = 15.0
        mock_ph.memory_bytes = 2048
        mock_ph.last_active = datetime.now()

        mock_aggregator = MagicMock()
        mock_aggregator.get_plugin_health.return_value = mock_ph

        with (
            patch(
                "backend.api.routes.plugin_health._get_metrics_aggregator",
                return_value=mock_aggregator,
            ),
            patch(
                "backend.api.routes.plugin_health._get_persistence",
                return_value=None,
            ),
        ):
            response = client.get("/api/plugins/health/plugins/test-plugin")
            assert response.status_code == 200
            data = response.json()
            assert data["plugin_id"] == "test-plugin"
            assert data["total_calls"] == 500


class TestUnhealthyEndpoint:
    """Tests for /unhealthy endpoint."""

    def test_no_unhealthy_plugins(self, client: TestClient) -> None:
        """Should return empty list when all plugins healthy."""
        mock_aggregator = MagicMock()
        mock_aggregator.get_unhealthy_plugins.return_value = []

        with patch(
            "backend.api.routes.plugin_health._get_metrics_aggregator",
            return_value=mock_aggregator,
        ):
            response = client.get("/api/plugins/health/unhealthy")
            assert response.status_code == 200
            assert response.json() == []


class TestMetricsQueryEndpoint:
    """Tests for /metrics endpoint."""

    def test_query_metrics_no_persistence(self, client: TestClient) -> None:
        """Should return empty when persistence not available."""
        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/metrics")
            assert response.status_code == 200
            data = response.json()
            assert data["count"] == 0
            assert data["metrics"] == []

    def test_query_metrics_with_data(self, client: TestClient) -> None:
        """Should return metrics from persistence."""
        mock_record = MagicMock()
        mock_record.id = 1
        mock_record.plugin_id = "plugin-1"
        mock_record.metric_type = "execution.duration_ms"
        mock_record.value = 100.0
        mock_record.timestamp = datetime.now()
        mock_record.labels = {}
        mock_record.session_id = "session-1"

        mock_persistence = MagicMock()
        mock_persistence.query_metrics.return_value = [mock_record]

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get("/api/plugins/health/metrics")
            assert response.status_code == 200
            data = response.json()
            assert data["count"] == 1
            assert data["metrics"][0]["plugin_id"] == "plugin-1"

    def test_query_metrics_with_filters(self, client: TestClient) -> None:
        """Should pass filters to persistence."""
        mock_persistence = MagicMock()
        mock_persistence.query_metrics.return_value = []

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get(
                "/api/plugins/health/metrics",
                params={
                    "plugin_id": "test-plugin",
                    "metric_type": "execution.count",
                    "limit": 50,
                },
            )
            assert response.status_code == 200
            mock_persistence.query_metrics.assert_called_once()


class TestAggregatedMetricsEndpoint:
    """Tests for /metrics/aggregated endpoint."""

    def test_aggregated_no_persistence(self, client: TestClient) -> None:
        """Should return empty when persistence not available."""
        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/metrics/aggregated")
            assert response.status_code == 200
            data = response.json()
            assert data["aggregations"] == []


class TestMetricsExportEndpoint:
    """Tests for /metrics/export endpoint."""

    def test_export_no_persistence(self, client: TestClient) -> None:
        """Should return 503 when persistence not available."""
        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/metrics/export")
            assert response.status_code == 503

    def test_export_json(self, client: TestClient) -> None:
        """Should export JSON format."""
        mock_persistence = MagicMock()
        mock_persistence.export_json.return_value = '{"metrics": []}'

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get(
                "/api/plugins/health/metrics/export",
                params={"format": "json"},
            )
            assert response.status_code == 200
            assert response.headers["content-type"] == "application/json"

    def test_export_csv(self, client: TestClient) -> None:
        """Should export CSV format."""
        mock_persistence = MagicMock()
        mock_persistence.export_csv.return_value = "id,plugin_id,value\n1,plugin-1,100"

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get(
                "/api/plugins/health/metrics/export",
                params={"format": "csv"},
            )
            assert response.status_code == 200
            assert "text/csv" in response.headers["content-type"]

    def test_export_prometheus(self, client: TestClient) -> None:
        """Should export Prometheus format."""
        mock_persistence = MagicMock()
        mock_persistence.export_prometheus.return_value = "# HELP metric\nmetric 100"

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get(
                "/api/plugins/health/metrics/export",
                params={"format": "prometheus"},
            )
            assert response.status_code == 200
            assert "text/plain" in response.headers["content-type"]


class TestStorageStatsEndpoint:
    """Tests for /metrics/storage endpoint."""

    def test_storage_stats_no_persistence(self, client: TestClient) -> None:
        """Should return empty stats when persistence not available."""
        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/metrics/storage")
            assert response.status_code == 200
            data = response.json()
            assert data["total_records"] == 0

    def test_storage_stats_with_data(self, client: TestClient) -> None:
        """Should return storage stats."""
        mock_persistence = MagicMock()
        mock_persistence.get_storage_stats.return_value = {
            "total_records": 1000,
            "file_size_bytes": 1048576,
            "file_size_mb": 1.0,
            "date_range": {"min": "2025-01-01", "max": "2025-01-15"},
            "records_by_plugin": {"plugin-1": 500},
            "records_by_type": {"execution.duration_ms": 1000},
            "retention_policy": "30_days",
        }

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.get("/api/plugins/health/metrics/storage")
            assert response.status_code == 200
            data = response.json()
            assert data["total_records"] == 1000
            assert data["file_size_mb"] == 1.0


class TestPruneEndpoint:
    """Tests for /metrics/prune endpoint."""

    def test_prune_no_persistence(self, client: TestClient) -> None:
        """Should return 503 when persistence not available."""
        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=None,
        ):
            response = client.post("/api/plugins/health/metrics/prune")
            assert response.status_code == 503

    def test_prune_success(self, client: TestClient) -> None:
        """Should prune old metrics."""
        mock_persistence = MagicMock()
        mock_persistence.apply_retention_policy.return_value = 100

        with patch(
            "backend.api.routes.plugin_health._get_persistence",
            return_value=mock_persistence,
        ):
            response = client.post("/api/plugins/health/metrics/prune")
            assert response.status_code == 200
            data = response.json()
            assert data["deleted_records"] == 100


class TestAuditEndpoints:
    """Tests for audit log endpoints."""

    def test_query_audit_no_logger(self, client: TestClient) -> None:
        """Should return empty when audit logger not available."""
        with patch(
            "backend.api.routes.plugin_health._get_audit_logger",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/audit")
            assert response.status_code == 200
            data = response.json()
            assert data["count"] == 0
            assert data["events"] == []

    def test_query_audit_with_events(self, client: TestClient) -> None:
        """Should return audit events."""
        mock_event = MagicMock()
        mock_event.id = 1
        mock_event.timestamp = datetime.now()
        mock_event.event_type = MagicMock(value="PLUGIN_INSTALLED")
        mock_event.plugin_id = "plugin-1"
        mock_event.severity = MagicMock(value="INFO")
        mock_event.message = "Plugin installed"
        mock_event.details = {}
        mock_event.actor = "system"

        mock_logger = MagicMock()
        mock_logger.query_events.return_value = [mock_event]

        with patch(
            "backend.api.routes.plugin_health._get_audit_logger",
            return_value=mock_logger,
        ):
            response = client.get("/api/plugins/health/audit")
            assert response.status_code == 200
            data = response.json()
            assert data["count"] == 1
            assert data["events"][0]["plugin_id"] == "plugin-1"

    def test_get_plugin_audit_trail(self, client: TestClient) -> None:
        """Should get audit trail for specific plugin."""
        mock_logger = MagicMock()
        mock_logger.query_events.return_value = []

        with patch(
            "backend.api.routes.plugin_health._get_audit_logger",
            return_value=mock_logger,
        ):
            response = client.get("/api/plugins/health/audit/plugins/test-plugin")
            assert response.status_code == 200

    def test_get_recent_events(self, client: TestClient) -> None:
        """Should get recent audit events."""
        mock_logger = MagicMock()
        mock_logger.query_events.return_value = []

        with patch(
            "backend.api.routes.plugin_health._get_audit_logger",
            return_value=mock_logger,
        ):
            response = client.get("/api/plugins/health/audit/recent")
            assert response.status_code == 200

    def test_get_audit_summary(self, client: TestClient) -> None:
        """Should get audit summary."""
        with patch(
            "backend.api.routes.plugin_health._get_audit_logger",
            return_value=None,
        ):
            response = client.get("/api/plugins/health/audit/summary")
            assert response.status_code == 200
            data = response.json()
            assert data["total_events"] == 0
