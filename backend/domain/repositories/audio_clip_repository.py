"""
Audio Clip Repository Interface.

Task 2.3: Abstract repository for audio clip persistence.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from backend.domain.entities.audio_clip import AudioClip


class AudioClipRepository(ABC):
    """Abstract repository for audio clip persistence."""

    @abstractmethod
    async def get_by_id(self, clip_id: str) -> AudioClip | None:
        """Get an audio clip by ID."""
        ...

    @abstractmethod
    async def save(self, clip: AudioClip) -> AudioClip:
        """Save or update an audio clip."""
        ...

    @abstractmethod
    async def delete(self, clip_id: str) -> bool:
        """Delete an audio clip. Returns True if existed."""
        ...

    @abstractmethod
    async def list_by_project(
        self,
        project_id: str,
        limit: int = 100,
        offset: int = 0,
    ) -> list[AudioClip]:
        """List audio clips for a project."""
        ...

    @abstractmethod
    async def count(self) -> int:
        """Return total number of audio clips."""
        ...
