"""Drop-in persistent replacement for module-level dicts.

Wraps an in-memory dict with SQLite persistence so data survives backend
restarts. Designed as a minimal-change migration path: replace
``_items: dict[str, dict] = {}`` with ``_items = PersistentStore("items")``.

The store is dict-like (supports [], .get, .pop, in, del, len, iter) and
handles serialization to/from SQLite automatically.

Thread-safety: all mutations are protected by an internal lock.
"""

from __future__ import annotations

import json
import logging
import os
import sqlite3
import threading
from pathlib import Path
from typing import Any, Iterator

logger = logging.getLogger(__name__)

_DEFAULT_DB_NAME = "voicestudio_state.db"


def _get_db_path() -> str:
    env_dir = os.environ.get("VOICESTUDIO_DATA_DIR")
    if env_dir:
        db_dir = Path(env_dir)
    else:
        from backend.config.path_config import get_path
        db_dir = get_path("data")
    db_dir.mkdir(parents=True, exist_ok=True)
    return str(db_dir / _DEFAULT_DB_NAME)


class PersistentStore:
    """A dict-like store backed by SQLite for persistence across restarts.

    Usage::

        _items = PersistentStore("items")

        # Write (same as dict)
        _items["abc"] = {"name": "Test", "status": "running"}

        # Read (same as dict)
        item = _items.get("abc")
        if "abc" in _items: ...

        # Delete
        del _items["abc"]

        # Iterate
        for key in _items: ...
        for key, val in _items.items(): ...
    """

    def __init__(
        self,
        table: str,
        db_path: str | None = None,
        preload: bool = True,
    ):
        self._table = table.replace("-", "_")
        self._db_path = db_path or _get_db_path()
        self._lock = threading.Lock()
        self._cache: dict[str, Any] = {}
        self._conn: sqlite3.Connection | None = None
        self._init_table()
        if preload:
            self._load_all()

    def _get_conn(self) -> sqlite3.Connection:
        if self._conn is None:
            self._conn = sqlite3.connect(
                self._db_path, timeout=10, check_same_thread=False
            )
            self._conn.execute("PRAGMA journal_mode=WAL")
        return self._conn

    def _init_table(self) -> None:
        conn = self._get_conn()
        conn.execute(
            f"CREATE TABLE IF NOT EXISTS [{self._table}] "
            f"(key TEXT PRIMARY KEY, value TEXT NOT NULL)"
        )
        conn.commit()

    def _load_all(self) -> None:
        with self._lock:
            conn = self._get_conn()
            rows = conn.execute(
                f"SELECT key, value FROM [{self._table}]"
            ).fetchall()
            for key, value_json in rows:
                try:
                    self._cache[key] = json.loads(value_json)
                except (json.JSONDecodeError, TypeError):
                    self._cache[key] = value_json

    def __setitem__(self, key: str, value: Any) -> None:
        value_json = json.dumps(value, default=str)
        with self._lock:
            self._cache[key] = value
            conn = self._get_conn()
            conn.execute(
                f"INSERT OR REPLACE INTO [{self._table}] (key, value) VALUES (?, ?)",
                (key, value_json),
            )
            conn.commit()

    def __getitem__(self, key: str) -> Any:
        with self._lock:
            return self._cache[key]

    def __delitem__(self, key: str) -> None:
        with self._lock:
            del self._cache[key]
            conn = self._get_conn()
            conn.execute(
                f"DELETE FROM [{self._table}] WHERE key = ?", (key,)
            )
            conn.commit()

    def __contains__(self, key: object) -> bool:
        with self._lock:
            return key in self._cache

    def __len__(self) -> int:
        with self._lock:
            return len(self._cache)

    def __iter__(self) -> Iterator[str]:
        with self._lock:
            return iter(list(self._cache.keys()))

    def __bool__(self) -> bool:
        return len(self) > 0

    def get(self, key: str, default: Any = None) -> Any:
        with self._lock:
            return self._cache.get(key, default)

    def pop(self, key: str, *args: Any) -> Any:
        with self._lock:
            result = self._cache.pop(key, *args)
            conn = self._get_conn()
            conn.execute(
                f"DELETE FROM [{self._table}] WHERE key = ?", (key,)
            )
            conn.commit()
            return result

    def keys(self):
        with self._lock:
            return list(self._cache.keys())

    def values(self):
        with self._lock:
            return list(self._cache.values())

    def items(self):
        with self._lock:
            return list(self._cache.items())

    def update(self, data: dict[str, Any]) -> None:
        for key, value in data.items():
            self[key] = value

    def clear(self) -> None:
        with self._lock:
            self._cache.clear()
            conn = self._get_conn()
            conn.execute(f"DELETE FROM [{self._table}]")
            conn.commit()

    def setdefault(self, key: str, default: Any = None) -> Any:
        with self._lock:
            if key in self._cache:
                return self._cache[key]
        self[key] = default
        return default

    def to_dict(self) -> dict[str, Any]:
        """Return a plain dict snapshot (for serialization)."""
        with self._lock:
            return dict(self._cache)
