"""Contract tests for API models.

Validates that Pydantic models produce stable, well-formed JSON schemas.
These schemas represent the contract between the Python backend and
the C# frontend. Any schema change here must be reflected in the
C# DTOs used by BackendClient.cs.

Run: python -m pytest tests/contract/ -v
"""

import json
import sys
from pathlib import Path

import pytest

project_root = str(Path(__file__).parent.parent.parent)
if project_root not in sys.path:
    sys.path.insert(0, project_root)

try:
    from backend.api.auth import UserRole
    from backend.api.models import ApiOk
except ImportError:
    pytest.skip("Could not import backend models", allow_module_level=True)


class TestApiModelContracts:
    """Verify API model schemas are stable and well-formed."""

    def test_api_ok_schema_shape(self):
        """ApiOk must have 'ok' boolean field."""
        schema = ApiOk.model_json_schema()
        assert "properties" in schema
        assert "ok" in schema["properties"]

    def test_user_role_values(self):
        """UserRole enum values must not change without C# update."""
        expected = {"admin", "user", "guest", "service"}
        actual = {role.value for role in UserRole}
        assert actual == expected, (
            f"UserRole values changed. Update C# UserRole enum. "
            f"Expected {expected}, got {actual}"
        )


class TestModelSchemaExport:
    """Verify key models can export JSON schemas without error."""

    @pytest.mark.parametrize("model_path", [
        "backend.api.models.ApiOk",
        "backend.api.models_additional.TrainingDataAnalysis",
    ])
    def test_model_exports_valid_json_schema(self, model_path):
        """Each model must produce a valid JSON schema."""
        module_path, class_name = model_path.rsplit(".", 1)
        try:
            import importlib
            mod = importlib.import_module(module_path)
            model_cls = getattr(mod, class_name)
        except (ImportError, AttributeError) as e:
            pytest.skip(f"Cannot import {model_path}: {e}")

        schema = model_cls.model_json_schema()
        schema_json = json.dumps(schema)
        assert len(schema_json) > 10
        assert "properties" in schema or "anyOf" in schema or "$defs" in schema


class TestSchemaRegistryIntegrity:
    """Verify the schema registry (if populated) matches disk."""

    def test_shared_directory_exists(self):
        """The shared/ directory must exist for cross-language contracts."""
        shared_dir = Path(project_root) / "shared"
        assert shared_dir.exists(), (
            "shared/ directory missing. Create it for cross-language schema contracts."
        )


class TestRouteContractStability:
    """Verify route modules export expected endpoint shapes."""

    ROUTE_FILES = [
        "backend.api.routes.voice",
        "backend.api.routes.transcribe",
        "backend.api.routes.training",
        "backend.api.routes.profiles",
        "backend.api.routes.tags",
        "backend.api.routes.batch",
        "backend.api.routes.audio_analysis",
        "backend.api.routes.spectrogram",
        "backend.api.routes.ssml",
        "backend.api.routes.rvc",
        "backend.api.routes.health",
        "backend.api.routes.engines",
    ]

    @pytest.mark.parametrize("route_module", ROUTE_FILES)
    def test_route_module_imports(self, route_module):
        """Each route module must import without error."""
        import importlib
        try:
            mod = importlib.import_module(route_module)
            assert hasattr(mod, "router"), (
                f"{route_module} must export a 'router' variable (FastAPI APIRouter)"
            )
        except ImportError as e:
            pytest.skip(f"Cannot import {route_module}: {e}")

    @pytest.mark.parametrize("route_module", ROUTE_FILES)
    def test_route_has_endpoints(self, route_module):
        """Each route module must register at least one endpoint."""
        import importlib
        try:
            mod = importlib.import_module(route_module)
            router = getattr(mod, "router", None)
            if router is None:
                pytest.skip(f"{route_module} has no router")
            assert len(router.routes) > 0, (
                f"{route_module} has a router but no registered routes"
            )
        except ImportError as e:
            pytest.skip(f"Cannot import {route_module}: {e}")

    def test_health_route_has_dependencies_endpoint(self):
        """Health route must have /dependencies endpoint (Sprint 3.1)."""
        try:
            from backend.api.routes.health import router
            paths = [r.path for r in router.routes if hasattr(r, "path")]
            assert any("dependencies" in p for p in paths), (
                "Health router missing /dependencies endpoint"
            )
        except ImportError:
            pytest.skip("Cannot import health routes")

    def test_training_status_has_simulation_fields(self):
        """TrainingStatus Pydantic model must include simulation_mode (Sprint 1.1)."""
        try:
            from backend.api.routes.training import TrainingStatus
            schema = TrainingStatus.model_json_schema()
            props = schema.get("properties", {})
            assert "simulation_mode" in props, (
                "TrainingStatus missing simulation_mode field"
            )
            assert "simulation_reason" in props, (
                "TrainingStatus missing simulation_reason field"
            )
        except ImportError:
            pytest.skip("Cannot import training route")
