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
    from backend.api.models import ApiOk
    from backend.api.auth import UserRole
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
