"""
Phase 6 Persistence Layer.

Sprint 2: Persistence for anomaly baselines (Q-2), analytics (D-1),
and privacy metadata (C-1).

Extends the existing MetricsPersistence pattern for Phase 6 components:
- Anomaly detection baselines
- Developer analytics events
- Privacy/compliance metadata

All data stored locally using SQLite.
"""

from __future__ import annotations

import json
import logging
import sqlite3
import threading
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path
from typing import Any, Generator

logger = logging.getLogger(__name__)


class RetentionPolicy(Enum):
    """Data retention policies."""

    KEEP_ALL = "keep_all"
    KEEP_7_DAYS = "7_days"
    KEEP_30_DAYS = "30_days"
    KEEP_90_DAYS = "90_days"
    KEEP_1_YEAR = "1_year"


# =============================================================================
# Q-2: Anomaly Baseline Persistence
# =============================================================================


@dataclass
class PersistedBaseline:
    """A persisted anomaly detection baseline."""

    id: int | None
    plugin_id: str
    metric_name: str
    mean: float
    std: float
    min_val: float
    max_val: float
    q1: float
    median: float
    q3: float
    sample_count: int
    last_updated: datetime
    created_at: datetime | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "plugin_id": self.plugin_id,
            "metric_name": self.metric_name,
            "mean": self.mean,
            "std": self.std,
            "min": self.min_val,
            "max": self.max_val,
            "q1": self.q1,
            "median": self.median,
            "q3": self.q3,
            "iqr": self.q3 - self.q1,
            "sample_count": self.sample_count,
            "last_updated": self.last_updated.isoformat(),
        }


# =============================================================================
# D-1: Analytics Persistence
# =============================================================================


@dataclass
class PersistedAnalyticsEvent:
    """A persisted analytics event."""

    id: int | None
    plugin_id: str
    event_type: str
    timestamp: datetime
    user_id: str | None = None
    session_id: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "plugin_id": self.plugin_id,
            "event_type": self.event_type,
            "timestamp": self.timestamp.isoformat(),
            "user_id": self.user_id,
            "session_id": self.session_id,
            "metadata": self.metadata,
        }


@dataclass
class PersistedPluginMetrics:
    """Aggregated plugin metrics for persistence."""

    plugin_id: str
    total_installs: int = 0
    active_installs: int = 0
    total_views: int = 0
    total_uses: int = 0
    total_errors: int = 0
    avg_rating: float = 0.0
    rating_count: int = 0
    last_updated: datetime = field(default_factory=datetime.utcnow)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "total_installs": self.total_installs,
            "active_installs": self.active_installs,
            "total_views": self.total_views,
            "total_uses": self.total_uses,
            "total_errors": self.total_errors,
            "avg_rating": round(self.avg_rating, 2),
            "rating_count": self.rating_count,
            "last_updated": self.last_updated.isoformat(),
        }


# =============================================================================
# C-1: Privacy Metadata Persistence
# =============================================================================


@dataclass
class PersistedConsentRecord:
    """A persisted user consent record."""

    id: int | None
    user_id: str
    plugin_id: str
    privacy_level: str
    consented_at: datetime
    revoked_at: datetime | None = None
    categories: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "user_id": self.user_id,
            "plugin_id": self.plugin_id,
            "privacy_level": self.privacy_level,
            "consented_at": self.consented_at.isoformat(),
            "revoked_at": self.revoked_at.isoformat() if self.revoked_at else None,
            "categories": self.categories,
            "is_active": self.revoked_at is None,
        }


@dataclass
class PersistedDataDeclaration:
    """A persisted plugin data declaration."""

    id: int | None
    plugin_id: str
    categories: list[str]
    retention_days: int
    required_consent_level: str
    created_at: datetime
    updated_at: datetime | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "plugin_id": self.plugin_id,
            "categories": self.categories,
            "retention_days": self.retention_days,
            "required_consent_level": self.required_consent_level,
            "created_at": self.created_at.isoformat(),
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


# =============================================================================
# Main Persistence Class
# =============================================================================


class Phase6Persistence:
    """
    SQLite-based persistence layer for Phase 6 components.

    Thread-safe implementation supporting:
    - Anomaly detection baselines (Q-2)
    - Developer analytics (D-1)
    - Privacy/compliance metadata (C-1)
    """

    SCHEMA_VERSION = 1

    def __init__(
        self,
        db_path: Path | None = None,
        retention_policy: RetentionPolicy = RetentionPolicy.KEEP_90_DAYS,
    ):
        """
        Initialize the persistence layer.

        Args:
            db_path: Path to SQLite database file
            retention_policy: Data retention policy
        """
        if db_path is None:
            self._db_path = Path.home() / ".voicestudio" / "phase6" / "phase6.db"
        elif isinstance(db_path, str):
            self._db_path = Path(db_path)
        else:
            self._db_path = db_path
        self._db_path.parent.mkdir(parents=True, exist_ok=True)
        self._retention_policy = retention_policy
        self._lock = threading.Lock()

        self._init_database()

    @contextmanager
    def _get_connection(self) -> Generator[sqlite3.Connection, None, None]:
        """Get a database connection."""
        conn = sqlite3.connect(str(self._db_path), timeout=30.0)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()

    def _init_database(self) -> None:
        """Initialize the database schema."""
        with self._get_connection() as conn:
            cursor = conn.cursor()

            # Schema version table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS schema_version (
                    version INTEGER PRIMARY KEY
                )
            """
            )

            # Check current version
            cursor.execute("SELECT version FROM schema_version LIMIT 1")
            row = cursor.fetchone()
            current_version = row[0] if row else 0

            if current_version < self.SCHEMA_VERSION:
                self._migrate_schema(conn, current_version)

            conn.commit()

    def _migrate_schema(self, conn: sqlite3.Connection, from_version: int) -> None:
        """Migrate database schema."""
        cursor = conn.cursor()

        if from_version < 1:
            # Q-2: Anomaly baselines table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS anomaly_baselines (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin_id TEXT NOT NULL,
                    metric_name TEXT NOT NULL,
                    mean REAL NOT NULL,
                    std REAL NOT NULL,
                    min_val REAL NOT NULL,
                    max_val REAL NOT NULL,
                    q1 REAL NOT NULL,
                    median REAL NOT NULL,
                    q3 REAL NOT NULL,
                    sample_count INTEGER NOT NULL,
                    last_updated TEXT NOT NULL,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(plugin_id, metric_name)
                )
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_baselines_plugin
                ON anomaly_baselines(plugin_id)
            """
            )

            # D-1: Analytics events table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS analytics_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    user_id TEXT,
                    session_id TEXT,
                    metadata TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                )
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_events_plugin
                ON analytics_events(plugin_id)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_events_timestamp
                ON analytics_events(timestamp)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_events_type
                ON analytics_events(event_type)
            """
            )

            # D-1: Aggregated plugin metrics table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS plugin_metrics (
                    plugin_id TEXT PRIMARY KEY,
                    total_installs INTEGER DEFAULT 0,
                    active_installs INTEGER DEFAULT 0,
                    total_views INTEGER DEFAULT 0,
                    total_uses INTEGER DEFAULT 0,
                    total_errors INTEGER DEFAULT 0,
                    avg_rating REAL DEFAULT 0.0,
                    rating_count INTEGER DEFAULT 0,
                    last_updated TEXT NOT NULL
                )
            """
            )

            # C-1: Consent records table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS consent_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    plugin_id TEXT NOT NULL,
                    privacy_level TEXT NOT NULL,
                    consented_at TEXT NOT NULL,
                    revoked_at TEXT,
                    categories TEXT,
                    UNIQUE(user_id, plugin_id)
                )
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_consent_user
                ON consent_records(user_id)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_consent_plugin
                ON consent_records(plugin_id)
            """
            )

            # C-1: Plugin data declarations table
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS data_declarations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin_id TEXT UNIQUE NOT NULL,
                    categories TEXT NOT NULL,
                    retention_days INTEGER NOT NULL,
                    required_consent_level TEXT NOT NULL,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT
                )
            """
            )

            # Update schema version
            cursor.execute("DELETE FROM schema_version")
            cursor.execute(
                "INSERT INTO schema_version (version) VALUES (?)",
                (self.SCHEMA_VERSION,),
            )

        logger.info(
            f"Phase 6 database schema migrated from v{from_version} to v{self.SCHEMA_VERSION}"
        )

    # =========================================================================
    # Q-2: Anomaly Baseline Methods
    # =========================================================================

    def save_baseline(
        self,
        plugin_id: str,
        metric_name: str,
        mean: float,
        std: float,
        min_val: float,
        max_val: float,
        q1: float,
        median: float,
        q3: float,
        sample_count: int,
    ) -> int:
        """
        Save or update an anomaly baseline.

        Returns:
            ID of the saved record
        """
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO anomaly_baselines
                        (plugin_id, metric_name, mean, std, min_val, max_val,
                         q1, median, q3, sample_count, last_updated)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(plugin_id, metric_name) DO UPDATE SET
                        mean = excluded.mean,
                        std = excluded.std,
                        min_val = excluded.min_val,
                        max_val = excluded.max_val,
                        q1 = excluded.q1,
                        median = excluded.median,
                        q3 = excluded.q3,
                        sample_count = excluded.sample_count,
                        last_updated = excluded.last_updated
                    """,
                    (
                        plugin_id,
                        metric_name,
                        mean,
                        std,
                        min_val,
                        max_val,
                        q1,
                        median,
                        q3,
                        sample_count,
                        datetime.utcnow().isoformat(),
                    ),
                )
                conn.commit()
                return cursor.lastrowid or 0

    def get_baseline(
        self,
        plugin_id: str,
        metric_name: str,
    ) -> PersistedBaseline | None:
        """Get a specific baseline."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT * FROM anomaly_baselines
                WHERE plugin_id = ? AND metric_name = ?
                """,
                (plugin_id, metric_name),
            )
            row = cursor.fetchone()
            if row:
                return PersistedBaseline(
                    id=row["id"],
                    plugin_id=row["plugin_id"],
                    metric_name=row["metric_name"],
                    mean=row["mean"],
                    std=row["std"],
                    min_val=row["min_val"],
                    max_val=row["max_val"],
                    q1=row["q1"],
                    median=row["median"],
                    q3=row["q3"],
                    sample_count=row["sample_count"],
                    last_updated=datetime.fromisoformat(row["last_updated"]),
                    created_at=(
                        datetime.fromisoformat(row["created_at"]) if row["created_at"] else None
                    ),
                )
            return None

    def get_all_baselines(self, plugin_id: str) -> list[PersistedBaseline]:
        """Get all baselines for a plugin."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT * FROM anomaly_baselines WHERE plugin_id = ?",
                (plugin_id,),
            )
            baselines = []
            for row in cursor.fetchall():
                baselines.append(
                    PersistedBaseline(
                        id=row["id"],
                        plugin_id=row["plugin_id"],
                        metric_name=row["metric_name"],
                        mean=row["mean"],
                        std=row["std"],
                        min_val=row["min_val"],
                        max_val=row["max_val"],
                        q1=row["q1"],
                        median=row["median"],
                        q3=row["q3"],
                        sample_count=row["sample_count"],
                        last_updated=datetime.fromisoformat(row["last_updated"]),
                        created_at=(
                            datetime.fromisoformat(row["created_at"]) if row["created_at"] else None
                        ),
                    )
                )
            return baselines

    def delete_baseline(self, plugin_id: str, metric_name: str | None = None) -> int:
        """Delete baseline(s) for a plugin."""
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                if metric_name:
                    cursor.execute(
                        "DELETE FROM anomaly_baselines WHERE plugin_id = ? AND metric_name = ?",
                        (plugin_id, metric_name),
                    )
                else:
                    cursor.execute(
                        "DELETE FROM anomaly_baselines WHERE plugin_id = ?",
                        (plugin_id,),
                    )
                conn.commit()
                return cursor.rowcount

    # =========================================================================
    # D-1: Analytics Methods
    # =========================================================================

    def record_analytics_event(
        self,
        plugin_id: str,
        event_type: str,
        user_id: str | None = None,
        session_id: str | None = None,
        metadata: dict[str, Any] | None = None,
        timestamp: datetime | None = None,
    ) -> int:
        """Record an analytics event."""
        timestamp = timestamp or datetime.utcnow()
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO analytics_events
                        (plugin_id, event_type, timestamp, user_id, session_id, metadata)
                    VALUES (?, ?, ?, ?, ?, ?)
                    """,
                    (
                        plugin_id,
                        event_type,
                        timestamp.isoformat(),
                        user_id,
                        session_id,
                        json.dumps(metadata or {}),
                    ),
                )
                conn.commit()
                return cursor.lastrowid or 0

    def get_analytics_events(
        self,
        plugin_id: str | None = None,
        event_type: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
        limit: int = 1000,
    ) -> list[PersistedAnalyticsEvent]:
        """Query analytics events with filtering."""
        conditions = []
        params: list[Any] = []

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)

        if event_type:
            conditions.append("event_type = ?")
            params.append(event_type)

        if start_time:
            conditions.append("timestamp >= ?")
            params.append(start_time.isoformat())

        if end_time:
            conditions.append("timestamp <= ?")
            params.append(end_time.isoformat())

        where_clause = " AND ".join(conditions) if conditions else "1=1"

        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                f"""
                SELECT * FROM analytics_events
                WHERE {where_clause}
                ORDER BY timestamp DESC
                LIMIT ?
                """,
                params + [limit],
            )

            events = []
            for row in cursor.fetchall():
                events.append(
                    PersistedAnalyticsEvent(
                        id=row["id"],
                        plugin_id=row["plugin_id"],
                        event_type=row["event_type"],
                        timestamp=datetime.fromisoformat(row["timestamp"]),
                        user_id=row["user_id"],
                        session_id=row["session_id"],
                        metadata=json.loads(row["metadata"] or "{}"),
                    )
                )
            return events

    def update_plugin_metrics(
        self,
        plugin_id: str,
        **kwargs: Any,
    ) -> None:
        """Update aggregated plugin metrics."""
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()

                # Get existing or create new
                cursor.execute(
                    "SELECT * FROM plugin_metrics WHERE plugin_id = ?",
                    (plugin_id,),
                )
                row = cursor.fetchone()

                if row:
                    # Update existing
                    updates = []
                    params = []
                    for key, value in kwargs.items():
                        if key in (
                            "total_installs",
                            "active_installs",
                            "total_views",
                            "total_uses",
                            "total_errors",
                            "avg_rating",
                            "rating_count",
                        ):
                            updates.append(f"{key} = ?")
                            params.append(value)

                    if updates:
                        updates.append("last_updated = ?")
                        params.append(datetime.utcnow().isoformat())
                        params.append(plugin_id)

                        cursor.execute(
                            f"UPDATE plugin_metrics SET {', '.join(updates)} WHERE plugin_id = ?",
                            params,
                        )
                else:
                    # Insert new
                    cursor.execute(
                        """
                        INSERT INTO plugin_metrics
                            (plugin_id, total_installs, active_installs, total_views,
                             total_uses, total_errors, avg_rating, rating_count, last_updated)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        (
                            plugin_id,
                            kwargs.get("total_installs", 0),
                            kwargs.get("active_installs", 0),
                            kwargs.get("total_views", 0),
                            kwargs.get("total_uses", 0),
                            kwargs.get("total_errors", 0),
                            kwargs.get("avg_rating", 0.0),
                            kwargs.get("rating_count", 0),
                            datetime.utcnow().isoformat(),
                        ),
                    )

                conn.commit()

    def get_plugin_metrics(self, plugin_id: str) -> PersistedPluginMetrics | None:
        """Get aggregated metrics for a plugin."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT * FROM plugin_metrics WHERE plugin_id = ?",
                (plugin_id,),
            )
            row = cursor.fetchone()
            if row:
                return PersistedPluginMetrics(
                    plugin_id=row["plugin_id"],
                    total_installs=row["total_installs"],
                    active_installs=row["active_installs"],
                    total_views=row["total_views"],
                    total_uses=row["total_uses"],
                    total_errors=row["total_errors"],
                    avg_rating=row["avg_rating"],
                    rating_count=row["rating_count"],
                    last_updated=datetime.fromisoformat(row["last_updated"]),
                )
            return None

    # =========================================================================
    # C-1: Privacy/Consent Methods
    # =========================================================================

    def save_consent(
        self,
        user_id: str,
        plugin_id: str,
        privacy_level: str,
        categories: list[str] | None = None,
    ) -> int:
        """Save or update user consent."""
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO consent_records
                        (user_id, plugin_id, privacy_level, consented_at, categories)
                    VALUES (?, ?, ?, ?, ?)
                    ON CONFLICT(user_id, plugin_id) DO UPDATE SET
                        privacy_level = excluded.privacy_level,
                        consented_at = excluded.consented_at,
                        revoked_at = NULL,
                        categories = excluded.categories
                    """,
                    (
                        user_id,
                        plugin_id,
                        privacy_level,
                        datetime.utcnow().isoformat(),
                        json.dumps(categories or []),
                    ),
                )
                conn.commit()
                return cursor.lastrowid or 0

    def revoke_consent(self, user_id: str, plugin_id: str) -> bool:
        """Revoke user consent."""
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    UPDATE consent_records
                    SET revoked_at = ?
                    WHERE user_id = ? AND plugin_id = ? AND revoked_at IS NULL
                    """,
                    (datetime.utcnow().isoformat(), user_id, plugin_id),
                )
                conn.commit()
                return cursor.rowcount > 0

    def get_consent(self, user_id: str, plugin_id: str) -> PersistedConsentRecord | None:
        """Get consent record for a user-plugin pair."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT * FROM consent_records
                WHERE user_id = ? AND plugin_id = ?
                """,
                (user_id, plugin_id),
            )
            row = cursor.fetchone()
            if row:
                return PersistedConsentRecord(
                    id=row["id"],
                    user_id=row["user_id"],
                    plugin_id=row["plugin_id"],
                    privacy_level=row["privacy_level"],
                    consented_at=datetime.fromisoformat(row["consented_at"]),
                    revoked_at=(
                        datetime.fromisoformat(row["revoked_at"]) if row["revoked_at"] else None
                    ),
                    categories=json.loads(row["categories"] or "[]"),
                )
            return None

    def get_user_consents(
        self, user_id: str, active_only: bool = True
    ) -> list[PersistedConsentRecord]:
        """Get all consents for a user."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            if active_only:
                cursor.execute(
                    "SELECT * FROM consent_records WHERE user_id = ? AND revoked_at IS NULL",
                    (user_id,),
                )
            else:
                cursor.execute(
                    "SELECT * FROM consent_records WHERE user_id = ?",
                    (user_id,),
                )

            consents = []
            for row in cursor.fetchall():
                consents.append(
                    PersistedConsentRecord(
                        id=row["id"],
                        user_id=row["user_id"],
                        plugin_id=row["plugin_id"],
                        privacy_level=row["privacy_level"],
                        consented_at=datetime.fromisoformat(row["consented_at"]),
                        revoked_at=(
                            datetime.fromisoformat(row["revoked_at"]) if row["revoked_at"] else None
                        ),
                        categories=json.loads(row["categories"] or "[]"),
                    )
                )
            return consents

    def save_data_declaration(
        self,
        plugin_id: str,
        categories: list[str],
        retention_days: int,
        required_consent_level: str,
    ) -> int:
        """Save or update a plugin's data declaration."""
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO data_declarations
                        (plugin_id, categories, retention_days, required_consent_level, created_at)
                    VALUES (?, ?, ?, ?, ?)
                    ON CONFLICT(plugin_id) DO UPDATE SET
                        categories = excluded.categories,
                        retention_days = excluded.retention_days,
                        required_consent_level = excluded.required_consent_level,
                        updated_at = ?
                    """,
                    (
                        plugin_id,
                        json.dumps(categories),
                        retention_days,
                        required_consent_level,
                        datetime.utcnow().isoformat(),
                        datetime.utcnow().isoformat(),
                    ),
                )
                conn.commit()
                return cursor.lastrowid or 0

    def get_data_declaration(self, plugin_id: str) -> PersistedDataDeclaration | None:
        """Get data declaration for a plugin."""
        with self._get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT * FROM data_declarations WHERE plugin_id = ?",
                (plugin_id,),
            )
            row = cursor.fetchone()
            if row:
                return PersistedDataDeclaration(
                    id=row["id"],
                    plugin_id=row["plugin_id"],
                    categories=json.loads(row["categories"]),
                    retention_days=row["retention_days"],
                    required_consent_level=row["required_consent_level"],
                    created_at=datetime.fromisoformat(row["created_at"]),
                    updated_at=(
                        datetime.fromisoformat(row["updated_at"]) if row["updated_at"] else None
                    ),
                )
            return None

    # =========================================================================
    # Retention and Maintenance
    # =========================================================================

    def apply_retention_policy(self) -> dict[str, int]:
        """Apply retention policy and delete old data."""
        if self._retention_policy == RetentionPolicy.KEEP_ALL:
            return {"analytics_events": 0}

        days_map = {
            RetentionPolicy.KEEP_7_DAYS: 7,
            RetentionPolicy.KEEP_30_DAYS: 30,
            RetentionPolicy.KEEP_90_DAYS: 90,
            RetentionPolicy.KEEP_1_YEAR: 365,
        }

        days = days_map.get(self._retention_policy, 90)
        cutoff = datetime.utcnow() - timedelta(days=days)

        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()

                # Delete old analytics events
                cursor.execute(
                    "DELETE FROM analytics_events WHERE timestamp < ?",
                    (cutoff.isoformat(),),
                )
                events_deleted = cursor.rowcount

                conn.commit()

                logger.info(
                    f"Retention policy applied: deleted {events_deleted} events older than {days} days"
                )
                return {"analytics_events": events_deleted}

    def get_storage_stats(self) -> dict[str, Any]:
        """Get storage statistics."""
        with self._get_connection() as conn:
            cursor = conn.cursor()

            stats = {}

            # Baselines count
            cursor.execute("SELECT COUNT(*) FROM anomaly_baselines")
            stats["baselines_count"] = cursor.fetchone()[0]

            # Events count
            cursor.execute("SELECT COUNT(*) FROM analytics_events")
            stats["events_count"] = cursor.fetchone()[0]

            # Plugin metrics count
            cursor.execute("SELECT COUNT(*) FROM plugin_metrics")
            stats["metrics_count"] = cursor.fetchone()[0]

            # Consent records
            cursor.execute("SELECT COUNT(*) FROM consent_records WHERE revoked_at IS NULL")
            stats["active_consents"] = cursor.fetchone()[0]

            # Data declarations
            cursor.execute("SELECT COUNT(*) FROM data_declarations")
            stats["declarations_count"] = cursor.fetchone()[0]

            # File size
            stats["file_size_bytes"] = self._db_path.stat().st_size if self._db_path.exists() else 0
            stats["file_size_mb"] = round(stats["file_size_bytes"] / 1024 / 1024, 2)

            stats["retention_policy"] = self._retention_policy.value

            return stats

    def vacuum(self) -> None:
        """Vacuum the database to reclaim space."""
        with self._get_connection() as conn:
            conn.execute("VACUUM")
            logger.info("Phase 6 database vacuumed")


# =============================================================================
# Global Instance
# =============================================================================

_phase6_persistence: Phase6Persistence | None = None


def get_phase6_persistence(
    db_path: Path | None = None,
    retention_policy: RetentionPolicy = RetentionPolicy.KEEP_90_DAYS,
) -> Phase6Persistence:
    """
    Get or create the global Phase 6 persistence instance.

    Args:
        db_path: Optional database path
        retention_policy: Retention policy for data

    Returns:
        Phase6Persistence instance
    """
    global _phase6_persistence
    if _phase6_persistence is None:
        _phase6_persistence = Phase6Persistence(
            db_path=db_path,
            retention_policy=retention_policy,
        )
    return _phase6_persistence


def reset_phase6_persistence() -> None:
    """Reset the global persistence instance (for testing)."""
    global _phase6_persistence
    _phase6_persistence = None
