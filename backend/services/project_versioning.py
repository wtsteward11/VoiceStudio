"""
Project File Versioning.

Task 1.3.2: Version control for .vsproj files.
Tracks versions of project files with change history.
"""

from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import shutil
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


@dataclass
class ProjectVersion:
    """A version of a project file."""

    version_id: str
    project_id: str
    version_number: int
    file_hash: str
    created_at: datetime
    created_by: str
    message: str
    file_path: str
    size_bytes: int
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass
class VersionDiff:
    """Difference between two versions."""

    from_version: int
    to_version: int
    additions: list[str]
    deletions: list[str]
    modifications: list[str]


@dataclass
class VersioningConfig:
    """Configuration for project versioning."""

    storage_path: str = "data/versions"
    max_versions_per_project: int = 50
    auto_cleanup: bool = True
    compression_enabled: bool = True


class ProjectVersioning:
    """
    Version control system for VoiceStudio project files.

    Features:
    - Automatic version tracking
    - Diff between versions
    - Rollback support
    - Storage optimization
    - Version metadata
    """

    def __init__(self, config: VersioningConfig | None = None):
        self.config = config or VersioningConfig()

        self._storage_path = Path(self.config.storage_path)
        self._storage_path.mkdir(parents=True, exist_ok=True)

        self._versions_index: dict[str, list[ProjectVersion]] = {}
        self._lock = asyncio.Lock()

        # Load existing versions
        self._load_index()

    def _load_index(self) -> None:
        """Load versions index from disk."""
        index_path = self._storage_path / "index.json"

        if index_path.exists():
            try:
                with open(index_path) as f:
                    data = json.load(f)

                for project_id, versions in data.items():
                    self._versions_index[project_id] = [
                        ProjectVersion(
                            version_id=v["version_id"],
                            project_id=v["project_id"],
                            version_number=v["version_number"],
                            file_hash=v["file_hash"],
                            created_at=datetime.fromisoformat(v["created_at"]),
                            created_by=v["created_by"],
                            message=v["message"],
                            file_path=v["file_path"],
                            size_bytes=v["size_bytes"],
                            metadata=v.get("metadata", {}),
                        )
                        for v in versions
                    ]
            except Exception as e:
                logger.error(f"Failed to load version index: {e}")

    def _save_index(self) -> None:
        """Save versions index to disk."""
        index_path = self._storage_path / "index.json"

        data = {}
        for project_id, versions in self._versions_index.items():
            data[project_id] = [
                {
                    "version_id": v.version_id,
                    "project_id": v.project_id,
                    "version_number": v.version_number,
                    "file_hash": v.file_hash,
                    "created_at": v.created_at.isoformat(),
                    "created_by": v.created_by,
                    "message": v.message,
                    "file_path": v.file_path,
                    "size_bytes": v.size_bytes,
                    "metadata": v.metadata,
                }
                for v in versions
            ]

        with open(index_path, "w") as f:
            json.dump(data, f, indent=2)

    def _compute_hash(self, file_path: Path) -> str:
        """Compute SHA-256 hash of file."""
        sha256 = hashlib.sha256()
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(8192), b""):
                sha256.update(chunk)
        return sha256.hexdigest()

    def _get_next_version_number(self, project_id: str) -> int:
        """Get next version number for a project."""
        versions = self._versions_index.get(project_id, [])
        if not versions:
            return 1
        return max(v.version_number for v in versions) + 1

    async def create_version(
        self,
        project_id: str,
        file_path: str,
        message: str = "",
        created_by: str = "system",
        metadata: dict[str, Any] | None = None,
    ) -> ProjectVersion:
        """
        Create a new version of a project file.

        Args:
            project_id: Unique project identifier
            file_path: Path to the project file
            message: Version message/description
            created_by: User who created this version
            metadata: Additional metadata

        Returns:
            Created ProjectVersion
        """
        async with self._lock:
            source = Path(file_path)
            if not source.exists():
                raise FileNotFoundError(f"File not found: {file_path}")

            # Compute hash
            file_hash = self._compute_hash(source)

            # Check if this version already exists (same hash)
            existing = self._versions_index.get(project_id, [])
            for v in existing:
                if v.file_hash == file_hash:
                    logger.info(f"Version with same content already exists: {v.version_id}")
                    return v

            # Create version
            version_number = self._get_next_version_number(project_id)
            version_id = f"{project_id}_v{version_number}"

            # Copy file to storage
            project_dir = self._storage_path / project_id
            project_dir.mkdir(parents=True, exist_ok=True)

            stored_path = project_dir / f"v{version_number}{source.suffix}"
            shutil.copy2(source, stored_path)

            version = ProjectVersion(
                version_id=version_id,
                project_id=project_id,
                version_number=version_number,
                file_hash=file_hash,
                created_at=datetime.now(),
                created_by=created_by,
                message=message,
                file_path=str(stored_path),
                size_bytes=source.stat().st_size,
                metadata=metadata or {},
            )

            # Add to index
            if project_id not in self._versions_index:
                self._versions_index[project_id] = []
            self._versions_index[project_id].append(version)

            # Cleanup old versions
            if self.config.auto_cleanup:
                await self._cleanup_old_versions(project_id)

            # Save index
            self._save_index()

            logger.info(f"Created version {version_id}")
            return version

    async def get_version(
        self,
        project_id: str,
        version_number: int | None = None,
    ) -> ProjectVersion | None:
        """
        Get a specific version or latest.

        Args:
            project_id: Project identifier
            version_number: Specific version, or None for latest
        """
        versions = self._versions_index.get(project_id, [])
        if not versions:
            return None

        if version_number is None:
            return max(versions, key=lambda v: v.version_number)

        for v in versions:
            if v.version_number == version_number:
                return v

        return None

    async def list_versions(
        self,
        project_id: str,
        limit: int = 20,
    ) -> list[ProjectVersion]:
        """List versions for a project."""
        versions = self._versions_index.get(project_id, [])
        return sorted(versions, key=lambda v: v.version_number, reverse=True)[:limit]

    async def restore_version(
        self,
        project_id: str,
        version_number: int,
        target_path: str,
    ) -> bool:
        """
        Restore a specific version to a target path.

        Args:
            project_id: Project identifier
            version_number: Version to restore
            target_path: Where to restore the file
        """
        version = await self.get_version(project_id, version_number)
        if not version:
            logger.warning(f"Version not found: {project_id} v{version_number}")
            return False

        source = Path(version.file_path)
        if not source.exists():
            logger.error(f"Version file missing: {source}")
            return False

        target = Path(target_path)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)

        logger.info(f"Restored {project_id} v{version_number} to {target_path}")
        return True

    async def delete_version(
        self,
        project_id: str,
        version_number: int,
    ) -> bool:
        """Delete a specific version."""
        async with self._lock:
            versions = self._versions_index.get(project_id, [])

            for i, v in enumerate(versions):
                if v.version_number == version_number:
                    # Remove file
                    file_path = Path(v.file_path)
                    if file_path.exists():
                        file_path.unlink()

                    # Remove from index
                    versions.pop(i)
                    self._save_index()

                    logger.info(f"Deleted version {v.version_id}")
                    return True

            return False

    async def _cleanup_old_versions(self, project_id: str) -> int:
        """Remove old versions beyond the limit."""
        versions = self._versions_index.get(project_id, [])

        if len(versions) <= self.config.max_versions_per_project:
            return 0

        # Sort by version number
        sorted_versions = sorted(versions, key=lambda v: v.version_number)

        # Remove oldest versions
        to_remove = len(versions) - self.config.max_versions_per_project
        removed = 0

        for v in sorted_versions[:to_remove]:
            file_path = Path(v.file_path)
            if file_path.exists():
                file_path.unlink()
            versions.remove(v)
            removed += 1

        if removed > 0:
            self._save_index()
            logger.info(f"Cleaned up {removed} old versions for {project_id}")

        return removed

    def get_storage_stats(self) -> dict[str, Any]:
        """Get storage statistics."""
        total_versions = sum(len(v) for v in self._versions_index.values())
        total_size = 0

        for versions in self._versions_index.values():
            for v in versions:
                total_size += v.size_bytes

        return {
            "project_count": len(self._versions_index),
            "total_versions": total_versions,
            "total_size_mb": round(total_size / 1e6, 2),
            "storage_path": str(self._storage_path),
            "max_versions_per_project": self.config.max_versions_per_project,
        }


# Global versioning instance
_versioning: ProjectVersioning | None = None


def get_project_versioning() -> ProjectVersioning:
    """Get or create the global project versioning system."""
    global _versioning
    if _versioning is None:
        _versioning = ProjectVersioning()
    return _versioning
