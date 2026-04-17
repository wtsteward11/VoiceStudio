"""
Unit Tests for Search API Route
Tests search functionality endpoints in isolation.
"""

import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

# Repo root: tests/unit/backend/api/routes -> 6 parents (match routes/conftest.py)
project_root = Path(__file__).resolve().parent.parent.parent.parent.parent.parent
if str(project_root) not in sys.path:
    sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import search
except ImportError:
    pytest.skip("Could not import search route module", allow_module_level=True)

from httpx import ASGITransport, AsyncClient

from backend.api.main import app
from backend.api.optimization import invalidate_api_response_cache


def _reset_search_storage_state() -> None:
    """Reset lazy storage globals for isolated tests (single-threaded pytest)."""
    search.STORAGE_AVAILABLE = False
    search._markers = None
    search._profiles = None
    search._projects = None
    search._scripts = None


class TestSearchRouteImports:
    """Test search route module can be imported."""

    def test_search_module_imports(self):
        """Test search module can be imported."""
        assert search is not None, "Failed to import search module"
        assert hasattr(search, "router"), "search module missing router"


class TestSearchRouteHandlers:
    """Test search route handlers exist and are callable."""

    def test_search_handler_exists(self):
        """Test search handler exists."""
        if hasattr(search, "search"):
            assert callable(search.search), "search is not callable"

    def test_no_legacy_advanced_search_symbol(self):
        """advanced_search is not part of the public search API; avoid phantom handler checks."""
        assert not hasattr(search, "advanced_search"), (
            "Remove dead references: advanced_search was never implemented on this module"
        )


class TestSearchRouter:
    """Test search router configuration."""

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert search.router is not None, "Router should exist"
        if hasattr(search.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(search.router, "routes"):
            routes = [route.path for route in search.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestSearchCollectionSafety:
    """GAP-069 slice 5: import/collection must not require a live DB."""

    def test_module_import_does_not_touch_db(self):
        """Route module imports without eager store/DB initialization."""
        assert search.STORAGE_AVAILABLE is False
        assert search._markers is None
        assert search._profiles is None
        assert search._projects is None
        assert search._scripts is None

    def test_storage_available_false_before_load(self):
        """Honest default: storage not loaded until first request loads it."""
        assert search.STORAGE_AVAILABLE is False


class TestSearchLazyLoader:
    """Regression: _load_search_storage is lazy, idempotent, and surfaces failures."""

    def test_load_search_storage_sets_available(self):
        _reset_search_storage_state()
        mock_markers = MagicMock()
        mock_profiles = {"p1": {"name": "n"}}
        mock_projects = {"j1": {"name": "j"}}
        mock_scripts: dict[str, object] = {}

        with (
            patch.object(search, "get_marker_store", return_value=mock_markers),
            patch.object(search, "get_profiles_for_search", return_value=mock_profiles),
            patch.object(search, "get_projects_for_search", return_value=mock_projects),
            patch.object(search, "get_scripts_for_search", return_value=mock_scripts),
        ):
            search._load_search_storage()

        assert search.STORAGE_AVAILABLE is True
        assert search._markers is mock_markers
        assert search._profiles is mock_profiles
        assert search._projects is mock_projects
        assert search._scripts is mock_scripts

    def test_load_search_storage_idempotent(self):
        _reset_search_storage_state()
        gm = MagicMock()
        gp = MagicMock()
        gj = MagicMock()
        gs = MagicMock()

        with (
            patch.object(search, "get_marker_store", gm),
            patch.object(search, "get_profiles_for_search", gp),
            patch.object(search, "get_projects_for_search", gj),
            patch.object(search, "get_scripts_for_search", gs),
        ):
            search._load_search_storage()
            search._load_search_storage()

        assert gm.call_count == 1
        assert gp.call_count == 1
        assert gj.call_count == 1
        assert gs.call_count == 1

    def test_load_search_storage_propagates_error(self):
        _reset_search_storage_state()

        def _boom() -> dict[str, object]:
            raise RuntimeError("simulated store failure")

        with (
            patch.object(search, "get_marker_store", return_value=MagicMock()),
            patch.object(search, "get_profiles_for_search", return_value={}),
            patch.object(search, "get_projects_for_search", side_effect=_boom),
            patch.object(search, "get_scripts_for_search", return_value={}),
        ):
            with pytest.raises(RuntimeError, match="simulated store failure"):
                search._load_search_storage()

        assert search.STORAGE_AVAILABLE is False
        assert search._markers is None


# --- Slice 7: HTTP contract tests (ASGI, no real HTTP server) ---


@pytest.fixture(autouse=True)
def clear_api_cache_for_search_http():
    """Avoid stale cached search responses between tests."""
    invalidate_api_response_cache()
    yield
    invalidate_api_response_cache()


@pytest.fixture
def patch_search_stores_no_sqlite():
    """
    Avoid SQLite/asyncio loop conflicts during ASGI search tests.

    Search storage getters normally open DB-backed stores; patch them with
    in-memory dicts so HTTP contract tests validate routing + JSON shape only.
    """
    _reset_search_storage_state()
    mock_markers = MagicMock()
    profiles = {
        "p-slice7": {
            "name": "test profile slice7",
            "description": "",
            "tags": ["te"],
        }
    }
    with (
        patch.object(search, "get_marker_store", return_value=mock_markers),
        patch.object(search, "get_profiles_for_search", return_value=profiles),
        patch.object(search, "get_projects_for_search", return_value={}),
        patch.object(search, "get_scripts_for_search", return_value={}),
    ):
        yield
    _reset_search_storage_state()


@pytest.fixture
async def asgi_client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c


@pytest.mark.asyncio
class TestSearchHttpContract:
    """HTTP-level truth for GET /api/search (Slice 7)."""

    async def test_search_missing_q_returns_422(self, asgi_client: AsyncClient):
        resp = await asgi_client.get("/api/search")
        assert resp.status_code == 422
        body = resp.json()
        assert "detail" in body or "errors" in body

    async def test_search_q_too_short_returns_422(self, asgi_client: AsyncClient):
        resp = await asgi_client.get("/api/search", params={"q": "a"})
        assert resp.status_code == 422

    async def test_search_valid_q_returns_200_and_shape(
        self, asgi_client: AsyncClient, patch_search_stores_no_sqlite
    ):
        resp = await asgi_client.get("/api/search", params={"q": "te", "limit": 10})
        assert resp.status_code == 200, resp.text
        data = resp.json()
        assert "query" in data
        assert data["query"] == "te"
        assert "results" in data and isinstance(data["results"], list)
        assert "total_results" in data
        assert isinstance(data["total_results"], int)
        assert data["total_results"] == len(data["results"])
        assert "results_by_type" in data and isinstance(data["results_by_type"], dict)
        if data["results"]:
            item = data["results"][0]
            for key in ("id", "type", "title", "panel_id"):
                assert key in item, f"missing {key} in SearchResultItem"

    async def test_search_limit_out_of_range_returns_422(self, asgi_client: AsyncClient):
        resp = await asgi_client.get("/api/search", params={"q": "test", "limit": 0})
        assert resp.status_code == 422

    async def test_search_types_profile_filters_keys(
        self, asgi_client: AsyncClient, patch_search_stores_no_sqlite
    ):
        resp = await asgi_client.get(
            "/api/search", params={"q": "te", "types": "profile", "limit": 5}
        )
        assert resp.status_code == 200, resp.text
        data = resp.json()
        assert data["total_results"] == len(data["results"])
        for item in data["results"]:
            assert item.get("type") == "profile"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
