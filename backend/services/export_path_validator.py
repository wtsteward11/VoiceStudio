"""
Export path validation for plugin exporters.

Prevents path traversal and ensures export output is under an allowed directory.
"""

from __future__ import annotations

import os
from pathlib import Path


class ExportPathRejectedError(ValueError):
    """Raised when an export path is outside the allowed root."""


def get_export_root() -> Path:
    """
    Return the allowed root directory for plugin export output.

    Resolution order:
    1. VOICESTUDIO_EXPORT_DIR environment variable
    2. VoiceStudio artifacts path (get_path("artifacts"))
    3. System temp directory

    Returns:
        Path to the export root (created if necessary).
    """
    env_path = os.getenv("VOICESTUDIO_EXPORT_DIR")
    if env_path:
        root = Path(env_path)
    else:
        try:
            from backend.config.path_config import get_path

            root = get_path("artifacts")
        except Exception:
            import tempfile

            root = Path(tempfile.gettempdir()) / "voicestudio_export"
    root.mkdir(parents=True, exist_ok=True)
    return root.resolve()


def validate_export_path(requested: str | Path) -> Path:
    """
    Validate that the requested export path is under the allowed root.

    Resolves the path to absolute, normalizes it, and ensures it does not
    escape the export root (rejects '..' traversal and symlink escapes).

    Args:
        requested: User-provided output path (string or Path).

    Returns:
        Resolved, validated Path under the export root.

    Raises:
        ExportPathRejectedError: If the path is outside the allowed root or
            otherwise invalid.
    """
    root = get_export_root()
    requested_path = Path(requested).expanduser().resolve()
    try:
        requested_path.relative_to(root)
    except ValueError:
        raise ExportPathRejectedError(
            f"Export path must be under {root}. Got: {requested_path}"
        ) from None
    return requested_path
