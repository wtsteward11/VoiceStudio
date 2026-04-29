#!/usr/bin/env python3
"""
Automated voice synthesis real-engine proof harness (producer).

Writes JSON + Markdown proof artifacts and validates Markdown via
`scripts.ci.check_voice_synthesis_proof_boundary.validate_report`.

Dry-run requires no backend. Real mode calls local FastAPI routes only.
"""
from __future__ import annotations

import argparse
import json
import os
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


@dataclass
class ProofResult:
    classification: str
    blockers: list[str] = field(default_factory=list)
    evidence: dict[str, Any] = field(default_factory=dict)


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
    summary = {
        "mode": "dry_run_fixtures",
        "classification": "STUB_ENGINE+REAL_ENGINE",
        "files": [str(stub_path), str(real_path)],
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
            f"VERDICT: REAL_ENGINE",
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
            "## Explicit Non-Claims",
            "",
            "- not operator proof",
            "- not runtime FULL PASS",
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
    evidence: dict[str, Any] = {}

    def base(path: str) -> str:
        b = options.base_url.rstrip("/")
        if not path.startswith("/"):
            path = "/" + path
        return b + path

    # Health
    try:
        st, _body = client.get(base("/api/health/"), t)
        if st >= 400:
            blockers.append(f"health HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"health request failed: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    # Readiness
    try:
        st, body = client.get(base("/api/health/readiness"), t)
        if st == 503:
            blockers.append("readiness: service not ready (503)")
            return ProofResult("UNKNOWN", blockers, evidence)
        if st >= 400:
            blockers.append(f"readiness HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
        try:
            readiness = json.loads(body.decode("utf-8", errors="replace"))
            evidence["readiness"] = readiness
        except json.JSONDecodeError:
            blockers.append("readiness: non-JSON body")
            return ProofResult("UNKNOWN", blockers, evidence)
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"readiness failed: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    # Profiles
    try:
        st, body = client.get(base("/api/profiles"), t)
        if st >= 400:
            blockers.append(f"profiles HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
        prof = json.loads(body.decode("utf-8", errors="replace"))
        items = prof.get("items") or []
        evidence["profile_count"] = len(items)
        profile_id = options.profile_id
        if not profile_id:
            chosen = None
            for it in items:
                if it.get("reference_audio_bound"):
                    chosen = it.get("id")
                    break
            profile_id = chosen or (items[0].get("id") if items else None)
        if not profile_id:
            blockers.append("no profile available")
            return ProofResult("UNKNOWN", blockers, evidence)
        evidence["profile_id"] = profile_id
    except (OSError, HTTPError, URLError, json.JSONDecodeError, KeyError) as e:
        blockers.append(f"profiles: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    engine = options.engine or "xtts_v2"
    synth_payload = {
        "profile_id": profile_id,
        "text": "Harness proof one two three.",
        "engine": engine,
        "language": "en",
    }
    try:
        st, body = client.post_json(base("/api/voice/synthesize"), synth_payload, t)
        if st >= 400:
            blockers.append(f"synthesize HTTP {st}: {body[:200]!r}")
            return ProofResult("UNKNOWN", blockers, evidence)
        syn = json.loads(body.decode("utf-8", errors="replace"))
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"synthesize: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    audio_id = syn.get("audio_id")
    routed = str(syn.get("routed_engine") or "")
    evidence["routed_engine"] = routed
    if not audio_id:
        blockers.append("synthesize response missing audio_id")
        return ProofResult("UNKNOWN", blockers, evidence)
    if routed.lower() in ("stub", "mock", "test"):
        blockers.append(f"routed_engine is non-real: {routed}")
        return ProofResult("UNKNOWN", blockers, evidence)

    try:
        st, audio_bytes = client.get(base(f"/api/voice/audio/{audio_id}"), t)
        if st >= 400:
            blockers.append(f"audio GET HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"audio GET: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    if len(audio_bytes) < 44:
        blockers.append(f"audio body too small ({len(audio_bytes)} bytes) for WAV container")
        return ProofResult("UNKNOWN", blockers, evidence)
    if audio_bytes[:1] == b"{":
        blockers.append("audio body looks like JSON error, not binary wav")
        return ProofResult("UNKNOWN", blockers, evidence)
    if audio_bytes[:4] != b"RIFF" or b"WAVE" not in audio_bytes[:16]:
        blockers.append("audio body missing RIFF/WAVE signature")
        return ProofResult("UNKNOWN", blockers, evidence)

    evidence["artifact_size_bytes"] = len(audio_bytes)
    evidence["artifact_size_kib"] = len(audio_bytes) / 1024.0

    # Library upload
    try:
        st, up_body = client.post_multipart_file(
            base("/api/library/assets/upload"),
            "file",
            "harness_proof.wav",
            audio_bytes,
            t,
        )
        if st != 201:
            blockers.append(f"library upload HTTP {st}: {up_body[:200]!r}")
            return ProofResult("UNKNOWN", blockers, evidence)
        asset = json.loads(up_body.decode("utf-8", errors="replace"))
        evidence["library"] = {
            "asset_id": asset.get("id"),
            "audio_id": asset.get("audio_id"),
            "path": asset.get("path"),
        }
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"library upload: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    # Timeline
    sid_q = urlencode({"session_id": options.session_id})
    try:
        st, body = client.get(base(f"/api/timeline/state?{sid_q}"), t)
        if st >= 400:
            blockers.append(f"timeline state HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
        before = json.loads(body.decode("utf-8", errors="replace"))
        rev0 = int(before.get("revision") or 0)
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline state: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    try:
        st, _ = client.post_json(
            base(f"/api/timeline/create?{sid_q}"),
            {"name": "Harness Timeline", "sample_rate": 48000},
            t,
        )
        if st >= 400:
            blockers.append(f"timeline create HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
    except (OSError, HTTPError, URLError) as e:
        blockers.append(f"timeline create: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    try:
        st, tr_body = client.post_json(
            base(f"/api/timeline/tracks?{sid_q}"),
            {"name": "Harness Track", "type": "audio"},
            t,
        )
        if st >= 400:
            blockers.append(f"timeline tracks HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
        track = json.loads(tr_body.decode("utf-8", errors="replace"))
        track_id = track.get("id")
        if not track_id:
            blockers.append("timeline track response missing id")
            return ProofResult("UNKNOWN", blockers, evidence)
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline track: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    lib_path = str(evidence["library"].get("path") or "")

    try:
        st, clip_body = client.post_json(
            base(f"/api/timeline/clips?{sid_q}"),
            {
                "track_id": track_id,
                "source_path": lib_path,
                "start_time": 0.0,
                "duration": 1.0,
                "name": "HarnessClip",
            },
            t,
        )
        if st >= 400:
            blockers.append(f"timeline clips HTTP {st}: {clip_body[:200]!r}")
            return ProofResult("UNKNOWN", blockers, evidence)
        clip = json.loads(clip_body.decode("utf-8", errors="replace"))
        clip_id = clip.get("id")
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline clip: {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    try:
        st, body = client.get(base(f"/api/timeline/state?{sid_q}"), t)
        if st >= 400:
            blockers.append(f"timeline state after clip HTTP {st}")
            return ProofResult("UNKNOWN", blockers, evidence)
        after = json.loads(body.decode("utf-8", errors="replace"))
        rev1 = int(after.get("revision") or 0)
    except (OSError, HTTPError, URLError, json.JSONDecodeError) as e:
        blockers.append(f"timeline state (after): {e}")
        return ProofResult("UNKNOWN", blockers, evidence)

    evidence["timeline"] = {
        "revision_before": rev0,
        "revision_after": rev1,
        "clip_id": clip_id,
        "track_id": track_id,
    }

    return ProofResult("REAL_ENGINE", [], evidence)


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
        result = ProofResult("STUB_ENGINE", [], {"reason": "VOICESTUDIO_TEST_MODE"})
        md = render_markdown_report(result, opts)
        out_md = opts.markdown_output or (opts.output_dir / "VOICE_SYNTHESIS_PROOF_HARNESS_STUB.md")
        opts.output_dir.mkdir(parents=True, exist_ok=True)
        out_md.write_text(md, encoding="utf-8")
        summary = {"classification": "STUB_ENGINE", "blockers": []}
        out_json = opts.json_output or (opts.output_dir / "proof_harness_result.json")
        out_json.write_text(json.dumps(summary, indent=2), encoding="utf-8")
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
    out_json.write_text(
        json.dumps(
            {
                "classification": result.classification,
                "blockers": result.blockers,
                "evidence": result.evidence,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    if _validate_paths([out_md]) != 0:
        return 1
    if opts.require_real and result.classification != "REAL_ENGINE":
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
