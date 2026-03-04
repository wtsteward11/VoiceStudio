"""
Invariant I-4: OpenAPI Spec Validity Gate.

Validates the generated OpenAPI spec:
- Zero unresolvable $ref entries (via openapi_spec_validator)
- All response models on core synthesis routes have concrete schema
  definitions (no {} any-type responses)

Roadmap v2.0 Phase 0 — Permanent CI invariant.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

pytestmark = [pytest.mark.ci]

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
OPENAPI_PATH = PROJECT_ROOT / "docs" / "api" / "openapi.json"

CORE_SYNTHESIS_PATH_PATTERNS = [
    "/api/voice/synthesize",
    "/api/voice/clone",
    "/api/voice/instant",
    "/api/batch",
    "/api/ensemble",
    "/api/multi-voice",
    "/api/multi_voice",
]


def _load_openapi_spec() -> dict[str, Any]:
    """Load the OpenAPI spec from disk."""
    if not OPENAPI_PATH.exists():
        pytest.skip(f"OpenAPI spec not found at {OPENAPI_PATH}")
    with open(OPENAPI_PATH) as f:
        return json.load(f)


def _is_empty_schema(schema: Any) -> bool:
    """Check if a schema is effectively empty (any-type)."""
    if schema is None:
        return True
    if isinstance(schema, dict):
        if not schema:
            return True
        if schema == {"type": "object"}:
            return True
    return False


class TestOpenAPIValidity:
    """Validate OpenAPI spec integrity."""

    def test_openapi_spec_exists(self):
        """OpenAPI spec file must exist."""
        assert OPENAPI_PATH.exists(), (
            f"OpenAPI spec not found at {OPENAPI_PATH}. "
            "Run `python scripts/export_openapi_schema.py` to generate it."
        )

    def test_openapi_spec_is_valid_json(self):
        """OpenAPI spec must be valid JSON with required top-level keys."""
        spec = _load_openapi_spec()
        assert "openapi" in spec, "Missing 'openapi' version field"
        assert "paths" in spec, "Missing 'paths' field"
        assert "info" in spec, "Missing 'info' field"

    def test_openapi_spec_validates_with_openapi_spec_validator(self):
        """Spec must pass openapi_spec_validator.validate() (resolves $ref)."""
        pytest.importorskip("openapi_spec_validator")
        from openapi_spec_validator import validate_spec

        spec = _load_openapi_spec()
        validate_spec(spec)

    def test_core_synthesis_routes_have_concrete_schemas(self):
        """Assert core synthesis routes have concrete response schemas."""
        spec = _load_openapi_spec()
        paths = spec.get("paths", {})
        empty_schema_routes = []

        for path, path_item in paths.items():
            is_synthesis = any(
                pattern in path.lower() for pattern in CORE_SYNTHESIS_PATH_PATTERNS
            )
            if not is_synthesis:
                continue

            for method, operation in path_item.items():
                if method in ("parameters", "summary", "description"):
                    continue
                responses = operation.get("responses", {})
                for status_code, response in responses.items():
                    if not status_code.startswith("2"):
                        continue
                    content = response.get("content", {})
                    for media_type, media_obj in content.items():
                        schema = media_obj.get("schema")
                        if _is_empty_schema(schema):
                            empty_schema_routes.append(
                                f"{method.upper()} {path} [{status_code}]"
                            )

        assert not empty_schema_routes, (
            f"Core synthesis routes with empty/any-type response schemas "
            f"({len(empty_schema_routes)}):\n"
            + "\n".join(f"  - {r}" for r in empty_schema_routes)
        )
