"""
SQLite-backed persistence for the session timeline (POST/GET /api/timeline/*).

D-001: Module-level `_timeline_state` was per-worker process; Uvicorn workers did not
share state. This store uses the same SQLite database file as the rest of the backend
so all workers read/write one authoritative row per session_id (default: ``default``).

Hardening: rows carry a monotonic ``revision`` column for optimistic concurrency.
"""

from __future__ import annotations

import json
from typing import Any

from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

DEFAULT_SESSION_ID = "default"


class TimelineConflictError(Exception):
    """Raised when compare-and-swap fails because the persisted revision advanced."""

    def __init__(
        self,
        *,
        session_id: str,
        expected_revision: int,
        actual_revision: int | None,
        message: str | None = None,
    ) -> None:
        self.session_id = session_id
        self.expected_revision = expected_revision
        self.actual_revision = actual_revision
        super().__init__(
            message
            or (
                f"Timeline conflict for session_id={session_id!r}: "
                f"expected revision {expected_revision}, "
                f"database has {actual_revision}."
            )
        )


async def _ensure_db_connected(adapter: DatabaseAdapter) -> None:
    """
    Connect the singleton adapter if needed.

    App lifespan normally connects; tests that use TestClient without lifespan
    (e.g. security matrix) must still be able to hit /api/timeline/* without
    RuntimeError: Database not connected.
    """
    if not adapter._connected:
        await adapter.connect()


def _is_duplicate_column_error(exc: BaseException) -> bool:
    msg = str(exc).lower()
    return "duplicate column name" in msg or "duplicate column" in msg


async def ensure_session_timeline_table(db: DatabaseAdapter | None = None) -> None:
    """Create session_timeline table if missing (idempotent); migrate revision column."""
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await adapter.execute(
        """
        CREATE TABLE IF NOT EXISTS session_timeline (
            session_id TEXT NOT NULL PRIMARY KEY,
            updated_at TEXT NOT NULL,
            timeline_json TEXT NOT NULL,
            undo_stack_json TEXT NOT NULL,
            redo_stack_json TEXT NOT NULL,
            revision INTEGER NOT NULL DEFAULT 0
        )
        """
    )
    try:
        await adapter.execute(
            "ALTER TABLE session_timeline ADD COLUMN revision INTEGER NOT NULL DEFAULT 0"
        )
    except Exception as exc:
        if not _is_duplicate_column_error(exc):
            raise


async def save_session_timeline_raw(
    timeline_dict: dict[str, Any],
    undo_stack_dicts: list[dict[str, Any]],
    redo_stack_dicts: list[dict[str, Any]],
    *,
    session_id: str = DEFAULT_SESSION_ID,
    expected_revision: int = 0,
    db: DatabaseAdapter | None = None,
) -> int:
    """
    Persist timeline body and undo/redo stacks with optimistic concurrency.

    ``expected_revision`` must match the database row ``revision`` at read time.
    Use ``0`` when no row existed at hydrate (insert path).

    Returns:
        New revision after a successful save (always ``expected_revision + 1``).

    Raises:
        TimelineConflictError: Row exists but revision does not match ``expected_revision``.
    """
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await ensure_session_timeline_table(adapter)
    from datetime import datetime, timezone

    updated_at = datetime.now(timezone.utc).isoformat()
    td = dict(timeline_dict)
    td.pop("revision", None)
    timeline_json = json.dumps(td, default=str)
    undo_json = json.dumps(undo_stack_dicts, default=str)
    redo_json = json.dumps(redo_stack_dicts, default=str)

    rows_updated = await adapter.execute(
        """
        UPDATE session_timeline SET
          updated_at = ?,
          timeline_json = ?,
          undo_stack_json = ?,
          redo_stack_json = ?,
          revision = revision + 1
        WHERE session_id = ? AND revision = ?
        """,
        (updated_at, timeline_json, undo_json, redo_json, session_id, expected_revision),
    )
    if rows_updated > 0:
        return expected_revision + 1

    row = await adapter.fetch_one(
        "SELECT revision FROM session_timeline WHERE session_id = ?",
        (session_id,),
    )
    if row is None:
        if expected_revision != 0:
            raise TimelineConflictError(
                session_id=session_id,
                expected_revision=expected_revision,
                actual_revision=None,
                message=(
                    f"No timeline row for session_id={session_id!r} but expected_revision "
                    f"was {expected_revision} (non-zero)."
                ),
            )
        try:
            await adapter.execute(
                """
                INSERT INTO session_timeline (
                  session_id, updated_at, timeline_json, undo_stack_json, redo_stack_json, revision
                )
                VALUES (?, ?, ?, ?, ?, 1)
                """,
                (session_id, updated_at, timeline_json, undo_json, redo_json),
            )
        except Exception as exc:
            msg = str(exc).lower()
            if "unique" not in msg and "integrity" not in msg:
                raise
            raced = await adapter.fetch_one(
                "SELECT revision FROM session_timeline WHERE session_id = ?",
                (session_id,),
            )
            actual_r = int(raced["revision"]) if raced else None
            raise TimelineConflictError(
                session_id=session_id,
                expected_revision=expected_revision,
                actual_revision=actual_r,
            ) from exc
        return 1

    actual = int(row["revision"])
    raise TimelineConflictError(
        session_id=session_id,
        expected_revision=expected_revision,
        actual_revision=actual,
    )


async def load_session_timeline_raw(
    session_id: str = DEFAULT_SESSION_ID,
    *,
    db: DatabaseAdapter | None = None,
) -> dict[str, Any] | None:
    """
    Load persisted session or None if no row exists.

    Returns dict with keys: ``timeline`` (dict), ``undo`` (list[dict]),
    ``redo`` (list[dict]), ``revision`` (int).
    """
    adapter = db or get_database_adapter()
    await _ensure_db_connected(adapter)
    await ensure_session_timeline_table(adapter)
    row = await adapter.fetch_one(
        """
        SELECT timeline_json, undo_stack_json, redo_stack_json, revision
        FROM session_timeline WHERE session_id = ?
        """,
        (session_id,),
    )
    if not row:
        return None
    timeline = json.loads(row["timeline_json"])
    undo = json.loads(row["undo_stack_json"])
    redo = json.loads(row["redo_stack_json"])
    rev_raw = row.get("revision")
    revision = int(rev_raw) if rev_raw is not None else 0
    if not isinstance(undo, list):
        undo = []
    if not isinstance(redo, list):
        redo = []
    return {"timeline": timeline, "undo": undo, "redo": redo, "revision": revision}


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
