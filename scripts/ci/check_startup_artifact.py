#!/usr/bin/env python3
"""
Canonical validator for %LocalAppData%\\VoiceStudio\\crashes\\startup_decision.json (schema v2).

Written by BackendProcessManager.WriteStartupArtifact (see src/VoiceStudio.App/Services/BackendProcessManager.cs).

Exit codes:
  0 — Artifact valid for success path, or failure path without hard-fail decisions; advisory timing only warns.
  1 — Structural or operational regression (see regression table in lane docs).

Advisory timing budgets (ms) do NOT change exit code — warnings go to stderr; summary JSON to stdout.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# ---------------------------------------------------------------------------
# Constants — advisory vs hard fail is documented in execution row / closure report.
# ---------------------------------------------------------------------------
REQUIRED_SCHEMA_VERSION = 2

# Operational class: failure status with these decisions is a hard regression for this guard.
HARD_FAIL_DECISIONS = frozenset(
    {
        "health_timeout",
        "spawn_failure",
        "app_root_invalid",
        "runtime_missing",
    }
)

# Advisory only — slow machines must not fail CI on these breaches.
ADVISORY_HEALTHY_ELAPSED_MS = 45_000
ADVISORY_SPAWN_ELAPSED_MS = 10_000

# Keys that must exist on the parsed object (values may be JSON null).
# Matches BackendProcessManager payload shape (schema v2).
REQUIRED_KEYS = frozenset(
    {
        "schema_version",
        "status",
        "timestamp_utc",
        "decision",
        "health_probe_result",
        "port_occupied",
        "backend_pid",
        "spawn_attempted",
        "reused_existing_backend",
        "conflict_category",
        "timeout_seconds",
        "elapsed_ms",
        "spawn_elapsed_ms",
        "health_attempts",
        "healthy_elapsed_ms",
        "last_stderr_lines",
        "python_path_resolved",
    }
)


def default_artifact_path() -> Path:
    """Windows: %LOCALAPPDATA%\\VoiceStudio\\crashes\\startup_decision.json."""
    la = os.environ.get("LOCALAPPDATA", "").strip()
    if la:
        return Path(la) / "VoiceStudio" / "crashes" / "startup_decision.json"
    # Non-Windows dev: use XDG-style path for discoverability (tests should use --path).
    return Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))) / (
        "VoiceStudio"
    ) / "crashes" / "startup_decision.json"


@dataclass
class CheckResult:
    """Result of validating one artifact file."""

    passed: bool
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    path: str = ""


def _timing_warnings(data: dict[str, Any]) -> list[str]:
    out: list[str] = []
    hem = data.get("healthy_elapsed_ms")
    if isinstance(hem, (int, float)) and hem > ADVISORY_HEALTHY_ELAPSED_MS:
        out.append(
            f"Advisory: healthy_elapsed_ms={hem} exceeds advisory budget "
            f"{ADVISORY_HEALTHY_ELAPSED_MS} ms (does not fail check)",
        )
    sem = data.get("spawn_elapsed_ms")
    if isinstance(sem, (int, float)) and sem > ADVISORY_SPAWN_ELAPSED_MS:
        out.append(
            f"Advisory: spawn_elapsed_ms={sem} exceeds advisory budget "
            f"{ADVISORY_SPAWN_ELAPSED_MS} ms (does not fail check)",
        )
    return out


def _structure_and_ops_errors(data: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    missing = REQUIRED_KEYS - data.keys()
    if missing:
        errors.append(f"Missing required key(s): {sorted(missing)}")

    sv = data.get("schema_version")
    if sv is not None and sv != REQUIRED_SCHEMA_VERSION:
        errors.append(
            f"schema_version must be {REQUIRED_SCHEMA_VERSION}, got {sv!r}",
        )

    status = data.get("status")
    if status is not None and status not in ("success", "failure"):
        errors.append(f"status must be 'success' or 'failure', got {status!r}")

    decision = data.get("decision")
    if decision is not None and not isinstance(decision, str):
        errors.append(f"decision must be a string, got {type(decision).__name__}")

    if "last_stderr_lines" in data and not isinstance(data["last_stderr_lines"], list):
        errors.append("last_stderr_lines must be a JSON array")

    if status == "failure" and isinstance(decision, str) and decision in HARD_FAIL_DECISIONS:
        errors.append(f"Operational regression: status=failure with decision={decision!r}")

    if status == "success" and data.get("health_probe_result") is False:
        errors.append("Logic contradiction: status=success but health_probe_result=false")

    return errors


def check_artifact(path: Path) -> CheckResult:
    """Validate ``startup_decision.json`` at ``path``."""

    path_str = str(path.resolve())

    if not path.is_file():
        return CheckResult(
            passed=False,
            errors=[f"Startup artifact missing or not a file: {path_str}"],
            warnings=[],
            path=path_str,
        )

    try:
        raw_text = path.read_text(encoding="utf-8")
    except OSError as exc:
        return CheckResult(
            passed=False,
            errors=[f"Cannot read artifact: {path_str}: {exc}"],
            warnings=[],
            path=path_str,
        )

    try:
        data: Any = json.loads(raw_text)
    except json.JSONDecodeError as exc:
        return CheckResult(
            passed=False,
            errors=[f"Invalid JSON: {exc}"],
            warnings=[],
            path=path_str,
        )

    if not isinstance(data, dict):
        return CheckResult(
            passed=False,
            errors=["Root JSON value must be an object"],
            warnings=[],
            path=path_str,
        )

    body = data
    errors = _structure_and_ops_errors(body)
    warnings = _timing_warnings(body)
    passed = len(errors) == 0
    return CheckResult(passed=passed, errors=errors, warnings=warnings, path=path_str)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--path",
        type=Path,
        help="Override artifact path (default: VoiceStudio crashes dir / startup_decision.json)",
    )
    args = parser.parse_args()
    target = args.path if args.path is not None else default_artifact_path()

    result = check_artifact(target)
    payload = {
        "passed": result.passed,
        "path": result.path,
        "errors": result.errors,
        "warnings": result.warnings,
    }
    print(json.dumps(payload, indent=2))
    for w in result.warnings:
        print(w, file=sys.stderr)
    return 0 if result.passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
