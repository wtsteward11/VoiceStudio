"""Timeline session repository: shared SQLite across logical instances (D-001)."""

from __future__ import annotations

import asyncio
import tempfile
from pathlib import Path

import pytest

from backend.api.routes.timeline import TimelineState
from backend.infrastructure.adapters.database import DatabaseAdapter


@pytest.fixture
def shared_sqlite_path():
    fd, path = tempfile.mkstemp(suffix=".session_timeline.db")
    import os

    os.close(fd)
    p = Path(path)
    yield p
    try:
        p.unlink(missing_ok=True)
    except OSError:
        pass


@pytest.mark.asyncio
async def test_save_on_one_connection_load_on_another(shared_sqlite_path: Path) -> None:
    from backend.project.timeline.session_repository import (
        DEFAULT_SESSION_ID,
        ensure_session_timeline_table,
        load_session_timeline_raw,
        save_session_timeline_raw,
    )

    cs = f"sqlite:///{shared_sqlite_path.resolve().as_posix()}"
    db_a = DatabaseAdapter(connection_string=cs)
    db_b = DatabaseAdapter(connection_string=cs)
    assert await db_a.connect()
    assert await db_b.connect()
    await ensure_session_timeline_table(db_a)

    state = TimelineState(name="Shared", sample_rate=44100)
    await save_session_timeline_raw(
        state.model_dump(mode="json"),
        [],
        [],
        session_id=DEFAULT_SESSION_ID,
        db=db_a,
    )

    raw = await load_session_timeline_raw(DEFAULT_SESSION_ID, db=db_b)
    assert raw is not None
    loaded = TimelineState.model_validate(raw["timeline"])
    assert loaded.name == "Shared"
    assert loaded.sample_rate == 44100

    await db_a.disconnect()
    await db_b.disconnect()


@pytest.mark.asyncio
async def test_session_isolation_keys(shared_sqlite_path: Path) -> None:
    from backend.project.timeline.session_repository import (
        ensure_session_timeline_table,
        load_session_timeline_raw,
        save_session_timeline_raw,
    )

    cs = f"sqlite:///{shared_sqlite_path.resolve().as_posix()}"
    db = DatabaseAdapter(connection_string=cs)
    await db.connect()
    await ensure_session_timeline_table(db)

    a = TimelineState(name="DefaultSession")
    b = TimelineState(name="OtherSession")

    await save_session_timeline_raw(
        a.model_dump(mode="json"), [], [], session_id="default", db=db
    )
    await save_session_timeline_raw(
        b.model_dump(mode="json"), [], [], session_id="other", db=db
    )

    d = await load_session_timeline_raw("default", db=db)
    o = await load_session_timeline_raw("other", db=db)
    assert d is not None and o is not None
    assert TimelineState.model_validate(d["timeline"]).name == "DefaultSession"
    assert TimelineState.model_validate(o["timeline"]).name == "OtherSession"

    await db.disconnect()


def test_route_client_sees_persisted_track_after_second_client(tmp_path):
    """Simulate two workers: two TestClients share one SQLite file."""
    from fastapi import FastAPI
    from fastapi.testclient import TestClient

    from backend.api.routes.timeline import router
    from backend.infrastructure.adapters.database import (
        get_database_adapter,
        reset_database_adapter_singleton,
    )
    from backend.project.timeline.session_repository import (
        DEFAULT_SESSION_ID,
        delete_session_timeline,
        ensure_session_timeline_table,
    )

    db_file = tmp_path / "route_twice.db"

    def make_client(*, clear_session: bool) -> TestClient:
        reset_database_adapter_singleton()
        db = get_database_adapter(connection_string=f"sqlite:///{db_file.resolve().as_posix()}")

        async def setup():
            await db.connect()
            await ensure_session_timeline_table(db)
            if clear_session:
                await delete_session_timeline(DEFAULT_SESSION_ID, db=db)

        asyncio.run(setup())
        app = FastAPI()
        app.include_router(router)
        return TestClient(app)

    c1 = make_client(clear_session=True)
    r = c1.post("/api/timeline/tracks", json={"name": "T1", "type": "audio"})
    assert r.status_code == 200
    tid = r.json()["id"]

    c2 = make_client(clear_session=False)
    r2 = c2.get("/api/timeline/state")
    assert r2.status_code == 200
    tracks = r2.json()["tracks"]
    assert len(tracks) == 1
    assert tracks[0]["id"] == tid
