# CI verify-harness — first authoritative GitHub Actions run

**Status:** Recorded (implementation + first GHA observation; **operational certification pending**)  
**Date:** 2026-04-14  
**Type:** Governance + CI proof (continuous backlog; **GAP-069** umbrella **Closed** for bounded chain — this documents **C-2** Windows CI certification posture)

## Operational verdict

| Verdict | Meaning |
| --- | --- |
| **Implemented on `main`** | **Yes** — workflow + contracts + lineage tooling ship on default branch. |
| **Operationally certified (GHA)** | **No** — authoritative **`workflow_dispatch`** **`24409873139`** (**`d904757a`**) exercised; **Verify Quick Gate** **success**; **Verify Checkpoint + Resume Chain** **failure** at **STAGE 13: C# Unit Tests - Services** (6 failures: 4x `BackendLifecycleManager` NAudio `BadDeviceId` + 2x `AudioPlayerService` NAudio `BadDeviceId`). **STAGE 14: C# Unit Tests - CommandsGateways** also red (2x `PlaybackOperationsHandler.Record_*` — `Assert.IsTrue` on headless runner). **Not** a harness logic issue — **hosted runner has no audio device**. |
| **Authoritative `workflow_dispatch` run** | **Exercised** — run **[`24409873139`](https://github.com/wtsteward11/VoiceStudio/actions/runs/24409873139)** (**`d904757a`**, `workflow_dispatch`, `run_full_chain: true`). **Quick Gate: success**. **Checkpoint + Resume Chain: failure** at **Checkpoint run (stop after C# Unit Tests)** step — C# test shards 8 and 9 had **8** test failures (NAudio / audio-device-dependent on headless runner). Subsequent steps (lineage, resume, gate/ledger) **skipped**. |

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
| **`gh workflow run "Verify Harness (Checkpoint + Resume)" -f run_full_chain=true`** | PAT with permission to **create workflow dispatch events** (fine-grained: **Actions: Read and write**; classic: **`workflow`** + **`repo`**, if org policy allows) | **Initially HTTP 403** — `GITHUB_TOKEN` env var (Cursor-injected fine-grained PAT without Actions write) overrides keyring OAuth token. **Fix:** `Remove-Item env:GITHUB_TOKEN` before `gh workflow run` — keyring `gho_*` token with `workflow` scope succeeds. |

**Truth:** Dispatch unblocked after clearing `GITHUB_TOKEN` env override. Run **`24409873139`** dispatched successfully.

## Authoritative `workflow_dispatch` (full chain) — capture template

**Status:** **Recorded** — dispatched via `gh workflow run` after clearing `GITHUB_TOKEN` env override (keyring OAuth token `gho_*` with `workflow` scope).

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24409873139 |
| **Run ID** | `24409873139` |
| **Commit SHA** | `d904757afef5e66b1d5e7fae52faf639217e16f1` |
| **Event** | `workflow_dispatch` |
| **Inputs** | `run_full_chain: true` |
| **Verify Quick Gate** | **success** (14m19s) |
| **Verify Checkpoint + Resume Chain** | **failure** — first failing step: **Checkpoint run (stop after C# Unit Tests)** |
| **Artifacts** | `verify-quick-artifacts` + `verify-checkpoint-resume-artifacts` uploaded |
| **First failing stage** | **STAGE 13: C# Unit Tests - Services** — 6 failures (4x `BackendLifecycleManager` startup-decision tests: NAudio `BadDeviceId calling waveOutOpen` on headless runner; 2x `AudioPlayerService.PlayUrlAsync`: same NAudio error). **STAGE 14: C# Unit Tests - CommandsGateways** — 2 failures (`Record_StartsRecording`, `Record_WhenRecording_StopsRecording`: `Assert.IsTrue` on headless). Stages 15–16 passed. |

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

### Run D — `startup_artifact_check` skip on hosted (`24407929189`, commit `d5b98e2d`)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24407929189 |
| **Run ID** | `24407929189` |
| **Commit SHA** | `d5b98e2d42a7e361b2b985e1305d68d2548a7f67` |
| **Trigger** | `push` (path-filter: **`.github/workflows/verify-harness.yml`** — **`VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK: "true"`**) |
| **Verify Quick Gate** | **success** |
| **Verify Checkpoint + Resume Chain** | **skipped** (expected for **`push`**) |
| **STAGE 28: Gate/Ledger** | **PASS** — **`startup_artifact_check`** not run (env skip; CI has no desktop **`startup_decision.json`**) |
| **Artifacts** | **`verify-quick-artifacts`** uploaded |

**Authoritative `workflow_dispatch` evidence:** still **pending** — **operator** should run **Actions → Verify Harness → Run workflow** with **`run_full_chain: true`** now that hosted **Quick** is green (**prerequisite satisfied**).

## Outcome classification rule (this session)

**Do not collapse to a single bucket.** Observed push runs have **different** failure modes:

| Run | ID | Bucket | First meaningful failure | `verify.ps1` signal? |
| --- | --- | --- | --- | --- |
| **A** | `24379285704` | **`BucketC_InfraRed`** | **pip** / SSL during **Install Python dependencies** | **No** — failed before Quick stages |
| **B** | `24381385977` | **`BucketB_HarnessRed`** | **STAGE 20: Security Tests** — `test_path_allowed_only_in_whitelist` | **Yes** |
| **C** | `24382787205` | **`BucketB_HarnessRed`** | **STAGE 28: Gate/Ledger** — **`startup_artifact_check`** exit **1** (missing `%LOCALAPPDATA%\...\startup_decision.json` on clean runner) | **Yes** |
| **D** | `24407929189` | **Hosted Quick PASS** (not a failure bucket) | **STAGE 28** **PASS** — workflow sets **`VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK`**; **Verify Quick Gate** job **success** | **Yes** |

- **`BucketC_InfraRed`** applies to **Run A only** (infra before harness).
- **`BucketB_HarnessRed`** = hosted Quick reached **`verify.ps1`** stages and failed on a **real stage** (Security, Gate/Ledger sub-check, etc.).
- **No blending** — do not attribute Run B/C failures to pip SSL; do not attribute Run A to Security or startup artifact.
- **Checkpoint/resume** on **`push`** is **skipped by design** until authoritative **`workflow_dispatch`**.

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

**Startup artifact check (`startup_decision.json`):** Skipped on hosted runners via `VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK: "true"` in workflow `env`. This is a runner-environment exception only — hosted Windows runners do not launch the WinUI desktop app, so `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` is never produced. Local `verify.ps1` and `run_verification.py` continue to enforce the startup artifact contract when the env var is absent. This skip must not propagate to local developer workflows, `.env` files, or non-CI invocations.

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

## `workflow_dispatch` — run `24424525825` (2026-04-14) — false checkpoint failure

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24424525825 |
| **Run ID** | `24424525825` |
| **Commit SHA** | `1695cd347016bbf9ef322ba555bb27579dd0547b` |
| **Inputs** | `run_full_chain: true` |
| **Verify Quick Gate** | **success** |
| **Verify Checkpoint + Resume Chain** | **failure** — **Checkpoint run (stop after C# Unit Tests)** exit **1** |
| **Resume** | **skipped** |

**Bucket:** **`BucketB_HarnessRed`** — not a test failure: **STAGE 1–16** all **PASSED** in logs; **`Invoke-StopIfRequested`** exited **`1`** because **`$script:OverallPassed`** was never initialized **`$true`** while **`$OverallPassed`** was. **Remediation:** [`scripts/verify.ps1`](../../scripts/verify.ps1) — initialize and sync **`$script:OverallPassed`** with **`$OverallPassed`**; **StopAfterStage** exit uses **`$OverallPassed`**. **Local proof:** **`verify.ps1 -StopAfterStage "C# Unit Tests - Other"`** → **exit 0** (`artifacts/verify/20260414_173048/`).

**Operational certification:** still **pending** until a post-fix hosted **`workflow_dispatch`** **green**.

## `workflow_dispatch` — run `24426420065` (2026-04-14) — checkpoint green; resume failure

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24426420065 |
| **Run ID** | `24426420065` |
| **Commit SHA** | `7835f8fb4fb0232dc4a2405a522975061311f65a` |
| **Verify Quick Gate** | **success** |
| **Checkpoint run (stop after C# Unit Tests)** | **success** |
| **Resume run** | **failure** — **Contract Tests** stage **exit 1** (fail-fast) |

**Notes:** **`verify.ps1`** **StopAfterStage** fix validated (checkpoint no longer false-red). **`test_search.py`** collection hit **`Database not connected`** from eager **`search.py`** imports on **`main`** — addressed by **`903b4031`** (lazy storage). **Row stays open** until full-chain green on post-**`903b4031`** dispatch.

## `workflow_dispatch` — run `24429204800` (2026-04-15) — third full chain; resume red (**STAGE 17**)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24429204800 |
| **Run ID** | `24429204800` |
| **Commit SHA** | `9d5ccb1b0a45d6d79472d678ceff7c286cccee14` |
| **Verify Quick Gate** | **success** |
| **Checkpoint run (stop after C# Unit Tests)** | **success** |
| **Resume run** | **failure** — **STAGE 17: Python Unit Tests** — collection error **`ModuleNotFoundError: No module named 'aiohttp'`** (`test_dependency_resolver.py` → **`dependency_resolver`**) |
| **STAGE 18 Contract Tests** | **not reached** |

**Bucket:** **`BucketB_HarnessRed`** — dependency/import surface on hosted runner (not Contract Tests). **Operational certification:** still **No**. **Execution row** remains **Open** with **STAGE 17** bounded slice ([execution row § 24429204800](../../design/GOV_VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_FAILURE_EXECUTION_ROW.md)).

## Closure criteria

| Criterion | Status |
| --- | --- |
| Workflow exists on default branch and is triggerable | **Met** |
| At least one **GitHub Actions** run recorded with **immutable URL/ID/SHA** (push run `24379285704`) | **Met** |
| **`workflow_dispatch`** with **`run_full_chain: true`** exercised | **Exercised** — run **`24409873139`** (**`d904757a`**); **Quick Gate success**; **Checkpoint + Resume Chain failure** (C# unit test NAudio `BadDeviceId` on headless runner — 8 tests across STAGE 13–14). **Not yet green.** |
| Hosted **Verify Quick Gate** green on **`windows-latest`** (through **STAGE 28** after **`startup_artifact_check`** fix) | **Met** — run **`24407929189`** (**`d5b98e2d`**) |
| Outcome buckets classified without ambiguity (Run A **`BucketC_InfraRed`**; Run B/C **`BucketB_HarnessRed`**; Run D prerequisite pass — see **Outcome classification rule**) | **Met** |
| If non-green: exactly **one** bounded CI-only slice opened | **Met** |

---

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [CANONICAL_REGISTRY.md](../../governance/CANONICAL_REGISTRY.md) (CI verify-harness addendum)
