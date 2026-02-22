"""
Voice Profile Repository Interface.

Task 2.3: Abstract repository for voice profile persistence.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from backend.domain.entities.voice_profile import VoiceProfile


class VoiceProfileRepository(ABC):
    """Abstract repository for voice profile persistence."""

    @abstractmethod
    async def get_by_id(self, profile_id: str) -> VoiceProfile | None:
        """Get a voice profile by ID."""
        ...

    @abstractmethod
    async def save(self, profile: VoiceProfile) -> VoiceProfile:
        """Save or update a voice profile."""
        ...

    @abstractmethod
    async def delete(self, profile_id: str) -> bool:
        """Delete a voice profile. Returns True if existed."""
        ...

    @abstractmethod
    async def list_all(
        self,
        limit: int = 100,
        offset: int = 0,
        language: str | None = None,
        search: str | None = None,
    ) -> list[VoiceProfile]:
        """List voice profiles with optional filtering."""
        ...

    @abstractmethod
    async def count(self) -> int:
        """Return total number of profiles."""
        ...
