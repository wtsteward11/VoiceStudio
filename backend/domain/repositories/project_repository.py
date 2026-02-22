"""
Project Repository Interface.

Task 2.3: Abstract repository for project persistence.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from backend.domain.entities.project import Project


class ProjectRepository(ABC):
    """Abstract repository for project persistence."""

    @abstractmethod
    async def get_by_id(self, project_id: str) -> Project | None:
        """Get a project by ID."""
        ...

    @abstractmethod
    async def save(self, project: Project) -> Project:
        """Save or update a project."""
        ...

    @abstractmethod
    async def delete(self, project_id: str) -> bool:
        """Delete a project. Returns True if existed."""
        ...

    @abstractmethod
    async def list_all(
        self,
        limit: int = 100,
        offset: int = 0,
        status: str | None = None,
    ) -> list[Project]:
        """List projects with optional filtering."""
        ...

    @abstractmethod
    async def count(self) -> int:
        """Return total number of projects."""
        ...
