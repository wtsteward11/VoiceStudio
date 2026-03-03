"""
Invariant I-3: Proof Artifact Completeness Gate.

Validates that every golden path proof artifact JSON in .buildlogs/proof_runs/
contains all required fields per Roadmap v2.0:
  - git_commit (40-char hex SHA)
  - timestamp (ISO 8601 UTC)
  - engine_mode ('real' or 'stub')
  - model_hashes (dict of model_name -> SHA-256)
  - output_file_hash (SHA-256 of exported audio)
  - output_duration_seconds (> 0 for 'real' mode)
  - output_energy_rms (> 0.001 for 'real' mode — catches silent audio)
  - all_steps_passed (boolean, must be true)

Roadmap v2.0 Phase 0 — Permanent CI invariant.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
PROOF_RUNS_DIR = PROJECT_ROOT / ".buildlogs" / "proof_runs"

REQUIRED_FIELDS = {
    "git_commit",
    "timestamp",
    "engine_mode",
    "model_hashes",
    "output_file_hash",
    "output_duration_seconds",
    "output_energy_rms",
    "all_steps_passed",
}

SHA256_PATTERN = re.compile(r"^[a-f0-9]{64}$")
GIT_SHA_PATTERN = re.compile(r"^[a-f0-9]{40}$")
ISO8601_PATTERN = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"
)


def _find_proof_artifacts() -> list[Path]:
    """Find all proof.json files under .buildlogs/proof_runs/."""
    if not PROOF_RUNS_DIR.exists():
        return []
    return list(PROOF_RUNS_DIR.rglob("proof.json"))


def validate_proof_artifact(proof_path: Path) -> list[str]:
    """Validate a single proof artifact. Returns list of error messages."""
    errors = []

    try:
        with open(proof_path) as f:
            data = json.load(f)
    except (json.JSONDecodeError, OSError) as e:
        return [f"Cannot read/parse {proof_path}: {e}"]

    missing = REQUIRED_FIELDS - set(data.keys())
    if missing:
        errors.append(f"Missing required fields: {missing}")
        return errors

    if not GIT_SHA_PATTERN.match(str(data.get("git_commit", ""))):
        errors.append(
            f"git_commit must be 40-char hex SHA, got: {data.get('git_commit')!r}"
        )

    if not ISO8601_PATTERN.match(str(data.get("timestamp", ""))):
        errors.append(
            f"timestamp must be ISO 8601 UTC, got: {data.get('timestamp')!r}"
        )

    engine_mode = data.get("engine_mode")
    if engine_mode not in ("real", "stub"):
        errors.append(
            f"engine_mode must be 'real' or 'stub', got: {engine_mode!r}"
        )

    model_hashes = data.get("model_hashes")
    if not isinstance(model_hashes, dict):
        errors.append(f"model_hashes must be a dict, got: {type(model_hashes).__name__}")
    elif model_hashes:
        for model_name, hash_val in model_hashes.items():
            if not SHA256_PATTERN.match(str(hash_val)):
                errors.append(
                    f"model_hashes[{model_name!r}] must be 64-char hex SHA-256, "
                    f"got: {str(hash_val)[:20]}..."
                )

    output_hash = data.get("output_file_hash", "")
    if not SHA256_PATTERN.match(str(output_hash)):
        errors.append(
            f"output_file_hash must be 64-char hex SHA-256, got: {str(output_hash)[:20]}..."
        )

    if data.get("all_steps_passed") is not True:
        errors.append(
            f"all_steps_passed must be true, got: {data.get('all_steps_passed')!r}"
        )

    if engine_mode == "real":
        duration = data.get("output_duration_seconds", 0)
        if not isinstance(duration, (int, float)) or duration <= 0:
            errors.append(
                f"output_duration_seconds must be > 0 for real mode, got: {duration}"
            )

        energy = data.get("output_energy_rms", 0)
        if not isinstance(energy, (int, float)) or energy <= 0.001:
            errors.append(
                f"output_energy_rms must be > 0.001 for real mode "
                f"(catches silent/empty audio), got: {energy}"
            )

    return errors


def test_proof_runs_directory_exists():
    """Assert .buildlogs/proof_runs/ exists (create if needed)."""
    PROOF_RUNS_DIR.mkdir(parents=True, exist_ok=True)
    assert PROOF_RUNS_DIR.exists(), f"{PROOF_RUNS_DIR} must exist"
    assert PROOF_RUNS_DIR.is_dir(), f"{PROOF_RUNS_DIR} must be a directory"


class TestProofArtifactSchema:
    """Validate golden path proof artifacts against I-3 schema."""

    def test_all_proof_artifacts_valid(self):
        """If any proof artifacts exist, they must all be valid."""
        artifacts = _find_proof_artifacts()
        if not artifacts:
            pytest.skip(
                "No proof artifacts found in .buildlogs/proof_runs/. "
                "Phase E will generate them."
            )

        all_errors = {}
        for artifact in artifacts:
            errors = validate_proof_artifact(artifact)
            if errors:
                all_errors[str(artifact.relative_to(PROJECT_ROOT))] = errors

        assert not all_errors, (
            f"Proof artifact validation failures ({len(all_errors)} artifacts):\n"
            + "\n".join(
                f"\n  {path}:\n" + "\n".join(f"    - {e}" for e in errs)
                for path, errs in all_errors.items()
            )
        )

    def test_proof_artifact_schema_is_defined(self):
        """Assert the required field set matches roadmap v2.0 spec."""
        expected = {
            "git_commit",
            "timestamp",
            "engine_mode",
            "model_hashes",
            "output_file_hash",
            "output_duration_seconds",
            "output_energy_rms",
            "all_steps_passed",
        }
        assert REQUIRED_FIELDS == expected, (
            f"REQUIRED_FIELDS drifted from roadmap spec. "
            f"Missing: {expected - REQUIRED_FIELDS}, Extra: {REQUIRED_FIELDS - expected}"
        )
