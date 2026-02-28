"""
Metrics Cleanup Service — Phase 5.2.5

Provides configurable retention policies for metrics and diagnostic data.
Automatically cleans up old metrics files to prevent unbounded disk growth.
All operations are local-first with no external dependencies.
"""

from __future__ import annotations

import asyncio
import json
import logging
import threading
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Optional

logger = logging.getLogger(__name__)

# Default directories to manage
DEFAULT_METRICS_DIRS = [
    Path(".voicestudio/metrics"),
    Path(".voicestudio/traces"),
    Path(".buildlogs/verification"),
]

# Default configuration file
CONFIG_FILE = Path(".voicestudio/metrics_retention.json")


@dataclass
class RetentionPolicy:
    """Configuration for a single retention policy."""

    directory: Path
    max_age_days: int = 7
    max_files: int = 100
    max_size_mb: float = 100.0
    file_pattern: str = "*"
    enabled: bool = True

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "directory": str(self.directory),
            "max_age_days": self.max_age_days,
            "max_files": self.max_files,
            "max_size_mb": self.max_size_mb,
            "file_pattern": self.file_pattern,
            "enabled": self.enabled,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> RetentionPolicy:
        """Create from dictionary."""
        return cls(
            directory=Path(data["directory"]),
            max_age_days=data.get("max_age_days", 7),
            max_files=data.get("max_files", 100),
            max_size_mb=data.get("max_size_mb", 100.0),
            file_pattern=data.get("file_pattern", "*"),
            enabled=data.get("enabled", True),
        )


@dataclass
class CleanupResult:
    """Result of a cleanup operation."""

    directory: Path
    files_deleted: int = 0
    bytes_freed: int = 0
    errors: list[str] = field(default_factory=list)
    skipped: int = 0

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "directory": str(self.directory),
            "files_deleted": self.files_deleted,
            "bytes_freed": self.bytes_freed,
            "bytes_freed_mb": round(self.bytes_freed / (1024 * 1024), 2),
            "errors": self.errors,
            "skipped": self.skipped,
        }


class MetricsCleanupService:
    """
    Service for managing metrics file retention.

    Supports multiple retention policies with configurable limits on:
    - File age (days)
    - Number of files
    - Total directory size

    Thread-safe and suitable for background operation.
    """

    _instance: Optional[MetricsCleanupService] = None
    _lock = threading.Lock()

    def __new__(cls) -> MetricsCleanupService:
        if cls._instance is None:
            with cls._lock:
                if cls._instance is None:
                    instance = super().__new__(cls)
                    instance._policies: dict[str, RetentionPolicy] = {}
                    instance._last_cleanup: datetime | None = None
                    instance._init_default_policies()
                    cls._instance = instance
        return cls._instance

    def _init_default_policies(self) -> None:
        """Initialize default retention policies."""
        # Metrics directory - keep 7 days
        self._policies["metrics"] = RetentionPolicy(
            directory=Path(".voicestudio/metrics"),
            max_age_days=7,
            max_files=50,
            max_size_mb=50.0,
            file_pattern="*.json",
        )

        # Traces directory - keep 3 days (more frequent)
        self._policies["traces"] = RetentionPolicy(
            directory=Path(".voicestudio/traces"),
            max_age_days=3,
            max_files=30,
            max_size_mb=100.0,
            file_pattern="*.jsonl",
        )

        # Verification logs - keep 14 days
        self._policies["verification"] = RetentionPolicy(
            directory=Path(".buildlogs/verification"),
            max_age_days=14,
            max_files=100,
            max_size_mb=50.0,
            file_pattern="*.json",
        )

        # Load any saved configuration
        self._load_config()

    def _load_config(self) -> None:
        """Load configuration from file if it exists."""
        if not CONFIG_FILE.exists():
            return

        try:
            with open(CONFIG_FILE, encoding="utf-8") as f:
                data = json.load(f)

            for name, policy_data in data.get("policies", {}).items():
                self._policies[name] = RetentionPolicy.from_dict(policy_data)

            logger.info("Loaded %d retention policies", len(self._policies))
        except (json.JSONDecodeError, KeyError) as exc:
            logger.warning("Failed to load retention config: %s", exc)

    def save_config(self) -> Path:
        """Save current configuration to file."""
        CONFIG_FILE.parent.mkdir(parents=True, exist_ok=True)

        config = {
            "policies": {name: policy.to_dict() for name, policy in self._policies.items()},
            "last_updated": datetime.now(timezone.utc).isoformat(),
        }

        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2)

        logger.info("Saved retention config to %s", CONFIG_FILE)
        return CONFIG_FILE

    def set_policy(
        self,
        name: str,
        directory: Path,
        max_age_days: int = 7,
        max_files: int = 100,
        max_size_mb: float = 100.0,
        file_pattern: str = "*",
    ) -> None:
        """
        Set or update a retention policy.

        Args:
            name: Policy name (e.g., 'metrics', 'traces')
            directory: Directory to manage
            max_age_days: Delete files older than this
            max_files: Maximum number of files to keep
            max_size_mb: Maximum total size in MB
            file_pattern: Glob pattern for files to manage
        """
        with self._lock:
            self._policies[name] = RetentionPolicy(
                directory=directory,
                max_age_days=max_age_days,
                max_files=max_files,
                max_size_mb=max_size_mb,
                file_pattern=file_pattern,
            )

        logger.info("Set retention policy '%s' for %s", name, directory)

    def get_policy(self, name: str) -> RetentionPolicy | None:
        """Get a retention policy by name."""
        return self._policies.get(name)

    def list_policies(self) -> dict[str, RetentionPolicy]:
        """List all retention policies."""
        return dict(self._policies)

    def cleanup_policy(self, name: str) -> CleanupResult:
        """
        Run cleanup for a specific policy.

        Args:
            name: Policy name

        Returns:
            CleanupResult with statistics
        """
        policy = self._policies.get(name)
        if not policy:
            return CleanupResult(
                directory=Path("."),
                errors=[f"Policy '{name}' not found"],
            )

        if not policy.enabled:
            return CleanupResult(
                directory=policy.directory,
                skipped=1,
            )

        return self._cleanup_directory(policy)

    def cleanup_all(self) -> list[CleanupResult]:
        """
        Run cleanup for all policies.

        Returns:
            List of CleanupResult for each policy
        """
        results = []
        for name in self._policies:
            result = self.cleanup_policy(name)
            results.append(result)

        self._last_cleanup = datetime.now(timezone.utc)
        return results

    def _cleanup_directory(self, policy: RetentionPolicy) -> CleanupResult:
        """
        Clean up a directory according to its policy.

        Applies cleanup in order:
        1. Delete files older than max_age_days
        2. Delete oldest files if count exceeds max_files
        3. Delete oldest files if size exceeds max_size_mb
        """
        result = CleanupResult(directory=policy.directory)

        if not policy.directory.exists():
            logger.debug("Directory %s does not exist", policy.directory)
            return result

        try:
            # Get all matching files with their stats
            files = self._get_files_with_stats(policy.directory, policy.file_pattern)

            if not files:
                return result

            # Sort by modification time (oldest first)
            files.sort(key=lambda x: x[1])

            now = datetime.now(timezone.utc)
            cutoff = now - timedelta(days=policy.max_age_days)

            # Phase 1: Delete files older than max_age_days
            for filepath, mtime, size in files[:]:
                if mtime < cutoff.timestamp():
                    if self._delete_file(filepath, result):
                        files.remove((filepath, mtime, size))

            # Phase 2: Delete oldest files if count exceeds max_files
            while len(files) > policy.max_files:
                filepath, mtime, size = files.pop(0)
                self._delete_file(filepath, result)

            # Phase 3: Delete oldest files if size exceeds max_size_mb
            max_bytes = policy.max_size_mb * 1024 * 1024
            total_size = sum(f[2] for f in files)

            while total_size > max_bytes and files:
                filepath, mtime, size = files.pop(0)
                if self._delete_file(filepath, result):
                    total_size -= size

        except OSError as exc:
            result.errors.append(f"Directory error: {exc}")
            logger.error("Cleanup error for %s: %s", policy.directory, exc)

        if result.files_deleted > 0:
            logger.info(
                "Cleaned %s: deleted %d files, freed %.2f MB",
                policy.directory,
                result.files_deleted,
                result.bytes_freed / (1024 * 1024),
            )

        return result

    def _get_files_with_stats(self, directory: Path, pattern: str) -> list[tuple]:
        """Get list of (path, mtime, size) tuples for matching files."""
        files = []
        for filepath in directory.glob(pattern):
            if filepath.is_file():
                try:
                    stat = filepath.stat()
                    files.append((filepath, stat.st_mtime, stat.st_size))
                except OSError:
                    continue
        return files

    def _delete_file(self, filepath: Path, result: CleanupResult) -> bool:
        """Delete a file and update result statistics."""
        try:
            size = filepath.stat().st_size
            filepath.unlink()
            result.files_deleted += 1
            result.bytes_freed += size
            logger.debug("Deleted: %s", filepath)
            return True
        except OSError as exc:
            result.errors.append(f"Failed to delete {filepath}: {exc}")
            return False

    def get_status(self) -> dict[str, Any]:
        """Get current status of all managed directories."""
        status = {
            "last_cleanup": (self._last_cleanup.isoformat() if self._last_cleanup else None),
            "policies": {},
        }

        for name, policy in self._policies.items():
            dir_status = {
                "enabled": policy.enabled,
                "max_age_days": policy.max_age_days,
                "max_files": policy.max_files,
                "max_size_mb": policy.max_size_mb,
            }

            if policy.directory.exists():
                files = self._get_files_with_stats(policy.directory, policy.file_pattern)
                dir_status["current_files"] = len(files)
                dir_status["current_size_mb"] = round(sum(f[2] for f in files) / (1024 * 1024), 2)
            else:
                dir_status["current_files"] = 0
                dir_status["current_size_mb"] = 0

            status["policies"][name] = dir_status

        return status


def get_cleanup_service() -> MetricsCleanupService:
    """Get the global metrics cleanup service instance."""
    return MetricsCleanupService()


# =============================================================================
# Background Task Support
# =============================================================================


async def run_cleanup_task(
    interval_hours: float = 24.0,
    run_immediately: bool = False,
) -> None:
    """
    Run cleanup as a background task.

    Args:
        interval_hours: Hours between cleanup runs
        run_immediately: Whether to run cleanup immediately on start
    """
    service = get_cleanup_service()

    if run_immediately:
        logger.info("Running immediate cleanup")
        results = service.cleanup_all()
        for result in results:
            if result.files_deleted > 0:
                logger.info(
                    "Cleanup %s: %d files, %.2f MB freed",
                    result.directory,
                    result.files_deleted,
                    result.bytes_freed / (1024 * 1024),
                )

    while True:
        await asyncio.sleep(interval_hours * 3600)

        logger.info("Running scheduled cleanup")
        results = service.cleanup_all()
        total_deleted = sum(r.files_deleted for r in results)
        total_freed = sum(r.bytes_freed for r in results)

        if total_deleted > 0:
            logger.info(
                "Cleanup complete: %d files, %.2f MB freed",
                total_deleted,
                total_freed / (1024 * 1024),
            )
