"""
Observability Integration Tests.

Task 3.1: RED metrics, SLO config, health dashboard.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from tests.integration.test_backend.base import IntegrationTestBase, integration


class TestREDMetrics(IntegrationTestBase):
    """Tests for RED metrics collector."""

    @integration
    def test_red_metrics_record_and_export(self):
        """Test recording and exporting RED metrics."""
        from backend.platform.monitoring.red_metrics import (
            get_red_metrics,
        )

        collector = get_red_metrics()
        collector.record("voice", 0.5, is_error=False)
        collector.record("voice", 1.2, is_error=True)
        collector.record("audio", 0.1, is_error=False)

        all_metrics = collector.get_all()
        assert "voice" in all_metrics
        assert all_metrics["voice"]["request_count"] == 2
        assert all_metrics["voice"]["error_count"] == 1
        assert all_metrics["voice"]["error_rate"] == 0.5
        assert "audio" in all_metrics


class TestSLOConfig(IntegrationTestBase):
    """Tests for SLO configuration."""

    @integration
    def test_slos_json_exists_and_valid(self):
        """Test config/slos.json exists and is valid JSON."""
        slos_path = Path(__file__).resolve().parent.parent.parent / "config" / "slos.json"
        assert slos_path.exists(), f"Expected {slos_path} to exist"
        data = json.loads(slos_path.read_text(encoding="utf-8"))
        assert "slos" in data
        assert len(data["slos"]) >= 1
        slo = data["slos"][0]
        assert "id" in slo
        assert "name" in slo
        assert "target" in slo


@pytest.mark.asyncio
class TestHealthDashboard(IntegrationTestBase):
    """Tests for health dashboard endpoint."""

    @integration
    async def test_health_dashboard_returns_expected_structure(self):
        """Test GET /api/health/dashboard returns expected structure."""
        from httpx import ASGITransport, AsyncClient

        from backend.api.main import _register_all_routes, app

        _register_all_routes()

        async with AsyncClient(
            transport=ASGITransport(app=app),
            base_url="http://test",
        ) as client:
            resp = await client.get("/api/health/dashboard")
        assert resp.status_code == 200
        data = resp.json()
        assert "timestamp" in data
        assert "red_metrics" in data
        assert "slo_compliance" in data
        assert "engines" in data
        assert "active_alerts" in data
        assert "overall" in data
