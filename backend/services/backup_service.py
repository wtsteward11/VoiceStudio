"""
Backup/Restore Automation.

Task 1.3.3: Scheduled backups with verification.
Automated backup system with integrity verification.
"""

from __future__ import annotations

import asyncio
import contextlib
import gzip
import hashlib
import json
import logging
import shutil
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class BackupType(Enum):
    """Types of backups."""

    FULL = "full"
    INCREMENTAL = "incremental"
    DIFFERENTIAL = "differential"


class BackupStatus(Enum):
    """Status of a backup."""

    PENDING = "pending"
    IN_PROGRESS = "in_progress"
    COMPLETED = "completed"
    FAILED = "failed"
    VERIFIED = "verified"


@dataclass
class BackupManifest:
    """Manifest for a backup."""

    backup_id: str
    backup_type: BackupType
    created_at: datetime
    completed_at: datetime | None = None
    status: BackupStatus = BackupStatus.PENDING
    size_bytes: int = 0
    file_count: int = 0
    checksum: str = ""
    backup_path: str = ""
    source_paths: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
    error_message: str | None = None


@dataclass
class BackupConfig:
    """Configuration for backup service."""

    backup_path: str = "data/backups"
    retention_days: int = 30
    max_backups: int = 10
    compression: bool = True
    schedule_interval_hours: int = 24
    verify_after_backup: bool = True

    # Paths to backup
    source_paths: list[str] = field(
        default_factory=lambda: [
            "data/projects",
            "data/voices",
            "data/settings",
            "data/voicestudio.db",
        ]
    )

    # Patterns to exclude
    exclude_patterns: list[str] = field(
        default_factory=lambda: [
            "*.tmp",
            "*.log",
            "__pycache__",
            ".git",
        ]
    )


class BackupService:
    """
    Automated backup service with verification.

    Features:
    - Scheduled automatic backups
    - Compression support
    - Integrity verification
    - Retention policy enforcement
    - Restore capabilities
    """

    def __init__(self, config: BackupConfig | None = None):
        self.config = config or BackupConfig()

        self._backup_path = Path(self.config.backup_path)
        self._backup_path.mkdir(parents=True, exist_ok=True)

        self._manifests: dict[str, BackupManifest] = {}
        self._running = False
        self._scheduler_task: asyncio.Task | None = None
        self._lock = asyncio.Lock()

        self._load_manifests()

    def _load_manifests(self) -> None:
        """Load backup manifests from disk."""
        manifests_file = self._backup_path / "manifests.json"

        if manifests_file.exists():
            try:
                with open(manifests_file) as f:
                    data = json.load(f)

                for backup_id, manifest_data in data.items():
                    self._manifests[backup_id] = BackupManifest(
                        backup_id=manifest_data["backup_id"],
                        backup_type=BackupType(manifest_data["backup_type"]),
                        created_at=datetime.fromisoformat(manifest_data["created_at"]),
                        completed_at=(
                            datetime.fromisoformat(manifest_data["completed_at"])
                            if manifest_data.get("completed_at")
                            else None
                        ),
                        status=BackupStatus(manifest_data["status"]),
                        size_bytes=manifest_data.get("size_bytes", 0),
                        file_count=manifest_data.get("file_count", 0),
                        checksum=manifest_data.get("checksum", ""),
                        backup_path=manifest_data.get("backup_path", ""),
                        source_paths=manifest_data.get("source_paths", []),
                        metadata=manifest_data.get("metadata", {}),
                        error_message=manifest_data.get("error_message"),
                    )
            except Exception as e:
                logger.error(f"Failed to load backup manifests: {e}")

    def _save_manifests(self) -> None:
        """Save backup manifests to disk."""
        manifests_file = self._backup_path / "manifests.json"

        data = {}
        for backup_id, manifest in self._manifests.items():
            data[backup_id] = {
                "backup_id": manifest.backup_id,
                "backup_type": manifest.backup_type.value,
                "created_at": manifest.created_at.isoformat(),
                "completed_at": (
                    manifest.completed_at.isoformat() if manifest.completed_at else None
                ),
                "status": manifest.status.value,
                "size_bytes": manifest.size_bytes,
                "file_count": manifest.file_count,
                "checksum": manifest.checksum,
                "backup_path": manifest.backup_path,
                "source_paths": manifest.source_paths,
                "metadata": manifest.metadata,
                "error_message": manifest.error_message,
            }

        with open(manifests_file, "w") as f:
            json.dump(data, f, indent=2)

    async def start_scheduler(self) -> None:
        """Start the backup scheduler."""
        self._running = True
        self._scheduler_task = asyncio.create_task(self._schedule_loop())
        logger.info("Backup scheduler started")

    async def stop_scheduler(self) -> None:
        """Stop the backup scheduler."""
        self._running = False
        if self._scheduler_task:
            self._scheduler_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._scheduler_task
        logger.info("Backup scheduler stopped")

    async def _schedule_loop(self) -> None:
        """Main scheduling loop."""
        while self._running:
            try:
                # Check if backup is needed
                if self._should_run_backup():
                    await self.create_backup()

                # Cleanup old backups
                await self.cleanup_old_backups()

                # Wait for next check
                await asyncio.sleep(3600)  # Check every hour

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Scheduler error: {e}")
                await asyncio.sleep(60)

    def _should_run_backup(self) -> bool:
        """Check if a backup should be run."""
        if not self._manifests:
            return True

        latest = max(self._manifests.values(), key=lambda m: m.created_at)
        age = datetime.now() - latest.created_at
        return age > timedelta(hours=self.config.schedule_interval_hours)

    async def create_backup(
        self,
        backup_type: BackupType = BackupType.FULL,
        message: str = "",
    ) -> BackupManifest:
        """
        Create a new backup.

        Args:
            backup_type: Type of backup to create
            message: Optional description

        Returns:
            Backup manifest
        """
        async with self._lock:
            backup_id = datetime.now().strftime("%Y%m%d_%H%M%S")

            manifest = BackupManifest(
                backup_id=backup_id,
                backup_type=backup_type,
                created_at=datetime.now(),
                status=BackupStatus.IN_PROGRESS,
                source_paths=self.config.source_paths.copy(),
                metadata={"message": message},
            )

            self._manifests[backup_id] = manifest
            self._save_manifests()

            try:
                # Create backup directory
                backup_dir = self._backup_path / backup_id
                backup_dir.mkdir(parents=True, exist_ok=True)

                total_size = 0
                file_count = 0

                # Copy files
                for source_path in self.config.source_paths:
                    source = Path(source_path)
                    if not source.exists():
                        logger.warning(f"Source path not found: {source_path}")
                        continue

                    if source.is_file():
                        dest = backup_dir / source.name
                        shutil.copy2(source, dest)
                        total_size += source.stat().st_size
                        file_count += 1
                    else:
                        dest = backup_dir / source.name
                        shutil.copytree(
                            source,
                            dest,
                            ignore=shutil.ignore_patterns(*self.config.exclude_patterns),
                        )
                        # Count files and size
                        for f in dest.rglob("*"):
                            if f.is_file():
                                total_size += f.stat().st_size
                                file_count += 1

                # Compress if enabled
                if self.config.compression:
                    archive_path = self._backup_path / f"{backup_id}.tar.gz"
                    shutil.make_archive(
                        str(self._backup_path / backup_id),
                        "gztar",
                        backup_dir,
                    )
                    # Remove uncompressed
                    shutil.rmtree(backup_dir)
                    manifest.backup_path = str(archive_path)
                    manifest.size_bytes = archive_path.stat().st_size
                else:
                    manifest.backup_path = str(backup_dir)
                    manifest.size_bytes = total_size

                manifest.file_count = file_count
                manifest.completed_at = datetime.now()
                manifest.status = BackupStatus.COMPLETED

                # Verify if enabled
                if self.config.verify_after_backup:
                    if await self.verify_backup(backup_id):
                        manifest.status = BackupStatus.VERIFIED

                # Compute checksum
                manifest.checksum = self._compute_checksum(manifest.backup_path)

                logger.info(f"Backup completed: {backup_id} ({manifest.size_bytes / 1e6:.2f} MB)")

            except Exception as e:
                manifest.status = BackupStatus.FAILED
                manifest.error_message = str(e)
                logger.error(f"Backup failed: {e}")

            self._save_manifests()
            return manifest

    def _compute_checksum(self, path: str) -> str:
        """Compute SHA-256 checksum of backup."""
        file_path = Path(path)
        if not file_path.exists():
            return ""

        sha256 = hashlib.sha256()
        if file_path.is_file():
            with open(file_path, "rb") as f:
                for chunk in iter(lambda: f.read(8192), b""):
                    sha256.update(chunk)
        else:
            for f in sorted(file_path.rglob("*")):
                if f.is_file():
                    with open(f, "rb") as fp:
                        for chunk in iter(lambda: fp.read(8192), b""):
                            sha256.update(chunk)

        return sha256.hexdigest()

    async def verify_backup(self, backup_id: str) -> bool:
        """
        Verify backup integrity.

        Args:
            backup_id: ID of backup to verify

        Returns:
            True if backup is valid
        """
        manifest = self._manifests.get(backup_id)
        if not manifest:
            return False

        backup_path = Path(manifest.backup_path)
        if not backup_path.exists():
            logger.error(f"Backup file not found: {backup_path}")
            return False

        # Verify checksum
        current_checksum = self._compute_checksum(str(backup_path))
        if manifest.checksum and current_checksum != manifest.checksum:
            logger.error(f"Backup checksum mismatch for {backup_id}")
            return False

        # Verify archive can be read
        if backup_path.suffix == ".gz":
            try:
                with gzip.open(backup_path, "rb") as f:
                    f.read(1024)  # Read first KB
            except Exception as e:
                logger.error(f"Backup archive corrupted: {e}")
                return False

        logger.info(f"Backup verified: {backup_id}")
        return True

    async def restore_backup(
        self,
        backup_id: str,
        target_path: str | None = None,
    ) -> bool:
        """
        Restore a backup.

        Args:
            backup_id: ID of backup to restore
            target_path: Where to restore (None for original locations)

        Returns:
            True if restore succeeded
        """
        manifest = self._manifests.get(backup_id)
        if not manifest:
            logger.error(f"Backup not found: {backup_id}")
            return False

        backup_path = Path(manifest.backup_path)
        if not backup_path.exists():
            logger.error(f"Backup file not found: {backup_path}")
            return False

        try:
            # Extract if compressed
            if backup_path.suffix == ".gz":
                extract_dir = self._backup_path / f"restore_{backup_id}"
                shutil.unpack_archive(backup_path, extract_dir)
                source_dir = extract_dir
            else:
                source_dir = backup_path

            # Restore files
            if target_path:
                target = Path(target_path)
                shutil.copytree(source_dir, target, dirs_exist_ok=True)
            else:
                # Restore to original locations
                for source_path in manifest.source_paths:
                    source = Path(source_path)
                    backup_source = source_dir / source.name
                    if backup_source.exists():
                        if source.exists():
                            if source.is_file():
                                source.unlink()
                            else:
                                shutil.rmtree(source)
                        (
                            shutil.copytree(backup_source, source)
                            if backup_source.is_dir()
                            else shutil.copy2(backup_source, source)
                        )

            # Cleanup
            if backup_path.suffix == ".gz":
                shutil.rmtree(source_dir)

            logger.info(f"Backup restored: {backup_id}")
            return True

        except Exception as e:
            logger.error(f"Restore failed: {e}")
            return False

    async def cleanup_old_backups(self) -> int:
        """
        Remove old backups based on retention policy.

        Returns:
            Number of backups removed
        """
        cutoff = datetime.now() - timedelta(days=self.config.retention_days)

        # Sort by date
        sorted_manifests = sorted(
            self._manifests.values(),
            key=lambda m: m.created_at,
        )

        removed = 0

        # Remove by age
        for manifest in sorted_manifests:
            if manifest.created_at < cutoff:
                await self.delete_backup(manifest.backup_id)
                removed += 1

        # Remove by count
        while len(self._manifests) > self.config.max_backups:
            oldest = sorted_manifests[0]
            await self.delete_backup(oldest.backup_id)
            sorted_manifests.pop(0)
            removed += 1

        if removed > 0:
            logger.info(f"Cleaned up {removed} old backups")

        return removed

    async def delete_backup(self, backup_id: str) -> bool:
        """Delete a backup."""
        manifest = self._manifests.get(backup_id)
        if not manifest:
            return False

        backup_path = Path(manifest.backup_path)
        if backup_path.exists():
            if backup_path.is_file():
                backup_path.unlink()
            else:
                shutil.rmtree(backup_path)

        del self._manifests[backup_id]
        self._save_manifests()

        logger.info(f"Deleted backup: {backup_id}")
        return True

    def list_backups(self) -> list[BackupManifest]:
        """List all backups."""
        return sorted(
            self._manifests.values(),
            key=lambda m: m.created_at,
            reverse=True,
        )

    def get_stats(self) -> dict[str, Any]:
        """Get backup statistics."""
        backups = list(self._manifests.values())

        total_size = sum(m.size_bytes for m in backups)
        verified_count = sum(1 for m in backups if m.status == BackupStatus.VERIFIED)

        return {
            "backup_count": len(backups),
            "total_size_mb": round(total_size / 1e6, 2),
            "verified_count": verified_count,
            "latest_backup": backups[0].created_at.isoformat() if backups else None,
            "retention_days": self.config.retention_days,
            "max_backups": self.config.max_backups,
        }


# Global backup service
_backup_service: BackupService | None = None


def get_backup_service() -> BackupService:
    """Get or create the global backup service."""
    global _backup_service
    if _backup_service is None:
        _backup_service = BackupService()
    return _backup_service
