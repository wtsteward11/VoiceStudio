"""
Backend Version and Build Information
Provides version and build information for diagnostics.
"""

import os
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

# Try to get version from version_lock.json
_version = "1.1.0"
_build_date = None
_git_commit = None

try:
    import json

    project_root = Path(__file__).parent.parent.parent
    version_file = project_root / "version_lock.json"
    if version_file.exists():
        with open(version_file, encoding="utf-8") as f:
            version_data = json.load(f)
            _version = version_data.get("version", "1.1.0")
            _build_date = version_data.get("build_date")
            _git_commit = version_data.get("git_commit")
except Exception:
    ...

# If build date not in version file, use file modification time
if not _build_date:
    try:
        backend_init = Path(__file__).parent / "__init__.py"
        if backend_init.exists():
            _build_date = datetime.fromtimestamp(backend_init.stat().st_mtime).isoformat()
    except Exception:
        _build_date = datetime.utcnow().isoformat()

# Try to get git commit if available
if not _git_commit:
    try:
        import subprocess

        project_root = Path(__file__).parent.parent.parent
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=project_root,
            capture_output=True,
            text=True,
            timeout=2,
        )
        if result.returncode == 0:
            _git_commit = result.stdout.strip()[:8]  # Short commit hash
    except Exception:
        ...


def get_version_info() -> dict[str, Any]:
    """
    Get version and build information.

    Returns:
        Dictionary with version, build date, git commit, Python version, etc.
    """
    return {
        "version": _version,
        "build_date": _build_date,
        "git_commit": _git_commit,
        "python_version": sys.version,
        "python_executable": sys.executable,
        "platform": sys.platform,
        "environment": os.getenv("ENVIRONMENT", "development"),
    }


def get_version_string() -> str:
    """
    Get version string for display.

    Returns:
        Version string
    """
    parts = [f"v{_version}"]
    if _git_commit:
        parts.append(f"({_git_commit})")
    if _build_date:
        parts.append(f"built {_build_date[:10]}")
    return " ".join(parts)
