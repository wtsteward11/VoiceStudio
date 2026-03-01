#!/usr/bin/env python3
"""
Baseline End-to-End Voice Workflow Proof
Engine Engineer - Quality + Functions Tranche

This script runs a baseline end-to-end voice workflow:
1. TTS synthesis (default: XTTS v2; use --engine to select)
2. whisper.cpp transcription
3. Quality metrics capture and SLO-6 checks (MOS >= 3.5, similarity >= 0.7)

All evidence (inputs, outputs, metrics, paths, latency, SLO) is captured for baseline
comparison. Use --strict-slo to exit non-zero when quality targets are not met.
"""

import argparse
import json
import logging
import os
import sys
import time
from datetime import datetime
from importlib import metadata as importlib_metadata
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import requests

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)

# Default configuration
DEFAULT_BACKEND_URL = "http://localhost:8000"
DEFAULT_BACKEND_PORTS = [8000, 8001, 8002, 8080, 8888]
DEFAULT_TEST_TEXT = (
    "Hello, this is a baseline voice workflow proof. "
    "We are testing XTTS v2 synthesis, whisper.cpp transcription, "
    "and quality metrics calculation."
)
DEFAULT_LANGUAGE = "en"


def _engine_to_backend_id(engine: str) -> str:
    """Map CLI engine name to backend engine id (e.g. xtts -> xtts_v2)."""
    e = (engine or "").strip().lower()
    aliases = {
        "xtts": "xtts_v2",
        "xtts_v2": "xtts_v2",
        "sovits": "sovits_svc",
        "sovits_svc": "sovits_svc",
        "gpt_sovits": "gpt_sovits",
        "whisper_cpp": "whisper_cpp",
        "whisper": "whisper",
    }
    return aliases.get(e, e) or "xtts_v2"


class BaselineWorkflowProof:
    """Baseline end-to-end voice workflow proof runner."""

    # SLO targets per docs/governance/SERVICE_LEVEL_OBJECTIVES.md (SLO-6)
    MOS_TARGET = 3.5
    SIMILARITY_TARGET = 0.7

    def __init__(
        self,
        backend_url: str = DEFAULT_BACKEND_URL,
        output_dir: str | None = None,
        synthesis_engine: str = "xtts",
        strict_slo: bool = False,
    ):
        """
        Initialize the baseline proof runner.

        Args:
            backend_url: Backend API base URL
            output_dir: Directory to save proof artifacts (default: timestamped dir)
            synthesis_engine: TTS engine for synthesis (default: xtts; maps to xtts_v2)
            strict_slo: If True, exit non-zero when MOS/similarity below SLO targets
        """
        self.backend_url_requested = backend_url.rstrip("/")
        self.backend_url = self.backend_url_requested
        self._backend_resolution_error: str | None = None
        self.output_dir = output_dir or self._create_output_dir()
        self._synthesis_engine = (synthesis_engine or "xtts").strip().lower()
        self._strict_slo = bool(strict_slo)
        Path(self.output_dir).resolve().mkdir(parents=True, exist_ok=True)
        self.proof_data: dict[str, Any] = {
            "timestamp": datetime.utcnow().isoformat(),
            "workflow": "baseline_voice_workflow",
            "steps": [],
            "inputs": {},
            "outputs": {},
            "metrics": {},
            "config": {},
            "slo": {},
        }
        logger.info(f"Proof output directory: {self.output_dir}")

    def _create_output_dir(self) -> str:
        """Create timestamped output directory for proof artifacts."""
        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        output_dir = os.path.join(
            os.path.dirname(__file__),
            "..",
            ".buildlogs",
            "proof_runs",
            f"baseline_workflow_{timestamp}",
        )
        output_path = Path(output_dir).resolve()
        output_path.mkdir(parents=True, exist_ok=True)
        return str(output_path)

    def _save_proof_data(self):
        """Save proof data to JSON file."""
        proof_file = os.path.join(self.output_dir, "proof_data.json")
        with open(proof_file, "w", encoding="utf-8") as f:
            json.dump(self.proof_data, f, indent=2, ensure_ascii=False)
        logger.info(f"Proof data saved to: {proof_file}")

    def _check_backend_health(
        self, base_url: str | None = None, log_failures: bool = True
    ) -> bool:
        """Check if backend API is accessible."""
        base_url = (base_url or self.backend_url).rstrip("/")
        # Try both /health and /api/health endpoints
        for endpoint in ["/api/health", "/health"]:
            try:
                response = requests.get(f"{base_url}{endpoint}", timeout=5)
                if response.status_code == 200:
                    logger.info(f"Backend API is accessible ({endpoint})")
                    return True
            except requests.exceptions.RequestException:
                continue
        if log_failures:
            logger.warning(
                "Backend health check failed for both /health and /api/health"
            )
        return False

    def _check_voice_clone_route(
        self, base_url: str | None = None, log_missing: bool = True
    ) -> bool:
        """Verify the /api/voice/clone route is registered on the backend."""
        base_url = (base_url or self.backend_url).rstrip("/")
        try:
            response = requests.get(f"{base_url}/openapi.json", timeout=10)
            if response.status_code == 200:
                spec = response.json()
                paths = spec.get("paths", {}) if isinstance(spec, dict) else {}
                if "/api/voice/clone" in paths:
                    return True
                if log_missing:
                    logger.warning(
                        "Backend %s missing /api/voice/clone in OpenAPI.",
                        base_url,
                    )
                return False
        except (requests.exceptions.RequestException, ValueError) as e:
            logger.debug("OpenAPI check failed for %s: %s", base_url, e)

        try:
            response = requests.options(f"{base_url}/api/voice/clone", timeout=5)
            if response.status_code == 404:
                if log_missing:
                    logger.warning(
                        "Backend %s missing /api/voice/clone (404 on OPTIONS).",
                        base_url,
                    )
                return False
            return True
        except requests.exceptions.RequestException:
            if log_missing:
                logger.warning(
                    "Backend %s did not respond for /api/voice/clone OPTIONS.",
                    base_url,
                )
            return False

    def _check_engine_available(
        self,
        engine_id: str,
        base_url: str | None = None,
        log_missing: bool = True,
    ) -> bool:
        """Verify the given synthesis engine is available on the backend."""
        base_url = (base_url or self.backend_url).rstrip("/")
        backend_id = _engine_to_backend_id(engine_id)
        try:
            response = requests.get(f"{base_url}/api/engines", timeout=10)
            if response.status_code != 200:
                if log_missing:
                    logger.warning(
                        "Backend %s returned %s for /api/engines.",
                        base_url,
                        response.status_code,
                    )
                return False
            payload = response.json()
            engines_payload = (
                payload.get("engines", []) if isinstance(payload, dict) else payload
            )
            engine_ids: list[str] = []
            if isinstance(engines_payload, list):
                for item in engines_payload:
                    if isinstance(item, str):
                        engine_ids.append(item)
                    elif isinstance(item, dict) and "id" in item:
                        engine_ids.append(str(item["id"]))
            engine_ids = [eid.lower() for eid in engine_ids]
            if backend_id in engine_ids:
                return True
            # Allow xtts when backend reports xtts_v2
            if backend_id == "xtts_v2" and "xtts" in engine_ids:
                return True
            if log_missing:
                logger.warning(
                    "Backend %s does not list %s in /api/engines.",
                    base_url,
                    backend_id,
                )
            return False
        except (requests.exceptions.RequestException, ValueError) as e:
            if log_missing:
                logger.warning("Failed to read /api/engines from %s: %s", base_url, e)
            return False

    def _resolve_backend_url(self) -> bool:
        """Resolve a backend URL that exposes /api/voice/clone and requested engine."""
        self._backend_resolution_error = None
        backend_engine_id = _engine_to_backend_id(self._synthesis_engine)
        if self._check_backend_health() and self._check_voice_clone_route():
            if self._check_engine_available(self._synthesis_engine):
                return True
            self._backend_resolution_error = (
                f"Backend is reachable but {backend_engine_id} is not available."
            )
        else:
            self._backend_resolution_error = (
                "Backend API is not accessible or /api/voice/clone is missing."
            )

        if self.backend_url_requested != DEFAULT_BACKEND_URL:
            return False

        parsed = urlparse(self.backend_url)
        if parsed.scheme not in {"http", "https"}:
            return False

        host = parsed.hostname or "localhost"
        if host == "0.0.0.0":
            host = "localhost"

        if host not in {"localhost", "127.0.0.1"}:
            return False

        for port in DEFAULT_BACKEND_PORTS:
            candidate = f"{parsed.scheme}://{host}:{port}"
            if candidate.rstrip("/") == self.backend_url:
                continue
            if not self._check_backend_health(candidate, log_failures=False):
                continue
            if not self._check_voice_clone_route(candidate, log_missing=False):
                continue
            if not self._check_engine_available(
                self._synthesis_engine, candidate, log_missing=False
            ):
                continue
            logger.warning(
                "Backend URL auto-detected as %s (scripts/backend/start_backend.ps1 may have picked an alternate port).",
                candidate,
            )
            self.backend_url = candidate.rstrip("/")
            return True

        self._backend_resolution_error = (
            f"No backend with {backend_engine_id} detected on default ports "
            "(8000, 8001, 8002, 8080, 8888)."
        )
        return False

    def _create_test_profile(
        self, reference_audio_path: str | None = None
    ) -> str | None:
        """
        Create a test voice profile for synthesis.

        Args:
            reference_audio_path: Optional path to reference audio file

        Returns:
            Profile ID if successful, None otherwise
        """
        logger.info("Checking for existing voice profiles...")

        try:
            # Try to list existing profiles first
            response = requests.get(f"{self.backend_url}/api/profiles", timeout=10)
            if response.status_code == 200:
                profiles_data = response.json()
                # Handle paginated response
                if isinstance(profiles_data, dict) and "items" in profiles_data:
                    profiles = profiles_data.get("items", [])
                else:
                    profiles = profiles_data if isinstance(profiles_data, list) else []

                if profiles and len(profiles) > 0:
                    profile_id = profiles[0].get("id")
                    logger.info(f"Using existing profile: {profile_id}")
                    self.proof_data["config"]["profile_id"] = profile_id
                    self.proof_data["config"]["profile_source"] = "existing"
                    return profile_id

            # If no profiles exist, try to create a minimal one
            logger.info(
                "No existing profiles found. Attempting to create test profile..."
            )

            # Create a minimal profile (without reference audio for now)
            # Note: Some engines may require reference audio for synthesis
            profile_request = {
                "name": "Baseline Proof Test Profile",
                "language": "en",
                "tags": ["baseline", "test"],
            }

            response = requests.post(
                f"{self.backend_url}/api/profiles",
                json=profile_request,
                timeout=10,
            )

            if response.status_code in [200, 201]:
                profile = response.json()
                profile_id = profile.get("id")
                logger.info(f"Created test profile: {profile_id}")
                self.proof_data["config"]["profile_id"] = profile_id
                self.proof_data["config"]["profile_source"] = "created"
                return profile_id
            else:
                logger.warning(
                    f"Profile creation failed: {response.status_code} - {response.text}. "
                    "Synthesis may require an existing profile with reference audio."
                )
                return None

        except requests.exceptions.RequestException as e:
            logger.error(f"Failed to create/get profile: {e}")
            return None

    def _generate_test_reference_audio(self, text: str, output_path: str) -> bool:
        """
        Generate a simple test reference audio file using a TTS utility.

        Args:
            text: Text to synthesize for reference
            output_path: Path to save the reference audio

        Returns:
            True if successful, False otherwise
        """
        try:
            # Try using pyttsx3 (usually available) or eSpeak
            import pyttsx3

            engine = pyttsx3.init()
            engine.save_to_file(text[:100], output_path)  # Limit text length
            engine.runAndWait()
            if os.path.exists(output_path):
                logger.info(f"Generated test reference audio: {output_path}")
                return True
        except ImportError:
            logger.debug("pyttsx3 not available, trying alternative methods")
        except Exception as e:
            logger.debug(f"pyttsx3 failed: {e}")

        # Fallback: create a minimal WAV file (silence)
        try:
            import struct
            import wave

            sample_rate = 22050
            duration = 2.0  # 2 seconds
            num_samples = int(sample_rate * duration)

            with wave.open(output_path, "wb") as wav_file:
                wav_file.setnchannels(1)  # Mono
                wav_file.setsampwidth(2)  # 16-bit
                wav_file.setframerate(sample_rate)
                # Write silence
                for _ in range(num_samples):
                    wav_file.writeframes(struct.pack("<h", 0))

            logger.warning(f"Created minimal reference audio (silence): {output_path}")
            logger.warning("For best results, provide a real reference audio file")
            return True
        except Exception as e:
            logger.error(f"Failed to generate reference audio: {e}")
            return False

    def synthesize_with_xtts(
        self,
        text: str,
        reference_audio_paths: list[str] | None = None,
        language: str = DEFAULT_LANGUAGE,
        quality_mode: str = "standard",
        use_multi_reference: bool = False,
        prosody_params: str | None = None,
        engine: str | None = None,
    ) -> dict[str, Any]:
        """
        Synthesize audio via clone endpoint (accepts reference audio directly).

        Args:
            text: Text to synthesize
            reference_audio_paths: Reference audio path(s) (will generate if missing)
            language: Language code
            quality_mode: Quality mode ("fast", "standard", "high", "ultra")
            use_multi_reference: Enable multi-reference cloning when multiple references provided
            prosody_params: JSON string of prosody parameters (pitch, tempo, formant_shift, energy)
            engine: Backend engine id (default: xtts_v2 from instance synthesis_engine)

        Returns:
            Synthesis result with audio_id, audio_url, quality_metrics, etc.
        """
        backend_engine = (engine or _engine_to_backend_id(self._synthesis_engine))
        logger.info("Step 1: Synthesizing with %s (clone endpoint)...", backend_engine)
        step_start = time.time()

        # Normalize reference audio inputs
        reference_audio_paths = (
            [p for p in (reference_audio_paths or []) if p]
            if reference_audio_paths
            else []
        )
        if reference_audio_paths:
            reference_audio_paths = [
                p for p in reference_audio_paths if os.path.exists(p)
            ]

        # Generate reference audio if not provided
        if not reference_audio_paths:
            logger.info(
                "No reference audio provided, generating test reference audio..."
            )
            ref_audio_path = os.path.join(self.output_dir, "test_reference.wav")
            if not self._generate_test_reference_audio(
                "This is a test reference audio for baseline proof.", ref_audio_path
            ):
                error_msg = "Failed to generate reference audio and none provided"
                logger.error(error_msg)
                self.proof_data["steps"].append(
                    {
                        "step": "synthesize",
                        "status": "failed",
                        "error": error_msg,
                        "duration_seconds": time.time() - step_start,
                    }
                )
                return {"error": error_msg}
            reference_audio_paths = [ref_audio_path]

        self.proof_data["inputs"]["synthesis"] = {
            "engine": backend_engine,
            "text": text,
            "language": language,
            "quality_mode": quality_mode,
            "reference_audio": reference_audio_paths,
            "use_multi_reference": use_multi_reference,
            "prosody_params": prosody_params,
        }
        self.proof_data["config"]["engine"] = backend_engine
        self.proof_data["config"]["language"] = language
        self.proof_data["config"]["quality_mode"] = quality_mode
        self.proof_data["config"]["reference_audio_paths"] = reference_audio_paths
        self.proof_data["config"]["use_multi_reference"] = use_multi_reference
        if prosody_params:
            self.proof_data["config"]["prosody_params"] = prosody_params

        try:
            # Use clone endpoint which accepts reference audio directly
            from contextlib import ExitStack

            with ExitStack() as stack:
                if len(reference_audio_paths) > 1:
                    files = [
                        ("reference_audio", stack.enter_context(open(path, "rb")))
                        for path in reference_audio_paths
                    ]
                else:
                    files = {
                        "reference_audio": stack.enter_context(
                            open(reference_audio_paths[0], "rb")
                        )
                    }
                data = {
                    "text": text,
                    "engine": backend_engine,
                    "language": language,
                    "quality_mode": quality_mode,
                    "use_multi_reference": str(bool(use_multi_reference)).lower(),
                }
                if prosody_params:
                    data["prosody_params"] = prosody_params

                # First-time XTTS model download can take longer than the default 5 minute timeout.
                # Heuristic: if the expected Coqui cache dir under the models root looks empty,
                # extend the timeout for this run.
                synthesis_timeout = 300  # 5 minutes (typical steady-state)
                try:
                    models_root = os.environ.get(
                        "VOICESTUDIO_MODELS_PATH", r"E:\VoiceStudio\models"
                    )
                    expected_xtts_dir = os.path.join(
                        models_root,
                        "xtts",
                        "tts",
                        "tts_models--multilingual--multi-dataset--xtts_v2",
                    )
                    has_any_files = False
                    if os.path.isdir(expected_xtts_dir):
                        for _, _, filenames in os.walk(expected_xtts_dir):
                            if filenames:
                                has_any_files = True
                                break
                    if not has_any_files:
                        synthesis_timeout = 1800  # 30 minutes (first download + warmup)
                        logger.info(
                            "XTTS model cache appears empty; increasing synthesis timeout "
                            f"to {synthesis_timeout}s to allow first-time model download."
                        )
                except Exception:
                    # If detection fails, keep default timeout.
                    ...
                response = requests.post(
                    f"{self.backend_url}/api/voice/clone",
                    files=files,
                    data=data,
                    timeout=synthesis_timeout,
                )

            if response.status_code not in [200, 201]:
                error_msg = (
                    f"Synthesis failed: {response.status_code} - {response.text}"
                )
                logger.error(error_msg)
                self.proof_data["steps"].append(
                    {
                        "step": "synthesize",
                        "status": "failed",
                        "error": error_msg,
                        "duration_seconds": time.time() - step_start,
                    }
                )
                return {"error": error_msg}

            result = response.json()
            step_duration = time.time() - step_start

            # Extract key information
            audio_id = result.get("audio_id")
            audio_url = result.get("audio_url")
            quality_metrics = result.get("quality_metrics", {})
            duration = result.get("duration")
            device = result.get("device")
            candidate_metrics = result.get("candidate_metrics")

            duration_display = "unknown"
            if isinstance(duration, (int, float)):
                duration_display = f"{duration:.2f}s"
            elif duration is not None:
                try:
                    duration_value = float(duration)
                    duration_display = f"{duration_value:.2f}s"
                    duration = duration_value
                except (TypeError, ValueError):
                    duration_display = "unknown"

            logger.info(
                f"Synthesis complete: audio_id={audio_id}, duration={duration_display}"
            )
            if quality_metrics:
                logger.info(f"Quality metrics: {json.dumps(quality_metrics, indent=2)}")

            step_result = {
                "step": "synthesize",
                "status": "success",
                "audio_id": audio_id,
                "audio_url": audio_url,
                "audio_duration_seconds": duration,
                "quality_metrics": quality_metrics,
                "step_duration_seconds": step_duration,
                # Back-compat / summary helpers
                "duration_seconds": step_duration,
            }
            if device:
                step_result["device"] = device
                self.proof_data["config"]["device"] = device
            if candidate_metrics:
                step_result["candidate_metrics"] = candidate_metrics
            profile_id = result.get(
                "profile_id"
            )  # Clone endpoint may return profile_id
            if profile_id:
                step_result["profile_id"] = profile_id
                self.proof_data["config"]["profile_id"] = profile_id

            # If the clone endpoint did not synthesize audio, treat this as a failed step
            # (profile may still be created, but baseline proof should not report success).
            if not audio_id:
                step_result["status"] = "failed"
                step_result["error"] = (
                    "No audio_id returned from /api/voice/clone (profile may have been created, "
                    "but synthesis did not produce audio)."
                )

            self.proof_data["steps"].append(step_result)
            self.proof_data["outputs"]["synthesis"] = {
                "audio_id": audio_id,
                "audio_url": audio_url,
                "duration": duration,
            }
            self.proof_data["metrics"]["synthesis"] = quality_metrics

            # Download audio file for evidence
            if audio_url:
                self._download_audio(audio_id, audio_url)

            if (duration is None or duration == 0.0) and self.proof_data["outputs"].get(
                "audio_file_path"
            ):
                audio_file_path = self.proof_data["outputs"]["audio_file_path"]
                computed_duration = self._get_wav_duration_seconds(audio_file_path)
                if computed_duration is not None:
                    step_result["audio_duration_seconds"] = computed_duration
                    self.proof_data["outputs"]["synthesis"][
                        "duration"
                    ] = computed_duration

            return result

        except requests.exceptions.RequestException as e:
            error_msg = f"Synthesis request failed: {e}"
            logger.error(error_msg)
            self.proof_data["steps"].append(
                {
                    "step": "synthesize",
                    "status": "failed",
                    "error": error_msg,
                    "duration_seconds": time.time() - step_start,
                }
            )
            return {"error": error_msg}

    def transcribe_with_whisper(
        self,
        audio_id: str,
        engine: str = "whisper_cpp",
        language: str | None = None,
    ) -> dict[str, Any]:
        """
        Transcribe audio using whisper.cpp.

        Args:
            audio_id: Audio ID from synthesis
            engine: Transcription engine (default: whisper_cpp)
            language: Language code (optional, auto-detect if None)

        Returns:
            Transcription result with text, segments, etc.
        """
        logger.info(f"Step 2: Transcribing with {engine}...")
        step_start = time.time()

        transcription_request = {
            "audio_id": audio_id,
            "engine": engine,
            "word_timestamps": True,
        }
        if language:
            transcription_request["language"] = language

        self.proof_data["inputs"]["transcription"] = transcription_request.copy()

        try:
            response = requests.post(
                f"{self.backend_url}/api/transcribe",
                json=transcription_request,
                timeout=300,  # 5 minutes for transcription
            )

            if response.status_code not in [200, 201]:
                error_msg = (
                    f"Transcription failed: {response.status_code} - {response.text}"
                )
                logger.error(error_msg)
                self.proof_data["steps"].append(
                    {
                        "step": "transcribe",
                        "status": "failed",
                        "error": error_msg,
                        "duration_seconds": time.time() - step_start,
                    }
                )
                return {"error": error_msg}

            result = response.json()
            step_duration = time.time() - step_start

            transcription_text = result.get("text", "")
            detected_language = result.get("language", "unknown")
            segments = result.get("segments", [])

            logger.info(f"Transcription complete: language={detected_language}")
            logger.info(f"Transcribed text: {transcription_text[:100]}...")

            step_result = {
                "step": "transcribe",
                "status": "success",
                "text": transcription_text,
                "language": detected_language,
                "segments_count": len(segments),
                "duration_seconds": step_duration,
            }

            self.proof_data["steps"].append(step_result)
            self.proof_data["outputs"]["transcription"] = {
                "text": transcription_text,
                "language": detected_language,
                "segments": segments,
            }

            return result

        except requests.exceptions.RequestException as e:
            error_msg = f"Transcription request failed: {e}"
            logger.error(error_msg)
            self.proof_data["steps"].append(
                {
                    "step": "transcribe",
                    "status": "failed",
                    "error": error_msg,
                    "duration_seconds": time.time() - step_start,
                }
            )
            return {"error": error_msg}

    def _download_audio(self, audio_id: str, audio_url: str):
        """Download audio file for evidence."""
        try:
            # Construct full URL if relative
            full_url = f"{self.backend_url}{audio_url}" if audio_url.startswith("/") else audio_url

            response = requests.get(full_url, timeout=60)
            if response.status_code == 200:
                audio_file = os.path.join(self.output_dir, f"{audio_id}.wav")
                with open(audio_file, "wb") as f:
                    f.write(response.content)
                logger.info(f"Audio downloaded to: {audio_file}")
                self.proof_data["outputs"]["audio_file_path"] = audio_file
        except Exception as e:
            logger.warning(f"Failed to download audio: {e}")

    def _get_wav_duration_seconds(self, audio_path: str) -> float | None:
        try:
            import wave

            with wave.open(audio_path, "rb") as wav_file:
                frames = wav_file.getnframes()
                sample_rate = wav_file.getframerate()
                if sample_rate:
                    return frames / float(sample_rate)
        except Exception as e:
            logger.debug(f"Duration check failed for {audio_path}: {e}")
        return None

    def run_baseline_proof(
        self,
        text: str = DEFAULT_TEST_TEXT,
        language: str = DEFAULT_LANGUAGE,
        reference_audio_paths: list[str] | None = None,
        quality_mode: str = "standard",
        use_multi_reference: bool = False,
        prosody_params: str | None = None,
    ) -> dict[str, Any]:
        """
        Run the complete baseline workflow proof.

        Args:
            text: Text to synthesize
            language: Language code
            profile_id: Optional voice profile ID

        Returns:
            Complete proof data dictionary
        """
        logger.info("=" * 80)
        logger.info("BASELINE VOICE WORKFLOW PROOF")
        logger.info("=" * 80)
        logger.info(f"Output directory: {self.output_dir}")
        logger.info(f"Test text: {text}")
        logger.info("")

        # Check backend health + voice route availability
        if not self._resolve_backend_url():
            logger.error(
                "Backend API is not accessible or /api/voice/clone is missing."
            )
            if self._backend_resolution_error:
                logger.error(self._backend_resolution_error)
            logger.info(
                "Start backend with: .\\scripts\\backend\\start_backend.ps1 (preferred) "
                "or: .\\venv\\Scripts\\python.exe -m uvicorn backend.api.main:app --port 8000. "
                "If scripts/backend/start_backend.ps1 selected an alternate port, re-run with "
                "--backend-url http://localhost:<port>."
            )
            return {"error": "Backend not accessible"}

        backend_url_source = (
            "auto-detected"
            if self.backend_url != self.backend_url_requested
            else "requested"
        )
        self.proof_data["config"]["backend_url_requested"] = self.backend_url_requested
        self.proof_data["config"]["backend_url"] = self.backend_url
        self.proof_data["config"]["backend_url_source"] = backend_url_source
        logger.info(f"Backend URL: {self.backend_url}")

        # Store inputs
        self.proof_data["inputs"]["text"] = text
        self.proof_data["inputs"]["language"] = language

        # Capture local dependency versions for proof metadata
        self._capture_dependency_versions()

        # Step 1: Synthesize with XTTS v2
        synthesis_result = self.synthesize_with_xtts(
            text=text,
            reference_audio_paths=reference_audio_paths,
            language=language,
            quality_mode=quality_mode,
            use_multi_reference=use_multi_reference,
            prosody_params=prosody_params,
        )

        if "error" in synthesis_result:
            logger.error("Synthesis failed. Cannot proceed with transcription.")
            self._save_proof_data()
            return self.proof_data

        audio_id = synthesis_result.get("audio_id")
        if not audio_id:
            logger.warning("No audio_id returned from synthesis.")
            logger.warning(
                "The clone endpoint may have created a profile but did not synthesize audio."
            )
            logger.warning("This could indicate:")
            logger.warning("  1. Engine initialization failed")
            logger.warning("  2. Text parameter was not processed correctly")
            logger.warning("  3. Synthesis error was caught silently")
            logger.warning("Check backend logs for detailed error messages.")
            # Still capture model/caching configuration for evidence
            self._capture_model_paths()
            # Still save proof data - this is valuable baseline information
            self._save_proof_data()
            logger.info("Baseline proof captured (synthesis needs investigation)")
            # Return early since we can't transcribe without audio_id
            return self.proof_data

        # SLO and latency from synthesis step (SLO-6: MOS >= 3.5, similarity >= 0.7)
        syn_step = next(
            (s for s in self.proof_data["steps"] if s.get("step") == "synthesize"), None
        )
        if syn_step and syn_step.get("status") == "success":
            qm = syn_step.get("quality_metrics") or self.proof_data.get("metrics", {}).get(
                "synthesis", {}
            )
            lat = syn_step.get("duration_seconds") or syn_step.get("step_duration_seconds")
            mos = qm.get("mos_score") if isinstance(qm, dict) else None
            sim = qm.get("similarity") if isinstance(qm, dict) else None
            self.proof_data["slo"]["mos_target"] = self.MOS_TARGET
            self.proof_data["slo"]["similarity_target"] = self.SIMILARITY_TARGET
            self.proof_data["slo"]["mos_met"] = (
                (float(mos) >= self.MOS_TARGET) if mos is not None else None
            )
            self.proof_data["slo"]["similarity_met"] = (
                (float(sim) >= self.SIMILARITY_TARGET) if sim is not None else None
            )
            self.proof_data["slo"]["synthesis_latency_seconds"] = lat
            if self.proof_data["outputs"].get("synthesis") is not None:
                self.proof_data["outputs"]["synthesis"]["synthesis_latency_seconds"] = lat

        # Step 2: Transcribe with whisper.cpp
        transcription_result = self.transcribe_with_whisper(
            audio_id=audio_id,
            engine="whisper_cpp",
            language=language,
        )

        if "error" in transcription_result:
            logger.warning("Transcription failed, but synthesis succeeded.")

        # Transcription latency for SLO / P50-P95 use
        trans_step = next(
            (s for s in self.proof_data["steps"] if s.get("step") == "transcribe"), None
        )
        if trans_step:
            t_lat = trans_step.get("duration_seconds")
            self.proof_data["slo"]["transcription_latency_seconds"] = t_lat
            if self.proof_data["outputs"].get("transcription") is not None and t_lat is not None:
                self.proof_data["outputs"]["transcription"][
                    "transcription_latency_seconds"
                ] = t_lat

        # Step 3: Capture model paths and configuration
        self._capture_model_paths()

        # Save proof data
        self._save_proof_data()

        # Print summary
        self._print_summary()

        return self.proof_data

    def _capture_model_paths(self):
        """Capture model paths and configuration from backend."""
        try:
            # Try to get engine info
            response = requests.get(f"{self.backend_url}/api/engines", timeout=10)
            if response.status_code == 200:
                engines = response.json()
                self.proof_data["config"]["available_engines"] = engines

            # Capture health preflight (includes env + writable path checks)
            response = requests.get(
                f"{self.backend_url}/api/health/preflight", timeout=30
            )
            if response.status_code == 200:
                self.proof_data["config"]["health_preflight"] = response.json()

            # Engine preflight (canonical)
            response = requests.get(
                f"{self.backend_url}/api/engines/preflight", timeout=30
            )
            if response.status_code == 200:
                preflight = response.json()
                self.proof_data["config"]["preflight"] = preflight
                return

            # Fallback: health preflight (older route shape)
            response = requests.get(
                f"{self.backend_url}/api/health/preflight", timeout=30
            )
            if response.status_code == 200:
                preflight = response.json()
                self.proof_data["config"]["preflight"] = preflight
        except Exception as e:
            logger.debug(f"Could not capture model paths: {e}")

    def _capture_dependency_versions(self):
        """Capture local dependency versions for evidence."""
        versions: dict[str, Any] = {
            "python": sys.version,
            "python_executable": sys.executable,
        }
        for package in ("torch", "torchaudio", "transformers"):
            try:
                versions[package] = importlib_metadata.version(package)
            except importlib_metadata.PackageNotFoundError:
                versions[package] = None
            except Exception as exc:
                versions[package] = f"error: {exc}"
        self.proof_data["config"]["dependency_versions"] = versions

    def _print_summary(self):
        """Print proof summary."""
        logger.info("")
        logger.info("=" * 80)
        logger.info("PROOF SUMMARY")
        logger.info("=" * 80)

        # Synthesis summary
        synthesis_step = next(
            (s for s in self.proof_data["steps"] if s.get("step") == "synthesize"), None
        )
        if synthesis_step and synthesis_step.get("status") == "success":
            logger.info("✅ Synthesis: SUCCESS")
            logger.info(f"   Audio ID: {synthesis_step.get('audio_id')}")
            logger.info(
                f"   Duration: {synthesis_step.get('duration_seconds', 0):.2f}s"
            )
            metrics = self.proof_data.get("metrics", {}).get("synthesis", {})
            if metrics:
                logger.info("   Quality Metrics:")
                for key, value in metrics.items():
                    if value is not None:
                        logger.info(f"     {key}: {value}")
            slo = self.proof_data.get("slo") or {}
            lat = slo.get("synthesis_latency_seconds")
            if lat is not None:
                logger.info("   Synthesis latency: %.2fs", lat)
            if slo.get("mos_met") is not None or slo.get("similarity_met") is not None:
                logger.info(
                    "   SLO-6: MOS>=3.5=%s, similarity>=0.7=%s",
                    slo.get("mos_met"),
                    slo.get("similarity_met"),
                )
        else:
            logger.info("❌ Synthesis: FAILED")

        # Transcription summary
        transcription_step = next(
            (s for s in self.proof_data["steps"] if s.get("step") == "transcribe"), None
        )
        if transcription_step and transcription_step.get("status") == "success":
            logger.info("✅ Transcription: SUCCESS")
            logger.info(f"   Language: {transcription_step.get('language')}")
            logger.info(f"   Text: {transcription_step.get('text', '')[:100]}...")
            t_lat = (self.proof_data.get("slo") or {}).get("transcription_latency_seconds")
            if t_lat is not None:
                logger.info("   Transcription latency: %.2fs", t_lat)
        else:
            logger.info("❌ Transcription: FAILED")

        logger.info("")
        logger.info(f"Proof data saved to: {self.output_dir}/proof_data.json")
        logger.info("=" * 80)


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Baseline end-to-end voice workflow proof"
    )
    parser.add_argument(
        "--backend-url",
        default=DEFAULT_BACKEND_URL,
        help=f"Backend API URL (default: {DEFAULT_BACKEND_URL})",
    )
    parser.add_argument(
        "--text",
        default=DEFAULT_TEST_TEXT,
        help="Text to synthesize",
    )
    parser.add_argument(
        "--language",
        default=DEFAULT_LANGUAGE,
        help=f"Language code (default: {DEFAULT_LANGUAGE})",
    )
    parser.add_argument(
        "--quality-mode",
        choices=["fast", "standard", "high", "ultra"],
        default="standard",
        help="XTTS clone quality mode (fast/standard/high/ultra)",
    )
    parser.add_argument(
        "--reference-audio",
        action="append",
        default=None,
        help="Reference audio path (pass multiple times for multi-reference).",
    )
    parser.add_argument(
        "--use-multi-reference",
        action="store_true",
        help="Enable multi-reference cloning when multiple reference audios are provided.",
    )
    parser.add_argument(
        "--prosody-params",
        default=None,
        help="JSON string of prosody params (pitch, tempo, formant_shift, energy).",
    )
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Output directory for proof artifacts (default: timestamped)",
    )
    parser.add_argument(
        "--engine",
        default="xtts",
        help="TTS engine for synthesis (default: xtts; maps to xtts_v2 for clone endpoint)",
    )
    parser.add_argument(
        "--strict-slo",
        action="store_true",
        help="Exit non-zero if MOS < 3.5 or similarity < 0.7 (SLO-6 targets)",
    )

    args = parser.parse_args()

    proof = BaselineWorkflowProof(
        backend_url=args.backend_url,
        output_dir=args.output_dir,
        synthesis_engine=args.engine,
        strict_slo=args.strict_slo,
    )

    result = proof.run_baseline_proof(
        text=args.text,
        language=args.language,
        reference_audio_paths=args.reference_audio,
        quality_mode=args.quality_mode,
        use_multi_reference=args.use_multi_reference,
        prosody_params=args.prosody_params,
    )

    # Exit with error code if proof failed
    if "error" in result:
        sys.exit(1)

    # Check if all steps succeeded
    failed_steps = [s for s in result.get("steps", []) if s.get("status") != "success"]
    if failed_steps:
        logger.error(f"Proof completed with {len(failed_steps)} failed steps")
        sys.exit(1)

    # --strict-slo: exit non-zero if MOS or similarity missing or below SLO-6 targets
    if args.strict_slo:
        slo = result.get("slo") or {}
        mos_met = slo.get("mos_met")
        sim_met = slo.get("similarity_met")
        if mos_met is not True or sim_met is not True:
            logger.error(
                "SLO-6 check failed (--strict-slo): MOS >= 3.5 and similarity >= 0.7 required. "
                "mos_met=%s, similarity_met=%s",
                mos_met,
                sim_met,
            )
            sys.exit(1)

    logger.info("✅ Baseline proof completed successfully")
    sys.exit(0)


if __name__ == "__main__":
    main()
