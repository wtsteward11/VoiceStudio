"""
Plugin Ratings System.

Phase 5C M2: Local-first ratings and reviews storage.
Provides offline-capable rating persistence with optional sync.
"""

from __future__ import annotations

import json
import logging
import sqlite3
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Generator, Optional

logger = logging.getLogger(__name__)


@dataclass
class PluginRating:
    """Individual plugin rating entry."""

    plugin_id: str
    version: str
    rating: int  # 1-5 stars
    review: str = ""
    created_at: str = ""
    updated_at: str = ""
    rating_id: str = ""

    def __post_init__(self) -> None:
        """Set timestamps and ID if not provided."""
        now = datetime.now(timezone.utc).isoformat()
        if not self.created_at:
            self.created_at = now
        if not self.updated_at:
            self.updated_at = now
        if not self.rating_id:
            import uuid

            self.rating_id = str(uuid.uuid4())

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "rating_id": self.rating_id,
            "plugin_id": self.plugin_id,
            "version": self.version,
            "rating": self.rating,
            "review": self.review,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
        }


@dataclass
class PluginRatingStats:
    """Aggregated rating statistics for a plugin."""

    plugin_id: str
    average_rating: float = 0.0
    total_ratings: int = 0
    rating_distribution: dict[int, int] = field(default_factory=dict)
    latest_ratings: list[PluginRating] = field(default_factory=list)

    def __post_init__(self) -> None:
        """Initialize distribution."""
        if not self.rating_distribution:
            self.rating_distribution = {1: 0, 2: 0, 3: 0, 4: 0, 5: 0}

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "average_rating": round(self.average_rating, 2),
            "total_ratings": self.total_ratings,
            "rating_distribution": self.rating_distribution,
            "latest_ratings": [r.to_dict() for r in self.latest_ratings],
        }


class PluginRatingsStore:
    """
    Local-first plugin ratings storage.

    Uses SQLite for persistence with offline-first design.
    Supports optional sync with remote catalog.
    """

    def __init__(self, db_path: Optional[Path] = None):
        """
        Initialize ratings store.

        Args:
            db_path: Path to SQLite database file
        """
        if db_path is None:
            db_path = Path.home() / ".voicestudio" / "data" / "ratings.db"
        self._db_path = db_path
        self._db_path.parent.mkdir(parents=True, exist_ok=True)
        self._init_db()

    def _init_db(self) -> None:
        """Initialize database schema."""
        with self._get_connection() as conn:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS ratings (
                    rating_id TEXT PRIMARY KEY,
                    plugin_id TEXT NOT NULL,
                    version TEXT NOT NULL,
                    rating INTEGER NOT NULL CHECK (rating >= 1 AND rating <= 5),
                    review TEXT DEFAULT '',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    synced INTEGER DEFAULT 0
                )
            """
            )
            conn.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_ratings_plugin
                ON ratings(plugin_id)
            """
            )
            conn.execute(
                """
                CREATE INDEX IF NOT EXISTS idx_ratings_created
                ON ratings(created_at DESC)
            """
            )
            conn.commit()
            logger.debug(f"Initialized ratings database at {self._db_path}")

    @contextmanager
    def _get_connection(self) -> Generator[sqlite3.Connection, None, None]:
        """Get database connection as context manager."""
        conn = sqlite3.connect(str(self._db_path))
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()

    def add_rating(
        self, plugin_id: str, version: str, rating: int, review: str = ""
    ) -> PluginRating:
        """
        Add or update a rating for a plugin.

        Args:
            plugin_id: Plugin identifier
            version: Plugin version rated
            rating: Star rating (1-5)
            review: Optional review text

        Returns:
            Created or updated rating

        Raises:
            ValueError: If rating is not 1-5
        """
        if not 1 <= rating <= 5:
            raise ValueError("Rating must be between 1 and 5")

        # Check for existing rating for this plugin
        existing = self.get_my_rating(plugin_id)

        plugin_rating = PluginRating(
            plugin_id=plugin_id,
            version=version,
            rating=rating,
            review=review,
            rating_id=existing.rating_id if existing else "",
        )

        with self._get_connection() as conn:
            if existing:
                # Update existing rating
                conn.execute(
                    """
                    UPDATE ratings
                    SET rating = ?, review = ?, version = ?, updated_at = ?, synced = 0
                    WHERE rating_id = ?
                """,
                    (
                        rating,
                        review,
                        version,
                        plugin_rating.updated_at,
                        plugin_rating.rating_id,
                    ),
                )
            else:
                # Insert new rating
                conn.execute(
                    """
                    INSERT INTO ratings (rating_id, plugin_id, version, rating, review, created_at, updated_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                    (
                        plugin_rating.rating_id,
                        plugin_id,
                        version,
                        rating,
                        review,
                        plugin_rating.created_at,
                        plugin_rating.updated_at,
                    ),
                )
            conn.commit()

        logger.info(f"{'Updated' if existing else 'Added'} rating for {plugin_id}: {rating} stars")
        return plugin_rating

    def remove_rating(self, plugin_id: str) -> bool:
        """
        Remove user's rating for a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if rating was removed
        """
        with self._get_connection() as conn:
            cursor = conn.execute("DELETE FROM ratings WHERE plugin_id = ?", (plugin_id,))
            conn.commit()
            removed = cursor.rowcount > 0

        if removed:
            logger.info(f"Removed rating for {plugin_id}")
        return removed

    def get_my_rating(self, plugin_id: str) -> Optional[PluginRating]:
        """
        Get user's rating for a specific plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            Rating or None
        """
        with self._get_connection() as conn:
            row = conn.execute(
                "SELECT * FROM ratings WHERE plugin_id = ? LIMIT 1", (plugin_id,)
            ).fetchone()

            if row:
                return PluginRating(
                    rating_id=row["rating_id"],
                    plugin_id=row["plugin_id"],
                    version=row["version"],
                    rating=row["rating"],
                    review=row["review"],
                    created_at=row["created_at"],
                    updated_at=row["updated_at"],
                )
        return None

    def get_all_my_ratings(self) -> list[PluginRating]:
        """
        Get all user ratings.

        Returns:
            List of all ratings
        """
        with self._get_connection() as conn:
            rows = conn.execute("SELECT * FROM ratings ORDER BY updated_at DESC").fetchall()

            return [
                PluginRating(
                    rating_id=row["rating_id"],
                    plugin_id=row["plugin_id"],
                    version=row["version"],
                    rating=row["rating"],
                    review=row["review"],
                    created_at=row["created_at"],
                    updated_at=row["updated_at"],
                )
                for row in rows
            ]

    def get_stats(self, plugin_id: str) -> PluginRatingStats:
        """
        Get rating statistics for a plugin.

        Note: In local-first mode, this only includes the user's own rating.
        For full stats, the catalog service should merge with remote stats.

        Args:
            plugin_id: Plugin identifier

        Returns:
            Rating statistics
        """
        my_rating = self.get_my_rating(plugin_id)

        if my_rating:
            distribution = {1: 0, 2: 0, 3: 0, 4: 0, 5: 0}
            distribution[my_rating.rating] = 1

            return PluginRatingStats(
                plugin_id=plugin_id,
                average_rating=float(my_rating.rating),
                total_ratings=1,
                rating_distribution=distribution,
                latest_ratings=[my_rating],
            )

        return PluginRatingStats(plugin_id=plugin_id)

    def get_rated_plugins(self) -> list[str]:
        """
        Get list of plugin IDs that have been rated.

        Returns:
            List of plugin IDs
        """
        with self._get_connection() as conn:
            rows = conn.execute("SELECT DISTINCT plugin_id FROM ratings").fetchall()
            return [row["plugin_id"] for row in rows]

    def get_unsynced_ratings(self) -> list[PluginRating]:
        """
        Get ratings that haven't been synced to remote.

        Returns:
            List of unsynced ratings
        """
        with self._get_connection() as conn:
            rows = conn.execute(
                "SELECT * FROM ratings WHERE synced = 0 ORDER BY updated_at"
            ).fetchall()

            return [
                PluginRating(
                    rating_id=row["rating_id"],
                    plugin_id=row["plugin_id"],
                    version=row["version"],
                    rating=row["rating"],
                    review=row["review"],
                    created_at=row["created_at"],
                    updated_at=row["updated_at"],
                )
                for row in rows
            ]

    def mark_synced(self, rating_ids: list[str]) -> int:
        """
        Mark ratings as synced.

        Args:
            rating_ids: List of rating IDs to mark

        Returns:
            Number of ratings updated
        """
        if not rating_ids:
            return 0

        with self._get_connection() as conn:
            placeholders = ",".join("?" * len(rating_ids))
            cursor = conn.execute(
                f"UPDATE ratings SET synced = 1 WHERE rating_id IN ({placeholders})",
                rating_ids,
            )
            conn.commit()
            return cursor.rowcount

    def export_ratings(self) -> str:
        """
        Export all ratings as JSON.

        Returns:
            JSON string of all ratings
        """
        ratings = self.get_all_my_ratings()
        return json.dumps(
            {
                "exported_at": datetime.now(timezone.utc).isoformat(),
                "ratings": [r.to_dict() for r in ratings],
            },
            indent=2,
        )

    def import_ratings(self, json_data: str, overwrite: bool = False) -> int:
        """
        Import ratings from JSON.

        Args:
            json_data: JSON string with ratings
            overwrite: Whether to overwrite existing ratings

        Returns:
            Number of ratings imported
        """
        data = json.loads(json_data)
        imported = 0

        for rating_data in data.get("ratings", []):
            plugin_id = rating_data.get("plugin_id")
            if not plugin_id:
                continue

            existing = self.get_my_rating(plugin_id)
            if existing and not overwrite:
                continue

            self.add_rating(
                plugin_id=plugin_id,
                version=rating_data.get("version", "unknown"),
                rating=rating_data.get("rating", 3),
                review=rating_data.get("review", ""),
            )
            imported += 1

        logger.info(f"Imported {imported} ratings")
        return imported

    def clear_all(self) -> int:
        """
        Clear all ratings. Use with caution.

        Returns:
            Number of ratings removed
        """
        with self._get_connection() as conn:
            cursor = conn.execute("DELETE FROM ratings")
            conn.commit()
            count = cursor.rowcount

        logger.warning(f"Cleared {count} ratings from database")
        return count


# Module-level singleton
_ratings_store: Optional[PluginRatingsStore] = None


def get_ratings_store(db_path: Optional[Path] = None) -> PluginRatingsStore:
    """Get or create the global ratings store."""
    global _ratings_store
    if _ratings_store is None:
        _ratings_store = PluginRatingsStore(db_path)
    return _ratings_store


def rate_plugin(plugin_id: str, version: str, rating: int, review: str = "") -> PluginRating:
    """
    Rate a plugin.

    Args:
        plugin_id: Plugin identifier
        version: Plugin version
        rating: Star rating (1-5)
        review: Optional review text

    Returns:
        Created rating
    """
    return get_ratings_store().add_rating(plugin_id, version, rating, review)


def get_my_rating(plugin_id: str) -> Optional[PluginRating]:
    """Get user's rating for a plugin."""
    return get_ratings_store().get_my_rating(plugin_id)


def remove_rating(plugin_id: str) -> bool:
    """Remove rating for a plugin."""
    return get_ratings_store().remove_rating(plugin_id)
