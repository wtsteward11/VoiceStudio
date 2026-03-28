"""Runtime honesty: engine telemetry must not return fabricated metrics on failure."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(project_root))

from backend.api.routes import engine as engine_mod
from backend.ml.models.engine_service import get_engine_service


def test_telemetry_unavailable_returns_503_no_fake_values():
    """When the engine service raises, response is 503 with TELEMETRY_UNAVAILABLE — no 12.3/42.0."""
    app = FastAPI()
    app.include_router(engine_mod.router)

    class FailingService:
        def get_engine_stats(self, engine_id=None):
            raise RuntimeError("service unavailable")

    app.dependency_overrides[get_engine_service] = lambda: FailingService()
    client = TestClient(app)
    response = client.get("/api/engine/telemetry")
    assert response.status_code == 503
    body = response.json()
    detail = body.get("detail")
    assert isinstance(detail, dict)
    assert detail.get("code") == "TELEMETRY_UNAVAILABLE"
    assert detail.get("available") is False
    assert "12.3" not in response.text and "42.0" not in response.text


def test_telemetry_success_returns_service_values():
    """Happy path: metrics come from the engine service (no hardcoded fallbacks)."""

    class FakeService:
        def get_engine_stats(self, engine_id=None):
            if engine_id:
                return {
                    "avg_synthesis_time_ms": 7.5,
                    "underruns": 3,
                    "vram_usage_percent": 22.25,
                }
            return {
                "e1": {
                    "avg_synthesis_time_ms": 10.0,
                    "underruns": 1,
                    "vram_usage_percent": 30.0,
                },
                "e2": {
                    "avg_synthesis_time_ms": 20.0,
                    "underruns": 2,
                    "vram_usage_percent": 50.0,
                },
            }

    app = FastAPI()
    app.include_router(engine_mod.router)
    app.dependency_overrides[get_engine_service] = lambda: FakeService()
    client = TestClient(app)

    r1 = client.get("/api/engine/telemetry?engine_id=my_engine")
    assert r1.status_code == 200
    d1 = r1.json()
    assert d1["engine_ms"] == 7.5
    assert d1["underruns"] == 3
    assert d1["vram_pct"] == 22.25

    r2 = client.get("/api/engine/telemetry")
    assert r2.status_code == 200
    d2 = r2.json()
    assert d2["engine_ms"] == pytest.approx(15.0)
    assert d2["underruns"] == 3
    assert d2["vram_pct"] == pytest.approx(40.0)
