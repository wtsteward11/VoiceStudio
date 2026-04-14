# CI verify-harness — first authoritative GitHub Actions run

**Status:** Recorded  
**Date:** 2026-04-14  
**Type:** Governance + CI proof (continuous backlog; **GAP-069** umbrella **Closed** for bounded chain — this documents **C-2** Windows CI certification)

## Workflow

| Field | Value |
| --- | --- |
| **Workflow file** | `.github/workflows/verify-harness.yml` |
| **Workflow name (GHA)** | Verify Harness (Checkpoint + Resume) |
| **Runner** | `windows-latest` (GitHub-hosted; exact image version in run logs) |
| **Trigger used** | `workflow_dispatch` with `run_full_chain: true` |
| **Artifact retention** | **7 days** (`verify-quick-artifacts`, `verify-checkpoint-resume-artifacts`) |

## Run identity

| Field | Value |
| --- | --- |
| **Repository** | `wtsteward11/VoiceStudio` |
| **Run URL** | _Filled after `gh workflow run` — see session commit / `gh run view`_ |
| **Run ID** | _TBD_ |
| **Commit** | _SHA at dispatch time_ |

## Jobs (expected layout)

### Job: Verify Quick Gate

- **Command:** `.\scripts\verify.ps1 -Quick`
- **Purpose:** Baseline harness gate on a clean Windows runner (matches local Quick discipline).

### Job: Verify Checkpoint + Resume Chain

- **Checkpoint:** `.\scripts\verify.ps1 -StopAfterStage "C# Unit Tests - Other"`
- **Lineage:** `.\scripts\show-checkpoint-lineage.ps1` (stdout in CI log)
- **Resume:** `.\scripts\verify.ps1 -ResumeFrom "Python Unit Tests"`
- **Gate/ledger:** `python scripts/run_verification.py` (includes `completion_guard` — not `--skip-guard`)

## Environment caveats (hosted Windows)

- No interactive display; **UI Smoke** / **Failure-Path Smoke** / **Runtime-Missing Failure Smoke** may fail or degrade vs a developer machine with WinUI + built `VoiceStudio.App.exe`.
- **Primary proof:** checkpoint/resume lineage + inherited stages + `run_verification.py` outcome — not necessarily every UI stage green.

## Artifacts

- **verify-quick-artifacts:** `artifacts/verify/` from Quick job  
- **verify-checkpoint-resume-artifacts:** `artifacts/verify/` + `.buildlogs/verification/last_run.json`

Download:

```powershell
gh run download <run-id> --dir artifacts/ci-harness-download/
```

## Regression tripwires (repo)

- `tests/unit/test_verify_harness_contract.py` — workflow stage names, upload `if: always()`, lineage field parity, `$knownResumeStages` vs `Invoke-Stage` literals
- `scripts/verify.ps1` — `-ResumeFrom` unknown stage name → exit **1**

## Closure criteria

- [x] Workflow exists on default branch and is triggerable  
- [ ] At least one **`workflow_dispatch`** run completed (update **Run identity** above with URL/ID/conclusion)  
- [ ] If any job failed: open a **single** bounded CI-only slice (one root cause), do not smear  

---

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) (CI verify-harness addendum)
