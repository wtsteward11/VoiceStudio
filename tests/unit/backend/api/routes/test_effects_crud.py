"""
Effect Chain CRUD route tests — path-style API (/api/effects/chains/{project_id}/...).

Proves the CRUD endpoints work against the real JsonFileStore
via FastAPI TestClient (ASGITransport, no real HTTP server).

NOTE: The path-style list endpoint (GET /api/effects/chains/{project_id}) has a route
conflict with the query-param get-by-id (GET /api/effects/chains/{chain_id}) — both
match the same single-segment pattern, and the first registered wins. This is a known
routing issue. The query-param list endpoint (GET /api/effects/chains?project_id=...)
is used instead for list operations. All other path-style routes (2-segment paths)
work correctly.

Run: python -m pytest tests/unit/backend/api/routes/test_effects_crud.py -v
"""
from __future__ import annotations

import uuid

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app
from backend.api.optimization import invalidate_api_response_cache

TEST_PROJECT_ID = f"slice6-effects-{uuid.uuid4().hex[:8]}"
BASE = f"/api/effects/chains/{TEST_PROJECT_ID}"


@pytest.fixture(autouse=True)
def clear_cache():
    """Clear the response cache before each test to avoid stale list results."""
    invalidate_api_response_cache()
    yield
    invalidate_api_response_cache()


@pytest.fixture
async def client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c


@pytest.fixture
async def created_chain(client: AsyncClient):
    """Create a chain and yield its data; delete it after the test."""
    resp = await client.post(BASE, json={"name": "Fixture Chain", "effects": []})
    assert resp.status_code == 200, f"Fixture chain creation failed: {resp.text}"
    data = resp.json()
    yield data
    await client.delete(f"{BASE}/{data['id']}")


@pytest.mark.asyncio
async def test_list_empty(client: AsyncClient) -> None:
    """GET /api/effects/chains?project_id=... returns 200 with an empty list for a fresh project."""
    unique_project = f"slice6-empty-{uuid.uuid4().hex[:8]}"
    resp = await client.get(f"/api/effects/chains?project_id={unique_project}")
    assert resp.status_code == 200
    assert resp.json() == []


@pytest.mark.asyncio
async def test_create_chain(client: AsyncClient) -> None:
    """POST /{project_id} creates a chain with server-assigned id."""
    resp = await client.post(
        BASE, json={"name": "Test Chain", "description": "desc", "effects": []}
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["name"] == "Test Chain"
    assert data["description"] == "desc"
    assert data["project_id"] == TEST_PROJECT_ID
    assert "id" in data
    assert "created" in data
    assert "modified" in data

    await client.delete(f"{BASE}/{data['id']}")


@pytest.mark.asyncio
async def test_get_chain(client: AsyncClient, created_chain: dict) -> None:
    """GET /{project_id}/{chain_id} returns the previously created chain."""
    chain_id = created_chain["id"]
    resp = await client.get(f"{BASE}/{chain_id}")
    assert resp.status_code == 200
    data = resp.json()
    assert data["id"] == chain_id
    assert data["name"] == "Fixture Chain"
    assert data["project_id"] == TEST_PROJECT_ID


@pytest.mark.asyncio
async def test_update_chain(client: AsyncClient, created_chain: dict) -> None:
    """PUT /{project_id}/{chain_id} updates the chain name."""
    chain_id = created_chain["id"]
    resp = await client.put(
        f"{BASE}/{chain_id}", json={"name": "Updated Chain"}
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["name"] == "Updated Chain"
    assert data["id"] == chain_id


@pytest.mark.asyncio
async def test_delete_chain(client: AsyncClient) -> None:
    """DELETE /{project_id}/{chain_id} removes the chain."""
    create_resp = await client.post(
        BASE, json={"name": "To Delete", "effects": []}
    )
    assert create_resp.status_code == 200
    chain_id = create_resp.json()["id"]

    del_resp = await client.delete(f"{BASE}/{chain_id}")
    assert del_resp.status_code == 200
    assert del_resp.json() == {"success": True}

    get_resp = await client.get(f"{BASE}/{chain_id}")
    assert get_resp.status_code == 404


@pytest.mark.asyncio
async def test_full_crud_lifecycle(client: AsyncClient) -> None:
    """Full lifecycle: create -> get -> update (verify from response) -> delete."""
    create_resp = await client.post(
        BASE, json={"name": "Lifecycle Chain", "description": "original", "effects": []}
    )
    assert create_resp.status_code == 200
    chain = create_resp.json()
    chain_id = chain["id"]
    assert chain["name"] == "Lifecycle Chain"
    assert chain["project_id"] == TEST_PROJECT_ID

    get_resp = await client.get(f"{BASE}/{chain_id}")
    assert get_resp.status_code == 200
    assert get_resp.json()["name"] == "Lifecycle Chain"

    update_resp = await client.put(
        f"{BASE}/{chain_id}", json={"name": "Updated Lifecycle", "description": "modified"}
    )
    assert update_resp.status_code == 200
    updated = update_resp.json()
    assert updated["name"] == "Updated Lifecycle"
    assert updated["description"] == "modified"
    assert updated["id"] == chain_id

    del_resp = await client.delete(f"{BASE}/{chain_id}")
    assert del_resp.status_code == 200
    assert del_resp.json() == {"success": True}


@pytest.mark.asyncio
async def test_create_empty_name_returns_400(client: AsyncClient) -> None:
    """POST with empty name returns 400."""
    resp = await client.post(BASE, json={"name": "", "effects": []})
    assert resp.status_code == 400


@pytest.mark.asyncio
async def test_get_nonexistent_chain_returns_404(client: AsyncClient) -> None:
    """GET with a nonexistent chain_id returns 404."""
    fake_id = str(uuid.uuid4())
    resp = await client.get(f"{BASE}/{fake_id}")
    assert resp.status_code == 404
