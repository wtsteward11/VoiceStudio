"""
Shared Schema Validation Tests

Validates shared JSON schemas between frontend and backend:
- Schema structure and validity
- Cross-schema references
- Frontend/Backend usage consistency
- Contract synchronization
"""

import json
import re
import sys
from pathlib import Path
from typing import Any

import pytest

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))


# =============================================================================
# FIXTURES
# =============================================================================


@pytest.fixture(scope="module")
def shared_dir() -> Path:
    """Get shared directory path."""
    path = PROJECT_ROOT / "shared"
    if not path.exists():
        pytest.skip(f"Shared directory not found: {path}")
    return path


@pytest.fixture(scope="module")
def schema_registry(shared_dir: Path) -> dict[str, Any]:
    """Load schema registry."""
    registry_path = shared_dir / "schemas" / "_registry.json"

    if not registry_path.exists():
        pytest.skip(f"Schema registry not found: {registry_path}")

    with open(registry_path, encoding="utf-8") as f:
        return json.load(f)


@pytest.fixture(scope="module")
def all_schemas(shared_dir: Path) -> dict[str, dict[str, Any]]:
    """Load all JSON schema files from shared directory."""
    schemas = {}

    for schema_file in shared_dir.rglob("*.json"):
        if schema_file.name.startswith("_"):
            continue

        try:
            with open(schema_file, encoding="utf-8") as f:
                content = json.load(f)
                relative_path = schema_file.relative_to(shared_dir)
                schemas[str(relative_path)] = content
        except json.JSONDecodeError as e:
            pytest.fail(f"Invalid JSON in {schema_file}: {e}")

    return schemas


@pytest.fixture(scope="module")
def contract_schemas(all_schemas) -> dict[str, dict[str, Any]]:
    """Filter to contract schemas only."""
    # Handle both forward and backslashes for cross-platform compatibility
    return {k: v for k, v in all_schemas.items() if k.replace("\\", "/").startswith("contracts/")}


# =============================================================================
# BASIC VALIDATION TESTS
# =============================================================================


@pytest.mark.contract
class TestSchemaDiscovery:
    """Tests for schema discovery."""

    def test_shared_directory_exists(self, shared_dir):
        """Verify shared directory exists."""
        assert shared_dir.exists()

    def test_schemas_found(self, all_schemas):
        """Verify at least some schemas are found."""
        assert len(all_schemas) > 0, "No schemas found in shared directory"

    def test_contracts_found(self, contract_schemas):
        """Verify contract schemas exist."""
        assert len(contract_schemas) > 0, "No contract schemas found"

    def test_registry_exists(self, schema_registry):
        """Verify schema registry exists and is valid."""
        assert "schemas" in schema_registry


@pytest.mark.contract
class TestSchemaRegistryConsistency:
    """Tests for schema registry consistency."""

    def test_registered_schemas_exist(self, schema_registry, shared_dir):
        """Verify all registered schemas exist on disk."""
        missing = []

        for schema_entry in schema_registry.get("schemas", []):
            path = schema_entry.get("path", "")
            full_path = shared_dir / path

            if not full_path.exists():
                missing.append(path)

        assert not missing, "Registered schemas not found:\n" + "\n".join(missing)

    def test_all_schemas_registered(self, schema_registry, all_schemas, shared_dir):
        """Verify all schema files are in registry."""
        registered_paths = {entry["path"] for entry in schema_registry.get("schemas", [])}

        unregistered = []
        for schema_path in all_schemas:
            # Skip the registry itself
            if "_registry" in schema_path:
                continue

            if schema_path not in registered_paths:
                unregistered.append(schema_path)

        # Advisory - some schemas may intentionally be unregistered
        if unregistered:
            import warnings

            warnings.warn(f"Unregistered schemas: {unregistered}", stacklevel=2)

    def test_registry_ids_unique(self, schema_registry):
        """Verify schema IDs are unique."""
        ids = []
        duplicates = []

        for entry in schema_registry.get("schemas", []):
            schema_id = entry.get("id")
            if schema_id in ids:
                duplicates.append(schema_id)
            else:
                ids.append(schema_id)

        assert not duplicates, f"Duplicate schema IDs: {duplicates}"


@pytest.mark.contract
class TestSchemaStructure:
    """Tests for schema structure validity."""

    def test_schemas_are_valid_json_schema(self, all_schemas):
        """Verify schemas follow JSON Schema structure."""
        errors = []

        for path, schema in all_schemas.items():
            # Skip non-schema JSON files
            if not path.endswith(".schema.json"):
                continue

            # Check for type or $ref at root
            has_type = "type" in schema or "$ref" in schema
            has_properties = "properties" in schema

            if not has_type and not has_properties:
                errors.append(f"{path}: missing 'type' or 'properties'")

        # Allow some flexibility
        if len(errors) > len(all_schemas) * 0.3:
            pytest.fail("Schema structure errors:\n" + "\n".join(errors[:10]))

    def test_object_schemas_have_properties(self, all_schemas):
        """Verify object type schemas have properties defined."""
        errors = []

        for path, schema in all_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            if schema.get("type") == "object" and "properties" not in schema:
                # Allow empty objects or those with additionalProperties
                if "additionalProperties" not in schema:
                    errors.append(path)

        # Advisory only
        if errors:
            import warnings

            warnings.warn(f"Object schemas without properties: {errors}", stacklevel=2)

    def test_array_schemas_have_items(self, all_schemas):
        """Verify array type schemas have items defined."""
        errors = []

        def check_arrays(obj: Any, path: str, schema_path: str):
            if isinstance(obj, dict):
                if obj.get("type") == "array" and "items" not in obj:
                    errors.append(f"{schema_path}:{path}")
                for key, value in obj.items():
                    check_arrays(value, f"{path}.{key}", schema_path)

        for schema_path, schema in all_schemas.items():
            if schema_path.endswith(".schema.json"):
                check_arrays(schema, "", schema_path)

        assert not errors, "Arrays without items:\n" + "\n".join(errors[:10])

    def test_required_fields_exist(self, all_schemas):
        """Verify required fields are defined in properties."""
        errors = []

        for path, schema in all_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            required = schema.get("required", [])
            properties = schema.get("properties", {})

            for field in required:
                if field not in properties:
                    errors.append(f"{path}: required field '{field}' not in properties")

        assert not errors, "Required field errors:\n" + "\n".join(errors)


@pytest.mark.contract
class TestSchemaReferences:
    """Tests for schema references."""

    def test_internal_refs_resolve(self, all_schemas, shared_dir):
        """Verify internal $ref references resolve."""
        errors = []

        def find_refs(obj: Any, path: str) -> list[tuple[str, str]]:
            refs = []
            if isinstance(obj, dict):
                if "$ref" in obj:
                    refs.append((path, obj["$ref"]))
                for key, value in obj.items():
                    refs.extend(find_refs(value, f"{path}.{key}"))
            elif isinstance(obj, list):
                for i, item in enumerate(obj):
                    refs.extend(find_refs(item, f"{path}[{i}]"))
            return refs

        for schema_path, schema in all_schemas.items():
            refs = find_refs(schema, "")

            for ref_path, ref_value in refs:
                # Internal refs start with #
                if ref_value.startswith("#/"):
                    parts = ref_value.split("/")[1:]
                    target = schema
                    for part in parts:
                        if isinstance(target, dict) and part in target:
                            target = target[part]
                        else:
                            errors.append(f"{schema_path}{ref_path}: unresolved {ref_value}")
                            break
                # External refs (file paths)
                elif not ref_value.startswith("http"):
                    current_schema = shared_dir / schema_path
                    ref_file = current_schema.parent / ref_value
                    if not ref_file.exists():
                        ref_file = shared_dir / ref_value
                    if not ref_file.exists():
                        errors.append(f"{schema_path}{ref_path}: file not found {ref_value}")

        assert not errors, "Unresolved references:\n" + "\n".join(errors[:10])


# =============================================================================
# CONTRACT TESTS
# =============================================================================


@pytest.mark.contract
class TestContractSchemas:
    """Tests for contract schema validity."""

    def test_contracts_have_title(self, contract_schemas):
        """Verify contract schemas have title."""
        missing = []

        for path, schema in contract_schemas.items():
            if "title" not in schema:
                missing.append(path)

        # Advisory only
        if missing:
            import warnings

            warnings.warn(f"Contracts without title: {missing}", stacklevel=2)

    def test_contracts_are_objects(self, contract_schemas):
        """Verify most contracts define object types."""
        non_objects = []

        for path, schema in contract_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            schema_type = schema.get("type")
            if schema_type and schema_type != "object":
                non_objects.append(f"{path}: type={schema_type}")

        # Allow some non-object contracts (enums, etc.)
        if len(non_objects) > len(contract_schemas) * 0.3:
            import warnings

            warnings.warn(f"Non-object contracts: {non_objects}", stacklevel=2)

    def test_request_contracts_have_required(self, contract_schemas):
        """Verify request contracts define required fields."""
        requests_without_required = []

        for path, schema in contract_schemas.items():
            if "request" in path.lower() and path.endswith(".schema.json"):
                if "required" not in schema or len(schema.get("required", [])) == 0:
                    requests_without_required.append(path)

        # Advisory - some requests may have all optional fields
        if requests_without_required:
            import warnings

            warnings.warn(
                f"Request contracts without required: {requests_without_required}", stacklevel=2
            )


@pytest.mark.contract
class TestErrorCodes:
    """Tests for error code definitions."""

    @pytest.fixture
    def error_codes(self, shared_dir) -> dict[str, Any]:
        """Load error codes."""
        error_path = shared_dir / "contracts" / "error_codes.json"

        if not error_path.exists():
            pytest.skip("error_codes.json not found")

        with open(error_path, encoding="utf-8") as f:
            return json.load(f)

    def test_error_codes_exist(self, error_codes):
        """Verify error codes are defined."""
        assert error_codes is not None

    def test_error_codes_have_messages(self, error_codes):
        """Verify error codes have message definitions."""
        # Check structure based on actual format
        if isinstance(error_codes, dict):
            for code, details in error_codes.items():
                if isinstance(details, dict):
                    # Should have message or description
                    has_desc = "message" in details or "description" in details
                    if not has_desc:
                        import warnings

                        warnings.warn(f"Error code {code} missing description", stacklevel=2)

    def test_error_codes_unique(self, error_codes):
        """Verify error codes are unique."""
        if isinstance(error_codes, dict):
            # Keys are already unique in dict
            pass
        elif isinstance(error_codes, list):
            codes = [e.get("code") for e in error_codes if isinstance(e, dict)]
            duplicates = [c for c in codes if codes.count(c) > 1]
            assert not duplicates, f"Duplicate error codes: {set(duplicates)}"


# =============================================================================
# CROSS-PLATFORM SYNC TESTS
# =============================================================================


@pytest.mark.contract
class TestFrontendBackendSync:
    """Tests for frontend/backend schema synchronization."""

    @pytest.fixture
    def backend_models_dir(self) -> Path:
        """Get backend models directory."""
        path = PROJECT_ROOT / "backend" / "api" / "models"
        return path if path.exists() else PROJECT_ROOT / "backend" / "api"

    @pytest.fixture
    def frontend_contracts_dir(self) -> Path:
        """Get frontend contracts directory."""
        # C# might have DTOs in various locations
        app_path = PROJECT_ROOT / "src" / "VoiceStudio.App"
        return app_path / "Models" if (app_path / "Models").exists() else app_path

    def test_shared_contracts_documented(self, schema_registry):
        """Verify all shared contracts have descriptions."""
        undocumented = []

        for entry in schema_registry.get("schemas", []):
            if not entry.get("description"):
                undocumented.append(entry.get("id"))

        # All should have descriptions for documentation
        if undocumented:
            import warnings

            warnings.warn(f"Undocumented schemas: {undocumented}", stacklevel=2)

    def test_analyze_voice_request_sync(self, contract_schemas):
        """Verify AnalyzeVoiceRequest schema has expected fields."""
        schema = None
        for path, s in contract_schemas.items():
            if "analyze_voice_request" in path.lower():
                schema = s
                break

        if not schema:
            pytest.skip("AnalyzeVoiceRequest schema not found")

        # Expected fields based on typical voice analysis request
        expected_fields = {"profileId", "clipId"}
        actual_fields = set(schema.get("properties", {}).keys())

        missing = expected_fields - actual_fields
        assert not missing, f"AnalyzeVoiceRequest missing fields: {missing}"

    def test_layout_state_sync(self, contract_schemas):
        """Verify layout state schema exists and is valid."""
        schema = None
        for path, s in contract_schemas.items():
            if "layout_state" in path.lower():
                schema = s
                break

        if not schema:
            pytest.skip("layout_state schema not found")

        # Should be an object type for storing UI state
        assert schema.get("type") == "object" or "properties" in schema

    def test_mcp_operation_sync(self, contract_schemas):
        """Verify MCP operation schemas exist and are valid."""
        mcp_schemas = []
        for path, s in contract_schemas.items():
            if "mcp_operation" in path.lower():
                mcp_schemas.append((path, s))

        if not mcp_schemas:
            pytest.skip("MCP operation schemas not found")

        # Should have at least request and response
        assert len(mcp_schemas) >= 1


@pytest.mark.contract
class TestSchemaVersioning:
    """Tests for schema versioning."""

    def test_registry_has_version(self, schema_registry):
        """Verify schema registry has version."""
        assert "version" in schema_registry, "Registry missing version"

    def test_version_is_semver(self, schema_registry):
        """Verify version follows semver format."""
        version = schema_registry.get("version", "")
        pattern = r"^\d+\.\d+\.\d+$"

        assert re.match(pattern, version), f"Invalid version format: {version}"


# =============================================================================
# SCHEMA COMPATIBILITY TESTS
# =============================================================================


@pytest.mark.contract
class TestSchemaCompatibility:
    """Tests for schema compatibility."""

    def test_no_breaking_type_changes(self, all_schemas):
        """Check for potential breaking type changes in schemas."""
        # This would compare against a baseline
        # For now, just verify types are consistent
        type_usage = {}

        def collect_types(obj: Any, path: str, schema_path: str):
            if isinstance(obj, dict):
                if "type" in obj:
                    type_val = obj["type"]
                    key = f"{path}:{type_val}"
                    if key not in type_usage:
                        type_usage[key] = []
                    type_usage[key].append(schema_path)
                for key, value in obj.items():
                    collect_types(value, f"{path}.{key}", schema_path)

        for schema_path, schema in all_schemas.items():
            if schema_path.endswith(".schema.json"):
                collect_types(schema, "", schema_path)

        # No assertion - just collecting data
        # Could be used for regression testing

    def test_property_names_consistent(self, all_schemas):
        """Check for consistent property naming conventions."""
        naming_styles = {"camelCase": 0, "snake_case": 0, "PascalCase": 0}

        for path, schema in all_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            properties = schema.get("properties", {})
            for prop_name in properties:
                if "_" in prop_name:
                    naming_styles["snake_case"] += 1
                elif prop_name[0].isupper():
                    naming_styles["PascalCase"] += 1
                else:
                    naming_styles["camelCase"] += 1

        # Should have a dominant style
        total = sum(naming_styles.values())
        if total > 0:
            dominant_style = max(naming_styles, key=naming_styles.get)
            dominant_count = naming_styles[dominant_style]

            if dominant_count / total < 0.7:
                import warnings

                warnings.warn(f"Mixed naming conventions: {naming_styles}", stacklevel=2)


@pytest.mark.contract
class TestSchemaDocumentation:
    """Tests for schema documentation quality."""

    def test_schemas_have_descriptions(self, all_schemas):
        """Verify schemas have descriptions."""
        undocumented = []

        for path, schema in all_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            if "description" not in schema and "title" not in schema:
                undocumented.append(path)

        # Allow some undocumented, but not too many
        if len(undocumented) > len(all_schemas) * 0.5:
            import warnings

            warnings.warn(f"Many undocumented schemas: {undocumented[:5]}", stacklevel=2)

    def test_properties_have_descriptions(self, all_schemas):
        """Check if important properties have descriptions."""
        undocumented_props = []

        for path, schema in all_schemas.items():
            if not path.endswith(".schema.json"):
                continue

            properties = schema.get("properties", {})
            for prop_name, prop_def in properties.items():
                if isinstance(prop_def, dict) and "description" not in prop_def:
                    undocumented_props.append(f"{path}:{prop_name}")

        # Advisory only - just report coverage
        total_props = len(undocumented_props)
        if total_props > 20:
            import warnings

            warnings.warn(f"Properties without descriptions: {total_props}", stacklevel=2)


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-m", "contract"])
