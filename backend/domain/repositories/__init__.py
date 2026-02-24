"""
Domain Repository Interfaces.

Task 2.3: Abstract repository interfaces for persistence.
"""

from backend.domain.repositories.audio_clip_repository import AudioClipRepository
from backend.domain.repositories.job_repository import JobRepository
from backend.domain.repositories.project_repository import ProjectRepository
from backend.domain.repositories.voice_profile_repository import VoiceProfileRepository

__all__ = [
    "AudioClipRepository",
    "JobRepository",
    "ProjectRepository",
    "VoiceProfileRepository",
]
