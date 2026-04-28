"""
SQLite-backed persistence for the global session timeline (POST/GET /api/timeline/*).

D-001: Module-level `_timeline_state` was per-worker process; Uvicorn workers did not
share state. This store uses the same SQLite database file as the rest of the backend
so all workers read/write one authoritative row per session_id (default: ``default``).
"""

from __future__ import annotations

import json
from typing import Any

from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

DEFAULT_SESSION_ID = "default"


async def _ensure_db_connected(adapter: DatabaseAdapter) -> None:
    """
    Connect the singleton adapter if needed.

    App lifespan normally connects; tests that use TestClient without lifespan
    (e.g. security matrix) must still be able to hit /api/timeline/* without
    RuntimeError: Database not connected.
    """
    if not adapter._connected:
        await adapter.connect()


async def ensure_session_timeline_table(db: DatabaseAdapter | None = None) -> None:
    """Create session_timeline table if missing (idempotent)."""
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await adapter.execute(
        """
        CREATE TABLE IF NOT EXISTS session_timeline (
            session_id TEXT NOT NULL PRIMARY KEY,
            updated_at TEXT NOT NULL,
            timeline_json TEXT NOT NULL,
            undo_stack_json TEXT NOT NULL,
            redo_stack_json TEXT NOT NULL
        )
        """
    )


async def save_session_timeline_raw(
    timeline_dict: dict[str, Any],
    undo_stack_dicts: list[dict[str, Any]],
    redo_stack_dicts: list[dict[str, Any]],
    *,
    session_id: str = DEFAULT_SESSION_ID,
    db: DatabaseAdapter | None = None,
) -> None:
    """Persist timeline body and undo/redo stacks as JSON."""
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await ensure_session_timeline_table(adapter)
    from datetime import datetime, timezone

    updated_at = datetime.now(timezone.utc).isoformat()
    timeline_json = json.dumps(timeline_dict, default=str)
    undo_json = json.dumps(undo_stack_dicts, default=str)
    redo_json = json.dumps(redo_stack_dicts, default=str)

    await adapter.execute(
        """
        INSERT INTO session_timeline (session_id, updated_at, timeline_json, undo_stack_json, redo_stack_json)
        VALUES (?, ?, ?, ?, ?)
        ON CONFLICT(session_id) DO UPDATE SET
          updated_at = excluded.updated_at,
          timeline_json = excluded.timeline_json,
          undo_stack_json = excluded.undo_stack_json,
          redo_stack_json = excluded.redo_stack_json
        """,
        (session_id, updated_at, timeline_json, undo_json, redo_json),
    )


async def load_session_timeline_raw(
    session_id: str = DEFAULT_SESSION_ID,
    *,
    db: DatabaseAdapter | None = None,
) -> dict[str, Any] | None:
    """
    Load persisted session or None if no row exists.

    Returns dict with keys: ``timeline`` (dict), ``undo`` (list[dict]), ``redo`` (list[dict]).
    """
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await ensure_session_timeline_table(adapter)
    row = await adapter.fetch_one(
        "SELECT timeline_json, undo_stack_json, redo_stack_json FROM session_timeline WHERE session_id = ?",
        (session_id,),
    )
    if not row:
        return None
    timeline = json.loads(row["timeline_json"])
    undo = json.loads(row["undo_stack_json"])
    redo = json.loads(row["redo_stack_json"])
    if not isinstance(undo, list):
        undo = []
    if not isinstance(redo, list):
        redo = []
    return {"timeline": timeline, "undo": undo, "redo": redo}


async def delete_session_timeline(
    session_id: str = DEFAULT_SESSION_ID,
    *,
    db: DatabaseAdapter | None = None,
) -> None:
    """Remove a session row (tests)."""
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await ensure_session_timeline_table(adapter)
    await adapter.execute("DELETE FROM session_timeline WHERE session_id = ?", (session_id,))
