"""
Metrics Persistence Layer.

Phase 5D M2: SQLite-based persistence for plugin metrics with export capabilities.

Provides durable storage for plugin metrics, enabling:
- Historical analysis and trending
- Cross-session metric continuity
- Export to JSON, CSV, and Prometheus formats
- Retention policies and pruning
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
from typing import Any, Generator, Iterator

logger = logging.getLogger(__name__)


class RetentionPolicy(Enum):
    """Data retention policies."""

    KEEP_ALL = "keep_all"
    KEEP_7_DAYS = "7_days"
    KEEP_30_DAYS = "30_days"
    KEEP_90_DAYS = "90_days"
    KEEP_1_YEAR = "1_year"


@dataclass
class MetricRecord:
    """A persisted metric record."""

    id: int | None
    plugin_id: str
    metric_type: str
    value: float
    timestamp: datetime
    labels: dict[str, str] = field(default_factory=dict)
    session_id: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "plugin_id": self.plugin_id,
            "metric_type": self.metric_type,
            "value": self.value,
            "timestamp": self.timestamp.isoformat(),
            "labels": self.labels,
            "session_id": self.session_id,
        }


@dataclass
class AggregatedMetric:
    """An aggregated metric over a time window."""

    plugin_id: str
    metric_type: str
    window_start: datetime
    window_end: datetime
    count: int
    sum_value: float
    min_value: float
    max_value: float
    avg_value: float

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "metric_type": self.metric_type,
            "window_start": self.window_start.isoformat(),
            "window_end": self.window_end.isoformat(),
            "count": self.count,
            "sum": self.sum_value,
            "min": self.min_value,
            "max": self.max_value,
            "avg": round(self.avg_value, 4),
        }


class MetricsPersistence:
    """
    SQLite-based persistence layer for plugin metrics.

    Thread-safe implementation with connection pooling.
    """

    SCHEMA_VERSION = 1

    def __init__(
        self,
        db_path: Path | None = None,
        retention_policy: RetentionPolicy = RetentionPolicy.KEEP_30_DAYS,
    ):
        """
        Initialize the persistence layer.

        Args:
            db_path: Path to SQLite database file
            retention_policy: Data retention policy
        """
        self._db_path = db_path or Path.home() / ".voicestudio" / "metrics" / "metrics.db"
        self._db_path.parent.mkdir(parents=True, exist_ok=True)
        self._retention_policy = retention_policy
        self._lock = threading.Lock()
        self._session_id = datetime.now().strftime("%Y%m%d_%H%M%S")

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
            # Initial schema
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS metrics (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin_id TEXT NOT NULL,
                    metric_type TEXT NOT NULL,
                    value REAL NOT NULL,
                    timestamp TEXT NOT NULL,
                    labels TEXT,
                    session_id TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                )
            """
            )

            # Indexes for common queries
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_metrics_plugin_id
                ON metrics(plugin_id)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_metrics_timestamp
                ON metrics(timestamp)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_metrics_type
                ON metrics(metric_type)
            """
            )
            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_metrics_plugin_timestamp
                ON metrics(plugin_id, timestamp)
            """
            )

            # Aggregated metrics table for faster historical queries
            cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS metrics_hourly (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plugin_id TEXT NOT NULL,
                    metric_type TEXT NOT NULL,
                    hour_start TEXT NOT NULL,
                    count INTEGER NOT NULL,
                    sum_value REAL NOT NULL,
                    min_value REAL NOT NULL,
                    max_value REAL NOT NULL,
                    UNIQUE(plugin_id, metric_type, hour_start)
                )
            """
            )

            cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_hourly_plugin_hour
                ON metrics_hourly(plugin_id, hour_start)
            """
            )

            # Update schema version
            cursor.execute("DELETE FROM schema_version")
            cursor.execute(
                "INSERT INTO schema_version (version) VALUES (?)",
                (self.SCHEMA_VERSION,),
            )

        logger.info(f"Database schema migrated from v{from_version} to v{self.SCHEMA_VERSION}")

    def store_metric(
        self,
        plugin_id: str,
        metric_type: str,
        value: float,
        timestamp: datetime | None = None,
        labels: dict[str, str] | None = None,
    ) -> int:
        """
        Store a single metric.

        Args:
            plugin_id: Plugin identifier
            metric_type: Type of metric
            value: Metric value
            timestamp: When the metric was recorded
            labels: Additional labels/tags

        Returns:
            ID of the inserted record
        """
        timestamp = timestamp or datetime.now()
        labels = labels or {}

        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    """
                    INSERT INTO metrics (plugin_id, metric_type, value, timestamp, labels, session_id)
                    VALUES (?, ?, ?, ?, ?, ?)
                    """,
                    (
                        plugin_id,
                        metric_type,
                        value,
                        timestamp.isoformat(),
                        json.dumps(labels),
                        self._session_id,
                    ),
                )
                conn.commit()
                return cursor.lastrowid or 0

    def store_metrics_batch(
        self,
        metrics: list[tuple[str, str, float, datetime | None, dict[str, str] | None]],
    ) -> int:
        """
        Store multiple metrics in a single transaction.

        Args:
            metrics: List of (plugin_id, metric_type, value, timestamp, labels) tuples

        Returns:
            Number of records inserted
        """
        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                count = 0

                for plugin_id, metric_type, value, timestamp, labels in metrics:
                    ts = timestamp or datetime.now()
                    lbls = labels or {}
                    cursor.execute(
                        """
                        INSERT INTO metrics (plugin_id, metric_type, value, timestamp, labels, session_id)
                        VALUES (?, ?, ?, ?, ?, ?)
                        """,
                        (
                            plugin_id,
                            metric_type,
                            value,
                            ts.isoformat(),
                            json.dumps(lbls),
                            self._session_id,
                        ),
                    )
                    count += 1

                conn.commit()
                return count

    def query_metrics(
        self,
        plugin_id: str | None = None,
        metric_type: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
        limit: int = 1000,
        offset: int = 0,
    ) -> list[MetricRecord]:
        """
        Query metrics with filtering.

        Args:
            plugin_id: Filter by plugin ID
            metric_type: Filter by metric type
            start_time: Filter by start time
            end_time: Filter by end time
            limit: Maximum records to return
            offset: Number of records to skip

        Returns:
            List of metric records
        """
        conditions = []
        params: list[Any] = []

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)

        if metric_type:
            conditions.append("metric_type = ?")
            params.append(metric_type)

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
                SELECT id, plugin_id, metric_type, value, timestamp, labels, session_id
                FROM metrics
                WHERE {where_clause}
                ORDER BY timestamp DESC
                LIMIT ? OFFSET ?
                """,
                params + [limit, offset],
            )

            records = []
            for row in cursor.fetchall():
                records.append(
                    MetricRecord(
                        id=row["id"],
                        plugin_id=row["plugin_id"],
                        metric_type=row["metric_type"],
                        value=row["value"],
                        timestamp=datetime.fromisoformat(row["timestamp"]),
                        labels=json.loads(row["labels"] or "{}"),
                        session_id=row["session_id"],
                    )
                )
            return records

    def get_aggregated_metrics(
        self,
        plugin_id: str | None = None,
        metric_type: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
        window_minutes: int = 60,
    ) -> list[AggregatedMetric]:
        """
        Get aggregated metrics over time windows.

        Args:
            plugin_id: Filter by plugin ID
            metric_type: Filter by metric type
            start_time: Start of aggregation window
            end_time: End of aggregation window
            window_minutes: Size of aggregation window in minutes

        Returns:
            List of aggregated metrics
        """
        start_time = start_time or (datetime.now() - timedelta(days=7))
        end_time = end_time or datetime.now()

        conditions = ["timestamp >= ?", "timestamp <= ?"]
        params: list[Any] = [start_time.isoformat(), end_time.isoformat()]

        if plugin_id:
            conditions.append("plugin_id = ?")
            params.append(plugin_id)

        if metric_type:
            conditions.append("metric_type = ?")
            params.append(metric_type)

        where_clause = " AND ".join(conditions)

        # SQLite doesn't have great time-bucket functions, so we use string manipulation
        # to group by hour (or configurable window)
        with self._get_connection() as conn:
            cursor = conn.cursor()

            # Use strftime to bucket by window
            cursor.execute(
                f"""
                SELECT
                    plugin_id,
                    metric_type,
                    strftime('%Y-%m-%dT%H:00:00', timestamp) as window_start,
                    COUNT(*) as count,
                    SUM(value) as sum_value,
                    MIN(value) as min_value,
                    MAX(value) as max_value,
                    AVG(value) as avg_value
                FROM metrics
                WHERE {where_clause}
                GROUP BY plugin_id, metric_type, strftime('%Y-%m-%dT%H:00:00', timestamp)
                ORDER BY window_start DESC
                """,
                params,
            )

            results = []
            for row in cursor.fetchall():
                window_start = datetime.fromisoformat(row["window_start"])
                results.append(
                    AggregatedMetric(
                        plugin_id=row["plugin_id"],
                        metric_type=row["metric_type"],
                        window_start=window_start,
                        window_end=window_start + timedelta(hours=1),
                        count=row["count"],
                        sum_value=row["sum_value"],
                        min_value=row["min_value"],
                        max_value=row["max_value"],
                        avg_value=row["avg_value"],
                    )
                )
            return results

    def get_plugin_summary(
        self,
        plugin_id: str,
        since: datetime | None = None,
    ) -> dict[str, Any]:
        """
        Get a summary of metrics for a plugin.

        Args:
            plugin_id: Plugin identifier
            since: Only consider metrics after this time

        Returns:
            Summary statistics dictionary
        """
        since = since or (datetime.now() - timedelta(days=1))

        with self._get_connection() as conn:
            cursor = conn.cursor()

            cursor.execute(
                """
                SELECT
                    metric_type,
                    COUNT(*) as count,
                    SUM(value) as sum_value,
                    MIN(value) as min_value,
                    MAX(value) as max_value,
                    AVG(value) as avg_value
                FROM metrics
                WHERE plugin_id = ? AND timestamp >= ?
                GROUP BY metric_type
                """,
                (plugin_id, since.isoformat()),
            )

            summary = {
                "plugin_id": plugin_id,
                "since": since.isoformat(),
                "metrics": {},
            }

            for row in cursor.fetchall():
                summary["metrics"][row["metric_type"]] = {
                    "count": row["count"],
                    "sum": row["sum_value"],
                    "min": row["min_value"],
                    "max": row["max_value"],
                    "avg": round(row["avg_value"], 4),
                }

            return summary

    def export_json(
        self,
        output_path: Path | None = None,
        plugin_id: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
    ) -> str:
        """
        Export metrics to JSON file.

        Args:
            output_path: Path to output file (optional)
            plugin_id: Filter by plugin ID
            start_time: Start time filter
            end_time: End time filter

        Returns:
            JSON string of exported data
        """
        metrics = self.query_metrics(
            plugin_id=plugin_id,
            start_time=start_time,
            end_time=end_time,
            limit=100000,
        )

        data = {
            "exported_at": datetime.now().isoformat(),
            "filters": {
                "plugin_id": plugin_id,
                "start_time": start_time.isoformat() if start_time else None,
                "end_time": end_time.isoformat() if end_time else None,
            },
            "count": len(metrics),
            "metrics": [m.to_dict() for m in metrics],
        }

        json_str = json.dumps(data, indent=2)

        if output_path:
            output_path.write_text(json_str)
            logger.info(f"Exported {len(metrics)} metrics to {output_path}")

        return json_str

    def export_csv(
        self,
        output_path: Path | None = None,
        plugin_id: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
    ) -> str:
        """
        Export metrics to CSV format.

        Args:
            output_path: Path to output file (optional)
            plugin_id: Filter by plugin ID
            start_time: Start time filter
            end_time: End time filter

        Returns:
            CSV string
        """
        metrics = self.query_metrics(
            plugin_id=plugin_id,
            start_time=start_time,
            end_time=end_time,
            limit=100000,
        )

        lines = ["id,plugin_id,metric_type,value,timestamp,session_id,labels"]

        for m in metrics:
            labels_str = json.dumps(m.labels).replace('"', '""')
            lines.append(
                f"{m.id},{m.plugin_id},{m.metric_type},{m.value},"
                f'{m.timestamp.isoformat()},{m.session_id or ""},"{labels_str}"'
            )

        csv_str = "\n".join(lines)

        if output_path:
            output_path.write_text(csv_str)
            logger.info(f"Exported {len(metrics)} metrics to {output_path}")

        return csv_str

    def export_prometheus(
        self,
        plugin_id: str | None = None,
        start_time: datetime | None = None,
        end_time: datetime | None = None,
    ) -> str:
        """
        Export metrics in Prometheus exposition format.

        Args:
            plugin_id: Filter by plugin ID
            start_time: Start time filter
            end_time: End time filter

        Returns:
            Prometheus format string
        """
        # Get aggregated metrics for Prometheus
        aggregated = self.get_aggregated_metrics(
            plugin_id=plugin_id,
            start_time=start_time or (datetime.now() - timedelta(hours=1)),
            end_time=end_time,
        )

        lines: list[str] = []
        seen_types: set[str] = set()

        for agg in aggregated:
            metric_name = f"voicestudio_{agg.metric_type.replace('.', '_')}"

            # Add HELP and TYPE once per metric
            if metric_name not in seen_types:
                lines.append(f"# HELP {metric_name} Plugin metric: {agg.metric_type}")
                lines.append(f"# TYPE {metric_name} gauge")
                seen_types.add(metric_name)

            labels = f'plugin_id="{agg.plugin_id}"'

            # Export avg as the primary value
            lines.append(f"{metric_name}{{{labels}}} {agg.avg_value}")

            # Also export count and sum for rate calculations
            lines.append(f"{metric_name}_count{{{labels}}} {agg.count}")
            lines.append(f"{metric_name}_sum{{{labels}}} {agg.sum_value}")

        return "\n".join(lines)

    def apply_retention_policy(self) -> int:
        """
        Apply the retention policy and delete old data.

        Returns:
            Number of records deleted
        """
        if self._retention_policy == RetentionPolicy.KEEP_ALL:
            return 0

        days_map = {
            RetentionPolicy.KEEP_7_DAYS: 7,
            RetentionPolicy.KEEP_30_DAYS: 30,
            RetentionPolicy.KEEP_90_DAYS: 90,
            RetentionPolicy.KEEP_1_YEAR: 365,
        }

        days = days_map.get(self._retention_policy, 30)
        cutoff = datetime.now() - timedelta(days=days)

        with self._lock:
            with self._get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute(
                    "DELETE FROM metrics WHERE timestamp < ?",
                    (cutoff.isoformat(),),
                )
                deleted = cursor.rowcount
                conn.commit()

                logger.info(
                    f"Retention policy applied: deleted {deleted} records older than {days} days"
                )
                return deleted

    def get_storage_stats(self) -> dict[str, Any]:
        """
        Get storage statistics.

        Returns:
            Dictionary with storage stats
        """
        with self._get_connection() as conn:
            cursor = conn.cursor()

            # Total records
            cursor.execute("SELECT COUNT(*) FROM metrics")
            total_records = cursor.fetchone()[0]

            # Records by plugin
            cursor.execute(
                """
                SELECT plugin_id, COUNT(*) as count
                FROM metrics
                GROUP BY plugin_id
                ORDER BY count DESC
                """
            )
            by_plugin = {row["plugin_id"]: row["count"] for row in cursor.fetchall()}

            # Records by type
            cursor.execute(
                """
                SELECT metric_type, COUNT(*) as count
                FROM metrics
                GROUP BY metric_type
                ORDER BY count DESC
                """
            )
            by_type = {row["metric_type"]: row["count"] for row in cursor.fetchall()}

            # Date range
            cursor.execute("SELECT MIN(timestamp), MAX(timestamp) FROM metrics")
            row = cursor.fetchone()
            min_date = row[0]
            max_date = row[1]

            # File size
            file_size = self._db_path.stat().st_size if self._db_path.exists() else 0

        return {
            "total_records": total_records,
            "file_size_bytes": file_size,
            "file_size_mb": round(file_size / 1024 / 1024, 2),
            "date_range": {
                "min": min_date,
                "max": max_date,
            },
            "records_by_plugin": by_plugin,
            "records_by_type": by_type,
            "retention_policy": self._retention_policy.value,
        }

    def vacuum(self) -> None:
        """Vacuum the database to reclaim space."""
        with self._get_connection() as conn:
            conn.execute("VACUUM")
            logger.info("Database vacuumed")


# Global instance
_persistence: MetricsPersistence | None = None


def get_metrics_persistence(
    db_path: Path | None = None,
    retention_policy: RetentionPolicy = RetentionPolicy.KEEP_30_DAYS,
) -> MetricsPersistence:
    """
    Get or create the global metrics persistence instance.

    Args:
        db_path: Optional database path
        retention_policy: Retention policy for data

    Returns:
        MetricsPersistence instance
    """
    global _persistence
    if _persistence is None:
        _persistence = MetricsPersistence(
            db_path=db_path,
            retention_policy=retention_policy,
        )
    return _persistence


def reset_persistence() -> None:
    """Reset the global persistence instance (for testing)."""
    global _persistence
    _persistence = None
