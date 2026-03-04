"""
Typed exceptions for the artifact spine.

Milestone 2: Artifact store and registry error types.
"""

from __future__ import annotations


class ArtifactSpineError(Exception):
    """Base exception for artifact spine operations."""


class ArtifactNotFoundError(ArtifactSpineError):
    """Raised when an audio_id is not found in the registry."""


class ArtifactStoreError(ArtifactSpineError):
    """Raised when a store operation fails (write, delete, etc.)."""


class InvalidExtensionError(ArtifactSpineError):
    """Raised when an unsupported file extension is requested."""


class PathTraversalError(ArtifactSpineError):
    """
    Raised when a path traversal attempt is detected.

    Reserved for future use; the spine has no API surface that accepts
    user-provided output paths.
    """
