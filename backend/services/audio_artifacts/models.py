"""
Data models for the artifact spine.

Milestone 2: AudioArtifact and related types for registry/store.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True)
class AudioArtifact:
    """
    Immutable record for a registered audio artifact.

    Attributes:
        audio_id: Unique identifier (UUID or caller-supplied).
        path: Absolute filesystem path to the audio file.
        ext: File extension (wav, mp3, flac, m4a, ogg).
        duration_sec: Duration in seconds, or None if unknown.
        created_at: ISO 8601 timestamp string.
        created_by: Identifier of creator (e.g., engine name).
        user_id: Optional user context.
        project_id: Optional project context.
        kind: Artifact kind (default "audio").
        source_audio_ids: Optional JSON list of source audio IDs.
        metadata: Optional JSON-serializable metadata.
    """

    audio_id: str
    path: str
    ext: str
    duration_sec: float | None
    created_at: str
    created_by: str
    user_id: str | None = None
    project_id: str | None = None
    kind: str = "audio"
    source_audio_ids: list[str] | None = None
    metadata: dict[str, Any] | None = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dict for JSON serialization."""
        return {
            "audio_id": self.audio_id,
            "path": self.path,
            "ext": self.ext,
            "duration_sec": self.duration_sec,
            "created_at": self.created_at,
            "created_by": self.created_by,
            "user_id": self.user_id,
            "project_id": self.project_id,
            "kind": self.kind,
            "source_audio_ids": (
                json.dumps(self.source_audio_ids) if self.source_audio_ids else None
            ),
            "metadata_json": json.dumps(self.metadata) if self.metadata else None,
        }
