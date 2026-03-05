"""
Integration Test Configuration

Provides shared fixtures and configuration for integration tests.

Features:
- TestClient fixture for FastAPI
- Response validation helpers
- Request/response logging
- Test categorization markers
- API versioning support
"""

from __future__ import annotations

import json
import logging
import os
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any
from unittest.mock import MagicMock

import pytest
from fastapi.testclient import TestClient

# Add project root to path
project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

# Configure logging
logging.basicConfig(
    level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)

logger = logging.getLogger(__name__)


# =============================================================================
# Test Configuration
# =============================================================================


@dataclass
class IntegrationTestConfig:
    """Configuration for integration tests."""

    api_base_url: str = "http://localhost:8000/api"
    api_version: str = "1.0"
    timeout: float = 60.0
    retry_count: int = 3
    retry_delay: float = 1.0
    enable_logging: bool = True
    validate_responses: bool = True


# Global test config
TEST_CONFIG = IntegrationTestConfig()


# =============================================================================
# Custom Markers
# =============================================================================


def pytest_configure(config):
    """Configure custom pytest markers."""
    config.addinivalue_line("markers", "integration: mark test as integration test")
    config.addinivalue_line("markers", "api: mark test as API test")
    config.addinivalue_line("markers", "slow: mark test as slow running")
    config.addinivalue_line("markers", "requires_backend: mark test as requiring running backend")
    config.addinivalue_line("markers", "requires_gpu: mark test as requiring GPU")
    config.addinivalue_line("markers", "smoke: mark test as smoke test")
    # Phase 4B: Additional markers for error scenario test organization
    config.addinivalue_line("markers", "errors: mark test as error handling test")
    config.addinivalue_line("markers", "negative: mark test as negative test case")
    config.addinivalue_line("markers", "validation: mark test as input validation test")
    config.addinivalue_line("markers", "resource: mark test as resource handling test")
    config.addinivalue_line("markers", "network: mark test as network/connection test")
    config.addinivalue_line("markers", "concurrency: mark test as concurrency test")


# =============================================================================
# Response Tracking
# =============================================================================


@dataclass
class TestResponse:
    """Tracked test response with metadata."""

    status_code: int
    body: Any
    headers: dict[str, str]
    elapsed_ms: float
    request_method: str
    request_url: str
    timestamp: datetime = field(default_factory=datetime.utcnow)

    @property
    def is_success(self) -> bool:
        return 200 <= self.status_code < 300

    @property
    def api_version(self) -> str | None:
        return self.headers.get("x-api-version")


class ResponseTracker:
    """Track responses during test execution."""

    def __init__(self):
        self.responses: list[TestResponse] = []

    def track(self, response, method: str, url: str, elapsed_ms: float):
        """Track a response."""
        try:
            body = response.json()
        except (json.JSONDecodeError, ValueError):
            body = response.text

        tracked = TestResponse(
            status_code=response.status_code,
            body=body,
            headers=dict(response.headers),
            elapsed_ms=elapsed_ms,
            request_method=method,
            request_url=url,
        )
        self.responses.append(tracked)
        return tracked

    def clear(self):
        """Clear tracked responses."""
        self.responses.clear()

    @property
    def last(self) -> TestResponse | None:
        """Get last tracked response."""
        return self.responses[-1] if self.responses else None

    def get_failures(self) -> list[TestResponse]:
        """Get all failed responses."""
        return [r for r in self.responses if not r.is_success]

    def get_slow_responses(self, threshold_ms: float = 1000) -> list[TestResponse]:
        """Get responses slower than threshold."""
        return [r for r in self.responses if r.elapsed_ms > threshold_ms]


# =============================================================================
# Test Client Wrapper
# =============================================================================


class IntegrationTestClient:
    """Enhanced test client with tracking and validation."""

    def __init__(self, client: TestClient, config: IntegrationTestConfig):
        self.client = client
        self.config = config
        self.tracker = ResponseTracker()

    def _make_request(
        self,
        method: str,
        url: str,
        **kwargs,
    ) -> TestResponse:
        """Make request with tracking."""
        # Add version header if configured
        headers = kwargs.get("headers", {})
        if "X-API-Version" not in headers:
            headers["X-API-Version"] = self.config.api_version
        kwargs["headers"] = headers

        # Execute request with timing
        start = time.perf_counter()
        response = getattr(self.client, method)(url, **kwargs)
        elapsed_ms = (time.perf_counter() - start) * 1000

        # Track response
        tracked = self.tracker.track(response, method.upper(), url, elapsed_ms)

        # Log if enabled
        if self.config.enable_logging:
            logger.info(
                "%s %s -> %d (%.2fms)",
                method.upper(),
                url,
                response.status_code,
                elapsed_ms,
            )

        return tracked

    def get(self, url: str, **kwargs) -> TestResponse:
        """Make GET request."""
        return self._make_request("get", url, **kwargs)

    def post(self, url: str, **kwargs) -> TestResponse:
        """Make POST request."""
        return self._make_request("post", url, **kwargs)

    def put(self, url: str, **kwargs) -> TestResponse:
        """Make PUT request."""
        return self._make_request("put", url, **kwargs)

    def delete(self, url: str, **kwargs) -> TestResponse:
        """Make DELETE request."""
        return self._make_request("delete", url, **kwargs)

    def patch(self, url: str, **kwargs) -> TestResponse:
        """Make PATCH request."""
        return self._make_request("patch", url, **kwargs)

    def assert_success(self, response: TestResponse):
        """Assert response is successful."""
        assert response.is_success, f"Expected success, got {response.status_code}: {response.body}"

    def assert_status(self, response: TestResponse, expected: int):
        """Assert response has expected status."""
        assert (
            response.status_code == expected
        ), f"Expected {expected}, got {response.status_code}: {response.body}"


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(scope="session")
def api_base_url():
    """Provide API base URL."""
    return TEST_CONFIG.api_base_url


@pytest.fixture(scope="session")
def test_timeout():
    """Provide test timeout in seconds."""
    return TEST_CONFIG.timeout


@pytest.fixture(scope="session")
def test_config():
    """Provide test configuration."""
    return TEST_CONFIG


@pytest.fixture(scope="session")
def app():
    """Get the FastAPI application."""
    # Import here to avoid circular imports
    try:
        # Reset logging to simple format to avoid correlation_id issues
        import logging

        for handler in logging.root.handlers[:]:
            logging.root.removeHandler(handler)
        logging.basicConfig(
            level=logging.WARNING, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
        )

        from backend.api.main import app as fastapi_app

        return fastapi_app
    except ImportError as e:
        # Return a mock if import fails (for isolated testing)
        logger.warning("Could not import FastAPI app: %s", e)
        return MagicMock()
    except Exception as e:
        logger.warning("Error importing FastAPI app: %s", e)
        return MagicMock()


@pytest.fixture(scope="function")
def client(app):
    """Create a test client for the FastAPI app."""
    return TestClient(app)


@pytest.fixture(scope="function")
def test_client(client):
    """Create enhanced test client with tracking."""
    return IntegrationTestClient(client, TEST_CONFIG)


@pytest.fixture(autouse=True)
def setup_test_environment():
    """Setup test environment before each test."""
    # Store original environment
    original_env = os.environ.copy()

    # Set test-specific environment variables
    os.environ["VOICESTUDIO_TEST_MODE"] = "1"
    os.environ["VOICESTUDIO_DISABLE_TELEMETRY"] = "1"

    yield

    # Restore original environment
    os.environ.clear()
    os.environ.update(original_env)


# =============================================================================
# State Isolation Fixtures (C-T2)
# =============================================================================


@pytest.fixture(autouse=True)
def reset_performance_middleware_metrics():
    """Clear per-request metrics from performance monitoring middleware."""
    yield
    try:
        from backend.api.middleware_setup import get_performance_middleware

        mw = get_performance_middleware()
        if mw is not None:
            mw.reset()
    # ALLOWED: bare except - best effort, failure acceptable
    except Exception:
        pass


# =============================================================================
# Sample Data Fixtures
# =============================================================================


@pytest.fixture
def sample_profile_id():
    """Sample profile ID for testing."""
    return "test-profile-integration"


@pytest.fixture
def sample_project_id():
    """Sample project ID for testing."""
    return "test-project-integration"


@pytest.fixture
def sample_audio_id():
    """Sample audio ID for testing."""
    return "test-audio-integration"


@pytest.fixture
def sample_text():
    """Sample text for synthesis testing."""
    return "This is a test sentence for integration testing."


@pytest.fixture
def sample_synthesis_request(sample_profile_id, sample_text):
    """Sample synthesis request payload."""
    return {
        "profile_id": sample_profile_id,
        "text": sample_text,
        "engine": "xtts_v2",
        "language": "en",
    }


# =============================================================================
# Validation Helpers
# =============================================================================


def validate_response_schema(response: TestResponse, required_fields: list[str]):
    """Validate response body has required fields."""
    if not isinstance(response.body, dict):
        raise AssertionError(f"Expected dict response, got {type(response.body)}")

    missing = [f for f in required_fields if f not in response.body]
    if missing:
        raise AssertionError(f"Missing required fields: {missing}")


def validate_error_response(response: TestResponse):
    """Validate error response has standard format."""
    if response.is_success:
        return

    body = response.body
    if isinstance(body, dict):
        # Standard error should have 'detail' or 'error'
        has_error_info = "detail" in body or "error" in body
        if not has_error_info:
            logger.warning("Non-standard error response: %s", json.dumps(body)[:200])


def validate_version_headers(response: TestResponse):
    """Validate API version headers are present."""
    headers = response.headers
    required = ["x-api-version", "x-min-version"]

    for header in required:
        if header.lower() not in {k.lower() for k in headers}:
            logger.warning("Missing version header: %s", header)


# =============================================================================
# Test Utilities
# =============================================================================


def retry_request(func, *args, retries: int = 3, delay: float = 1.0, **kwargs):
    """Retry a request function with exponential backoff."""
    last_error = None

    for attempt in range(retries):
        try:
            return func(*args, **kwargs)
        except Exception as e:
            last_error = e
            if attempt < retries - 1:
                sleep_time = delay * (2**attempt)
                logger.warning(
                    "Request failed (attempt %d/%d), retrying in %.1fs: %s",
                    attempt + 1,
                    retries,
                    sleep_time,
                    str(e)[:100],
                )
                time.sleep(sleep_time)

    raise last_error


def is_backend_available(base_url: str | None = None) -> bool:
    """Check if backend is available."""
    import requests

    url = base_url or TEST_CONFIG.api_base_url
    try:
        response = requests.get(f"{url}/health", timeout=5)
        return response.status_code == 200
    except Exception:
        return False


# Skip decorator for tests requiring backend
requires_backend = pytest.mark.skipif(
    not is_backend_available(), reason="Backend server is not available"
)
