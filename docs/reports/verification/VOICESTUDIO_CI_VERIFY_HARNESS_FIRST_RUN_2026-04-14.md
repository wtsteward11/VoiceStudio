# CI verify-harness — first authoritative GitHub Actions run

**Status:** Recorded (implementation + first GHA observation; **operational certification pending**)  
**Date:** 2026-04-14  
**Type:** Governance + CI proof (continuous backlog; **GAP-069** umbrella **Closed** for bounded chain — this documents **C-2** Windows CI certification posture)

## Operational verdict

| Verdict | Meaning |
| --- | --- |
| **Implemented on `main`** | **Yes** — workflow + contracts + lineage tooling ship on default branch. |
| **Operationally certified (GHA)** | **No** — authoritative **`workflow_dispatch`** + **`run_full_chain: true`** green **not** recorded (**`gh workflow run`** **HTTP 403**). **Post-fix push** **`24382787205`** (**`b7a4ddf5`**) reached **STAGE 20: Security Tests** **PASS** (sandbox remediation verified on **`windows-latest`**); **Quick** still **failure** at **STAGE 28: Gate/Ledger Validation** — first failing sub-check **`startup_artifact_check`** **exit 1** (not sandbox; not pip). |
| **Authoritative `workflow_dispatch` run** | **Not yet executed** — automation **`gh workflow run`** returns **HTTP 403**; **operator must use Actions UI** (or a token with workflow dispatch permission). **Push run `24382787205`** is **substitute evidence only** (Quick + path-filter), **not** a full-chain dispatch. See **Dispatch path** and **Run C** below. |

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

## Authoritative `workflow_dispatch` (full chain) — capture template

**Status:** **Not yet recorded** — automation **`gh workflow run`** returns **HTTP 403** (`Resource not accessible by personal access token`). **2026-04-14:** Path-filter **push** on **`.github/workflows/verify-harness.yml`** triggered **Run C** (`24382787205`) as **substitute** for hosted signal; **does not** replace **`workflow_dispatch`** + **`run_full_chain`**.

| Field | Value |
| --- | --- |
| **Run URL** | *Pending UI dispatch — use Actions → Run workflow* |
| **Run ID** | *Pending* |
| **Commit SHA** | *Pending (expect `main` tip at dispatch time)* |
| **Event** | `workflow_dispatch` |
| **Inputs** | `run_full_chain: true` |
| **Verify Quick Gate** | *Pending* |
| **Verify Checkpoint + Resume Chain** | *Pending* |
| **Artifacts** | *Pending (`verify-quick-artifacts`, `verify-checkpoint-resume-artifacts`)* |

**Push substitute (NOT dispatch):** Run **`24382787205`** — see **Run C** below.

## Run identity — observed GHA executions (push; not authoritative dispatch)

These runs are **not** a substitute for **`workflow_dispatch`** + **`run_full_chain`**.

### Run A — first workflow landing (`24379285704`)

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

### Run B — post pip-hardening push (`24381385977`, commit `887ae942`)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24381385977 |
| **Run ID** | `24381385977` |
| **Commit SHA** | `887ae942fce3deab94feb599f98108f4238789f6` |
| **Trigger** | `push` (workflow / harness paths) |
| **Install Python dependencies** | **success** (pip retries/timeouts; SSL path cleared) |
| **First failing step (fail-fast)** | **Quick verification gate** → **STAGE 20: Security Tests** — `tests/security/test_plugin_sandbox_security.py::TestPermissionEnforcement::test_path_allowed_only_in_whitelist` (**AssertionError**). |
| **Python Quality** | **passed** (console noted **Mypy strict scope** delta **+1** vs budget **0** as advisory in that stage; stage still **PASSED** per harness) |
| **Verify Checkpoint + Resume Chain** | **skipped** | Same as run A — **`push`** does not run full chain. |

**Remediation (repo):** `SandboxPermissions.can_access_path` in [`backend/services/plugin_sandbox.py`](../../../backend/services/plugin_sandbox.py) now resolves allow-list roots and uses **`Path.relative_to()`** so paths outside the workspace are rejected on Windows-hosted runners (fixes false allow on `windows-latest`).

### Run C — path-filter push after sandbox + STATE sync (`24382787205`, commit `b7a4ddf5`)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24382787205 |
| **Run ID** | `24382787205` |
| **Commit SHA** | `b7a4ddf5d769cc145250141cd42887f78cfcc0b6` |
| **Trigger** | `push` (path-filter: **`.github/workflows/verify-harness.yml`** comment touch — re-trigger after **`be2c10b4`** sandbox fix; **`gh workflow_dispatch`** **403**) |
| **Verify Quick Gate** | **failure** |
| **Verify Checkpoint + Resume Chain** | **skipped** (expected for **`push`**) |
| **Install Python dependencies** | **success** |
| **STAGE 20: Security Tests** | **PASS** (~140s) — **hosted proof** that prior **`24381385977`** **`test_path_allowed_only_in_whitelist`** failure is **resolved** (full security stage green). |
| **First failing step (fail-fast terminal)** | **STAGE 28: Gate/Ledger Validation** → **`startup_artifact_check`** **exit 1** (`ledger_validate` **PASS**; overall Gate/Ledger **FAIL**). |
| **Artifacts** | **`verify-quick-artifacts`** uploaded (run log + `artifacts/verify/20260414_053817/`) |

**Authoritative `workflow_dispatch` evidence:** still **pending** — use **Actions UI** for **`run_full_chain: true`**; **`24382787205`** is **Quick-only** substitute.

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
| **`workflow_dispatch`** with **`run_full_chain: true`** **completed green** | **Pending** (operator UI or dispatch-capable token); push **`24382787205`** **does not** satisfy this row |
| Outcome bucket recorded without ambiguity (**`BucketC_InfraRed`** for observed run) | **Met** |
| If non-green: exactly **one** bounded CI-only slice opened | **Met** |

---

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) (CI verify-harness addendum)
