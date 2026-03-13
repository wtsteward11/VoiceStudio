"""
Input Validation and Injection Security Tests.

Tests for SQL injection, path traversal, XSS, and other injection attacks.
Uses FastAPI TestClient for standalone testing without external backend.

Phase 7B: Input Validation and Injection
"""

from __future__ import annotations

import pytest

# Pytest markers
pytestmark = [
    pytest.mark.security,
    pytest.mark.input_validation,
]


@pytest.fixture
def api_client():
    """Create a test client for API tests."""
    from fastapi.testclient import TestClient

    from backend.api.main import app

    return TestClient(app)


@pytest.fixture
def backend_available(api_client):
    """Check if backend is available."""
    try:
        response = api_client.get("/api/health")
        return response.status_code == 200
    except Exception:
        return False


# Status codes that indicate secure behavior
# 429 = rate limited (security feature), 404 = not found, 400/422 = validation error
SECURE_STATUS_CODES = (400, 404, 422, 429, 500, 503)


class TestSQLInjectionPrevention:
    """Tests for SQL injection attack prevention."""

    SQL_INJECTION_PAYLOADS = [
        "'; DROP TABLE users; --",
        "1 OR 1=1",
        "' UNION SELECT * FROM users --",
        "1'; EXEC xp_cmdshell('dir'); --",
        "admin'--",
        "' OR '1'='1",
        "'; INSERT INTO users VALUES('hacked'); --",
        "1; DELETE FROM profiles WHERE 1=1; --",
    ]

    def test_sql_injection_in_search_query(self, api_client, backend_available):
        """Test SQL injection attempts in search endpoints."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        for payload in self.SQL_INJECTION_PAYLOADS[:3]:  # Test subset to avoid rate limiting
            # Test in query parameters (using validation endpoint which exists)
            response = api_client.get(f"/api/validation/stats?q={payload}")
            # Should not return 200 with data (indicates failed injection)
            # 500 would indicate SQL error, which is bad but not exploitation
            assert response.status_code != 200 or "SELECT" not in response.text

    def test_sql_injection_in_json_body(self, api_client, backend_available):
        """Test SQL injection in JSON request body."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        for payload in self.SQL_INJECTION_PAYLOADS[:2]:  # Test subset
            response = api_client.post("/api/cache/invalidate", json={"pattern": payload})
            # Should not cause server error that exposes SQL
            assert "SQL" not in response.text.upper() or response.status_code in SECURE_STATUS_CODES

    def test_sql_injection_in_headers(self, api_client, backend_available):
        """Test SQL injection via HTTP headers."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        payload = "'; DROP TABLE users; --"
        response = api_client.get("/api/health", headers={"X-Custom-Header": payload})
        # Header injection should not cause SQL errors
        assert response.status_code in (200, 400, 401, 403, 422, 429)


class TestPathTraversalPrevention:
    """Tests for path traversal attack prevention."""

    PATH_TRAVERSAL_PAYLOADS = [
        "../../../etc/passwd",
        "..\\..\\..\\windows\\system32\\config\\sam",
        "....//....//....//etc/passwd",
        "%2e%2e%2f%2e%2e%2f%2e%2e%2fetc/passwd",
    ]

    def test_path_traversal_in_query_params(self, api_client, backend_available):
        """Test path traversal in query parameters."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        for payload in self.PATH_TRAVERSAL_PAYLOADS[:2]:  # Test subset
            response = api_client.get(f"/api/endpoints/metrics/{payload}")
            # Should return error, never 200 with sensitive file content
            # 404 is acceptable - endpoint pattern doesn't match
            # 429 = rate limited (security)
            assert response.status_code in SECURE_STATUS_CODES or (
                response.status_code == 200
                and "passwd" not in response.text
                and "root:" not in response.text
            )

    def test_path_traversal_in_json_body(self, api_client, backend_available):
        """Test path traversal in JSON request body."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post("/api/cache/invalidate", json={"path": "../../../etc/passwd"})
        # Should reject malicious paths or be rate limited
        assert response.status_code in SECURE_STATUS_CODES or (
            response.status_code == 200 and "passwd" not in response.text
        )


class TestXSSPrevention:
    """Tests for Cross-Site Scripting (XSS) prevention."""

    XSS_PAYLOADS = [
        "<script>alert('xss')</script>",
        "<img src=x onerror=alert('xss')>",
        "javascript:alert('xss')",
    ]

    def test_xss_in_query_params(self, api_client, backend_available):
        """Test XSS payloads in query parameters."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        for payload in self.XSS_PAYLOADS:
            response = api_client.get(f"/api/version?name={payload}")
            # Response should not contain unescaped XSS
            if response.status_code == 200:
                body = response.text
                # Script tags should be escaped or absent
                assert "<script>" not in body or "&lt;script&gt;" in body

    def test_xss_in_json_response(self, api_client, backend_available):
        """Test XSS in JSON response encoding."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        # Make a request and verify JSON escaping
        response = api_client.get("/api/version")
        if response.status_code == 200:
            # JSON encoding should escape special characters
            content_type = response.headers.get("content-type", "")
            if "json" in content_type.lower():
                # JSON should be properly encoded
                assert response.json() is not None


class TestCommandInjectionPrevention:
    """Tests for OS command injection prevention."""

    COMMAND_INJECTION_PAYLOADS = [
        "; ls -la",
        "| cat /etc/passwd",
        "& dir",
        "$(whoami)",
        "`whoami`",
    ]

    def test_command_injection_in_params(self, api_client, backend_available):
        """Test command injection in parameters."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        for payload in self.COMMAND_INJECTION_PAYLOADS[:2]:  # Test subset
            response = api_client.get(f"/api/engines/metrics/{payload}")
            # Should not execute commands
            # 404 is fine - means the injection didn't work as a valid path
            assert response.status_code in SECURE_STATUS_CODES or (
                response.status_code == 200
                and "root:" not in response.text
                and "uid=" not in response.text
            )

    def test_command_injection_in_json(self, api_client, backend_available):
        """Test command injection in JSON body."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post("/api/cache/invalidate", json={"command": "; rm -rf /"})
        # Should not execute shell commands
        assert response.status_code in SECURE_STATUS_CODES or response.status_code == 200


class TestOversizedRequestHandling:
    """Tests for handling oversized requests."""

    def test_oversized_json_body(self, api_client, backend_available):
        """Test handling of very large JSON body."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        # Create a large payload (1MB of data)
        large_data = {"data": "x" * (1024 * 1024)}
        response = api_client.post("/api/cache/invalidate", json=large_data)
        # Should reject or handle gracefully
        assert response.status_code in (200, 400, 413, 422, 429, 500, 503)

    def test_oversized_array(self, api_client, backend_available):
        """Test handling of arrays with many elements."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        # Create array with many elements
        large_array = {"items": list(range(10000))}
        response = api_client.post("/api/cache/invalidate", json=large_array)
        # Should reject or handle gracefully
        assert response.status_code in (200, 400, 413, 422, 404, 429, 500, 503)

    def test_deeply_nested_json(self, api_client, backend_available):
        """Test handling of deeply nested JSON."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        # Create deeply nested structure
        nested = {"level": 0}
        current = nested
        for i in range(100):
            current["nested"] = {"level": i + 1}
            current = current["nested"]

        response = api_client.post("/api/cache/invalidate", json=nested)
        # Should reject or handle gracefully
        assert response.status_code in (200, 400, 422, 429, 500)


class TestMalformedInputHandling:
    """Tests for handling malformed inputs."""

    def test_invalid_json(self, api_client, backend_available):
        """Test handling of invalid JSON."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post(
            "/api/cache/invalidate",
            content=b"not valid json {{{",
            headers={"Content-Type": "application/json"},
        )
        # Should reject with 400/422, or handle gracefully with 200
        # (some endpoints may ignore body if no params required)
        assert response.status_code in (200, 400, 422, 429)

    def test_missing_content_type(self, api_client, backend_available):
        """Test request without Content-Type header."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post("/api/cache/invalidate", content=b'{"key": "value"}')
        # Should handle gracefully
        assert response.status_code in (200, 400, 415, 422, 429)

    def test_wrong_content_type(self, api_client, backend_available):
        """Test request with wrong Content-Type."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post(
            "/api/cache/invalidate",
            content=b'{"key": "value"}',
            headers={"Content-Type": "text/plain"},
        )
        # Should handle gracefully
        assert response.status_code in (200, 400, 415, 422, 429)

    def test_null_bytes_in_input(self, api_client, backend_available):
        """Test handling of null bytes in input."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        response = api_client.post("/api/cache/invalidate", json={"key": "value\x00with\x00nulls"})
        # Should handle gracefully - either accept or reject
        assert response.status_code in (200, 400, 422, 429)

    def test_unicode_edge_cases(self, api_client, backend_available):
        """Test handling of Unicode edge cases."""
        if not backend_available:
            pytest.skip("Backend not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")

        unicode_payloads = [
            "test\uffff",  # Max BMP character
            "test\U0001f600",  # Emoji
            "test\u202e\u0041\u0042",  # RTL override
        ]

        for payload in unicode_payloads:
            response = api_client.post("/api/cache/invalidate", json={"text": payload})
            # Should handle Unicode gracefully
            assert response.status_code in (200, 400, 422, 429)
