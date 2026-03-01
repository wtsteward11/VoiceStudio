#!/usr/bin/env python3
"""
So-VITS-SVC Conversion Proof

Runs an end-to-end conversion proof using the sovits_svc engine once
checkpoint + config files exist under E:\\VoiceStudio\\models\\checkpoints.
"""

import argparse
import json
import os
import sys
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import requests

DEFAULT_BACKEND_URL = "http://localhost:8000"
DEFAULT_BACKEND_PORTS = [8000, 8001, 8002, 8080, 8888]
DEFAULT_TEST_TEXT = (
    "Hello, this is a So-VITS-SVC conversion proof. "
    "We are validating conversion output and metrics."
)
DEFAULT_LANGUAGE = "en"


class SovitsSvcConversionProof:
    def __init__(
        self, backend_url: str = DEFAULT_BACKEND_URL, output_dir: str | None = None
    ):
        self.backend_url_requested = backend_url.rstrip("/")
        self.backend_url = self.backend_url_requested
        self._backend_resolution_error: str | None = None
        self.output_dir = output_dir or self._create_output_dir()
        Path(self.output_dir).resolve().mkdir(parents=True, exist_ok=True)
        self.proof_data: dict[str, Any] = {
            "timestamp": datetime.utcnow().isoformat(),
            "workflow": "sovits_svc_conversion",
            "steps": [],
            "inputs": {},
            "outputs": {},
            "metrics": {},
            "config": {},
        }

    def _create_output_dir(self) -> str:
        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        output_dir = os.path.join(
            os.path.dirname(__file__),
            "..",
            "proof_runs",
            f"sovits_svc_workflow_{timestamp}",
        )
        output_path = Path(output_dir).resolve()
        output_path.mkdir(parents=True, exist_ok=True)
        return str(output_path)

    def _save_proof_data(self):
        proof_file = os.path.join(self.output_dir, "proof_data.json")
        with open(proof_file, "w", encoding="utf-8") as f:
            json.dump(self.proof_data, f, indent=2, ensure_ascii=False)

    def _check_backend_health(self, base_url: str | None = None) -> bool:
        base_url = (base_url or self.backend_url).rstrip("/")
        for endpoint in ["/api/health", "/health"]:
            try:
                response = requests.get(f"{base_url}{endpoint}", timeout=5)
                if response.status_code == 200:
                    return True
            except requests.exceptions.RequestException:
                continue
        return False

    def _check_required_routes(self, base_url: str | None = None) -> bool:
        base_url = (base_url or self.backend_url).rstrip("/")
        try:
            response = requests.get(f"{base_url}/openapi.json", timeout=10)
            if response.status_code == 200:
                spec = response.json()
                paths = spec.get("paths", {}) if isinstance(spec, dict) else {}
                return "/api/voice/clone" in paths and "/api/rvc/convert" in paths
        # Best effort - failure is acceptable here
        except (requests.exceptions.RequestException, ValueError):
            pass

        try:
            response = requests.options(f"{base_url}/api/voice/clone", timeout=5)
            if response.status_code == 404:
                return False
        except requests.exceptions.RequestException:
            return False

        try:
            response = requests.options(f"{base_url}/api/rvc/convert", timeout=5)
            if response.status_code == 404:
                return False
        except requests.exceptions.RequestException:
            return False

        return True

    def _resolve_backend_url(self) -> bool:
        self._backend_resolution_error = None
        if self._check_backend_health(self.backend_url) and self._check_required_routes(
            self.backend_url
        ):
            return True

        if self.backend_url_requested != DEFAULT_BACKEND_URL:
            self._backend_resolution_error = (
                "Backend API is not accessible or missing required routes."
            )
            return False

        parsed = urlparse(self.backend_url)
        scheme = parsed.scheme or "http"
        host = parsed.hostname or "localhost"
        if host == "0.0.0.0":
            host = "localhost"

        for port in DEFAULT_BACKEND_PORTS:
            candidate = f"{scheme}://{host}:{port}"
            if candidate == self.backend_url:
                continue
            if self._check_backend_health(candidate) and self._check_required_routes(
                candidate
            ):
                self.backend_url = candidate
                return True

        self._backend_resolution_error = (
            "Backend API is not accessible or missing required routes."
        )
        return False

    def _fetch_preflight(self) -> dict[str, Any] | None:
        try:
            response = requests.get(
                f"{self.backend_url}/api/engines/preflight", timeout=30
            )
            if response.status_code == 200:
                return response.json()
        except Exception as exc:
            self.proof_data["config"]["preflight_error"] = str(exc)
        return None

    def _generate_reference_audio(self, output_path: str) -> bool:
        pyttsx_success = False
        try:
            import pyttsx3

            engine = pyttsx3.init()
            engine.save_to_file(DEFAULT_TEST_TEXT[:100], output_path)
            engine.runAndWait()
            pyttsx_success = os.path.exists(output_path)
        except Exception:
            pyttsx_success = False

        if pyttsx_success:
            return True

        try:
            import struct
            import wave

            sample_rate = 22050
            duration = 2.0
            num_samples = int(sample_rate * duration)
            with wave.open(output_path, "wb") as wav_file:
                wav_file.setnchannels(1)
                wav_file.setsampwidth(2)
                wav_file.setframerate(sample_rate)
                for _ in range(num_samples):
                    wav_file.writeframes(struct.pack("<h", 0))
            return True
        except Exception:
            return False

    def _download_audio(self, audio_url: str, out_path: str) -> bool:
        try:
            full_url = (
                f"{self.backend_url}{audio_url}"
                if audio_url.startswith("/")
                else audio_url
            )
            response = requests.get(full_url, timeout=60)
            if response.status_code == 200:
                with open(out_path, "wb") as f:
                    f.write(response.content)
                return True
        except Exception:
            return False
        return False

    def run(self, checkpoint_path: str, config_path: str) -> dict[str, Any]:
        self.proof_data["inputs"]["checkpoint_path"] = checkpoint_path
        self.proof_data["inputs"]["config_path"] = config_path
        self.proof_data["config"]["engine"] = "sovits_svc"

        if not os.path.exists(checkpoint_path) or not os.path.exists(config_path):
            self.proof_data["steps"].append(
                {
                    "step": "preflight",
                    "status": "blocked",
                    "error": "Checkpoint or config file not found.",
                }
            )
            self._save_proof_data()
            return self.proof_data

        if not self._resolve_backend_url():
            self.proof_data["steps"].append(
                {
                    "step": "health",
                    "status": "failed",
                    "error": self._backend_resolution_error or "Backend not reachable",
                }
            )
            self._save_proof_data()
            return self.proof_data

        self.proof_data["config"]["backend_url"] = self.backend_url

        preflight = self._fetch_preflight()
        if preflight:
            self.proof_data["config"]["preflight"] = preflight
            results = preflight.get("results", {}) if isinstance(preflight, dict) else {}
            sovits_status = results.get("sovits_svc") or results.get("gpt_sovits")
            xtts_status = results.get("xtts_v2")

            if isinstance(xtts_status, dict) and not xtts_status.get("ok", False):
                self.proof_data["steps"].append(
                    {
                        "step": "preflight_api",
                        "status": "blocked",
                        "error": xtts_status.get("message")
                        or "XTTS preflight failed",
                    }
                )
                self._save_proof_data()
                return self.proof_data

            if isinstance(sovits_status, dict):
                if not sovits_status.get("ok", False):
                    self.proof_data["steps"].append(
                        {
                            "step": "preflight_api",
                            "status": "blocked",
                            "error": sovits_status.get("message")
                            or "So-VITS-SVC preflight failed",
                        }
                    )
                    self._save_proof_data()
                    return self.proof_data

                inference_configured = bool(
                    sovits_status.get("inference_command_configured", False)
                )
                allow_passthrough = bool(sovits_status.get("allow_passthrough", False))
                if not inference_configured and not allow_passthrough:
                    self.proof_data["steps"].append(
                        {
                            "step": "preflight_api",
                            "status": "blocked",
                            "error": (
                                "So-VITS-SVC inference command not configured. "
                                "Set SOVITS_SVC_INFER_COMMAND or engine infer_command."
                            ),
                        }
                    )
                    self._save_proof_data()
                    return self.proof_data

        reference_audio_path = os.path.join(self.output_dir, "reference.wav")
        if not self._generate_reference_audio(reference_audio_path):
            self.proof_data["steps"].append(
                {
                    "step": "reference_audio",
                    "status": "failed",
                    "error": "Reference audio creation failed",
                }
            )
            self._save_proof_data()
            return self.proof_data

        # Step 1: Create source audio via clone
        with open(reference_audio_path, "rb") as audio_file:
            files = {"reference_audio": audio_file}
            data = {
                "text": DEFAULT_TEST_TEXT,
                "engine": "xtts_v2",
                "language": DEFAULT_LANGUAGE,
                "quality_mode": "standard",
            }
            response = requests.post(
                f"{self.backend_url}/api/voice/clone",
                files=files,
                data=data,
                timeout=300,
            )

        if response.status_code not in [200, 201]:
            self.proof_data["steps"].append(
                {
                    "step": "source_synthesis",
                    "status": "failed",
                    "error": response.text,
                }
            )
            self._save_proof_data()
            return self.proof_data

        clone_result = response.json()
        source_audio_id = clone_result.get("audio_id")
        if not source_audio_id:
            self.proof_data["steps"].append(
                {
                    "step": "source_synthesis",
                    "status": "failed",
                    "error": "No audio_id returned",
                }
            )
            self._save_proof_data()
            return self.proof_data

        self.proof_data["steps"].append(
            {
                "step": "source_synthesis",
                "status": "success",
                "audio_id": source_audio_id,
                "device": clone_result.get("device"),
            }
        )

        # Step 2: So-VITS-SVC conversion
        convert_params = {
            "source_audio_id": source_audio_id,
            "target_speaker_model": checkpoint_path,
            "engine_id": "sovits_svc",
            "calculate_quality": True,
            "enhance_quality": True,
        }
        response = requests.post(
            f"{self.backend_url}/api/rvc/convert",
            params=convert_params,
            timeout=300,
        )

        if response.status_code not in [200, 201]:
            self.proof_data["steps"].append(
                {
                    "step": "conversion",
                    "status": "failed",
                    "error": response.text,
                }
            )
            self._save_proof_data()
            return self.proof_data

        convert_result = response.json()
        converted_audio_id = convert_result.get("audio_id")
        audio_url = convert_result.get("audio_url")

        self.proof_data["steps"].append(
            {
                "step": "conversion",
                "status": "success",
                "audio_id": converted_audio_id,
                "audio_url": audio_url,
                "device": convert_result.get("device"),
                "duration": convert_result.get("duration"),
                "quality_metrics": convert_result.get("quality_metrics"),
            }
        )

        if audio_url and converted_audio_id:
            converted_path = os.path.join(
                self.output_dir, f"{converted_audio_id}.wav"
            )
            if self._download_audio(audio_url, converted_path):
                self.proof_data["outputs"]["converted_audio_path"] = converted_path

        # Capture preflight for evidence
        if "preflight" not in self.proof_data["config"]:
            extra_preflight = self._fetch_preflight()
            if extra_preflight:
                self.proof_data["config"]["preflight"] = extra_preflight

        self._save_proof_data()
        return self.proof_data


def main():
    parser = argparse.ArgumentParser(
        description="So-VITS-SVC conversion proof"
    )
    parser.add_argument(
        "--backend-url",
        default=DEFAULT_BACKEND_URL,
        help=f"Backend API URL (default: {DEFAULT_BACKEND_URL})",
    )
    parser.add_argument(
        "--checkpoint-path",
        required=True,
        help="Path to the So-VITS-SVC checkpoint .pth file",
    )
    parser.add_argument(
        "--config-path",
        required=True,
        help="Path to the So-VITS-SVC config .json file",
    )
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Output directory for proof artifacts (default: timestamped)",
    )

    args = parser.parse_args()

    proof = SovitsSvcConversionProof(
        backend_url=args.backend_url,
        output_dir=args.output_dir,
    )
    result = proof.run(args.checkpoint_path, args.config_path)
    if any(step.get("status") != "success" for step in result.get("steps", [])):
        sys.exit(1)
    sys.exit(0)


if __name__ == "__main__":
    main()
