"""Pure helpers for Slice 27 session artifacts.

Tasks 91–92, 98–100, 107; testable without live backend.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import pytest

META_SCHEMA = "voicestudio.slice27_session_meta.v2"
SESSION_SCHEMA = "voicestudio.slice27_whisper_cpp_session.v1"

# Stable machine-readable codes for blocked/skipped runs (Task 100 / 107).
SLICE27_STAGE_BLOCKED_REASON_CODES: dict[str, str] = {
    "health_connect": "health_connect_failed",
    "health_http": "health_http_failed",
    "engines_ready": "engines_ready_false",
    "preflight_whisper_cpp": "preflight_not_green",
    "transcribe_http": "transcribe_http_failed",
}


def blocked_reason_code_for_stage(stage: str) -> str:
    """Map integration-test ``stage`` to ``blocked_reason_code`` for session meta."""
    code = SLICE27_STAGE_BLOCKED_REASON_CODES.get(stage)
    if code is None:
        msg = f"unknown slice27 stage for blocked_reason_code: {stage!r}"
        raise ValueError(msg)
    return code


def slice27_artifact_dir_from_env() -> Path | None:
    import os

    key = "VOICESTUDIO_SLICE27_ARTIFACT_DIR"
    raw = os.environ.get(key, "").strip().strip('"')
    if not raw:
        return None
    return Path(raw)


def whisper_cpp_check_summary(
    preflight: dict[str, Any] | None,
) -> dict[str, Any]:
    """Compact view of checks.whisper_cpp for session_meta.extra."""
    if not isinstance(preflight, dict):
        return {}
    checks = preflight.get("checks")
    if not isinstance(checks, dict):
        return {}
    w = checks.get("whisper_cpp")
    if not isinstance(w, dict):
        return {
            "checks_whisper_cpp": {
                "present": False,
                "note": "checks.whisper_cpp absent or not an object",
            }
        }
    out: dict[str, Any] = {
        "ok": w.get("ok"),
        "reason": w.get("reason"),
    }
    for key in ("detail", "message", "hint"):
        if key in w and w[key] is not None:
            val = w[key]
            if isinstance(val, str) and len(val) > 500:
                val = val[:500] + "…"
            out[key] = val
    return {"checks_whisper_cpp": out}


def write_slice27_blocked_artifacts(
    out_dir: Path,
    *,
    base_url: str,
    stage: str,
    skip_reason: str,
    blocked_reason_code: str,
    preflight_http_status: int | None,
    preflight_json: dict[str, Any] | None,
    extra: dict[str, Any] | None = None,
) -> None:
    """Write preflight capture + session meta on skip/block."""
    out_dir.mkdir(parents=True, exist_ok=True)
    recorded = datetime.now(timezone.utc).isoformat()
    if preflight_json is not None:
        dest = out_dir / "slice27_preflight_capture.json"
        dest.write_text(json.dumps(preflight_json, indent=2), encoding="utf-8")
    meta: dict[str, Any] = {
        "schema": META_SCHEMA,
        "recorded_utc": recorded,
        "base_url": base_url,
        "stage": stage,
        "blocked_reason_code": blocked_reason_code,
        "outcome": "skipped",
        "skip_reason": skip_reason,
        "preflight_http_status": preflight_http_status,
    }
    if extra:
        meta["extra"] = extra
    (out_dir / "slice27_session_meta.json").write_text(
        json.dumps(meta, indent=2), encoding="utf-8"
    )


def record_slice27_skip_and_exit(
    out_dir: Path | None,
    *,
    base_url: str,
    stage: str,
    skip_reason: str,
    preflight_http_status: int | None,
    preflight_json: dict[str, Any] | None,
    extra: dict[str, Any] | None = None,
) -> None:
    """Write blocked artifacts if ``out_dir`` set, then ``pytest.skip``."""
    blocked_code = blocked_reason_code_for_stage(stage)
    if out_dir is not None:
        write_slice27_blocked_artifacts(
            out_dir,
            base_url=base_url,
            stage=stage,
            skip_reason=skip_reason,
            blocked_reason_code=blocked_code,
            preflight_http_status=preflight_http_status,
            preflight_json=preflight_json,
            extra=extra,
        )
    pytest.skip(skip_reason)


def write_slice27_pass_bundle(
    out_dir: Path,
    *,
    base_url: str,
    audio_id: str,
    transcript_payload: dict[str, Any],
) -> None:
    """Write PASS transcript bundle (same shape as before + outcome)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    bundle = {
        "schema": SESSION_SCHEMA,
        "outcome": "pass",
        "recorded_utc": datetime.now(timezone.utc).isoformat(),
        "base_url": base_url,
        "audio_id": audio_id,
        "transcribe_response": transcript_payload,
    }
    (out_dir / "slice27_transcribe_response.json").write_text(
        json.dumps(bundle, indent=2), encoding="utf-8"
    )
