"""
Core Workflow E2E Test (UI Alive Plan Step 3).

Validates the target workflow:
  import audio → create/select profile → synthesize → audio in library → playback works.

Uses backend API; UI smoke is covered by MainWindow.Smoke.RunGateCUiSmokeNavigationAsync.
"""

from __future__ import annotations

import logging
import os
import sys
import tempfile
import time
import uuid
from pathlib import Path

import pytest
import requests

pytestmark = [pytest.mark.e2e, pytest.mark.workflow]

project_root = Path(__file__).parent.parent.parent
sys.path.insert(0, str(project_root))

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

API_BASE_URL = os.environ.get("VOICESTUDIO_API_URL", "http://localhost:8000")
TIMEOUT_SECONDS = 120


@pytest.fixture(scope="module")
def backend_health():
    """Verify backend is healthy."""
    try:
        response = requests.get(f"{API_BASE_URL}/api/health", timeout=5)
        if response.status_code != 200:
            pytest.skip(f"Backend unhealthy: {response.status_code}")
        return response.json()
    except requests.exceptions.ConnectionError:
        pytest.skip("Backend not running")
    except Exception as e:
        pytest.skip(f"Backend check failed: {e}")


@pytest.fixture(scope="module")
def test_audio():
    """Create minimal test audio."""
    fixture_paths = [
        project_root / "tests" / "fixtures" / "audio" / "sample.wav",
        project_root / "tests" / "fixtures" / "sample.wav",
    ]
    for p in fixture_paths:
        if p.exists():
            return str(p)
    try:
        import numpy as np
        import scipy.io.wavfile as wav

        sample_rate, duration = 22050, 2.0
        t = np.linspace(0, duration, int(sample_rate * duration), False)
        audio = (np.sin(2 * np.pi * 440 * t) * 32767).astype(np.int16)
        fd, path = tempfile.mkstemp(suffix=".wav")
        os.close(fd)
        wav.write(path, sample_rate, audio)
        yield path
        try:
            os.unlink(path)
        except OSError:
            pass
    except ImportError:
        pytest.skip("scipy not available")


class TestCoreWorkflow:
    """Core workflow: import → profile → synthesize → library → playback."""

    def test_import_profile_synthesize_playback(
        self, backend_health, test_audio
    ):
        """Import audio, create profile, synthesize, verify playback URL."""
        session = str(uuid.uuid4())[:8]

        # 1. Import audio (library upload)
        with open(test_audio, "rb") as f:
            r = requests.post(
                f"{API_BASE_URL}/api/library/assets/upload",
                files={"file": ("test.wav", f, "audio/wav")},
                data={"folder_id": None, "tags": "core-workflow"},
                timeout=TIMEOUT_SECONDS,
            )
        assert r.status_code in (200, 201), f"Import failed: {r.status_code} - {r.text}"
        import_result = r.json()
        audio_id = import_result.get("id") or import_result.get("audio_id")
        assert audio_id, f"No audio ID: {import_result}"
        reference_audio_path = import_result.get("path")
        assert reference_audio_path, f"No path in import result: {import_result}"
        logger.info("Import OK: %s (path=%s)", audio_id, reference_audio_path)

        # 2. Create or select profile
        r = requests.get(f"{API_BASE_URL}/api/profiles", timeout=TIMEOUT_SECONDS)
        profile_id = None
        if r.status_code == 200:
            data = r.json()
            items = data.get("items", data) if isinstance(data, dict) else data
            if isinstance(items, list) and items:
                profile_id = items[0].get("id") or items[0].get("profileId")
                logger.info("Using existing profile: %s", profile_id)

        if not profile_id:
            r = requests.post(
                f"{API_BASE_URL}/api/profiles",
                json={"name": f"Core_{session}", "language": "en", "tags": ["core"]},
                timeout=TIMEOUT_SECONDS,
            )
            assert r.status_code in (200, 201), f"Profile create failed: {r.status_code} - {r.text}"
            profile_result = r.json()
            profile_id = profile_result.get("id") or profile_result.get("profileId")
            assert profile_id, f"No profile ID: {profile_result}"
            logger.info("Profile created: %s", profile_id)

        # 2b. Set reference audio from imported file (required for synthesis)
        pr = requests.post(
            f"{API_BASE_URL}/api/profiles/{profile_id}/preprocess-reference",
            json={"reference_audio_path": reference_audio_path, "auto_enhance": False},
            timeout=TIMEOUT_SECONDS,
        )
        assert pr.status_code in (200, 201), (
            f"Preprocess-reference failed: {pr.status_code} - {pr.text}"
        )
        logger.info("Reference audio set for profile %s", profile_id)

        # 2c. Grant consent for profile (required for synthesis when VOICESTUDIO_TEST_MODE not set)
        try:
            cr = requests.post(
                f"{API_BASE_URL}/api/consent/request",
                json={
                    "voice_id": profile_id,
                    "grantor_id": f"e2e_{session}",
                    "grantor_name": "E2E Test",
                    "consent_type": "voice_usage",
                },
                timeout=TIMEOUT_SECONDS,
            )
            if cr.status_code in (200, 201):
                consent_record = cr.json()
                consent_id = consent_record.get("consent_id")
                if consent_id:
                    gr = requests.post(
                        f"{API_BASE_URL}/api/consent/grant/{consent_id}",
                        timeout=TIMEOUT_SECONDS,
                    )
                    if gr.status_code in (200, 201):
                        logger.info("Consent granted for profile %s", profile_id)
        except Exception as e:
            logger.warning("Consent setup skipped (backend may use VOICESTUDIO_TEST_MODE): %s", e)

        # 3. Synthesize
        r = requests.post(
            f"{API_BASE_URL}/api/voice/synthesize",
            json={
                "profile_id": profile_id,
                "text": "Hello, core workflow test.",
                "language": "en",
            },
            timeout=TIMEOUT_SECONDS,
        )
        if r.status_code == 503:
            err = r.json() if r.headers.get("content-type", "").startswith("application/json") else {}
            msg = err.get("message", r.text)
            if "engine" in msg.lower() or "Engine" in r.text:
                pytest.skip(
                    f"Synthesis engine not available (503): {msg[:120]}. "
                    "Set VOICESTUDIO_TEST_MODE=stub or install engines for full E2E."
                )
        assert r.status_code in (200, 201, 202), f"Synthesis failed: {r.status_code} - {r.text}"
        synth_result = r.json()
        if "job_id" in synth_result:
            job_id = synth_result["job_id"]
            for _ in range(60):
                jr = requests.get(f"{API_BASE_URL}/api/jobs/{job_id}", timeout=10)
                if jr.status_code != 200:
                    continue
                js = jr.json()
                status = js.get("status", "").lower()
                if status in ("completed", "success", "done"):
                    synth_result = js.get("result", js)
                    break
                if status in ("failed", "error"):
                    pytest.fail(f"Job failed: {js.get('error')}")
                time.sleep(2)
            else:
                pytest.fail("Job timed out")

        out_audio_id = synth_result.get("audio_id") or synth_result.get("id")
        out_audio_url = synth_result.get("audio_url") or synth_result.get("url")
        assert out_audio_id or out_audio_url, f"No audio output: {synth_result}"
        logger.info("Synthesis OK: %s", out_audio_id or out_audio_url)

        # 4. Playback: verify audio endpoint returns 200
        if out_audio_url:
            if out_audio_url.startswith("/"):
                base = API_BASE_URL.rsplit("/api", 1)[0]
                url = f"{base}{out_audio_url}"
            else:
                url = out_audio_url
            pr = requests.get(url, timeout=15)
            assert pr.status_code == 200, f"Playback URL failed: {pr.status_code}"
            assert len(pr.content) > 1024, "Audio too small"
        elif out_audio_id:
            ar = requests.get(
                f"{API_BASE_URL}/api/audio/file/{out_audio_id}",
                timeout=15,
            )
            if ar.status_code == 404:
                ar = requests.get(
                    f"{API_BASE_URL}/api/voice/audio/{out_audio_id}",
                    timeout=15,
                )
            assert ar.status_code == 200, f"Audio file failed: {ar.status_code}"
            assert len(ar.content) > 1024, "Audio too small"
        logger.info("Playback OK")
