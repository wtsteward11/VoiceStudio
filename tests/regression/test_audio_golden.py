"""
Golden Audio Regression Tests

Compares synthesized audio against golden reference files to detect quality drift.
Uses configurable tolerances for MOS, similarity, LUFS, and other metrics.

Usage:
    # Run golden tests
    pytest tests/regression/test_audio_golden.py -v

    # Update golden files (after intentional changes)
    pytest tests/regression/test_audio_golden.py --update-golden

    # Run for specific engine
    pytest tests/regression/test_audio_golden.py -k xtts
"""

from __future__ import annotations

import logging
import os
import tempfile
from pathlib import Path
from typing import Any

import pytest

from .audio_comparison import (
    GoldenComparisonReport,
    compare_with_golden,
    load_golden_config,
    update_golden_metadata,
)

logger = logging.getLogger(__name__)

# Paths
TESTS_DIR = Path(__file__).parent.parent
FIXTURES_DIR = TESTS_DIR / "fixtures" / "golden"
CONFIG_PATH = FIXTURES_DIR / "config.json"


def pytest_addoption(parser):
    """Add --update-golden option to pytest."""
    parser.addoption(
        "--update-golden",
        action="store_true",
        default=False,
        help="Update golden reference files instead of comparing",
    )


@pytest.fixture
def update_golden(request) -> bool:
    """Check if --update-golden flag is set."""
    return request.config.getoption("--update-golden", default=False)


@pytest.fixture
def golden_config() -> dict[str, Any]:
    """Load golden test configuration."""
    if not CONFIG_PATH.exists():
        pytest.skip(f"Golden config not found: {CONFIG_PATH}")
    return load_golden_config(CONFIG_PATH)


@pytest.fixture
def tolerances(golden_config: dict[str, Any]) -> dict[str, float]:
    """Get tolerance configuration."""
    return golden_config.get(
        "tolerances",
        {
            "mos": 0.3,
            "similarity": 0.1,
            "lufs": 2.0,
            "snr_db": 3.0,
        },
    )


def get_backend_client():
    """Get backend client for synthesis, or None if unavailable."""
    try:
        import httpx

        backend_url = os.environ.get("VOICESTUDIO_BACKEND_URL", "http://127.0.0.1:8000")
        client = httpx.Client(base_url=backend_url, timeout=120.0)

        # Check if backend is available
        try:
            response = client.get("/api/health")
            if response.status_code == 200:
                return client
        # ALLOWED: bare except - Best effort, failure is acceptable
        except Exception:
            pass

        return None
    except ImportError:
        return None


def synthesize_audio(
    client,
    text: str,
    engine: str,
    voice_profile: str,
    output_path: Path,
) -> bool:
    """
    Synthesize audio using the backend API.

    Returns True if successful, False otherwise.
    """
    try:
        response = client.post(
            "/api/voice/synthesize",
            json={
                "text": text,
                "engine_id": engine,
                "voice_profile": voice_profile,
            },
        )

        if response.status_code != 200:
            logger.warning(f"Synthesis failed: {response.status_code} - {response.text}")
            return False

        result = response.json()
        audio_id = result.get("audio_id")

        if not audio_id:
            logger.warning("No audio_id in synthesis response")
            return False

        # Download the audio
        audio_response = client.get(f"/api/voice/audio/{audio_id}")
        if audio_response.status_code != 200:
            logger.warning(f"Audio download failed: {audio_response.status_code}")
            return False

        output_path.write_bytes(audio_response.content)
        return True

    except Exception as e:
        logger.exception(f"Synthesis error: {e}")
        return False


def check_engine_available(client, engine: str) -> bool:
    """Check if an engine is available."""
    if client is None:
        return False

    try:
        response = client.get("/api/engines/status")
        if response.status_code != 200:
            return False

        engines = response.json()
        for eng in engines.get("engines", []):
            if eng.get("engine_id") == engine and eng.get("status") == "ready":
                return True

        return False
    except Exception:
        return False


class TestGoldenAudio:
    """Golden audio regression tests."""

    @pytest.fixture(autouse=True)
    def setup(self, golden_config: dict[str, Any], tolerances: dict[str, float]):
        """Set up test fixtures."""
        self.config = golden_config
        self.tolerances = tolerances
        self.client = get_backend_client()
        self.skip_if_unavailable = golden_config.get("skip_if_engine_unavailable", True)

    def _run_golden_test(
        self,
        engine: str,
        test_case: dict[str, Any],
        update_mode: bool,
    ) -> GoldenComparisonReport | None:
        """Run a single golden test case."""
        test_id = test_case["id"]
        text = test_case["text"]
        voice_profile = test_case.get("voice_profile", "default")
        golden_file = test_case["golden_file"]

        golden_audio_path = FIXTURES_DIR / golden_file
        golden_metadata_path = golden_audio_path.with_suffix(".json")

        # Check engine availability
        if not check_engine_available(self.client, engine):
            if self.skip_if_unavailable:
                pytest.skip(f"Engine {engine} not available")
            else:
                pytest.fail(f"Engine {engine} not available")

        # Generate audio
        with tempfile.TemporaryDirectory() as tmpdir:
            generated_path = Path(tmpdir) / f"{test_id}.wav"

            if not synthesize_audio(self.client, text, engine, voice_profile, generated_path):
                if update_mode:
                    pytest.fail(f"Failed to synthesize audio for golden update: {test_id}")
                else:
                    pytest.fail(f"Failed to synthesize audio for test: {test_id}")

            if update_mode:
                # Update golden files
                golden_audio_path.parent.mkdir(parents=True, exist_ok=True)

                # Copy generated audio to golden location
                import shutil

                shutil.copy2(generated_path, golden_audio_path)

                # Update metadata
                update_golden_metadata(
                    golden_audio_path,
                    golden_metadata_path,
                    test_case,
                    engine,
                )

                logger.info(f"Updated golden files for {engine}/{test_id}")
                return None
            else:
                # Compare with golden
                report = compare_with_golden(
                    generated_path,
                    golden_audio_path,
                    golden_metadata_path,
                    self.tolerances,
                    test_id,
                    engine,
                )

                return report

    @pytest.mark.parametrize("engine", ["xtts", "chatterbox", "piper"])
    def test_golden_hello_world(
        self,
        engine: str,
        golden_config: dict[str, Any],
        update_golden: bool,
    ):
        """Test golden audio for hello_world test case."""
        engine_config = golden_config.get("engines", {}).get(engine, {})

        if not engine_config.get("enabled", False):
            pytest.skip(f"Engine {engine} is disabled in config")

        test_cases = engine_config.get("test_cases", [])
        hello_world_case = next(
            (tc for tc in test_cases if tc["id"] == "hello_world"),
            None,
        )

        if not hello_world_case:
            pytest.skip(f"No hello_world test case for {engine}")

        if self.client is None:
            pytest.skip("Backend not available")

        report = self._run_golden_test(engine, hello_world_case, update_golden)

        if report is None:
            # Update mode
            return

        if report.error:
            pytest.fail(f"Golden test error: {report.error}")

        if not report.passed:
            failure_messages = [r.message for r in report.results if not r.passed]
            pytest.fail(
                f"Golden test failed for {engine}/{hello_world_case['id']}:\n"
                + "\n".join(failure_messages)
            )


class TestGoldenInfrastructure:
    """Tests for golden test infrastructure."""

    def test_config_exists(self):
        """Test that golden config exists."""
        assert CONFIG_PATH.exists(), f"Golden config not found: {CONFIG_PATH}"

    def test_config_valid(self, golden_config: dict[str, Any]):
        """Test that golden config is valid."""
        assert "tolerances" in golden_config, "Config missing tolerances"
        assert "engines" in golden_config, "Config missing engines"

        tolerances = golden_config["tolerances"]
        assert "mos" in tolerances, "Missing MOS tolerance"
        assert "lufs" in tolerances, "Missing LUFS tolerance"

    def test_golden_directories_exist(self, golden_config: dict[str, Any]):
        """Test that golden directories exist for enabled engines."""
        for engine, config in golden_config.get("engines", {}).items():
            if config.get("enabled", False):
                engine_dir = FIXTURES_DIR / engine
                assert engine_dir.exists(), f"Missing golden directory: {engine_dir}"

    def test_audio_comparison_import(self):
        """Test that audio comparison module imports correctly."""
        from .audio_comparison import (
            ComparisonResult,
            GoldenComparisonReport,
            calculate_metrics,
            compare_metrics,
        )

        assert ComparisonResult is not None
        assert GoldenComparisonReport is not None
        assert calculate_metrics is not None
        assert compare_metrics is not None
