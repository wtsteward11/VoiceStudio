"""
JSON File Store - Persistent key-value storage backed by JSON files.

Provides a drop-in replacement for in-memory Dict storage used across routes.
Data is stored in individual JSON files organized by namespace/collection.

Usage:
    store = JsonFileStore("library_assets")
    store.put("asset-123", {"name": "My Audio", "type": "audio"})
    asset = store.get("asset-123")
    all_assets = store.list()
    store.delete("asset-123")
"""

from __future__ import annotations

import builtins
import json
import logging
import os
import threading
from datetime import datetime
from typing import Any

logger = logging.getLogger(__name__)

# Default data directory
_DATA_ROOT = os.environ.get(
    "VOICESTUDIO_DATA_DIR",
    os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(__file__))), "data"),
)


class JsonFileStore:
    """Thread-safe persistent JSON file store."""

    def __init__(self, collection: str, data_root: str | None = None, max_items: int = 10000):
        """
        Initialize the store.

        Args:
            collection: Name of the collection (used as subdirectory name).
            data_root: Root directory for data files. Defaults to project data/ dir.
            max_items: Maximum number of items to store. Oldest are evicted when exceeded.
        """
        self._root = os.path.join(data_root or _DATA_ROOT, "stores", collection)
        self._collection = collection
        self._max_items = max_items
        self._lock = threading.RLock()
        self._cache: dict[str, dict[str, Any]] = {}
        self._loaded = False

        # Ensure directory exists
        os.makedirs(self._root, exist_ok=True)

    def _ensure_loaded(self):
        """Lazy-load all items from disk into cache on first access."""
        if self._loaded:
            return
        with self._lock:
            if self._loaded:
                return
            try:
                for filename in os.listdir(self._root):
                    if filename.endswith(".json"):
                        filepath = os.path.join(self._root, filename)
                        try:
                            with open(filepath, encoding="utf-8") as f:
                                data = json.load(f)
                                item_id = filename[:-5]  # Remove .json
                                self._cache[item_id] = data
                        except (json.JSONDecodeError, OSError) as e:
                            logger.warning(f"Failed to load {filepath}: {e}")
                self._loaded = True
                logger.info(
                    f"JsonFileStore[{self._collection}]: Loaded {len(self._cache)} items from disk"
                )
            except OSError as e:
                logger.warning(f"Failed to list store directory {self._root}: {e}")
                self._loaded = True

    def _write_to_disk(self, item_id: str, data: dict[str, Any]):
        """Write a single item to disk atomically."""
        filepath = os.path.join(self._root, f"{item_id}.json")
        tmp_path = filepath + ".tmp"
        try:
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, default=str)
            os.replace(tmp_path, filepath)
        except OSError as e:
            logger.error(f"Failed to write {filepath}: {e}")
            # Clean up temp file
            try:
                os.remove(tmp_path)
            except OSError:
                pass  # ALLOWED: bare except - best-effort cleanup of temp file
            raise

    def _delete_from_disk(self, item_id: str):
        """Delete a single item from disk."""
        filepath = os.path.join(self._root, f"{item_id}.json")
        try:
            os.remove(filepath)
        except FileNotFoundError:
            pass  # ALLOWED: bare except - file already deleted is acceptable
        except OSError as e:
            logger.warning(f"Failed to delete {filepath}: {e}")

    def _evict_if_needed(self):
        """Evict oldest items if max_items exceeded."""
        if len(self._cache) <= self._max_items:
            return
        # Sort by _created_at or _updated_at timestamp
        items_with_time = []
        for item_id, data in self._cache.items():
            ts = data.get("_created_at", data.get("created", ""))
            items_with_time.append((item_id, ts))
        items_with_time.sort(key=lambda x: x[1])

        excess = len(self._cache) - self._max_items
        for item_id, _ in items_with_time[:excess]:
            del self._cache[item_id]
            self._delete_from_disk(item_id)
        logger.info(f"JsonFileStore[{self._collection}]: Evicted {excess} old items")

    def put(self, item_id: str, data: dict[str, Any]) -> dict[str, Any]:
        """Store or update an item."""
        self._ensure_loaded()
        with self._lock:
            # Add metadata
            now = datetime.utcnow().isoformat()
            if item_id not in self._cache:
                data["_created_at"] = now
            data["_updated_at"] = now

            self._cache[item_id] = data
            self._write_to_disk(item_id, data)
            self._evict_if_needed()
            return data

    def get(self, item_id: str) -> dict[str, Any] | None:
        """Get an item by ID. Returns None if not found."""
        self._ensure_loaded()
        with self._lock:
            return self._cache.get(item_id)

    def list(self) -> list[dict[str, Any]]:
        """List all items."""
        self._ensure_loaded()
        with self._lock:
            return list(self._cache.values())

    def list_ids(self) -> builtins.list[str]:
        """List all item IDs."""
        self._ensure_loaded()
        with self._lock:
            return list(self._cache.keys())

    def delete(self, item_id: str) -> bool:
        """Delete an item. Returns True if existed."""
        self._ensure_loaded()
        with self._lock:
            if item_id in self._cache:
                del self._cache[item_id]
                self._delete_from_disk(item_id)
                return True
            return False

    def count(self) -> int:
        """Return the number of items."""
        self._ensure_loaded()
        with self._lock:
            return len(self._cache)

    def exists(self, item_id: str) -> bool:
        """Check if an item exists."""
        self._ensure_loaded()
        with self._lock:
            return item_id in self._cache

    def search(self, predicate) -> builtins.list[dict[str, Any]]:
        """Search items matching a predicate function."""
        self._ensure_loaded()
        with self._lock:
            return [item for item in self._cache.values() if predicate(item)]

    def clear(self):
        """Delete all items."""
        self._ensure_loaded()
        with self._lock:
            for item_id in list(self._cache.keys()):
                self._delete_from_disk(item_id)
            self._cache.clear()
