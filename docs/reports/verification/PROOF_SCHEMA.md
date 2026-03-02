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

## Type-Specific Required Keys

### PROOF_PROVENANCE_*.json

| Key | Type | Description |
|-----|------|--------------|
| `stdout` | string | Captured stdout from pytest run |
| `stderr` | string | Captured stderr from pytest run |

### PROOF_GATE_C_*.json

| Key | Type | Description |
|-----|------|--------------|
| `ui_smoke` | object | Must contain `exit_code` (int); may include `nav_steps_completed`, `binding_failure_count` |
| `gatec_log` | string | Gate C publish log content or path to log |

### PROOF_INSTALLER_*.json

| Key | Type | Description |
|-----|------|--------------|
| `results` | object | Step names to PASS/FAIL (e.g. InstallV1, LaunchV1, UpgradeV1ToV2, etc.) |
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
- **git_commit match**: In CI, must match `git rev-parse HEAD` unless `historical_proof: true` and path is in allowlist

## Historical Proof Exception

If a proof JSON contains `"historical_proof": true`, the git_commit match is skipped. Use only for proofs produced before schema enforcement. New proofs must NOT use this flag.

## Machine-Readable Schema

See [.ci/proof_schema.json](../../../.ci/proof_schema.json) for programmatic validation.
