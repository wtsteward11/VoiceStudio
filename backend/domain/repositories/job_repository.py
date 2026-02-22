"""
Job Repository Interface.

Task 2.3: Abstract repository for job state persistence.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from backend.domain.entities.job import Job


class JobRepository(ABC):
    """Abstract repository for job state persistence."""

    @abstractmethod
    async def get_by_id(self, job_id: str, namespace: str = "default") -> Job | None:
        """Get a job by ID and namespace."""
        ...

    @abstractmethod
    async def save(self, job: Job) -> Job:
        """Save or update a job."""
        ...

    @abstractmethod
    async def delete(self, job_id: str, namespace: str = "default") -> bool:
        """Delete a job. Returns True if existed."""
        ...

    @abstractmethod
    async def list_all(
        self,
        namespace: str = "default",
        limit: int = 100,
        offset: int = 0,
        status: str | None = None,
    ) -> list[Job]:
        """List jobs with optional filtering."""
        ...

    @abstractmethod
    async def count(self, namespace: str = "default") -> int:
        """Return total number of jobs in namespace."""
        ...
