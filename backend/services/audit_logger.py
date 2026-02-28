"""
Audit Logging System.

Task 1.3.5: Track all data modifications.
Comprehensive audit trail for data changes.
"""

from __future__ import annotations

import asyncio
import contextlib
import json
import logging
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any
from uuid import uuid4

logger = logging.getLogger(__name__)


class AuditAction(Enum):
    """Types of auditable actions."""

    CREATE = "create"
    READ = "read"
    UPDATE = "update"
    DELETE = "delete"
    LOGIN = "login"
    LOGOUT = "logout"
    EXPORT = "export"
    IMPORT = "import"
    EXECUTE = "execute"
    CONFIG_CHANGE = "config_change"


class AuditSeverity(Enum):
    """Severity levels for audit events."""

    DEBUG = "debug"
    INFO = "info"
    WARNING = "warning"
    ERROR = "error"
    CRITICAL = "critical"


@dataclass
class AuditEntry:
    """An audit log entry."""

    id: str
    timestamp: datetime
    action: AuditAction
    severity: AuditSeverity
    entity_type: str
    entity_id: str | None
    user_id: str | None
    user_ip: str | None
    session_id: str | None
    old_value: dict[str, Any] | None
    new_value: dict[str, Any] | None
    metadata: dict[str, Any]
    success: bool
    error_message: str | None = None


@dataclass
class AuditConfig:
    """Configuration for audit logging."""

    storage_path: str = "data/audit"
    max_entries_per_file: int = 10000
    retention_days: int = 90
    log_reads: bool = False
    sensitive_fields: list[str] = field(
        default_factory=lambda: ["password", "token", "secret", "api_key", "credential"]
    )
    async_writes: bool = True


class AuditLogger:
    """
    Audit logging system for tracking data modifications.

    Features:
    - Comprehensive audit trail
    - Sensitive data masking
    - Async write support
    - Query and search
    - Retention management
    """

    def __init__(self, config: AuditConfig | None = None):
        self.config = config or AuditConfig()

        self._storage_path = Path(self.config.storage_path)
        self._storage_path.mkdir(parents=True, exist_ok=True)

        self._current_file: Path | None = None
        self._current_count = 0
        self._write_queue: asyncio.Queue = asyncio.Queue()
        self._writer_task: asyncio.Task | None = None
        self._lock = asyncio.Lock()
        self._running = False

    async def start(self) -> None:
        """Start the audit logger."""
        self._running = True
        if self.config.async_writes:
            self._writer_task = asyncio.create_task(self._writer_loop())
        logger.info("Audit logger started")

    async def stop(self) -> None:
        """Stop the audit logger."""
        self._running = False
        if self._writer_task:
            # Flush remaining entries
            while not self._write_queue.empty():
                await asyncio.sleep(0.1)
            self._writer_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._writer_task
        logger.info("Audit logger stopped")

    async def _writer_loop(self) -> None:
        """Background writer loop for async writes."""
        while self._running:
            try:
                entry = await asyncio.wait_for(self._write_queue.get(), timeout=1.0)
                await self._write_entry(entry)
            except asyncio.TimeoutError:
                continue
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Audit writer error: {e}")

    def _get_current_file(self) -> Path:
        """Get or create current log file."""
        today = datetime.now().strftime("%Y-%m-%d")
        file_path = self._storage_path / f"audit_{today}.jsonl"

        if self._current_file != file_path:
            self._current_file = file_path
            self._current_count = 0
            if file_path.exists():
                with open(file_path) as f:
                    self._current_count = sum(1 for _ in f)

        return file_path

    def _mask_sensitive(self, data: dict[str, Any] | None) -> dict[str, Any] | None:
        """Mask sensitive fields in data."""
        if not data:
            return data

        masked = {}
        for key, value in data.items():
            if any(s in key.lower() for s in self.config.sensitive_fields):
                masked[key] = "***MASKED***"
            elif isinstance(value, dict):
                masked[key] = self._mask_sensitive(value)
            else:
                masked[key] = value

        return masked

    async def log(
        self,
        action: AuditAction,
        entity_type: str,
        entity_id: str | None = None,
        user_id: str | None = None,
        user_ip: str | None = None,
        session_id: str | None = None,
        old_value: dict[str, Any] | None = None,
        new_value: dict[str, Any] | None = None,
        metadata: dict[str, Any] | None = None,
        success: bool = True,
        error_message: str | None = None,
        severity: AuditSeverity = AuditSeverity.INFO,
    ) -> str:
        """
        Log an audit entry.

        Args:
            action: Type of action
            entity_type: Type of entity being modified
            entity_id: ID of the entity
            user_id: ID of user performing action
            user_ip: IP address of user
            session_id: Session identifier
            old_value: Previous value (for updates)
            new_value: New value (for creates/updates)
            metadata: Additional context
            success: Whether action succeeded
            error_message: Error message if failed
            severity: Severity level

        Returns:
            Audit entry ID
        """
        # Skip reads if configured
        if action == AuditAction.READ and not self.config.log_reads:
            return ""

        entry = AuditEntry(
            id=str(uuid4()),
            timestamp=datetime.now(),
            action=action,
            severity=severity,
            entity_type=entity_type,
            entity_id=entity_id,
            user_id=user_id,
            user_ip=user_ip,
            session_id=session_id,
            old_value=self._mask_sensitive(old_value),
            new_value=self._mask_sensitive(new_value),
            metadata=metadata or {},
            success=success,
            error_message=error_message,
        )

        if self.config.async_writes:
            await self._write_queue.put(entry)
        else:
            await self._write_entry(entry)

        return entry.id

    async def _write_entry(self, entry: AuditEntry) -> None:
        """Write entry to storage."""
        async with self._lock:
            file_path = self._get_current_file()

            entry_dict = {
                "id": entry.id,
                "timestamp": entry.timestamp.isoformat(),
                "action": entry.action.value,
                "severity": entry.severity.value,
                "entity_type": entry.entity_type,
                "entity_id": entry.entity_id,
                "user_id": entry.user_id,
                "user_ip": entry.user_ip,
                "session_id": entry.session_id,
                "old_value": entry.old_value,
                "new_value": entry.new_value,
                "metadata": entry.metadata,
                "success": entry.success,
                "error_message": entry.error_message,
            }

            with open(file_path, "a") as f:
                f.write(json.dumps(entry_dict) + "\n")

            self._current_count += 1

    # Convenience methods
    async def log_create(
        self,
        entity_type: str,
        entity_id: str,
        new_value: dict[str, Any],
        **kwargs,
    ) -> str:
        """Log a create action."""
        return await self.log(
            action=AuditAction.CREATE,
            entity_type=entity_type,
            entity_id=entity_id,
            new_value=new_value,
            **kwargs,
        )

    async def log_update(
        self,
        entity_type: str,
        entity_id: str,
        old_value: dict[str, Any],
        new_value: dict[str, Any],
        **kwargs,
    ) -> str:
        """Log an update action."""
        return await self.log(
            action=AuditAction.UPDATE,
            entity_type=entity_type,
            entity_id=entity_id,
            old_value=old_value,
            new_value=new_value,
            **kwargs,
        )

    async def log_delete(
        self,
        entity_type: str,
        entity_id: str,
        old_value: dict[str, Any] | None = None,
        **kwargs,
    ) -> str:
        """Log a delete action."""
        return await self.log(
            action=AuditAction.DELETE,
            entity_type=entity_type,
            entity_id=entity_id,
            old_value=old_value,
            **kwargs,
        )

    async def log_login(
        self,
        user_id: str,
        user_ip: str | None = None,
        success: bool = True,
        **kwargs,
    ) -> str:
        """Log a login attempt."""
        return await self.log(
            action=AuditAction.LOGIN,
            entity_type="user",
            entity_id=user_id,
            user_id=user_id,
            user_ip=user_ip,
            success=success,
            severity=AuditSeverity.INFO if success else AuditSeverity.WARNING,
            **kwargs,
        )

    async def query(
        self,
        start_date: datetime | None = None,
        end_date: datetime | None = None,
        action: AuditAction | None = None,
        entity_type: str | None = None,
        entity_id: str | None = None,
        user_id: str | None = None,
        limit: int = 100,
    ) -> list[AuditEntry]:
        """
        Query audit entries.

        Args:
            start_date: Filter by start date
            end_date: Filter by end date
            action: Filter by action type
            entity_type: Filter by entity type
            entity_id: Filter by entity ID
            user_id: Filter by user ID
            limit: Maximum entries to return

        Returns:
            List of matching audit entries
        """
        results = []

        # Find relevant files
        files = sorted(self._storage_path.glob("audit_*.jsonl"), reverse=True)

        for file_path in files:
            # Check if file is in date range
            date_str = file_path.stem.replace("audit_", "")
            try:
                file_date = datetime.strptime(date_str, "%Y-%m-%d")
            except ValueError:
                continue

            if start_date and file_date.date() < start_date.date():
                continue
            if end_date and file_date.date() > end_date.date():
                continue

            # Read and filter entries
            with open(file_path) as f:
                for line in f:
                    if len(results) >= limit:
                        return results

                    try:
                        entry_dict = json.loads(line)

                        # Apply filters
                        if action and entry_dict["action"] != action.value:
                            continue
                        if entity_type and entry_dict["entity_type"] != entity_type:
                            continue
                        if entity_id and entry_dict["entity_id"] != entity_id:
                            continue
                        if user_id and entry_dict["user_id"] != user_id:
                            continue

                        entry = AuditEntry(
                            id=entry_dict["id"],
                            timestamp=datetime.fromisoformat(entry_dict["timestamp"]),
                            action=AuditAction(entry_dict["action"]),
                            severity=AuditSeverity(entry_dict["severity"]),
                            entity_type=entry_dict["entity_type"],
                            entity_id=entry_dict["entity_id"],
                            user_id=entry_dict["user_id"],
                            user_ip=entry_dict["user_ip"],
                            session_id=entry_dict["session_id"],
                            old_value=entry_dict["old_value"],
                            new_value=entry_dict["new_value"],
                            metadata=entry_dict["metadata"],
                            success=entry_dict["success"],
                            error_message=entry_dict["error_message"],
                        )

                        results.append(entry)

                    except Exception as e:
                        logger.warning(f"Failed to parse audit entry: {e}")

        return results

    async def get_entity_history(
        self,
        entity_type: str,
        entity_id: str,
        limit: int = 50,
    ) -> list[AuditEntry]:
        """Get complete history for an entity."""
        return await self.query(
            entity_type=entity_type,
            entity_id=entity_id,
            limit=limit,
        )

    async def cleanup_old_entries(self) -> int:
        """Remove entries older than retention period."""
        from datetime import timedelta

        cutoff = datetime.now() - timedelta(days=self.config.retention_days)
        removed = 0

        for file_path in self._storage_path.glob("audit_*.jsonl"):
            date_str = file_path.stem.replace("audit_", "")
            try:
                file_date = datetime.strptime(date_str, "%Y-%m-%d")
                if file_date < cutoff:
                    file_path.unlink()
                    removed += 1
            except ValueError:
                continue

        if removed > 0:
            logger.info(f"Cleaned up {removed} old audit files")

        return removed

    def get_stats(self) -> dict[str, Any]:
        """Get audit statistics."""
        files = list(self._storage_path.glob("audit_*.jsonl"))
        total_size = sum(f.stat().st_size for f in files)

        return {
            "file_count": len(files),
            "total_size_mb": round(total_size / 1e6, 2),
            "retention_days": self.config.retention_days,
            "async_writes": self.config.async_writes,
            "queue_size": self._write_queue.qsize() if self.config.async_writes else 0,
        }


# Global audit logger
_audit_logger: AuditLogger | None = None


def get_audit_logger() -> AuditLogger:
    """Get or create the global audit logger."""
    global _audit_logger
    if _audit_logger is None:
        _audit_logger = AuditLogger()
    return _audit_logger
