#!/usr/bin/env python3
"""Build an index over VoiceStudio voice synthesis proof JSON bundles."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
from typing import Any

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.check_voice_synthesis_proof_json import validate_proof_json


def _load(path: Path) -> dict[str, Any] | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    if not isinstance(data, dict):
        return None
    if data.get("schema_version") != "voice_synthesis_proof.v1":
        return None
    return data


def _proof_files(directory: Path) -> list[Path]:
    if not directory.exists():
        return []
    return sorted([p for p in directory.rglob("*.json") if p.is_file()], key=lambda p: str(p))


def build_index(directory: Path, *, strict: bool = False) -> tuple[dict[str, Any], int]:
    entries: list[dict[str, Any]] = []
    invalid: list[dict[str, Any]] = []
    counts: dict[str, int] = {}
    schema_versions: set[str] = set()

    for path in _proof_files(directory):
        data = _load(path)
        if data is None:
            continue
        violations = validate_proof_json(path)
        rel = str(path)
        entry = {
            "file": rel,
            "timestamp_utc": data.get("timestamp_utc"),
            "classification": data.get("classification"),
            "schema_version": data.get("schema_version"),
            "valid": not violations,
        }
        entries.append(entry)
        schema_versions.add(str(data.get("schema_version")))
        cls = str(data.get("classification") or "UNKNOWN")
        counts[cls] = counts.get(cls, 0) + 1
        if violations:
            invalid.append(
                {
                    "file": rel,
                    "violations": [
                        {
                            "rule": v.rule,
                            "field": v.field,
                            "detail": v.detail,
                            "fix": v.fix,
                        }
                        for v in violations
                    ],
                }
            )

    sorted_entries = sorted(entries, key=lambda e: (str(e.get("timestamp_utc") or ""), e["file"]))
    latest = sorted_entries[-1] if sorted_entries else None
    real_entries = [e for e in sorted_entries if e.get("classification") == "REAL_ENGINE"]
    unknown_entries = [e for e in sorted_entries if e.get("classification") == "UNKNOWN"]
    index = {
        "directory": str(directory),
        "latest_proof": latest,
        "latest_real_engine": real_entries[-1] if real_entries else None,
        "latest_unknown_blocker": unknown_entries[-1] if unknown_entries else None,
        "counts_by_classification": {k: counts[k] for k in sorted(counts)},
        "proof_files": sorted_entries,
        "schema_versions": sorted(schema_versions),
        "validation_status": {
            "valid": not invalid,
            "invalid_files": invalid,
        },
    }
    return index, 1 if strict and invalid else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Index voice synthesis proof JSON bundles.")
    parser.add_argument("--dir", type=Path, default=Path("docs/reports/verification/runtime_proofs"))
    parser.add_argument("--output", type=Path, default=None)
    parser.add_argument("--json", action="store_true", dest="json_output")
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args(argv)

    index, rc = build_index(args.dir, strict=args.strict)
    rendered = json.dumps(index, sort_keys=True, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    if args.json_output or not args.output:
        print(rendered)
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
