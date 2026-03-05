"""
XTTS Service Client

Client adapter for the isolated XTTS microservice.
This allows the main backend to use XTTS without numpy compatibility issues.
"""

from __future__ import annotations

import logging
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import requests

logger = logging.getLogger(__name__)

# Default service URL
XTTS_SERVICE_URL = os.environ.get("XTTS_SERVICE_URL", "http://127.0.0.1:8081")
XTTS_SERVICE_TIMEOUT = int(os.environ.get("XTTS_SERVICE_TIMEOUT", "120"))


class XTTSServiceClient:
    """
    Client for the isolated XTTS microservice.

    The XTTS service runs in a separate Python environment with numpy 1.26.4
    to avoid binary incompatibility issues with the main backend.
    """

    def __init__(self, service_url: str | None = None, auto_start: bool = True):
        self.service_url = service_url or XTTS_SERVICE_URL
        self.auto_start = auto_start
        self._process: subprocess.Popen | None = None
        self._initialized = False

    def _get_service_paths(self) -> tuple:
        """Get paths to XTTS service files."""
        # Find project root
        current = Path(__file__).resolve()
        project_root = current.parent.parent.parent.parent

        xtts_path = project_root / "runtime" / "xtts_service"
        venv_python = xtts_path / ".venv" / "Scripts" / "python.exe"
        service_script = xtts_path / "xtts_service.py"
        setup_marker = xtts_path / ".setup_complete"

        return xtts_path, venv_python, service_script, setup_marker

    def is_service_running(self) -> bool:
        """Check if XTTS service is running."""
        try:
            response = requests.get(f"{self.service_url}/health", timeout=5)
            return bool(response.status_code == 200)
        except Exception:
            return False

    def start_service(self) -> bool:
        """Start the XTTS service if not running."""
        if self.is_service_running():
            logger.info("XTTS service already running")
            return True

        xtts_path, venv_python, service_script, setup_marker = self._get_service_paths()

        # Check if setup is complete
        if not setup_marker.exists():
            logger.error("XTTS service not set up. Run: scripts\\setup\\setup_xtts_venv.ps1")
            return False

        if not venv_python.exists():
            logger.error(f"XTTS venv not found at: {venv_python}")
            return False

        if not service_script.exists():
            logger.error(f"XTTS service script not found at: {service_script}")
            return False

        logger.info("Starting XTTS service...")

        try:
            # Start the service as a subprocess
            self._process = subprocess.Popen(
                [str(venv_python), str(service_script), "--mode", "http", "--port", "8081"],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                cwd=str(xtts_path),
                creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
            )

            # Wait for service to be ready (up to 60 seconds for model loading)
            for _i in range(60):
                time.sleep(1)
                if self.is_service_running():
                    logger.info("XTTS service started successfully")
                    return True
                if self._process.poll() is not None:
                    # Process exited
                    stderr = self._process.stderr.read().decode() if self._process.stderr else ""
                    logger.error(f"XTTS service failed to start: {stderr}")
                    return False

            logger.error("XTTS service startup timeout")
            return False

        except Exception as e:
            logger.error(f"Failed to start XTTS service: {e}")
            return False

    def stop_service(self):
        """Stop the XTTS service."""
        if self._process:
            self._process.terminate()
            self._process.wait(timeout=10)
            self._process = None
            logger.info("XTTS service stopped")

    def ensure_running(self) -> bool:
        """Ensure the service is running, starting it if necessary."""
        if self.is_service_running():
            return True
        if self.auto_start:
            return self.start_service()
        return False

    def synthesize(
        self,
        text: str,
        speaker_wav: str,
        language: str = "en",
        output_path: str | None = None,
    ) -> dict[str, Any]:
        """
        Synthesize speech using XTTS service.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference audio for voice cloning
            language: Language code (default: "en")
            output_path: Optional output path for the audio file

        Returns:
            Dict with success status and output path or error
        """
        if not self.ensure_running():
            return {
                "success": False,
                "error": "XTTS service not available. Run: scripts\\setup\\setup_xtts_venv.ps1",
            }

        try:
            response = requests.post(
                f"{self.service_url}/synthesize",
                json={
                    "text": text,
                    "speaker_wav": speaker_wav,
                    "language": language,
                    "output_path": output_path,
                },
                timeout=XTTS_SERVICE_TIMEOUT,
            )

            if response.status_code == 200:
                return dict(response.json())
            else:
                return {
                    "success": False,
                    "error": f"Service returned status {response.status_code}: {response.text}",
                }

        except requests.Timeout:
            return {"success": False, "error": "XTTS synthesis timeout"}
        except Exception as e:
            return {"success": False, "error": str(e)}

    def synthesize_to_bytes(
        self,
        text: str,
        speaker_wav: str,
        language: str = "en",
    ) -> bytes | None:
        """
        Synthesize speech and return audio bytes directly.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference audio
            language: Language code

        Returns:
            Audio bytes (WAV format) or None on error
        """
        if not self.ensure_running():
            logger.error("XTTS service not available")
            return None

        try:
            response = requests.post(
                f"{self.service_url}/synthesize_and_return",
                json={
                    "text": text,
                    "speaker_wav": speaker_wav,
                    "language": language,
                },
                timeout=XTTS_SERVICE_TIMEOUT,
            )

            if response.status_code == 200:
                return bytes(response.content)
            else:
                logger.error(f"XTTS synthesis failed: {response.text}")
                return None

        except Exception as e:
            logger.error(f"XTTS synthesis error: {e}")
            return None


# Singleton instance
_client: XTTSServiceClient | None = None


def get_xtts_client() -> XTTSServiceClient:
    """Get or create the XTTS service client singleton."""
    global _client
    if _client is None:
        _client = XTTSServiceClient()
    return _client


def is_xtts_available() -> bool:
    """Check if XTTS service is available."""
    client = get_xtts_client()
    return client.is_service_running()


def synthesize_with_xtts(
    text: str,
    speaker_wav: str,
    language: str = "en",
    output_path: str | None = None,
) -> dict[str, Any]:
    """
    Convenience function to synthesize with XTTS.

    Args:
        text: Text to synthesize
        speaker_wav: Reference audio path
        language: Language code
        output_path: Optional output path

    Returns:
        Result dict with success status
    """
    client = get_xtts_client()
    return client.synthesize(text, speaker_wav, language, output_path)
