"""
One-shot API proof: synthesis -> library upload -> timeline session durability.
Run from repo root with backend up (VOICESTUDIO_TEST_MODE=stub recommended for consent).
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

import httpx

_REPO = Path(__file__).resolve().parents[1]
if str(_REPO) not in sys.path:
    sys.path.insert(0, str(_REPO))

BASE = os.environ.get("PROOF_API_BASE", "http://127.0.0.1:8000")
SESSION_ID = os.environ.get("PROOF_SESSION_ID", "proof-ga-dur-2026-04-28")


def main() -> int:
    out: dict = {"base": BASE, "session_id": SESSION_ID, "steps": []}

    def log(step: str, **kw: object) -> None:
        row = {"step": step, **kw}
        out["steps"].append(row)
        print(json.dumps(row, default=str))

    with httpx.Client(timeout=120.0) as client:
        r = client.get(f"{BASE}/api/profiles", params={"page": 1, "page_size": 20})
        r.raise_for_status()
        items = r.json().get("items") or []
        if not items:
            log("profiles", error="no_profiles")
            print(json.dumps(out, indent=2))
            return 2
        profile_id = items[0]["id"]
        log("profiles", profile_id=profile_id)

        synth_body = {
            "profile_id": profile_id,
            "text": "Durability proof utterance for timeline session.",
            "engine": "piper",
            "language": "en",
            "enhance_quality": False,
        }
        r = client.post(f"{BASE}/api/voice/synthesize", json=synth_body)
        if r.status_code >= 400:
            log("synthesize_piper", status=r.status_code, body=r.text[:2000])
            synth_body["engine"] = "espeak_ng"
            r = client.post(f"{BASE}/api/voice/synthesize", json=synth_body)
        r.raise_for_status()
        syn = r.json()
        audio_id = syn["audio_id"]
        duration = syn["duration"]
        audio_url = syn.get("audio_url", "")
        log(
            "synthesize",
            audio_id=audio_id,
            duration=duration,
            audio_url=audio_url,
            routed_engine=syn.get("routed_engine", ""),
        )

        from backend.services.audio_artifacts import AudioRegistry

        fs_path = AudioRegistry.get_path(audio_id)
        if not fs_path or not Path(fs_path).is_file():
            log("resolve_path", error="no_filesystem_path", audio_id=audio_id)
            print(json.dumps(out, indent=2))
            return 3
        sz = Path(fs_path).stat().st_size
        head = Path(fs_path).read_bytes()[:12]
        riff = head[:4] == b"RIFF"
        log("audio_file", path=fs_path, size_bytes=sz, riff_wav=riff)

        wav_bytes = Path(fs_path).read_bytes()
        files = {"file": ("proof.wav", wav_bytes, "audio/wav")}
        r = client.post(f"{BASE}/api/library/assets/upload", files=files)
        r.raise_for_status()
        lib = r.json()
        lib_asset_id = lib.get("id", "")
        log("library_upload", asset_id=lib_asset_id)

        r = client.get(f"{BASE}/api/timeline/state", params={"session_id": SESSION_ID})
        r.raise_for_status()
        st0 = r.json()
        rev0 = st0.get("revision", 0)
        log(
            "timeline_state_before",
            revision=rev0,
            cache_header=r.headers.get("X-Cache"),
        )

        tr_body = {"name": "Proof Track", "type": "audio"}
        r = client.post(
            f"{BASE}/api/timeline/tracks",
            json=tr_body,
            params={"session_id": SESSION_ID},
        )
        r.raise_for_status()
        track = r.json()
        track_id = track["id"]
        log("add_track", track_id=track_id, response_revision=None)

        clip_body = {
            "track_id": track_id,
            "source_path": fs_path,
            "start_time": 0.0,
            "duration": min(float(duration), 30.0),
            "name": "Proof Clip",
        }
        r = client.post(
            f"{BASE}/api/timeline/clips",
            json=clip_body,
            params={"session_id": SESSION_ID},
        )
        if r.status_code == 409:
            log("add_clip", conflict=True, body=r.text)
            print(json.dumps(out, indent=2))
            return 4
        r.raise_for_status()
        clip = r.json()
        clip_id = clip["id"]
        log(
            "add_clip",
            clip_id=clip_id,
            start=clip.get("start_time"),
            end=clip.get("end_time"),
        )

        r = client.get(f"{BASE}/api/timeline/state", params={"session_id": SESSION_ID})
        r.raise_for_status()
        st1 = r.json()
        rev1 = st1.get("revision", 0)
        log(
            "timeline_reload_1",
            revision=rev1,
            cache_header=r.headers.get("X-Cache"),
        )

        clips_found = []
        for t in st1.get("tracks", []):
            for c in t.get("clips", []):
                clips_found.append(c["id"])
        ok_reload = clip_id in clips_found
        log("reload_clip_present", clip_id_in_state=ok_reload)
        if not ok_reload:
            print(json.dumps(out, indent=2))
            return 5

        # Fresh Python process reading the same SQLite file (no shared in-memory state).
        import subprocess

        fresh_py = r"""
import asyncio, json, os, sys
from pathlib import Path

repo = Path(os.environ["PROOF_REPO_ROOT"])
os.chdir(repo)
sys.path.insert(0, str(repo))
sid = os.environ["PROOF_SESSION_ID"]

async def main() -> None:
    from backend.infrastructure.adapters.database import (
        get_database_adapter,
        reset_database_adapter_singleton,
    )
    from backend.project.timeline.session_repository import (
        ensure_session_timeline_table,
        load_session_timeline_raw,
    )
    from backend.settings import config

    reset_database_adapter_singleton()
    p = config.database.sqlite_path
    abs_p = p if os.path.isabs(p) else str((repo / p).resolve())
    conn = "sqlite:///" + Path(abs_p).as_posix()
    db = get_database_adapter(connection_string=conn)
    await db.connect()
    await ensure_session_timeline_table(db)
    raw = await load_session_timeline_raw(sid, db=db)
    await db.disconnect()
    if raw is None:
        print(json.dumps({"fresh_sqlite_load": False}))
        return
    tracks = raw["timeline"].get("tracks") or []
    clip_ids = [c["id"] for t in tracks for c in (t.get("clips") or [])]
    print(
        json.dumps(
            {
                "fresh_sqlite_load": True,
                "revision": raw.get("revision"),
                "track_count": len(tracks),
                "clip_ids": clip_ids,
            }
        )
    )

asyncio.run(main())
"""
        env = os.environ.copy()
        env["PROOF_REPO_ROOT"] = str(_REPO)
        env["PROOF_SESSION_ID"] = SESSION_ID
        proc = subprocess.run(
            [sys.executable, "-c", fresh_py],
            cwd=str(_REPO),
            env=env,
            capture_output=True,
            text=True,
            check=False,
        )
        fresh_out = (proc.stdout or "").strip() or (proc.stderr or "").strip()
        log(
            "fresh_process_sqlite_read",
            returncode=proc.returncode,
            stdout_tail=fresh_out[-2000:],
        )
        if proc.returncode != 0:
            print(json.dumps(out, indent=2))
            return 6
        try:
            fresh_data = json.loads(fresh_out.splitlines()[-1])
        except json.JSONDecodeError:
            log("fresh_process_sqlite_read", parse_error=True, raw=fresh_out[:2000])
            print(json.dumps(out, indent=2))
            return 7
        log("fresh_process_sqlite_parsed", **fresh_data)
        if not fresh_data.get("fresh_sqlite_load") or clip_id not in fresh_data.get("clip_ids", []):
            print(json.dumps(out, indent=2))
            return 8

    proof_path = _REPO / ".buildlogs" / "proof_generated_audio_durability_once.json"
    proof_path.parent.mkdir(parents=True, exist_ok=True)
    proof_path.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print("WROTE", proof_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
