"""
Health and Dashboard Contract Tests.

Task 3.2: Validates health and dashboard responses against JSON schemas.
"""

from __future__ import annotations

import json
from pathlib import Path

import jsonschema
import pytest

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
CONTRACTS_DIR = PROJECT_ROOT / "shared" / "contracts"

pytestmark = pytest.mark.contract


def _load_schema(name: str) -> dict:
    """Load schema from shared/contracts."""
    path = CONTRACTS_DIR / f"{name}.schema.json"
    if not path.exists():
        pytest.skip(f"Schema not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


class TestHealthDashboardContract:
    """Validate health dashboard response against schema."""

    def test_health_dashboard_matches_schema(self, contract_client):
        """GET /api/health/dashboard response matches health_dashboard_response schema."""
        schema = _load_schema("health_dashboard_response")
        resp = contract_client.get("/api/health/dashboard")
        assert resp.status_code == 200, f"Expected 200, got {resp.status_code}"
        data = resp.json()
        jsonschema.validate(instance=data, schema=schema)

    def test_health_summary_has_required_fields(self, contract_client):
        """GET /api/health/summary has expected structure."""
        resp = contract_client.get("/api/health/summary")
        assert resp.status_code == 200
        data = resp.json()
        assert "timestamp" in data
        assert "engines" in data
        assert "overall" in data
