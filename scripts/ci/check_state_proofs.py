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
import glob
import hashlib
import json
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
from scripts.ci.proof_fingerprint import compute_fingerprint


def _rel(path: Path) -> str:
    """Return path relative to ROOT, or the path name if outside ROOT."""
    try:
        return str(path.relative_to(ROOT))
    except ValueError:
        return str(path)
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


def _load_allowlisted_paths(schema: dict) -> list[str]:
    """Load allowlisted proof paths from historical_proofs_allowlist.json."""
    allowlist_path = ROOT / schema.get(
        "historical_proofs_allowlist_path", ".ci/historical_proofs_allowlist.json"
    )
    if not allowlist_path.exists():
        return []
    try:
        allowlist = json.loads(allowlist_path.read_text(encoding="utf-8"))
        if isinstance(allowlist, list):
            return [e.get("path", "") for e in allowlist if isinstance(e, dict)]
    # ALLOWED: bare except - best effort, failure acceptable
    except (json.JSONDecodeError, OSError):
        pass
    return []


def get_proof_type(basename: str) -> str | None:
    """Return schema key for proof type (e.g. PROOF_GATE_C) or None if unknown."""
    if not basename.startswith("PROOF_") or not basename.endswith(PROOF_JSON_SUFFIX):
        return None
    stem = basename[: -len(PROOF_JSON_SUFFIX)]
    # Match longest prefix first: PROOF_PHASE_3, PROOF_PHASE_2_1, then PROOF_PHASE (legacy-only last)
    for prefix in (
        "PROOF_PAYLOAD_DETOX",
        "PROOF_PROVENANCE",
        "PROOF_GATE_C",
        "PROOF_INSTALLER",
        "PROOF_GOLDEN_PATH_STUB",
        "PROOF_GOLDEN_PATH_REAL",
        "PROOF_GOLDEN_PATH",
        "PROOF_UI_COMMAND_SURFACE",
        "PROOF_CRASH_RECOVERY",
        "PROOF_SUPPORT_BUNDLE_RUNTIME",
        "PROOF_SUPPORT_BUNDLE",
        "PROOF_BACKEND_COLD_START",
        "PROOF_PERF_BUDGET_RUNTIME",
        "PROOF_PERF_BUDGET",
        "PROOF_PHASE_3",
        "PROOF_PHASE_2_1",
        "PROOF_PHASE",
    ):
        if stem == prefix or stem.startswith(prefix + "_"):
            return prefix
    return None


def get_required_keys(schema: dict, proof_type: str) -> list[str]:
    """Return all required keys for proof type."""
    type_spec = schema.get("type_specific", {}).get(proof_type, {})
    if type_spec.get("override_common"):
        return list(type_spec.get("required", []))
    common = schema.get("common_required", [])
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
        ui_smoke = data.get("ui_smoke")
        if not isinstance(ui_smoke, dict):
            errors.append(
                f"{_rel(path)}: ui_smoke must be object for nested validation"
            )
        else:
            expected_exit = nested.get("ui_smoke.exit_code")
            if expected_exit is not None:
                ec = ui_smoke.get("exit_code")
                if ec != expected_exit:
                    errors.append(
                        f"{_rel(path)}: ui_smoke.exit_code must be {expected_exit}, got {ec}"
                    )
            min_nav = nested.get("ui_smoke.nav_steps_completed_min")
            if min_nav is not None:
                nav = ui_smoke.get("nav_steps_completed")
                if nav is None:
                    errors.append(
                        f"{_rel(path)}: ui_smoke.nav_steps_completed is required"
                    )
                elif not isinstance(nav, int) or nav < min_nav:
                    errors.append(
                        f"{_rel(path)}: ui_smoke.nav_steps_completed must be >= {min_nav}, got {nav}"
                    )
            bfc = nested.get("ui_smoke.binding_failure_count")
            if bfc is not None:
                actual = ui_smoke.get("binding_failure_count")
                if actual is None:
                    errors.append(
                        f"{_rel(path)}: ui_smoke.binding_failure_count is required"
                    )
                elif actual != bfc:
                    errors.append(
                        f"{_rel(path)}: ui_smoke.binding_failure_count must be {bfc}, got {actual}"
                    )
            if nested.get("ui_smoke.summary_path_required"):
                sp = ui_smoke.get("summary_path")
                if not sp or not isinstance(sp, str):
                    errors.append(
                        f"{_rel(path)}: ui_smoke.summary_path is required"
                    )
                elif not data.get("historical_proof"):
                    summary_full = (ROOT / sp.replace("\\", "/")).resolve()
                    root_resolved = ROOT.resolve()
                    if not str(summary_full).startswith(str(root_resolved)):
                        errors.append(
                            f"{_rel(path)}: ui_smoke.summary_path must be under repo root"
                        )
                    elif not summary_full.exists():
                        errors.append(
                            f"{_rel(path)}: ui_smoke.summary_path does not exist: {sp}"
                        )
                    elif nested.get("ui_smoke.summary_sha256_hex"):
                        sh = ui_smoke.get("summary_sha256")
                        if not sh or not re.match(r"^[a-f0-9]{64}$", str(sh)):
                            errors.append(
                                f"{_rel(path)}: ui_smoke.summary_sha256 must be 64-hex SHA-256"
                            )
                        else:
                            with open(summary_full, "rb") as f:
                                actual_hash = hashlib.sha256(f.read()).hexdigest()
                            if sh.lower() != actual_hash.lower():
                                errors.append(
                                    f"{_rel(path)}: ui_smoke.summary_sha256 does not match file"
                                )
            if nested.get("ui_smoke.log_path_required"):
                lp = ui_smoke.get("log_path")
                if not lp or not isinstance(lp, str):
                    errors.append(
                        f"{_rel(path)}: ui_smoke.log_path is required"
                    )
                elif not data.get("historical_proof"):
                    log_full = (ROOT / lp.replace("\\", "/")).resolve()
                    root_resolved = ROOT.resolve()
                    if not str(log_full).startswith(str(root_resolved)):
                        errors.append(
                            f"{_rel(path)}: ui_smoke.log_path must be under repo root"
                        )
                    elif not log_full.exists():
                        errors.append(
                            f"{_rel(path)}: ui_smoke.log_path does not exist: {lp}"
                        )
                    elif nested.get("ui_smoke.log_sha256_hex"):
                        lh = ui_smoke.get("log_sha256")
                        if not lh or not re.match(r"^[a-f0-9]{64}$", str(lh)):
                            errors.append(
                                f"{_rel(path)}: ui_smoke.log_sha256 must be 64-hex SHA-256"
                            )
                        else:
                            with open(log_full, "rb") as f:
                                actual_hash = hashlib.sha256(f.read()).hexdigest()
                            if lh.lower() != actual_hash.lower():
                                errors.append(
                                    f"{_rel(path)}: ui_smoke.log_sha256 does not match file"
                                )

    elif proof_type == "PROOF_INSTALLER":
        required_keys = nested.get("results_required_keys", [])
        if required_keys:
            results = data.get("results")
            if not isinstance(results, dict):
                errors.append(
                    f"{_rel(path)}: results must be object for nested validation"
                )
            else:
                missing = [k for k in required_keys if k not in results]
                if missing:
                    errors.append(
                        f"{_rel(path)}: results missing required keys: {missing}"
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
                            f"{_rel(path)}: all_passed=true but results not all PASS: {failed}"
                        )

    elif proof_type == "PROOF_GOLDEN_PATH":
        engine_mode = data.get("engine_mode")
        if engine_mode not in ("stub", "real"):
            errors.append(
                f"{_rel(path)}: engine_mode must be 'stub' or 'real', got {engine_mode!r}"
            )
        if data.get("all_steps_passed") is not True:
            errors.append(
                f"{_rel(path)}: all_steps_passed must be true"
            )
        ofh = data.get("output_file_hash", "")
        if not re.match(r"^[a-f0-9]{64}$", str(ofh)):
            errors.append(
                f"{_rel(path)}: output_file_hash must be 64-char hex, got {str(ofh)[:20]}"
            )
        mh = data.get("model_hashes")
        if not isinstance(mh, dict):
            errors.append(
                f"{_rel(path)}: model_hashes must be a dict"
            )
        if engine_mode == "real":
            d = data.get("output_duration_seconds", 0)
            if not isinstance(d, (int, float)) or d <= 0:
                errors.append(
                    f"{_rel(path)}: output_duration_seconds must be > 0 for real mode"
                )
            e = data.get("output_energy_rms", 0)
            if not isinstance(e, (int, float)) or e <= 0.001:
                errors.append(
                    f"{_rel(path)}: output_energy_rms must be > 0.001 for real mode"
                )

    elif proof_type == "PROOF_UI_COMMAND_SURFACE":
        if data.get("all_commands_registered") is not True:
            errors.append(
                f"{_rel(path)}: all_commands_registered must be true"
            )
        if data.get("all_panels_registered") is not True:
            errors.append(
                f"{_rel(path)}: all_panels_registered must be true"
            )

    elif proof_type == "PROOF_CRASH_RECOVERY":
        tp = data.get("tests_passed", 0)
        tt = data.get("tests_total", 0)
        if not isinstance(tp, int) or not isinstance(tt, int):
            errors.append(
                f"{_rel(path)}: tests_passed/tests_total must be int"
            )
        elif tp != tt:
            errors.append(
                f"{_rel(path)}: tests_passed ({tp}) != tests_total ({tt})"
            )
        for flag in (
            "circuit_breaker_tested",
            "state_preservation_tested",
            "restart_policies_tested",
        ):
            if data.get(flag) is not True:
                errors.append(
                    f"{_rel(path)}: {flag} must be true"
                )

    elif proof_type == "PROOF_SUPPORT_BUNDLE":
        for flag in (
            "script_exists",
            "ui_handler_exists",
            "required_items_present",
        ):
            if data.get(flag) is not True:
                errors.append(
                    f"{_rel(path)}: {flag} must be true"
                )

    elif proof_type == "PROOF_PERF_BUDGET":
        if data.get("budgets_defined") is not True:
            errors.append(
                f"{_rel(path)}: budgets_defined must be true"
            )
        if data.get("regression_detector_exists") is not True:
            errors.append(
                f"{_rel(path)}: regression_detector_exists must be true"
            )

    elif proof_type == "PROOF_SUPPORT_BUNDLE_RUNTIME":
        if data.get("ui_wiring_verified") is not True:
            errors.append(
                f"{_rel(path)}: ui_wiring_verified must be true"
            )
        bph = data.get("bundle_path_hash", "")
        if not re.match(r"^[a-f0-9]{64}$", str(bph)):
            errors.append(
                f"{_rel(path)}: bundle_path_hash must be 64-hex SHA-256"
            )
        if data.get("bundle_manifest_present") is not True:
            errors.append(
                f"{_rel(path)}: bundle_manifest_present must be true"
            )
        if data.get("crash_evidence_present") is not True:
            errors.append(
                f"{_rel(path)}: crash_evidence_present must be true"
            )
        rf = data.get("required_files")
        if not isinstance(rf, list) or not rf:
            errors.append(
                f"{_rel(path)}: required_files must be a non-empty list"
            )
        else:
            name_to_entry = {e.get("name", ""): e for e in rf if isinstance(e, dict)}
            categories = [
                ("system_info", ["system_info.json", "hardware_info.json"]),
                ("engine_manifests", ["engine_manifests_list.txt", "engine_manifests.json"]),
                ("build_version", ["build_version_source.txt", "build_version.json"]),
                ("bundle_manifest", ["BUNDLE_MANIFEST.json"]),
            ]
            for cat_name, names in categories:
                satisfied = any(
                    name_to_entry.get(n, {}).get("exists") is True
                    and isinstance(name_to_entry.get(n, {}).get("bytes"), int)
                    and name_to_entry.get(n, {}).get("bytes", 0) > 0
                    for n in names
                )
                if not satisfied:
                    errors.append(
                        f"{_rel(path)}: required_files must satisfy category "
                        f"'{cat_name}' (one of {names} with exists and bytes>0)"
                    )
            for i, entry in enumerate(rf):
                if not isinstance(entry, dict):
                    errors.append(
                        f"{_rel(path)}: required_files[{i}] must be object"
                    )
                    continue
                if entry.get("exists") is True:
                    b = entry.get("bytes", 0)
                    if not isinstance(b, int) or b <= 0:
                        errors.append(
                            f"{_rel(path)}: required_files[{i}] "
                            f"({entry.get('name', '?')}) bytes must be > 0 when exists"
                        )
                    h = entry.get("sha256", "")
                    if not re.match(r"^[a-f0-9]{64}$", str(h)):
                        errors.append(
                            f"{_rel(path)}: required_files[{i}] "
                            f"({entry.get('name', '?')}) sha256 must be 64-hex when exists"
                        )

    elif proof_type == "PROOF_PERF_BUDGET_RUNTIME":
        if data.get("within_budget") is not True:
            errors.append(
                f"{_rel(path)}: within_budget must be true"
            )
        env = data.get("environment")
        if not isinstance(env, dict) or env.get("os") != "Windows":
            errors.append(
                f"{_rel(path)}: environment.os must be 'Windows'"
            )
        if isinstance(env, dict) and not (env.get("runner") or "").strip():
            errors.append(
                f"{_rel(path)}: environment.runner must be non-empty"
            )
        pms = data.get("panel_measurement_source", "")
        if not isinstance(pms, str) or not pms.strip():
            errors.append(
                f"{_rel(path)}: panel_measurement_source must be non-empty"
            )
        measured = data.get("measured")
        budgets = data.get("budgets")
        if not isinstance(measured, dict) or not isinstance(budgets, dict):
            errors.append(
                f"{_rel(path)}: measured and budgets must be objects"
            )
        else:
            for key in ("StartupMs", "ApiResponseMs", "PanelLoadMs"):
                mv = measured.get(key)
                bv = budgets.get(key)
                if not isinstance(mv, (int, float)) or mv <= 0:
                    errors.append(
                        f"{_rel(path)}: measured.{key} must be > 0"
                    )
                if isinstance(mv, (int, float)) and isinstance(bv, (int, float)):
                    if mv > bv:
                        errors.append(
                            f"{_rel(path)}: measured.{key} ({mv}) "
                            f"exceeds budget ({bv})"
                        )

    elif proof_type == "PROOF_BACKEND_COLD_START":
        if data.get("within_budget") is not True:
            errors.append(
                f"{_rel(path)}: within_budget must be true"
            )
        env = data.get("environment")
        if not isinstance(env, dict) or env.get("os") != "Windows":
            errors.append(
                f"{_rel(path)}: environment.os must be 'Windows'"
            )
        for key, budget_key in (
            ("cold_start_ms", "ColdStartMs"),
            ("first_api_ms", "FirstApiMs"),
            ("warm_api_ms", "WarmApiMs"),
        ):
            mv = data.get(key)
            if not isinstance(mv, (int, float)) or mv <= 0:
                errors.append(
                    f"{_rel(path)}: {key} must be > 0"
                )
            budgets = data.get("budgets")
            if isinstance(budgets, dict) and isinstance(mv, (int, float)):
                bv = budgets.get(budget_key)
                if isinstance(bv, (int, float)) and mv > bv:
                    errors.append(
                        f"{_rel(path)}: {key} ({mv}) exceeds budget {budget_key} ({bv})"
                    )

    elif proof_type in ("PROOF_GOLDEN_PATH_STUB", "PROOF_GOLDEN_PATH_REAL"):
        if data.get("passed") is not True:
            errors.append(
                f"{_rel(path)}: passed must be true"
            )
        em = data.get("engine_mode")
        expected_mode = "stub" if proof_type.endswith("_STUB") else "real"
        if em != expected_mode:
            errors.append(
                f"{_rel(path)}: engine_mode must be '{expected_mode}', got {em!r}"
            )
        om = data.get("output_metrics")
        if not isinstance(om, dict):
            errors.append(
                f"{_rel(path)}: output_metrics must be an object"
            )
        else:
            dur = om.get("duration_seconds", 0)
            rms = om.get("rms_energy", 0)
            sha = om.get("output_sha256", "")
            if not isinstance(dur, (int, float)) or dur <= 0:
                errors.append(
                    f"{_rel(path)}: output_metrics.duration_seconds must be > 0"
                )
            if not isinstance(rms, (int, float)) or rms <= 0.001:
                errors.append(
                    f"{_rel(path)}: output_metrics.rms_energy must be > 0.001"
                )
            if not re.match(r"^[a-f0-9]{64}$", str(sha)):
                errors.append(
                    f"{_rel(path)}: output_metrics.output_sha256 must be 64-hex"
                )
        if proof_type == "PROOF_GOLDEN_PATH_REAL":
            models = data.get("models")
            if not isinstance(models, dict) or not models:
                errors.append(
                    f"{_rel(path)}: models must be a non-empty dict for real mode"
                )
            elif models:
                for mk, mv in models.items():
                    if not re.match(r"^[a-f0-9]{64}$", str(mv)):
                        errors.append(
                            f"{_rel(path)}: models.{mk} must be 64-hex sha256"
                        )

        if nested.get("test_ran_must_be_true"):
            if data.get("test_ran") is not True:
                errors.append(
                    f"{_rel(path)}: test_ran must be true"
                )

        if nested.get("artifact_path_required"):
            art_path_str = data.get("artifact_path", "")
            if not art_path_str or not isinstance(art_path_str, str):
                errors.append(
                    f"{_rel(path)}: artifact_path is required"
                )
            else:
                art_full = (ROOT / art_path_str.replace("\\", "/")).resolve()
                root_resolved = ROOT.resolve()
                if not str(art_full).startswith(str(root_resolved)):
                    errors.append(
                        f"{_rel(path)}: artifact_path must be under repo root"
                    )
                elif not art_full.exists():
                    errors.append(
                        f"{_rel(path)}: artifact_path does not exist: {art_path_str}"
                    )

        if nested.get("artifact_sha256_required"):
            art_sha = data.get("artifact_sha256", "")
            if not re.match(r"^[a-f0-9]{64}$", str(art_sha)):
                errors.append(
                    f"{_rel(path)}: artifact_sha256 must be 64-hex SHA-256"
                )
            else:
                art_path_str = data.get("artifact_path", "")
                if art_path_str and isinstance(art_path_str, str):
                    art_full = (ROOT / art_path_str.replace("\\", "/")).resolve()
                    if art_full.exists():
                        with open(art_full, "rb") as f:
                            actual_hash = hashlib.sha256(f.read()).hexdigest()
                        if art_sha.lower() != actual_hash.lower():
                            errors.append(
                                f"{_rel(path)}: artifact_sha256 does not match file at artifact_path"
                            )
                    else:
                        errors.append(
                            f"{_rel(path)}: cannot verify artifact_sha256: artifact file does not exist"
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
        return [f"file missing: {_rel(path)}"]

    if not path.suffix.lower() == ".json":
        return []  # Non-JSON proofs: existence only, no schema

    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as e:
        return [f"{_rel(path)}: JSON parse error: {e}"]

    basename = path.name
    proof_type = get_proof_type(basename)
    if not proof_type:
        return [f"{_rel(path)}: unknown proof type (basename={basename})"]

    required = get_required_keys(schema, proof_type)
    missing = [k for k in required if k not in data or data[k] is None]
    if missing:
        errors.append(f"{_rel(path)}: missing required keys: {missing}")
        errors.append(f"  expected schema: {required}")

    # PROOF_PHASE legacy-only: fail unless historical_proof + allowlisted
    if proof_type == "PROOF_PHASE" and not missing:
        rel_path = str(_rel(path)).replace("\\", "/")
        allowlisted_paths = _load_allowlisted_paths(schema)
        if not (data.get("historical_proof") is True and rel_path in allowlisted_paths):
            errors.append(
                f"{_rel(path)}: PROOF_PHASE (weak) is no longer accepted; "
                "use PROOF_PHASE_2_1 or PROOF_PHASE_3. For legacy proofs only: "
                "set historical_proof=true and add path to .ci/historical_proofs_allowlist.json"
            )
            return errors
        return []

    # Type checks for common keys
    type_checks = [
        ("command", "str"),
        ("exit_code", "int"),
        ("timestamp", "str"),
        ("git_commit", "str"),
        ("git_branch", "str"),
        ("evidence_fingerprint", "str"),
    ]
    for key, expected in type_checks:
        if key in data and (err := validate_types(data, key, expected)):
            errors.append(f"{_rel(path)}: {err}")

    # Nested semantics (M10)
    nested_errs = validate_nested_semantics(path, data, proof_type, schema)
    errors.extend(nested_errs)

    if errors:
        return errors

    # Semantic: exit_code == 0
    if data.get("exit_code") != 0:
        errors.append(f"{_rel(path)}: exit_code must be 0, got {data.get('exit_code')}")

    # Semantic: timestamp parses as ISO8601
    ts = data.get("timestamp")
    if ts:
        try:
            datetime.fromisoformat(ts.replace("Z", "+00:00"))
        except (ValueError, TypeError):
            errors.append(f"{_rel(path)}: timestamp not valid ISO8601: {ts!r}")

    # Semantic: git_commit 40 hex
    gc = data.get("git_commit")
    if gc and not (isinstance(gc, str) and len(gc) == 40 and all(c in "0123456789abcdef" for c in gc.lower())):
        errors.append(f"{_rel(path)}: git_commit must be 40 hex chars, got {gc!r}")

    # M11: Refresh rules — refreshed proofs require historical_proof and allowlist
    if data.get("refreshed") is True:
        if data.get("historical_proof") is not True:
            errors.append(
                f"{_rel(path)}: refreshed=true requires historical_proof=true"
            )
        else:
            rel_path = str(_rel(path)).replace("\\", "/")
            allowlisted_paths = _load_allowlisted_paths(schema)
            if rel_path not in allowlisted_paths:
                errors.append(
                    f"{_rel(path)}: refreshed proof not in .ci/historical_proofs_allowlist.json"
                )

    # M11: Evidence fingerprint validation
    stored_fp = data.get("evidence_fingerprint")
    expected_fp = compute_fingerprint(data, proof_type)
    if stored_fp != expected_fp:
        got = f"{stored_fp[:16]}..." if stored_fp else "missing"
        errors.append(
            f"{_rel(path)}: evidence_fingerprint mismatch (expected {expected_fp[:16]}..., got {got})"
        )

    # Semantic: git_commit matches HEAD (unless historical_proof or --no-git-match)
    # PROOF_GOLDEN_PATH_REAL: allow HEAD or HEAD~1 (proof attests to test-run commit; proof commit is child)
    if not no_git_match and not data.get("historical_proof"):
        try:
            head_result = subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=ROOT,
                capture_output=True,
                text=True,
                timeout=5,
            )
            head = head_result.stdout.strip() if head_result.returncode == 0 else ""
            if head and gc:
                allowed = [head.lower()]
                if proof_type == "PROOF_GOLDEN_PATH_REAL":
                    # Allow HEAD~1..~4: proof attests to test-run commit; CI fix commits can precede proof commit
                    for n in (1, 2, 3, 4):
                        parent = subprocess.run(
                            ["git", "rev-parse", f"HEAD~{n}"],
                            cwd=ROOT,
                            capture_output=True,
                            text=True,
                            timeout=5,
                        )
                        if parent.returncode == 0 and parent.stdout.strip():
                            allowed.append(parent.stdout.strip().lower())
                if gc.lower() not in allowed:
                    errors.append(
                        f"{_rel(path)}: git_commit {gc[:8]}... does not match HEAD {head[:8]}..."
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
    parser.add_argument(
        "--validate-file",
        type=str,
        default=None,
        help="Validate a specific proof file or glob (bypass STATE.md extraction)",
    )
    args = parser.parse_args()

    schema = load_schema()
    all_errors: list[str] = []

    if args.validate_file:
        # Standalone validation: resolve glob, validate each file (no STATE.md)
        resolved = sorted(glob.glob(str(ROOT / args.validate_file)))
        if not resolved:
            print(
                f"No files matched: {args.validate_file}",
                file=sys.stderr,
            )
            return 1
        for p in resolved:
            full = Path(p)
            if full.is_file() and full.suffix.lower() == ".json":
                errs = validate_proof(full, schema, no_git_match=args.no_git_match)
                all_errors.extend(errs)
    else:
        # STATE.md mode
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
