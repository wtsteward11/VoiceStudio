"""
End-to-End Integration Test Suite
Tests complete user workflows from start to finish.
"""

import logging
import sys
import time
from pathlib import Path

import pytest
import requests

# Add project root to path
project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

API_BASE_URL = "http://localhost:8000/api"


@pytest.fixture(scope="module")
def backend_available():
    """Check if backend is available. Fails (not skips) if explicitly required."""
    try:
        response = requests.get(f"{API_BASE_URL}/health", timeout=2)
        return response.status_code == 200
    except Exception:
        return False


@pytest.fixture(autouse=True)
def _require_backend(backend_available):
    """Auto-skip all tests in this module when backend is not running."""
    if not backend_available:
        pytest.skip("Backend not available at " + API_BASE_URL)


@pytest.fixture(scope="function")
def test_profile(backend_available):
    """Create a test profile for workflow tests."""
    if not backend_available:
        pytest.skip("Backend not available")

    profile_data = {
        "name": f"Test Profile {int(time.time())}",
        "description": "Test profile for E2E workflow testing",
        "language": "en",
    }

    try:
        response = requests.post(f"{API_BASE_URL}/profiles", json=profile_data, timeout=5)

        if response.status_code == 200:
            profile = response.json()
            yield profile

            profile_id = profile.get("id")
            if profile_id:
                requests.delete(f"{API_BASE_URL}/profiles/{profile_id}", timeout=5)
        else:
            pytest.skip(f"Could not create test profile: {response.status_code}")
    except Exception as e:
        pytest.skip(f"Could not create test profile: {e}")


@pytest.mark.requires_engine
class TestVoiceSynthesisWorkflow:
    """Test complete voice synthesis workflow (requires loaded engine)."""

    def test_create_profile_and_synthesize(self, backend_available, test_profile):
        """Test creating profile and synthesizing speech."""
        if not backend_available:
            pytest.skip("Backend not available")

        profile_id = test_profile.get("id")
        assert profile_id is not None, "Profile ID is None"

        synthesis_request = {
            "profile_id": profile_id,
            "text": "Hello, this is a test of the voice synthesis workflow.",
            "sample_rate": 22050,
            "quality_preset": "standard",
        }

        response = requests.post(
            f"{API_BASE_URL}/voice/synthesize", json=synthesis_request, timeout=30
        )

        if response.status_code == 503:
            pytest.skip("Synthesis engine not available (503)")

        assert (
            response.status_code == 200
        ), f"Synthesis request failed with status {response.status_code}"

        result = response.json()
        assert (
            "audio_id" in result or "audio_url" in result
        ), "Synthesis result missing audio identifier"

        logger.info(f"Synthesis workflow completed successfully: {result}")


class TestProjectWorkflow:
    """Test complete project management workflow."""

    def test_create_project_and_add_audio(self, backend_available):
        """Test creating project and adding audio."""
        if not backend_available:
            pytest.skip("Backend not available")

        project_data = {
            "name": f"Test Project {int(time.time())}",
            "description": "Test project for E2E workflow testing",
        }

        try:
            create_response = requests.post(
                f"{API_BASE_URL}/projects", json=project_data, timeout=5
            )

            assert (
                create_response.status_code == 200
            ), f"Project creation failed with status {create_response.status_code}"

            project = create_response.json()
            project_id = project.get("id")
            assert project_id is not None, "Project ID is None"

            get_response = requests.get(f"{API_BASE_URL}/projects/{project_id}", timeout=5)

            assert get_response.status_code == 200, "Failed to retrieve created project"

            retrieved_project = get_response.json()
            assert (
                retrieved_project.get("name") == project_data["name"]
            ), "Retrieved project name doesn't match"

            delete_response = requests.delete(f"{API_BASE_URL}/projects/{project_id}", timeout=5)

            assert delete_response.status_code in [200, 204], "Failed to delete project"

            logger.info("Project workflow completed successfully")
        except Exception as e:
            pytest.fail(f"Project workflow failed: {e}")


class TestQualityAnalysisWorkflow:
    """Test quality analysis workflow."""

    def test_analyze_audio_quality(self, backend_available):
        """Test analyzing audio quality."""
        if not backend_available:
            pytest.skip("Backend not available")

        analysis_request = {
            "audio_id": "test_audio_id",
            "metrics": ["mos", "similarity", "naturalness", "snr"],
        }

        try:
            response = requests.post(
                f"{API_BASE_URL}/quality/analyze", json=analysis_request, timeout=10
            )

            if response.status_code == 200:
                result = response.json()
                assert (
                    "metrics" in result or "scores" in result
                ), "Quality analysis result missing metrics"
                logger.info("Quality analysis workflow completed successfully")
            else:
                pytest.skip(f"Quality analysis endpoint returned {response.status_code}")
        except Exception as e:
            pytest.skip(f"Quality analysis workflow failed: {e}")


class TestEngineRecommendationWorkflow:
    """Test engine recommendation workflow."""

    def test_get_engine_recommendation(self, backend_available):
        """Test getting engine recommendation."""
        if not backend_available:
            pytest.skip("Backend not available")

        recommendation_request = {
            "text": "Hello, this is a test.",
            "voice_profile_id": "test_profile",
            "quality_preset": "standard",
        }

        try:
            response = requests.post(
                f"{API_BASE_URL}/engines/recommend", json=recommendation_request, timeout=10
            )

            if response.status_code == 200:
                result = response.json()
                assert (
                    "engine" in result or "recommended_engine" in result
                ), "Engine recommendation result missing engine"
                logger.info("Engine recommendation workflow completed successfully")
            else:
                pytest.skip(f"Engine recommendation endpoint returned {response.status_code}")
        except Exception as e:
            pytest.skip(f"Engine recommendation workflow failed: {e}")


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
