#!/usr/bin/env python3
"""Verify generated-audio proof durability across a backend restart."""
from __future__ import annotations

import argparse
import json
import shlex
import subprocess
import sys
import time
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.proof.audio_forensics import is_json_error_body
from scripts.proof.verify_generated_audio_replay import validate_audio_bytes, validate_audio_path


def _load_proof(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("proof JSON must be an object")
    return data


def _section(data: dict[str, Any], name: str) -> dict[str, Any]:
    value = data.get(name)
    return value if isinstance(value, dict) else {}


def _text(value: Any) -> str:
    return str(value).strip() if value is not None else ""


def _full_url(base_url: str, path_or_url: str) -> str:
    if path_or_url.startswith(("http://", "https://")):
        return path_or_url
    base = base_url.rstrip("/")
    path = path_or_url if path_or_url.startswith("/") else f"/{path_or_url}"
    return f"{base}{path}"


def _timeline_state_url(base_url: str, session_id: str) -> str:
    if session_id:
        return _full_url(base_url, f"/api/timeline/state?{urlencode({'session_id': session_id})}")
    return _full_url(base_url, "/api/timeline/state")


def _timeline_contains(state: Any, track_id: str, clip_id: str) -> bool:
    if not isinstance(state, dict):
        return False
    for track in state.get("tracks", []):
        if not isinstance(track, dict) or _text(track.get("id")) != track_id:
            continue
        return any(isinstance(clip, dict) and _text(clip.get("id")) == clip_id for clip in track.get("clips", []))
    return False


def _read_url(url: str, timeout_seconds: float) -> bytes:
    with urlopen(url, timeout=timeout_seconds) as response:
        return response.read()


def _read_json_url(url: str, timeout_seconds: float) -> dict[str, Any]:
    data = _read_url(url, timeout_seconds)
    parsed = json.loads(data.decode("utf-8", errors="replace"))
    if not isinstance(parsed, dict):
        raise ValueError("HTTP JSON response must be an object")
    return parsed


def _proof_targets(proof: dict[str, Any]) -> dict[str, str]:
    synthesis = _section(proof, "synthesis")
    generated = _section(proof, "generated_audio")
    library = _section(proof, "library")
    timeline = _section(proof, "timeline")
    project = _section(proof, "project")
    export = _section(proof, "export")

    return {
        "audio_url": _text(synthesis.get("audio_url") or generated.get("audio_url")),
        "library_asset_id": _text(generated.get("library_asset_id") or library.get("asset_id")),
        "timeline_track_id": _text(generated.get("timeline_track_id") or timeline.get("track_id")),
        "timeline_clip_id": _text(generated.get("timeline_clip_id") or timeline.get("clip_id")),
        "session_id": _text(project.get("session_id") or timeline.get("session_id")),
        "export_path": _text(export.get("path")) if export.get("claimed") is True else "",
    }


def _default_result() -> dict[str, Any]:
    return {
        "status": "fail",
        "restart_performed": False,
        "readiness_restored": False,
        "audio_reloaded": False,
        "library_asset_reloaded": False,
        "timeline_clip_reloaded": False,
        "export_replay_validated": False,
        "blockers": [],
        "evidence": [],
    }


def _wait_for_readiness(base_url: str, timeout_seconds: float) -> tuple[bool, list[str]]:
    deadline = time.monotonic() + timeout_seconds
    evidence: list[str] = []
    last_error = ""
    while time.monotonic() <= deadline:
        try:
            payload = _read_json_url(_full_url(base_url, "/api/health/readiness"), min(timeout_seconds, 5.0))
            if payload.get("ready") is True or payload.get("status") in ("ready", "ok", "healthy"):
                evidence.append("backend readiness restored")
                return True, evidence
            last_error = f"readiness payload not ready: {payload}"
        except (HTTPError, URLError, TimeoutError, OSError, json.JSONDecodeError, ValueError) as exc:
            last_error = str(exc)
        time.sleep(0.25)
    evidence.append(f"readiness not restored: {last_error or 'timeout'}")
    return False, evidence


def verify_restart_durability(
    proof_json: Path,
    *,
    restart_command: str | None,
    base_url: str,
    timeout_seconds: float,
) -> dict[str, Any]:
    result = _default_result()
    try:
        proof = _load_proof(proof_json)
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        result["blockers"].append(f"failed to load proof JSON: {exc}")
        return result

    targets = _proof_targets(proof)
    if not restart_command:
        result["status"] = "blocked"
        result["blockers"].append("restart command not supplied; restart durability is a non-claim")
        return result

    cp = subprocess.run(
        shlex.split(restart_command),
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if cp.returncode != 0:
        result["blockers"].append(f"restart command failed with exit {cp.returncode}")
        if cp.stderr.strip():
            result["evidence"].append(cp.stderr.strip()[:500])
        return result

    result["restart_performed"] = True
    result["evidence"].append("restart command succeeded")
    readiness_ok, readiness_evidence = _wait_for_readiness(base_url, timeout_seconds)
    result["evidence"].extend(readiness_evidence)
    result["readiness_restored"] = readiness_ok
    if not readiness_ok:
        result["blockers"].append("backend readiness was not restored after restart")
        return result

    audio_url = targets["audio_url"]
    if not audio_url:
        result["blockers"].append("proof JSON lacks synthesis.audio_url")
        return result
    try:
        data = _read_url(_full_url(base_url, audio_url), timeout_seconds)
        if is_json_error_body(data):
            result["blockers"].append("audio reload returned JSON body")
            return result
        replay = validate_audio_bytes(data, {"kind": "audio_url", "url": audio_url})
        result["audio_reloaded"] = replay["status"] == "pass"
        if replay["status"] != "pass":
            result["blockers"].extend(f"audio reload: {blocker}" for blocker in replay["blockers"])
            return result
    except (HTTPError, URLError, TimeoutError, OSError) as exc:
        result["blockers"].append(f"audio reload failed: {exc}")
        return result

    asset_id = targets["library_asset_id"]
    if asset_id:
        try:
            _read_json_url(_full_url(base_url, f"/api/library/assets/{asset_id}"), timeout_seconds)
            result["library_asset_reloaded"] = True
        except (HTTPError, URLError, TimeoutError, OSError, json.JSONDecodeError, ValueError) as exc:
            result["blockers"].append(f"library asset reload failed: {exc}")
            return result

    track_id = targets["timeline_track_id"]
    clip_id = targets["timeline_clip_id"]
    if track_id and clip_id:
        try:
            state = _read_json_url(_timeline_state_url(base_url, targets["session_id"]), timeout_seconds)
        except (HTTPError, URLError, TimeoutError, OSError, json.JSONDecodeError, ValueError) as exc:
            result["blockers"].append(f"timeline reload failed: {exc}")
            return result
        result["timeline_clip_reloaded"] = _timeline_contains(state, track_id, clip_id)
        if not result["timeline_clip_reloaded"]:
            result["blockers"].append("timeline reload did not find expected track/clip")
            return result

    export_path = targets["export_path"]
    if export_path:
        replay = validate_audio_path(Path(export_path))
        result["export_replay_validated"] = replay["status"] == "pass"
        if replay["status"] != "pass":
            result["blockers"].extend(f"export replay: {blocker}" for blocker in replay["blockers"])
            return result

    result["status"] = "pass"
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--proof-json", type=Path, required=True)
    parser.add_argument("--restart-command", default=None)
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--timeout-seconds", type=float, default=60.0)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    result = verify_restart_durability(
        args.proof_json,
        restart_command=args.restart_command,
        base_url=args.base_url,
        timeout_seconds=args.timeout_seconds,
    )
    if args.json:
        print(json.dumps(result, indent=2, sort_keys=True))
    else:
        print(f"status={result['status']}")
        for blocker in result["blockers"]:
            print(f"- {blocker}")
    return 0 if result["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
