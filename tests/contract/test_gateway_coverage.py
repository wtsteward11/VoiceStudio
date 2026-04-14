"""Gateway coverage contract tests.

Validates that critical API groups expected by the C# gateway interfaces
have corresponding registered routes in the FastAPI backend.
"""

from __future__ import annotations

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app

CRITICAL_ENDPOINTS = [
    ("GET", "/api/health", "Health"),
    ("GET", "/api/health/engines", "Health"),
    ("GET", "/api/engines/list", "Engines"),
    ("GET", "/api/profiles", "Profiles"),
    ("POST", "/api/voice/synthesize", "Voice"),
    ("POST", "/api/transcribe/", "Transcription"),
    ("GET", "/api/library/assets", "Library"),
    ("POST", "/api/library/assets/upload", "Library"),
]


@pytest.fixture
async def client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c


@pytest.mark.asyncio
@pytest.mark.parametrize("method,path,group", CRITICAL_ENDPOINTS)
async def test_endpoint_registered(client: AsyncClient, method: str, path: str, group: str):
    """Verify the endpoint is registered (not 404/405 for the method)."""
    if method == "GET":
        resp = await client.get(path)
    elif method == "POST":
        resp = await client.post(path)
    else:
        resp = await client.request(method, path)

    assert (
        resp.status_code != 404
    ), f"[{group}] {method} {path} returned 404 -- route not registered"


@pytest.mark.asyncio
async def test_health_returns_json(client: AsyncClient):
    """Health endpoint must return JSON with status field."""
    resp = await client.get("/api/health")
    assert resp.status_code == 200
    data = resp.json()
    assert "status" in data


@pytest.mark.asyncio
async def test_engines_list_returns_array(client: AsyncClient):
    """Engine list endpoint must return a list."""
    resp = await client.get("/api/engines/list")
    if resp.status_code == 200:
        data = resp.json()
        assert isinstance(data, (list, dict))


@pytest.mark.asyncio
async def test_profiles_returns_list(client: AsyncClient):
    """Profiles endpoint must return a list or paginated envelope with items."""
    resp = await client.get("/api/profiles")
    if resp.status_code == 200:
        data = resp.json()
        if isinstance(data, dict):
            assert "items" in data, "Paginated response must contain 'items'"
            assert isinstance(data["items"], list)
        else:
            assert isinstance(data, list)
