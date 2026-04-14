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

### Post Seam A-D fix — `workflow_dispatch` `24417704302` (2026-04-14)

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24417704302 |
| **Checkpoint: STAGE 6 Seam A-D** | **PASSED** (~8s) — `RunConfiguration.MaxCpuCount=1` + bounded dispatcher shutdown (`6772b9b3` on `main`) |
| **Verify Quick Gate** | **success** |
| **Verify Checkpoint + Resume Chain** | **failure** (resume path) |
| **First failing stage after checkpoint** | **STAGE 18: Contract Tests** — **FAILED** (pytest exit **1**); see job artifact `logs/contract_tests.log` |

**Seam A-D bounded slice:** **addressed** (no longer the first failing stage). **Row stays Open** until full chain green; **next bounded slice** = **Contract Tests** failure analysis (do not re-open Seam A-D unless regression).

### Bounded slice (active): STAGE 18 — Resume-leg Contract Tests (`24417704302`)

**Frozen scope (only):** **`workflow_dispatch` resume leg** → **STAGE 18: Contract Tests** — pytest exit **1** on **`main @ 6772b9b3`** (hosted). **Not** Seam A-D, audio guards, venv guards, or harness plumbing.

**Evidence anchor:** run **`24417704302`**; resume **`summary.json`**: **Python Unit Tests** **PASSED**; **Contract Tests** **FAILED** (19 failed, 205 passed); artifact **`logs/contract_tests.log`** / **`test-results/contract_tests.xml`**.

**Failure clusters (19 tests, grouped for fix order):**

| Cluster | Count | Theme | Examples |
| --- | --- | --- | --- |
| **1** | 10 | Engine manifest v3 migration not on `main` | Duplicate legacy `engines/bark/` vs `engines/audio/bark/`; old `type`/`capabilities`/`dependencies` shape; `coqui_tts`/`styletts2` incomplete v3 fields |
| **2** | 5 | Contract test expectations vs current API/OpenAPI | POST without `requestBody` whitelist; `/api/voice/voices`; paginated profiles; shared schema `$ref` / `items` |
| **3** | 3 | WebSocket route visibility in `TestClient` | `/ws/realtime`, `/ws/events`, `/ws/plugins` 404 |
| **4** | 1–2 | Environment / optional deps | `aiohttp` import in plugin contract test; library DB table on cold runner |

**Remediation (repo):** single coherent commit — remove duplicate legacy engine manifests, finalize v3 manifests under `engines/audio/` and `engines/llm/`, align `tests/contract/*` and backend route registration with **`shared/schemas/engine_manifest_v3.schema.json`**. Prove locally: **`pytest tests/contract`** then **`verify.ps1 -OnlyStage "Contract Tests"`**.

**Hard OUT:** reopen Seam A-D; broad unrelated refactors; mixing unrelated backend slices in the same “contract” commit beyond what contract tests require.

**Exit:** green **`workflow_dispatch`** + **`run_full_chain: true`** with **Contract Tests** **PASSED** on resume, or freeze **next** failing stage only if a different stage fails first after this lands.

**Remediation committed (2026-04-14 — `8ad0a26e` on `main`):** Remove duplicate legacy **`engines/{bark,chatterbox,openvoice,piper,whisper,xtts}/engine.manifest.json`**; finalize v3 manifests under **`engines/audio/`** / **`engines/llm/`**; align **`tests/contract/*`** + voice route registration with **`shared/schemas/engine_manifest_v3.schema.json`**. **Local proof:** **`pytest tests/contract`** **238** passed / **5** skipped; **`verify.ps1 -OnlyStage "Contract Tests"`** **PASS** **`artifacts/verify/20260414_163511/`**. **Current tip:** **`34725e1f`** (governance-docs commit on top of **`8ad0a26e`**; remote HEAD confirmed).

**Next:** operator **`workflow_dispatch`** + **`run_full_chain: true`** — fine-grained PAT must include **Actions: Read and write** (or classic **`workflow`**); **`gh workflow run`** may return **HTTP 403** if **`GITHUB_TOKEN`** overrides keyring without dispatch scope — **unset `GITHUB_TOKEN`** for **`gh`** or use **Actions UI**. Record run URL/ID/SHA here when observed.

### GHA proof — `24424525825` (2026-04-14) — **false checkpoint failure (harness)**

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24424525825 |
| **Run ID** | `24424525825` |
| **Commit SHA** | `1695cd347016bbf9ef322ba555bb27579dd0547b` (truth-sync docs on `main`) |
| **Verify Quick Gate** | **success** (~11m14s) |
| **Verify Checkpoint + Resume Chain** | **failure** — step **Checkpoint run (stop after C# Unit Tests)** exit **1** |
| **Resume leg** | **skipped** (checkpoint step failed) |

**Observed harness behavior:** All checkpoint stages **STAGE 1–16** reported **PASSED** (including **STAGE 16: C# Unit Tests - Other**). **`[StopAfterStage]`** ran, then **`##[error]Process completed with exit code 1`**.

**Root cause:** [`scripts/verify.ps1`](../../scripts/verify.ps1) **`Invoke-StopIfRequested`** exited with **`$script:OverallPassed`**, which was **never initialized to `$true`** (only **`$OverallPassed`** was). On an all-green checkpoint, **`$script:OverallPassed`** stayed **`$null`** → **exit 1** despite success.

**Remediation (repo):** Initialize **`$script:OverallPassed = $true`** with **`$OverallPassed`**; keep both in sync on failures; **`Invoke-StopIfRequested`** exits on **`$OverallPassed`**. **Local proof:** **`verify.ps1 -StopAfterStage "C# Unit Tests - Other"`** (narrow skips) → **exit 0**; artifact dir **`artifacts/verify/20260414_173048/`**.

**Contract Tests slice (`8ad0a26e`):** **not** invalidated — resume leg **not reached** on this run. **Row stays Open** until a hosted **`workflow_dispatch`** **full chain** **green** after the harness fix lands on **`main`**.

### GHA proof — `24426420065` (2026-04-14) — **checkpoint green; resume red**

| Field | Value |
| --- | --- |
| **Run URL** | https://github.com/wtsteward11/VoiceStudio/actions/runs/24426420065 |
| **Run ID** | `24426420065` |
| **Commit SHA** | `7835f8fb4fb0232dc4a2405a522975061311f65a` (includes **`verify.ps1`** StopAfter fix) |
| **Verify Quick Gate** | **success** |
| **Checkpoint run (stop after C# Unit Tests)** | **success** — harness **`StopAfterStage`** fix **confirmed** |
| **Resume run** | **failure** — **`[Contract Tests] FAILED (exit code 1)`** (fail-fast after resume pipeline) |

**Collection error on resume (Python Unit Tests / import):** **`test_search.py`** collection → **`RuntimeError: Database not connected`** from **eager** module-level **`get_projects_for_search()`** in **`backend/api/routes/search.py`** on **`main`** (pre-**`903b4031`**). **Remediation:** **`903b4031`** — lazy **`_load_search_storage()`** (GAP-069 slice 5). **Re-dispatch** **`workflow_dispatch`** after push to re-prove resume + Contract Tests.

**Bounded slice (active):** **STAGE 18 — Contract Tests** on resume **or** **import/collection** unblock — **freeze** until hosted green on tip **`903b4031`**+ (do **not** reopen Seam A-D / checkpoint harness unless regression).

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
