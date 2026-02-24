"""
Data Retention Policy.

Task 2.4.5: GDPR-inspired data lifecycle.
Manages data retention and automatic deletion.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import shutil
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class DataCategory(Enum):
    """Categories of data for retention purposes."""

    AUDIO_UPLOADS = "audio_uploads"
    VOICE_CLONES = "voice_clones"
    SYNTHESIS_OUTPUT = "synthesis_output"
    PROJECT_FILES = "project_files"
    SESSION_DATA = "session_data"
    AUDIT_LOGS = "audit_logs"
    TEMP_FILES = "temp_files"
    CACHE = "cache"


@dataclass
class RetentionPolicy:
    """Policy for a data category."""

    category: DataCategory
    retention_days: int
    auto_delete: bool = True
    require_confirmation: bool = False
    archive_before_delete: bool = False

    @property
    def retention_period(self) -> timedelta:
        return timedelta(days=self.retention_days)


@dataclass
class RetentionConfig:
    """Configuration for retention service."""

    policies: dict[DataCategory, RetentionPolicy] = field(
        default_factory=lambda: {
            DataCategory.AUDIO_UPLOADS: RetentionPolicy(
                DataCategory.AUDIO_UPLOADS, retention_days=30
            ),
            DataCategory.VOICE_CLONES: RetentionPolicy(
                DataCategory.VOICE_CLONES, retention_days=365
            ),
            DataCategory.SYNTHESIS_OUTPUT: RetentionPolicy(
                DataCategory.SYNTHESIS_OUTPUT, retention_days=7
            ),
            DataCategory.PROJECT_FILES: RetentionPolicy(
                DataCategory.PROJECT_FILES, retention_days=365, archive_before_delete=True
            ),
            DataCategory.SESSION_DATA: RetentionPolicy(DataCategory.SESSION_DATA, retention_days=1),
            DataCategory.AUDIT_LOGS: RetentionPolicy(
                DataCategory.AUDIT_LOGS, retention_days=365, auto_delete=False
            ),
            DataCategory.TEMP_FILES: RetentionPolicy(DataCategory.TEMP_FILES, retention_days=1),
            DataCategory.CACHE: RetentionPolicy(DataCategory.CACHE, retention_days=7),
        }
    )

    # Directory mappings
    category_paths: dict[DataCategory, str] = field(
        default_factory=lambda: {
            DataCategory.AUDIO_UPLOADS: "data/audio_uploads",
            DataCategory.VOICE_CLONES: "data/voice_clones",
            DataCategory.SYNTHESIS_OUTPUT: "data/output",
            DataCategory.PROJECT_FILES: "data/projects",
            DataCategory.SESSION_DATA: "data/sessions",
            DataCategory.AUDIT_LOGS: "data/audit",
            DataCategory.TEMP_FILES: "data/temp",
            DataCategory.CACHE: "data/cache",
        }
    )

    archive_path: str = "data/archive"
    check_interval_hours: int = 24


@dataclass
class DeletionRecord:
    """Record of a deleted item."""

    category: DataCategory
    path: str
    size_bytes: int
    deleted_at: datetime
    archived: bool
    archive_path: str | None = None


class DataRetentionService:
    """
    Manages data lifecycle and retention.

    Features:
    - Configurable retention policies per category
    - Automatic cleanup scheduling
    - Optional archiving before deletion
    - Deletion logging
    - GDPR-style data management
    """

    def __init__(self, config: RetentionConfig | None = None):
        self.config = config or RetentionConfig()

        self._archive_path = Path(self.config.archive_path)
        self._archive_path.mkdir(parents=True, exist_ok=True)

        self._deletion_log: list[DeletionRecord] = []
        self._running = False
        self._task: asyncio.Task | None = None

        self._callbacks: dict[DataCategory, list[Callable]] = {}

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

                # Sleep until next check
                await asyncio.sleep(self.config.check_interval_hours * 3600)

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Cleanup error: {e}")
                await asyncio.sleep(300)  # Retry in 5 minutes

    async def run_cleanup(self) -> dict[DataCategory, int]:
        """
        Run cleanup for all categories.

        Returns:
            Dict of category to deleted count
        """
        results: dict[DataCategory, int] = {}

        for category, policy in self.config.policies.items():
            if not policy.auto_delete:
                continue

            count = await self.cleanup_category(category)
            results[category] = count

        return results

    async def cleanup_category(self, category: DataCategory) -> int:
        """
        Clean up expired data for a category.

        Returns:
            Number of items deleted
        """
        policy = self.config.policies.get(category)
        if not policy:
            return 0

        path = self.config.category_paths.get(category)
        if not path:
            return 0

        category_path = Path(path)
        if not category_path.exists():
            return 0

        cutoff = datetime.now() - policy.retention_period
        deleted_count = 0

        for item in category_path.iterdir():
            try:
                # Get modification time
                mtime = datetime.fromtimestamp(item.stat().st_mtime)

                if mtime < cutoff:
                    # Archive if needed
                    archive_path = None
                    if policy.archive_before_delete:
                        archive_path = await self._archive_item(item, category)

                    # Calculate size before deletion
                    size = self._get_size(item)

                    # Delete
                    if item.is_dir():
                        shutil.rmtree(item)
                    else:
                        item.unlink()

                    # Record deletion
                    record = DeletionRecord(
                        category=category,
                        path=str(item),
                        size_bytes=size,
                        deleted_at=datetime.now(),
                        archived=archive_path is not None,
                        archive_path=archive_path,
                    )
                    self._deletion_log.append(record)

                    # Invoke callbacks
                    await self._invoke_callbacks(category, record)

                    deleted_count += 1
                    logger.debug(f"Deleted expired item: {item}")

            except Exception as e:
                logger.error(f"Failed to cleanup {item}: {e}")

        if deleted_count > 0:
            logger.info(f"Cleaned up {deleted_count} items from {category.value}")

        return deleted_count

    async def _archive_item(self, item: Path, category: DataCategory) -> str:
        """Archive an item before deletion."""
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        archive_name = f"{category.value}_{timestamp}_{item.name}"
        dest = self._archive_path / archive_name

        if item.is_dir():
            shutil.copytree(item, dest)
        else:
            shutil.copy2(item, dest)

        return str(dest)

    def _get_size(self, path: Path) -> int:
        """Get size of file or directory."""
        if path.is_file():
            return path.stat().st_size

        total = 0
        for item in path.rglob("*"):
            if item.is_file():
                total += item.stat().st_size
        return total

    def register_callback(
        self,
        category: DataCategory,
        callback: Callable[[DeletionRecord], None],
    ) -> None:
        """Register a callback for deletion events."""
        if category not in self._callbacks:
            self._callbacks[category] = []
        self._callbacks[category].append(callback)

    async def _invoke_callbacks(
        self,
        category: DataCategory,
        record: DeletionRecord,
    ) -> None:
        """Invoke registered callbacks."""
        callbacks = self._callbacks.get(category, [])
        for callback in callbacks:
            try:
                if asyncio.iscoroutinefunction(callback):
                    await callback(record)
                else:
                    callback(record)
            except Exception as e:
                logger.error(f"Callback error: {e}")

    def get_policy(self, category: DataCategory) -> RetentionPolicy | None:
        """Get retention policy for a category."""
        return self.config.policies.get(category)

    def update_policy(self, policy: RetentionPolicy) -> None:
        """Update a retention policy."""
        self.config.policies[policy.category] = policy
        logger.info(f"Updated policy for {policy.category.value}: {policy.retention_days} days")

    def get_deletion_log(
        self,
        category: DataCategory | None = None,
        since: datetime | None = None,
    ) -> list[DeletionRecord]:
        """Get deletion records."""
        records = self._deletion_log

        if category:
            records = [r for r in records if r.category == category]

        if since:
            records = [r for r in records if r.deleted_at >= since]

        return records

    async def get_storage_usage(self) -> dict[str, dict]:
        """Get storage usage per category."""
        usage = {}

        for category, path in self.config.category_paths.items():
            category_path = Path(path)

            if not category_path.exists():
                usage[category.value] = {
                    "exists": False,
                    "size_bytes": 0,
                    "file_count": 0,
                }
                continue

            size = self._get_size(category_path)
            count = sum(1 for _ in category_path.rglob("*") if _.is_file())

            policy = self.config.policies.get(category)

            entry: dict[str, Any] = {
                "exists": True,
                "size_bytes": size,
                "size_mb": round(size / (1024 * 1024), 2),
                "file_count": count,
                "retention_days": policy.retention_days if policy else None,
                "auto_delete": policy.auto_delete if policy else None,
            }
            usage[category.value] = entry

        return usage


# Global retention service
_service: DataRetentionService | None = None


def get_retention_service() -> DataRetentionService:
    """Get or create the global retention service."""
    global _service
    if _service is None:
        _service = DataRetentionService()
    return _service
