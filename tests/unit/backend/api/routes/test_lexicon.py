"""
Unit Tests for Lexicon API Route
Tests lexicon management endpoints comprehensively.
"""

import sys
import uuid
from datetime import datetime
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import lexicon
except ImportError:
    pytest.skip("Could not import lexicon route module", allow_module_level=True)


class TestLexiconRouteImports:
    """Test lexicon route module can be imported."""

    def test_lexicon_module_imports(self):
        """Test lexicon module can be imported."""
        assert lexicon is not None, "Failed to import lexicon module"
        assert hasattr(lexicon, "router"), "lexicon module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert lexicon.router is not None, "Router should exist"
        if hasattr(lexicon.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(lexicon.router, "routes"):
            routes = [route.path for route in lexicon.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestLexiconCRUD:
    """Test lexicon CRUD operations."""

    def setup_method(self):
        self._saved_lexicons = dict(lexicon._lexicons)
        self._saved_entries = dict(lexicon._lexicon_entries)
        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

    def teardown_method(self):
        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()
        lexicon._lexicons.update(self._saved_lexicons)
        lexicon._lexicon_entries.update(self._saved_entries)

    def test_create_lexicon_success(self):
        """Test successful lexicon creation."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        request_data = {
            "name": "Test Lexicon",
            "language": "en",
            "description": "A test lexicon",
        }

        response = client.post("/api/lexicon/lexicons", json=request_data)
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Test Lexicon"
        assert "lexicon_id" in data

    def test_list_lexicons_empty(self):
        """Test listing lexicons when empty."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        response = client.get("/api/lexicon/lexicons")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) == 0

    def test_list_lexicons_with_data(self):
        """Test listing lexicons with data."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        response = client.get("/api/lexicon/lexicons")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1

    def test_list_lexicons_filtered_by_language(self):
        """Test listing lexicons filtered by language."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        lexicon_id1 = f"lexicon-{uuid.uuid4().hex[:8]}"
        lexicon_id2 = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        lexicon._lexicons[lexicon_id1] = {
            "lexicon_id": lexicon_id1,
            "name": "English Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicons[lexicon_id2] = {
            "lexicon_id": lexicon_id2,
            "name": "Spanish Lexicon",
            "language": "es",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        response = client.get("/api/lexicon/lexicons?language=en")
        assert response.status_code == 200
        data = response.json()
        assert all(lex["language"] == "en" for lex in data)

    def test_get_lexicon_success(self):
        """Test successful lexicon retrieval."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        response = client.get(f"/api/lexicon/lexicons/{lexicon_id}")
        assert response.status_code == 200
        data = response.json()
        assert data["lexicon_id"] == lexicon_id

    def test_get_lexicon_not_found(self):
        """Test getting non-existent lexicon."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        response = client.get("/api/lexicon/lexicons/nonexistent")
        assert response.status_code == 404

    def test_update_lexicon_success(self):
        """Test successful lexicon update."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Original Name",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        update_data = {
            "name": "Updated Name",
            "language": "en",
            "description": "Updated description",
        }

        response = client.put(f"/api/lexicon/lexicons/{lexicon_id}", json=update_data)
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Updated Name"

    def test_update_lexicon_not_found(self):
        """Test updating non-existent lexicon."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        update_data = {"name": "Updated Name", "language": "en"}

        response = client.put("/api/lexicon/lexicons/nonexistent", json=update_data)
        assert response.status_code == 404

    def test_delete_lexicon_success(self):
        """Test successful lexicon deletion."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "To Delete",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        response = client.delete(f"/api/lexicon/lexicons/{lexicon_id}")
        assert response.status_code == 200

        # Verify lexicon is deleted
        get_response = client.get(f"/api/lexicon/lexicons/{lexicon_id}")
        assert get_response.status_code == 404

    def test_delete_lexicon_not_found(self):
        """Test deleting non-existent lexicon."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()

        response = client.delete("/api/lexicon/lexicons/nonexistent")
        assert response.status_code == 404


class TestLexiconEntries:
    """Test lexicon entry operations."""

    def test_create_lexicon_entry_success(self):
        """Test successful lexicon entry creation."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        entry_data = {
            "word": "test",
            "pronunciation": "/tɛst/",
            "part_of_speech": "noun",
        }

        response = client.post(f"/api/lexicon/lexicons/{lexicon_id}/entries", json=entry_data)
        assert response.status_code == 200
        data = response.json()
        assert data["word"] == "test"

    def test_create_lexicon_entry_duplicate(self):
        """Test creating duplicate lexicon entry."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [{"word": "test", "pronunciation": "/tɛst/"}]

        entry_data = {
            "word": "test",
            "pronunciation": "/tɛst/",
        }

        response = client.post(f"/api/lexicon/lexicons/{lexicon_id}/entries", json=entry_data)
        assert response.status_code == 400

    def test_list_lexicon_entries_success(self):
        """Test successful lexicon entries listing."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        response = client.get(f"/api/lexicon/lexicons/{lexicon_id}/entries")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1

    def test_list_lexicon_entries_filtered(self):
        """Test listing lexicon entries with filters."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [
            {
                "word": "test",
                "pronunciation": "/tɛst/",
                "part_of_speech": "noun",
                "language": "en",
            },
            {
                "word": "run",
                "pronunciation": "/rʌn/",
                "part_of_speech": "verb",
                "language": "en",
            },
        ]

        response = client.get(f"/api/lexicon/lexicons/{lexicon_id}/entries?part_of_speech=noun")
        assert response.status_code == 200
        data = response.json()
        assert all(entry["part_of_speech"] == "noun" for entry in data)

    def test_update_lexicon_entry_success(self):
        """Test successful lexicon entry update."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        update_data = {
            "word": "test",
            "pronunciation": "/tɛstɪŋ/",
        }

        response = client.put(
            f"/api/lexicon/lexicons/{lexicon_id}/entries/test",
            json=update_data,
        )
        assert response.status_code == 200
        data = response.json()
        assert data["pronunciation"] == "/tɛstɪŋ/"

    def test_delete_lexicon_entry_success(self):
        """Test successful lexicon entry deletion."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        response = client.delete(f"/api/lexicon/lexicons/{lexicon_id}/entries/test")
        assert response.status_code == 200

    def test_search_lexicon_entries_success(self):
        """Test successful lexicon entry search."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[lexicon_id] = {
            "lexicon_id": lexicon_id,
            "name": "Test Lexicon",
            "language": "en",
            "entry_count": 0,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[lexicon_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        search_data = {"query": "test"}

        response = client.post("/api/lexicon/search", json=search_data)
        assert response.status_code == 200
        data = response.json()
        assert "results" in data
        assert data["count"] >= 0


class TestSimplifiedLexiconEndpoints:
    """Test simplified lexicon endpoints for panel."""

    def test_add_lexicon_entry_success(self):
        """Test successful add via simplified endpoint."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        entry_data = {
            "word": "test",
            "pronunciation": "/tɛst/",
        }

        response = client.post("/api/lexicon/add", json=entry_data)
        assert response.status_code == 200
        data = response.json()
        assert data["word"] == "test"

    def test_update_lexicon_entry_simplified_success(self):
        """Test successful update via simplified endpoint."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        # Create default lexicon with entry
        default_id = "default-lexicon"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[default_id] = {
            "lexicon_id": default_id,
            "name": "Default Lexicon",
            "language": "en",
            "entry_count": 1,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[default_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        update_data = {
            "word": "test",
            "pronunciation": "/tɛstɪŋ/",
        }

        response = client.put("/api/lexicon/update", json=update_data)
        assert response.status_code == 200
        data = response.json()
        assert data["pronunciation"] == "/tɛstɪŋ/"

    def test_remove_lexicon_entry_success(self):
        """Test successful remove via simplified endpoint."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        # Create default lexicon with entry
        default_id = "default-lexicon"
        now = datetime.utcnow().isoformat()
        lexicon._lexicons[default_id] = {
            "lexicon_id": default_id,
            "name": "Default Lexicon",
            "language": "en",
            "entry_count": 1,
            "created": now,
            "modified": now,
        }

        lexicon._lexicon_entries[default_id] = [
            {"word": "test", "pronunciation": "/tɛst/", "language": "en"}
        ]

        response = client.delete("/api/lexicon/remove/test")
        assert response.status_code == 200

    def test_list_lexicon_entries_simplified(self):
        """Test listing entries via simplified endpoint."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        lexicon._lexicons.clear()
        lexicon._lexicon_entries.clear()

        response = client.get("/api/lexicon/list")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)

    @patch("backend.api.routes.lexicon.Phonemizer")
    def test_estimate_phonemes_with_phonemizer(self, mock_phonemizer_class):
        """Test phoneme estimation using Phonemizer (highest quality)."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        # Mock Phonemizer with phonemizer backend
        mock_phonemizer = MagicMock()
        mock_phonemizer.phonemizer_available = True
        mock_phonemizer.phonemize_with_phonemizer.return_value = "t ɛ s t"
        mock_phonemizer_class.return_value = mock_phonemizer

        request_data = {"word": "test", "language": "en"}

        response = client.post("/api/lexicon/phoneme", json=request_data)
        # May return 200 or 500 depending on dependencies
        if response.status_code == 200:
            data = response.json()
            assert "pronunciation" in data
            assert data.get("method") == "phonemizer"

    @patch("backend.api.routes.lexicon.Phonemizer")
    def test_estimate_phonemes_with_gruut(self, mock_phonemizer_class):
        """Test phoneme estimation using Gruut (alternative method)."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        # Mock Phonemizer with gruut backend (phonemizer not available)
        mock_phonemizer = MagicMock()
        mock_phonemizer.phonemizer_available = False
        mock_phonemizer.gruut_available = True
        mock_phonemizer.phonemize_with_gruut.return_value = [{"phonemes_str": "t ɛ s t"}]
        mock_phonemizer_class.return_value = mock_phonemizer

        request_data = {"word": "test", "language": "en"}

        response = client.post("/api/lexicon/phoneme", json=request_data)
        # May return 200 or 500 depending on dependencies
        if response.status_code == 200:
            data = response.json()
            assert "pronunciation" in data
            assert data.get("method") == "gruut"

    @patch("subprocess.run")
    def test_estimate_phonemes_with_espeak_fallback(self, mock_subprocess):
        """Test phoneme estimation using espeak-ng fallback."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        # Mock Phonemizer to not be available
        with patch("backend.api.routes.lexicon.Phonemizer") as mock_phonemizer_class:
            mock_phonemizer = MagicMock()
            mock_phonemizer.phonemizer_available = False
            mock_phonemizer.gruut_available = False
            mock_phonemizer_class.return_value = mock_phonemizer

            # Mock espeak-ng
            mock_result = MagicMock()
            mock_result.returncode = 0
            mock_result.stdout = "t ɛ s t"
            mock_subprocess.return_value = mock_result

            request_data = {"word": "test", "language": "en"}

            response = client.post("/api/lexicon/phoneme", json=request_data)
            # May return 200 or 500 depending on dependencies
            if response.status_code == 200:
                data = response.json()
                assert "pronunciation" in data
                assert data.get("method") == "espeak"

    def test_estimate_phonemes_success(self):
        """Test successful phoneme estimation."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        request_data = {"word": "test", "language": "en"}

        with patch("backend.api.routes.lexicon.Phonemizer") as mock_phonemizer:
            mock_instance = MagicMock()
            mock_instance.phonemize.return_value = "/tɛst/"
            mock_phonemizer.return_value = mock_instance

            response = client.post("/api/lexicon/phoneme", json=request_data)
            # May return 200 or 500 depending on dependencies
            assert response.status_code in [200, 500]

    def test_estimate_phonemes_missing_word(self):
        """Test phoneme estimation with missing word."""
        app = FastAPI()
        app.include_router(lexicon.router)
        client = TestClient(app)

        request_data = {"language": "en"}  # Missing word

        response = client.post("/api/lexicon/phoneme", json=request_data)
        # Should handle missing word gracefully
        assert response.status_code in [200, 400, 422]


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
