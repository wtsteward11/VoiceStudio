# Voice Synthesis Proof Reporting Standard

**Version:** 1.0
**Date:** 2026-04-29
**Owner:** Verification gate `voice_synthesis_proof_boundary`
**Validator:** `scripts/ci/check_voice_synthesis_proof_boundary.py`
**Template:** `docs/templates/VOICE_SYNTHESIS_PROOF_REPORT_TEMPLATE.md`

---

## Purpose

This standard prevents mock/stub engine results from being treated as real synthesis proof.
Every proof report named `VOICE_SYNTHESIS*.md`, `GENERATED_AUDIO*.md`, or
`REAL_ENGINE_GENERATED_AUDIO*.md` under `docs/reports/verification/` must satisfy
the rules in this document. The CI gate enforces them on every new or changed report.

---

## Required Metadata Block

Every relevant proof report must include a `VOICESTUDIO_PROOF_BOUNDARY_V1` metadata block
near the top, before the first heading section:

```markdown
<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->
```

### Required fields

| Field | Allowed values |
|---|---|
| `classification` | `REAL_ENGINE`, `STUB_ENGINE`, `MOCK_ENGINE`, `UNKNOWN` |
| `proof_type` | `voice_synthesis`, `generated_audio`, `proof_boundary`, `other` |
| `engine_mode_source` | `runtime_probe`, `test_mode_env`, `mock_fixture`, `blocked_unknown`, `manual_unknown`, `not_applicable` |
| `runtime_claim` | `true` or `false` |
| `operator_claim` | `true` or `false` |

### Constraint: classification must match

The `classification` field in the metadata block must match the textual `Classification:` or
`VERDICT:` line in the report body. Mismatches are a CI violation.

---

## Classification Definitions

### REAL_ENGINE

The real ML inference engine (e.g. XTTS v2, Piper, eSpeak NG) performed synthesis.
`VOICESTUDIO_TEST_MODE` was not set. `routed_engine` in the API response names a
non-stub engine. Audio artifact has been validated (RIFF/WAV header, non-zero size).
Library save and timeline placement have been confirmed.

**What it proves:** The synthesis path from API → engine → audio artifact → library → timeline
operated with a real model at the time of the proof run.

**What it does NOT prove (must be listed in Non-Claims):**
- Runtime FULL PASS (full suite across all engines)
- Operator/human proof (heard and attested by a human)
- Durability (data persists across restarts) unless explicitly tested
- Performance/latency guarantees
- Other engine parity (RHVoice, ENGINE_PARITY_MATRIX, etc.)

### STUB_ENGINE

`VOICESTUDIO_TEST_MODE=1` or equivalent stub path was active. The synthesis route
was exercised but no real ML model was used. `routed_engine` may be `stub` or absent.

**What it proves:** Orchestration path (API → stub handler → response) executed without error.

**What it does NOT prove:** Real synthesis, real audio quality, real engine routing.

### MOCK_ENGINE

A mock engine fixture was injected in a test environment (unit/integration test).
No real HTTP backend was involved.

**What it proves:** The code path under test (ViewModel, service, route) called the
engine interface correctly and handled the response.

**What it does NOT prove:** Runtime synthesis, real HTTP integration, real audio.

### UNKNOWN

Engine mode could not be determined. This is a blocker condition — the report must
explain why verification could not complete.

**What it proves:** Nothing about synthesis. Documents the failure to determine engine mode.

---

## Required Non-Claims Section

Every relevant report must contain a Non-Claims section with one of these headings:

- `## Explicit Non-Claims`
- `## Non-Claims`
- `## Mock/Stub Non-Claims`
- `## Boundaries`
- `## Proof Boundary`
- `## What This Does Not Prove`

The section must list what the report does NOT establish. At minimum:
- Whether this is a runtime FULL PASS
- Whether this is an operator/human proof
- Whether this covers engines not tested

---

## REAL_ENGINE Evidence Requirements

A `REAL_ENGINE`-classified report must include all of:

| Evidence | Accepted terms |
|---|---|
| `routed_engine` field | `routed_engine` present and non-stub |
| Artifact size | `bytes`, `KiB`, `MiB` |
| Artifact format | `RIFF`, `WAV`, `WAVE`, `header` |
| Library evidence | `asset id`, `asset_id`, `library asset`, `HTTP 201`, `audio_id` |
| Timeline evidence | `revision`, `track`, `clip`, `placement` |

**Negative-only evidence is rejected.** Phrases like "no library evidence", "timeline not tested",
"library unavailable" fail the check unless placed inside an explicit Non-Claims section.

---

## UNKNOWN Blocker Requirement

An `UNKNOWN`-classified report must contain explicit blocker language outside the
classification line. Accepted terms:

`blocker`, `blocked`, `could not determine`, `unable to determine`, `engine mode unknown`,
`unavailable`, `missing evidence`, `verification could not complete`, `automatic verification failed`

---

## Forbidden Overclaim Patterns

The following phrases are forbidden outside Non-Claims sections in STUB_ENGINE, MOCK_ENGINE,
and UNKNOWN reports:

| Forbidden phrase | Why |
|---|---|
| `REAL_ENGINE confirmed` | False classification claim |
| `real synthesis proof` | Overclaims synthesis quality |
| `real engine generated audio proof` | Overclaims real-engine proof |
| `actual model output confirmed` | Overclaims ML inference |
| `real model output` | Overclaims ML inference |
| `non-stub synthesis confirmed` | Overclaims real synthesis |
| `runtime proof complete` | Overclaims runtime coverage |
| `runtime FULL PASS` | Reserved for full runtime verification |
| `operator proof complete` | Requires human attestation |
| `heard attestation` | Requires human attestation |
| `manual playback confirmed` | Requires human attestation |

These phrases are allowed only inside an explicit Non-Claims section (to negate them).

---

## Running the Validator

```powershell
# Changed-from mode (CI default — checks new/changed/staged/untracked reports)
python scripts/ci/check_voice_synthesis_proof_boundary.py --changed-from origin/main

# All mode (checks all relevant reports, including pre-existing — for audits)
python scripts/ci/check_voice_synthesis_proof_boundary.py --all

# JSON output
python scripts/ci/check_voice_synthesis_proof_boundary.py --json --changed-from origin/main

# Self-test (validate built-in examples, quick sanity check)
python scripts/ci/check_voice_synthesis_proof_boundary.py --self-test-examples
```

Exit 0 = pass. Exit 1 = violations.

---

## How Changed-File Mode Works

The CI gate uses `--changed-from origin/main`. This mode collects the union of:

1. **Committed delta** — `git diff --name-only --diff-filter=ACM origin/main..HEAD`
2. **Staged changes** — `git diff --name-only --cached --diff-filter=ACM`
3. **Unstaged changes** — `git diff --name-only --diff-filter=ACM`
4. **Untracked files** — `git ls-files --others --exclude-standard docs/reports/verification/`

Only files matching the relevant name patterns under `docs/reports/verification/` are checked.
Guard/meta-reports (filenames containing `PROOF_BOUNDARY`, `_BOUNDARY_GUARD`, `_GUARD_`) are excluded.

---

## Historical Compatibility

Pre-existing reports committed before the `voice_synthesis_proof_boundary` gate was introduced
(`20f700b2`, 2026-04-29) are not retroactively checked by the default CI gate
(`--changed-from origin/main`). Only reports added or modified after that point are subject to
the standard. Use `--all` to audit all historical reports.

---

## Excluded Reports (Guard/Meta)

Files matching these name patterns are excluded even if they start with `VOICE_SYNTHESIS`:
- `*PROOF_BOUNDARY*`
- `*_BOUNDARY_GUARD*`
- `*_GUARD_*`

This prevents the guard documentation report itself from needing to classify itself.
