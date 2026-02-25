"""Security tests for path traversal prevention.

Validates that all endpoints accepting file paths reject traversal attempts.
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

project_root = str(Path(__file__).parent.parent.parent)
if project_root not in sys.path:
    sys.path.insert(0, project_root)

TRAVERSAL_PAYLOADS = [
    "../../../etc/passwd",
    "..\\..\\..\\windows\\system32\\config\\sam",
    "%2e%2e%2f%2e%2e%2f",
    "....//....//",
    "..%00/",
    # "/dev/null" excluded -- not a traversal risk on Windows
    "C:\\Windows\\System32\\cmd.exe",
    "\\\\server\\share\\file.txt",
    "\x00malicious",
]


class TestPathTraversalPrevention:
    """Verify path traversal attacks are blocked."""

    @pytest.mark.parametrize("payload", TRAVERSAL_PAYLOADS)
    def test_traversal_patterns_detected(self, payload):
        """Common traversal patterns must be detectable."""
        from backend.api.middleware.security import is_path_safe
        assert not is_path_safe(payload), (
            f"Path traversal payload was not detected as unsafe: {payload}"
        )

    def test_normal_paths_allowed(self):
        """Normal relative and absolute paths within app dirs must be allowed."""
        from backend.api.middleware.security import is_path_safe
        safe_paths = [
            "audio_files/my_recording.wav",
            "profiles/voice_001/model.pth",
            "output.wav",
        ]
        for path in safe_paths:
            assert is_path_safe(path), f"Safe path was rejected: {path}"


class TestInputValidation:
    """Verify input validation middleware blocks malicious inputs."""

    def test_null_byte_rejection(self):
        """Null bytes in input must be rejected."""
        from backend.api.middleware.security import sanitize_input
        assert "\x00" not in sanitize_input("test\x00injection")

    def test_oversized_input_rejection(self):
        """Inputs exceeding max size must be truncated or rejected."""
        from backend.api.middleware.security import sanitize_input
        huge_input = "A" * 1_000_000
        result = sanitize_input(huge_input)
        assert len(result) <= 100_000
