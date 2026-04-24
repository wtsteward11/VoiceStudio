"""Unit tests for Slice 27 evidence helpers (Tasks 91–92, 98–100, 107–108)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from _pytest.outcomes import Skipped

from tests.integration.slice27_whisper_cpp_evidence import (
    META_SCHEMA,
    SLICE27_STAGE_BLOCKED_REASON_CODES,
    blocked_reason_code_for_stage,
    record_slice27_skip_and_exit,
    whisper_cpp_check_summary,
    write_slice27_blocked_artifacts,
    write_slice27_pass_bundle,
)


def test_whisper_cpp_check_summary_empty() -> None:
    assert whisper_cpp_check_summary(None) == {}
    assert whisper_cpp_check_summary({}) == {}
    assert whisper_cpp_check_summary({"checks": "bad"}) == {}
    missing = whisper_cpp_check_summary({"checks": {"xtts_v2": {"ok": True}}})
    assert missing["checks_whisper_cpp"]["present"] is False


def test_whisper_cpp_check_summary_extracts_ok_reason() -> None:
    preflight = {
        "checks": {
            "whisper_cpp": {
                "ok": False,
                "reason": "missing_gguf",
                "detail": "x" * 600,
            }
        }
    }
    got = whisper_cpp_check_summary(preflight)
    inner = got["checks_whisper_cpp"]
    assert inner["ok"] is False
    assert inner["reason"] == "missing_gguf"
    assert isinstance(inner["detail"], str)
    assert inner["detail"].endswith("…")


def test_blocked_reason_code_for_stage_unknown() -> None:
    with pytest.raises(ValueError, match="unknown slice27 stage"):
        blocked_reason_code_for_stage("not_a_real_stage")


def test_blocked_reason_code_for_stage_all_stages() -> None:
    for stage, expected in SLICE27_STAGE_BLOCKED_REASON_CODES.items():
        assert blocked_reason_code_for_stage(stage) == expected


def test_write_slice27_blocked_artifacts_writes_files(tmp_path: Path) -> None:
    out = tmp_path / "slice27"
    preflight = {"checks": {"whisper_cpp": {"ok": False}}}
    write_slice27_blocked_artifacts(
        out,
        base_url="http://127.0.0.1:8000",
        stage="preflight_whisper_cpp",
        skip_reason="test skip reason",
        blocked_reason_code="preflight_not_green",
        preflight_http_status=200,
        preflight_json=preflight,
        extra={"k": 1},
    )
    cap = json.loads((out / "slice27_preflight_capture.json").read_text(encoding="utf-8"))
    assert cap == preflight
    meta = json.loads((out / "slice27_session_meta.json").read_text(encoding="utf-8"))
    assert meta["schema"] == META_SCHEMA
    assert meta["stage"] == "preflight_whisper_cpp"
    assert meta["blocked_reason_code"] == "preflight_not_green"
    assert meta["skip_reason"] == "test skip reason"
    assert meta["outcome"] == "skipped"
    assert meta["extra"] == {"k": 1}


def test_slice27_blocked_session_meta_v2_required_keys_with_extra(tmp_path: Path) -> None:
    out = tmp_path / "out"
    extra = {"transcribe_http_status": 502, "transcribe_body_snippet": "body"}
    write_slice27_blocked_artifacts(
        out,
        base_url="http://127.0.0.1:7",
        stage="health_connect",
        skip_reason="connect failed",
        blocked_reason_code="health_connect_failed",
        preflight_http_status=None,
        preflight_json=None,
        extra=extra,
    )
    meta = json.loads((out / "slice27_session_meta.json").read_text(encoding="utf-8"))
    required_keys = {
        "schema",
        "recorded_utc",
        "base_url",
        "stage",
        "blocked_reason_code",
        "outcome",
        "skip_reason",
        "preflight_http_status",
        "extra",
    }
    assert set(meta.keys()) == required_keys
    assert meta["schema"] == META_SCHEMA
    assert meta["recorded_utc"]
    assert meta["base_url"] == "http://127.0.0.1:7"
    assert meta["stage"] == "health_connect"
    assert meta["blocked_reason_code"] == "health_connect_failed"
    assert meta["outcome"] == "skipped"
    assert meta["skip_reason"] == "connect failed"
    assert meta["preflight_http_status"] is None
    assert meta["extra"] == extra
    assert not (out / "slice27_preflight_capture.json").is_file()


def test_slice27_blocked_session_meta_v2_without_extra(tmp_path: Path) -> None:
    out = tmp_path / "solo"
    write_slice27_blocked_artifacts(
        out,
        base_url="http://127.0.0.1:8",
        stage="engines_ready",
        skip_reason="engines_ready=false",
        blocked_reason_code="engines_ready_false",
        preflight_http_status=200,
        preflight_json={"checks": {}},
    )
    meta = json.loads((out / "slice27_session_meta.json").read_text(encoding="utf-8"))
    assert set(meta.keys()) == {
        "schema",
        "recorded_utc",
        "base_url",
        "stage",
        "blocked_reason_code",
        "outcome",
        "skip_reason",
        "preflight_http_status",
    }
    assert meta["schema"] == META_SCHEMA
    assert meta["blocked_reason_code"] == "engines_ready_false"
    assert (out / "slice27_preflight_capture.json").is_file()


def test_record_slice27_skip_and_exit_no_out_dir_skips_with_reason() -> None:
    reason = "exact-skip-reason-for-test-98"
    with pytest.raises(Skipped, match=reason):
        record_slice27_skip_and_exit(
            None,
            base_url="http://127.0.0.1:1",
            stage="health_http",
            skip_reason=reason,
            preflight_http_status=503,
            preflight_json=None,
            extra={"health_status": 503},
        )


def test_record_slice27_skip_and_exit_writes_meta_then_skips(tmp_path: Path) -> None:
    out = tmp_path / "artifacts"
    preflight = {"checks": {}}
    skip_reason = "blocked for unit test"
    with pytest.raises(Skipped, match=skip_reason):
        record_slice27_skip_and_exit(
            out,
            base_url="http://127.0.0.1:9000",
            stage="transcribe_http",
            skip_reason=skip_reason,
            preflight_http_status=200,
            preflight_json=preflight,
            extra={"transcribe_http_status": 500, "transcribe_body_snippet": "err"},
        )
    meta = json.loads((out / "slice27_session_meta.json").read_text(encoding="utf-8"))
    assert meta["schema"] == META_SCHEMA
    assert meta["base_url"] == "http://127.0.0.1:9000"
    assert meta["stage"] == "transcribe_http"
    assert meta["blocked_reason_code"] == "transcribe_http_failed"
    assert meta["skip_reason"] == skip_reason
    assert meta["preflight_http_status"] == 200
    assert meta["extra"] == {"transcribe_http_status": 500, "transcribe_body_snippet": "err"}
    cap = json.loads((out / "slice27_preflight_capture.json").read_text(encoding="utf-8"))
    assert cap == preflight


def test_write_slice27_pass_bundle(tmp_path: Path) -> None:
    out = tmp_path / "out"
    write_slice27_pass_bundle(
        out,
        base_url="http://127.0.0.1:1",
        audio_id="aid",
        transcript_payload={"text": "hello", "engine": "whisper_cpp"},
    )
    data = json.loads((out / "slice27_transcribe_response.json").read_text(encoding="utf-8"))
    assert data["outcome"] == "pass"
    assert data["audio_id"] == "aid"
    assert data["transcribe_response"]["engine"] == "whisper_cpp"
