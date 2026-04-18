"""
Unit Tests for Library API Route

Tests library management endpoints against the real DB-backed
repository layer (BaseRepository + aiosqlite).
"""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.routes.library import router


def _make_client() -> TestClient:
    app = FastAPI()
    app.include_router(router)
    return TestClient(app, raise_server_exceptions=False)


class TestLibraryRouteImports:
    """Verify the library route module loads and exposes a router."""

    def test_router_exists(self):
        from backend.api.routes import library

        assert library.router is not None

    def test_router_has_routes(self):
        from backend.api.routes import library

        routes = [r.path for r in library.router.routes if hasattr(r, "path")]
        assert len(routes) > 0


class TestLibraryFoldersEndpoints:
    """Test GET /api/library/folders against the real DB."""

    def test_get_folders_returns_200(self):
        client = _make_client()
        r = client.get("/api/library/folders")
        assert r.status_code == 200

    def test_get_folders_response_shape(self):
        """Response must be an object with a 'folders' array (FoldersResponse)."""
        client = _make_client()
        r = client.get("/api/library/folders")
        data = r.json()
        assert isinstance(data, dict), "Response must be an object, not a raw array"
        assert "folders" in data, "Response must contain 'folders' key"
        assert isinstance(data["folders"], list)

    def test_get_folders_with_parent_id(self):
        client = _make_client()
        r = client.get("/api/library/folders?parent_id=nonexistent")
        assert r.status_code == 200
        data = r.json()
        assert data["folders"] == []


class TestLibraryAssetsEndpoints:
    """Test GET /api/library/assets against the real DB."""

    def test_search_assets_returns_200(self):
        client = _make_client()
        r = client.get("/api/library/assets")
        assert r.status_code == 200

    def test_search_assets_response_shape(self):
        """Response must be AssetSearchResponse with assets/total/limit/offset."""
        client = _make_client()
        r = client.get("/api/library/assets?limit=5")
        data = r.json()
        assert isinstance(data, dict)
        assert "assets" in data
        assert "total" in data
        assert "limit" in data
        assert "offset" in data
        assert isinstance(data["assets"], list)
        assert data["limit"] == 5
        assert data["offset"] == 0

    def test_search_assets_with_query(self):
        client = _make_client()
        r = client.get("/api/library/assets?query=nonexistent_xyz_query")
        assert r.status_code == 200
        data = r.json()
        assert isinstance(data["assets"], list)

    def test_search_assets_with_asset_type_filter(self):
        client = _make_client()
        r = client.get("/api/library/assets?asset_type=audio&limit=5")
        assert r.status_code == 200
        data = r.json()
        for asset in data["assets"]:
            assert asset["type"] == "audio"

    def test_search_assets_honest_empty_state(self):
        """When no assets match, total should be 0 and assets should be empty."""
        client = _make_client()
        r = client.get(
            "/api/library/assets?query=__absolutely_no_match_ever__&limit=10"
        )
        assert r.status_code == 200
        data = r.json()
        assert data["total"] == 0
        assert data["assets"] == []


class TestLibraryTypesEndpoint:
    """Test GET /api/library/types."""

    def test_get_types_returns_200(self):
        client = _make_client()
        r = client.get("/api/library/types")
        assert r.status_code == 200

    def test_get_types_response_shape(self):
        client = _make_client()
        r = client.get("/api/library/types")
        data = r.json()
        assert "types" in data
        assert isinstance(data["types"], list)
        assert len(data["types"]) > 0
        first = data["types"][0]
        assert "id" in first
        assert "name" in first

    def test_get_types_includes_audio(self):
        client = _make_client()
        r = client.get("/api/library/types")
        data = r.json()
        type_ids = [t["id"] for t in data["types"]]
        assert "audio" in type_ids


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
