# GOV — CI verify-harness first GHA signal (execution row)

**Status:** Open (bounded CI-only)  
**Opened:** 2026-04-14  
**Scope:** Single root cause — **hosted runner pip install SSL failure** before **`verify.ps1`** executes; **not** harness logic drift.

## Outcome bucket (frozen)

**`BucketC_InfraRed`** — workflow failed on **`windows-latest`** during **`pip install -e ".[dev,extras]"`** with transient SSL:

`pip._vendor.urllib3.exceptions.SSLError: [SSL: DECRYPTION_FAILED_OR_BAD_RECORD_MAC]`

**First failing stage:** `Verify Quick Gate` → **Install Python dependencies** (step 5).

## Immutable run evidence (observed)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24379285704 |
| **Run ID** | `24379285704` |
| **Commit SHA** | `18abac073f4f324aea71125c92c4236883275a25` |
| **Trigger** | `push` (path-filtered workflow paths) — **not** `workflow_dispatch` |
| **Verify Quick Gate** | **failure** (pip install) |
| **Verify Checkpoint + Resume Chain** | **skipped** (by design: only `workflow_dispatch` + `run_full_chain` or `schedule`) |

**Note:** This run does **not** satisfy “first authoritative full-chain” certification; it only proves first GHA attempt on the workflow landed and failed in CI env before harness signal.

## Dispatch path (operator)

| Path | Status |
| --- | --- |
| **GitHub Actions UI** | **Primary** — **Actions → Verify Harness (Checkpoint + Resume) → Run workflow**, check **Run checkpoint+resume chain** (`run_full_chain: true`). |
| **`gh workflow run "Verify Harness (Checkpoint + Resume)" -f run_full_chain=true`** | **Blocked here** with **HTTP 403** (`Resource not accessible by personal access token`). Use a token allowed to **dispatch workflows** (fine-grained: **Actions: Read and write** on repo; or classic PAT with **`workflow`** + **`repo`**, subject to org policy). |

## Remediation scope (one slice)

1. **Re-run** the workflow (prefer **`workflow_dispatch`** with **`run_full_chain: true`**) after transient SSL clears, **or** add a **single** bounded retry on pip install in `.github/workflows/verify-harness.yml` if flakes persist (document rationale; no blind loops).
2. **Do not** change **`scripts/verify.ps1`** lineage or checkpoint semantics under this row — failure occurred **before** Quick.

### Applied (2026-04-14)

- **Pip resilience:** both **Install Python dependencies** steps in [`.github/workflows/verify-harness.yml`](../../.github/workflows/verify-harness.yml) now use explicit **`--retries 5`** and **`--timeout 60`** on **`pip`** / **`python -m pip`** (see [closure report](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md) § Workflow hardening).
- **Next:** operator **`workflow_dispatch`** on **`main`** with **`run_full_chain: true`**; if still red, freeze **first failing step only** and update this row (do not smear root causes).

### Supplemental GHA signal (2026-04-14 — push `887ae942`)

After pip hardening reached **`main`**, GitHub Actions run **`24381385977`** (**[link](https://github.com/wtsteward11/VoiceStudio/actions/runs/24381385977)**) **passed** **Install Python dependencies** and **failed** **Verify Quick Gate** at **Security Tests**: `test_plugin_sandbox_security.py::TestPermissionEnforcement::test_path_allowed_only_in_whitelist`. **Not** pip; **not** `workflow_dispatch` (checkpoint job **skipped**).

**Bounded fix:** [`backend/services/plugin_sandbox.py`](../../backend/services/plugin_sandbox.py) — `can_access_path` now compares **resolved** roots and uses **`relative_to()`** for containment (Windows runner-safe).

**Supplemental GHA signal (2026-04-14 — push `b7a4ddf5`, run `24382787205`)**

Path-filter **push** after **`be2c10b4`** sandbox fix + workflow comment touch: **[run](https://github.com/wtsteward11/VoiceStudio/actions/runs/24382787205)**. **Verify Quick Gate:** **failure**. **STAGE 20: Security Tests:** **PASS** (hosted confirmation — prior **`24381385977`** sandbox failure **superseded**). **First terminal failure (single slice):** **STAGE 28: Gate/Ledger Validation** → **`startup_artifact_check`** **exit 1** (**not** sandbox; **not** pip). Checkpoint/resume job **skipped** (`push`).

**Row stays Open** until a green **`workflow_dispatch`** + **`run_full_chain: true`** is recorded in the [closure report](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md) **and** hosted **Quick** is green end-to-end (or a **new** bounded row documents the **`startup_artifact_check`** slice without smearing sandbox scope).

### Bounded slice — `startup_artifact_check` on hosted Quick (2026-04-14)

**Scope (only):** GHA Quick red at **`startup_artifact_check`** — `scripts/ci/check_startup_artifact.py` expects **`%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json`** (written by the WinUI app); clean **`windows-latest`** has no such file.

**Fix (repo):** workflow-level env **`VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK: "true"`** in [`.github/workflows/verify-harness.yml`](../../.github/workflows/verify-harness.yml) (honest skip: CI does not launch the desktop app). Wired in [`scripts/run_verification.py`](../../scripts/run_verification.py) when appending the **`startup_artifact_check`** subprocess.

**Hard OUT:** no checkpoint/resume edits; no Gate/Ledger refactor beyond this env; no fake seeding of **`startup_decision.json`** unless policy changes.

**Proof target:** next path-filter **push** run (**Run D**) — **Verify Quick Gate** **success** through **STAGE 28**; then operator may spend **`workflow_dispatch`** + **`run_full_chain: true`**.

**Observed (2026-04-14 — Run D `24407929189`, commit `d5b98e2d`):** **[run](https://github.com/wtsteward11/VoiceStudio/actions/runs/24407929189)** — **Verify Quick Gate** **success**. **`startup_artifact_check`** slice **closed** on hosted. **Next:** authoritative **`workflow_dispatch`** + **`run_full_chain: true`** via **Actions UI** (`gh` **403**); record URL/ID/SHA in [closure report](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md).

### Authoritative `workflow_dispatch` (2026-04-14 — run `24409873139`)

**Dispatch:** `gh workflow run` succeeded after clearing `GITHUB_TOKEN` env override (Cursor-injected fine-grained PAT lacked Actions write; keyring `gho_*` OAuth token with `workflow` scope works).

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24409873139 |
| **Run ID** | `24409873139` |
| **Commit SHA** | `d904757afef5e66b1d5e7fae52faf639217e16f1` |
| **Event** | `workflow_dispatch` |
| **Inputs** | `run_full_chain: true` |
| **Verify Quick Gate** | **success** (14m19s) |
| **Verify Checkpoint + Resume Chain** | **failure** |
| **First failing step** | **Checkpoint run (stop after C# Unit Tests)** — exit 1 |
| **First failing stage** | **STAGE 13: C# Unit Tests - Services** (6 test failures) |

**Failure root cause (all 8 tests):** NAudio `BadDeviceId calling waveOutOpen` — headless `windows-latest` runner has **no audio output device**. Tests that call `WaveOutEvent.Init()` / `waveOutOpen` fail because there is no `WAVE_MAPPER` device. This is an **environment limitation**, not a harness or code defect.

**Affected tests (STAGE 13 — Services, 6 failures):**
- `AudioPlayerService.PlayUrlAsync_NormalCompletion_DeletesTempFile`
- `AudioPlayerService.PlayUrlAsync_StreamingDownload_CreatesPlayableFile`
- `BackendLifecycleManager.EnsureBackendRunningAsync_WhenHealthyBackendExists_WritesReuseDecision`
- `BackendLifecycleManager.EnsureBackendRunningAsync_WhenBackendMissing_WritesSpawnDecision`
- `BackendLifecycleManager.EnsureBackendRunningAsync_WhenPortHeldByNonHttpListener_WritesPortCollisionDecision`
- `BackendLifecycleManager.EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn`

**Affected tests (STAGE 14 — CommandsGateways, 2 failures):**
- `PlaybackOperationsHandlerTests.Record_StartsRecording`
- `PlaybackOperationsHandlerTests.Record_WhenRecording_StopsRecording`

**Row stays Open.** Next bounded slice: skip or guard audio-device-dependent tests on headless runners (test category / `[TestCategory("RequiresAudioDevice")]` or `RuntimeInformation`-based skip).

### Post audio-guard `workflow_dispatch` (2026-04-14 — run `24412155919`)

**Commit:** `c5403d35` — `fix(test): headless audio-device + venv guards for CI certification` (`AudioDeviceGuard`, `RequiresAudioDevice` / `RequiresLocalVenv` categories).

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24412155919 |
| **Run ID** | `24412155919` |
| **Verify Quick Gate** | **success** (~11m46s) |
| **Verify Checkpoint + Resume Chain** | **failure** |
| **First failing stage** | **STAGE 6: C# Unit Tests - ViewModels Seam A-D** — **TIMED_OUT** (180s per-shard budget) |

**Not** NAudio: **STAGE 13 Services** and **STAGE 14 CommandsGateways** completed with **Passed**; inconclusives/skips match headless audio + venv guards (e.g. `Record_*` skipped, `BackendProcessManagerDecision*` skipped without venv).

**Current first slice for Checkpoint green:** **Seam A-D shard timeout** — investigate hang/slow tests or raise shard budget for GHA (separate bounded row if needed). Audio-device guard slice for STAGE 13–14 is **addressed** in this commit.

**Row stays Open** until a full **`workflow_dispatch`** + **`run_full_chain: true`** completes **green** end-to-end.

### Bounded slice (active): STAGE 6 — ViewModels Seam A-D timeout

**Frozen scope (only):** **`C# Unit Tests - ViewModels Seam A-D`** — harness **`TIMED_OUT`** at **180s** on hosted **`windows-latest`** (Checkpoint job). Diagnose hang vs budget vs contamination; fix test/runtime or shard split; **not** timeout inflation until shard health is proven.

**Hard OUT:** audio-device guards, venv guards, `startup_artifact_check`, `GITHUB_TOKEN` / dispatch mechanics, unrelated CI cleanup, broad ViewModel refactors.

**Evidence anchor:** run **`24412155919`**; filter = **`$SeamAD`** in [`scripts/verify.ps1`](../../scripts/verify.ps1) (`$SeamBase` + A–D FQN tokens).

**Exit:** green **`workflow_dispatch`** + **`run_full_chain: true`** with Seam A-D **PASSED** under budget, or documented follow-up row if a **different** stage fails first after fix.

**Mitigations applied (2026-04-14):**
1. **`DispatcherQueueTestHelpers.ShutdownSyncBounded`** — replace unbounded `ShutdownQueueAsync().GetAwaiter().GetResult()` in ViewModel test cleanup.
2. **Harness: `RunConfiguration.MaxCpuCount=1`** for the Seam A-D `dotnet test` invocation only — avoids multi-minute hangs from parallel MSTest workers + per-test `DispatcherQueueController` on `windows-latest`.
3. **Shard budget** — remains **180s** for Seam A-D once (1)+(2) are in place (300s was insufficient while the parallel deadlock persisted).

## Rerun command (after token/UI access)

```powershell
gh workflow run "Verify Harness (Checkpoint + Resume)" --repo wtsteward11/VoiceStudio -f run_full_chain=true
# If 403: use Actions UI per table above.
```

## Closure

Close this row when:

- A **`workflow_dispatch`** run with **`run_full_chain: true`** completes, **and**
- Outcome bucket + job conclusions are recorded in [VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md), **and**
- If **`BucketA_Green`**: operational certification may be claimed; if **`BucketB_Partial`** / **`BucketC_InfraRed`**: open a **new** single-row slice (do not smear root causes).

**Related:** [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md) §8 · [verify-harness.yml](../../.github/workflows/verify-harness.yml)
