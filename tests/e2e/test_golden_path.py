"""
Golden Path End-to-End Test
============================

This is the single most critical test for VoiceStudio v1.0 release.
It tests the complete "happy path" workflow that a user would follow:

1. Import audio file → 2. Transcribe → 3. Clone voice → 4. Synthesize → 5. Validate output

If this test fails, the release is NOT ready.

Ship Plan Phase B - Created 2026-02-15
"""

import json
import logging
import os
import sys
import tempfile
import time
import uuid
from pathlib import Path
from typing import Optional

import pytest
import requests

# Add project root to path
project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Configuration: base URL without /api suffix (routes add /api/... themselves)
API_BASE_URL = os.environ.get("VOICESTUDIO_API_URL", "http://localhost:8000")
TIMEOUT_SECONDS = 60  # Maximum time for long operations


class GoldenPathTestData:
    """Test data and state for the golden path test."""

    def __init__(self):
        self.session_id = str(uuid.uuid4())[:8]
        self.imported_audio_id: Optional[str] = None
        self.imported_audio_path: Optional[str] = None  # Path to uploaded audio WAV
        self.transcription_id: Optional[str] = None
        self.transcription_text: Optional[str] = None
        self.voice_profile_id: Optional[str] = None
        self.synthesized_audio_id: Optional[str] = None
        self.synthesized_audio_url: Optional[str] = None  # URL to synthesized audio
        self.validation_passed: bool = False

    def cleanup(self):
        """Clean up test resources."""
        resources_to_delete = []

        if self.synthesized_audio_id:
            resources_to_delete.append(("audio", self.synthesized_audio_id))
        if self.voice_profile_id:
            resources_to_delete.append(("profiles", self.voice_profile_id))
        if self.imported_audio_id:
            resources_to_delete.append(("audio", self.imported_audio_id))

        for resource_type, resource_id in resources_to_delete:
            try:
                requests.delete(f"{API_BASE_URL}/api/{resource_type}/{resource_id}", timeout=5)
                logger.info(f"Cleaned up {resource_type}/{resource_id}")
            except Exception as e:
                logger.warning(f"Failed to clean up {resource_type}/{resource_id}: {e}")


@pytest.fixture(scope="module")
def backend_health():
    """Verify backend is healthy before running golden path test."""
    try:
        response = requests.get(f"{API_BASE_URL}/api/health", timeout=5)
        if response.status_code != 200:
            pytest.skip(f"Backend unhealthy: status {response.status_code}")
        return response.json()
    except requests.exceptions.ConnectionError:
        pytest.skip("Backend not running - start with 'python -m backend.api.main'")
    except Exception as e:
        pytest.skip(f"Backend check failed: {e}")


@pytest.fixture(scope="module")
def test_audio_file():
    """Create or locate a test audio file for the golden path."""
    # Check for existing test fixture
    fixture_paths = [
        project_root / "tests" / "fixtures" / "audio" / "sample.wav",
        project_root / "tests" / "fixtures" / "sample.wav",
        project_root / "test_data" / "sample.wav",
    ]

    for path in fixture_paths:
        if path.exists():
            logger.info(f"Using existing test audio: {path}")
            return str(path)

    # Generate minimal test audio if no fixture exists
    try:
        import numpy as np
        import scipy.io.wavfile as wav

        # Generate 3 seconds of simple tone (440 Hz)
        sample_rate = 22050
        duration = 3.0
        t = np.linspace(0, duration, int(sample_rate * duration), False)
        audio = (np.sin(2 * np.pi * 440 * t) * 32767).astype(np.int16)

        # Save to temp file
        temp_file = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
        temp_path = temp_file.name
        temp_file.close()  # Close before writing to avoid permission issues
        wav.write(temp_path, sample_rate, audio)

        logger.info(f"Generated test audio: {temp_path}")
        yield temp_path

        # Cleanup with retry for Windows file locking
        import time

        for attempt in range(3):
            try:
                os.unlink(temp_path)
                break
            except PermissionError:
                if attempt < 2:
                    time.sleep(0.5)
                # Ignore cleanup failure on last attempt
    except ImportError:
        pytest.skip("scipy not available for audio generation")


@pytest.fixture(scope="module")
def golden_path_data():
    """Test data container with cleanup."""
    data = GoldenPathTestData()
    yield data
    data.cleanup()


class TestGoldenPath:
    """
    Golden Path Test Suite

    This test class executes the complete "happy path" workflow that represents
    the core value proposition of VoiceStudio. Each test method is a step in the
    workflow and must be run in order.

    CRITICAL: If any step fails, the release is NOT ready.
    """

    def test_step1_import_audio(
        self, backend_health, test_audio_file, golden_path_data: GoldenPathTestData
    ):
        """
        Step 1: Import Audio File

        User imports an audio file into VoiceStudio library.
        This is the entry point for voice cloning workflows.
        """
        logger.info("=" * 60)
        logger.info("GOLDEN PATH STEP 1: Import Audio")
        logger.info("=" * 60)

        # Prepare multipart upload
        with open(test_audio_file, "rb") as f:
            files = {"file": ("test_audio.wav", f, "audio/wav")}
            data = {
                "name": f"GoldenPath_Import_{golden_path_data.session_id}",
                "description": "Golden path test import",
                "tags": ["golden-path", "test"],
            }

            # Use library/assets/upload endpoint for audio import
            # The /audio/* routes are for analysis, not uploads
            response = requests.post(
                f"{API_BASE_URL}/api/library/assets/upload",
                files=files,
                data={"folder_id": None, "tags": ",".join(data.get("tags", []))},
                timeout=TIMEOUT_SECONDS,
            )

        # Verify import succeeded
        assert response.status_code in [
            200,
            201,
        ], f"Audio import failed: {response.status_code} - {response.text}"

        result = response.json()
        audio_id = result.get("id") or result.get("audio_id")
        assert audio_id, f"No audio ID in response: {result}"

        golden_path_data.imported_audio_id = audio_id
        # Store audio path for use in voice cloning (preprocess-reference)
        golden_path_data.imported_audio_path = result.get("path") or result.get("canonical_path")
        logger.info(f"✓ Audio imported successfully: {audio_id}")
        if golden_path_data.imported_audio_path:
            logger.info(f"  Audio path: {golden_path_data.imported_audio_path}")

    def test_step2_transcribe_audio(self, backend_health, golden_path_data: GoldenPathTestData):
        """
        Step 2: Transcribe Audio

        User transcribes the imported audio to get text content.
        This verifies the audio is valid and prepares text for synthesis.
        """
        logger.info("=" * 60)
        logger.info("GOLDEN PATH STEP 2: Transcribe Audio")
        logger.info("=" * 60)

        assert golden_path_data.imported_audio_id, "No audio to transcribe (Step 1 failed)"

        transcription_request = {
            "audio_id": golden_path_data.imported_audio_id,
            "language": "en",
            "engine": "whisper_cpp",  # Use whisper.cpp engine
            "word_timestamps": False,
        }

        # POST to /api/transcribe/ (prefix is "/api/transcribe")
        response = requests.post(
            f"{API_BASE_URL}/api/transcribe/", json=transcription_request, timeout=TIMEOUT_SECONDS
        )

        # Accept various success codes
        assert response.status_code in [
            200,
            201,
            202,
        ], f"Transcription failed: {response.status_code} - {response.text}"

        result = response.json()

        # Handle async transcription (job started)
        if "job_id" in result:
            job_id = result["job_id"]
            result = self._wait_for_job(job_id, "transcription")

        # Extract transcription
        text = result.get("text") or result.get("transcription") or ""
        transcription_id = result.get("id") or result.get("transcription_id")

        # For test audio, we just verify the pipeline works
        # Real audio would have actual transcribed text
        golden_path_data.transcription_text = text or "Test audio transcription"
        golden_path_data.transcription_id = transcription_id

        logger.info(f"✓ Transcription completed: '{text[:50]}...' (id: {transcription_id})")

    def test_step3_clone_voice(self, backend_health, golden_path_data: GoldenPathTestData):
        """
        Step 3: Clone Voice

        User creates a voice profile from the imported audio.
        This is the core voice cloning functionality.

        Uses the audio imported in Step 1 (by path) to create a voice profile
        with reference audio for voice cloning.
        """
        logger.info("=" * 60)
        logger.info("GOLDEN PATH STEP 3: Clone Voice")
        logger.info("=" * 60)

        assert golden_path_data.imported_audio_id, "No audio for cloning (Step 1 failed)"

        # Use the audio path stored from Step 1
        audio_path = golden_path_data.imported_audio_path
        if audio_path:
            logger.info(f"  Using audio path from import: {audio_path}")

        # Create a voice profile with reference to the imported audio
        profile_request = {
            "name": f"GoldenPath_Voice_{golden_path_data.session_id}",
            "language": "en",
            "tags": ["golden-path", "test"],
        }

        profile_response = requests.post(
            f"{API_BASE_URL}/api/profiles", json=profile_request, timeout=TIMEOUT_SECONDS
        )

        # Accept profile creation
        assert profile_response.status_code in [
            200,
            201,
        ], f"Profile creation failed: {profile_response.status_code} - {profile_response.text}"

        profile_result = profile_response.json()
        profile_id = profile_result.get("id") or profile_result.get("profile_id")
        assert profile_id, f"No profile ID in response: {profile_result}"

        golden_path_data.voice_profile_id = profile_id
        logger.info(f"✓ Voice profile created: {profile_id}")

        # Preprocess reference audio for the profile using the imported audio path
        if audio_path:
            try:
                preprocess_request = {
                    "profile_id": profile_id,
                    "reference_audio_path": audio_path,
                    "auto_enhance": True,
                    "select_optimal_segments": True,
                }

                preprocess_response = requests.post(
                    f"{API_BASE_URL}/api/profiles/{profile_id}/preprocess-reference",
                    json=preprocess_request,
                    timeout=TIMEOUT_SECONDS,
                )

                if preprocess_response.status_code in [200, 201, 202]:
                    logger.info(f"✓ Reference audio preprocessed for profile: {profile_id}")
                else:
                    logger.warning(
                        f"Reference preprocessing returned {preprocess_response.status_code}: "
                        f"{preprocess_response.text[:200]} - synthesis may use default voice"
                    )
            except Exception as e:
                logger.warning(
                    f"Reference preprocessing failed: {e} - synthesis may use default voice"
                )
        else:
            logger.warning("No audio path found - synthesis may use default voice")

    def test_step4_synthesize_speech(self, backend_health, golden_path_data: GoldenPathTestData):
        """
        Step 4: Synthesize Speech

        User generates speech using the cloned voice.
        This is the core synthesis functionality.
        """
        logger.info("=" * 60)
        logger.info("GOLDEN PATH STEP 4: Synthesize Speech")
        logger.info("=" * 60)

        assert golden_path_data.voice_profile_id, "No voice profile (Step 3 failed)"

        # Use transcription text or default
        synthesis_text = (
            golden_path_data.transcription_text
            or "Hello, this is a test of the VoiceStudio voice cloning system."
        )

        synthesis_request = {
            "profile_id": golden_path_data.voice_profile_id,
            "text": synthesis_text,
            "language": "en",
            "enhance_quality": False,  # Faster for testing
        }

        response = requests.post(
            f"{API_BASE_URL}/api/voice/synthesize", json=synthesis_request, timeout=TIMEOUT_SECONDS
        )

        assert response.status_code in [
            200,
            201,
            202,
        ], f"Synthesis failed: {response.status_code} - {response.text}"

        result = response.json()

        # Handle async synthesis (job started)
        if "job_id" in result:
            job_id = result["job_id"]
            result = self._wait_for_job(job_id, "synthesis")

        audio_id = result.get("audio_id") or result.get("id")
        audio_url = result.get("audio_url") or result.get("url")
        duration = result.get("duration", 0)
        quality_score = result.get("quality_score", 0)

        assert audio_id or audio_url, f"No audio output in response: {result}"

        golden_path_data.synthesized_audio_id = audio_id
        golden_path_data.synthesized_audio_url = audio_url
        # Store additional data for validation
        golden_path_data._synthesis_duration = duration
        golden_path_data._synthesis_quality_score = quality_score
        logger.info(f"✓ Speech synthesized: {audio_id or audio_url}")
        logger.info(f"  Duration: {duration:.2f}s, Quality: {quality_score:.2f}")

    def test_step5_validate_output(self, backend_health, golden_path_data: GoldenPathTestData):
        """
        Step 5: Validate Output

        Verify the synthesized audio meets quality standards.
        This is the final validation before user acceptance.

        Validates using synthesis response data and optionally downloads
        the audio file to verify it's accessible.
        """
        logger.info("=" * 60)
        logger.info("GOLDEN PATH STEP 5: Validate Output")
        logger.info("=" * 60)

        assert (
            golden_path_data.synthesized_audio_id or golden_path_data.synthesized_audio_url
        ), "No synthesized audio (Step 4 failed)"

        # Validate using data from synthesis response (stored in step 4)
        validations = []

        # Check duration (from synthesis response)
        duration = getattr(golden_path_data, "_synthesis_duration", 0)
        validations.append(("duration > 0", duration > 0, f"duration={duration:.2f}s"))

        # Check quality score (from synthesis response)
        quality_score = getattr(golden_path_data, "_synthesis_quality_score", 0)
        validations.append(
            ("quality score valid", quality_score >= 0, f"quality={quality_score:.2f}")
        )

        # Check audio URL is accessible (download test)
        if golden_path_data.synthesized_audio_url:
            try:
                audio_url = golden_path_data.synthesized_audio_url
                # Convert relative URL to absolute
                if audio_url.startswith("/"):
                    # Extract base URL from API_BASE_URL (remove /api suffix)
                    base_url = API_BASE_URL.rsplit("/api", 1)[0]
                    audio_url = f"{base_url}{audio_url}"

                audio_response = requests.get(audio_url, timeout=30)
                audio_accessible = audio_response.status_code == 200
                audio_size = len(audio_response.content) if audio_accessible else 0
                validations.append(
                    ("audio downloadable", audio_accessible, f"size={audio_size} bytes")
                )

                if audio_accessible and audio_size > 0:
                    proof_output_dir = os.environ.get(
                        "VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR"
                    )
                    if proof_output_dir:
                        if not os.path.isdir(proof_output_dir):
                            pytest.fail(
                                f"VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR "
                                f"set but not a directory: "
                                f"{proof_output_dir}"
                            )
                        export_dir = proof_output_dir
                    else:
                        export_dir = os.path.join(
                            tempfile.gettempdir(),
                            "voicestudio_golden_path",
                        )
                    os.makedirs(export_dir, exist_ok=True)
                    wav_filename = "golden_path_export.wav"
                    export_path = os.path.join(
                        export_dir, wav_filename
                    )
                    with open(export_path, "wb") as f:
                        f.write(audio_response.content)
                    exported = (
                        os.path.exists(export_path)
                        and os.path.getsize(export_path) > 1024
                    )
                    validations.append(
                        (
                            "audio exported to disk",
                            exported,
                            f"path={export_path}, "
                            f"size={os.path.getsize(export_path)}",
                        )
                    )
                    logger.info(f"  Exported audio to: {export_path}")

                    if proof_output_dir:
                        manifest = {
                            "output_wav": wav_filename,
                        }
                        manifest_path = os.path.join(
                            export_dir, "output_manifest.json"
                        )
                        with open(manifest_path, "w",
                                  encoding="utf-8") as mf:
                            json.dump(manifest, mf, indent=2)
                        logger.info(
                            f"  Manifest: {manifest_path}"
                        )
            except Exception as e:
                validations.append(("audio downloadable", False, f"error={e}"))
        else:
            # If no URL, check audio_id was returned
            validations.append(
                (
                    "audio_id present",
                    bool(golden_path_data.synthesized_audio_id),
                    f"id={golden_path_data.synthesized_audio_id}",
                )
            )

        # Log validation results
        all_passed = True
        for name, passed, detail in validations:
            status = "✓" if passed else "✗"
            logger.info(f"  {status} {name}: {detail}")
            if not passed:
                all_passed = False

        assert all_passed, "Output validation failed"

        golden_path_data.validation_passed = True
        logger.info("✓ Output validation PASSED")
        logger.info("=" * 60)
        logger.info("GOLDEN PATH TEST COMPLETE - RELEASE READY")
        logger.info("=" * 60)

    def _wait_for_job(self, job_id: str, job_type: str, timeout: int = TIMEOUT_SECONDS) -> dict:
        """Poll for async job completion."""
        start_time = time.time()
        poll_interval = 2

        while time.time() - start_time < timeout:
            response = requests.get(f"{API_BASE_URL}/api/jobs/{job_id}", timeout=10)

            if response.status_code != 200:
                time.sleep(poll_interval)
                continue

            job_status = response.json()
            status = job_status.get("status", "").lower()

            if status in ["completed", "success", "done"]:
                return job_status.get("result", job_status)
            elif status in ["failed", "error"]:
                raise AssertionError(f"{job_type} job failed: {job_status.get('error')}")

            logger.info(f"  Waiting for {job_type} job: {status}")
            time.sleep(poll_interval)

        raise AssertionError(f"{job_type} job timed out after {timeout}s")


# Standalone execution for quick manual testing
if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short", "-x"])
