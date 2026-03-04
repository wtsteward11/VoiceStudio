# Proof Schema — Canonical Definition

**Purpose:** All proof JSON files referenced by STATE.md must conform to this schema. CI fails on schema violation.

## Common Required Keys (All Proof Types)

| Key | Type | Description |
|-----|------|--------------|
| `command` | string | The actual test/verification command that produced the proof (NOT the copy script) |
| `exit_code` | int | Must be 0 for valid proof |
| `timestamp` | string | ISO8601 format (e.g. `2026-03-02T06:51:08Z`) |
| `git_commit` | string | 40-hex SHA of commit when proof was produced |
| `git_branch` | string | Branch name when proof was produced |
| `evidence_fingerprint` | string | SHA256 hex of canonical evidence fields (tamper-evident) |

## Evidence Fingerprint (M11)

The `evidence_fingerprint` is a 64-char hex SHA256 of the proof's evidence fields, canonicalized (sorted keys, stable JSON). Large strings (>250KB) are hashed before inclusion. Any change to evidence invalidates the fingerprint; CI fails on mismatch.

## Type-Specific Required Keys

### PROOF_PROVENANCE_*.json

| Key | Type | Description |
|-----|------|--------------|
| `stdout` | string | Captured stdout from pytest run |
| `stderr` | string | Captured stderr from pytest run |

### PROOF_GATE_C_*.json

| Key | Type | Description |
|-----|------|--------------|
| `ui_smoke` | object | Must contain `exit_code` (int) equal to 0; may include `nav_steps_completed`, `binding_failure_count` |
| `gatec_log` | string | Gate C publish log content or path to log |

### PROOF_INSTALLER_*.json

| Key | Type | Description |
|-----|------|--------------|
| `results` | object | Must contain all lifecycle steps (InstallV1, LaunchV1, UpgradeV1ToV2, LaunchV2, RollbackV2ToV1, LaunchV1AfterRollback, UninstallV1); each value must be "PASS" when all_passed is true |
| `all_passed` | bool | Must be true for valid proof |

### PROOF_PAYLOAD_DETOX_*.json

| Key | Type | Description |
|-----|------|--------------|
| `check_repo_payloads` | string | Summary string (e.g. "PASS (git-tracked large files: 0; ...)") |
| `policy_file_summary` | object | Summary from repo_payload_policy (e.g. large_file_exceptions_count, payload_dir_baselines) |

## Semantic Validation

- **exit_code**: Must equal 0
- **timestamp**: Must parse as ISO8601
- **git_commit**: Must be exactly 40 hex characters
- **git_commit match**: In CI, must match `git rev-parse HEAD` unless `historical_proof: true` in the proof JSON

## Nested Semantics (M10)

- **PROOF_GATE_C**: `ui_smoke.exit_code` must equal 0
- **PROOF_INSTALLER**: `results` must contain all keys from `results_required_keys` (InstallV1, LaunchV1, UpgradeV1ToV2, LaunchV2, RollbackV2ToV1, LaunchV1AfterRollback, UninstallV1). When `all_passed` is true, each result value must be "PASS". Update schema when `installer/test-installer-lifecycle.ps1` changes.

## Refresh Policy (M11)

When `refresh_proof_git_metadata.py` updates git metadata, it MUST set:
- `historical_proof: true`
- `refreshed: true`
- `refreshed_reason` (required string)
- `refreshed_at` (ISO8601 timestamp)

Refreshed proofs require an entry in `.ci/historical_proofs_allowlist.json`. CI fails if a refreshed proof is not allowlisted.

## Historical Proof Exception

The only exemption from git_commit match is `"historical_proof": true` in the proof JSON AND the proof path listed in `.ci/historical_proofs_allowlist.json`. Refreshed proofs are always historical. New proofs must NOT use historical_proof unless refreshed.

## Machine-Readable Schema

See [.ci/proof_schema.json](../../../.ci/proof_schema.json) for programmatic validation.
