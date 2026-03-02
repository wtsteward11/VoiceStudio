#!/usr/bin/env python3
"""
CI check: fail if STATE.md claims DONE but proof files are missing or invalid.

Parses .cursor/STATE.md, extracts Proof: paths, validates schema and semantics.
Exit 0 if all proof files present and valid; 1 if any missing or invalid.

Schema: .ci/proof_schema.json
Required: command, exit_code, timestamp, git_commit, git_branch + type-specific keys.
Semantic: exit_code==0, timestamp ISO8601, git_commit 40-hex, git_commit matches HEAD (unless historical_proof).

Usage:
  python scripts/ci/check_state_proofs.py           # CI mode (strict git match)
  python scripts/ci/check_state_proofs.py --no-git-match  # Local dev (skip git commit match)
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
STATE_PATH = ROOT / ".cursor" / "STATE.md"
SCHEMA_PATH = ROOT / ".ci" / "proof_schema.json"

# Pattern: Proof: `path` or Proof: path (json, md, txt)
PROOF_PATTERN = re.compile(
    r"Proof:\s*[`\"]?([a-zA-Z0-9_./\\-]+\.(?:json|md|txt))[`\"]?",
    re.IGNORECASE,
)

# Only validate .json proof files for schema; .md/.txt are existence-only
PROOF_JSON_SUFFIX = ".json"


def extract_proof_paths(content: str) -> list[str]:
    """Extract proof file paths from entire STATE.md content. Deduplicated, normalized."""
    paths: list[str] = []
    for line in content.splitlines():
        match = PROOF_PATTERN.search(line)
        if match:
            path = match.group(1).replace("\\", "/")
            if path not in paths:
                paths.append(path)
    return paths


def filter_canonical_proof_paths(paths: list[str]) -> list[str]:
    """Keep only paths that are canonical proof JSONs (docs/reports/verification/PROOF_*.json)."""
    canonical: list[str] = []
    for p in paths:
        norm = p.replace("\\", "/")
        if norm.startswith("docs/reports/verification/PROOF_") and norm.endswith(".json"):
            if norm not in canonical:
                canonical.append(norm)
    return canonical


def load_schema() -> dict:
    """Load proof schema from .ci/proof_schema.json."""
    if not SCHEMA_PATH.exists():
        print(f"Schema not found: {SCHEMA_PATH}", file=sys.stderr)
        sys.exit(1)
    return json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))


def get_proof_type(basename: str) -> str | None:
    """Return schema key for proof type (e.g. PROOF_GATE_C) or None if unknown."""
    if not basename.startswith("PROOF_") or not basename.endswith(PROOF_JSON_SUFFIX):
        return None
    stem = basename[: -len(PROOF_JSON_SUFFIX)]
    # Match longest prefix: PROOF_PROVENANCE, PROOF_GATE_C, PROOF_INSTALLER, PROOF_PAYLOAD_DETOX
    for prefix in ("PROOF_PAYLOAD_DETOX", "PROOF_PROVENANCE", "PROOF_GATE_C", "PROOF_INSTALLER"):
        if stem == prefix or stem.startswith(prefix + "_"):
            return prefix
    return None


def get_required_keys(schema: dict, proof_type: str) -> list[str]:
    """Return all required keys for proof type."""
    common = schema.get("common_required", [])
    type_spec = schema.get("type_specific", {}).get(proof_type, {})
    extra = type_spec.get("required", [])
    return list(common) + list(extra)


def validate_nested_semantics(
    path: Path,
    data: dict,
    proof_type: str,
    schema: dict,
) -> list[str]:
    """
    Validate nested semantics per proof type. Return list of error messages (empty if valid).
    """
    errors: list[str] = []
    nested = schema.get("nested_semantics", {}).get(proof_type)
    if not nested:
        return []

    if proof_type == "PROOF_GATE_C":
        expected_exit = nested.get("ui_smoke.exit_code")
        if expected_exit is not None:
            ui_smoke = data.get("ui_smoke")
            if not isinstance(ui_smoke, dict):
                errors.append(
                    f"{path.relative_to(ROOT)}: ui_smoke must be object for nested validation"
                )
            else:
                ec = ui_smoke.get("exit_code")
                if ec != expected_exit:
                    errors.append(
                        f"{path.relative_to(ROOT)}: ui_smoke.exit_code must be {expected_exit}, got {ec}"
                    )

    elif proof_type == "PROOF_INSTALLER":
        required_keys = nested.get("results_required_keys", [])
        if required_keys:
            results = data.get("results")
            if not isinstance(results, dict):
                errors.append(
                    f"{path.relative_to(ROOT)}: results must be object for nested validation"
                )
            else:
                missing = [k for k in required_keys if k not in results]
                if missing:
                    errors.append(
                        f"{path.relative_to(ROOT)}: results missing required keys: {missing}"
                    )
                elif nested.get("results_all_pass_when_all_passed_true") and data.get(
                    "all_passed"
                ):
                    failed = [
                        k for k, v in results.items()
                        if v != "PASS"
                    ]
                    if failed:
                        errors.append(
                            f"{path.relative_to(ROOT)}: all_passed=true but results not all PASS: {failed}"
                        )

    return errors


def validate_types(data: dict, key: str, expected: str) -> str | None:
    """Return error message if type mismatch, else None."""
    val = data.get(key)
    if val is None:
        return f"missing key: {key}"
    if expected == "str" and not isinstance(val, str):
        return f"{key}: expected str, got {type(val).__name__}"
    if expected == "int" and not isinstance(val, int):
        return f"{key}: expected int, got {type(val).__name__}"
    if expected == "bool" and not isinstance(val, bool):
        return f"{key}: expected bool, got {type(val).__name__}"
    if expected == "object" and not isinstance(val, dict):
        return f"{key}: expected object, got {type(val).__name__}"
    return None


def validate_proof(
    path: Path,
    schema: dict,
    no_git_match: bool,
) -> list[str]:
    """
    Validate a single proof JSON file. Return list of error messages (empty if valid).
    """
    errors: list[str] = []

    if not path.exists():
        return [f"file missing: {path.relative_to(ROOT)}"]

    if not path.suffix.lower() == ".json":
        return []  # Non-JSON proofs: existence only, no schema

    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as e:
        return [f"{path.relative_to(ROOT)}: JSON parse error: {e}"]

    basename = path.name
    proof_type = get_proof_type(basename)
    if not proof_type:
        return [f"{path.relative_to(ROOT)}: unknown proof type (basename={basename})"]

    required = get_required_keys(schema, proof_type)
    missing = [k for k in required if k not in data or data[k] is None]
    if missing:
        errors.append(f"{path.relative_to(ROOT)}: missing required keys: {missing}")
        errors.append(f"  expected schema: {required}")

    # Type checks for common keys
    type_checks = [
        ("command", "str"),
        ("exit_code", "int"),
        ("timestamp", "str"),
        ("git_commit", "str"),
        ("git_branch", "str"),
    ]
    for key, expected in type_checks:
        if key in data and (err := validate_types(data, key, expected)):
            errors.append(f"{path.relative_to(ROOT)}: {err}")

    # Nested semantics (M10)
    nested_errs = validate_nested_semantics(path, data, proof_type, schema)
    errors.extend(nested_errs)

    if errors:
        return errors

    # Semantic: exit_code == 0
    if data.get("exit_code") != 0:
        errors.append(f"{path.relative_to(ROOT)}: exit_code must be 0, got {data.get('exit_code')}")

    # Semantic: timestamp parses as ISO8601
    ts = data.get("timestamp")
    if ts:
        try:
            datetime.fromisoformat(ts.replace("Z", "+00:00"))
        except (ValueError, TypeError):
            errors.append(f"{path.relative_to(ROOT)}: timestamp not valid ISO8601: {ts!r}")

    # Semantic: git_commit 40 hex
    gc = data.get("git_commit")
    if gc and not (isinstance(gc, str) and len(gc) == 40 and all(c in "0123456789abcdef" for c in gc.lower())):
        errors.append(f"{path.relative_to(ROOT)}: git_commit must be 40 hex chars, got {gc!r}")

    # Semantic: git_commit matches HEAD (unless historical_proof or --no-git-match)
    if not no_git_match and not data.get("historical_proof"):
        try:
            result = subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=ROOT,
                capture_output=True,
                text=True,
                timeout=5,
            )
            head = result.stdout.strip() if result.returncode == 0 else ""
            if head and gc and gc.lower() != head.lower():
                errors.append(
                    f"{path.relative_to(ROOT)}: git_commit {gc[:8]}... does not match HEAD {head[:8]}..."
                )
        except (subprocess.TimeoutExpired, FileNotFoundError):
            pass  # Skip if git unavailable

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate STATE.md proof files against schema.")
    parser.add_argument(
        "--no-git-match",
        action="store_true",
        help="Skip git_commit vs HEAD check (for local dev after regeneration)",
    )
    args = parser.parse_args()

    if not STATE_PATH.exists():
        print(f"STATE.md not found: {STATE_PATH}", file=sys.stderr)
        return 1

    content = STATE_PATH.read_text(encoding="utf-8")
    all_paths = extract_proof_paths(content)
    paths = filter_canonical_proof_paths(all_paths)

    if not paths:
        print(
            "No canonical Proof paths (docs/reports/verification/PROOF_*.json) found in STATE.md.",
            file=sys.stderr,
        )
        return 1

    schema = load_schema()
    all_errors: list[str] = []

    for p in paths:
        full = ROOT / p
        errs = validate_proof(full, schema, no_git_match=args.no_git_match)
        all_errors.extend(errs)

    if all_errors:
        print("PROOF VALIDATION FAILED:", file=sys.stderr)
        for e in all_errors:
            print(f"  {e}", file=sys.stderr)
        print(
            "\nRegenerate proofs per docs/reports/verification/PROOF_INDEX_NEXT3_2026-03-02.md",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
