# Voice Synthesis Proof Boundary Guard — 2026-04-29

**Type:** CI Guard Documentation
**Date:** 2026-04-29
**HEAD at time of implementation:** `2d05cacb` (ahead of `origin/main` `f44d7c39`)
**Status:** ACTIVE — integrated into `run_verification.py`

---

## Purpose

Prevent mock/stub engine results from being treated as real synthesis proof. Every new or changed
voice synthesis proof report must explicitly declare one engine-mode classification so that CI
enforces the boundary automatically.

Prior to this guard, proof reports existed without engine-mode labels. A developer reading a
`GENERATED_AUDIO_*` report had no reliable signal whether it documented real XTTS v2 synthesis
or a stub/test-mode substitution.

---

## Files Created / Modified

| File | Change |
|---|---|
| `scripts/ci/check_voice_synthesis_proof_boundary.py` | **Created** — the validator |
| `tests/unit/scripts/ci/test_voice_synthesis_proof_boundary.py` | **Created** — 13 unit tests |
| `scripts/run_verification.py` | **Modified** — gate entry added after `ui_gap_audit` |

---

## Guard Rules

### 1. Classification Required

Each relevant proof report must declare exactly one of:

| Token | Meaning |
|---|---|
| `REAL_ENGINE` | Real ML engine performed synthesis (e.g. XTTS v2, Piper) |
| `STUB_ENGINE` | Stub/test-mode engine used; proves orchestration only |
| `MOCK_ENGINE` | Mock engine in unit/integration tests; proves call paths only |
| `UNKNOWN` | Engine mode could not be determined (blocker condition) |

Detection accepts formats: `Classification: REAL_ENGINE`, `**Classification: REAL_ENGINE**`,
`VERDICT: REAL_ENGINE`, `engine_mode: REAL_ENGINE`, table cells, etc.

### 2. REAL_ENGINE: Evidence Required

A `REAL_ENGINE`-classified report must contain all of:

| Evidence Group | Required term(s) |
|---|---|
| `routed_engine` field | `routed_engine` |
| Artifact size | `bytes` / `KiB` / `MiB` / ` B)` |
| Artifact format | `RIFF` / `WAV` / `WAVE` / `header` |
| Library proof | `library` / `Library` / `asset` |
| Timeline proof | `timeline` / `revision` / `clip` |

### 3. STUB / MOCK / UNKNOWN: No Real-Synthesis Claims

Reports classified as non-REAL must not contain phrases outside a `Non-Claims` section:

- `REAL_ENGINE confirmed`
- `real synthesis proof`
- `real engine generated audio proof`
- `actual model output confirmed`

### 4. Historical Compatibility

The CI gate runs with `--changed-from origin/main` so only files added or modified since
`origin/main` are checked. Pre-existing reports committed before this rule are never
retroactively flagged.

### 5. Excluded Names (Meta-Reports)

Guard and boundary meta-reports are excluded even if their filename starts with `VOICE_SYNTHESIS`:
- Filenames containing `PROOF_BOUNDARY`, `_BOUNDARY_GUARD`, or `_GUARD_`

---

## CI Gate Registration

```python
# scripts/run_verification.py (inserted after ui_gap_audit)
proof_boundary_script = project_root / "scripts" / "ci" / "check_voice_synthesis_proof_boundary.py"
if proof_boundary_script.exists():
    checks.append({
        "name": "voice_synthesis_proof_boundary",
        "command": f"{sys.executable} {proof_boundary_script} --changed-from origin/main",
        "timeout": 15,
    })
```

**Gate name:** `voice_synthesis_proof_boundary`
**Mode in CI:** `--changed-from origin/main`
**Exit codes:** `0` = pass, `1` = violations

---

## Validation Results

### Existing Real-Engine Proof Report

```
python scripts/ci/check_voice_synthesis_proof_boundary.py --changed-from origin/main
[voice_synthesis_proof_boundary] Checked 1 report(s) (changed from origin/main):
  docs/reports/verification/REAL_ENGINE_GENERATED_AUDIO_PROOF_2026-04-29.md
[voice_synthesis_proof_boundary] All 1 report(s) PASS
EXIT=0
```

The `REAL_ENGINE_GENERATED_AUDIO_PROOF_2026-04-29.md` report passes all rules:
- Classification: `REAL_ENGINE`
- `routed_engine: xtts_v2`
- Artifact: 186,956 bytes, RIFF/WAVE header
- Library asset: `7882e9f9-d835-4fb0-9535-bfe6ca33b244`
- Timeline revision: 1 → 2

### run_verification.py Gate Result

```
[PASS] voice_synthesis_proof_boundary (exit 0, 0.08s)
Overall: PASS
```

### verify.ps1 -Quick Result

```
VERIFICATION PASSED
All stages passed. Safe to merge.
Artifacts: E:\VoiceStudio\artifacts\verify\20260429_144838\verification_report.md
```

### Unit Tests

```
python -m pytest tests/unit/scripts/ci/test_voice_synthesis_proof_boundary.py -v
========================= 13 passed, 5 warnings in 2.63s =========================
```

Test coverage:

| # | Test | Result |
|---|---|---|
| 1 | `test_valid_real_engine_passes` | PASS |
| 2 | `test_valid_stub_engine_passes` | PASS |
| 3 | `test_valid_mock_engine_passes` | PASS |
| 4 | `test_valid_unknown_passes` | PASS |
| 5 | `test_missing_classification_fails` | PASS |
| 6 | `test_multiple_classifications_fail` | PASS |
| 7 | `test_stub_with_real_engine_confirmed_fails` | PASS |
| 8 | `test_mock_with_real_synthesis_claim_fails` | PASS |
| 9 | `test_real_engine_missing_artifact_evidence_fails` | PASS |
| 10 | `test_changed_from_only_validates_relevant_names` | PASS |
| 11 | `test_unrelated_reports_ignored` | PASS |
| 12 | `test_error_output_includes_file_and_fix` | PASS |
| 13 | `test_stub_real_claim_in_nonclaims_section_passes` | PASS |

---

## Explicit Non-Claims

- This report documents the guard implementation; it is NOT a synthesis proof.
- This is NOT a runtime FULL PASS.
- This is NOT a human/operator proof.
- No synthesis was performed to create this document.
- Not related to GAP-008, Slice 46, MainWindow*ShellBridge, or ENGINE_PARITY_MATRIX.
- Not related to RHVoice.
