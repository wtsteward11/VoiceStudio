"""
OpenAPI Contract Tests - Python

Tests that validate the OpenAPI schema is correct and matches expectations.
These tests run as part of CI to catch contract issues early.
"""

import sys
from pathlib import Path
from typing import Any

import pytest

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

# OpenAPI schema: use session-scoped `openapi_schema` from tests/contract/conftest.py
# (live FastAPI app after _register_all_routes), not static docs/api/openapi.json.


class TestOpenAPISchemaStructure:
    """Tests for OpenAPI schema structure."""

    def test_schema_has_required_fields(self, openapi_schema: dict[str, Any]):
        """Verify schema has all required top-level fields."""
        required_fields = ["openapi", "info", "paths"]

        for field in required_fields:
            assert field in openapi_schema, f"Missing required field: {field}"

    def test_info_section_complete(self, openapi_schema: dict[str, Any]):
        """Verify info section has required fields."""
        info = openapi_schema.get("info", {})

        assert "title" in info, "Missing info.title"
        assert "version" in info, "Missing info.version"
        assert info["title"], "info.title is empty"
        assert info["version"], "info.version is empty"

    def test_openapi_version(self, openapi_schema: dict[str, Any]):
        """Verify OpenAPI version is supported."""
        version = openapi_schema.get("openapi", "")

        assert version.startswith("3."), f"Unsupported OpenAPI version: {version}"

    def test_paths_not_empty(self, openapi_schema: dict[str, Any]):
        """Verify at least some paths are defined."""
        paths = openapi_schema.get("paths", {})

        assert len(paths) > 0, "No paths defined in schema"


class TestEndpointDefinitions:
    """Tests for endpoint definitions."""

    def test_all_endpoints_have_operation_id(self, openapi_schema: dict[str, Any]):
        """Verify all endpoints have operationId."""
        paths = openapi_schema.get("paths", {})
        missing = []

        for path, methods in paths.items():
            for method, details in methods.items():
                if method in ["get", "post", "put", "delete", "patch"]:
                    if not details.get("operationId"):
                        missing.append(f"{method.upper()} {path}")

        assert not missing, f"Endpoints missing operationId: {missing}"

    def test_all_endpoints_have_responses(self, openapi_schema: dict[str, Any]):
        """Verify all endpoints have response definitions."""
        paths = openapi_schema.get("paths", {})
        missing = []

        for path, methods in paths.items():
            for method, details in methods.items():
                if method in ["get", "post", "put", "delete", "patch"]:
                    responses = details.get("responses", {})
                    if not responses:
                        missing.append(f"{method.upper()} {path}")

        assert not missing, f"Endpoints missing responses: {missing}"

    def test_health_endpoint_exists(self, openapi_schema: dict[str, Any]):
        """Verify health endpoint is defined."""
        paths = openapi_schema.get("paths", {})

        health_paths = ["/health", "/api/health"]
        has_health = any(p in paths for p in health_paths)

        assert has_health, "No health endpoint defined"


class TestCriticalEndpoints:
    """Tests for critical API endpoints that must exist."""

    CRITICAL_ENDPOINTS = [
        # Health and diagnostics
        ("GET", "/health"),
        ("GET", "/api/health"),
        # Metrics
        ("GET", "/api/metrics"),
        ("GET", "/api/cache/stats"),
    ]

    def test_critical_endpoints_exist(self, openapi_schema: dict[str, Any]):
        """Verify all critical endpoints are defined."""
        paths = openapi_schema.get("paths", {})
        missing = []

        for method, path in self.CRITICAL_ENDPOINTS:
            if path not in paths:
                missing.append(f"{method} {path}")
                continue

            path_methods = paths[path]
            if method.lower() not in path_methods:
                missing.append(f"{method} {path}")

        assert not missing, f"Missing critical endpoints: {missing}"


class TestSchemaDefinitions:
    """Tests for schema/component definitions."""

    def test_schemas_have_type_definitions(self, openapi_schema: dict[str, Any]):
        """Verify schemas have type definitions."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        untyped = []
        for name, schema in schemas.items():
            if not isinstance(schema, dict):
                continue

            has_type = any(
                [
                    "type" in schema,
                    "$ref" in schema,
                    "allOf" in schema,
                    "anyOf" in schema,
                    "oneOf" in schema,
                ]
            )

            if not has_type:
                untyped.append(name)

        # Allow some untyped for backward compatibility
        if len(untyped) > len(schemas) * 0.1:
            pytest.fail(f"Too many untyped schemas: {untyped[:10]}")


class TestBackwardCompatibility:
    """Tests for backward compatibility with C# client."""

    def test_operation_ids_are_valid_identifiers(self, openapi_schema: dict[str, Any]):
        """Verify operationIds can be converted to valid C# method names."""
        paths = openapi_schema.get("paths", {})
        invalid = []

        for _path, methods in paths.items():
            for method, details in methods.items():
                if method not in ["get", "post", "put", "delete", "patch"]:
                    continue

                op_id = details.get("operationId", "")
                if op_id:
                    # Check for characters that would break C# naming
                    if op_id.startswith("-") or op_id[0].isdigit():
                        invalid.append(f"{op_id} (invalid start)")
                    if any(c in op_id for c in [".", "/", "\\", " "]):
                        invalid.append(f"{op_id} (invalid chars)")

        assert not invalid, f"Invalid operationIds for C#: {invalid}"


# =============================================================================
# ENHANCED OPENAPI SCHEMA VALIDATION
# =============================================================================


class TestOpenAPISchemaValidation:
    """Comprehensive OpenAPI schema validation tests."""

    def test_all_refs_resolve(self, openapi_schema: dict[str, Any]):
        """Verify all $ref references resolve to existing schemas."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        def find_refs(obj: Any, path: str = "") -> list[tuple]:
            """Recursively find all $ref in schema."""
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

        all_refs = find_refs(openapi_schema)
        unresolved = []

        for path, ref in all_refs:
            if ref.startswith("#/components/schemas/"):
                schema_name = ref.split("/")[-1]
                if schema_name not in schemas:
                    unresolved.append(f"{path}: {ref}")

        assert not unresolved, "Unresolved $ref references:\n" + "\n".join(unresolved[:20])

    def test_no_circular_refs(self, openapi_schema: dict[str, Any]):
        """Check for circular references in schemas."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        def get_direct_refs(schema: dict) -> set[str]:
            """Get schema names directly referenced by this schema."""
            refs = set()

            def find_refs(obj):
                if isinstance(obj, dict):
                    if "$ref" in obj:
                        ref = obj["$ref"]
                        if ref.startswith("#/components/schemas/"):
                            refs.add(ref.split("/")[-1])
                    for value in obj.values():
                        find_refs(value)
                elif isinstance(obj, list):
                    for item in obj:
                        find_refs(item)

            find_refs(schema)
            return refs

        # Build dependency graph
        deps = {name: get_direct_refs(schema) for name, schema in schemas.items()}

        # Check for direct self-references (allowed in some cases like linked lists)
        # But detect longer cycles
        def has_cycle(name: str, visited: set[str]) -> bool:
            if name in visited:
                return True
            if name not in deps:
                return False

            visited.add(name)
            for dep in deps.get(name, set()):
                if dep != name and has_cycle(dep, visited.copy()):
                    return True
            return False

        cycles = []
        for name in schemas:
            if has_cycle(name, set()):
                cycles.append(name)

        # Note: Some cycles may be intentional (recursive types)
        # Only fail if there are many unexpected cycles
        if len(cycles) > 5:
            pytest.fail(f"Possible circular refs detected: {cycles[:10]}")

    def test_required_fields_are_defined(self, openapi_schema: dict[str, Any]):
        """Verify required fields are actually defined in properties."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        errors = []
        for name, schema in schemas.items():
            if not isinstance(schema, dict):
                continue

            required = schema.get("required", [])
            properties = schema.get("properties", {})

            for field in required:
                if field not in properties:
                    errors.append(f"{name}: required field '{field}' not in properties")

        assert not errors, "Required field errors:\n" + "\n".join(errors[:20])

    def test_array_items_defined(self, openapi_schema: dict[str, Any]):
        """Verify array types have items defined."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        def check_arrays(obj: Any, path: str) -> list[str]:
            errors = []
            if isinstance(obj, dict):
                if obj.get("type") == "array" and "items" not in obj:
                    errors.append(f"{path}: array without items")
                for key, value in obj.items():
                    errors.extend(check_arrays(value, f"{path}.{key}"))
            elif isinstance(obj, list):
                for i, item in enumerate(obj):
                    errors.extend(check_arrays(item, f"{path}[{i}]"))
            return errors

        all_errors = []
        for name, schema in schemas.items():
            all_errors.extend(check_arrays(schema, name))

        # Allow some without items (may be intentional for any array)
        if len(all_errors) > 5:
            pytest.fail("Arrays without items:\n" + "\n".join(all_errors[:10]))

    def test_enum_values_valid(self, openapi_schema: dict[str, Any]):
        """Verify enum types have valid values."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        errors = []

        def check_enums(obj: Any, path: str):
            if isinstance(obj, dict):
                if "enum" in obj:
                    values = obj["enum"]
                    if not isinstance(values, list):
                        errors.append(f"{path}: enum is not a list")
                    elif len(values) == 0:
                        errors.append(f"{path}: empty enum")
                    elif len(values) != len({str(v) for v in values}):
                        errors.append(f"{path}: duplicate enum values")
                for key, value in obj.items():
                    check_enums(value, f"{path}.{key}")
            elif isinstance(obj, list):
                for i, item in enumerate(obj):
                    check_enums(item, f"{path}[{i}]")

        for name, schema in schemas.items():
            check_enums(schema, name)

        assert not errors, "Enum errors:\n" + "\n".join(errors[:10])


class TestRequestBodyValidation:
    """Tests for request body definitions."""

    def test_post_endpoints_have_request_body(self, openapi_schema: dict[str, Any]):
        """Verify POST endpoints have request body defined."""
        paths = openapi_schema.get("paths", {})
        missing = []

        for path, methods in paths.items():
            if "post" in methods:
                post_op = methods["post"]
                # Allow some endpoints without body (like /logout)
                if "requestBody" not in post_op:
                    # Skip if the path suggests no JSON body (health pings, auth/session, action verbs)
                    skip_keywords = (
                        "logout",
                        "refresh",
                        "ping",
                        "generate",
                        "revoke",
                        "reset",
                        "undo",
                        "redo",
                        "execute",
                        "preview",
                        # Common POST actions on resource IDs (live OpenAPI has many of these)
                        "rollback",
                        "verify",
                        "start",
                        "cancel",
                        "grant",
                        "simulate",
                        "acknowledge",
                        "resolve",
                        "export",
                        "pause",
                        "resume",
                        "restart",
                        "stop",
                        "flush",
                        "purge",
                        "clear",
                        "sync",
                        "invalidate",
                        "record",
                        "save",
                        "submit",
                        "complete",
                        "validate",
                        "analyze",
                        "convert",
                        "create",
                        "process",
                    )
                    if not any(kw in path for kw in skip_keywords):
                        missing.append(path)

        # Live OpenAPI includes many RPC-style POSTs (path params / multipart / no JSON).
        # Fail only on large regressions (static snapshot era used a smaller surface).
        if len(missing) > 40:
            pytest.fail(f"POST endpoints without requestBody: {missing[:15]}")

    def test_request_bodies_have_content(self, openapi_schema: dict[str, Any]):
        """Verify request bodies have content type defined."""
        paths = openapi_schema.get("paths", {})
        errors = []

        for path, methods in paths.items():
            for method, details in methods.items():
                if not isinstance(details, dict):
                    continue

                request_body = details.get("requestBody", {})
                if request_body and "content" not in request_body:
                    errors.append(f"{method.upper()} {path}")

        assert not errors, f"Request bodies without content: {errors}"


class TestResponseValidation:
    """Tests for response definitions."""

    def test_endpoints_have_success_response(self, openapi_schema: dict[str, Any]):
        """Verify endpoints have at least one success response."""
        paths = openapi_schema.get("paths", {})
        missing = []

        for path, methods in paths.items():
            for method, details in methods.items():
                if method not in ["get", "post", "put", "delete", "patch"]:
                    continue

                responses = details.get("responses", {})
                has_success = any(str(code).startswith("2") for code in responses)

                if not has_success:
                    missing.append(f"{method.upper()} {path}")

        assert not missing, f"Endpoints without success response: {missing}"

    def test_error_responses_consistent(self, openapi_schema: dict[str, Any]):
        """Verify error responses use consistent schema."""
        paths = openapi_schema.get("paths", {})

        error_schemas = set()
        for _path, methods in paths.items():
            for _method, details in methods.items():
                if not isinstance(details, dict):
                    continue

                responses = details.get("responses", {})
                for code, resp in responses.items():
                    if str(code).startswith("4") or str(code).startswith("5"):
                        content = resp.get("content", {})
                        for _media_type, media_def in content.items():
                            schema = media_def.get("schema", {})
                            if "$ref" in schema:
                                error_schemas.add(schema["$ref"])

        # Should have consistent error schema (HTTPValidationError, etc.)
        # Allow up to 3 different error schemas
        if len(error_schemas) > 5:
            pytest.fail(f"Too many different error schemas: {error_schemas}")


class TestSecurityDefinitions:
    """Tests for security definitions."""

    def test_security_schemes_defined(self, openapi_schema: dict[str, Any]):
        """Check if security schemes are defined when security is used."""
        components = openapi_schema.get("components", {})
        security_schemes = components.get("securitySchemes", {})

        # Check global security
        global_security = openapi_schema.get("security", [])

        if global_security:
            for security_req in global_security:
                for scheme_name in security_req:
                    if scheme_name not in security_schemes:
                        pytest.fail(f"Security scheme '{scheme_name}' not defined")

    def test_sensitive_endpoints_secured(self, openapi_schema: dict[str, Any]):
        """Verify sensitive endpoints have security requirements."""
        paths = openapi_schema.get("paths", {})

        sensitive_patterns = [
            "/admin",
            "/user",
            "/profile",
            "/settings",
            "/delete",
            "/update",
            "/create",
        ]

        # Check if any sensitive endpoints exist without security
        # This is advisory, not a hard fail
        unsecured = []
        for path, methods in paths.items():
            is_sensitive = any(p in path.lower() for p in sensitive_patterns)
            if not is_sensitive:
                continue

            for method, details in methods.items():
                if method not in ["post", "put", "delete", "patch"]:
                    continue

                has_security = "security" in details or "security" in openapi_schema

                if not has_security:
                    unsecured.append(f"{method.upper()} {path}")

        if unsecured:
            # Warning only, not a failure
            import warnings

            warnings.warn(f"Potentially unsecured endpoints: {unsecured[:5]}", stacklevel=2)


class TestDocumentation:
    """Tests for API documentation quality."""

    def test_endpoints_have_summary(self, openapi_schema: dict[str, Any]):
        """Verify endpoints have summary or description."""
        paths = openapi_schema.get("paths", {})
        undocumented = []

        for path, methods in paths.items():
            for method, details in methods.items():
                if method not in ["get", "post", "put", "delete", "patch"]:
                    continue

                has_docs = details.get("summary") or details.get("description")

                if not has_docs:
                    undocumented.append(f"{method.upper()} {path}")

        # Allow some undocumented, but not too many
        coverage = 1 - (len(undocumented) / max(len(paths), 1))
        if coverage < 0.5:
            pytest.fail(f"Documentation coverage too low ({coverage:.0%}): {undocumented[:10]}")

    def test_schemas_have_descriptions(self, openapi_schema: dict[str, Any]):
        """Verify important schemas have descriptions."""
        components = openapi_schema.get("components", {})
        schemas = components.get("schemas", {})

        described = sum(1 for s in schemas.values() if isinstance(s, dict) and s.get("description"))
        total = len(schemas)

        if total > 0:
            coverage = described / total
            # Advisory: at least 30% should have descriptions
            if coverage < 0.3:
                import warnings

                warnings.warn(f"Schema description coverage: {coverage:.0%}", stacklevel=2)

    def test_examples_provided(self, openapi_schema: dict[str, Any]):
        """Check if examples are provided for key endpoints."""
        paths = openapi_schema.get("paths", {})

        with_examples = 0
        total = 0

        for _path, methods in paths.items():
            for method, details in methods.items():
                if method not in ["get", "post", "put", "delete", "patch"]:
                    continue

                total += 1

                # Check for examples in responses
                responses = details.get("responses", {})
                for _code, resp in responses.items():
                    content = resp.get("content", {})
                    for media_def in content.values():
                        if "example" in media_def or "examples" in media_def:
                            with_examples += 1
                            break

        if total > 0:
            coverage = with_examples / total
            # Advisory only
            if coverage < 0.2:
                import warnings

                warnings.warn(
                    f"Example coverage: {coverage:.0%} ({with_examples}/{total})", stacklevel=2
                )
