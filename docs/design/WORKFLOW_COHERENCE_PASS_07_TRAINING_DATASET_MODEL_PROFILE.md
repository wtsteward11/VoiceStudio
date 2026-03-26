# Workflow Coherence Pass 07 — Training / Dataset / Model / Profile

**Purpose:** Bounded **planning-first** pass to determine whether the **Training** workflow cluster can move from **honest partial** (Product Trust [§2](PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md)) toward **closure-grade coherence**: dataset preparation, job lifecycle, model visibility, and handoff to **usable voice output** (profiles / synthesis)—without rewriting engines or claiming full ML production coverage in one swoop.

**Date:** 2026-03-26  
**Status:** **W7-C1 closed** (Profiles-first) — proof in **§8.2**. **Workflow 7 implementation lane — paused after W7-C1** (**§8.4**): no further Training-cluster **`src/`** until product signs a **new §5 / §8** row (e.g. **W7-C2**). **[Product trust Pass 01](PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md)** remains **paused** (§8.9 Option 1). **P05-Persist-A4** remains **§12-gated**. **Pass 06** further `src/` requires a **new §5** row.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) (Workflow 7), [PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md](PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md) (Training partial disclosure), [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md) (retained-async discipline on Training VM).

---

## 1. Purpose and scope

### 1.1 In scope (this pass)

- **Code-truth** map of the **as-is** Training cluster: panels, clients, events, and stop-short points.
- **Defect / coherence** inventory with stable IDs (symptoms tied to files and seams).
- **Bounded change matrix**: at most **one** implementation row active per product sign-off (starting with **W7-C1** recommendation below).
- **Strict OUT** list and **proof expectations** before any implementation.

### 1.2 Out of scope (unless a future signed row explicitly adds them)

- Engine training backend redesign, new training algorithms, or CUDA/tooling changes.
- **Product Trust** copy-only slices (lane **paused**).
- **Pass 05** **A4** (drag-drop → project parity).
- **Pass 06** restore/backup scope beyond an authorized §5 row.
- **Quality / benchmark** panels as primary owners (see Workflow 8).
- Broad “fix all training UX” or full dataset-editor overhaul in a single slice.

---

## 2. Code-truth owner map

Surfaces that **actually exist** in repo (2026-03-26 read). Paths relative to repo root.

| Owner | Path | Role |
|-------|------|------|
| **Training panel** | [src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs) | Primary VM: datasets, create/delete dataset, **start/cancel/delete jobs**, logs, quality history, simulation InfoBar, **WebSocket** job progress + fallback **polling**, `JobStartedEvent` / `ProfileCreatedEvent` on completion path |
| **Training view** | [src/VoiceStudio.App/Views/Panels/TrainingView.xaml](../../src/VoiceStudio.App/Views/Panels/TrainingView.xaml) (and `.xaml.cs`) | Binds to VM; hosts **SurfaceMaturityFootnote** (Product Trust slice 3) |
| **Training API client** | [src/VoiceStudio.App/Core/Services/ITrainingClient.cs](../../src/VoiceStudio.App/Core/Services/ITrainingClient.cs), implementation under `Services/` | HTTP to backend training endpoints |
| **Dataset editor** | [src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs](../../src/VoiceStudio.App/ViewModels/TrainingDatasetEditorViewModel.cs), [src/VoiceStudio.App/Core/Services/ITrainingDatasetEditorClient.cs](../../src/VoiceStudio.App/Core/Services/ITrainingDatasetEditorClient.cs), [src/VoiceStudio.App/Services/TrainingDatasetEditorClient.cs](../../src/VoiceStudio.App/Services/TrainingDatasetEditorClient.cs) | Advanced dataset editing (separate panel) |
| **Dataset QA** | [src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs](../../src/VoiceStudio.App/ViewModels/DatasetQAViewModel.cs), [src/VoiceStudio.App/Core/Services/IDatasetQAClient.cs](../../src/VoiceStudio.App/Core/Services/IDatasetQAClient.cs) | QA reports / cull flows (**not** the same type as “DatasetQAClient” — interface is `IDatasetQAClient`) |
| **Training quality viz** | [src/VoiceStudio.App/ViewModels/TrainingQualityVisualizationViewModel.cs](../../src/VoiceStudio.App/ViewModels/TrainingQualityVisualizationViewModel.cs) | Secondary training-adjacent panel |
| **Model manager** | [src/VoiceStudio.App/Views/Panels/ModelManagerViewModel.cs](../../src/VoiceStudio.App/Views/Panels/ModelManagerViewModel.cs), [src/VoiceStudio.App/Core/Services/IModelManagerClient.cs](../../src/VoiceStudio.App/Core/Services/IModelManagerClient.cs) | Model list/register **separate** from `TrainingViewModel` ctor (Training VM does **not** inject `IModelManagerClient` today) |
| **Job progress transport** | `JobProgressWebSocketClient` (via `TrainingViewModel` WebSocket factory) | Real-time updates; completion/failure handlers |
| **Events** | `JobStartedEvent`, `ProfileCreatedEvent` (published from Training completion path) | Cross-panel hooks — subscribers must be validated under Pass 07 |

**Registration:** Panels registered in [src/VoiceStudio.App/Services/CorePanelRegistrationService.cs](../../src/VoiceStudio.App/Services/CorePanelRegistrationService.cs) (and related registry services) — verify when adding navigation or freeze rows.

---

## 3. As-is workflow map (code-truth)

### 3.1 Entry: Training panel

1. User opens **Training** panel; `InitializeAsync` loads **datasets** and **training jobs** via `ITrainingClient`.
2. User selects **dataset** (`SelectedDataset`) and **profile id** (`SelectedProfileId` string), engine/epochs/hyperparameters.
3. **StartTrainingCommand** → `StartTrainingAsync` builds `TrainingRequest` (dataset + profile + engine + hyperparams) → `ITrainingClient.StartTrainingAsync`.
4. On success: job inserted/selected, **jobs list reloaded**, toast **Training.TrainingStarted**, **`JobStartedEvent`** published (when `IEventAggregator` wired).

### 3.2 Progress and completion

5. Progress: **WebSocket** handlers update in-memory `TrainingStatus` rows; fallback **PollTrainingStatusAsync** (~2s) when WebSocket unavailable.
6. On **JobCompleted** (WebSocket): job marked **completed**, toast “Training job completed successfully”, **`ProfileCreatedEvent`** published when `job.ProfileId` non-empty and aggregator present; **logs + jobs list refreshed** (`LoadLogsAsync`, `LoadTrainingJobsAsync`) using disposal token.

### 3.3 Adjacent surfaces (not unified in one VM)

7. **Dataset Editor** and **Dataset QA** are **separate panels** with their own clients; there is **no** single orchestrated “wizard” in code linking Training → Model Manager → Profiles in one VM.
8. **ModelManagerViewModel** consumes **`IModelManagerClient`** independently — coherence of “trained artifact visible where user expects” is **not proven** by Training panel alone.

### 3.4 Stop-short summary

| Step | User may believe | Code actually does |
|------|------------------|-------------------|
| After “training started” | End-to-end training lifecycle is “closed” | Product Trust + matrix: **partial**; simulation path possible; no workflow §8 proof |
| After “completed” | New voice is automatically discoverable everywhere | **ProfileCreatedEvent** + profile id on job — **downstream subscriber behavior** determines refresh; **no** automatic navigation to Model Manager or Synthesis |
| Dataset / QA / editor | One connected training story | **Multiple panels** — handoff depends on user navigation and panel state |

### 3.5 Subscriber graph (`ProfileCreatedEvent`, code-truth)

| Role | Type / location | Subscribe timing | On event behavior | Disposal / gaps |
|------|-----------------|------------------|-------------------|-----------------|
| **Publisher** | [`TrainingViewModel.OnTrainingJobCompleted`](../../src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs) | — | Publishes `ProfileCreatedEvent(PanelId.Training, job.ProfileId, profileName)` when `job.ProfileId` non-empty and `_eventAggregator` non-null | — |
| **Subscriber** | [`ProfilesViewModel.OnProfileCreatedRefresh`](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs) | **Constructor** — `_eventAggregator.Subscribe<ProfileCreatedEvent>(…)` | Skips if `evt.SourcePanelId == PanelIds.Profiles`; reloads list (or selects if id already present); **W7-C1** selects `evt.ProfileId` after reload — see §8 | **`OnDeactivatedAsync`** disposes `_profileCreatedToken` — **no events while Profiles panel deactivated** |
| **Subscriber** | [`LibraryViewModel.OnProfileCreatedRefresh`](../../src/VoiceStudio.App/ViewModels/LibraryViewModel.cs) | **`OnActivatedAsync`** → `EnsureEventSubscriptions` only | `CoalescedLoadAssetsAsync()` — **Library never subscribes until first activation** | Deactivate clears tokens (pattern in same file) |
| **Other** | [`VoiceCloningWizardViewModel`](../../src/VoiceStudio.App/ViewModels/VoiceCloningWizardViewModel.cs) | Publishes for clone flow | Separate from Training completion | — |
| **Not subscribed** | `ModelManagerViewModel` | — | **No** `ProfileCreatedEvent` subscription — model-list coherence is **not** this event | Any Model Manager row = **separate** mechanism (future W7-Cx) |

**Interpretation:** Publication does **not** guarantee Profiles UI refresh: **`DispatcherQueue.GetForCurrentThread()`** in `OnProfileCreatedRefresh` must be non-null for the enqueued `LoadProfilesAsync` to run. Tests and headless hosts must run publishes from a thread with a WinUI dispatcher when asserting behavior.

---

## 4. Defects / coherence gaps (initial inventory)

**Rule:** Each row must be **reproducible** from code or manual steps; priorities revised after §8 sign-off. IDs are stable for Pass 07.

| ID | Symptom | Owner / seam | Stop-short / code anchor | Priority |
|----|---------|----------------|--------------------------|----------|
| W7-D1 | After training completes, **Profiles** list refreshed but **new profile not selected** — user does not see “what to use next” in Profiles context | [`ProfilesViewModel.OnProfileCreatedRefresh`](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs) | **Pre–W7-C1:** `TryEnqueue` ran `LoadProfilesAsync` only; **`SelectedProfile` unchanged** after reload — **W7-C1 addresses** (select `evt.ProfileId` after successful load). If `TryEnqueue` null (no dispatcher), reload never runs. | High |
| W7-D2 | **Model Manager** list stale vs new trained artifact | [`ModelManagerViewModel`](../../src/VoiceStudio.App/Views/Panels/ModelManagerViewModel.cs), `IModelManagerClient` | **No** `ProfileCreatedEvent` subscriber on Model Manager — **OUT for W7-C1**; orthogonal **W7-Cx** or manual refresh. **Not** a Training→Profiles event bug. | Low (deferred out of W7-C1) |
| W7-D3 | **Multiple panels** (Training, Dataset Editor, Dataset QA) — duplicated or conflicting dataset state | `TrainingDatasetEditorViewModel`, `DatasetQAViewModel`, `TrainingViewModel` | No single orchestration VM; **Library** only listens after `OnActivatedAsync` — see §3.5 | Med |
| W7-D4 | **ListTrainingJobsAsync** scoped by `SelectedProfileId` — empty or wrong filter may **hide** jobs | `LoadTrainingJobsAsync` | Behavioral edge cases when profile changes while jobs exist | Med |
| W7-D5 | **WebSocket absent** (tests / degraded env): polling path only; **stale** UI until poll tick | `PollTrainingStatusAsync`, `StartPolling` | Documented in TRAINING_VIEWMODEL lifecycle patterns; still a **coherence** risk for “completed” perception | Med |
| W7-D6 | **Simulation mode** (MED-1): user may think non-sim training ran | `IsSimulationMode`, InfoBar + footnote | Partially mitigated by Product Trust footnote; still a trust surface | Low |

---

## 5. Bounded change matrix

Only **one** row may be **in progress** in implementation at a time. All rows require **§8** sign-off before `src/`.

| Row ID | Hypothesis | Primary owner | Supporting owner (exactly one path) | Initial tests / proof |
|--------|------------|---------------|-------------------------------------|------------------------|
| **W7-C1** (signed) | **Training completion → Profiles coherence (Profiles-first):** when another panel publishes `ProfileCreatedEvent` with a new `ProfileId`, Profiles **reloads** and **selects** that profile so `IContextManager` / synthesis consumers see active profile without manual hunt | [`TrainingViewModel`](../../src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs) (publisher unchanged) | **[`ProfilesViewModel`](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs)** only — **`ModelManagerViewModel` OUT** for this slice | **FQN** `ProfilesViewModelSeamTests`; `verify.ps1 -Quick` **separate** |
| W7-C2 | Dataset editor ↔ training **consistency** (TBD — only if W7-C1 closed and product re-prioritizes) | `TrainingDatasetEditorViewModel` | `TrainingViewModel` | TBD at sign-off |
| W7-C3 | Dataset QA handoff to training (TBD) | `DatasetQAViewModel` | `TrainingViewModel` | TBD at sign-off |

**W7-C1 rationale:** Highest user harm when “training finished” does not line up with **discoverability** of usable output (profile/synthesis path)—matches mentor recommendation for first bounded row.

---

## 6. Strict OUT list (Pass 07 — default)

Until **§8** expands scope:

- **OUT:** Engine / CUDA / training algorithm / batch remote runner changes.
- **OUT:** New FastAPI routes or `training` backend rewrite.
- **OUT:** **Product Trust** execution slices (lane paused).
- **OUT:** **A4** drag-drop parity; **Pass 06** `backup.py` or restore semantics.
- **OUT:** Quality Benchmark / Workflow 8 as **primary** owner in the same slice as W7-C1.
- **OUT:** **`ModelManagerViewModel` / Model Manager panel** as supporting owner in **W7-C1** (separate row if needed).
- **OUT:** Full dataset UX overhaul, global string sweeps, or feature-gating bundles.

---

## 7. Proof expectations (implementation phase)

When **§8** authorizes a row:

1. **Build:** `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — zero errors in changed scope.
2. **Seam / unit:** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~<ExactSeamClassFQN>"` — count **frozen in §8** (full class FQN, no lazy substring).
3. **Verify:** `.\scripts\verify.ps1 -Quick` — new `artifacts/verify/<timestamp>`; `latest_pointer.json` advances.
4. **Rule:** **Quick verify does not subsume** targeted seam proof.

---

## 8. Sign-off and execution record

| Milestone | Status | Notes |
|-----------|--------|-------|
| Planning doc accepted (§1–§6 baseline) | **Complete** (2026-03-26) | Tyler (product/engineering) — §3.5 subscriber graph + W7-D1/D2 sharpened |
| Execution row W7-C1 frozen | **Complete** (2026-03-26) | §8.1 — Profiles-first; **Model Manager OUT** |
| Implementation W7-C1 | **Complete** (2026-03-25) | `ProfilesViewModel.OnProfileCreatedRefresh` + `ProfilesViewModelSeamTests` (2) |
| Closure W7-C1 | **Complete** (2026-03-25) | Seam **2** passed; Quick **`artifacts/verify/20260325_162114`** (PASS); Quick ≠ seam |
| Workflow 7 pause after W7-C1 | **Complete** (2026-03-25) | **§8.4** — default **off** for Training-cluster `src/`; reopen = new signed §8 only |

### 8.1 Execution row W7-C1 — sign-off (frozen)

**Workflow Coherence Pass 07 — W7-C1 authorized — 2026-03-26 — Tyler (product/engineering)**

| | |
|--|--|
| **IN** | **Profiles-first:** On `ProfileCreatedEvent` from **non-Profiles** panels (e.g. `PanelIds.Training`), after `LoadProfilesAsync`, set **`SelectedProfile`** to the profile whose **`Id` == `evt.ProfileId`** when present in the loaded list; if profile **already** in `Profiles` before reload, **set selection** to that profile (no redundant load). **Downstream:** `OnSelectedProfileChanged` drives context / `ProfileSelectedEvent` as today. |
| **OUT** | **`ModelManagerViewModel`**, dataset editor/QA, `TrainingViewModel` publisher changes (Training already publishes), backend/engine routes, Quality/Workflow 8, Product Trust execution, A4, Pass 06 |
| **File lock** | [`ProfilesViewModel.cs`](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs) only |
| **Proof commands** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`; `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceStudio.App.Tests.ViewModels.ProfilesViewModelSeamTests"` — **expected 2 passed**; `.\scripts\verify.ps1 -Quick` |
| **Proof rule** | **Quick verify does not subsume** the seam filter; full **class FQN** only |

### 8.2 W7-C1 implementation proof (closure record)

| | |
|---|---|
| **Seam filter** | `FullyQualifiedName~VoiceStudio.App.Tests.ViewModels.ProfilesViewModelSeamTests` |
| **Seam passed** | **2** |
| **Quick artifact** | **`artifacts/verify/20260325_162114`** (`verify.ps1 -Quick` PASS; does not subsume seam) |
| **Files touched** | `ProfilesViewModel.cs`, `ProfilesViewModelSeamTests.cs` |

### 8.3 Scope reminder

Single downstream path: **Profiles** only. Do not expand to Model Manager in the same slice.

### 8.4 Workflow 7 — continuation / pause (governance)

**Decision recorded (2026-03-25) — Tyler (product/engineering):** **Pause** the Training-cluster **implementation lane** after **W7-C1**. One narrow coherence slice does **not** authorize automatic **W7-C2** or open-ended “training UX” work.

**Reopen only when all are true:**

1. **New** bounded row in **§5** (single defect / single primary path) and **signed §8** (file lock, full **FQN** seam filter, expected test count, proof commands).
2. **Ranked** product justification — not scope creep across dataset editor, Model Manager, and backend in one pass.

**Explicit OUT until reopen:** Training-cluster **`src/`** without §8; **Product Trust** honesty execution; **A4** (without Option A §12); **Pass 06** `src/` without Pass 06 §5. Candidate defects remain listed in **§4** (planning-only inventory).

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-25 | **§8.4:** Workflow 7 **paused after W7-C1**; milestone row; status banner sync |
| 2026-03-25 | **W7-C1 closure:** `ProfilesViewModel` selection after `ProfileCreatedEvent`; **`ProfilesViewModelSeamTests`**; §8.2 proof **`20260325_162114`** / seam **2** |
| 2026-03-26 | §8.1 W7-C1 frozen (Profiles-first); §3.5 subscriber graph; W7-D1/D2 tightened; OUT Model Manager for W7-C1 |
| 2026-03-26 | Initial planning freeze: owner map, as-is map, defects W7-D1–D6, matrix W7-C1–C3, OUT §6, proof §7, execution placeholder §8 |
