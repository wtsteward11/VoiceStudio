"""Audio artifact spine: store and registry for unified artifact pipeline."""

from backend.services.audio_artifacts.errors import (
    ArtifactNotFoundError,
    ArtifactSpineError,
    ArtifactStoreError,
    InvalidExtensionError,
    PathTraversalError,
)
from backend.services.audio_artifacts.models import AudioArtifact
from backend.services.audio_artifacts.registry import (
    AudioRegistry,
    get_audio_registry_facade,
)
from backend.services.audio_artifacts.registry_db import AudioRegistryDB
from backend.services.audio_artifacts.store import AudioArtifactStore, get_audio_artifact_store
from backend.services.audio_artifacts.use_cases import (
    create_audio_artifact_from_file,
    create_audio_artifact_from_wav_array,
)

__all__ = [
    "ArtifactNotFoundError",
    "ArtifactSpineError",
    "ArtifactStoreError",
    "AudioArtifact",
    "AudioArtifactStore",
    "AudioRegistry",
    "AudioRegistryDB",
    "InvalidExtensionError",
    "PathTraversalError",
    "create_audio_artifact_from_file",
    "create_audio_artifact_from_wav_array",
    "get_audio_artifact_store",
    "get_audio_registry_facade",
]
