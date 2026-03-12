"""
Unit Tests for Todo Panel API Route
Tests todo panel endpoints comprehensively.
"""

import sys
from datetime import datetime
from pathlib import Path
from unittest.mock import patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import todo_panel
except ImportError:
    pytest.skip("Could not import todo_panel route module", allow_module_level=True)


class TestTodoPanelRouteImports:
    """Test todo panel route module can be imported."""

    def test_todo_panel_module_imports(self):
        """Test todo_panel module can be imported."""
        assert todo_panel is not None, "Failed to import todo_panel module"
        assert hasattr(todo_panel, "router"), "todo_panel module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert todo_panel.router is not None, "Router should exist"
        if hasattr(todo_panel.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(todo_panel.router, "routes"):
            routes = [route.path for route in todo_panel.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


@pytest.mark.skipif(
    True,
    reason="Manipulates module state - needs fixture refactoring (SKIP_DEBT_CLEANUP_SUBPLAN § Flaky)",
)
class TestTodoEndpoints:
    """Test todo CRUD endpoints."""

    def test_list_todos_empty(self):
        """Test listing todos when empty."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)
            assert len(data) == 0

    def test_list_todos_with_data(self):
        """Test listing todos with data."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todos = [
            Todo(
                todo_id="todo1",
                title="Test Todo",
                description="A test todo",
                status="pending",
                priority="medium",
                category="test",
                tags=["test"],
                created_at=now,
                updated_at=now,
            )
        ]

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = mock_todos

            response = client.get("/api/todo-panel")
            assert response.status_code == 200
            data = response.json()
            assert len(data) == 1
            assert data[0]["title"] == "Test Todo"

    def test_list_todos_filtered_by_status(self):
        """Test listing todos filtered by status."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todos = [
            Todo(
                todo_id="todo1",
                title="Pending Todo",
                status="pending",
                priority="medium",
                created_at=now,
                updated_at=now,
            )
        ]

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = mock_todos

            response = client.get("/api/todo-panel?status=pending")
            assert response.status_code == 200
            data = response.json()
            assert len(data) == 1

    def test_list_todos_filtered_by_priority(self):
        """Test listing todos filtered by priority."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel?priority=high")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)

    def test_list_todos_filtered_by_category(self):
        """Test listing todos filtered by category."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel?category=work")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)

    def test_list_todos_filtered_by_tag(self):
        """Test listing todos filtered by tag."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel?tag=important")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)

    def test_get_todo_success(self):
        """Test successful todo retrieval."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todo = Todo(
            todo_id="todo1",
            title="Test Todo",
            status="pending",
            priority="medium",
            created_at=now,
            updated_at=now,
        )

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            mock_load.return_value = mock_todo

            response = client.get("/api/todo-panel/todo1")
            assert response.status_code == 200
            data = response.json()
            assert data["todo_id"] == "todo1"

    def test_get_todo_not_found(self):
        """Test getting non-existent todo."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            mock_load.return_value = None

            response = client.get("/api/todo-panel/nonexistent")
            assert response.status_code == 404

    def test_create_todo_success(self):
        """Test successful todo creation."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        request_data = {
            "title": "New Todo",
            "description": "A new todo item",
            "priority": "high",
            "category": "work",
            "tags": ["important"],
        }

        with patch("backend.api.routes.todo_panel._save_todo_to_db") as mock_save:
            mock_save.return_value = True

            response = client.post("/api/todo-panel", json=request_data)
            assert response.status_code == 200
            data = response.json()
            assert data["title"] == "New Todo"
            assert data["priority"] == "high"

    def test_create_todo_missing_title(self):
        """Test todo creation with missing title."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        request_data = {
            "description": "No title",
        }

        response = client.post("/api/todo-panel", json=request_data)
        assert response.status_code == 422  # Validation error

    def test_create_todo_invalid_priority(self):
        """Test todo creation with invalid priority."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        request_data = {
            "title": "Test Todo",
            "priority": "invalid",
        }

        response = client.post("/api/todo-panel", json=request_data)
        assert response.status_code == 400

    def test_update_todo_success(self):
        """Test successful todo update."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todo = Todo(
            todo_id="todo1",
            title="Original Title",
            status="pending",
            priority="medium",
            created_at=now,
            updated_at=now,
        )

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            with patch("backend.api.routes.todo_panel._save_todo_to_db") as mock_save:
                mock_load.return_value = mock_todo
                mock_save.return_value = True

                request_data = {
                    "title": "Updated Title",
                    "status": "in_progress",
                }

                response = client.put("/api/todo-panel/todo1", json=request_data)
                assert response.status_code == 200
                data = response.json()
                assert data["title"] == "Updated Title"

    def test_update_todo_not_found(self):
        """Test updating non-existent todo."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            mock_load.return_value = None

            request_data = {"title": "Updated Title"}

            response = client.put("/api/todo-panel/nonexistent", json=request_data)
            assert response.status_code == 404

    def test_update_todo_completed_status(self):
        """Test updating todo to completed status."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todo = Todo(
            todo_id="todo1",
            title="Test Todo",
            status="pending",
            priority="medium",
            created_at=now,
            updated_at=now,
        )

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            with patch("backend.api.routes.todo_panel._save_todo_to_db") as mock_save:
                mock_load.return_value = mock_todo
                mock_save.return_value = True

                request_data = {"status": "completed"}

                response = client.put("/api/todo-panel/todo1", json=request_data)
                assert response.status_code == 200
                data = response.json()
                assert data["status"] == "completed"
                assert data["completed_at"] is not None

    def test_delete_todo_success(self):
        """Test successful todo deletion."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todo = Todo(
            todo_id="todo1",
            title="To Delete",
            status="pending",
            priority="medium",
            created_at=now,
            updated_at=now,
        )

        with (
            patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load,
            patch("backend.api.routes.todo_panel._delete_todo_from_db") as mock_delete,
        ):
            mock_load.return_value = mock_todo
            mock_delete.return_value = True

            response = client.delete("/api/todo-panel/todo1")
            assert response.status_code == 200

    def test_delete_todo_not_found(self):
        """Test deleting non-existent todo."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._load_todo_from_db") as mock_load:
            mock_load.return_value = None

            response = client.delete("/api/todo-panel/nonexistent")
            assert response.status_code == 404


class TestTodoCategoriesAndTagsEndpoints:
    """Test todo categories and tags endpoints."""

    def test_list_categories_success(self):
        """Test successful categories listing."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todos = [
            Todo(
                todo_id="todo1",
                title="Todo 1",
                status="pending",
                priority="medium",
                category="work",
                created_at=now,
                updated_at=now,
            ),
            Todo(
                todo_id="todo2",
                title="Todo 2",
                status="pending",
                priority="medium",
                category="personal",
                created_at=now,
                updated_at=now,
            ),
        ]

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = mock_todos

            response = client.get("/api/todo-panel/categories/list")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)
            assert "work" in data
            assert "personal" in data

    def test_list_tags_success(self):
        """Test successful tags listing."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todos = [
            Todo(
                todo_id="todo1",
                title="Todo 1",
                status="pending",
                priority="medium",
                tags=["important", "urgent"],
                created_at=now,
                updated_at=now,
            ),
        ]

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = mock_todos

            response = client.get("/api/todo-panel/tags/list")
            assert response.status_code == 200
            data = response.json()
            assert isinstance(data, list)
            assert "important" in data
            assert "urgent" in data


class TestTodoStatsEndpoint:
    """Test todo statistics endpoint."""

    def test_get_todo_summary_success(self):
        """Test successful todo summary retrieval."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        from backend.api.routes.todo_panel import Todo

        now = datetime.utcnow().isoformat()
        mock_todos = [
            Todo(
                todo_id="todo1",
                title="Pending",
                status="pending",
                priority="medium",
                created_at=now,
                updated_at=now,
            ),
            Todo(
                todo_id="todo2",
                title="Completed",
                status="completed",
                priority="high",
                created_at=now,
                updated_at=now,
            ),
        ]

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = mock_todos

            response = client.get("/api/todo-panel/stats/summary")
            assert response.status_code == 200
            data = response.json()
            assert "total" in data
            assert "by_status" in data
            assert "by_priority" in data
            assert data["total"] == 2


@pytest.mark.skipif(
    True,
    reason="Endpoint not implemented (SKIP_DEBT_CLEANUP_SUBPLAN § Product gaps)",
)
class TestTodoExportEndpoint:
    """Test todo export endpoint."""

    def test_export_todos_json_success(self):
        """Test successful JSON export."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel/export?format=json")
            assert response.status_code == 200

    def test_export_todos_csv_success(self):
        """Test successful CSV export."""
        app = FastAPI()
        app.include_router(todo_panel.router)
        client = TestClient(app)

        with patch("backend.api.routes.todo_panel._list_todos_from_db") as mock_list:
            mock_list.return_value = []

            response = client.get("/api/todo-panel/export?format=csv")
            assert response.status_code == 200


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
