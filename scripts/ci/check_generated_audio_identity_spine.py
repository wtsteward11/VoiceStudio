#!/usr/bin/env python3
"""Validate generated-audio product authority identity spines in proof JSON."""
from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from pathlib import Path
from typing import Any, NamedTuple

ROOT = Path(__file__).resolve().parent.parent.parent
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
STUB_ROUTED_RE = re.compile(r"^(?:stub|mock|test)?$", re.IGNORECASE)


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
        data = json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        return None, [Violation(_rel(path), "FILE_READ", "$", str(exc), "Ensure the file is readable")]
    except json.JSONDecodeError as exc:
        return None, [Violation(_rel(path), "INVALID_JSON", "$", str(exc), "Write valid JSON")]
    if not isinstance(data, dict):
        return None, [Violation(_rel(path), "JSON_NOT_OBJECT", "$", "Top-level JSON is not an object", "Emit an object")]
    return data, []


def _section(data: dict[str, Any], name: str) -> dict[str, Any]:
    value = data.get(name)
    return value if isinstance(value, dict) else {}


def _text(value: Any) -> str:
    return str(value).strip() if value is not None else ""


def _has_blocker(data: dict[str, Any], token: str) -> bool:
    haystack: list[str] = []
    for key in ("blockers", "non_claims"):
        value = data.get(key)
        if isinstance(value, list):
            haystack.extend(str(v) for v in value)
        elif value is not None:
            haystack.append(str(value))
    return token.lower() in "\n".join(haystack).lower()


def _duration_mismatch(left: Any, right: Any) -> bool:
    try:
        a = float(left)
        b = float(right)
    except (TypeError, ValueError):
        return False
    if a <= 0 or b <= 0:
        return False
    delta = abs(a - b)
    return delta > 0.75 and delta / max(a, b) > 0.20


def _sha_from(data: dict[str, Any]) -> tuple[str, str]:
    generated = _section(data, "generated_audio")
    audio = _section(data, "audio_artifact")
    generated_sha = _text(generated.get("artifact_sha256"))
    audio_sha = _text(audio.get("sha256"))
    return generated_sha, audio_sha


def validate_identity_spine(path: Path) -> list[Violation]:
    data, violations = _load_json(path)
    if violations or data is None:
        return violations

    rel = _rel(path)
    classification = _text(data.get("classification"))
    if classification != "REAL_ENGINE":
        return []

    project = _section(data, "project")
    generated = _section(data, "generated_audio")
    library = _section(data, "library")
    timeline = _section(data, "timeline")
    synthesis = _section(data, "synthesis")
    audio = _section(data, "audio_artifact")
    export = _section(data, "export")

    routed = _text(generated.get("routed_engine") or data.get("routed_engine"))
    if STUB_ROUTED_RE.match(routed):
        violations.append(
            Violation(
                rel,
                "STUB_ROUTED_ENGINE",
                "$.generated_audio.routed_engine",
                f"REAL_ENGINE identity graph has invalid routed_engine '{routed}'",
                "Record the actual routed non-stub engine id or classify as UNKNOWN/STUB",
            )
        )

    project_id = _text(project.get("project_id"))
    if not project_id and not _has_blocker(data, "project_id"):
        violations.append(
            Violation(
                rel,
                "MISSING_PROJECT_ID",
                "$.project.project_id",
                "REAL_ENGINE product-closure proof lacks project_id",
                "Record project.project_id or add an explicit blocker/non-claim",
            )
        )

    generated_audio_id = _text(generated.get("generated_audio_id") or generated.get("audio_id"))
    if not generated_audio_id:
        violations.append(
            Violation(
                rel,
                "MISSING_GENERATED_AUDIO_ID",
                "$.generated_audio.generated_audio_id",
                "Generated audio id is missing",
                "Record generated_audio.generated_audio_id or explicitly map generated_audio.audio_id",
            )
        )

    library_link = _text(generated.get("library_asset_id") or library.get("asset_id") or library.get("audio_id"))
    if not library_link:
        violations.append(
            Violation(
                rel,
                "MISSING_LIBRARY_LINK",
                "$.generated_audio.library_asset_id",
                "No library asset/audio link exists for generated audio",
                "Record library.asset_id/audio_id and generated_audio.library_asset_id",
            )
        )

    clip_id = _text(generated.get("timeline_clip_id") or timeline.get("clip_id"))
    track_id = _text(timeline.get("track_id"))
    if not (clip_id and track_id):
        violations.append(
            Violation(
                rel,
                "MISSING_TIMELINE_LINK",
                "$.generated_audio.timeline_clip_id",
                "No timeline clip/track link exists for generated audio",
                "Record timeline.track_id and timeline.clip_id",
            )
        )

    session_ids = {
        value
        for value in (_text(project.get("session_id")), _text(timeline.get("session_id")))
        if value
    }
    if len(session_ids) > 1:
        violations.append(
            Violation(
                rel,
                "CONFLICTING_SESSION_ID",
                "$.project.session_id",
                f"Conflicting session ids: {sorted(session_ids)}",
                "Use one session id consistently across project and timeline evidence",
            )
        )

    if _duration_mismatch(timeline.get("duration_seconds"), synthesis.get("duration_seconds")):
        violations.append(
            Violation(
                rel,
                "DURATION_MISMATCH",
                "$.timeline.duration_seconds",
                "Timeline duration does not match synthesis duration",
                "Record clip duration from the synthesized/generated audio artifact",
            )
        )

    generated_sha, audio_sha = _sha_from(data)
    if not (generated_sha or audio_sha):
        violations.append(
            Violation(
                rel,
                "MISSING_ARTIFACT_HASH",
                "$.generated_audio.artifact_sha256",
                "Generated audio graph lacks artifact SHA-256",
                "Record generated_audio.artifact_sha256 or audio_artifact.sha256",
            )
        )
    for field, value in (
        ("$.generated_audio.artifact_sha256", generated_sha),
        ("$.audio_artifact.sha256", audio_sha),
    ):
        if value and not SHA256_RE.fullmatch(value):
            violations.append(
                Violation(
                    rel,
                    "MISSING_ARTIFACT_HASH",
                    field,
                    "Artifact SHA-256 must be lowercase 64-character hex",
                    "Use hashlib.sha256(...).hexdigest()",
                )
            )
    if generated_sha and audio_sha and generated_sha != audio_sha:
        violations.append(
            Violation(
                rel,
                "MISSING_ARTIFACT_HASH",
                "$.generated_audio.artifact_sha256",
                "Generated audio hash conflicts with audio artifact hash",
                "Use the same downloaded audio bytes as the hash source",
            )
        )

    if export.get("claimed") is True:
        if not (
            _text(export.get("export_id") or export.get("path"))
            and int(export.get("size_bytes") or 0) > 1024
            and ("WAV" in _text(export.get("container")).upper() or "RIFF" in _text(export.get("container")).upper())
            and export.get("non_silent") is True
        ):
            violations.append(
                Violation(
                    rel,
                    "MISSING_EXPORT_EVIDENCE",
                    "$.export",
                    "Export is claimed but lacks WAV forensic evidence",
                    "Record export path/id, size, WAV container, and non_silent=true",
                )
            )

    return violations


def _dir_files(directory: Path) -> list[Path]:
    if not directory.exists():
        return []
    files: list[Path] = []
    for path in directory.rglob("*.json"):
        if not path.is_file():
            continue
        data, load_violations = _load_json(path)
        if load_violations:
            files.append(path)
        elif data and data.get("schema_version") == "voice_synthesis_proof.v1":
            files.append(path)
    return sorted(files, key=lambda p: str(p))


def _valid_real_fixture() -> dict[str, Any]:
    sha = "a" * 64
    return {
        "schema_version": "voice_synthesis_proof.v1",
        "classification": "REAL_ENGINE",
        "routed_engine": "xtts_v2",
        "blockers": [],
        "non_claims": ["No human heard attestation is claimed."],
        "project": {
            "project_id": "project-123",
            "project_name": "Proof Project",
            "session_id": "session-123",
            "persistence_scope": "sqlite",
        },
        "generated_audio": {
            "generated_audio_id": "audio-123",
            "audio_id": "audio-123",
            "source_engine": "xtts_v2",
            "routed_engine": "xtts_v2",
            "profile_id": "profile-123",
            "artifact_sha256": sha,
            "library_asset_id": "asset-123",
            "timeline_clip_id": "clip-123",
        },
        "synthesis": {
            "audio_id": "audio-123",
            "duration_seconds": 2.0,
        },
        "audio_artifact": {
            "sha256": sha,
            "duration_seconds_from_wav": 2.0,
        },
        "library": {
            "asset_id": "asset-123",
            "audio_id": "audio-123",
        },
        "timeline": {
            "session_id": "session-123",
            "track_id": "track-123",
            "clip_id": "clip-123",
            "duration_seconds": 2.0,
        },
        "export": {
            "claimed": True,
            "export_id": "export-123",
            "path": "C:/tmp/export.wav",
            "size_bytes": 4096,
            "sha256": "b" * 64,
            "container": "RIFF/WAVE",
            "duration_seconds_from_wav": 2.0,
            "non_silent": True,
        },
    }


def _valid_unknown_fixture() -> dict[str, Any]:
    payload = _valid_real_fixture()
    payload["classification"] = "UNKNOWN"
    payload["routed_engine"] = ""
    payload["blockers"] = ["backend unavailable"]
    payload["project"] = {}
    payload["generated_audio"] = {}
    payload["export"] = {"claimed": False, "blocker": "not attempted"}
    return payload


def _run_self_tests() -> tuple[bool, list[str]]:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        valid = root / "valid.json"
        valid.write_text(json.dumps(_valid_real_fixture()), encoding="utf-8")
        missing = _valid_real_fixture()
        missing["project"]["project_id"] = None
        invalid = root / "missing_project.json"
        invalid.write_text(json.dumps(missing), encoding="utf-8")
        unknown = root / "unknown.json"
        unknown.write_text(json.dumps(_valid_unknown_fixture()), encoding="utf-8")

        messages: list[str] = []
        valid_rules = [v.rule for v in validate_identity_spine(valid)]
        invalid_rules = [v.rule for v in validate_identity_spine(invalid)]
        unknown_rules = [v.rule for v in validate_identity_spine(unknown)]
        messages.append(f"valid rules={valid_rules}")
        messages.append(f"invalid rules={invalid_rules}")
        messages.append(f"unknown rules={unknown_rules}")
        ok = not valid_rules and "MISSING_PROJECT_ID" in invalid_rules and not unknown_rules
        return ok, messages


def _to_json(violations: list[Violation]) -> str:
    return json.dumps(
        {
            "ok": not violations,
            "violations": [v._asdict() for v in violations],
        },
        sort_keys=True,
        indent=2,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate generated-audio identity spine proof JSON.")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--proof-json", type=Path)
    source.add_argument("--dir", type=Path)
    parser.add_argument("--self-test-examples", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test_examples:
        ok, messages = _run_self_tests()
        if args.json:
            print(json.dumps({"ok": ok, "messages": messages}, sort_keys=True, indent=2))
        else:
            for message in messages:
                print(message)
            print("PASS" if ok else "FAIL")
        return 0 if ok else 1

    files: list[Path]
    if args.proof_json:
        files = [args.proof_json]
    elif args.dir:
        files = _dir_files(args.dir)
    else:
        parser.error("one of --proof-json, --dir, or --self-test-examples is required")

    violations: list[Violation] = []
    for path in files:
        violations.extend(validate_identity_spine(path))

    if args.json:
        print(_to_json(violations))
    else:
        for violation in violations:
            print(f"{violation.file}: {violation.rule}: {violation.field}: {violation.detail}")
            print(f"  fix: {violation.fix}")
        if not violations:
            print(f"PASS: {len(files)} proof JSON file(s)")
    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
