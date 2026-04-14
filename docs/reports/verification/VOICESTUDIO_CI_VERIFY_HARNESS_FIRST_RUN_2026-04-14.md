# CI verify-harness — first authoritative GitHub Actions run

**Status:** Recorded (implementation + first GHA observation; **operational certification pending**)  
**Date:** 2026-04-14  
**Type:** Governance + CI proof (continuous backlog; **GAP-069** umbrella **Closed** for bounded chain — this documents **C-2** Windows CI certification posture)

## Operational verdict

| Verdict | Meaning |
| --- | --- |
| **Implemented on `main`** | **Yes** — workflow + contracts + lineage tooling ship on default branch. |
| **Operationally certified (GHA)** | **No** — first observed GitHub Actions run is **`BucketC_InfraRed`** (pip SSL during dependency install); **no** successful **`workflow_dispatch`** with **`run_full_chain: true`** has been recorded yet. |
| **Authoritative `workflow_dispatch` run** | **Not yet executed** — automation **`gh workflow run`** returns **HTTP 403** from the active token; **operator must use Actions UI** (or a token with workflow dispatch permission). See **Dispatch path** below. |

## Workflow

| Field | Value |
| --- | --- |
| **Workflow file** | `.github/workflows/verify-harness.yml` |
| **Workflow name (GHA)** | Verify Harness (Checkpoint + Resume) |
| **Runner** | `windows-latest` (GitHub-hosted; exact image version in run logs) |
| **Authoritative trigger (certification target)** | `workflow_dispatch` with `run_full_chain: true` |
| **Artifact retention** | **7 days** (`verify-quick-artifacts`, `verify-checkpoint-resume-artifacts`) |

## Dispatch path (repeatable operator record)

| Method | Identity / requirement | Result (2026-04-14) |
| --- | --- | --- |
| **GitHub Actions UI** | Repo **Write** or **Actions** access | **Primary path** — **Actions → Verify Harness (Checkpoint + Resume) → Run workflow**; enable **Run checkpoint+resume chain**. |
| **`gh workflow run "Verify Harness (Checkpoint + Resume)" -f run_full_chain=true`** | PAT with permission to **create workflow dispatch events** (fine-grained: **Actions: Read and write**; classic: **`workflow`** + **`repo`**, if org policy allows) | **HTTP 403** — `Resource not accessible by personal access token` (token in use cannot dispatch). |

**Truth:** Certification today is **unblocked only via UI** unless the operator rotates to a dispatch-capable token.

## Run identity — observed first GHA execution (push; not authoritative dispatch)

This is the **only** completed run available for immutable evidence as of closure update. It is **not** a substitute for **`workflow_dispatch`** + **`run_full_chain`**.

| Field | Value |
| --- | --- |
| **Repository** | `wtsteward11/VoiceStudio` |
| **Workflow landed on `main`** | `18abac073f4f324aea71125c92c4236883275a25` (among commits touching harness paths) |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24379285704 |
| **Run ID** | `24379285704` |
| **Commit SHA** | `18abac073f4f324aea71125c92c4236883275a25` |
| **Trigger** | `push` (path-filter: `verify.ps1`, `verify-harness.yml`, etc.) |
| **Outcome bucket** | **`BucketC_InfraRed`** |
| **First failure (only)** | **Install Python dependencies** — `SSL: DECRYPTION_FAILED_OR_BAD_RECORD_MAC` during pip download (transient runner/network SSL; not `verify.ps1` logic). |

### Job conclusions (run `24379285704`)

| Job | Conclusion | Notes |
| --- | --- | --- |
| **Verify Quick Gate** | **failure** | Failed before **Quick verification gate** step. |
| **Verify Checkpoint + Resume Chain** | **skipped** | Expected for **`push`**: second job runs only for **`workflow_dispatch` + `run_full_chain`** or **`schedule`**. |

## Outcome classification rule (this session)

- **Single bucket:** **`BucketC_InfraRed`** — failure on hosted runner during **pip** / SSL **before** meaningful **`verify.ps1`** signal.
- **No blending** — harness checkpoint/resume was **not** exercised on this run.

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
- **Transient pip/SSL** on `windows-latest` can fail installs without repo changes — classify as **infra** unless reproducible locally.

## Workflow hardening (pip resilience; 2026-04-14)

| Change | Location |
| --- | --- |
| **`python -m pip install --upgrade pip --retries 5 --timeout 60`** | `.github/workflows/verify-harness.yml` — both jobs — **Install Python dependencies** |
| **`pip install --retries 5 --timeout 60 -e ".[dev,extras]"`** | same step |

**Rationale:** first GHA run (`24379285704`) failed mid-download with **`SSL: DECRYPTION_FAILED_OR_BAD_RECORD_MAC`**; explicit retries and longer socket timeout reduce transient infra flakes without masking real resolution failures (no `continue-on-error`).

**Authoritative `workflow_dispatch`** with **`run_full_chain: true`** is still **not recorded** in GitHub until an operator runs **Actions → Verify Harness → Run workflow** (or uses a dispatch-capable token) **after** this workflow change is on `main`.

## Artifacts

- **verify-quick-artifacts:** `artifacts/verify/` from Quick job  
- **verify-checkpoint-resume-artifacts:** `artifacts/verify/` + `.buildlogs/verification/last_run.json`

Download:

```powershell
gh run download 24379285704 --dir artifacts/ci-harness-download/
```

## Post-certification trigger policy (explicit)

| Policy | Decision | Rationale |
| --- | --- | --- |
| **Weekly full-chain** | **Keep** (`schedule` cron in workflow) | Rolling proof of checkpoint/resume without every PR cost. |
| **Path-filter `push` Quick-only** | **Keep** | Cheap signal on harness-touching commits; full chain stays dispatch/schedule. |
| **Required check for harness PRs** | **Optional / not enforced in this closure** | Enforce when first **`workflow_dispatch`** green exists; avoids blocking PRs on flaky pre-proof CI. |

## Regression tripwires (repo)

- `tests/unit/test_verify_harness_contract.py` — workflow stage names, upload `if: always()`, lineage field parity, `$knownResumeStages` vs `Invoke-Stage` literals
- `scripts/verify.ps1` — `-ResumeFrom` unknown stage name → exit **1**

## Bounded follow-up (non-green)

- **Single execution row:** [GOV_VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_FAILURE_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_FAILURE_EXECUTION_ROW.md)

## Closure criteria

| Criterion | Status |
| --- | --- |
| Workflow exists on default branch and is triggerable | **Met** |
| At least one **GitHub Actions** run recorded with **immutable URL/ID/SHA** (push run `24379285704`) | **Met** |
| **`workflow_dispatch`** with **`run_full_chain: true`** **completed green** | **Pending** (operator UI or dispatch-capable token) |
| Outcome bucket recorded without ambiguity (**`BucketC_InfraRed`** for observed run) | **Met** |
| If non-green: exactly **one** bounded CI-only slice opened | **Met** |

---

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) (CI verify-harness addendum)
