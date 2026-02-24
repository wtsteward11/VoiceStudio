"""
Audio Test Data Fixtures for Allan Watts Workflow Testing.

Provides standardized test data for comprehensive audio workflow testing:
- Reference to canonical test audio (via tests/fixtures/canonical.py)
- Expected metadata (duration, sample rate, channels)
- Output format configurations
- Validation checksums for output formats

Audio resolution is delegated to tests/fixtures/canonical.py which provides:
1. VOICESTUDIO_TEST_AUDIO environment variable support
2. Canonical WAV from Git LFS (if available)
3. Synthetic generation fallback (via generate_test_audio.py)
"""

from __future__ import annotations

import hashlib
import sys
from dataclasses import dataclass, field
from pathlib import Path

# =============================================================================
# Test Audio File Configuration (delegated to canonical.py)
# =============================================================================

# Project root directory (relative to this file: tests/ui/fixtures/audio_test_data.py)
_PROJECT_ROOT = Path(__file__).resolve().parents[3]

# Add tests/ to path for canonical module import
_TESTS_DIR = _PROJECT_ROOT / "tests"
if str(_TESTS_DIR) not in sys.path:
    sys.path.insert(0, str(_TESTS_DIR))

# Delegate to unified canonical resolution
from fixtures.canonical import resolve_test_audio

# Primary test audio file - resolved via canonical.py
TEST_AUDIO_FILE = resolve_test_audio(prefer_segment=True, allow_synthetic=True)

# Output formats to test conversion
OUTPUT_FORMATS = ["wav", "mp3", "flac", "ogg"]

# MIME types for each format
FORMAT_MIME_TYPES = {
    "m4a": "audio/x-m4a",
    "mp4": "audio/mp4",
    "wav": "audio/wav",
    "mp3": "audio/mpeg",
    "flac": "audio/flac",
    "ogg": "audio/ogg",
}


@dataclass
class AudioMetadata:
    """Expected audio file metadata."""

    filename: str
    extension: str
    expected_duration_seconds: float | None = None  # Will be measured
    expected_sample_rate: int = 44100  # Common m4a rate
    expected_channels: int = 2  # Stereo
    expected_bit_depth: int = 16
    file_size_bytes: int | None = None

    # Tolerance for duration validation (percentage)
    duration_tolerance: float = 0.02  # 2%

    def duration_within_tolerance(self, measured: float) -> bool:
        """Check if measured duration is within tolerance of expected."""
        if self.expected_duration_seconds is None:
            return True  # No expected value set
        tolerance = self.expected_duration_seconds * self.duration_tolerance
        return abs(measured - self.expected_duration_seconds) <= tolerance


@dataclass
class ConversionSpec:
    """Specification for audio format conversion."""

    source_format: str
    target_format: str
    expected_mime_type: str
    preserve_duration: bool = True
    preserve_channels: bool = True
    target_sample_rate: int | None = None  # None = preserve source
    target_bit_rate: str | None = None  # e.g., "192k" for mp3

    def __post_init__(self):
        if self.target_format in FORMAT_MIME_TYPES:
            self.expected_mime_type = FORMAT_MIME_TYPES[self.target_format]


@dataclass
class WorkflowStep:
    """Definition of a workflow step for testing."""

    name: str
    panel_name: str
    nav_id: str
    root_id: str
    key_actions: list[str] = field(default_factory=list)
    expected_events: list[str] = field(default_factory=list)
    timeout_seconds: float = 30.0


# =============================================================================
# Library Asset Configuration
# =============================================================================

# DEPRECATED: Hardcoded asset UUIDs are environment-specific and may not exist.
# Prefer using the `uploaded_asset` or `session_uploaded_asset` pytest fixtures
# from conftest.py which dynamically upload test audio and return asset metadata.
#
# Example usage in tests:
#   def test_something(self, uploaded_asset, api_monitor):
#       asset_id = uploaded_asset["id"]
#       response = api_monitor.get(f"/api/library/assets/{asset_id}")

# Legacy hardcoded assets (for backward compatibility only)
LIBRARY_ASSETS = {
    "allan_watts_1": {
        "id": "305cdf87-73a1-4271-8ae1-3d9407ad51c1",
        "name": "Allan Watts Audio 1",
        "path": r"data\audio_uploads\wav\305cdf87-73a1-4271-8ae1-3d9407ad51c1.wav",
        "_deprecated": True,
    },
    "allan_watts_2": {
        "id": "350c300d-9127-4a9f-9f4a-0016691bcc14",
        "name": "Allan Watts Audio 2",
        "path": r"data\audio_uploads\wav\350c300d-9127-4a9f-9f4a-0016691bcc14.wav",
        "_deprecated": True,
    },
}

# Default asset to use for testing (DEPRECATED - use uploaded_asset fixture)
DEFAULT_TEST_ASSET = LIBRARY_ASSETS["allan_watts_1"]


def get_or_upload_asset(api_monitor, audio_path: Path) -> dict | None:
    """
    Upload audio file and return asset metadata.

    This is the preferred method for obtaining asset IDs in tests.
    Use the `uploaded_asset` fixture in conftest.py for automatic handling.

    Args:
        api_monitor: APIMonitor instance for making requests.
        audio_path: Path to the audio file to upload.

    Returns:
        dict with 'id', 'filename', etc. or None if upload fails.
    """
    if not audio_path.exists():
        return None

    try:
        with open(audio_path, "rb") as f:
            files = {"file": (audio_path.name, f, "audio/wav")}
            response = api_monitor.post("/api/library/assets/upload", files=files)

        if response.status_code in (200, 201):
            data = response.json()
            asset_id = data.get("id") or data.get("asset_id")
            if asset_id:
                return {
                    "id": asset_id,
                    "filename": audio_path.name,
                    "path": str(audio_path),
                    "response": data,
                }
    except Exception:
        pass

    return None


# =============================================================================
# Allan Watts Test Configuration
# =============================================================================

ALLAN_WATTS_METADATA = AudioMetadata(
    filename="Allan Watts.m4a",
    extension="m4a",
    expected_duration_seconds=None,  # Will be auto-detected on first run
    expected_sample_rate=44100,
    expected_channels=2,
    expected_bit_depth=16,
)

# Conversion specifications for each output format
CONVERSION_SPECS = {
    "wav": ConversionSpec(
        source_format="m4a",
        target_format="wav",
        expected_mime_type="audio/wav",
        preserve_duration=True,
        preserve_channels=True,
        target_sample_rate=44100,
    ),
    "mp3": ConversionSpec(
        source_format="m4a",
        target_format="mp3",
        expected_mime_type="audio/mpeg",
        preserve_duration=True,
        preserve_channels=True,
        target_bit_rate="192k",
    ),
    "flac": ConversionSpec(
        source_format="m4a",
        target_format="flac",
        expected_mime_type="audio/flac",
        preserve_duration=True,
        preserve_channels=True,
    ),
    "ogg": ConversionSpec(
        source_format="m4a",
        target_format="ogg",
        expected_mime_type="audio/ogg",
        preserve_duration=True,
        preserve_channels=True,
        target_bit_rate="160k",
    ),
}


# =============================================================================
# Workflow Definitions
# =============================================================================

IMPORT_WORKFLOW = WorkflowStep(
    name="Import Audio",
    panel_name="Library",
    nav_id="NavLibrary",
    root_id="LibraryView_AssetsListView",  # Grid root not exposed; use child ListView
    key_actions=["click_import", "select_file", "wait_upload"],
    expected_events=["AssetAddedEvent"],
    timeout_seconds=30.0,
)

LIBRARY_WORKFLOW = WorkflowStep(
    name="Library Operations",
    panel_name="Library",
    nav_id="NavLibrary",
    root_id="LibraryView_AssetsListView",  # Grid root not exposed; use child ListView
    key_actions=["select_asset", "play", "stop", "context_menu"],
    expected_events=["PlaybackRequestedEvent"],
    timeout_seconds=10.0,
)

TRANSCRIPTION_WORKFLOW = WorkflowStep(
    name="Transcription",
    panel_name="Transcribe",
    nav_id="NavTranscribe",
    root_id="TranscribeView_Root",
    key_actions=["select_audio", "select_engine", "transcribe", "wait_complete"],
    expected_events=["TranscriptionCompletedEvent"],
    timeout_seconds=120.0,  # Transcription can take time
)

CLONING_WORKFLOW = WorkflowStep(
    name="Voice Cloning",
    panel_name="VoiceCloningWizard",
    nav_id="NavCloning",
    root_id="VoiceCloningWizardView_Root",
    key_actions=["upload_reference", "configure", "start_clone", "wait_complete"],
    expected_events=["CloneReferenceSelectedEvent"],
    timeout_seconds=180.0,  # Cloning is slow
)

SYNTHESIS_WORKFLOW = WorkflowStep(
    name="Voice Synthesis",
    panel_name="VoiceSynthesis",
    nav_id="NavGenerate",
    root_id="VoiceSynthesisView_Root",
    key_actions=["select_profile", "enter_text", "synthesize", "play", "add_timeline"],
    expected_events=["SynthesisCompletedEvent", "AddToTimelineEvent"],
    timeout_seconds=60.0,
)

CONVERSION_WORKFLOW = WorkflowStep(
    name="Format Conversion",
    panel_name="Library",
    nav_id="NavLibrary",
    root_id="LibraryView_Root",
    key_actions=["select_asset", "export_dialog", "select_format", "export"],
    expected_events=[],
    timeout_seconds=60.0,
)

# All workflows in execution order
ALL_WORKFLOWS = [
    IMPORT_WORKFLOW,
    LIBRARY_WORKFLOW,
    TRANSCRIPTION_WORKFLOW,
    CLONING_WORKFLOW,
    SYNTHESIS_WORKFLOW,
    CONVERSION_WORKFLOW,
]


# =============================================================================
# Test Text for Synthesis
# =============================================================================

SYNTHESIS_TEST_TEXTS = {
    "short": "This is a test of the voice synthesis system using a cloned voice.",
    "medium": (
        "The quick brown fox jumps over the lazy dog. "
        "This pangram contains every letter of the alphabet and is commonly used for testing. "
        "VoiceStudio provides high-quality voice synthesis and cloning capabilities."
    ),
    "long": (
        "Welcome to VoiceStudio, the comprehensive voice synthesis and cloning platform. "
        "This text is designed to test longer synthesis capabilities including paragraph handling, "
        "sentence boundaries, and proper intonation across multiple sentences. "
        "The system should handle punctuation, numbers like 123, and special characters correctly. "
        "VoiceStudio supports multiple engines including XTTS, Bark, and OpenVoice for diverse voice generation needs."
    ),
    "allan_watts_style": (
        "The art of living is neither careless drifting on the one hand "
        "nor fearful clinging to the past on the other. "
        "It consists in being sensitive to each moment, "
        "in regarding it as utterly new and unique."
    ),
}


# =============================================================================
# Validation Utilities
# =============================================================================


def compute_file_checksum(filepath: Path, algorithm: str = "md5") -> str:
    """
    Compute checksum of a file for validation.

    Args:
        filepath: Path to the file.
        algorithm: Hash algorithm ('md5', 'sha256').

    Returns:
        Hexadecimal checksum string.
    """
    hash_func = hashlib.new(algorithm)
    with open(filepath, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            hash_func.update(chunk)
    return hash_func.hexdigest()


def validate_test_file_exists() -> bool:
    """Check if the Allan Watts test file exists."""
    return TEST_AUDIO_FILE.exists()


def get_file_size_mb(filepath: Path) -> float:
    """Get file size in megabytes."""
    if filepath.exists():
        return filepath.stat().st_size / (1024 * 1024)
    return 0.0


def get_test_audio_info() -> dict:
    """
    Get information about the test audio file.

    Returns:
        Dictionary with file info or error if not found.
    """
    if not TEST_AUDIO_FILE.exists():
        return {
            "exists": False,
            "path": str(TEST_AUDIO_FILE),
            "error": "Test audio file not found",
        }

    return {
        "exists": True,
        "path": str(TEST_AUDIO_FILE),
        "filename": TEST_AUDIO_FILE.name,
        "size_mb": round(get_file_size_mb(TEST_AUDIO_FILE), 2),
        "extension": TEST_AUDIO_FILE.suffix.lower(),
        "mime_type": FORMAT_MIME_TYPES.get(TEST_AUDIO_FILE.suffix.lower().lstrip("."), "unknown"),
    }


# =============================================================================
# Panel Navigation Mappings
# =============================================================================

PANEL_NAVIGATION = {
    "Library": ("NavLibrary", "LibraryView_Root"),
    "Transcribe": ("NavTranscribe", "TranscribeView_Root"),
    "VoiceSynthesis": ("NavGenerate", "VoiceSynthesisView_Root"),
    "VoiceCloningWizard": ("NavCloning", "VoiceCloningWizardView_Root"),
    "VoiceQuickClone": ("NavQuickClone", "VoiceQuickCloneView_Root"),
    "Timeline": ("NavTimeline", "TimelineView_Root"),
    "Profiles": ("NavProfiles", "ProfilesView_Root"),
    "Settings": ("NavSettings", "SettingsView_Root"),
    "Diagnostics": ("NavDiagnostics", "DiagnosticsView_Root"),
}


# =============================================================================
# Event Definitions for Inter-Panel Testing
# =============================================================================

PANEL_EVENTS = {
    "AssetAddedEvent": {
        "source_panel": "Library",
        "triggers": ["file_import", "drag_drop"],
        "expected_data": ["asset_id", "filename", "path"],
    },
    "CloneReferenceSelectedEvent": {
        "source_panel": "Library",
        "triggers": ["context_menu_clone"],
        "expected_data": ["asset_id", "audio_path"],
    },
    "VoiceProfileSelectedEvent": {
        "source_panel": "Library",
        "triggers": ["context_menu_synthesize"],
        "expected_data": ["profile_id"],
    },
    "PlaybackRequestedEvent": {
        "source_panel": "Library",
        "triggers": ["play_button", "double_click"],
        "expected_data": ["asset_id"],
    },
    "SynthesisCompletedEvent": {
        "source_panel": "VoiceSynthesis",
        "triggers": ["synthesis_complete"],
        "expected_data": ["audio_path", "duration"],
    },
    "TranscriptionCompletedEvent": {
        "source_panel": "Transcribe",
        "triggers": ["transcription_complete"],
        "expected_data": ["text", "word_timestamps"],
    },
    "AddToTimelineEvent": {
        "source_panel": "VoiceSynthesis",
        "triggers": ["add_to_timeline_button"],
        "expected_data": ["audio_path", "track_id"],
    },
    "PanelNavigationRequestEvent": {
        "source_panel": "Any",
        "triggers": ["workflow_navigation"],
        "expected_data": ["target_panel", "parameters"],
    },
}
