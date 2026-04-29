#!/usr/bin/env python3
"""
Automated voice synthesis real-engine proof harness (producer).

Writes JSON + Markdown proof artifacts and validates Markdown via
`scripts.ci.check_voice_synthesis_proof_boundary.validate_report`.

Dry-run requires no backend. Real mode calls local FastAPI routes only.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
import shlex
import subprocess
import sys
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Protocol
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

# Repo root (…/VoiceStudio)
ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.proof.audio_forensics import analyze_wav_bytes, is_json_error_body, sha256_hex

MINIMAL_WAV_BYTES = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def _stub_test_mode() -> bool:
    raw = os.environ.get("VOICESTUDIO_TEST_MODE", "").strip().lower()
    return raw in ("1", "true", "yes", "stub")


@dataclass
class ProofOptions:
    base_url: str
    engine: str | None
    profile_id: str | None
    session_id: str
    output_dir: Path
    json_output: Path | None
    markdown_output: Path | None
    require_real: bool
    dry_run_fixtures: bool
    timeout_seconds: float
    verify_durability: bool = False
    restart_backend_command: str | None = None


@dataclass
class ProofResult:
    classification: str
    blockers: list[str] = field(default_factory=list)
    evidence: dict[str, Any] = field(default_factory=dict)
    environment: dict[str, Any] = field(default_factory=dict)
    backend: dict[str, Any] = field(default_factory=dict)
    profile: dict[str, Any] = field(default_factory=dict)
    synthesis: dict[str, Any] = field(default_factory=dict)
    audio_artifact: dict[str, Any] = field(default_factory=dict)
    library: dict[str, Any] = field(default_factory=dict)
    timeline: dict[str, Any] = field(default_factory=dict)
    durability: dict[str, Any] = field(default_factory=dict)
    non_claims: list[str] = field(default_factory=list)


class ProofApiRoutes:
    HEALTH = "/api/health/"
    READINESS = "/api/health/readiness"
    PROFILES = "/api/profiles"
    SYNTHESIZE = "/api/voice/synthesize"
    AUDIO = "/api/voice/audio/{audio_id}"
    LIBRARY_UPLOAD = "/api/library/assets/upload"
    LIBRARY_ASSET = "/api/library/assets/{asset_id}"
    TIMELINE_STATE = "/api/timeline/state"
    TIMELINE_CREATE = "/api/timeline/create"
    TIMELINE_TRACKS = "/api/timeline/tracks"
    TIMELINE_CLIPS = "/api/timeline/clips"

    @staticmethod
    def full_url(base_url: str, path: str) -> str:
        base = base_url.rstrip("/")
        if not path.startswith("/"):
            path = "/" + path
        return base + path

    @staticmethod
    def audio_url(audio_id: str) -> str:
        return ProofApiRoutes.AUDIO.format(audio_id=audio_id)

    @staticmethod
    def library_asset_url(asset_id: str) -> str:
        return ProofApiRoutes.LIBRARY_ASSET.format(asset_id=asset_id)

    @staticmethod
    def timeline_with_session(path: str, session_id: str) -> str:
        return f"{path}?{urlencode({'session_id': session_id})}"


class HttpLike(Protocol):
    def get(self, url: str, timeout: float) -> tuple[int, bytes]:
        ...

    def post_json(self, url: str, payload: dict[str, Any], timeout: float) -> tuple[int, bytes]:
        ...

    def post_multipart_file(
        self, url: str, field_name: str, filename: str, file_bytes: bytes, timeout: float
    ) -> tuple[int, bytes]:
        ...


class StdlibHttpClient:
    """urllib-based HTTP client (no extra dependencies)."""

    def __init__(self, base_url: str) -> None:
        self._base = base_url.rstrip("/")

    def _url(self, path: str) -> str:
        if not path.startswith("/"):
            path = "/" + path
        return self._base + path

    def get(self, url: str, timeout: float) -> tuple[int, bytes]:
        req = Request(url, method="GET")
        with urlopen(req, timeout=timeout) as resp:
            return int(resp.status), resp.read()

    def post_json(self, url: str, payload: dict[str, Any], timeout: float) -> tuple[int, bytes]:
        data = json.dumps(payload).encode("utf-8")
        req = Request(
            url,
            data=data,
            method="POST",
            headers={"Content-Type": "application/json", "Accept": "application/json"},
        )
        try:
            with urlopen(req, timeout=timeout) as resp:
                return int(resp.status), resp.read()
        except HTTPError as e:
            body = e.read() if e.fp else b""
            return int(e.code), body

    def post_multipart_file(
        self, url: str, field_name: str, filename: str, file_bytes: bytes, timeout: float
    ) -> tuple[int, bytes]:
        full = url if url.startswith("http") else self._url(url)
        boundary = f"----vsproof{uuid.uuid4().hex}"
        crlf = b"\r\n"
        parts: list[bytes] = [
            f"--{boundary}".encode(),
            crlf,
            (
                f'Content-Disposition: form-data; name="{field_name}"; '
                f'filename="{filename}"'
            ).encode(),
            crlf,
            b"Content-Type: audio/wav",
            crlf,
            crlf,
            file_bytes,
            crlf,
            f"--{boundary}--".encode(),
            crlf,
        ]
        body = b"".join(parts)
        req = Request(
            full,
            data=body,
            method="POST",
            headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        )
        try:
            with urlopen(req, timeout=timeout) as resp:
                return int(resp.status), resp.read()
        except HTTPError as e:
            err_body = e.read() if e.fp else b""
            return int(e.code), err_body


def _run_git(args: list[str]) -> str | None:
    cp = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if cp.returncode != 0:
        return None
    return cp.stdout.strip()


def _git_metadata() -> dict[str, Any]:
    head = _run_git(["rev-parse", "HEAD"]) or "unknown"
    origin_main = _run_git(["rev-parse", "origin/main"])
    status = _run_git(["status", "--short"]) or ""
    dirty_summary = "clean" if not status else "; ".join(status.splitlines())
    return {
        "head": head,
        "origin_main": origin_main,
        "dirty_summary": dirty_summary,
    }


def _default_environment() -> dict[str, Any]:
    raw = os.environ.get("VOICESTUDIO_TEST_MODE")
    return {
        "voicestudio_test_mode": raw,
        "stub_gate_active": _stub_test_mode(),
    }


def _default_durability(blocker: str = "durability check not requested") -> dict[str, Any]:
    return {
        "claimed": False,
        "restart_performed": False,
        "reload_verified": False,
        "blocker": blocker,
        "evidence": [],
    }


def _default_non_claims() -> list[str]:
    return [
        "not operator proof",
        "not runtime FULL PASS",
        "not GAP-008",
        "not Slice 46",
        "not RHVoice",
        "not ENGINE_PARITY_MATRIX",
    ]


def _engine_mode_source(classification: str) -> str:
    if classification == "REAL_ENGINE":
        return "runtime_probe"
    if classification == "STUB_ENGINE":
        return "test_mode_env"
    if classification == "MOCK_ENGINE":
        return "mock_fixture"
    return "blocked_unknown"


def _proof_json(result: ProofResult, options: ProofOptions) -> dict[str, Any]:
    evidence = result.evidence
    requested_engine = evidence.get("requested_engine") or options.engine or "xtts_v2"
    routed_engine = evidence.get("routed_engine") or result.synthesis.get("routed_engine")
    return {
        "schema_version": "voice_synthesis_proof.v1",
        "timestamp_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "git": _git_metadata(),
        "classification": result.classification,
        "proof_type": "voice_synthesis",
        "engine_mode_source": _engine_mode_source(result.classification),
        "requested_engine": requested_engine,
        "routed_engine": routed_engine,
        "environment": result.environment or _default_environment(),
        "backend": {
            "base_url": options.base_url,
            "health_status": None,
            "readiness_status": None,
            **result.backend,
        },
        "profile": {
            "selected_profile_id": None,
            "selection_reason": "unavailable",
            "reference_audio_bound": None,
            "profile_count": None,
            **result.profile,
        },
        "synthesis": {
            "http_status": None,
            "audio_id": None,
            "audio_url": None,
            "duration_seconds": None,
            "quality_score": None,
            "quality_metrics": None,
            **result.synthesis,
        },
        "audio_artifact": {
            "size_bytes": None,
            "sha256": None,
            "header_hex": "",
            "container": None,
            "not_json_error_body": False,
            "sample_rate_hz": None,
            "channels": None,
            "bits_per_sample": None,
            "data_chunk_size": None,
            "duration_seconds_from_wav": None,
            "non_silent": None,
            "peak_abs_sample": None,
            "rms": None,
            "error": None,
            **result.audio_artifact,
        },
        "library": {
            "http_status": None,
            "asset_id": None,
            "audio_id": None,
            "saved_path": None,
            **result.library,
        },
        "timeline": {
            "session_id": options.session_id,
            "revision_before": None,
            "revision_after": None,
            "track_id": None,
            "clip_id": None,
            "start_time": None,
            "end_time": None,
            "duration_seconds": None,
            **result.timeline,
        },
        "durability": result.durability or _default_durability(),
        "non_claims": result.non_claims or _default_non_claims(),
        "blockers": result.blockers,
        "verdict": result.evidence.get("verdict") or result.classification,
    }


def _write_json(path: Path, payload: dict[str, Any]) -> None:
    path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")


def _split_restart_command(command: str) -> list[str]:
    return shlex.split(command, posix=False)


def _dry_run_stub_markdown() -> str:
    return """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: STUB_ENGINE
proof_type: voice_synthesis
engine_mode_source: test_mode_env
runtime_claim: false
operator_claim: false
-->
# Voice Synthesis Proof Harness — Dry-Run STUB Fixture

**Classification: STUB_ENGINE**

Harness dry-run fixture (no backend). VOICESTUDIO_TEST_MODE classification path documented only.

## Engine Mode

STUB_ENGINE (harness dry-run; no live synthesis).

routed_engine: stub (fixture narrative only)

## Non-Claims

- not a real-engine end-to-end proof
- not runtime FULL PASS
- not operator proof
"""


def _dry_run_real_markdown() -> str:
    return """\
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
# Voice Synthesis Proof Harness — Dry-Run REAL_ENGINE Fixture

**Classification: REAL_ENGINE**

## Engine Mode

VERDICT: REAL_ENGINE

| routed_engine | xtts_v2 |

## Audio Artifact

| Size | 186,956 bytes (182.6 KiB) |
| RIFF header | 52 49 46 46 = RIFF / WAVE |
| Body | binary audio — not a JSON error body; does not start with `{` |

## Library Evidence

HTTP 201 library asset; audio_id harness-dryrun

## Timeline Evidence

timeline revision 1→2; clip_id harness-dryrun; POST /api/timeline/tracks

## Explicit Non-Claims

- Dry-run fixture only; no live backend calls were made for this file.
- not operator proof
"""


def dry_run_write_reports(output_dir: Path) -> tuple[Path, Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    stub_path = output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_STUB.md"
    real_path = output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_REAL.md"
    stub_path.write_text(_dry_run_stub_markdown(), encoding="utf-8")
    real_path.write_text(_dry_run_real_markdown(), encoding="utf-8")
    opts = ProofOptions(
        base_url="http://127.0.0.1:8000",
        engine="xtts_v2",
        profile_id="dryrun-profile",
        session_id="dryrun-session",
        output_dir=output_dir,
        json_output=None,
        markdown_output=None,
        require_real=False,
        dry_run_fixtures=True,
        timeout_seconds=1.0,
    )
    wav = (
        b"RIFF\x2c\x08\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
        b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x08\x08\x00\x00"
        + (b"\x00\x10" * 1028)
    )
    analysis = analyze_wav_bytes(wav)
    audio_artifact = {
        "size_bytes": len(wav),
        "sha256": sha256_hex(wav),
        "header_hex": analysis["header_hex"],
        "container": analysis["container"],
        "not_json_error_body": not is_json_error_body(wav),
        "sample_rate_hz": analysis["sample_rate_hz"],
        "channels": analysis["channels"],
        "bits_per_sample": analysis["bits_per_sample"],
        "data_chunk_size": analysis["data_chunk_size"],
        "duration_seconds_from_wav": analysis["duration_seconds"],
        "non_silent": analysis["non_silent"],
        "peak_abs_sample": analysis["peak_abs_sample"],
        "rms": analysis["rms"],
        "error": analysis["error"],
    }
    stub_json = ProofResult(
        "STUB_ENGINE",
        [],
        {"requested_engine": "xtts_v2", "routed_engine": "stub", "verdict": "STUB_ENGINE dry-run fixture"},
        environment={"voicestudio_test_mode": "stub", "stub_gate_active": True},
    )
    real_json = ProofResult(
        "REAL_ENGINE",
        [],
        {"requested_engine": "xtts_v2", "routed_engine": "xtts_v2", "verdict": "REAL_ENGINE dry-run fixture"},
        environment={"voicestudio_test_mode": None, "stub_gate_active": False},
        backend={"health_status": 200, "readiness_status": 200},
        profile={
            "selected_profile_id": "dryrun-profile",
            "selection_reason": "dry_run_fixture",
            "reference_audio_bound": True,
            "profile_count": 1,
        },
        synthesis={
            "http_status": 200,
            "audio_id": "dryrun-audio",
            "audio_url": "/api/voice/audio/dryrun-audio",
            "duration_seconds": 1.0,
            "quality_score": 0.95,
            "quality_metrics": {"mos_score": 4.5},
        },
        audio_artifact=audio_artifact,
        library={"http_status": 201, "asset_id": "dryrun-asset", "audio_id": "dryrun-audio", "saved_path": "dryrun.wav"},
        timeline={
            "session_id": "dryrun-session",
            "revision_before": 1,
            "revision_after": 2,
            "track_id": "dryrun-track",
            "clip_id": "dryrun-clip",
            "start_time": 0.0,
            "end_time": 1.0,
            "duration_seconds": 1.0,
        },
    )
    stub_json_path = output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_STUB.json"
    real_json_path = output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_DRYRUN_REAL.json"
    _write_json(stub_json_path, _proof_json(stub_json, opts))
    _write_json(real_json_path, _proof_json(real_json, opts))
    summary = {
        "mode": "dry_run_fixtures",
        "classification": "STUB_ENGINE+REAL_ENGINE",
        "files": [str(stub_path), str(real_path), str(stub_json_path), str(real_json_path)],
    }
    (output_dir / "proof_harness_dry_run_summary.json").write_text(
        json.dumps(summary, indent=2),
        encoding="utf-8",
    )
    return stub_path, real_path


def render_markdown_report(result: ProofResult, options: ProofOptions) -> str:
    """Render a validator-compliant Markdown report for the proof result."""
    if result.classification == "REAL_ENGINE":
        ev = result.evidence
        routed = ev.get("routed_engine", "unknown")
        size_b = ev.get("artifact_size_bytes", 0)
        kib = ev.get("artifact_size_kib", 0.0)
        lib = ev.get("library", {})
        tl = ev.get("timeline", {})
        durability = result.durability or _default_durability()
        lines = [
            "<!-- VOICESTUDIO_PROOF_BOUNDARY_V1",
            "classification: REAL_ENGINE",
            "proof_type: voice_synthesis",
            "engine_mode_source: runtime_probe",
            "runtime_claim: false",
            "operator_claim: false",
            "-->",
            "# Voice Synthesis Real-Engine Proof (Harness)",
            "",
            "**Classification: REAL_ENGINE**",
            "",
            "## Engine Mode",
            "",
            "VERDICT: REAL_ENGINE",
            "",
            f"| routed_engine | {routed} |",
            "",
            "## Audio Artifact",
            "",
            f"| Size | {size_b} bytes ({kib:.1f} KiB) |",
            "| RIFF header | 52 49 46 46 = RIFF / WAVE |",
            "| Body | binary audio — not a JSON error body; does not start with `{` |",
            "",
            "## Library Evidence",
            "",
            f"HTTP 201 library asset; audio_id {lib.get('audio_id', 'n/a')}",
            "",
            "## Timeline Evidence",
            "",
            (
                f"timeline revision {tl.get('revision_before', '?')}"
                f"→{tl.get('revision_after', '?')}; clip_id {tl.get('clip_id', 'n/a')}; "
                "POST /api/timeline/tracks"
            ),
            "",
            "## Durability Evidence",
            "",
            (
                "Restart durability verified."
                if durability.get("claimed")
                else f"Durability non-claim: {durability.get('blocker') or 'restart/reload not verified'}."
            ),
            "",
            "## Explicit Non-Claims",
            "",
            "- not operator proof",
            "- not runtime FULL PASS",
            "- not durability proof unless restart durability is explicitly verified above",
            "",
        ]
        return "\n".join(lines)
    if result.classification == "STUB_ENGINE":
        return "\n".join(
            [
                "<!-- VOICESTUDIO_PROOF_BOUNDARY_V1",
                "classification: STUB_ENGINE",
                "proof_type: voice_synthesis",
                "engine_mode_source: test_mode_env",
                "runtime_claim: false",
                "operator_claim: false",
                "-->",
                "# Voice Synthesis Proof — STUB_ENGINE (Harness)",
                "",
                "**Classification: STUB_ENGINE**",
                "",
                "VOICESTUDIO_TEST_MODE active — synthesis skipped by policy.",
                "",
                "## Non-Claims",
                "",
                "- not REAL_ENGINE",
                "- not runtime FULL PASS",
                "",
            ]
        )
    # UNKNOWN
    blockers = "; ".join(result.blockers) if result.blockers else "unspecified blocker"
    return "\n".join(
        [
            "<!-- VOICESTUDIO_PROOF_BOUNDARY_V1",
            "classification: UNKNOWN",
            "proof_type: voice_synthesis",
            "engine_mode_source: blocked_unknown",
            "runtime_claim: false",
            "operator_claim: false",
            "-->",
            "# Voice Synthesis Proof — UNKNOWN (Harness)",
            "",
            "**Classification: UNKNOWN**",
            "",
            f"Blocked: could not complete real-engine proof — {blockers}",
            "",
            "## Non-Claims",
            "",
            "- not REAL_ENGINE",
            "- not operator proof",
            "",
        ]
    )


def run_real_engine_flow(client: HttpLike, options: ProofOptions) -> ProofResult:
    t = options.timeout_seconds
    blockers: list[str] = []
    evidence: dict[str, Any] = {"requested_engine": options.engine or "xtts_v2"}
    backend: dict[str, Any] = {"base_url": options.base_url, "health_status": None, "readiness_status": None}
    profile: dict[str, Any] = {}
    synthesis: dict[str, Any] = {}
    audio_artifact: dict[str, Any] = {}
    library: dict[str, Any] = {}
    timeline: dict[str, Any] = {"session_id": options.session_id}
    durability = _default_durability()

    def base(path: str) -> str:
        return ProofApiRoutes.full_url(options.base_url, path)

    def unknown() -> ProofResult:
        return ProofResult(
            "UNKNOWN",
            blockers,
            evidence,
            backend=backend,
            profile=profile,
            synthesis=synthesis,
            audio_artifact=audio_artifact,
            library=library,
            timeline=timeline,
            durability=durability,
        )

    try:
        st, body = client.get(base(ProofApiRoutes.HEALTH), t)
        backend["health_status"] = st
        if body:
            try:
                backend["health_body"] = json.loads(body.decode("utf-8", errors="replace"))
            except json.JSONDecodeError:
                backend["health_body"] = body.decode("utf-8", errors="replace")[:200]
        if st >= 400:
            blockers.append(f"health HTTP {st}")
            return unknown()
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"health request failed: {e}")
        return unknown()

    try:
        st, body = client.get(base(ProofApiRoutes.READINESS), t)
        backend["readiness_status"] = st
        if st == 503:
            blockers.append("readiness: service not ready (503)")
            return unknown()
        if st >= 400:
            blockers.append(f"readiness HTTP {st}")
            return unknown()
        try:
            readiness = json.loads(body.decode("utf-8", errors="replace"))
            backend["readiness_body"] = readiness
            evidence["readiness"] = readiness
        except json.JSONDecodeError:
            blockers.append("readiness: non-JSON body")
            return unknown()
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"readiness failed: {e}")
        return unknown()

    try:
        st, body = client.get(base(ProofApiRoutes.PROFILES), t)
        if st >= 400:
            blockers.append(f"profiles HTTP {st}")
            return unknown()
        prof = json.loads(body.decode("utf-8", errors="replace"))
        items = prof.get("items") or []
        profile["profile_count"] = len(items)
        profile_id = options.profile_id
        selection_reason = "explicit" if profile_id else "first_reference_audio_bound"
        selected_item: dict[str, Any] | None = None
        if profile_id:
            selected_item = next((it for it in items if it.get("id") == profile_id), None)
        else:
            for it in items:
                if it.get("reference_audio_bound"):
                    selected_item = it
                    profile_id = it.get("id")
                    break
            if not profile_id and items:
                first_item = items[0]
                selected_item = first_item if isinstance(first_item, dict) else {}
                profile_id = selected_item.get("id")
                selection_reason = "first_available"
        if not profile_id:
            blockers.append("no profile available")
            return unknown()
        profile.update(
            {
                "selected_profile_id": profile_id,
                "selection_reason": selection_reason,
                "reference_audio_bound": (selected_item or {}).get("reference_audio_bound"),
            }
        )
        evidence["profile_id"] = profile_id
    except (OSError, HTTPError, URLError, json.JSONDecodeError, KeyError) as e:
        blockers.append(f"profiles: {e}")
        return unknown()

    engine = options.engine or "xtts_v2"
    synth_payload = {
        "profile_id": profile_id,
        "text": "Harness proof one two three.",
        "engine": engine,
        "language": "en",
    }
    try:
        st, body = client.post_json(base(ProofApiRoutes.SYNTHESIZE), synth_payload, t)
        synthesis["http_status"] = st
        if st >= 400:
            blockers.append(f"synthesize HTTP {st}: {body[:200]!r}")
            return unknown()
        syn = json.loads(body.decode("utf-8", errors="replace"))
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"synthesize: {e}")
        return unknown()

    audio_id = syn.get("audio_id")
    audio_url = syn.get("audio_url") or (ProofApiRoutes.audio_url(str(audio_id)) if audio_id else None)
    routed = str(syn.get("routed_engine") or "")
    synthesis.update(
        {
            "audio_id": audio_id,
            "audio_url": audio_url,
            "duration_seconds": syn.get("duration"),
            "quality_score": syn.get("quality_score"),
            "quality_metrics": syn.get("quality_metrics"),
        }
    )
    evidence["routed_engine"] = routed
    if not audio_id:
        blockers.append("synthesize response missing audio_id")
        return unknown()
    if routed.lower() in ("stub", "mock", "test", ""):
        blockers.append(f"routed_engine is non-real: {routed}")
        return unknown()

    try:
        st, audio_bytes = client.get(base(str(audio_url)), t)
        if st >= 400:
            blockers.append(f"audio GET HTTP {st}")
            return unknown()
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"audio GET: {e}")
        return unknown()

    analysis = analyze_wav_bytes(audio_bytes)
    audio_artifact.update(
        {
            "size_bytes": len(audio_bytes),
            "sha256": sha256_hex(audio_bytes),
            "header_hex": analysis["header_hex"],
            "container": analysis["container"],
            "not_json_error_body": not is_json_error_body(audio_bytes),
            "sample_rate_hz": analysis["sample_rate_hz"],
            "channels": analysis["channels"],
            "bits_per_sample": analysis["bits_per_sample"],
            "data_chunk_size": analysis["data_chunk_size"],
            "duration_seconds_from_wav": analysis["duration_seconds"],
            "non_silent": analysis["non_silent"],
            "peak_abs_sample": analysis["peak_abs_sample"],
            "rms": analysis["rms"],
            "error": analysis["error"],
        }
    )
    if len(audio_bytes) <= 1024:
        blockers.append(f"audio body too small ({len(audio_bytes)} bytes) for durable REAL_ENGINE proof")
        return unknown()
    if is_json_error_body(audio_bytes):
        blockers.append("audio body looks like JSON error, not binary wav")
        return unknown()
    if analysis["is_wav"] is not True:
        blockers.append(f"audio body is not valid RIFF/WAVE: {analysis['error']}")
        return unknown()
    if analysis["non_silent"] is not True:
        blockers.append("audio forensic analysis did not prove non-silent PCM16 audio")
        return unknown()

    evidence["artifact_size_bytes"] = len(audio_bytes)
    evidence["artifact_size_kib"] = len(audio_bytes) / 1024.0

    try:
        st, up_body = client.post_multipart_file(
            base(ProofApiRoutes.LIBRARY_UPLOAD),
            "file",
            "harness_proof.wav",
            audio_bytes,
            t,
        )
        library["http_status"] = st
        if st != 201:
            blockers.append(f"library upload HTTP {st}: {up_body[:200]!r}")
            return unknown()
        asset = json.loads(up_body.decode("utf-8", errors="replace"))
        library.update(
            {
                "asset_id": asset.get("id"),
                "audio_id": asset.get("audio_id"),
                "saved_path": asset.get("path"),
            }
        )
        evidence["library"] = library
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"library upload: {e}")
        return unknown()

    state_path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_STATE, options.session_id)
    create_path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_CREATE, options.session_id)
    tracks_path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_TRACKS, options.session_id)
    clips_path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_CLIPS, options.session_id)
    try:
        st, body = client.get(base(state_path), t)
        if st >= 400:
            blockers.append(f"timeline state HTTP {st}")
            return unknown()
        before = json.loads(body.decode("utf-8", errors="replace"))
        rev0 = int(before.get("revision") or 0)
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline state: {e}")
        return unknown()

    try:
        st, _ = client.post_json(base(create_path), {"name": "Harness Timeline", "sample_rate": 48000}, t)
        if st >= 400:
            blockers.append(f"timeline create HTTP {st}")
            return unknown()
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"timeline create: {e}")
        return unknown()

    try:
        st, tr_body = client.post_json(base(tracks_path), {"name": "Harness Track", "type": "audio"}, t)
        if st >= 400:
            blockers.append(f"timeline tracks HTTP {st}")
            return unknown()
        track = json.loads(tr_body.decode("utf-8", errors="replace"))
        track_id = track.get("id")
        if not track_id:
            blockers.append("timeline track response missing id")
            return unknown()
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline track: {e}")
        return unknown()

    lib_path = str(library.get("saved_path") or "")
    duration = float(synthesis.get("duration_seconds") or audio_artifact.get("duration_seconds_from_wav") or 1.0)
    try:
        st, clip_body = client.post_json(
            base(clips_path),
            {
                "track_id": track_id,
                "source_path": lib_path,
                "start_time": 0.0,
                "duration": duration,
                "name": "HarnessClip",
            },
            t,
        )
        if st >= 400:
            blockers.append(f"timeline clips HTTP {st}: {clip_body[:200]!r}")
            return unknown()
        clip = json.loads(clip_body.decode("utf-8", errors="replace"))
        clip_id = clip.get("id")
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline clip: {e}")
        return unknown()

    try:
        st, body = client.get(base(state_path), t)
        if st >= 400:
            blockers.append(f"timeline state after clip HTTP {st}")
            return unknown()
        after = json.loads(body.decode("utf-8", errors="replace"))
        rev1 = int(after.get("revision") or 0)
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline state (after): {e}")
        return unknown()

    timeline.update(
        {
            "revision_before": rev0,
            "revision_after": rev1,
            "clip_id": clip_id,
            "track_id": track_id,
            "start_time": 0.0,
            "end_time": duration,
            "duration_seconds": duration,
        }
    )
    evidence["timeline"] = timeline

    durability = _verify_durability(
        client,
        options,
        audio_url=str(audio_url),
        asset_id=str(library.get("asset_id") or ""),
        track_id=str(track_id),
        clip_id=str(clip_id),
    )
    restart_missing = "restart command not supplied"
    if (
        options.verify_durability
        and durability.get("blocker")
        and restart_missing not in str(durability.get("blocker"))
    ):
        blockers.append(str(durability["blocker"]))
        return unknown()

    return ProofResult(
        "REAL_ENGINE",
        [],
        evidence,
        backend=backend,
        profile=profile,
        synthesis=synthesis,
        audio_artifact=audio_artifact,
        library=library,
        timeline=timeline,
        durability=durability,
    )


def _validate_paths(paths: list[Path]) -> int:
    if str(ROOT) not in sys.path:
        sys.path.insert(0, str(ROOT))
    from scripts.ci.check_voice_synthesis_proof_boundary import validate_report

    rc = 0
    for p in paths:
        v = validate_report(p)
        if v:
            rc = 1
            for viol in v:
                print(f"[harness] VALIDATION {p.name}: {viol.rule} — {viol.detail}", file=sys.stderr)
    return rc


def _timeline_contains(state: dict[str, Any], track_id: str, clip_id: str) -> bool:
    for track in state.get("tracks") or []:
        if str(track.get("id")) != track_id:
            continue
        for clip in track.get("clips") or []:
            if str(clip.get("id")) == clip_id:
                return True
    return False


def _durability_replay_checks(
    client: HttpLike,
    options: ProofOptions,
    *,
    audio_url: str,
    asset_id: str,
    track_id: str,
    clip_id: str,
) -> tuple[bool, list[str], str | None]:
    t = options.timeout_seconds
    evidence: list[str] = []
    base = lambda path: ProofApiRoutes.full_url(options.base_url, path)
    try:
        st, body = client.get(base(audio_url), t)
        if st >= 400:
            return False, evidence, f"durability audio re-download HTTP {st}"
        if is_json_error_body(body):
            return False, evidence, "durability audio re-download returned JSON body"
        evidence.append("audio re-download succeeded")
    except (OSError, HTTPError, URLError) as e:
        return False, evidence, f"durability audio re-download failed: {e}"

    if asset_id:
        try:
            st, _body = client.get(base(ProofApiRoutes.library_asset_url(asset_id)), t)
            if st >= 400:
                return False, evidence, f"durability library asset re-query HTTP {st}"
            evidence.append("library asset re-query succeeded")
        except (OSError, HTTPError, URLError) as e:
            return False, evidence, f"durability library re-query failed: {e}"

    try:
        state_path = ProofApiRoutes.timeline_with_session(ProofApiRoutes.TIMELINE_STATE, options.session_id)
        st, body = client.get(base(state_path), t)
        if st >= 400:
            return False, evidence, f"durability timeline re-query HTTP {st}"
        state = json.loads(body.decode("utf-8", errors="replace"))
        if not _timeline_contains(state, track_id, clip_id):
            return False, evidence, "durability timeline re-query did not find track/clip"
        evidence.append("timeline track/clip re-query succeeded")
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        return False, evidence, f"durability timeline re-query failed: {e}"

    return True, evidence, None


def _verify_durability(
    client: HttpLike,
    options: ProofOptions,
    *,
    audio_url: str,
    asset_id: str,
    track_id: str,
    clip_id: str,
) -> dict[str, Any]:
    if not options.verify_durability:
        return _default_durability()

    ok, evidence, blocker = _durability_replay_checks(
        client,
        options,
        audio_url=audio_url,
        asset_id=asset_id,
        track_id=track_id,
        clip_id=clip_id,
    )
    if not ok:
        return {
            "claimed": False,
            "restart_performed": False,
            "reload_verified": False,
            "blocker": blocker,
            "evidence": evidence,
        }
    if not options.restart_backend_command:
        return {
            "claimed": False,
            "restart_performed": False,
            "reload_verified": False,
            "blocker": "restart command not supplied; replay verified without restart only",
            "evidence": evidence,
        }

    cp = subprocess.run(
        _split_restart_command(options.restart_backend_command),
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if cp.returncode != 0:
        return {
            "claimed": False,
            "restart_performed": False,
            "reload_verified": False,
            "blocker": f"restart command failed with exit {cp.returncode}",
            "evidence": evidence + [cp.stderr.strip()[:200]],
        }

    ok, after_restart_evidence, blocker = _durability_replay_checks(
        client,
        options,
        audio_url=audio_url,
        asset_id=asset_id,
        track_id=track_id,
        clip_id=clip_id,
    )
    if not ok:
        return {
            "claimed": False,
            "restart_performed": True,
            "reload_verified": False,
            "blocker": blocker,
            "evidence": evidence + ["restart command succeeded"] + after_restart_evidence,
        }
    return {
        "claimed": True,
        "restart_performed": True,
        "reload_verified": True,
        "blocker": None,
        "evidence": evidence + ["restart command succeeded"] + after_restart_evidence,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Voice synthesis real-engine proof harness.")
    parser.add_argument("--base-url", default=os.environ.get("VOICESTUDIO_REAL_ENGINE_PROOF_BASE", "http://127.0.0.1:8000"))
    parser.add_argument("--engine", default=None)
    parser.add_argument("--profile-id", default=None)
    parser.add_argument("--session-id", default="default")
    parser.add_argument("--output-dir", type=Path, default=Path("artifacts/proof_harness_out"))
    parser.add_argument("--json-output", type=Path, default=None)
    parser.add_argument("--markdown-output", type=Path, default=None)
    parser.add_argument("--require-real", action="store_true")
    parser.add_argument("--dry-run-fixtures", action="store_true")
    parser.add_argument("--timeout-seconds", type=float, default=120.0)
    parser.add_argument("--verify-durability", action="store_true")
    parser.add_argument("--restart-backend-command", default=None)
    args = parser.parse_args(argv)

    opts = ProofOptions(
        base_url=args.base_url,
        engine=args.engine,
        profile_id=args.profile_id,
        session_id=args.session_id,
        output_dir=args.output_dir,
        json_output=args.json_output,
        markdown_output=args.markdown_output,
        require_real=args.require_real,
        dry_run_fixtures=args.dry_run_fixtures,
        timeout_seconds=args.timeout_seconds,
        verify_durability=args.verify_durability,
        restart_backend_command=args.restart_backend_command,
    )

    if opts.dry_run_fixtures:
        stub_p, real_p = dry_run_write_reports(opts.output_dir)
        if _validate_paths([stub_p, real_p]) != 0:
            return 1
        print(f"[harness] dry-run fixtures OK: {stub_p}, {real_p}")
        return 0

    if str(ROOT) not in sys.path:
        sys.path.insert(0, str(ROOT))

    if _stub_test_mode():
        result = ProofResult(
            "STUB_ENGINE",
            [],
            {
                "reason": "VOICESTUDIO_TEST_MODE",
                "requested_engine": opts.engine or "xtts_v2",
                "routed_engine": "stub",
                "verdict": "STUB_ENGINE: VOICESTUDIO_TEST_MODE active",
            },
            environment=_default_environment(),
            durability=_default_durability("stub mode skips real synthesis and durability"),
        )
        md = render_markdown_report(result, opts)
        out_md = opts.markdown_output or (opts.output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_STUB.md")
        opts.output_dir.mkdir(parents=True, exist_ok=True)
        out_md.write_text(md, encoding="utf-8")
        out_json = opts.json_output or (opts.output_dir / "proof_harness_result.json")
        _write_json(out_json, _proof_json(result, opts))
        if _validate_paths([out_md]) != 0:
            return 1
        if opts.require_real:
            print("[harness] STUB_ENGINE with --require-real → exit 1", file=sys.stderr)
            return 1
        return 0

    client: HttpLike = StdlibHttpClient(opts.base_url)
    result = run_real_engine_flow(client, opts)
    md = render_markdown_report(result, opts)
    out_md = opts.markdown_output or (
        opts.output_dir / f"VOICE_SYNTHESIS_PROOF_HARNESS_{result.classification}.md"
    )
    opts.output_dir.mkdir(parents=True, exist_ok=True)
    out_md.write_text(md, encoding="utf-8")
    out_json = opts.json_output or (opts.output_dir / "proof_harness_result.json")
    _write_json(out_json, _proof_json(result, opts))
    if _validate_paths([out_md]) != 0:
        return 1
    if opts.require_real and result.classification != "REAL_ENGINE":
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
