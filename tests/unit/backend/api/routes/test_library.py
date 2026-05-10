"""
Unit Tests for Library API Route

Tests library management endpoints against the real DB-backed
repository layer (BaseRepository + aiosqlite).
"""

import asyncio
import io
import os
import wave

import aiosqlite
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import backend.settings as backend_settings
from backend.api.routes.library import router
from backend.data.migrations.v003_library_tables import LibraryTablesMigration
from backend.data.repositories.library_repository import reset_library_repositories


@pytest.fixture(autouse=True)
def _library_route_isolated_sqlite(tmp_path, monkeypatch):
    """Use an isolated SQLite file with v003 library tables (CI-safe).

    The route tests mount only ``library.router`` on a bare FastAPI app, so
    production lifespan migrations never run. Without tables, SQLite raises
    ``OperationalError`` and the route maps that to HTTP 503. This fixture
    applies the canonical v003 schema and refreshes settings + repo singletons.
    """
    db_path = tmp_path / "library_routes_unit.sqlite"
    monkeypatch.setenv("VOICESTUDIO_DB_PATH", str(db_path))
    backend_settings.get_config.cache_clear()
    monkeypatch.setattr(backend_settings, "config", backend_settings.get_config())

    async def _apply_schema() -> None:
        async with aiosqlite.connect(str(db_path)) as conn:
            migration = LibraryTablesMigration()
            await migration.upgrade(conn)

    asyncio.run(_apply_schema())
    reset_library_repositories()
    yield
    reset_library_repositories()
    backend_settings.get_config.cache_clear()


def _make_client() -> TestClient:
    app = FastAPI()
    app.include_router(router)
    return TestClient(app, raise_server_exceptions=False)


def _wav_bytes() -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(16000)
        wf.writeframes((1000).to_bytes(2, "little", signed=True) * 1600)
    return buf.getvalue()


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

    def test_upload_asset_preserves_generated_audio_provenance(self, tmp_path, monkeypatch):
        """Generated-audio closure metadata must be persisted in library asset metadata."""
        from backend.api.routes import library

        monkeypatch.setattr(library, "get_path", lambda _name: tmp_path)
        client = _make_client()
        response = client.post(
            "/api/library/assets/upload",
            params={
                "project_id": "project-123",
                "session_id": "session-123",
                "generated_audio_id": "ga-123",
                "source_engine": "xtts_v2",
                "routed_engine": "xtts_v2",
                "profile_id": "profile-123",
            },
            files={"file": ("generated.wav", _wav_bytes(), "audio/wav")},
        )
        assert response.status_code == 201, response.text
        data = response.json()
        assert data["audio_id"]
        assert data["metadata"]["project_id"] == "project-123"
        assert data["metadata"]["session_id"] == "session-123"
        assert data["metadata"]["generated_audio_id"] == "ga-123"
        assert data["metadata"]["source_engine"] == "xtts_v2"
        assert data["metadata"]["routed_engine"] == "xtts_v2"
        assert data["metadata"]["profile_id"] == "profile-123"

    def test_upload_asset_audio_id_resolves_for_transcription(self, tmp_path, monkeypatch):
        """Library upload_id (audio_id) must resolve to on-disk WAV for STT."""
        from backend.api.routes import library
        from backend.services import audio_path_resolver

        monkeypatch.setattr(library, "get_path", lambda _name: tmp_path)
        monkeypatch.setattr(audio_path_resolver, "get_path", lambda _name: tmp_path)

        client = _make_client()
        response = client.post(
            "/api/library/assets/upload",
            files={"file": ("source.wav", _wav_bytes(), "audio/wav")},
        )
        assert response.status_code == 201, response.text
        data = response.json()
        audio_id = data.get("audio_id")
        assert audio_id, "audio_id must be returned for transcription-ready playback"
        resolved = audio_path_resolver.resolve_audio_path(audio_id)
        assert resolved is not None
        assert os.path.isfile(resolved)

    def test_upload_missing_file_returns_json_error(self, tmp_path, monkeypatch):
        """Missing upload body must yield JSON (not opaque HTML)."""
        from backend.api.routes import library

        monkeypatch.setattr(library, "get_path", lambda _name: tmp_path)
        client = _make_client()
        response = client.post("/api/library/assets/upload")
        assert response.status_code in (400, 422)
        assert response.headers.get("content-type", "").startswith("application/json")
        body = response.json()
        assert body is not None


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
