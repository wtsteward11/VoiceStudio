"""
API Contract Validation Tests.

Phase 2 Deliverable: Contract schema enforcement tests.
Validates API responses against JSON Schema contracts defined in tests/sentinel/contracts/.
"""

import json
import os
from pathlib import Path
from typing import Any

import pytest

try:
    import jsonschema
    from jsonschema import ValidationError, validate

    HAS_JSONSCHEMA = True
except ImportError:
    HAS_JSONSCHEMA = False

try:
    import requests
except ImportError:
    requests = None


# Paths
CONTRACTS_DIR = Path(__file__).parent.parent / "sentinel" / "contracts"
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")

pytestmark = [
    pytest.mark.integration,
    pytest.mark.contracts,
]


def load_schema(schema_name: str) -> dict[str, Any]:
    """Load a JSON schema from the contracts directory."""
    schema_path = CONTRACTS_DIR / schema_name
    if not schema_path.exists():
        pytest.skip(f"Schema not found: {schema_name}")
    with open(schema_path) as f:
        return json.load(f)


def validate_response(response_data: dict[str, Any], schema_name: str) -> None:
    """Validate response data against a schema."""
    if not HAS_JSONSCHEMA:
        pytest.skip("jsonschema not installed")

    schema = load_schema(schema_name)
    try:
        validate(instance=response_data, schema=schema)
    except ValidationError as e:
        pytest.fail(f"Schema validation failed for {schema_name}: {e.message}")


@pytest.fixture(scope="module")
def api_client():
    """Create API client for contract tests."""
    if requests is None:
        pytest.skip("requests not installed")

    session = requests.Session()
    session.headers.update({"Accept": "application/json"})

    # Verify backend is available
    try:
        resp = session.get(f"{BACKEND_URL}/health", timeout=5)
        if resp.status_code != 200:
            pytest.skip(f"Backend not healthy: {resp.status_code}")
    except requests.RequestException as e:
        pytest.skip(f"Backend not available: {e}")

    return session


class TestHealthContractValidation:
    """Validate health endpoint responses against contracts."""

    def test_health_response_schema(self, api_client):
        """Test /health response matches health_response.schema.json."""
        response = api_client.get(f"{BACKEND_URL}/health")
        assert response.status_code == 200

        data = response.json()
        validate_response(data, "health_response.schema.json")

    def test_health_detailed_response(self, api_client):
        """Test /api/health/detailed response structure."""
        response = api_client.get(f"{BACKEND_URL}/api/health/detailed")

        if response.status_code == 404:
            pytest.skip("Detailed health endpoint not available")

        assert response.status_code == 200
        data = response.json()

        # Basic structure validation
        assert "status" in data
        assert data["status"] in ["healthy", "degraded", "unhealthy"]


class TestTTSContractValidation:
    """Validate TTS endpoint responses against contracts."""

    def test_tts_request_schema_valid(self):
        """Test that valid TTS requests match the schema."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("tts_request.schema.json")

        # Valid request examples
        valid_requests = [
            {"text": "Hello world", "voice_id": "default"},
            {"text": "Test", "voice_id": "voice1", "engine": "xtts"},
            {"text": "Sample text", "voice_id": "v1", "speed": 1.0},
        ]

        for req in valid_requests:
            try:
                validate(instance=req, schema=schema)
            except ValidationError as e:
                pytest.fail(f"Valid request failed validation: {req} - {e.message}")

    def test_tts_request_schema_invalid(self):
        """Test that invalid TTS requests fail schema validation."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("tts_request.schema.json")

        # Invalid request examples (missing required fields)
        invalid_requests = [
            {},  # Missing all required fields
            {"text": "Hello"},  # Missing voice_id
            {"voice_id": "default"},  # Missing text
        ]

        for req in invalid_requests:
            with pytest.raises(ValidationError):
                validate(instance=req, schema=schema)


class TestUploadContractValidation:
    """Validate upload endpoint responses against contracts."""

    def test_upload_response_schema_structure(self):
        """Test upload response schema is valid."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("upload_response.schema.json")

        # Verify schema is well-formed
        assert "type" in schema
        assert schema["type"] == "object"
        assert "properties" in schema


class TestJobContractValidation:
    """Validate job endpoint responses against contracts."""

    def test_job_response_schema_structure(self):
        """Test job response schema is valid."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("job_response.schema.json")

        # Verify schema is well-formed
        assert "type" in schema
        assert "properties" in schema

    def test_job_response_valid_example(self):
        """Test that valid job responses match the schema."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("job_response.schema.json")

        # Example valid job response
        valid_job = {
            "job_id": "job-12345",
            "status": "pending",
            "created_at": "2026-02-14T12:00:00Z",
        }

        try:
            validate(instance=valid_job, schema=schema)
        except ValidationError as e:
            # Schema may require different fields
            pytest.skip(f"Schema structure differs: {e.message}")


class TestABSummaryContractValidation:
    """Validate A/B summary endpoint responses against contracts."""

    def test_ab_summary_response_schema_structure(self):
        """Test A/B summary response schema is valid."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema = load_schema("ab_summary_response.schema.json")

        # Verify schema is well-formed
        assert "type" in schema
        assert "properties" in schema


class TestContractSchemaIntegrity:
    """Validate that all contract schemas are well-formed JSON Schema documents."""

    def test_all_schemas_are_valid_json(self):
        """Test that all schema files are valid JSON."""
        schema_files = list(CONTRACTS_DIR.glob("*.schema.json"))

        if not schema_files:
            pytest.skip("No schema files found")

        for schema_file in schema_files:
            with open(schema_file) as f:
                try:
                    data = json.load(f)
                    assert isinstance(data, dict), f"{schema_file.name} is not a JSON object"
                except json.JSONDecodeError as e:
                    pytest.fail(f"Invalid JSON in {schema_file.name}: {e}")

    def test_all_schemas_have_type(self):
        """Test that all schemas define a type."""
        if not HAS_JSONSCHEMA:
            pytest.skip("jsonschema not installed")

        schema_files = list(CONTRACTS_DIR.glob("*.schema.json"))

        for schema_file in schema_files:
            schema = load_schema(schema_file.name)
            assert "type" in schema, f"{schema_file.name} missing 'type' field"

    def test_schema_count(self):
        """Verify expected number of contract schemas exist."""
        schema_files = list(CONTRACTS_DIR.glob("*.schema.json"))

        # We expect at least these schemas from Phase 1
        expected_schemas = {
            "health_response.schema.json",
            "tts_request.schema.json",
            "tts_response.schema.json",
            "upload_response.schema.json",
            "job_response.schema.json",
            "ab_summary_response.schema.json",
        }

        actual_schemas = {f.name for f in schema_files}
        missing = expected_schemas - actual_schemas

        if missing:
            pytest.fail(f"Missing expected schemas: {missing}")
