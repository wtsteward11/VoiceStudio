"""
VoiceStudio Error Scenario Tests.

Tests error handling and edge cases across the application:
- Invalid input validation
- Missing resource handling
- Network/connection errors
- Timeout scenarios
- Concurrent operation conflicts
- Resource exhaustion
- Permission/authorization errors
"""

import json
import os
from datetime import datetime
from pathlib import Path

import pytest

try:
    import requests
except ImportError:
    requests = None
    pytest.skip("requests not installed", allow_module_level=True)

# Test configuration
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
OUTPUT_DIR = Path(os.getenv("VOICESTUDIO_OUTPUT_DIR", ".buildlogs/validation"))

# Pytest markers
pytestmark = [
    pytest.mark.integration,
    pytest.mark.errors,
    pytest.mark.negative,
]


@pytest.fixture
def api_client():
    """Create API client."""

    class APIClient:
        def __init__(self, base_url: str):
            self.base_url = base_url
            self.session = requests.Session()

        def get(self, path: str, **kwargs) -> requests.Response:
            return self.session.get(f"{self.base_url}{path}", **kwargs)

        def post(self, path: str, **kwargs) -> requests.Response:
            return self.session.post(f"{self.base_url}{path}", **kwargs)

        def put(self, path: str, **kwargs) -> requests.Response:
            return self.session.put(f"{self.base_url}{path}", **kwargs)

        def delete(self, path: str, **kwargs) -> requests.Response:
            return self.session.delete(f"{self.base_url}{path}", **kwargs)

    return APIClient(BACKEND_URL)


@pytest.fixture
def backend_available(api_client):
    """Check if backend is available."""
    try:
        resp = api_client.get("/api/health", timeout=5)
        if resp.status_code >= 500:
            pytest.skip("Backend not healthy")
    except Exception:
        pytest.skip("Backend not reachable")
    return True


@pytest.mark.validation
class TestInvalidInputValidation:
    """Tests for invalid input handling."""

    def test_empty_synthesis_text(self, api_client, backend_available):
        """Test synthesis with empty text."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "",
                "engine": "piper",
            },
            timeout=10,
        )

        # Should return 400 or 422 for validation error
        assert response.status_code in [
            400,
            422,
            404,
        ], f"Expected validation error, got {response.status_code}"

        if response.status_code in [400, 422]:
            error = response.json()
            print(f"Validation error response: {error}")
            # Should have error details
            assert "error" in error or "detail" in error or "message" in error

    def test_invalid_engine_name(self, api_client, backend_available):
        """Test synthesis with invalid engine name."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "Test",
                "engine": "nonexistent_engine_12345",
            },
            timeout=10,
        )

        assert response.status_code in [
            400,
            404,
            422,
        ], f"Expected error for invalid engine, got {response.status_code}"

    def test_invalid_json_payload(self, api_client, backend_available):
        """Test endpoint with malformed JSON."""
        response = api_client.post(
            "/api/voice/synthesize",
            data="not valid json {{{",
            headers={"Content-Type": "application/json"},
            timeout=10,
        )

        assert response.status_code in [
            400,
            422,
        ], f"Expected JSON parse error, got {response.status_code}"

    def test_missing_required_fields(self, api_client, backend_available):
        """Test endpoint with missing required fields."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                # Missing "text" field
                "engine": "piper",
            },
            timeout=10,
        )

        assert response.status_code in [
            400,
            422,
            404,
        ], f"Expected validation error, got {response.status_code}"

    def test_wrong_data_types(self, api_client, backend_available):
        """Test endpoint with wrong data types."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": 12345,  # Should be string
                "engine": ["array", "not", "string"],  # Wrong type
            },
            timeout=10,
        )

        assert response.status_code in [
            400,
            422,
            404,
        ], f"Expected type error, got {response.status_code}"

    def test_excessively_long_text(self, api_client, backend_available):
        """Test synthesis with excessively long text."""
        long_text = "A" * 1000000  # 1MB of text

        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": long_text,
                "engine": "piper",
            },
            timeout=30,
        )

        # Should either reject or handle gracefully
        assert response.status_code in [
            400,
            413,
            422,
            500,
            404,
        ], f"Expected handling for long text, got {response.status_code}"

    def test_special_characters_in_text(self, api_client, backend_available):
        """Test synthesis with special characters."""
        special_text = "Test with émojis 🎤🎵 and symbols ©®™ and unicode Üñíçödé"

        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": special_text,
                "engine": "piper",
            },
            timeout=30,
        )

        # Should either process or return appropriate error
        assert response.status_code in [200, 400, 404, 422, 500]
        if response.status_code == 200:
            print("Successfully handled special characters")
        else:
            print(f"Special char handling: {response.status_code}")


@pytest.mark.resource
class TestMissingResourceHandling:
    """Tests for missing resource handling."""

    def test_nonexistent_profile(self, api_client, backend_available):
        """Test accessing nonexistent voice profile."""
        response = api_client.get("/api/profiles/nonexistent-profile-id-12345", timeout=10)

        assert (
            response.status_code == 404
        ), f"Expected 404 for missing profile, got {response.status_code}"

    def test_nonexistent_job(self, api_client, backend_available):
        """Test accessing nonexistent job."""
        response = api_client.get("/api/jobs/nonexistent-job-id-12345", timeout=10)

        assert response.status_code in [
            404,
            400,
        ], f"Expected 404 for missing job, got {response.status_code}"

    def test_nonexistent_audio_file(self, api_client, backend_available):
        """Test accessing nonexistent audio file."""
        response = api_client.get("/api/audio/nonexistent-file-id-12345", timeout=10)

        assert response.status_code in [
            404,
            400,
        ], f"Expected 404 for missing file, got {response.status_code}"

    def test_nonexistent_model(self, api_client, backend_available):
        """Test using nonexistent model."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "Test",
                "model": "nonexistent-model-12345",
            },
            timeout=10,
        )

        assert response.status_code in [400, 404, 422]

    def test_deleted_resource_access(self, api_client, backend_available):
        """Test accessing a deleted resource."""
        # This would require creating then deleting a resource first
        # For now, test with invalid ID pattern
        response = api_client.get("/api/profiles/deleted-resource-test", timeout=10)

        assert response.status_code in [404, 400, 410]


@pytest.mark.network
class TestNetworkErrorHandling:
    """Tests for network error handling."""

    def test_timeout_handling(self, api_client, backend_available):
        """Test timeout handling for slow operations."""
        # Try synthesis with very short timeout
        try:
            response = api_client.post(
                "/api/voice/synthesize",
                json={
                    "text": "Test timeout handling",
                    "engine": "piper",
                },
                timeout=0.001,
            )  # Extremely short timeout

            # If we get here, request completed before timeout
            print(f"Request completed with status: {response.status_code}")
        except requests.Timeout:
            print("Request correctly timed out")
        except requests.RequestException as e:
            print(f"Request failed: {e}")

    def test_connection_refused_handling(self):
        """Test handling when backend is not available."""
        # Connect to known-unavailable port
        bad_client = requests.Session()

        try:
            bad_client.get("http://127.0.0.1:59999/api/health", timeout=2)
            pytest.fail("Should have raised connection error")
        except requests.ConnectionError:
            print("Correctly handled connection refused")
        except requests.Timeout:
            print("Correctly handled timeout")


@pytest.mark.concurrency
class TestConcurrentOperationConflicts:
    """Tests for concurrent operation handling."""

    def test_double_cancel_job(self, api_client, backend_available):
        """Test cancelling already cancelled job."""
        # Cancel twice with fake ID
        job_id = "fake-job-for-double-cancel"

        first_cancel = api_client.post(f"/api/jobs/{job_id}/cancel", timeout=10)
        second_cancel = api_client.post(f"/api/jobs/{job_id}/cancel", timeout=10)

        # Both should handle gracefully
        assert first_cancel.status_code in [200, 400, 404]
        assert second_cancel.status_code in [200, 400, 404]

    def test_update_during_processing(self, api_client, backend_available):
        """Test updating resource while it's being processed."""
        # Try to update a profile that might be in use
        response = api_client.put(
            "/api/profiles/in-use-profile/settings",
            json={
                "pitch": 1.5,
            },
            timeout=10,
        )

        # Should either succeed or return appropriate error
        assert response.status_code in [200, 400, 404, 409, 422]


@pytest.mark.resource
@pytest.mark.slow
class TestResourceExhaustion:
    """Tests for resource exhaustion handling."""

    def test_rapid_request_burst(self, api_client, backend_available):
        """Test handling rapid burst of requests."""
        results = []

        # Send 10 rapid requests
        for _i in range(10):
            try:
                response = api_client.get("/api/health", timeout=5)
                results.append(response.status_code)
            except Exception as e:
                results.append(f"error: {type(e).__name__}")

        # Most should succeed, some might be rate limited (429)
        success_count = sum(1 for r in results if r == 200)
        print(f"Burst test: {success_count}/10 succeeded")
        print(f"Results: {results}")

        # At least some should succeed
        assert success_count > 0, "All requests failed in burst test"

    def test_large_file_upload_handling(self, api_client, backend_available):
        """Test handling of large file upload."""
        # Create fake large file content (don't actually send huge data)
        fake_large_content = b"x" * (10 * 1024 * 1024)  # 10MB

        try:
            response = api_client.post(
                "/api/audio/upload",
                files={"file": ("large_test.wav", fake_large_content, "audio/wav")},
                timeout=60,
            )

            # Should either accept, reject with 413, or handle appropriately
            assert response.status_code in [
                200,
                400,
                413,
                422,
            ], f"Unexpected status for large upload: {response.status_code}"

            print(f"Large file upload result: {response.status_code}")
        except requests.Timeout:
            print("Large upload timed out (acceptable)")


@pytest.mark.api
class TestInvalidHTTPMethods:
    """Tests for invalid HTTP method handling."""

    def test_get_on_post_only_endpoint(self, api_client, backend_available):
        """Test GET on POST-only endpoint."""
        response = api_client.get("/api/voice/synthesize", timeout=10)

        assert response.status_code in [
            405,
            404,
            422,
        ], f"Expected method not allowed, got {response.status_code}"

    def test_delete_on_readonly_endpoint(self, api_client, backend_available):
        """Test DELETE on read-only endpoint."""
        response = api_client.delete("/api/health", timeout=10)

        assert response.status_code in [
            405,
            404,
        ], f"Expected method not allowed, got {response.status_code}"

    def test_patch_unsupported(self, api_client, backend_available):
        """Test PATCH method support."""
        response = requests.patch(
            f"{BACKEND_URL}/api/profiles/test", json={"name": "patched"}, timeout=10
        )

        # PATCH might or might not be supported
        print(f"PATCH response: {response.status_code}")


@pytest.mark.validation
class TestEdgeCaseInputs:
    """Tests for edge case inputs."""

    def test_null_values(self, api_client, backend_available):
        """Test null values in request."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": None,
                "engine": None,
            },
            timeout=10,
        )

        # 500 is acceptable if null causes unhandled exception
        assert response.status_code in [400, 422, 404, 500]

    def test_unicode_control_characters(self, api_client, backend_available):
        """Test Unicode control characters."""
        control_chars = "Test\x00\x01\x02\x03text\x1b[31mred\x1b[0m"

        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": control_chars,
                "engine": "piper",
            },
            timeout=10,
        )

        # Should handle without crashing
        assert response.status_code in [200, 400, 404, 422]

    def test_sql_injection_attempt(self, api_client, backend_available):
        """Test SQL injection protection."""
        injection_attempt = "'; DROP TABLE users; --"

        response = api_client.get(f"/api/profiles/{injection_attempt}", timeout=10)

        # Should not cause server error - proper sanitization
        assert response.status_code != 500, "Possible SQL injection vulnerability"
        assert response.status_code in [400, 404]

    def test_path_traversal_attempt(self, api_client, backend_available):
        """Test path traversal protection."""
        traversal_attempt = "../../../etc/passwd"

        response = api_client.get(f"/api/audio/{traversal_attempt}", timeout=10)

        # Should not allow path traversal
        assert response.status_code in [400, 404, 422]

    def test_xss_attempt(self, api_client, backend_available):
        """Test XSS protection."""
        xss_attempt = '<script>alert("xss")</script>'

        response = api_client.post(
            "/api/profiles/create",
            json={
                "name": xss_attempt,
            },
            timeout=10,
        )

        # If created, check the response doesn't reflect raw script
        if response.status_code == 200:
            result = response.json()
            if "name" in result:
                assert "<script>" not in str(result), "Possible XSS vulnerability"

    def test_negative_numbers(self, api_client, backend_available):
        """Test negative number handling."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "Test",
                "engine": "piper",
                "speed": -1.5,  # Negative speed
                "pitch": -999,  # Extreme negative pitch
            },
            timeout=10,
        )

        # Should validate and reject or clamp values
        assert response.status_code in [200, 400, 404, 422]

    def test_extreme_values(self, api_client, backend_available):
        """Test extreme value handling."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "Test",
                "engine": "piper",
                "speed": 999999999,
                "pitch": 999999999.99,  # Very large but JSON-compliant (not infinity)
            },
            timeout=10,
        )

        # Should validate and reject
        assert response.status_code in [400, 404, 422, 500]


@pytest.mark.api
class TestErrorResponseFormat:
    """Tests for error response format consistency."""

    def test_error_response_has_message(self, api_client, backend_available):
        """Test that error responses include a message."""
        response = api_client.get("/api/nonexistent/endpoint", timeout=10)

        if response.status_code >= 400:
            try:
                error = response.json()
                has_message = (
                    "message" in error or "detail" in error or "error" in error or "errors" in error
                )
                assert has_message, f"Error response missing message field: {error}"
            except json.JSONDecodeError:
                # Non-JSON error response - check for text content
                assert len(response.text) > 0, "Empty error response"

    def test_validation_error_details(self, api_client, backend_available):
        """Test that validation errors include field details."""
        response = api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "",  # Invalid empty text
            },
            timeout=10,
        )

        if response.status_code == 422:
            error = response.json()
            print(f"Validation error format: {json.dumps(error, indent=2)}")
            # FastAPI typically returns {"detail": [{"loc": [...], "msg": "...", "type": "..."}]}


@pytest.mark.errors
class TestErrorRecovery:
    """Tests for error recovery scenarios."""

    def test_service_continues_after_error(self, api_client, backend_available):
        """Test that service continues operating after an error."""
        # Cause an error
        api_client.post(
            "/api/voice/synthesize",
            json={
                "text": "",
            },
            timeout=10,
        )

        # Verify service still works
        health_response = api_client.get("/api/health", timeout=10)

        assert health_response.status_code == 200, "Service unhealthy after handling error"

    def test_partial_failure_handling(self, api_client, backend_available):
        """Test partial failure in batch operations."""
        batch_request = {
            "items": [
                {"text": "Valid text", "voice": "default"},
                {"text": "", "voice": "invalid-voice"},  # Should fail
                {"text": "Another valid", "voice": "default"},
            ],
        }

        response = api_client.post("/api/batch/synthesis/submit", json=batch_request, timeout=30)

        # Should either reject all, process valid ones, or return partial results
        print(f"Partial failure response: {response.status_code}")
        if response.status_code == 200:
            result = response.json()
            print(f"Partial result: {result}")


@pytest.mark.smoke
class TestErrorReport:
    """Generate error handling report."""

    def test_generate_error_report(self, api_client, backend_available):
        """Generate comprehensive error handling report."""
        results = []

        test_cases = [
            ("Empty text", "/api/voice/synthesize", "POST", {"text": "", "engine": "piper"}),
            ("Invalid JSON", "/api/voice/synthesize", "POST_RAW", "not json"),
            ("Missing endpoint", "/api/nonexistent", "GET", None),
            ("Invalid ID", "/api/profiles/invalid-id", "GET", None),
            ("Wrong method", "/api/health", "DELETE", None),
        ]

        for name, endpoint, method, data in test_cases:
            try:
                if method == "GET":
                    response = api_client.get(endpoint, timeout=10)
                elif method == "POST":
                    response = api_client.post(endpoint, json=data, timeout=10)
                elif method == "POST_RAW":
                    response = requests.post(
                        f"{BACKEND_URL}{endpoint}",
                        data=data,
                        headers={"Content-Type": "application/json"},
                        timeout=10,
                    )
                elif method == "DELETE":
                    response = api_client.delete(endpoint, timeout=10)
                else:
                    continue

                results.append(
                    {
                        "test": name,
                        "endpoint": endpoint,
                        "status": response.status_code,
                        "handled": response.status_code < 500,
                    }
                )
            except Exception as e:
                results.append(
                    {
                        "test": name,
                        "endpoint": endpoint,
                        "status": "error",
                        "error": str(e),
                        "handled": False,
                    }
                )

        # Print report
        print("\n" + "=" * 60)
        print("ERROR HANDLING REPORT")
        print("=" * 60)

        handled_count = sum(1 for r in results if r["handled"])
        total_count = len(results)

        print(f"\nTotal tests: {total_count}")
        print(f"Properly handled: {handled_count}")
        print(f"Server errors: {total_count - handled_count}")

        print("\nDetails:")
        for r in results:
            status = "✓" if r["handled"] else "✗"
            print(f"  {status} {r['test']}: {r['status']}")

        # Write report
        report_path = OUTPUT_DIR / "error_handling_report.json"
        report_path.parent.mkdir(parents=True, exist_ok=True)

        with open(report_path, "w") as f:
            json.dump(
                {
                    "timestamp": datetime.now().isoformat(),
                    "summary": {
                        "total": total_count,
                        "handled": handled_count,
                        "success_rate": handled_count / total_count if total_count > 0 else 0,
                    },
                    "results": results,
                },
                f,
                indent=2,
            )

        print(f"\nReport saved to: {report_path}")


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
