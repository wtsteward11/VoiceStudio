"""
Quality Metrics Database — SQLite-backed persistence for quality history.

TASK-EE-002: Quality Metrics Dashboard Backend.
Replaces in-memory _quality_history with durable storage.
"""

from __future__ import annotations

import json
import logging
import sqlite3
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

_DEFAULT_DB_DIR = ".voicestudio"
_DEFAULT_DB_NAME = "quality_metrics.db"
_MAX_ENTRIES_PER_PROFILE = 1000
_MAX_TOTAL_ENTRIES = 10000


class QualityMetricsDatabase:
    """
    SQLite-backed quality metrics storage.

    Schema: quality_history (
        id TEXT PRIMARY KEY,
        profile_id TEXT NOT NULL,
        timestamp TEXT NOT NULL,
        engine TEXT NOT NULL,
        metrics TEXT NOT NULL,
        quality_score REAL NOT NULL,
        synthesis_text TEXT,
        audio_url TEXT,
        enhanced_quality INTEGER NOT NULL DEFAULT 0,
        metadata TEXT,
        created_at TEXT
    )
    """

    def __init__(self, db_path: str | None = None):
        if db_path is None:
            db_dir = Path(_DEFAULT_DB_DIR)
            db_dir.mkdir(parents=True, exist_ok=True)
            db_path = str(db_dir / _DEFAULT_DB_NAME)
        self._db_path = db_path
        self._init_db()

    def _init_db(self) -> None:
        """Create schema if not exists."""
        conn = sqlite3.connect(self._db_path)
        try:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS quality_history (
                    id TEXT PRIMARY KEY,
                    profile_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    engine TEXT NOT NULL,
                    metrics TEXT NOT NULL,
                    quality_score REAL NOT NULL,
                    synthesis_text TEXT,
                    audio_url TEXT,
                    enhanced_quality INTEGER NOT NULL DEFAULT 0,
                    metadata TEXT,
                    created_at TEXT
                )
            """
            )
            conn.execute(
                "CREATE INDEX IF NOT EXISTS idx_quality_history_profile_id ON quality_history(profile_id)"
            )
            conn.execute(
                "CREATE INDEX IF NOT EXISTS idx_quality_history_engine ON quality_history(engine)"
            )
            conn.execute(
                "CREATE INDEX IF NOT EXISTS idx_quality_history_timestamp ON quality_history(timestamp)"
            )
            conn.commit()
        finally:
            conn.close()

    def insert(self, entry: dict[str, Any]) -> str:
        """
        Insert a quality history entry. Returns the entry id.

        Entry keys: id, profile_id, timestamp, engine, metrics, quality_score,
        synthesis_text?, audio_url?, enhanced_quality?, metadata?
        """
        entry_id = entry.get("id", "")
        profile_id = entry.get("profile_id", "")
        timestamp = entry.get("timestamp", "")
        engine = entry.get("engine", "")
        metrics = entry.get("metrics", {})
        quality_score = float(entry.get("quality_score", 0.0))
        synthesis_text = entry.get("synthesis_text")
        audio_url = entry.get("audio_url")
        enhanced_quality = 1 if entry.get("enhanced_quality") else 0
        metadata = entry.get("metadata")
        created_at = datetime.utcnow().isoformat() + "Z"

        metrics_json = json.dumps(metrics) if isinstance(metrics, dict) else "{}"
        metadata_json = json.dumps(metadata) if isinstance(metadata, dict) else "null"

        conn = sqlite3.connect(self._db_path)
        try:
            conn.execute(
                """
                INSERT INTO quality_history (
                    id, profile_id, timestamp, engine, metrics, quality_score,
                    synthesis_text, audio_url, enhanced_quality, metadata, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    entry_id,
                    profile_id,
                    timestamp,
                    engine,
                    metrics_json,
                    quality_score,
                    synthesis_text,
                    audio_url,
                    enhanced_quality,
                    metadata_json,
                    created_at,
                ),
            )
            conn.commit()
            self._cleanup_if_needed(conn)
        finally:
            conn.close()
        return entry_id

    def _cleanup_if_needed(self, conn: sqlite3.Connection) -> None:
        """Enforce per-profile and total entry limits by removing oldest rows."""
        # Per-profile limit
        for row in conn.execute(
            """
            SELECT profile_id, COUNT(*) as cnt FROM quality_history
            GROUP BY profile_id HAVING cnt > ?
            """,
            (_MAX_ENTRIES_PER_PROFILE,),
        ).fetchall():
            profile_id, cnt = row[0], row[1]
            to_remove = cnt - _MAX_ENTRIES_PER_PROFILE
            cursor = conn.execute(
                """
                SELECT id FROM quality_history
                WHERE profile_id = ?
                ORDER BY timestamp ASC
                LIMIT ?
                """,
                (profile_id, to_remove),
            )
            ids_to_delete = [r[0] for r in cursor.fetchall()]
            for eid in ids_to_delete:
                conn.execute("DELETE FROM quality_history WHERE id = ?", (eid,))
        # Total limit
        total = conn.execute("SELECT COUNT(*) FROM quality_history").fetchone()[0]
        if total > _MAX_TOTAL_ENTRIES:
            to_remove = total - _MAX_TOTAL_ENTRIES
            cursor = conn.execute(
                """
                SELECT id FROM quality_history
                ORDER BY timestamp ASC
                LIMIT ?
                """,
                (to_remove,),
            )
            for (eid,) in cursor.fetchall():
                conn.execute("DELETE FROM quality_history WHERE id = ?", (eid,))
        conn.commit()

    def get_entries_by_profile(
        self,
        profile_id: str,
        limit: int | None = None,
        since: str | None = None,
        until: str | None = None,
    ) -> list[dict[str, Any]]:
        """Get quality history entries for a profile."""
        query = "SELECT id, profile_id, timestamp, engine, metrics, quality_score, synthesis_text, audio_url, enhanced_quality, metadata FROM quality_history WHERE profile_id = ?"
        params: list[Any] = [profile_id]
        if since:
            query += " AND timestamp >= ?"
            params.append(since)
        if until:
            query += " AND timestamp <= ?"
            params.append(until)
        query += " ORDER BY timestamp DESC"
        if limit is not None:
            query += " LIMIT ?"
            params.append(limit)
        conn = sqlite3.connect(self._db_path)
        try:
            rows = conn.execute(query, params).fetchall()
        finally:
            conn.close()
        return [self._row_to_entry(r) for r in rows]

    def get_all_entries_for_aggregation(self) -> list[dict[str, Any]]:
        """Get all entries (for in-memory aggregation by engine). Used by /metrics and /slo."""
        conn = sqlite3.connect(self._db_path)
        try:
            rows = conn.execute(
                "SELECT id, profile_id, timestamp, engine, metrics, quality_score, synthesis_text, audio_url, enhanced_quality, metadata FROM quality_history"
            ).fetchall()
        finally:
            conn.close()
        return [self._row_to_entry(r) for r in rows]

    def query(self, engine_id: str, since: datetime | None = None) -> list[dict[str, Any]]:
        """Query entries by engine_id and optional since datetime."""
        query = "SELECT id, profile_id, timestamp, engine, metrics, quality_score, synthesis_text, audio_url, enhanced_quality, metadata FROM quality_history WHERE engine = ?"
        params: list[Any] = [engine_id]
        if since is not None:
            query += " AND timestamp >= ?"
            params.append(since.isoformat())
        query += " ORDER BY timestamp DESC"
        conn = sqlite3.connect(self._db_path)
        try:
            rows = conn.execute(query, params).fetchall()
        finally:
            conn.close()
        return [self._row_to_entry(r) for r in rows]

    def get_engine_metrics(self, engine_id: str, period: timedelta) -> dict[str, Any]:
        """
        Get aggregated metrics for an engine over a time period.

        Returns dict with avg_quality_score, avg_mos_score, avg_similarity,
        p50_latency_ms, p95_latency_ms, synthesis_count, etc.
        """
        since = datetime.utcnow() - period
        entries = self.query(engine_id, since=since)
        if not entries:
            return {
                "engine_id": engine_id,
                "synthesis_count": 0,
                "avg_quality_score": None,
                "avg_mos_score": None,
                "avg_similarity": None,
                "p50_latency_ms": None,
                "p95_latency_ms": None,
                "period_seconds": period.total_seconds(),
            }
        quality_scores = [e["quality_score"] for e in entries]
        mos_scores = []
        similarity_scores = []
        latency_ms_list = []
        for e in entries:
            m = e.get("metrics") or {}
            if isinstance(m, dict):
                if "mos_score" in m and m["mos_score"] is not None:
                    mos_scores.append(float(m["mos_score"]))
                if "similarity" in m and m["similarity"] is not None:
                    similarity_scores.append(float(m["similarity"]))
                if "latency_ms" in m and m["latency_ms"] is not None:
                    latency_ms_list.append(float(m["latency_ms"]))
        import statistics

        avg_q = statistics.mean(quality_scores) if quality_scores else None
        avg_mos = statistics.mean(mos_scores) if mos_scores else None
        avg_sim = statistics.mean(similarity_scores) if similarity_scores else None
        p50_ms = statistics.median(latency_ms_list) if latency_ms_list else None
        p95_ms = (
            sorted(latency_ms_list)[int(len(latency_ms_list) * 0.95)]
            if len(latency_ms_list) > 1
            else (latency_ms_list[0] if latency_ms_list else None)
        )
        return {
            "engine_id": engine_id,
            "synthesis_count": len(entries),
            "avg_quality_score": round(avg_q, 4) if avg_q is not None else None,
            "avg_mos_score": round(avg_mos, 4) if avg_mos is not None else None,
            "avg_similarity": round(avg_sim, 4) if avg_sim is not None else None,
            "p50_latency_ms": round(p50_ms, 2) if p50_ms is not None else None,
            "p95_latency_ms": round(p95_ms, 2) if p95_ms is not None else None,
            "period_seconds": period.total_seconds(),
        }

    @staticmethod
    def _row_to_entry(
        row: tuple,
    ) -> dict[str, Any]:
        (
            id_,
            profile_id,
            timestamp,
            engine,
            metrics_json,
            quality_score,
            synthesis_text,
            audio_url,
            enhanced_quality,
            metadata_json,
        ) = row
        metrics = {}
        if metrics_json:
            try:
                metrics = json.loads(metrics_json)
            except (json.JSONDecodeError, TypeError) as e:
                # GAP-PY-001: Best effort - corrupted metrics JSON
                logger.debug(f"Failed to parse metrics JSON for record {id_}: {e}")
        metadata = None
        if metadata_json and metadata_json != "null":
            try:
                metadata = json.loads(metadata_json)
            except (json.JSONDecodeError, TypeError) as e:
                # GAP-PY-001: Best effort - corrupted metadata JSON
                logger.debug(f"Failed to parse metadata JSON for record {id_}: {e}")
        return {
            "id": id_,
            "profile_id": profile_id,
            "timestamp": timestamp,
            "engine": engine,
            "metrics": metrics,
            "quality_score": quality_score,
            "synthesis_text": synthesis_text,
            "audio_url": audio_url,
            "enhanced_quality": bool(enhanced_quality),
            "metadata": metadata,
        }


# Singleton for use by routes
_quality_db: QualityMetricsDatabase | None = None


def get_quality_metrics_db(db_path: str | None = None) -> QualityMetricsDatabase:
    """Get or create the quality metrics database singleton."""
    global _quality_db
    if _quality_db is None:
        _quality_db = QualityMetricsDatabase(db_path=db_path)
    return _quality_db
