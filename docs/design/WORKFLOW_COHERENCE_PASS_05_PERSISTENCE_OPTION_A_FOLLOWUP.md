# Pass 05 — Persistence follow-up (Option A: transcribe → project audio)

**Purpose:** Bounded **execution** matrix for **Option A** from [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md): after **successful transcription**, when an **active project** is selected, persist **source library audio** to the project via **`IProjectAudioClient.SaveAudioToProjectAsync`**.

**Date:** 2026-03-25  
**Status:** **P05-Persist-A1 — complete**. **P05-Persist-A2 — complete**. **P05-Persist-A3 — complete** (2026-03-25): batch **`LibraryUseCase.ImportFilesAsync`** → per-item **`SaveAudioToProjectAsync`** when **`IContextManager.ActiveProjectId`** set; Quick **`artifacts/verify/20260325_044801`**; seam §7 **50 passed**. Proof §8. **P05-Persist-A4** — §12 **execution-grade freeze** (**2026-03-26**): **Path A** (`LibraryView` drag-drop bounded persistence) vs **Path B** (**OUT** for A4 v1 — route through **`ImportFilesAsync`**). **No `src/`** until §1 sign-off row authorizes **A4**.

**Parent:** [WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md](WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md). **Sibling (closed):** [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md) (Option C — record-only; **do not** reopen for Option A work). **Policy reference:** [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) — **Option B** frozen; **do not** edit C3-OptB matrix.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) Workflow 5.

---

## 1. Sign-off

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | **P05-Persist-A1** accepted: **transcribe-only**; after `TranscribeAudioAsync` succeeds, if `SelectedProjectId` set, call `SaveAudioToProjectAsync` once for source `AudioId`; failures **non-blocking** with honest UX; **import** explicitly **OUT** of A1. | **2026-03-25 — Tyler** |
| **Product / engineering** | **P05-Persist-A2** accepted: **single-file shell import only** ([`ImportWorkflowService.ImportAudioFileAsync`](../../src/VoiceStudio.App/Services/ImportWorkflowService.cs)); after `UploadLibraryAssetAsync` succeeds, if **`IContextManager.ActiveProjectId`** ([`IContextManager`](../../src/VoiceStudio.Core/Services/IContextManager.cs) § Active State) is non-empty, call `SaveAudioToProjectAsync` once with **playback/library id** (`GetPlaybackAudioId` ?? `uploadedAsset.Id`) and **filename** from `Path.GetFileName(filePath)`; failures **non-blocking** (log **SaveImportToProject**); **batch** / `LibraryUseCase.ImportFilesAsync` **OUT** of A2. | **2026-03-25 — Tyler** |
| **Product / engineering** | **P05-Persist-A3** accepted: **`ImportFilesAsync` only** (POST `/api/library/import` via [`LibraryUseCase`](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs)); after non-empty **`ImportedItems`**, if **`ActiveProjectId`** set → **one** `SaveAudioToProjectAsync` **per item** with non-empty **`LibraryItem.Id`**; filename hint = **`Path.GetFileName(paths[i])`** when index aligned, else **`Path.GetFileName(item.Name)`**; failures **non-blocking**, log **`SaveBatchImportToProject`** per failure, **continue** remaining items; **no** `AssetAddedEvent` change in v1; **no** Library **drag-drop** / **`UploadAudioFileAsync`** multi-file path in v1; **no** new toasts from use case (no production caller yet). | **2026-03-25 — Tyler** |
| **Product / engineering** | **P05-Persist-A4** — **Pending sign-off.** Execution freeze: **Path A** — bounded persistence in [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) drag-drop path (per §11.1 **`UploadAudioFileAsync`** loop); optional small helper [`LibraryDragDropToProjectPersistence`](../../src/VoiceStudio.App/Services/) (new file); **`Path B`** (consolidate drag-drop onto **`ImportFilesAsync`**) **OUT** of A4 v1 — requires **new** matrix row + blast-radius sign-off. Full matrix §12.2–12.4. **No code until this row is dated.** | **—** |

---

## 2. Policy freezes (before code)

### 2.1 Paths in scope

| Slice | IN | OUT |
|-------|----|-----|
| **A1** | **Transcribe panel** — success path of `TranscribeAsync` only | **Import** / `LibraryUseCase.ImportFilesAsync` / batch `AssetAddedEvent` contract / timeline clip model / Pass 06 / `backup.py` |

**Import** requires a **separate** matrix row (**P05-Persist-A2** or equivalent) + sign-off — **not** A1.

### 2.2 Trigger rule (frozen)

After **`ITranscriptionClient.TranscribeAudioAsync`** returns a successful **`TranscriptionResponse`**:

- If **`SelectedProjectId`** is non-null and non-whitespace **and** response **`AudioId`** is non-empty → call **`IProjectAudioClient.SaveAudioToProjectAsync(SelectedProjectId, audioId, filename: null, ct)`** exactly **once** for this success.
- Otherwise → **no** project save (library-only semantics unchanged).

### 2.3 Success vs partial success (user-visible)

| Outcome | Behavior |
|---------|----------|
| Transcribe **fails** | Existing error path; **no** project save attempted. |
| Transcribe **OK**, no project | Existing **C3** toast + hint (library-only). |
| Transcribe **OK**, project set, save **OK** | Success toast + hint: source audio **also** copied to project audio (resource **A1** keys). |
| Transcribe **OK**, project set, save **fails** | Transcription result **unchanged**; toast/detail + hint state **honest partial success** — transcribe OK, project copy failed (see **A1** keys); error **logged**. |

### 2.4 Boundary with Option C

**Option C** owns **record** → project bridge. **Option A** does **not** change `RecordingViewModel` or Option C doc §8 without a separate decision.

---

## 3. As-is workflow map (pre–A1)

1. User selects library **audio id**; optional **project** syncs from `IContextManager` / `ProjectChangedEvent` (**Pass 05 C1**).
2. **Transcribe** runs → library asset + transcript; **C3 Option B** hints said source stayed library-only unless other flows.
3. **Option A1** adds: when project active, **also** copy source audio into project store via existing API.

---

## 4. Defect / friction (why A1)

- Users working **inside a project** expect **source audio** associated with that project when they transcribe, not only a transcript + library asset.
- Without persistence, **C3** honestly said “not in project” — correct but **gap** vs product expectation for project-scoped work.
- **Risk:** silent failure or messaging that implies project save when it did not occur — **mitigated** by §2.3 and tests.

---

## 5. Bounded matrix — **P05-Persist-A1**

| Field | Content |
|-------|---------|
| **ID** | **P05-Persist-A1** |
| **Target** | Post-`TranscribeAudioAsync` success: **`TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync`** when project + audio id set; update toast/hint per §2.3 |
| **Primary owner** | [`TranscribeViewModel`](../../src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs) (`TranscribeAsync`) |
| **Supporting** | [`TranscribeToProjectPersistence`](../../src/VoiceStudio.App/Services/TranscribeToProjectPersistence.cs); [`IProjectAudioClient`](../../src/VoiceStudio.App/Core/Services/IProjectAudioClient.cs); [`TranscribeView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/TranscribeView.xaml.cs) DI |
| **Tests** | `TranscribeToProjectPersistenceTests` (unit); `TranscribeViewModelSeamTests` — constructor + transcribe + project save / no project / save throws |
| **Proof** | §7 |

---

## 6. IN / OUT

### 6.1 IN (A1)

- `TranscribeViewModel` takes **`IProjectAudioClient`**; after successful transcribe, invoke **`TranscribeToProjectPersistence`**.
- Resource keys **`Transcribe.A1.*`** for honest copy when project save runs or fails.
- Unit + seam tests; governance after green proof.

### 6.2 OUT (A1)

- Import-only / batch persistence.
- `SaveAudioToProjectAsync` from **`SendSelectedTranscriptionToTimeline`** or non-transcribe paths (**separate** row if needed).
- Changing **Option C** record path.
- Timeline overlay / clip semantics rewrite.

---

## 7. Proof expectations

**Seam filter (Pass 05 lane + persistence):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam|FullyQualifiedName~RecordingToProjectPersistenceTests|FullyQualifiedName~TranscribeToProjectPersistenceTests|FullyQualifiedName~ImportToProjectPersistenceTests|FullyQualifiedName~ImportWorkflowServiceTests|FullyQualifiedName~LibraryUseCaseImportFilesPersistenceTests"
```

**Baseline before A1:** **27** passed (Option C hardened). **After A1:** **35** passed. **After A2:** **43** passed. **After A3 (authoritative):** **50** passed (adds `LibraryUseCaseImportFilesPersistenceTests` + batch rows in `ImportToProjectPersistenceTests`; 2026-03-25).

**Quick verify** (does **not** subsume seam):

```text
.\scripts\verify.ps1 -Quick
```

Align **`artifacts/verify/latest_pointer.json`** and **`verification_report.md`**.

---

## 8. Execution record

| Slice | Status | Proof (Quick artifact) | Seam note | Notes |
|-------|--------|------------------------|-----------|-------|
| **P05-Persist-A1** | **Complete** (2026-03-25) | **`artifacts/verify/20260325_035320`** | Seam filter §7: **35 passed** (Quick does not subsume seam) | `TranscribeToProjectPersistence`; `TranscribeViewModel` + `TranscribeView` DI; `Transcribe.A1.*`; `TranscribeToProjectPersistenceTests` + extended `TranscribeViewModelSeamTests` |
| **P05-Persist-A2** | **Complete** (2026-03-25) | **`artifacts/verify/20260325_042444`** | Seam filter §7: **43 passed** (Quick does not subsume seam) | `ImportToProjectPersistence`; `ImportWorkflowService` + `ApplyPostSingleFileLibraryImportSuccessAsync` (public seam for tests); `AppServices` DI; `ImportToProjectPersistenceTests` + `ImportWorkflowServiceTests` |
| **P05-Persist-A3** | **Complete** (2026-03-25) | **`artifacts/verify/20260325_044801`** | Seam filter §7: **50 passed** (Quick does not subsume seam) | `ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync`; `LibraryUseCase.ImportFilesAsync` + ctor (`IContextManager`, `IProjectAudioClient`, `IErrorLoggingService?`); `AppServices` `ILibraryUseCase`; `LibraryUseCaseImportFilesPersistenceTests`; extended `ImportToProjectPersistenceTests` |
| **P05-Persist-A4** | **Planned** (execution freeze **§12.2**; §1 sign-off **pending**) | — | Baseline §7 filter **50** passed → target **53** (**+3** `LibraryDragDropToProjectPersistenceTests`); Quick TBD on implementation close | **Path A** only until §1 authorizes; see §12 **OUT** |

---

## 9. Changelog

| Date | Note |
|------|------|
| 2026-03-25 | Initial doc; policy §2; matrix **P05-Persist-A1**; sign-off; §8. |
| 2026-03-25 | **A1 delivered:** helper + VM + tests + resources; proof row filled after verify. |
| 2026-03-25 | **A2 freeze + delivery:** §10 matrix **P05-Persist-A2**; sign-off; import single-file persistence; tests + §8 row. |
| 2026-03-25 | **A3 freeze + delivery:** §11 **P05-Persist-A3**; batch `ImportFilesAsync` persistence; §7 filter **50**; proof **`20260325_044801`**. |
| 2026-03-24 | **Post-A3 lane pick (default):** product **defers** library drag-drop → project parity (**P05-Persist-A4** not opened); Workflow 5 bounded persistence considered **sufficient for now** — pick next lane in STATE / [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md). §11.1 caller audit hardened with reproducible `rg` scope. |
| 2026-03-24 | **§12 / A4 planning shell:** frozen questions + OUT list for **P05-Persist-A4** (drag-drop parity); §8 **Planned** row; next implementation lane = [Pass 06 slice 4](WORKFLOW_COHERENCE_PASS_06_BACKUP_RESTORE_PROJECT_SETTINGS_PROFILE_RECOVERY.md) per STATE |
| 2026-03-26 | **STATE active lane:** **P05-Persist-A4** planning-only — finalize §12 + §1 authorization before **`src/`**; [`.cursor/STATE.md`](../../.cursor/STATE.md) records **`stash@{0}`** read-only inspection + **parked** disposition (83 paths; no pop). |
| 2026-03-26 | **§12 execution-grade freeze:** **Path A** — [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) + new **`LibraryDragDropToProjectPersistence`** (`Services/`); **Path B** (drag-drop → **`ImportFilesAsync`**) **OUT** for A4 v1. §1 **P05-Persist-A4** row **Pending** (product sign-off). Seam **50** → **53** (**+3** `LibraryDragDropToProjectPersistenceTests`); §8 row + §12 matrix locked until §1 dated. |

---

## 10. P05-Persist-A2 — single-file import → project audio

**Purpose:** Close the remaining **shell import** gap in Workflow 5: after a **single** user-selected file is uploaded to the library via [`ImportWorkflowService`](../../src/VoiceStudio.App/Services/ImportWorkflowService.cs), optionally copy the **library audio** into the active project — same API as A1/C1 (`IProjectAudioClient.SaveAudioToProjectAsync`).

**Sibling slices:** **A1** (transcribe) and **C1** (record upload) stay **closed**; do not change their §8 rows from this slice.

### 10.1 Project id source (verified)

| API | Location |
|-----|----------|
| **`IContextManager.ActiveProjectId`** | [`IContextManager`](../../src/VoiceStudio.Core/Services/IContextManager.cs) — `string?`; “Currently active project ID.” |

**Trigger:** After **`ILibraryClient.UploadLibraryAssetAsync`** returns a non-null **`LibraryAsset`**, if **`ActiveProjectId`** is non-null and not whitespace, invoke persistence **once** per successful import.

### 10.2 Policy freezes (A2)

| Decision | Frozen choice |
|----------|----------------|
| **Paths in scope** | **Only** [`ImportWorkflowService.ImportAudioFileAsync`](../../src/VoiceStudio.App/Services/ImportWorkflowService.cs) (picker → single upload). |
| **Library audio id for save** | **`GetPlaybackAudioId(uploadedAsset) ?? uploadedAsset.Id`** (same id as **`AssetAddedEvent`** `playbackId`). |
| **Filename argument** | **`Path.GetFileName(filePath)`** when local path non-empty (aligned with [`RecordingToProjectPersistence`](../../src/VoiceStudio.App/Services/RecordingToProjectPersistence.cs)). |
| **Success vs partial** | Import **success** toast and transport behavior **unchanged** if project save fails; failure **logged** only (**SaveImportToProject** context) — **non-blocking** (mirrors C1 logging). |
| **Order vs transport** | **Persist** then **`AssetAddedEvent`** then **`SetCurrentPlayable`** / **`SetActiveAsset`** (mirrors **C1** order: save before transport/event in [`RecordingViewModel.ApplyPostLibraryUploadSuccessAsync`](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs)). |

### 10.3 Bounded matrix — **P05-Persist-A2**

| Field | Content |
|-------|---------|
| **ID** | **P05-Persist-A2** |
| **Target** | Post-success single-file import: **`ImportToProjectPersistence.TrySaveAfterSingleFileImportAsync`** when **`ActiveProjectId`** and library id set |
| **Primary owner** | [`ImportWorkflowService`](../../src/VoiceStudio.App/Services/ImportWorkflowService.cs) — **`ApplyPostSingleFileLibraryImportSuccessAsync`** (public seam for tests; WinUI `InternalsVisibleTo` not relied on) |
| **Supporting** | [`ImportToProjectPersistence`](../../src/VoiceStudio.App/Services/ImportToProjectPersistence.cs); [`IProjectAudioClient`](../../src/VoiceStudio.App/Core/Services/IProjectAudioClient.cs); [`AppServices`](../../src/VoiceStudio.App/Services/AppServices.cs) `ImportWorkflowService` factory |
| **Tests** | **`ImportToProjectPersistenceTests`** (unit); **`ImportWorkflowServiceTests`** — project → one save; no project → no save; save throws → no throw |
| **Proof** | §7 extended filter; §8 row |

### 10.4 IN / OUT

**IN (A2):** `ImportWorkflowService` constructor gains **`IProjectAudioClient`** + **`IErrorLoggingService`**; persistence call on success path; tests above.

**OUT (A2):** **`LibraryUseCase.ImportFilesAsync`** / batch multi-file; drag-drop batch; changing **A1** / **C1** behavior; **`AssetAddedEvent`** contract redesign; timeline/shell-only refactors; Pass 06; `backup.py`.

**Batch / A3:** If product needs **`ImportFilesAsync`** + **`AssetAddedEvent`**, use a **new** row (e.g. **P05-Persist-A3**) + sign-off — **do not** expand A2 in place.

### 10.5 Test seam note

**Public** `ApplyPostSingleFileLibraryImportSuccessAsync` is a **pragmatic test seam** (same class of compromise as **`RecordingViewModel.ApplyPostLibraryUploadSuccessAsync`**). Prefer **not** stacking more behavior on **`ImportWorkflowService`** without a new matrix row.

---

## 11. P05-Persist-A3 — batch `ImportFilesAsync` → project audio

**Purpose:** When the **batch library import** API returns **`ImportedItems`**, optionally copy **each** imported **library audio** into the active project — same **`IProjectAudioClient.SaveAudioToProjectAsync`** surface as A1/A2/C1.

**Sibling slices:** **A1**, **A2**, **C1** stay **closed**; do not change their §8 rows from this slice.

### 11.1 Call-site audit (repo truth)

| Finding | Detail |
|--------|--------|
| **`ImportFilesAsync` definition** | [`LibraryUseCase.ImportFilesAsync`](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) → POST `/api/library/import`. |
| **Production callers (app layer)** | **None** besides **`LibraryUseCase`** / interface — reproducible: `rg ImportFilesAsync src/VoiceStudio.App --glob "*.cs"` → hits only [`ILibraryUseCase.cs`](../../src/VoiceStudio.App/UseCases/ILibraryUseCase.cs) (declaration) and [`LibraryUseCase.cs`](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) (implementation). **Tests** live under `src/VoiceStudio.App.Tests` (separate tree). |
| **Library multi-file drag-drop** | [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) uses **`IBackendClient.UploadAudioFileAsync`** per file — **not** `ImportFilesAsync`. **OUT** of A3 v1 (separate signed row for parity if product wants). |
| **DI** | **`ILibraryUseCase`** registered in [`AppServices`](../../src/VoiceStudio.App/Services/AppServices.cs) **after** **`IContextManager`** (batch persistence requires active project). |

### 11.2 Id + filename mapping (frozen)

| Field | Rule |
|-------|------|
| **Project id** | **`IContextManager.ActiveProjectId`** (same as A2). |
| **Library audio id** | **`LibraryItem.Id`** from each **`ImportedItems`** row (non-empty only). *If* backend later exposes a distinct playback id on this DTO, extend via a **new** matrix row — do not silently change A3. |
| **Filename hint** | Index **`i`**: use **`Path.GetFileName(filePaths[i])`** when **`i < filePaths.Count`**; else use **`Path.GetFileName(item.Name)`** when **`Name`** non-empty; else **`null`** (same pattern as single-file “optional filename”). |
| **Trigger** | After **`PostAsync`** returns and **`ImportedItems.Count > 0`**, run batch persistence **before** returning the list to the caller. |

### 11.3 Policy freezes (A3)

| Decision | Frozen choice |
|----------|----------------|
| **Scope** | **`LibraryUseCase.ImportFilesAsync` only** — one API surface. |
| **`AssetAddedEvent`** | **No contract change** in A3 v1 (batch import still does **not** publish per-file events; Transcribe C2 prefill unchanged for this path). |
| **Partial success** | **Library import** outcome is unchanged. **Project save:** per-item **`try/catch`**; failure → **`IErrorLoggingService.LogError(..., "SaveBatchImportToProject")`**; **continue** other items; **no** throw. |
| **UX / toasts** | **No** new user-visible strings from **`LibraryUseCase`** in v1 (no shell caller). Future UI may add summary toasts under a **new** row. |
| **Non-blocking** | Aligns with A2/C: project persistence **never** fails the import API result. |

### 11.4 Bounded matrix — **P05-Persist-A3**

| Field | Content |
|-------|---------|
| **ID** | **P05-Persist-A3** |
| **Target** | Post-success batch **`ImportFilesAsync`**: **`ImportToProjectPersistence.TrySaveAfterBatchLibraryImportAsync`** when **`ActiveProjectId`** set |
| **Primary owner** | [`LibraryUseCase.ImportFilesAsync`](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) |
| **Supporting** | [`ImportToProjectPersistence`](../../src/VoiceStudio.App/Services/ImportToProjectPersistence.cs) (`TrySaveAfterBatchLibraryImportAsync` + shared single-file path with log context); [`AppServices`](../../src/VoiceStudio.App/Services/AppServices.cs) **`ILibraryUseCase`** factory |
| **Tests** | **`ImportToProjectPersistenceTests`** — batch no-project / two saves / first throws continues / null-items guard; **`LibraryUseCaseImportFilesPersistenceTests`** — import + project / no project / empty response |
| **Proof** | §7 extended filter; §8 row |

### 11.5 IN / OUT

**IN (A3):** Batch **`ImportFilesAsync`** persistence; ctor dependencies; **`ILibraryUseCase`** registration; tests above.

**OUT (A3):** **Drag-drop** / **`UploadAudioFileAsync`** multi-file loop; changing **A1** / **A2** / **C1** behavior; **`AssetAddedEvent`** redesign; timeline semantics; Pass 06; `backup.py`.

### 11.6 Sign-off (A3 IN scope only)

| Role | Decision | Date |
|------|----------|------|
| **Product / engineering** | Same row as §1 **P05-Persist-A3** — batch API only; per §11.1–§11.5. | **2026-03-25 — Tyler** |

### 11.7 Architecture note (honest)

**`LibraryUseCase`** is already a **transport orchestrator** for library HTTP; adding optional project persistence **continues** the tactical pattern from A2 (**`ImportWorkflowService`**). Prefer **not** stacking unrelated behavior without a **new** matrix row.

---

## 12. P05-Persist-A4 — library drag-drop → project audio (execution-grade freeze; sign-off pending)

**Purpose:** Close **§11.1** gap: **multi-file drag-drop** in [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) uses per-file **`UploadAudioFileAsync`** — **not** **`ImportFilesAsync`**. When **`IContextManager.ActiveProjectId`** is set, optionally mirror **A2/A3** with **`IProjectAudioClient.SaveAudioToProjectAsync`** (non-blocking; honest failures logged).

**Current truth:** **A3** covers **`ImportFilesAsync`** only. **Drag-drop** remains **OUT** of shipped behavior until §1 **A4** row is **dated** and §8 moves to **Complete** with proof.

### 12.0 Frozen architecture choice (A4 v1)

| Path | Description | A4 v1 |
|------|-------------|-------|
| **Path A (frozen for A4 v1)** | **Bounded** persistence in **`LibraryView`** drag-drop loop (and **only** a **new** small helper under `Services/` invoked from that code-behind — **not** new behavior on **`LibraryUseCase`**). After **each** successful upload, if **`ActiveProjectId`** set → **one** `SaveAudioToProjectAsync` with library/playback id + filename hint (**`Path.GetFileName`**), failures **non-blocking**, log **`SaveDragDropToProject`** (or aligned context). **No** `ImportFilesAsync` change. | **IN** |
| **Path B** | Reroute drag-drop to call **`LibraryUseCase.ImportFilesAsync`** / batch API so A3 persistence applies “for free.” | **OUT** of A4 v1 — larger blast (§11.1 callers, UX, progress); **new** signed row if product selects |

**Trigger (frozen):** **Per successful file** in the drag-drop loop (same ordering as today’s upload loop); **no** new all-or-nothing batch semantics in A4 v1.

**Success / UX (frozen):** **No** new product toasts required in A4 v1 unless existing `LibraryView` already surfaces per-file errors — mirror **A3** policy: persistence failures **logged**; library upload success **unchanged**. Optional resource keys **only** if an existing status line is extended (no net-new UX wave).

**`AssetAddedEvent` (frozen):** **Unchanged** in A4 v1 — **no** new per-file events from drag-drop path (same as **A3** v1 choice).

### 12.1 Resolved decisions (replacing planning-only table)

| Decision | Frozen answer |
|----------|----------------|
| **Scope** | **Path A** only for A4 v1 (§12.0). |
| **Primary owner** | [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) — drag-drop handler path that calls **`UploadAudioFileAsync`**. |
| **Supporting** | **New** [`LibraryDragDropToProjectPersistence`](../../src/VoiceStudio.App/Services/) (or equivalent name) — **mirror** `ImportToProjectPersistence` / A3 logging shape; [`IProjectAudioClient`](../../src/VoiceStudio.App/Core/Services/IProjectAudioClient.cs); [`IContextManager`](../../src/VoiceStudio.Core/Services/IContextManager.cs); [`IErrorLoggingService`](../../src/VoiceStudio.App/Services/IErrorLoggingService.cs) (if used elsewhere in persistence helpers); [`AppServices`](../../src/VoiceStudio.App/Services/AppServices.cs) registration **if** helper is injected into **`LibraryView`**. |
| **Tests** | **New** `LibraryDragDropToProjectPersistenceTests` (**3** cases minimal): active project + valid ids → **one** save; no active project → **no** save; save throws → **swallow/log**, **no** throw. **No** WinRT / picker — pure unit tests on helper; **`LibraryView`** wiring covered by seam **only** if an existing test hook exists — **otherwise** helper-only for v1 test budget. |
| **Seam FQN (extend §7)** | Append: `FullyQualifiedName~LibraryDragDropToProjectPersistenceTests` to the §7 filter string. |

### 12.2 Bounded matrix — **P05-Persist-A4** (execution)

| Field | Content |
|-------|---------|
| **ID** | **P05-Persist-A4** |
| **Target** | Path **A**: post-upload drag-drop: **`LibraryDragDropToProjectPersistence.TrySaveAfterUploadAsync`** (or equivalent) when **`ActiveProjectId`** + library id set |
| **Primary owner** | [`LibraryView.xaml.cs`](../../src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs) |
| **Supporting** | **`LibraryDragDropToProjectPersistence`** (new under `Services/`); **`IProjectAudioClient`**; **`IContextManager`**; **`IErrorLoggingService`**; **`AppServices`** (if DI) |
| **Baseline seam count** | **50** passed (§7 filter as of **A3** closure) |
| **Target seam count** | **53** passed (**+3** unit tests on new helper) |
| **Proof commands** | (1) `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` (2) `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "<§7 filter + LibraryDragDrop FQN>"` (3) `.\scripts\verify.ps1 -Quick` → record artifact in §8; **pointer** only per repo rules |

### 12.3 OUT (strict — A4 v1)

- **Path B** / rerouting drag-drop through **`ImportFilesAsync`** without a **new** matrix row.
- **`LibraryUseCase`** behavior expansion beyond what Path A requires (§11.7).
- Changing **`ImportFilesAsync`**, **`ImportWorkflowService`**, or **A1 / A2 / A3** §8-complete behavior without a **separate** signed row.
- **`AssetAddedEvent`** contract or Transcribe **C2** prefill semantics change.
- Pass 06 / **`backup.py`** / unrelated panels.
- Mining **`stash@{0}`** omnibus into this slice — **no** bulk stash apply.

### 12.4 Proof expectations (after implementation)

Same discipline as §7 / §12.3 prior text: **seam count** and **Quick** cited **separately** in §8.

### 12.5 Sign-off

**No implementation** until §1 **Product / engineering** row for **P05-Persist-A4** is **dated** (replaces **Pending**). Then §8 **Planned** → **Complete** with artifact + seam note.
