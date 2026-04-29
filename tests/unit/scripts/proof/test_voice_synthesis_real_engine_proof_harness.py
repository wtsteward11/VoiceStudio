"""Unit tests for scripts/proof/run_voice_synthesis_real_engine_proof.py."""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path
from unittest.mock import patch

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.ci.check_voice_synthesis_proof_boundary import validate_report
from scripts.proof.run_voice_synthesis_real_engine_proof import (
    ProofApiRoutes,
    ProofOptions,
    ProofResult,
    _proof_json,
    dry_run_write_reports,
    render_markdown_report,
    run_real_engine_flow,
)


@pytest.fixture
def proof_opts(tmp_path: Path) -> ProofOptions:
    return ProofOptions(
        base_url="http://127.0.0.1:9",
        engine="xtts_v2",
        profile_id="p1",
        session_id="default",
        output_dir=tmp_path / "out",
        json_output=None,
        markdown_output=None,
        require_real=False,
        dry_run_fixtures=False,
        timeout_seconds=5.0,
    )


class FakeHttp:
    def __init__(self, handlers: dict[str, tuple[int, bytes]]) -> None:
        self.handlers = handlers
        self.calls: list[str] = []

    def _match(self, url: str) -> tuple[int, bytes] | None:
        for key in sorted(self.handlers, key=len, reverse=True):
            if key in url:
                return self.handlers[key]
        return None

    def get(self, url: str, timeout: float) -> tuple[int, bytes]:
        self.calls.append(url)
        hit = self._match(url)
        if hit is not None:
            return hit
        return (404, b"not found")

    def post_json(self, url: str, payload: dict, timeout: float) -> tuple[int, bytes]:
        self.calls.append(url)
        hit = self._match(url)
        if hit is not None:
            return hit
        return (404, b"{}")

    def post_multipart_file(
        self, url: str, field_name: str, filename: str, file_bytes: bytes, timeout: float
    ) -> tuple[int, bytes]:
        self.calls.append(url)
        hit = self._match(url)
        if hit is not None:
            return hit
        return (500, b"err")


def test_dry_run_fixtures_validate(tmp_path: Path) -> None:
    stub_p, real_p = dry_run_write_reports(tmp_path)
    assert validate_report(stub_p) == []
    assert validate_report(real_p) == []


def test_render_real_markdown_passes_validator(proof_opts: ProofOptions) -> None:
    result = ProofResult(
        "REAL_ENGINE",
        [],
        {
            "routed_engine": "xtts_v2",
            "artifact_size_bytes": 186956,
            "artifact_size_kib": 182.6,
            "library": {"audio_id": "a1"},
            "timeline": {
                "revision_before": 1,
                "revision_after": 2,
                "clip_id": "c1",
            },
        },
    )
    md = render_markdown_report(result, proof_opts)
    p = proof_opts.output_dir / "HARNESS_REAL.md"
    proof_opts.output_dir.mkdir(parents=True, exist_ok=True)
    p.write_text(md, encoding="utf-8")
    assert validate_report(p) == []


def test_stub_env_writes_valid_markdown(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("VOICESTUDIO_TEST_MODE", "1")
    out = tmp_path / "stub.md"
    from scripts.proof import run_voice_synthesis_real_engine_proof as mod

    monkeypatch.setattr(mod, "_stub_test_mode", lambda: True)
    result = ProofResult("STUB_ENGINE", [], {})
    md = mod.render_markdown_report(result, ProofOptions(
        base_url="http://x",
        engine=None,
        profile_id=None,
        session_id="default",
        output_dir=tmp_path,
        json_output=None,
        markdown_output=out,
        require_real=False,
        dry_run_fixtures=False,
        timeout_seconds=1.0,
    ))
    out.write_text(md, encoding="utf-8")
    assert validate_report(out) == []


def test_require_real_stub_exit_code(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("VOICESTUDIO_TEST_MODE", "1")
    from scripts.proof import run_voice_synthesis_real_engine_proof as mod

    with patch.object(mod, "_stub_test_mode", return_value=True):
        rc = mod.main([
            "--output-dir",
            str(tmp_path),
            "--require-real",
        ])
    assert rc == 1


def test_health_failure_unknown(proof_opts: ProofOptions) -> None:
    http = FakeHttp({})
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("health" in b.lower() for b in r.blockers)


def test_readiness_503_unknown(proof_opts: ProofOptions) -> None:
    http = FakeHttp({
        "/api/health/": (200, b"{}"),
        "/api/health/readiness": (503, b'{"detail":"no"}'),
    })
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("readiness" in b.lower() for b in r.blockers)


def test_no_profile_unknown(tmp_path: Path) -> None:
    opts = ProofOptions(
        base_url="http://127.0.0.1:9",
        engine="xtts_v2",
        profile_id=None,
        session_id="default",
        output_dir=tmp_path / "out",
        json_output=None,
        markdown_output=None,
        require_real=False,
        dry_run_fixtures=False,
        timeout_seconds=5.0,
    )
    http = FakeHttp({
        "/api/health/": (200, b"{}"),
        "/api/health/readiness": (200, json.dumps({"ready": True}).encode()),
        "/api/profiles": (200, json.dumps({"items": []}).encode()),
    })
    r = run_real_engine_flow(http, opts)
    assert r.classification == "UNKNOWN"
    assert any("profile" in b.lower() for b in r.blockers)


def test_stub_routed_engine_unknown(proof_opts: ProofOptions) -> None:
    syn = {
        "audio_id": "aid",
        "routed_engine": "stub",
        "duration": 1.0,
        "quality_score": 0.9,
    }
    http = FakeHttp({
        "/api/health/": (200, b"{}"),
        "/api/health/readiness": (200, json.dumps({"ready": True}).encode()),
        "/api/profiles": (200, json.dumps({"items": [{"id": "p1", "reference_audio_bound": True}]}).encode()),
        "/api/voice/synthesize": (200, json.dumps(syn).encode()),
    })
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("routed_engine" in b.lower() for b in r.blockers)


def _good_chain(
    audio_body: bytes,
    *,
    upload_status: int = 201,
    clip_status: int = 200,
) -> FakeHttp:
    syn = {
        "audio_id": "aid1",
        "routed_engine": "xtts_v2",
        "duration": 1.0,
        "quality_score": 0.9,
    }
    asset = {"id": "lib1", "audio_id": "aid1", "path": "/tmp/harness.wav"}
    track = {"id": "trk1"}
    clip = {"id": "clip1"}
    state0 = {"revision": 0, "tracks": []}
    state1 = {"revision": 2, "tracks": [{"id": "trk1", "clips": [{"id": "clip1"}]}]}
    return FakeHttp({
        "/api/health/": (200, b"{}"),
        "/api/health/readiness": (200, json.dumps({"ready": True}).encode()),
        "/api/profiles": (200, json.dumps({"items": [{"id": "p1", "reference_audio_bound": True}]}).encode()),
        "/api/voice/synthesize": (200, json.dumps(syn).encode()),
        "/api/voice/audio/aid1": (200, audio_body),
        "/api/library/assets/upload": (upload_status, json.dumps(asset).encode()),
        "/api/timeline/state": (200, json.dumps(state1).encode()),
        "/api/timeline/create": (200, json.dumps(state0).encode()),
        "/api/timeline/tracks": (200, json.dumps(track).encode()),
        "/api/timeline/clips": (clip_status, json.dumps(clip).encode() if clip_status == 200 else b'{"detail":"bad"}'),
    })


def _non_silent_wav(sample_count: int = 1200) -> bytes:
    pcm = b"".join(struct.pack("<h", 1000 if i % 2 else -1000) for i in range(sample_count))
    fmt = struct.pack("<HHIIHH", 1, 1, 44100, 88200, 2, 16)
    riff_size = 4 + (8 + len(fmt)) + (8 + len(pcm))
    return (
        b"RIFF"
        + struct.pack("<I", riff_size)
        + b"WAVEfmt "
        + struct.pack("<I", len(fmt))
        + fmt
        + b"data"
        + struct.pack("<I", len(pcm))
        + pcm
    )


def test_real_engine_happy_path_non_silent_wav(proof_opts: ProofOptions) -> None:
    syn = {
        "audio_id": "aid1",
        "routed_engine": "xtts_v2",
        "duration": 1.0,
        "quality_score": 0.9,
    }
    asset = {"id": "lib1", "audio_id": "aid1", "path": "/tmp/harness.wav"}
    track = {"id": "trk1"}
    clip = {"id": "clip1"}

    class SeqHttp2(FakeHttp):
        def __init__(self) -> None:
            super().__init__({})
            self._state_i = 0

        def get(self, url: str, timeout: float) -> tuple[int, bytes]:
            self.calls.append(url)
            if "/api/health/readiness" in url:
                return (200, json.dumps({"ready": True}).encode())
            if "/api/health/" in url:
                return (200, b"{}")
            if "/api/profiles" in url:
                return (200, json.dumps({"items": [{"id": "p1", "reference_audio_bound": True}]}).encode())
            if "/api/voice/audio/aid1" in url:
                return (200, _non_silent_wav())
            if "/api/timeline/state" in url:
                self._state_i += 1
                if self._state_i == 1:
                    return (200, json.dumps({"revision": 0}).encode())
                return (200, json.dumps({"revision": 2}).encode())
            return (404, b"nope")

        def post_json(self, url: str, payload: dict, timeout: float) -> tuple[int, bytes]:
            self.calls.append(url)
            if "/api/voice/synthesize" in url:
                return (200, json.dumps(syn).encode())
            if "/api/timeline/create" in url:
                return (200, json.dumps({}).encode())
            if "/api/timeline/tracks" in url:
                return (200, json.dumps(track).encode())
            if "/api/timeline/clips" in url:
                return (200, json.dumps(clip).encode())
            return (404, b"{}")

        def post_multipart_file(
            self, url: str, field_name: str, filename: str, file_bytes: bytes, timeout: float
        ) -> tuple[int, bytes]:
            self.calls.append(url)
            if "/api/library/assets/upload" in url:
                return (201, json.dumps(asset).encode())
            return (500, b"x")

    r = run_real_engine_flow(SeqHttp2(), proof_opts)
    assert r.classification == "REAL_ENGINE"
    assert not r.blockers


def test_audio_too_small_unknown(proof_opts: ProofOptions) -> None:
    http = _good_chain(b"hi")
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("small" in b.lower() for b in r.blockers)


def test_audio_json_unknown(proof_opts: ProofOptions) -> None:
    http = _good_chain(b'{"error":true}' + b"x" * 120)
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"


def test_audio_not_riff_unknown(proof_opts: ProofOptions) -> None:
    body = b"x" * 2048
    http = _good_chain(body)
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("RIFF" in b or "wave" in b.lower() for b in r.blockers)


def test_library_upload_fail_unknown(proof_opts: ProofOptions) -> None:
    http = _good_chain(_non_silent_wav(), upload_status=500)
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("library" in b.lower() for b in r.blockers)


def test_timeline_clip_fail_unknown(proof_opts: ProofOptions) -> None:
    http = _good_chain(_non_silent_wav(), clip_status=400)
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("clip" in b.lower() for b in r.blockers)


def test_default_run_does_not_claim_durability(proof_opts: ProofOptions) -> None:
    r = run_real_engine_flow(_good_chain(_non_silent_wav()), proof_opts)
    assert r.classification == "REAL_ENGINE"
    assert r.durability["claimed"] is False
    assert r.durability["restart_performed"] is False


def test_verify_durability_without_restart_replays_but_does_not_claim_restart(
    proof_opts: ProofOptions,
) -> None:
    proof_opts.verify_durability = True
    http = _good_chain(_non_silent_wav())
    http.handlers["/api/library/assets/lib1"] = (200, json.dumps({"id": "lib1"}).encode())
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "REAL_ENGINE"
    assert r.durability["claimed"] is False
    assert "restart command not supplied" in r.durability["blocker"]


def test_library_requery_failure_produces_durability_blocker(proof_opts: ProofOptions) -> None:
    proof_opts.verify_durability = True
    r = run_real_engine_flow(_good_chain(_non_silent_wav()), proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("library asset re-query" in b for b in r.blockers)


def test_timeline_requery_failure_produces_durability_blocker(proof_opts: ProofOptions) -> None:
    proof_opts.verify_durability = True
    http = _good_chain(_non_silent_wav())
    http.handlers["/api/library/assets/lib1"] = (200, json.dumps({"id": "lib1"}).encode())
    http.handlers["/api/timeline/state"] = (200, json.dumps({"revision": 2, "tracks": []}).encode())
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("timeline re-query" in b for b in r.blockers)


def test_restart_command_failure_produces_durability_blocker(
    proof_opts: ProofOptions,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    proof_opts.verify_durability = True
    proof_opts.restart_backend_command = "restart_backend --now"
    http = _good_chain(_non_silent_wav())
    http.handlers["/api/library/assets/lib1"] = (200, json.dumps({"id": "lib1"}).encode())

    class FailedProcess:
        returncode = 7
        stderr = "restart failed"

    from scripts.proof import run_voice_synthesis_real_engine_proof as mod

    monkeypatch.setattr(mod.subprocess, "run", lambda *args, **kwargs: FailedProcess())
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "UNKNOWN"
    assert any("restart command failed" in b for b in r.blockers)


def test_restart_command_success_sets_durability_claimed(
    proof_opts: ProofOptions,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    proof_opts.verify_durability = True
    proof_opts.restart_backend_command = "restart_backend --now"
    http = _good_chain(_non_silent_wav())
    http.handlers["/api/library/assets/lib1"] = (200, json.dumps({"id": "lib1"}).encode())

    class OkProcess:
        returncode = 0
        stderr = ""

    from scripts.proof import run_voice_synthesis_real_engine_proof as mod

    monkeypatch.setattr(mod.subprocess, "run", lambda *args, **kwargs: OkProcess())
    r = run_real_engine_flow(http, proof_opts)
    assert r.classification == "REAL_ENGINE"
    assert r.durability["claimed"] is True
    assert r.durability["restart_performed"] is True
    assert r.durability["reload_verified"] is True


def test_generated_json_records_durability_fields(proof_opts: ProofOptions) -> None:
    r = run_real_engine_flow(_good_chain(_non_silent_wav()), proof_opts)
    payload = _proof_json(r, proof_opts)
    assert payload["durability"]["claimed"] is False
    assert "restart_performed" in payload["durability"]
    assert "reload_verified" in payload["durability"]


def test_generated_markdown_marks_durability_non_claim(proof_opts: ProofOptions) -> None:
    r = run_real_engine_flow(_good_chain(_non_silent_wav()), proof_opts)
    md = render_markdown_report(r, proof_opts)
    assert "Durability non-claim" in md
    assert "not durability proof unless restart durability" in md


def test_route_builder_returns_expected_paths() -> None:
    assert ProofApiRoutes.audio_url("abc") == "/api/voice/audio/abc"
    assert ProofApiRoutes.library_asset_url("asset") == "/api/library/assets/asset"


def test_route_builder_session_id_is_encoded() -> None:
    path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_STATE, "a b&c")
    assert path == "/api/timeline/state?session_id=a+b%26c"


def test_full_url_trailing_slash_does_not_double_slash() -> None:
    assert ProofApiRoutes.full_url("http://127.0.0.1:8000/", "/api/health/") == "http://127.0.0.1:8000/api/health/"
