"""
Backend Helper for UI Tests.

Provides utilities for backend API interaction during UI testing.
"""

from __future__ import annotations

import os
import time

import requests

# =============================================================================
# Backend Configuration
# =============================================================================


DEFAULT_BACKEND_URL = "http://127.0.0.1:8000"
BACKEND_URL = os.getenv("VOICESTUDIO_BACKEND_URL", DEFAULT_BACKEND_URL)


# =============================================================================
# Backend Helper Class
# =============================================================================


class BackendHelper:
    """Helper class for backend API interaction during UI tests."""

    def __init__(self, base_url: str = BACKEND_URL):
        """
        Initialize backend helper.

        Args:
            base_url: Backend API base URL.
        """
        self.base_url = base_url.rstrip("/")
        self.session = requests.Session()

    def is_healthy(self) -> bool:
        """
        Check if backend is healthy.

        Returns:
            True if backend responds to health check.
        """
        try:
            response = self.session.get(f"{self.base_url}/api/health", timeout=5)
            return response.status_code == 200
        except requests.RequestException:
            return False

    def wait_for_backend(self, timeout: float = 30, poll_interval: float = 1) -> bool:
        """
        Wait for backend to become healthy.

        Args:
            timeout: Maximum time to wait (seconds).
            poll_interval: Time between checks (seconds).

        Returns:
            True if backend became healthy, False if timeout.
        """
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self.is_healthy():
                return True
            time.sleep(poll_interval)
        return False

    def get(self, endpoint: str, **kwargs) -> requests.Response | None:
        """
        Make GET request to backend.

        Args:
            endpoint: API endpoint (e.g., "/api/profiles").
            **kwargs: Additional requests arguments.

        Returns:
            Response object or None on error.
        """
        try:
            url = f"{self.base_url}{endpoint}"
            return self.session.get(url, **kwargs)
        except requests.RequestException:
            return None

    def post(self, endpoint: str, **kwargs) -> requests.Response | None:
        """
        Make POST request to backend.

        Args:
            endpoint: API endpoint.
            **kwargs: Additional requests arguments.

        Returns:
            Response object or None on error.
        """
        try:
            url = f"{self.base_url}{endpoint}"
            return self.session.post(url, **kwargs)
        except requests.RequestException:
            return None

    # -------------------------------------------------------------------------
    # Profiles API
    # -------------------------------------------------------------------------

    def get_profiles(self) -> list[dict] | None:
        """
        Get list of profiles from backend.

        Returns:
            List of profile dictionaries or None on error.
        """
        response = self.get("/api/profiles")
        if response and response.status_code == 200:
            return response.json()
        return None

    def create_profile(self, name: str, **kwargs) -> dict | None:
        """
        Create a new profile.

        Args:
            name: Profile name.
            **kwargs: Additional profile fields.

        Returns:
            Created profile dict or None on error.
        """
        data = {"name": name, **kwargs}
        response = self.post("/api/profiles", json=data)
        if response and response.status_code in (200, 201):
            return response.json()
        return None

    # -------------------------------------------------------------------------
    # Jobs API
    # -------------------------------------------------------------------------

    def get_jobs(self) -> list[dict] | None:
        """
        Get list of jobs from backend.

        Returns:
            List of job dictionaries or None on error.
        """
        response = self.get("/api/jobs")
        if response and response.status_code == 200:
            return response.json()
        return None

    def get_job_status(self, job_id: str) -> dict | None:
        """
        Get status of a specific job.

        Args:
            job_id: Job identifier.

        Returns:
            Job status dict or None on error.
        """
        response = self.get(f"/api/jobs/{job_id}")
        if response and response.status_code == 200:
            return response.json()
        return None

    # -------------------------------------------------------------------------
    # Voice Synthesis API
    # -------------------------------------------------------------------------

    def synthesize_voice(self, text: str, profile_id: str, **kwargs) -> dict | None:
        """
        Start a voice synthesis job.

        Args:
            text: Text to synthesize.
            profile_id: Profile ID to use.
            **kwargs: Additional synthesis parameters.

        Returns:
            Job info dict or None on error.
        """
        data = {"text": text, "profile_id": profile_id, **kwargs}
        response = self.post("/api/voice/synthesize", json=data)
        if response and response.status_code in (200, 201, 202):
            return response.json()
        return None

    # -------------------------------------------------------------------------
    # GPU Status API
    # -------------------------------------------------------------------------

    def get_gpu_status(self) -> dict | None:
        """
        Get GPU status from backend.

        Returns:
            GPU status dict or None on error.
        """
        response = self.get("/api/gpu/status")
        if response and response.status_code == 200:
            return response.json()
        return None

    # -------------------------------------------------------------------------
    # Training API
    # -------------------------------------------------------------------------

    def get_datasets(self) -> list[dict] | None:
        """
        Get list of training datasets.

        Returns:
            List of dataset dictionaries or None on error.
        """
        response = self.get("/api/training/datasets")
        if response and response.status_code == 200:
            return response.json()
        return None

    def create_dataset(
        self, name: str, description: str = "", audio_file_ids: list[str] | None = None
    ) -> dict | None:
        """
        Create a new training dataset.

        Args:
            name: Dataset name.
            description: Dataset description.
            audio_file_ids: List of audio file IDs to include.

        Returns:
            Created dataset dict or None on error.
        """
        data = {
            "name": name,
            "description": description,
            "audio_file_ids": audio_file_ids or [],
        }
        response = self.post("/api/training/datasets", json=data)
        if response and response.status_code in (200, 201):
            return response.json()
        return None

    def get_training_jobs(self, status: str | None = None) -> list[dict] | None:
        """
        Get list of training jobs.

        Args:
            status: Optional status filter (running, completed, failed, etc.).

        Returns:
            List of training job dictionaries or None on error.
        """
        endpoint = "/api/training/jobs"
        if status:
            endpoint += f"?status={status}"
        response = self.get(endpoint)
        if response and response.status_code == 200:
            return response.json()
        return None

    def start_training(self, dataset_id: str, **kwargs) -> dict | None:
        """
        Start a training job.

        Args:
            dataset_id: Dataset ID to train on.
            **kwargs: Additional training parameters.

        Returns:
            Training job info dict or None on error.
        """
        data = {"dataset_id": dataset_id, **kwargs}
        response = self.post("/api/training/start", json=data)
        if response and response.status_code in (200, 201, 202):
            return response.json()
        return None

    def cancel_training(self, job_id: str) -> bool:
        """
        Cancel a training job.

        Args:
            job_id: Training job ID.

        Returns:
            True if cancelled successfully.
        """
        response = self.post(f"/api/training/jobs/{job_id}/cancel")
        return response is not None and response.status_code in (200, 204)

    # -------------------------------------------------------------------------
    # Settings API
    # -------------------------------------------------------------------------

    def get_settings(self) -> dict | None:
        """
        Get application settings.

        Returns:
            Settings dictionary or None on error.
        """
        response = self.get("/api/settings")
        if response and response.status_code == 200:
            return response.json()
        return None

    def update_settings(self, settings: dict) -> dict | None:
        """
        Update application settings.

        Args:
            settings: Dictionary of settings to update.

        Returns:
            Updated settings dict or None on error.
        """
        response = self.put("/api/settings", json=settings)
        if response and response.status_code == 200:
            return response.json()
        return None

    def get_setting(self, key: str) -> str | int | bool | None:
        """
        Get a specific setting value.

        Args:
            key: Setting key name.

        Returns:
            Setting value or None if not found.
        """
        response = self.get(f"/api/settings/{key}")
        if response and response.status_code == 200:
            data = response.json()
            return data.get("value")
        return None

    # -------------------------------------------------------------------------
    # Library API
    # -------------------------------------------------------------------------

    def get_library_items(
        self, folder_id: str | None = None, search: str | None = None
    ) -> list[dict] | None:
        """
        Get library items.

        Args:
            folder_id: Optional folder ID to filter by.
            search: Optional search query.

        Returns:
            List of library item dictionaries or None on error.
        """
        params = {}
        if folder_id:
            params["folder_id"] = folder_id
        if search:
            params["search"] = search

        endpoint = "/api/library/assets"
        if params:
            query = "&".join(f"{k}={v}" for k, v in params.items())
            endpoint += f"?{query}"

        response = self.get(endpoint)
        if response and response.status_code == 200:
            return response.json()
        return None

    def get_library_folders(self) -> list[dict] | None:
        """
        Get library folders.

        Returns:
            List of folder dictionaries or None on error.
        """
        response = self.get("/api/library/folders")
        if response and response.status_code == 200:
            return response.json()
        return None

    def upload_audio(self, file_path: str, folder_id: str | None = None) -> dict | None:
        """
        Upload an audio file to the library.

        Args:
            file_path: Path to audio file.
            folder_id: Optional destination folder ID.

        Returns:
            Created library item dict or None on error.
        """
        try:
            with open(file_path, "rb") as f:
                files = {"file": f}
                data = {}
                if folder_id:
                    data["folder_id"] = folder_id
                response = self.session.post(
                    f"{self.base_url}/api/library/assets/upload",
                    files=files,
                    data=data,
                )
                if response and response.status_code in (200, 201):
                    return response.json()
        # ALLOWED: bare except - best effort, failure acceptable
        except (OSError, requests.RequestException):
            pass
        return None

    def delete_library_item(self, item_id: str) -> bool:
        """
        Delete a library item.

        Args:
            item_id: Library item ID.

        Returns:
            True if deleted successfully.
        """
        response = self.delete(f"/api/library/assets/{item_id}")
        return response is not None and response.status_code in (200, 204)

    # -------------------------------------------------------------------------
    # HTTP Methods
    # -------------------------------------------------------------------------

    def put(self, endpoint: str, **kwargs) -> requests.Response | None:
        """
        Make PUT request to backend.

        Args:
            endpoint: API endpoint.
            **kwargs: Additional requests arguments.

        Returns:
            Response object or None on error.
        """
        try:
            url = f"{self.base_url}{endpoint}"
            return self.session.put(url, **kwargs)
        except requests.RequestException:
            return None

    def delete(self, endpoint: str, **kwargs) -> requests.Response | None:
        """
        Make DELETE request to backend.

        Args:
            endpoint: API endpoint.
            **kwargs: Additional requests arguments.

        Returns:
            Response object or None on error.
        """
        try:
            url = f"{self.base_url}{endpoint}"
            return self.session.delete(url, **kwargs)
        except requests.RequestException:
            return None

    # -------------------------------------------------------------------------
    # Job Polling
    # -------------------------------------------------------------------------

    def wait_for_job(
        self,
        job_id: str,
        timeout: float = 60,
        poll_interval: float = 1,
        terminal_statuses: list[str] | None = None,
    ) -> dict | None:
        """
        Wait for a job to reach a terminal status.

        Args:
            job_id: Job identifier.
            timeout: Maximum time to wait (seconds).
            poll_interval: Time between checks (seconds).
            terminal_statuses: List of statuses that indicate completion.

        Returns:
            Final job status dict or None on timeout/error.
        """
        if terminal_statuses is None:
            terminal_statuses = ["completed", "failed", "cancelled", "error"]

        start_time = time.time()
        while time.time() - start_time < timeout:
            status = self.get_job_status(job_id)
            if status and status.get("status") in terminal_statuses:
                return status
            time.sleep(poll_interval)
        return None

    # -------------------------------------------------------------------------
    # Error Handling
    # -------------------------------------------------------------------------

    def get_last_error(self, response: requests.Response | None) -> str | None:
        """
        Extract error message from response.

        Args:
            response: Response object.

        Returns:
            Error message string or None.
        """
        if response is None:
            return "No response received"
        try:
            if response.status_code >= 400:
                data = response.json()
                return data.get("detail") or data.get("message") or str(data)
        except (ValueError, KeyError):
            return response.text or f"HTTP {response.status_code}"
        return None


# =============================================================================
# Convenience Functions
# =============================================================================


def is_backend_healthy(base_url: str = BACKEND_URL) -> bool:
    """
    Check if backend is healthy.

    Args:
        base_url: Backend URL.

    Returns:
        True if healthy.
    """
    helper = BackendHelper(base_url)
    return helper.is_healthy()


def wait_for_backend(base_url: str = BACKEND_URL, timeout: float = 30) -> bool:
    """
    Wait for backend to become healthy.

    Args:
        base_url: Backend URL.
        timeout: Maximum wait time.

    Returns:
        True if backend became healthy.
    """
    helper = BackendHelper(base_url)
    return helper.wait_for_backend(timeout)


def get_backend_profiles(base_url: str = BACKEND_URL) -> list[dict] | None:
    """
    Get profiles from backend.

    Args:
        base_url: Backend URL.

    Returns:
        List of profiles or None.
    """
    helper = BackendHelper(base_url)
    return helper.get_profiles()
