"""
Audio path resolver: canonical service for resolving audio_id to file path.

Platform Spine Migration: Routes use this instead of _get_audio_path from audio.py.
No route imports. Uses AudioRegistry, path_config, and project dirs.
"""

from __future__ import annotations

import os
from pathlib import Path

from backend.config.path_config import get_path
from backend.services.audio_artifacts import AudioRegistry


def resolve_audio_path(audio_id: str) -> str | None:
    """Resolve audio_id to file path.

    Checks (in order):
    1. AudioRegistry (artifact cache)
    2. Audio upload directories (from /api/audio/upload)
    3. Project audio directories (get_path("data")/projects/*/audio/)
    4. Legacy ~/.voicestudio/projects (fallback for compatibility)

    Returns:
        Absolute path to audio file, or None if not found.
    """
    # 1. AudioRegistry
    path = AudioRegistry.get_path(audio_id)
    if path and os.path.exists(path):
        return path

    # 2. Audio upload directories
    upload_base = get_path("audio_uploads")
    upload_wav_dir = str(upload_base / "wav")
    upload_originals_dir = str(upload_base / "originals")

    wav_path = os.path.join(upload_wav_dir, f"{audio_id}.wav")
    if os.path.exists(wav_path):
        return wav_path

    if audio_id.endswith(".wav"):
        wav_path = os.path.join(upload_wav_dir, audio_id)
        if os.path.exists(wav_path):
            return wav_path

    for ext in [".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac"]:
        original_path = os.path.join(upload_originals_dir, f"{audio_id}{ext}")
        if os.path.exists(original_path):
            return original_path

    # 3. Project audio (path_config-backed)
    projects_dir = get_path("data") / "projects"
    if projects_dir.exists():
        for project_dir in projects_dir.glob("*/audio/*"):
            if project_dir.is_file() and project_dir.name == audio_id:
                return str(project_dir)
        for project_dir in projects_dir.glob("*/audio/*"):
            if project_dir.is_file() and audio_id in project_dir.name:
                return str(project_dir)

    # 4. Legacy ~/.voicestudio/projects (compatibility)
    legacy_projects = Path(os.path.expanduser("~")) / ".voicestudio" / "projects"
    if legacy_projects.exists():
        for project_dir in legacy_projects.glob("*/audio/*"):
            if project_dir.is_file() and project_dir.name == audio_id:
                return str(project_dir)
        for project_dir in legacy_projects.glob("*/audio/*"):
            if project_dir.is_file() and audio_id in project_dir.name:
                return str(project_dir)
        if os.path.sep not in audio_id and (os.path.altsep or "") not in audio_id:
            for project_dir in legacy_projects.glob("*/audio/*"):
                if project_dir.is_file() and project_dir.name == audio_id:
                    return str(project_dir)

    return None
