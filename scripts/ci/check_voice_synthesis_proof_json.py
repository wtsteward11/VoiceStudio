#!/usr/bin/env python3
"""Validate VoiceStudio voice synthesis proof JSON artifacts."""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, NamedTuple

ROOT = Path(__file__).resolve().parent.parent.parent
SCHEMA_PATH = ROOT / "schemas" / "voice_synthesis_proof.schema.json"
RUNTIME_PROOF_DIR = ROOT / "docs" / "reports" / "verification" / "runtime_proofs"

VALID_CLASSIFICATIONS = frozenset(["REAL_ENGINE", "STUB_ENGINE", "MOCK_ENGINE", "UNKNOWN"])
STUB_ROUTED_RE = re.compile(r"^(?:stub|mock|test)?$", re.IGNORECASE)
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
REAL_CLAIM_RE = re.compile(
    r"\b(?:REAL_ENGINE\s+confirmed|real\s+synthesis\s+confirmed|real\s+engine\s+proof\s+complete)\b",
    re.IGNORECASE,
)


class Violation(NamedTuple):
    file: str
    rule: str
    field: str
    detail: str
    fix: str


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT)).replace("\\", "/")
    except ValueError:
        return str(path)


def _load_json(path: Path) -> tuple[dict[str, Any] | None, list[Violation]]:
    try:
        raw = path.read_text(encoding="utf-8")
    except OSError as exc:
        return None, [Violation(_rel(path), "FILE_READ", "$", str(exc), "Ensure the file is readable")]
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as exc:
        return None, [
            Violation(
                _rel(path),
                "INVALID_JSON",
                "$",
                f"JSON parse failed: {exc}",
                "Write valid JSON proof output",
            )
        ]
    if not isinstance(data, dict):
        return None, [
            Violation(_rel(path), "JSON_NOT_OBJECT", "$", "Top-level JSON is not an object", "Emit an object")
        ]
    return data, []


def _schema_errors(data: dict[str, Any], rel: str) -> list[Violation]:
    try:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return [
            Violation(
                rel,
                "SCHEMA_UNAVAILABLE",
                str(SCHEMA_PATH),
                str(exc),
                "Ensure schemas/voice_synthesis_proof.schema.json exists and is valid JSON",
            )
        ]

    try:
        from jsonschema import Draft7Validator
    except ImportError:
        return _manual_schema_errors(data, schema, rel)

    validator = Draft7Validator(schema)
    violations: list[Violation] = []
    for err in sorted(validator.iter_errors(data), key=lambda e: list(e.path)):
        field = "$" + "".join(f".{p}" if isinstance(p, str) else f"[{p}]" for p in err.path)
        violations.append(
            Violation(
                rel,
                "SCHEMA_VALIDATION",
                field,
                err.message,
                "Update the proof JSON to match schemas/voice_synthesis_proof.schema.json",
            )
        )
    return violations


def _manual_schema_errors(data: dict[str, Any], schema: dict[str, Any], rel: str) -> list[Violation]:
    """Narrow fallback when jsonschema is unavailable."""
    required = set(schema.get("required", []))
    missing = sorted(required - set(data))
    return [
        Violation(
            rel,
            "SCHEMA_REQUIRED_FIELD",
            f"$.{name}",
            f"Missing required top-level field '{name}'",
            "Emit all required proof JSON fields",
        )
        for name in missing
    ]


def _get(data: dict[str, Any], path: str) -> Any:
    cur: Any = data
    for part in path.split("."):
        if not isinstance(cur, dict):
            return None
        cur = cur.get(part)
    return cur


def _textual_claim_surface(data: dict[str, Any]) -> str:
    parts: list[str] = []
    for key in ("verdict", "non_claims", "blockers"):
        val = data.get(key)
        if isinstance(val, list):
            parts.extend(str(v) for v in val)
        elif val is not None:
            parts.append(str(val))
    return "\n".join(parts)


def _semantic_errors(data: dict[str, Any], rel: str) -> list[Violation]:
    violations: list[Violation] = []
    classification = str(data.get("classification") or "")
    if classification not in VALID_CLASSIFICATIONS:
        return violations

    routed = str(data.get("routed_engine") or "").strip()
    blockers = data.get("blockers") or []
    audio = data.get("audio_artifact") if isinstance(data.get("audio_artifact"), dict) else {}
    library = data.get("library") if isinstance(data.get("library"), dict) else {}
    timeline = data.get("timeline") if isinstance(data.get("timeline"), dict) else {}
    durability = data.get("durability") if isinstance(data.get("durability"), dict) else {}

    if classification == "REAL_ENGINE":
        if STUB_ROUTED_RE.match(routed):
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_STUB_ROUTED",
                    "$.routed_engine",
                    f"REAL_ENGINE proof has invalid routed_engine '{routed}'",
                    "Set routed_engine to the actual non-stub engine id or classify as STUB/MOCK/UNKNOWN",
                )
            )
        if int(audio.get("size_bytes") or 0) <= 1024:
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_SMALL_ARTIFACT",
                    "$.audio_artifact.size_bytes",
                    "REAL_ENGINE audio artifact must be larger than 1024 bytes",
                    "Record the downloaded binary WAV size",
                )
            )
        container = str(audio.get("container") or "")
        if "WAV" not in container.upper() and "RIFF" not in container.upper():
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_MISSING_WAV_CONTAINER",
                    "$.audio_artifact.container",
                    "REAL_ENGINE audio container must include WAV or RIFF/WAVE",
                    "Run WAV forensic analysis and record the container",
                )
            )
        if audio.get("not_json_error_body") is not True:
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_JSON_ERROR_BODY",
                    "$.audio_artifact.not_json_error_body",
                    "REAL_ENGINE audio must be confirmed not to be a JSON error body",
                    "Set not_json_error_body=true only after binary audio validation",
                )
            )
        non_silent = audio.get("non_silent")
        if non_silent is not True:
            blocker_text = " ".join(str(b) for b in blockers)
            if non_silent is not None or not blocker_text:
                violations.append(
                    Violation(
                        rel,
                        "REAL_ENGINE_SILENCE_NOT_RESOLVED",
                        "$.audio_artifact.non_silent",
                        "REAL_ENGINE proof must be non-silent or explicitly UNKNOWN with a blocker",
                        "Record non_silent=true from forensic analysis or classify as UNKNOWN with blocker",
                    )
                )
        if not (library.get("asset_id") or library.get("audio_id")):
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_MISSING_LIBRARY",
                    "$.library",
                    "REAL_ENGINE proof lacks library asset_id/audio_id",
                    "Record the uploaded library asset id or audio id",
                )
            )
        if not (timeline.get("track_id") and timeline.get("clip_id")):
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_MISSING_TIMELINE",
                    "$.timeline",
                    "REAL_ENGINE proof lacks timeline track_id and clip_id",
                    "Record durable timeline track and clip ids",
                )
            )
        before = timeline.get("revision_before")
        after = timeline.get("revision_after")
        if isinstance(before, int) and isinstance(after, int) and after < before:
            violations.append(
                Violation(
                    rel,
                    "REAL_ENGINE_TIMELINE_REGRESSION",
                    "$.timeline.revision_after",
                    "timeline.revision_after is lower than revision_before",
                    "Record the final revision after clip insertion",
                )
            )

    if classification == "UNKNOWN" and not blockers:
        violations.append(
            Violation(
                rel,
                "UNKNOWN_MISSING_BLOCKERS",
                "$.blockers",
                "UNKNOWN proof requires at least one blocker",
                "Record why engine mode or proof completion could not be determined",
            )
        )

    if classification in ("STUB_ENGINE", "MOCK_ENGINE") and REAL_CLAIM_RE.search(_textual_claim_surface(data)):
        violations.append(
            Violation(
                rel,
                "STUB_MOCK_CLAIMS_REAL",
                "$.verdict",
                "STUB/MOCK proof contains real synthesis claim text",
                "Remove real-engine claims or classify only with validated REAL_ENGINE evidence",
            )
        )

    if durability.get("claimed") is True:
        if durability.get("restart_performed") is not True or durability.get("reload_verified") is not True:
            violations.append(
                Violation(
                    rel,
                    "DURABILITY_CLAIMED_WITHOUT_EVIDENCE",
                    "$.durability",
                    "durability.claimed=true requires restart_performed=true and reload_verified=true",
                    "Do not claim durability unless restart/reload evidence exists",
                )
            )

    sha = _get(data, "audio_artifact.sha256")
    if sha:
        if not isinstance(sha, str) or not SHA256_RE.fullmatch(sha):
            violations.append(
                Violation(
                    rel,
                    "INVALID_SHA256",
                    "$.audio_artifact.sha256",
                    "sha256 must be 64 lowercase hexadecimal characters",
                    "Use hashlib.sha256(audio_bytes).hexdigest()",
                )
            )

    return violations


def validate_proof_json(path: Path) -> list[Violation]:
    data, violations = _load_json(path)
    if violations or data is None:
        return violations
    rel = _rel(path)
    return _schema_errors(data, rel) + _semantic_errors(data, rel)


def _is_relevant(path: Path) -> bool:
    norm = str(path).replace("\\", "/")
    return path.suffix.lower() == ".json" and "/docs/reports/verification/runtime_proofs/" in f"/{norm}"


def _run_git_names(args: list[str]) -> list[Path]:
    cp = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if cp.returncode != 0:
        return []
    return [ROOT / line.strip() for line in cp.stdout.splitlines() if line.strip()]


def _get_changed_files(ref: str) -> list[Path]:
    seen: dict[str, Path] = {}
    commands = [
        ["diff", "--name-only", "--diff-filter=ACM", f"{ref}..HEAD"],
        ["diff", "--name-only", "--cached", "--diff-filter=ACM"],
        ["diff", "--name-only", "--diff-filter=ACM"],
        ["ls-files", "--others", "--exclude-standard", "docs/reports/verification/runtime_proofs/"],
    ]
    for cmd in commands:
        for p in _run_git_names(cmd):
            if _is_relevant(p):
                seen[str(p.resolve())] = p
    return sorted(seen.values(), key=lambda p: str(p))


def _get_dir_files(directory: Path) -> list[Path]:
    if not directory.exists():
        return []
    files: list[Path] = []
    for p in directory.rglob("*.json"):
        if not p.is_file():
            continue
        data, violations = _load_json(p)
        if violations or data is None:
            files.append(p)
        elif data.get("schema_version") == "voice_synthesis_proof.v1":
            files.append(p)
    return sorted(files, key=lambda p: str(p))


def _valid_real_fixture() -> dict[str, Any]:
    return {
        "schema_version": "voice_synthesis_proof.v1",
        "timestamp_utc": "2026-04-29T00:00:00Z",
        "git": {"head": "abc123", "origin_main": "def456", "dirty_summary": "clean"},
        "classification": "REAL_ENGINE",
        "proof_type": "voice_synthesis",
        "engine_mode_source": "runtime_probe",
        "requested_engine": "xtts_v2",
        "routed_engine": "xtts_v2",
        "environment": {"voicestudio_test_mode": None, "stub_gate_active": False},
        "backend": {"base_url": "http://127.0.0.1:8000", "health_status": 200, "readiness_status": 200},
        "profile": {
            "selected_profile_id": "p1",
            "selection_reason": "explicit",
            "reference_audio_bound": True,
            "profile_count": 1,
        },
        "synthesis": {
            "http_status": 200,
            "audio_id": "a1",
            "audio_url": "/api/voice/audio/a1",
            "duration_seconds": 1.0,
            "quality_score": 0.9,
            "quality_metrics": {"mos_score": 4.5},
        },
        "audio_artifact": {
            "size_bytes": 4096,
            "sha256": "a" * 64,
            "header_hex": "52494646",
            "container": "RIFF/WAVE",
            "not_json_error_body": True,
            "sample_rate_hz": 44100,
            "channels": 1,
            "bits_per_sample": 16,
            "data_chunk_size": 2048,
            "duration_seconds_from_wav": 1.0,
            "non_silent": True,
            "peak_abs_sample": 1200,
            "rms": 42.0,
            "error": None,
        },
        "library": {"http_status": 201, "asset_id": "asset1", "audio_id": "a1", "saved_path": "x.wav"},
        "timeline": {
            "session_id": "default",
            "revision_before": 1,
            "revision_after": 2,
            "track_id": "trk1",
            "clip_id": "clip1",
            "start_time": 0.0,
            "end_time": 1.0,
            "duration_seconds": 1.0,
        },
        "durability": {
            "claimed": False,
            "restart_performed": False,
            "reload_verified": False,
            "blocker": "restart not performed",
            "evidence": [],
        },
        "non_claims": ["not operator proof", "not runtime FULL PASS"],
        "blockers": [],
        "verdict": "REAL_ENGINE proof JSON is schema-valid",
    }


def _valid_unknown_fixture() -> dict[str, Any]:
    data = _valid_real_fixture()
    data.update(
        {
            "classification": "UNKNOWN",
            "engine_mode_source": "blocked_unknown",
            "routed_engine": None,
            "blockers": ["backend unavailable"],
            "verdict": "UNKNOWN: backend unavailable",
        }
    )
    data["audio_artifact"].update({"size_bytes": 0, "sha256": None, "container": None, "non_silent": None})
    data["library"].update({"http_status": None, "asset_id": None, "audio_id": None, "saved_path": None})
    data["timeline"].update({"revision_before": None, "revision_after": None, "track_id": None, "clip_id": None})
    return data


def run_self_test() -> int:
    cases: list[tuple[str, dict[str, Any], list[str], bool]] = [
        ("valid_real", _valid_real_fixture(), [], True),
        ("valid_unknown", _valid_unknown_fixture(), [], True),
        ("bad_stub_routed", {**_valid_real_fixture(), "routed_engine": "stub"}, ["REAL_ENGINE_STUB_ROUTED"], False),
        ("unknown_no_blocker", {**_valid_unknown_fixture(), "blockers": []}, ["UNKNOWN_MISSING_BLOCKERS"], False),
    ]
    failures: list[str] = []
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        for name, payload, expected, should_pass in cases:
            path = root / f"{name}.json"
            path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")
            violations = validate_proof_json(path)
            rules = [v.rule for v in violations]
            if should_pass and violations:
                failures.append(f"{name}: expected PASS, got {rules}")
            elif not should_pass:
                missing = [r for r in expected if r not in rules]
                if not violations or missing:
                    failures.append(f"{name}: expected {expected}, got {rules}")
    if failures:
        for f in failures:
            print(f"[voice_synthesis_proof_json] SELF-TEST FAIL: {f}", file=sys.stderr)
        return 1
    print(f"[voice_synthesis_proof_json] Self-test: {len(cases)} example(s) PASS")
    return 0


def _result_payload(status: str, mode: str, checked: list[Path], violations: list[Violation]) -> dict[str, Any]:
    return {
        "status": status,
        "mode": mode,
        "checked": [_rel(p) for p in checked],
        "violations": [
            {
                "file": v.file,
                "rule": v.rule,
                "field": v.field,
                "detail": v.detail,
                "fix": v.fix,
            }
            for v in violations
        ],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate voice synthesis proof JSON artifacts.")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--path", type=Path)
    group.add_argument("--dir", type=Path)
    group.add_argument("--changed-from", default=None)
    group.add_argument("--self-test-examples", action="store_true")
    parser.add_argument("--json", action="store_true", dest="json_output")
    args = parser.parse_args(argv)

    if args.self_test_examples:
        rc = run_self_test()
        if args.json_output:
            print(json.dumps({"status": "pass" if rc == 0 else "fail", "mode": "self-test"}, indent=2))
        return rc

    if args.path:
        files = [args.path]
        mode = f"path {args.path}"
    elif args.dir:
        files = _get_dir_files(args.dir)
        mode = f"dir {args.dir}"
    else:
        ref = args.changed_from or "origin/main"
        files = _get_changed_files(ref)
        mode = f"changed from {ref}"

    all_violations: list[Violation] = []
    for path in files:
        all_violations.extend(validate_proof_json(path))

    if all_violations:
        payload = _result_payload("fail", mode, files, all_violations)
        if args.json_output:
            print(json.dumps(payload, indent=2))
        else:
            print("VOICE SYNTHESIS PROOF JSON VIOLATIONS:", file=sys.stderr)
            for v in all_violations:
                print(f"FAIL {v.file}: {v.rule} ({v.field}) — {v.detail}", file=sys.stderr)
                print(f"  Fix: {v.fix}", file=sys.stderr)
        return 1

    payload = _result_payload("pass", mode, files, [])
    if args.json_output:
        print(json.dumps(payload, indent=2))
    else:
        print(f"[voice_synthesis_proof_json] All {len(files)} JSON proof file(s) PASS ({mode})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
