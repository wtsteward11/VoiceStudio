"""Tests for /health endpoint baseline dependency and interpreter fields."""

import sys

from fastapi.testclient import TestClient

from backend.api.main import app

client = TestClient(app, raise_server_exceptions=False)


class TestHealthBaselineFields:
    """The root /health endpoint must expose runtime authority diagnostics."""

    def test_health_returns_baseline_deps_valid(self):
        resp = client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert "baseline_deps_valid" in data
        assert isinstance(data["baseline_deps_valid"], bool)

    def test_health_returns_python_executable(self):
        resp = client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert "python_executable" in data
        assert data["python_executable"] == sys.executable

    def test_health_returns_python_version(self):
        resp = client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert "python_version" in data
        assert data["python_version"] == sys.version.split()[0]

    def test_health_baseline_valid_in_test_env(self):
        """In the test environment, all baseline deps should be present."""
        resp = client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert data["baseline_deps_valid"] is True
        assert data["status"] == "ok"

    def test_health_no_failures_field_when_valid(self):
        """When baseline deps are valid, failures list should be absent."""
        resp = client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert data["baseline_deps_valid"] is True
        assert "baseline_deps_failures" not in data
