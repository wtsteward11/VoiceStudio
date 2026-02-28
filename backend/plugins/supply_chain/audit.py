"""
Plugin Audit Trail System.

Phase 5B Enhancement: Provides comprehensive audit logging for plugin lifecycle
events with SQLite persistence. Tracks all plugin operations including:
    - Installation, uninstallation, and updates
    - Signature verification results
    - Vulnerability scan findings
    - Runtime events (start, stop, crash)
    - Permission changes
    - Configuration changes

This enables compliance auditing, forensic analysis, and security monitoring.
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import sqlite3
import threading
import uuid
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, Generator, List, Optional

logger = logging.getLogger(__name__)


# =============================================================================
# Enums
# =============================================================================


class AuditEventType(Enum):
    """Types of auditable plugin events."""

    # Lifecycle events
    PLUGIN_INSTALLED = "plugin_installed"
    PLUGIN_UNINSTALLED = "plugin_uninstalled"
    PLUGIN_UPDATED = "plugin_updated"
    PLUGIN_ENABLED = "plugin_enabled"
    PLUGIN_DISABLED = "plugin_disabled"

    # Runtime events
    PLUGIN_STARTED = "plugin_started"
    PLUGIN_STOPPED = "plugin_stopped"
    PLUGIN_CRASHED = "plugin_crashed"
    PLUGIN_RESTARTED = "plugin_restarted"

    # Security events
    SIGNATURE_VERIFIED = "signature_verified"
    SIGNATURE_FAILED = "signature_failed"
    SIGNATURE_MISSING = "signature_missing"
    VULNERABILITY_SCAN = "vulnerability_scan"
    PERMISSION_GRANTED = "permission_granted"
    PERMISSION_DENIED = "permission_denied"

    # Configuration events
    CONFIG_CHANGED = "config_changed"
    SETTINGS_UPDATED = "settings_updated"

    # Resource events
    RESOURCE_LIMIT_EXCEEDED = "resource_limit_exceeded"
    NETWORK_ACCESS_BLOCKED = "network_access_blocked"
    STORAGE_QUOTA_EXCEEDED = "storage_quota_exceeded"

    # General events
    ERROR = "error"
    WARNING = "warning"
    INFO = "info"


class AuditSeverity(Enum):
    """Severity levels for audit events."""

    DEBUG = "debug"  # Debugging information
    INFO = "info"  # Normal operations
    WARNING = "warning"  # Potential issues
    ERROR = "error"  # Errors occurred
    CRITICAL = "critical"  # Critical security events


class AuditCategory(Enum):
    """Categories for grouping audit events."""

    LIFECYCLE = "lifecycle"
    RUNTIME = "runtime"
    SECURITY = "security"
    CONFIGURATION = "configuration"
    RESOURCE = "resource"
    GENERAL = "general"


# =============================================================================
# Data Models
# =============================================================================


@dataclass
class AuditEvent:
    """A single auditable event."""

    event_type: AuditEventType
    plugin_id: str
    severity: AuditSeverity = AuditSeverity.INFO
    category: AuditCategory = AuditCategory.GENERAL
    event_id: str = ""
    timestamp: str = ""
    plugin_version: Optional[str] = None
    actor: Optional[str] = None  # User or system that triggered the event
    details: Dict[str, Any] = field(default_factory=dict)
    metadata: Dict[str, Any] = field(default_factory=dict)

    def __post_init__(self):
        """Initialize event ID and timestamp if not set."""
        if not self.event_id:
            self.event_id = str(uuid.uuid4())
        if not self.timestamp:
            self.timestamp = datetime.now(timezone.utc).isoformat()

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "event_id": self.event_id,
            "event_type": self.event_type.value,
            "category": self.category.value,
            "severity": self.severity.value,
            "plugin_id": self.plugin_id,
            "plugin_version": self.plugin_version,
            "actor": self.actor,
            "timestamp": self.timestamp,
            "details": self.details,
            "metadata": self.metadata,
        }

    def to_json(self, indent: int = 2) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict(), indent=indent)

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> AuditEvent:
        """Create from dictionary."""
        return cls(
            event_id=data.get("event_id", ""),
            event_type=AuditEventType(data.get("event_type", "info")),
            category=AuditCategory(data.get("category", "general")),
            severity=AuditSeverity(data.get("severity", "info")),
            plugin_id=data.get("plugin_id", ""),
            plugin_version=data.get("plugin_version"),
            actor=data.get("actor"),
            timestamp=data.get("timestamp", ""),
            details=data.get("details", {}),
            metadata=data.get("metadata", {}),
        )


@dataclass
class AuditSummary:
    """Summary statistics for audit events."""

    total_events: int = 0
    by_type: Dict[str, int] = field(default_factory=dict)
    by_severity: Dict[str, int] = field(default_factory=dict)
    by_category: Dict[str, int] = field(default_factory=dict)
    by_plugin: Dict[str, int] = field(default_factory=dict)
    first_event: Optional[str] = None
    last_event: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "total_events": self.total_events,
            "by_type": self.by_type,
            "by_severity": self.by_severity,
            "by_category": self.by_category,
            "by_plugin": self.by_plugin,
            "first_event": self.first_event,
            "last_event": self.last_event,
        }


@dataclass
class AuditQuery:
    """Query parameters for filtering audit events."""

    plugin_id: Optional[str] = None
    event_types: Optional[List[AuditEventType]] = None
    severities: Optional[List[AuditSeverity]] = None
    categories: Optional[List[AuditCategory]] = None
    start_time: Optional[str] = None
    end_time: Optional[str] = None
    actor: Optional[str] = None
    limit: int = 100
    offset: int = 0
    order_by: str = "timestamp"
    order_desc: bool = True


# =============================================================================
# SQLite Schema
# =============================================================================


AUDIT_SCHEMA = """
-- Audit events table
CREATE TABLE IF NOT EXISTS audit_events (
    event_id TEXT PRIMARY KEY,
    event_type TEXT NOT NULL,
    category TEXT NOT NULL,
    severity TEXT NOT NULL,
    plugin_id TEXT NOT NULL,
    plugin_version TEXT,
    actor TEXT,
    timestamp TEXT NOT NULL,
    details TEXT,
    metadata TEXT,
    created_at TEXT DEFAULT (datetime('now'))
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_audit_plugin_id ON audit_events(plugin_id);
CREATE INDEX IF NOT EXISTS idx_audit_event_type ON audit_events(event_type);
CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_events(timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_severity ON audit_events(severity);
CREATE INDEX IF NOT EXISTS idx_audit_category ON audit_events(category);
CREATE INDEX IF NOT EXISTS idx_audit_actor ON audit_events(actor);

-- Compound index for common filtered queries
CREATE INDEX IF NOT EXISTS idx_audit_plugin_time
    ON audit_events(plugin_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_type_time
    ON audit_events(event_type, timestamp);

-- Schema version tracking
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT DEFAULT (datetime('now'))
);

-- Insert current schema version
INSERT OR IGNORE INTO schema_version (version) VALUES (1);
"""


# =============================================================================
# Audit Logger
# =============================================================================


class AuditLogger:
    """
    Audit logging service with SQLite persistence.

    Provides thread-safe logging of plugin lifecycle events with:
    - SQLite persistence for audit trail
    - Flexible querying and filtering
    - Summary statistics
    - Export capabilities

    Example:
        >>> logger = AuditLogger(Path("./audit.db"))
        >>> logger.log_event(
        ...     AuditEventType.PLUGIN_INSTALLED,
        ...     plugin_id="my-plugin",
        ...     plugin_version="1.0.0",
        ...     details={"source": "gallery"}
        ... )
    """

    def __init__(
        self,
        db_path: Path,
        auto_initialize: bool = True,
    ):
        """
        Initialize the audit logger.

        Args:
            db_path: Path to the SQLite database file
            auto_initialize: Whether to auto-create schema on init
        """
        self.db_path = Path(db_path)
        self._lock = threading.Lock()
        self._listeners: List[Callable[[AuditEvent], None]] = []

        if auto_initialize:
            self._initialize_database()

    def _initialize_database(self) -> None:
        """Initialize the database schema."""
        self.db_path.parent.mkdir(parents=True, exist_ok=True)

        with self._get_connection() as conn:
            conn.executescript(AUDIT_SCHEMA)
            conn.commit()

        logger.debug(f"Audit database initialized at {self.db_path}")

    @contextmanager
    def _get_connection(self) -> Generator[sqlite3.Connection, None, None]:
        """Get a database connection with thread safety."""
        conn = sqlite3.connect(str(self.db_path), check_same_thread=False)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()

    def add_listener(self, listener: Callable[[AuditEvent], None]) -> None:
        """Add an event listener for real-time notifications."""
        with self._lock:
            self._listeners.append(listener)

    def remove_listener(self, listener: Callable[[AuditEvent], None]) -> None:
        """Remove an event listener."""
        with self._lock:
            if listener in self._listeners:
                self._listeners.remove(listener)

    def _notify_listeners(self, event: AuditEvent) -> None:
        """Notify all listeners of a new event."""
        with self._lock:
            listeners = list(self._listeners)

        for listener in listeners:
            try:
                listener(event)
            except Exception as e:
                logger.warning(f"Audit listener error: {e}")

    def log_event(
        self,
        event_type: AuditEventType,
        plugin_id: str,
        plugin_version: Optional[str] = None,
        severity: AuditSeverity = AuditSeverity.INFO,
        category: Optional[AuditCategory] = None,
        actor: Optional[str] = None,
        details: Optional[Dict[str, Any]] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> AuditEvent:
        """
        Log an audit event.

        Args:
            event_type: Type of event
            plugin_id: Plugin identifier
            plugin_version: Optional version string
            severity: Event severity
            category: Event category (auto-detected if not provided)
            actor: User or system that triggered the event
            details: Event-specific details
            metadata: Additional metadata

        Returns:
            The created AuditEvent
        """
        # Auto-detect category if not provided
        if category is None:
            category = self._infer_category(event_type)

        event = AuditEvent(
            event_type=event_type,
            plugin_id=plugin_id,
            plugin_version=plugin_version,
            severity=severity,
            category=category,
            actor=actor,
            details=details or {},
            metadata=metadata or {},
        )

        self._store_event(event)
        self._notify_listeners(event)

        return event

    def _infer_category(self, event_type: AuditEventType) -> AuditCategory:
        """Infer category from event type."""
        lifecycle_types = {
            AuditEventType.PLUGIN_INSTALLED,
            AuditEventType.PLUGIN_UNINSTALLED,
            AuditEventType.PLUGIN_UPDATED,
            AuditEventType.PLUGIN_ENABLED,
            AuditEventType.PLUGIN_DISABLED,
        }

        runtime_types = {
            AuditEventType.PLUGIN_STARTED,
            AuditEventType.PLUGIN_STOPPED,
            AuditEventType.PLUGIN_CRASHED,
            AuditEventType.PLUGIN_RESTARTED,
        }

        security_types = {
            AuditEventType.SIGNATURE_VERIFIED,
            AuditEventType.SIGNATURE_FAILED,
            AuditEventType.SIGNATURE_MISSING,
            AuditEventType.VULNERABILITY_SCAN,
            AuditEventType.PERMISSION_GRANTED,
            AuditEventType.PERMISSION_DENIED,
        }

        config_types = {
            AuditEventType.CONFIG_CHANGED,
            AuditEventType.SETTINGS_UPDATED,
        }

        resource_types = {
            AuditEventType.RESOURCE_LIMIT_EXCEEDED,
            AuditEventType.NETWORK_ACCESS_BLOCKED,
            AuditEventType.STORAGE_QUOTA_EXCEEDED,
        }

        if event_type in lifecycle_types:
            return AuditCategory.LIFECYCLE
        elif event_type in runtime_types:
            return AuditCategory.RUNTIME
        elif event_type in security_types:
            return AuditCategory.SECURITY
        elif event_type in config_types:
            return AuditCategory.CONFIGURATION
        elif event_type in resource_types:
            return AuditCategory.RESOURCE
        else:
            return AuditCategory.GENERAL

    def _store_event(self, event: AuditEvent) -> None:
        """Store an event in the database."""
        with self._get_connection() as conn:
            conn.execute(
                """
                INSERT INTO audit_events
                (event_id, event_type, category, severity, plugin_id,
                 plugin_version, actor, timestamp, details, metadata)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    event.event_id,
                    event.event_type.value,
                    event.category.value,
                    event.severity.value,
                    event.plugin_id,
                    event.plugin_version,
                    event.actor,
                    event.timestamp,
                    json.dumps(event.details),
                    json.dumps(event.metadata),
                ),
            )
            conn.commit()

    def get_event(self, event_id: str) -> Optional[AuditEvent]:
        """Get a specific event by ID."""
        with self._get_connection() as conn:
            row = conn.execute(
                "SELECT * FROM audit_events WHERE event_id = ?",
                (event_id,),
            ).fetchone()

            if row:
                return self._row_to_event(row)
            return None

    def query_events(self, query: AuditQuery) -> List[AuditEvent]:
        """Query events with flexible filtering."""
        conditions = []
        params = []

        if query.plugin_id:
            conditions.append("plugin_id = ?")
            params.append(query.plugin_id)

        if query.event_types:
            placeholders = ",".join("?" * len(query.event_types))
            conditions.append(f"event_type IN ({placeholders})")
            params.extend(et.value for et in query.event_types)

        if query.severities:
            placeholders = ",".join("?" * len(query.severities))
            conditions.append(f"severity IN ({placeholders})")
            params.extend(s.value for s in query.severities)

        if query.categories:
            placeholders = ",".join("?" * len(query.categories))
            conditions.append(f"category IN ({placeholders})")
            params.extend(c.value for c in query.categories)

        if query.start_time:
            conditions.append("timestamp >= ?")
            params.append(query.start_time)

        if query.end_time:
            conditions.append("timestamp <= ?")
            params.append(query.end_time)

        if query.actor:
            conditions.append("actor = ?")
            params.append(query.actor)

        where_clause = " AND ".join(conditions) if conditions else "1=1"
        order_dir = "DESC" if query.order_desc else "ASC"

        sql = f"""
            SELECT * FROM audit_events
            WHERE {where_clause}
            ORDER BY {query.order_by} {order_dir}
            LIMIT ? OFFSET ?
        """
        params.extend([query.limit, query.offset])

        with self._get_connection() as conn:
            rows = conn.execute(sql, params).fetchall()
            return [self._row_to_event(row) for row in rows]

    def get_plugin_events(
        self,
        plugin_id: str,
        limit: int = 100,
        offset: int = 0,
    ) -> List[AuditEvent]:
        """Get all events for a specific plugin."""
        return self.query_events(AuditQuery(plugin_id=plugin_id, limit=limit, offset=offset))

    def get_recent_events(
        self,
        limit: int = 50,
        severities: Optional[List[AuditSeverity]] = None,
    ) -> List[AuditEvent]:
        """Get recent events, optionally filtered by severity."""
        return self.query_events(AuditQuery(limit=limit, severities=severities))

    def get_security_events(
        self,
        plugin_id: Optional[str] = None,
        limit: int = 100,
    ) -> List[AuditEvent]:
        """Get security-related events."""
        return self.query_events(
            AuditQuery(
                plugin_id=plugin_id,
                categories=[AuditCategory.SECURITY],
                limit=limit,
            )
        )

    def _row_to_event(self, row: sqlite3.Row) -> AuditEvent:
        """Convert a database row to an AuditEvent."""
        return AuditEvent(
            event_id=row["event_id"],
            event_type=AuditEventType(row["event_type"]),
            category=AuditCategory(row["category"]),
            severity=AuditSeverity(row["severity"]),
            plugin_id=row["plugin_id"],
            plugin_version=row["plugin_version"],
            actor=row["actor"],
            timestamp=row["timestamp"],
            details=json.loads(row["details"] or "{}"),
            metadata=json.loads(row["metadata"] or "{}"),
        )

    def get_summary(
        self,
        plugin_id: Optional[str] = None,
        start_time: Optional[str] = None,
        end_time: Optional[str] = None,
    ) -> AuditSummary:
        """Get summary statistics for audit events."""
        conditions = []
        params = []

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)
        if start_time:
            conditions.append("timestamp >= ?")
            params.append(start_time)
        if end_time:
            conditions.append("timestamp <= ?")
            params.append(end_time)

        where_clause = " AND ".join(conditions) if conditions else "1=1"

        summary = AuditSummary()

        with self._get_connection() as conn:
            # Total count
            row = conn.execute(
                f"SELECT COUNT(*) as cnt FROM audit_events WHERE {where_clause}",
                params,
            ).fetchone()
            summary.total_events = row["cnt"]

            # By type
            rows = conn.execute(
                f"""SELECT event_type, COUNT(*) as cnt
                    FROM audit_events WHERE {where_clause}
                    GROUP BY event_type""",
                params,
            ).fetchall()
            summary.by_type = {row["event_type"]: row["cnt"] for row in rows}

            # By severity
            rows = conn.execute(
                f"""SELECT severity, COUNT(*) as cnt
                    FROM audit_events WHERE {where_clause}
                    GROUP BY severity""",
                params,
            ).fetchall()
            summary.by_severity = {row["severity"]: row["cnt"] for row in rows}

            # By category
            rows = conn.execute(
                f"""SELECT category, COUNT(*) as cnt
                    FROM audit_events WHERE {where_clause}
                    GROUP BY category""",
                params,
            ).fetchall()
            summary.by_category = {row["category"]: row["cnt"] for row in rows}

            # By plugin
            rows = conn.execute(
                f"""SELECT plugin_id, COUNT(*) as cnt
                    FROM audit_events WHERE {where_clause}
                    GROUP BY plugin_id
                    ORDER BY cnt DESC
                    LIMIT 20""",
                params,
            ).fetchall()
            summary.by_plugin = {row["plugin_id"]: row["cnt"] for row in rows}

            # First and last events
            row = conn.execute(
                f"""SELECT MIN(timestamp) as first_ts, MAX(timestamp) as last_ts
                    FROM audit_events WHERE {where_clause}""",
                params,
            ).fetchone()
            summary.first_event = row["first_ts"]
            summary.last_event = row["last_ts"]

        return summary

    def count_events(
        self,
        plugin_id: Optional[str] = None,
        event_type: Optional[AuditEventType] = None,
    ) -> int:
        """Count events matching criteria."""
        conditions = []
        params = []

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)
        if event_type:
            conditions.append("event_type = ?")
            params.append(event_type.value)

        where_clause = " AND ".join(conditions) if conditions else "1=1"

        with self._get_connection() as conn:
            row = conn.execute(
                f"SELECT COUNT(*) as cnt FROM audit_events WHERE {where_clause}",
                params,
            ).fetchone()
            return row["cnt"]

    def delete_old_events(
        self,
        older_than: str,
        plugin_id: Optional[str] = None,
    ) -> int:
        """Delete events older than a given timestamp."""
        conditions = ["timestamp < ?"]
        params = [older_than]

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)

        where_clause = " AND ".join(conditions)

        with self._get_connection() as conn:
            cursor = conn.execute(
                f"DELETE FROM audit_events WHERE {where_clause}",
                params,
            )
            conn.commit()
            return cursor.rowcount

    def export_events(
        self,
        output_path: Path,
        query: Optional[AuditQuery] = None,
        format: str = "json",
    ) -> int:
        """
        Export events to a file.

        Args:
            output_path: Path to output file
            query: Optional query to filter events
            format: Output format ("json" or "csv")

        Returns:
            Number of events exported
        """
        if query is None:
            query = AuditQuery(limit=100000)  # Large limit for export

        events = self.query_events(query)

        if format == "json":
            data = [e.to_dict() for e in events]
            output_path.write_text(
                json.dumps(data, indent=2),
                encoding="utf-8",
            )
        elif format == "csv":
            import csv

            with open(output_path, "w", newline="", encoding="utf-8") as f:
                if events:
                    writer = csv.DictWriter(
                        f,
                        fieldnames=[
                            "event_id",
                            "event_type",
                            "category",
                            "severity",
                            "plugin_id",
                            "plugin_version",
                            "actor",
                            "timestamp",
                            "details",
                            "metadata",
                        ],
                    )
                    writer.writeheader()
                    for event in events:
                        row = event.to_dict()
                        row["details"] = json.dumps(row["details"])
                        row["metadata"] = json.dumps(row["metadata"])
                        writer.writerow(row)
        else:
            raise ValueError(f"Unsupported export format: {format}")

        logger.info(f"Exported {len(events)} audit events to {output_path}")
        return len(events)

    def close(self) -> None:
        """Close the audit logger (clears listeners)."""
        with self._lock:
            self._listeners.clear()


# =============================================================================
# Convenience Helpers
# =============================================================================


# Module-level default logger
_default_logger: Optional[AuditLogger] = None


def get_default_audit_logger(db_path: Optional[Path] = None) -> AuditLogger:
    """
    Get or create the default audit logger.

    Args:
        db_path: Path to database (uses default if not provided)

    Returns:
        The default AuditLogger instance
    """
    global _default_logger

    if _default_logger is None:
        if db_path is None:
            # Default to user data directory
            data_dir = Path(os.environ.get("VOICESTUDIO_DATA_PATH", Path.home() / ".voicestudio"))
            db_path = data_dir / "audit" / "plugin_audit.db"

        _default_logger = AuditLogger(db_path)

    return _default_logger


def log_plugin_event(
    event_type: AuditEventType,
    plugin_id: str,
    **kwargs,
) -> AuditEvent:
    """
    Log a plugin event using the default logger.

    Convenience function for quick logging without managing logger instance.

    Args:
        event_type: Type of event
        plugin_id: Plugin identifier
        **kwargs: Additional arguments passed to log_event()

    Returns:
        The created AuditEvent
    """
    audit_logger = get_default_audit_logger()
    return audit_logger.log_event(event_type, plugin_id, **kwargs)


# =============================================================================
# Pre-defined Event Helpers
# =============================================================================


def log_installation(
    audit_logger: AuditLogger,
    plugin_id: str,
    plugin_version: str,
    source: str,
    actor: Optional[str] = None,
    signature_verified: Optional[bool] = None,
) -> AuditEvent:
    """Log a plugin installation event."""
    details = {"source": source}
    if signature_verified is not None:
        details["signature_verified"] = signature_verified

    return audit_logger.log_event(
        AuditEventType.PLUGIN_INSTALLED,
        plugin_id,
        plugin_version=plugin_version,
        actor=actor,
        details=details,
    )


def log_uninstallation(
    audit_logger: AuditLogger,
    plugin_id: str,
    plugin_version: str,
    actor: Optional[str] = None,
    reason: Optional[str] = None,
) -> AuditEvent:
    """Log a plugin uninstallation event."""
    details = {}
    if reason:
        details["reason"] = reason

    return audit_logger.log_event(
        AuditEventType.PLUGIN_UNINSTALLED,
        plugin_id,
        plugin_version=plugin_version,
        actor=actor,
        details=details,
    )


def log_vulnerability_scan(
    audit_logger: AuditLogger,
    plugin_id: str,
    plugin_version: str,
    vulnerability_count: int,
    critical_count: int = 0,
    high_count: int = 0,
    scanner: Optional[str] = None,
) -> AuditEvent:
    """Log a vulnerability scan event."""
    severity = AuditSeverity.INFO
    if critical_count > 0:
        severity = AuditSeverity.CRITICAL
    elif high_count > 0:
        severity = AuditSeverity.WARNING

    return audit_logger.log_event(
        AuditEventType.VULNERABILITY_SCAN,
        plugin_id,
        plugin_version=plugin_version,
        severity=severity,
        details={
            "total_vulnerabilities": vulnerability_count,
            "critical": critical_count,
            "high": high_count,
            "scanner": scanner,
        },
    )


def log_signature_verification(
    audit_logger: AuditLogger,
    plugin_id: str,
    plugin_version: str,
    success: bool,
    key_id: Optional[str] = None,
    reason: Optional[str] = None,
) -> AuditEvent:
    """Log a signature verification event."""
    if success:
        event_type = AuditEventType.SIGNATURE_VERIFIED
        severity = AuditSeverity.INFO
    else:
        event_type = AuditEventType.SIGNATURE_FAILED
        severity = AuditSeverity.WARNING

    details = {}
    if key_id:
        details["key_id"] = key_id
    if reason:
        details["reason"] = reason

    return audit_logger.log_event(
        event_type,
        plugin_id,
        plugin_version=plugin_version,
        severity=severity,
        details=details,
    )


def log_crash(
    audit_logger: AuditLogger,
    plugin_id: str,
    plugin_version: str,
    error_message: str,
    stack_trace: Optional[str] = None,
) -> AuditEvent:
    """Log a plugin crash event."""
    details = {"error_message": error_message}
    if stack_trace:
        details["stack_trace"] = stack_trace

    return audit_logger.log_event(
        AuditEventType.PLUGIN_CRASHED,
        plugin_id,
        plugin_version=plugin_version,
        severity=AuditSeverity.ERROR,
        details=details,
    )
