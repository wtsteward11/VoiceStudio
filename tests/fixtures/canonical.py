"""
Canonical test audio paths, validation, and manifest utilities.

The canonical test audio (Allan Watts) is the standard reference for voice cloning,
transcription, and synthesis tests.
"""

from __future__ import annotations

import json
from pathlib import Path

from backend.config.path_config import resolve_payload_path

# Base paths
TESTS_DIR = Path(__file__).resolve().parent.parent
PROJECT_ROOT = TESTS_DIR.parent
CANONICAL_AUDIO_DIR = TESTS_DIR / "assets" / "canonical"
MANIFEST_PATH = CANONICAL_AUDIO_DIR / "manifest.json"

# Standard paths (resolved via pointer files when migrated to payload root, M8)
CANONICAL_WAV = resolve_payload_path(
    Path("tests/assets/canonical/standard/allan_watts.wav"), PROJECT_ROOT
)
CANONICAL_WAV_SEGMENT = resolve_payload_path(
    Path("tests/assets/canonical/standard/allan_watts_15s.wav"), PROJECT_ROOT
)
CANONICAL_ORIGINAL = resolve_payload_path(
    Path("tests/assets/canonical/originals/allan_watts.m4a"), PROJECT_ROOT
)


class CanonicalAudioError(Exception):
    """Raised when canonical audio files are missing or corrupted."""

    pass


def get_manifest() -> dict:
    """Load and return the canonical audio manifest."""
    if not MANIFEST_PATH.exists():
        raise CanonicalAudioError(f"Manifest not found: {MANIFEST_PATH}")
    with open(MANIFEST_PATH, encoding="utf-8") as f:
        return json.load(f)


def get_canonical_wav_path(segment: bool = False, validate: bool = True) -> Path:
    """
    Return path to the standard canonical test audio (WAV).

    Args:
        segment: If True, return the 15-second segment instead of full file.
        validate: If True, raise CanonicalAudioError if file doesn't exist.

    Returns:
        Path to the WAV file.

    Raises:
        CanonicalAudioError: If validate=True and file doesn't exist.
    """
    path = CANONICAL_WAV_SEGMENT if segment else CANONICAL_WAV
    if validate and not path.exists():
        raise CanonicalAudioError(
            f"Canonical WAV not found: {path}\n"
            "Run: ffmpeg -i originals/allan_watts.m4a -acodec pcm_s16le -ar 22050 -ac 1 standard/allan_watts.wav"
        )
    return path


def get_canonical_original_path(validate: bool = True) -> Path:
    """
    Return path to the original canonical test audio (M4A).

    WARNING: Do not modify this file.
    """
    if validate and not CANONICAL_ORIGINAL.exists():
        raise CanonicalAudioError(f"Original M4A not found: {CANONICAL_ORIGINAL}")
    return CANONICAL_ORIGINAL


def get_canonical_duration(segment: bool = False) -> float:
    """Return duration in seconds from manifest."""
    manifest = get_manifest()
    key = "wav_segment" if segment else "wav_full"
    return manifest["canonical_audio"]["formats"][key]["duration_seconds"]


def verify_integrity(path: Path | None = None) -> bool:
    """
    Verify file integrity against manifest SHA256 hash.

    Args:
        path: Specific file to verify. If None, verifies all files.

    Returns:
        True if all verified files match their manifest hashes.
    """
    import hashlib

    manifest = get_manifest()
    files_to_check = []

    if path:
        files_to_check.append(path)
    else:
        files_to_check = [CANONICAL_ORIGINAL, CANONICAL_WAV]
        if CANONICAL_WAV_SEGMENT.exists():
            files_to_check.append(CANONICAL_WAV_SEGMENT)

    for file_path in files_to_check:
        if not file_path.exists():
            return False

        # Find expected hash in manifest
        expected_hash = None
        if "originals" in str(file_path):
            expected_hash = manifest["canonical_audio"]["source"]["sha256"]
        else:
            for fmt in manifest["canonical_audio"]["formats"].values():
                if file_path.name in fmt["path"]:
                    expected_hash = fmt.get("sha256")
                    break

        if not expected_hash or expected_hash.startswith("<"):
            continue  # Skip if hash not yet computed

        actual_hash = hashlib.sha256(file_path.read_bytes()).hexdigest().upper()
        if actual_hash != expected_hash.upper():
            return False

    return True


def resolve_test_audio(
    prefer_segment: bool = True,
    allow_synthetic: bool = True,
    validate: bool = False,
) -> Path:
    """
    Unified resolution for test audio with fallback chain.

    Resolution order:
    1. VOICESTUDIO_TEST_AUDIO environment variable (if set and exists)
    2. Canonical WAV segment (15s, preferred for speed)
    3. Canonical WAV full
    4. Synthetic generation (if allow_synthetic=True and files missing)
    5. Return canonical path anyway (for informative error messages)

    Args:
        prefer_segment: If True, prefer the shorter 15s segment for faster tests.
        allow_synthetic: If True, generate synthetic audio if canonical is missing.
        validate: If True, raise CanonicalAudioError if no audio is available.

    Returns:
        Path to the resolved test audio file.

    Raises:
        CanonicalAudioError: If validate=True and no audio is available.
    """
    import os

    # Priority 1: Environment variable
    env_path = os.environ.get("VOICESTUDIO_TEST_AUDIO")
    if env_path:
        path = Path(env_path)
        if path.exists():
            return path

    # Priority 2/3: Canonical audio (segment or full)
    if prefer_segment and CANONICAL_WAV_SEGMENT.exists():
        return CANONICAL_WAV_SEGMENT
    if CANONICAL_WAV.exists():
        return CANONICAL_WAV
    if not prefer_segment and CANONICAL_WAV_SEGMENT.exists():
        return CANONICAL_WAV_SEGMENT

    # Priority 4: Synthetic generation
    if allow_synthetic:
        synthetic_path = _generate_synthetic_if_needed(prefer_segment)
        if synthetic_path and synthetic_path.exists():
            return synthetic_path

    # Priority 5: Return path for error messages
    target = CANONICAL_WAV_SEGMENT if prefer_segment else CANONICAL_WAV
    if validate:
        raise CanonicalAudioError(
            f"Test audio not found: {target}\n"
            "Run: scripts/setup_test_audio.ps1 or py tests/ui/fixtures/generate_test_audio.py --canonical"
        )
    return target


def _generate_synthetic_if_needed(segment: bool = True) -> Path | None:
    """
    Generate synthetic audio as fallback if canonical is missing.

    Returns:
        Path to generated audio, or None if generation failed.
    """
    import subprocess
    import sys

    target = CANONICAL_WAV_SEGMENT if segment else CANONICAL_WAV
    if target.exists():
        return target

    # Find the generator script
    generator = TESTS_DIR / "ui" / "fixtures" / "generate_test_audio.py"
    if not generator.exists():
        return None

    try:
        result = subprocess.run(
            [sys.executable, str(generator), "--canonical"],
            capture_output=True,
            timeout=60,
            cwd=str(TESTS_DIR.parent),
            check=False,
        )
        if result.returncode == 0 and target.exists():
            return target
    # ALLOWED: bare except - best effort, failure acceptable
    except (subprocess.TimeoutExpired, FileNotFoundError, OSError):
        pass

    return None


def is_synthetic_audio(path: Path | None = None) -> bool:
    """
    Check if the current canonical audio is synthetic (generated fallback).

    Args:
        path: Specific path to check. If None, checks the canonical standard dir.

    Returns:
        True if the audio is synthetic (has .synthetic_marker).
    """
    if path:
        marker = path.parent / ".synthetic_marker"
    else:
        marker = CANONICAL_AUDIO_DIR / "standard" / ".synthetic_marker"
    return marker.exists()
