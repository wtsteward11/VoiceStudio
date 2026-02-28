"""
Data Retention Policies.

Task 2.4.5: Configurable retention with auto-cleanup.
Manages data lifecycle and automatic cleanup.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import shutil
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path

logger = logging.getLogger(__name__)


class RetentionPeriod(Enum):
    """Predefined retention periods."""

    NONE = 0  # Delete immediately
    DAY = 1  # 1 day
    WEEK = 7  # 1 week
    MONTH = 30  # 30 days
    QUARTER = 90  # 90 days
    YEAR = 365  # 1 year
    FOREVER = -1  # Never delete


@dataclass
class RetentionPolicy:
    """A data retention policy."""

    name: str
    path_pattern: str
    retention_days: int
    description: str = ""
    enabled: bool = True
    archive_before_delete: bool = False
    archive_path: str | None = None
    on_delete: Callable[[Path], Awaitable[None]] | None = None


@dataclass
class RetentionConfig:
    """Configuration for data retention."""

    check_interval_hours: int = 24
    dry_run: bool = False
    log_deletions: bool = True
    default_archive_path: str = "data/archive"


class DataRetentionService:
    """
    Data retention and cleanup service.

    Features:
    - Policy-based retention
    - Automatic cleanup scheduling
    - Archive support
    - Deletion logging
    - Custom callbacks
    """

    def __init__(self, config: RetentionConfig | None = None):
        self.config = config or RetentionConfig()

        self._policies: dict[str, RetentionPolicy] = {}
        self._running = False
        self._task: asyncio.Task | None = None
        self._deletion_log: list[dict] = []

    def add_policy(self, policy: RetentionPolicy) -> None:
        """Add a retention policy."""
        self._policies[policy.name] = policy
        logger.info(f"Added retention policy: {policy.name}")

    def remove_policy(self, name: str) -> bool:
        """Remove a retention policy."""
        if name in self._policies:
            del self._policies[name]
            return True
        return False

    async def start(self) -> None:
        """Start the retention service."""
        self._running = True
        self._task = asyncio.create_task(self._cleanup_loop())
        logger.info("Data retention service started")

    async def stop(self) -> None:
        """Stop the retention service."""
        self._running = False
        if self._task:
            self._task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._task
        logger.info("Data retention service stopped")

    async def _cleanup_loop(self) -> None:
        """Background cleanup loop."""
        while self._running:
            try:
                await self.run_cleanup()
                await asyncio.sleep(self.config.check_interval_hours * 3600)
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Cleanup error: {e}")
                await asyncio.sleep(3600)

    async def run_cleanup(self) -> dict[str, int]:
        """
        Run cleanup for all policies.

        Returns:
            Dict of policy_name -> deleted_count
        """
        results = {}

        for name, policy in self._policies.items():
            if not policy.enabled:
                continue

            try:
                count = await self._cleanup_policy(policy)
                results[name] = count
            except Exception as e:
                logger.error(f"Policy {name} cleanup failed: {e}")
                results[name] = -1

        return results

    async def _cleanup_policy(self, policy: RetentionPolicy) -> int:
        """Run cleanup for a single policy."""
        if policy.retention_days < 0:  # FOREVER
            return 0

        cutoff = datetime.now() - timedelta(days=policy.retention_days)
        deleted = 0

        # Find matching paths
        base_path = Path(policy.path_pattern).parent
        pattern = Path(policy.path_pattern).name

        if not base_path.exists():
            return 0

        for path in base_path.glob(pattern):
            if not path.exists():
                continue

            # Check modification time
            mtime = datetime.fromtimestamp(path.stat().st_mtime)

            if mtime < cutoff:
                # Archive if configured
                if policy.archive_before_delete:
                    await self._archive_path(path, policy)

                # Delete
                if not self.config.dry_run:
                    await self._delete_path(path, policy)
                    deleted += 1
                else:
                    logger.info(f"[DRY RUN] Would delete: {path}")
                    deleted += 1

                # Log deletion
                if self.config.log_deletions:
                    self._deletion_log.append(
                        {
                            "policy": policy.name,
                            "path": str(path),
                            "deleted_at": datetime.now().isoformat(),
                            "age_days": (datetime.now() - mtime).days,
                        }
                    )

        if deleted > 0:
            logger.info(f"Policy {policy.name}: cleaned up {deleted} items")

        return deleted

    async def _archive_path(
        self,
        path: Path,
        policy: RetentionPolicy,
    ) -> Path | None:
        """Archive a path before deletion."""
        archive_base = Path(policy.archive_path or self.config.default_archive_path)
        archive_base.mkdir(parents=True, exist_ok=True)

        # Create unique archive name
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        archive_name = f"{timestamp}_{path.name}"
        archive_path = archive_base / policy.name / archive_name
        archive_path.parent.mkdir(parents=True, exist_ok=True)

        try:
            if path.is_file():
                shutil.copy2(path, archive_path)
            else:
                shutil.copytree(path, archive_path)

            logger.debug(f"Archived {path} to {archive_path}")
            return archive_path

        except Exception as e:
            logger.error(f"Archive failed for {path}: {e}")
            return None

    async def _delete_path(
        self,
        path: Path,
        policy: RetentionPolicy,
    ) -> bool:
        """Delete a path."""
        try:
            # Call callback if defined
            if policy.on_delete:
                await policy.on_delete(path)

            if path.is_file():
                path.unlink()
            else:
                shutil.rmtree(path)

            logger.debug(f"Deleted: {path}")
            return True

        except Exception as e:
            logger.error(f"Delete failed for {path}: {e}")
            return False

    def get_deletion_log(self, limit: int = 100) -> list[dict]:
        """Get recent deletions."""
        return self._deletion_log[-limit:]

    async def preview_cleanup(self) -> dict[str, list[str]]:
        """
        Preview what would be deleted.

        Returns:
            Dict of policy_name -> list of paths
        """
        preview = {}

        for name, policy in self._policies.items():
            if not policy.enabled or policy.retention_days < 0:
                continue

            cutoff = datetime.now() - timedelta(days=policy.retention_days)
            paths = []

            base_path = Path(policy.path_pattern).parent
            pattern = Path(policy.path_pattern).name

            if base_path.exists():
                for path in base_path.glob(pattern):
                    mtime = datetime.fromtimestamp(path.stat().st_mtime)
                    if mtime < cutoff:
                        paths.append(str(path))

            if paths:
                preview[name] = paths

        return preview

    def get_stats(self) -> dict:
        """Get retention service statistics."""
        return {
            "policies": len(self._policies),
            "enabled_policies": sum(1 for p in self._policies.values() if p.enabled),
            "total_deletions": len(self._deletion_log),
            "check_interval_hours": self.config.check_interval_hours,
            "dry_run": self.config.dry_run,
        }


# Default policies
DEFAULT_POLICIES = [
    RetentionPolicy(
        name="temp_files",
        path_pattern="data/temp/*",
        retention_days=1,
        description="Temporary processing files",
    ),
    RetentionPolicy(
        name="logs",
        path_pattern="logs/*.log",
        retention_days=30,
        description="Application logs",
        archive_before_delete=True,
    ),
    RetentionPolicy(
        name="exports",
        path_pattern="data/exports/*",
        retention_days=7,
        description="Exported audio files",
    ),
    RetentionPolicy(
        name="cache",
        path_pattern="data/cache/*",
        retention_days=7,
        description="Cached data",
    ),
]


# Global retention service
_retention: DataRetentionService | None = None


def get_data_retention_service() -> DataRetentionService:
    """Get or create the global data retention service."""
    global _retention
    if _retention is None:
        _retention = DataRetentionService()
        for policy in DEFAULT_POLICIES:
            _retention.add_policy(policy)
    return _retention
