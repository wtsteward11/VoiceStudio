"""Security utilities for input validation and path safety.

Provides functions used by routes and middleware to validate user inputs,
prevent path traversal attacks, and sanitize data.
"""

from __future__ import annotations

import os
import re


_TRAVERSAL_PATTERNS = re.compile(
    r"(\.\./|\.\.\\|%2e%2e|%252e|%c0%ae|%c1%9c|\x00|\\\\[a-zA-Z])"
    r"|^[a-zA-Z]:\\",
    re.IGNORECASE,
)

_MAX_INPUT_LENGTH = 100_000


def is_path_safe(path: str) -> bool:
    """Check if a file path is safe from traversal attacks.

    Returns False for paths containing traversal sequences, null bytes,
    UNC paths, or absolute Windows paths outside the app directory.
    """
    if not path or not isinstance(path, str):
        return False

    if "\x00" in path:
        return False

    if _TRAVERSAL_PATTERNS.search(path):
        return False

    normalized = os.path.normpath(path)
    if normalized.startswith("..") or normalized.startswith(os.sep + os.sep):
        return False

    return True


def sanitize_input(value: str) -> str:
    """Sanitize user input by removing null bytes and limiting length."""
    if not isinstance(value, str):
        return ""

    result = value.replace("\x00", "")

    if len(result) > _MAX_INPUT_LENGTH:
        result = result[:_MAX_INPUT_LENGTH]

    return result


def sanitize_filename(filename: str) -> str:
    """Sanitize a filename by removing path separators and dangerous characters."""
    if not filename:
        return "unnamed"

    basename = os.path.basename(filename)
    safe = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", basename)

    if not safe or safe.startswith("."):
        safe = "file_" + safe

    return safe[:255]
