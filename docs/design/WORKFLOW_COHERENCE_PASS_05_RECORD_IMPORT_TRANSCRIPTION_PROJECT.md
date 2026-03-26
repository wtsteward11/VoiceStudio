# Workflow Coherence Pass 05 — Record / import → transcription → project persistence

**Purpose:** Bounded pass to align recording and library import of audio with transcription requests, project-scoped persistence (`IProjectAudioClient`), and timeline/project association—without rewriting panels, transport, or the timeline data model.

**Date:** 2026-03-24  
**Status:** **Slices 1–3 complete** for this pass’s chosen scope (2026-03-24) — C1+C4 (slice 1), C2 (slice 2), **C3 Option B** (slice 3 — semantics/messaging only; **no** `SaveAudioToProjectAsync` on record/import/transcribe **within those slices**). **Option C** (record-only project audio) is delivered **outside** this pass’s §8 matrix — [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md). **Option A** + full import/transcribe persistence **still deferred** (policy + §11). **Latest closure proof (slice 3):** `artifacts/verify/20260324_190103`.

**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) (Workflow 5), [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) (slice 3 / C3 policy freeze), [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md) (Option C execution), [WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md](WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md), [POST_EXTRACTION_TRANSITION_PLAN.md](POST_EXTRACTION_TRANSITION_PLAN.md).

**Authoritative prior proof (Pass 04):** `artifacts/verify/20260324_070722` (historical baseline for repo discipline; not Pass 05 closure).

**Pass 05 closure proof (slice 1):** `artifacts/verify/20260324_173141` — `verification_report.md` + `summary.json`; see §8.

**Pass 05 closure proof (slice 2):** `artifacts/verify/20260324_181021` — see §8.1. Seam tests: **17 passed** (§7.1 filter); Quick verify does not substitute for that filter.

---

## 1. Participating components (as-is, code-truth)

| Component | Role |
|-----------|------|
| [RecordingViewModel.cs](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs) | Local NAudio record → stop → `IRecordingClient.UploadAudioFileAsync` → `RecordedAudioId` / `RecordedAudioUrl`; `AssetAddedEvent` to refresh Library; `IContextManager.SetCurrentPlayable` for transport; **does not** call `IProjectAudioClient` or pass audio into Transcribe panel automatically |
| [TranscribeViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs) | **`TranscribeViewModel`** (not `TranscriptionViewModel`) — bottom panel; `SelectedAudioId` / `SelectedProjectId` (XAML two-way); **slice 2:** prefills `SelectedAudioId` from `AssetAddedEvent` (`recording-panel`, `import-workflow`) when empty; `ITranscriptionClient.TranscribeAudioAsync` / `ListTranscriptionsAsync`; `TranscriptionCompletedEvent`; `SendToTimelineCommand` → `NavigateToEvent` (`loadTranscript`) |
| [TranscribeView.xaml](../../src/VoiceStudio.App/Views/Panels/TranscribeView.xaml) | Binds `SelectedAudioId`, `SelectedProjectId`; **slice 1** syncs `SelectedProjectId` from `IContextManager` in VM; **slice 2** can prefill `SelectedAudioId` via events |
| [ITranscriptionClient](../../src/VoiceStudio.App/Core/Services/ITranscriptionClient.cs) / [TranscriptionClient.cs](../../src/VoiceStudio.App/Services/TranscriptionClient.cs) | Transcription API seam: languages, engines, transcribe, list, get, delete |
| [TimelineViewModel.cs](../../src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs) | **`ITimelineTranscriptionService`** — `LoadTranscriptSegmentsAsync(transcriptionId)` loads subtitle segments for overlay; **`IProjectAudioClient`** — `SaveAudioToProjectAsync` after synthesis add-clip path, `ListProjectAudioAsync`, `GetProjectAudioAsync`; `NavigateToEvent` handler `loadTranscript`; subscribes to `TranscriptionCompletedEvent` for subtitle overlay |
| [ITimelineTranscriptionService](../../src/VoiceStudio.App/Core/Services/ITimelineTranscriptionService.cs) / [TimelineTranscriptionService.cs](../../src/VoiceStudio.App/Services/TimelineTranscriptionService.cs) | **Load transcription by ID only** (facade over backend); not the same as “start transcription job” |
| [IProjectAudioClient](../../src/VoiceStudio.App/Core/Services/IProjectAudioClient.cs) / [ProjectAudioClient.cs](../../src/VoiceStudio.App/Services/ProjectAudioClient.cs) | Project-scoped list/get/save of audio files |
| [LibraryUseCase.cs](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) | `ImportFilesAsync` — POST import paths to backend; returns `LibraryItem` list (import → library asset, distinct from timeline `SaveAudioToProjectAsync` path) |
| [TimelineSynthesisService.cs](../../src/VoiceStudio.App/Services/TimelineSynthesisService.cs) | Uses `IProjectAudioClient` in synthesis-related path (related to clip/audio cohesion) |

**Backlog / inventory drift (Workflow 5 row):**

- The backlog names **`TranscriptionViewModel`** — **that type does not exist.** The transcription UI ViewModel is **`TranscribeViewModel`** (`Views/Panels/TranscribeViewModel.cs`).
- **`ITimelineTranscriptionService`** is **load-by-id** for transcript display, not a duplicate of `ITranscriptionClient` “transcribe audio” operations.

---

## 2. As-is workflow map (code-truth)

### 2.1 Recording → library asset

1. User starts/stops recording in Recording panel — [`RecordingViewModel`](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs).
2. On stop, local file uploaded via `_recordingClient.UploadAudioFileAsync` → `RecordedAudioId`, `RecordedAudioUrl`.
3. `AssetAddedEvent` published (Library refresh); `SetCurrentPlayable` for transport.
4. **`ProjectId` (slice 1 — C1):** `RecordingViewModel` syncs `ProjectId` from `IContextManager.ActiveProjectId` on activation and via `ProjectChangedEvent` (same coherence pattern as `TranscribeViewModel`). **Upload path:** `IRecordingClient.UploadAudioFileAsync` has **no** project parameter — the asset is a **library/backend** upload. **`IProjectAudioClient.SaveAudioToProjectAsync` is not** called from the record/stop path; that gap is **C3 / policy**, not “missing ProjectId sync.”

### 2.2 Import → library asset

1. [LibraryUseCase.ImportFilesAsync](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) posts file paths to backend; returns imported items.
2. This path is **library-centric**; automatic handoff to Transcribe panel or project audio listing is **not** described in this map without further tracing (implementation pass should verify drag/drop from Library to Transcribe if present).

### 2.3 Transcription request (Transcribe panel)

1. User sets **`SelectedAudioId`** (backend audio id) and optionally **`SelectedProjectId`** in UI.
2. `TranscribeAsync` builds `TranscriptionRequest` and calls `ITranscriptionClient.TranscribeAudioAsync(request, SelectedProjectId, ct)`.
3. On success, `TranscriptionCompletedEvent` published if segments exist; toast shown.
4. **Slice 1 (C1):** `SelectedProjectId` is synchronized from `IContextManager.ActiveProjectId` on activation and refresh and via **`ProjectChangedEvent`** (user may **clear** the field after sync, or have no active project — see §10.3). **Remaining coherence gap (C3):** persistence semantics and **honest messaging** (library vs project audio vs overlay), not missing project-id propagation.

### 2.4 Transcription → timeline display (not full clip persistence)

1. **Send to Timeline:** `NavigateToEvent` → Timeline with `loadTranscript` + `transcriptionId`.
2. **Timeline:** `LoadTranscriptSegmentsAsync` uses `ITimelineTranscriptionService.GetTranscriptionAsync` → fills `TranscriptSegments` subtitle overlay.
3. **Event path:** `TranscriptionCompletedEvent` also updates transcript overlay in `TimelineViewModel.OnTranscriptionCompleted`.

### 2.5 Project audio persistence (timeline-centric today)

1. **`IProjectAudioClient.SaveAudioToProjectAsync`** is invoked from **timeline add-clip from synthesis** flow ([`TimelineViewModel`](../../src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs) ~L1715) using `LastSynthesizedAudioId` — **voice synthesis → clip**, not recording/transcription import per se.
2. **`ListProjectAudioAsync` / `GetProjectAudioAsync`** support project audio browser and playback from timeline context.

### 2.6 Stop-short / degradation points

| Step | Behavior |
|------|----------|
| Record completes but user never copies `RecordedAudioId` to Transcribe | **Slice 2:** `AssetAddedEvent` from recording can prefill Transcribe when `SelectedAudioId` was empty; user may still clear or override |
| `SelectedProjectId` empty in Transcribe | API called with `null` project scope — may weaken project-scoped listing/persistence semantics |
| Transcription succeeds | Text + events; **timeline overlay** updated; **does not** by itself call `SaveAudioToProjectAsync` for the source audio |
| Import only | Assets in library; project/timeline linkage requires separate user steps |
| Synthesis add-clip | `SaveAudioToProjectAsync` runs for that synthesis audio id — **different workflow branch** than record/transcribe |

---

## 3. Target behavior (Pass 05 — TBD until implementation pass)

High-level intent (not implemented by this planning doc):

1. **Coherent project ownership:** Active project propagates to transcription and project-audio operations where appropriate.
2. **Normalized handoffs:** Recorded or imported audio ids discoverable for transcribe without fragile manual id entry where feasible.
3. **Honest persistence:** Clear distinction between library asset, project audio files, and timeline clips; no silent “saved to project” if only one layer persisted.
4. **Failure surfaces:** Actionable messages when project id missing, audio id missing, or save partial-fails.

Exact acceptance criteria belong in the bounded change matrix (§5) after implementation scope is chosen.

---

## 4. Current defects / coherence gaps (pre-implementation inventory)

Hypotheses to validate in implementation pass; IDs are Pass 05 working labels. **Verification** is code-truth status after slice 1 audit (2026-03-24).

| ID | Symptom | Files / classes | Likely cause | Priority | Verification |
|----|---------|-------------------|--------------|----------|--------------|
| D1 | “Record/import → transcribe → project” feels disconnected | Recording, Library, Transcribe | No single orchestrator; flow still spans panels | High | **Partial** — C1/C2/C3 **Option B** improve sync, handoff, and honesty; **remaining:** no unified orchestrator; **project-audio persistence** (**Option A/C**) still deferred — see §11 |
| D2 | `RecordingViewModel.ProjectId` unused in reviewed stop/upload path | `RecordingViewModel` | Property present without wiring to `IRecordingClient` (API has no project param) | Med | **Partial** — C1 syncs `ProjectId` from `IContextManager` on activate + `ProjectChangedEvent`; upload path unchanged |
| D3 | Transcription may run without project scope | `TranscribeViewModel` | `SelectedProjectId` optional; user may clear after **C1** sync | Med | **C1** sync + §10.3; **C3 Option B (slice 3)** adds semantics/messaging (library vs overlay vs not project-persisted); **persistence policy** for optional project scope remains **Option A/C** — not in Option B |
| D4 | `IProjectAudioClient` usage concentrated on timeline synthesis clip path | `TimelineViewModel` | `SaveAudioToProjectAsync` at `TimelineViewModel` add-clip path (~L1715); **no** call from `RecordingViewModel` / `TranscribeViewModel` | Med | **Confirmed** — grep `SaveAudioToProjectAsync` → only timeline synthesis path in reviewed scope |
| D5 | Backlog named `TranscriptionViewModel` | `CROSS_FEATURE_WORKFLOW_BACKLOG.md` | Stale inventory | Low | **Resolved (doc)** — Workflow 5 row uses `TranscribeViewModel` |
| D6 | `ITimelineTranscriptionService` vs `ITranscriptionClient` confusion | Docs / onboarding | Two seams: **load transcript** vs **run transcribe** | Low | **Confirmed** — §1 distinguishes; UX polish deferred |

---

## 5. Bounded change matrix (matrix-to-code)

| Change ID | Target behavior | Primary owner / methods | Supporting | Behavior vs today | User-visible effect | Tests | Slice |
|-----------|-----------------|---------------------------|------------|-------------------|---------------------|-------|-------|
| **C1** | Propagate active `project_id` from `IContextManager` when a project is active | **`TranscribeViewModel`:** `SyncSelectedProjectFromContext`, `EnsureProjectChangedSubscription`, `OnProjectChanged`; **`IPanelLifecycle`:** `OnActivatedAsync`, `RefreshAsync`. **`RecordingViewModel`:** `SyncProjectFromContext`, `EnsureProjectChangedSubscription`, `OnProjectChanged`; extend **`OnActivatedAsync`** | `AppServices.TryGetContextManager`, `AppServices.TryGetEventAggregator`, `ProjectChangedEvent` | On panel activate (and refresh), `SelectedProjectId` / `ProjectId` match `ActiveProjectId`; project switches update via event | Transcribe/Recording project fields pre-filled from active project; user can still edit after sync | `TranscribeViewModelSeamTests`; `RecordingViewModelSeamTests` | **1** |
| **C2** | Prefill `SelectedAudioId` when empty from `AssetAddedEvent` (`recording-panel`, `import-workflow`, `audio` only) | **`TranscribeViewModel`:** `EnsureAssetAddedSubscription`, `OnAssetAdded`; **`OnDeactivatedAsync`:** release event subscriptions; **`Dispose`:** release subscriptions | `AssetAddedEvent`, `IEventAggregator` | No overwrite when user already entered an audio id; no handoff on unrelated sources | Transcribe audio id field fills after successful record upload or single-file import | `TranscribeViewModelSeamTests` (slice 2 cases) | **2** |
| **C3** | Honest semantics/messaging (Option B): library vs overlay vs project persistence — **no new** `SaveAudioToProjectAsync` from record/import/transcribe in slice 3 | **`TranscribeViewModel`** (primary); optional **`TimelineViewModel`** copy for overlay | [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) | See slice 3 lock §10.7 | User-visible honesty about what is persisted vs overlay | Seam tests + policy doc §5 | **3** |
| **C4** | Honest validation and errors for missing transcription inputs | **`TranscribeViewModel`:** `TranscribeAsync`, `LoadTranscriptionsAsync` — early exit with `ErrorMessage` + toast when `SelectedAudioId` missing/whitespace; **`RecordingViewModel`:** `PlayRecordedAsync` uses [`BackendPlaybackBaseUrl.Resolve`](../../src/VoiceStudio.App/Utilities/BackendPlaybackBaseUrl.cs) instead of ad hoc localhost fallback | `ToastNotificationService`, `ResourceHelper` | No silent no-op transcribe without message; playback URL aligned with Pass 04 | Warnings when audio id missing for list/transcribe; consistent play base URL | `TranscribeViewModelSeamTests`, `RecordingViewModelSeamTests` | **Yes** |
| **C5** | Backlog/registry §1 alignment | **Largely done** (Workflow 5 row); residual cross-links only | — | — | — | Doc review | **N/A** |

Exact rows may be trimmed at implementation time; **do not** add rows that violate §6.

---

## 6. Strict out-of-scope

- No backup/restore workflow (Pass 06 / separate backlog).
- No global search / overlay / navigation changes (Pass 03 territory unless a minimal event is required for Pass 05 — default **none**).
- No broad shell, `PanelHost`, or workspace rewrite.
- No `IBackendClient` extraction restart or transport migration (extraction paused per transition plan).
- No large timeline data model or track redesign.
- No cosmetic-only UI polish or unrelated refactors.
- No Script Editor / synthesis pass regression (Pass 04); touch only if a shared helper is required and covered by tests.

---

## 7. Proof expectations by slice

All **three** bounded slices in this pass are **closed** (§8). Persistence beyond **C3 Option B** is **out of scope** here — see §11.

Per slice below; **Quick verify** does **not** substitute for the **seam test** filter — cite **both** in any closure narrative.

### 7.0 Slice 1 (historical)

Minimum proof standard (same discipline as Pass 03–04):

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. **Targeted tests** — slice 1 closure filter:

   ```text
   dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"
   ```

   **2026-03-24 closure:** **11 passed**, 0 failed (TranscribeViewModelSeam + RecordingViewModelSeam).

3. `.\scripts\verify.ps1 -Quick`
4. Record **authoritative** artifact directory from `artifacts/verify/latest_pointer.json` (must contain `verification_report.md` + `summary.json`).
5. Update §8 execution record with **exact** files changed and commands run.
6. Update `.cursor/STATE.md` proof index and `CROSS_FEATURE_WORKFLOW_BACKLOG.md` Workflow 5 row when Pass 05 closes.

**E2E:** Not in scope for slice 1.

### 7.1 Proof expectations (slice 2 — C2)

Same discipline as §7.0: **build**, **targeted seam tests** (separate command from Quick verify), **`verify.ps1 -Quick`**. Quick verify may skip the full C# test stage; do **not** claim the verify artifact alone proves seam tests — record both.

**Slice 2 closure filter (append Transcribe seam tests for C2):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"
```

(Recording seam tests remain in the filter for regression; slice 2 adds C2 cases under `TranscribeViewModelSeam`.)

### 7.2 Proof expectations (slice 3 — C3 / Option B)

Same build + seam filter as §7.1; Quick verify **cited separately**. **2026-03-24 closure:** seam filter **19 passed** (includes 2 new C3 cases on `TranscribeViewModelSeam`); Quick verify `artifacts/verify/20260324_190103`. Record proof in §8.2.

---

## 8. Execution record (slice 1 closure)

| Item | Detail |
|------|--------|
| **Status** | **Slice 1 complete** — C1 (project sync) + C4 (validation / playback URL policy) per §10 |
| **Behavior** | `TranscribeViewModel` / `RecordingViewModel` sync project id from `IContextManager` on activation + `ProjectChangedEvent`; transcribe/list paths reject empty `SelectedAudioId` with `ErrorMessage` + resource-backed warning; recording playback uses `BackendPlaybackBaseUrl.Resolve` |
| **Files changed (primary)** | `TranscribeViewModel.cs`, `RecordingViewModel.cs`, `Resources.resw` (`Transcribe.MissingAudioId*`), `TranscribeViewModelSeamTests.cs`, `RecordingViewModelSeamTests.cs`, this doc |
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (warnings pre-existing) |
| **Tests** | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"` — **11 passed** |
| **verify.ps1 -Quick** | **PASSED** — authoritative dir `artifacts/verify/20260324_173141` (`verification_report.md`, `summary.json`; `latest_pointer.json` aligned) |
| **Known leftovers** | C2 in slice 2; **C3 Option B** closed slice 3 — see §8.2; **Option A/C** persistence deferred — §11 |

### 8.1 Execution record (slice 2 closure — C2)

| Item | Detail |
|------|--------|
| **Status** | **Slice 2 complete** (2026-03-24) — C2 (`AssetAddedEvent` handoff + lifecycle cleanup) |
| **Behavior** | `TranscribeViewModel` subscribes to `AssetAddedEvent` for `recording-panel` and `import-workflow`; prefills `SelectedAudioId` only when empty/whitespace; unsubscribes `ProjectChangedEvent` and `AssetAddedEvent` on deactivate and dispose |
| **Files changed (primary)** | `TranscribeViewModel.cs`, `Resources/en-US/Resources.resw` (`Transcribe.AudioIdPrefilled*`), `TranscribeViewModelSeamTests.cs`, this doc, `.cursor/STATE.md`, `CROSS_FEATURE_WORKFLOW_BACKLOG.md` |
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** |
| **Tests** | `dotnet test ... --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"` — **17 passed** (6 new C2 tests on Transcribe seam); Quick verify does **not** replace this filter |
| **verify.ps1 -Quick** | **PASSED** — `artifacts/verify/20260324_181021` (`verification_report.md`, `summary.json`; `latest_pointer.json` aligned) |
| **Known leftovers** | **C3 Option B** → slice 3 closed (§8.2); **Option A/C** / batch import event gap still deferred — §11 |

### 8.2 Execution record (slice 3 closure — C3 / Option B)

| Item | Detail |
|------|--------|
| **Status** | **Slice 3 complete** (2026-03-24) — C3 **Option B** per §10.7 + [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) |
| **Behavior** | Resource-backed transcribe success + Send-to-Timeline toasts; `AudioPersistenceSemanticsHint` + status line in `TranscribeView`; no `SaveAudioToProjectAsync` on transcribe paths |
| **Files changed (primary)** | `TranscribeViewModel.cs`, `TranscribeView.xaml`, `Resources/en-US/Resources.resw`, `TranscribeViewModelSeamTests.cs`, policy + Pass doc + registry |
| **Build** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** |
| **Tests** | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TranscribeViewModelSeam|FullyQualifiedName~RecordingViewModelSeam"` — **19 passed** |
| **verify.ps1 -Quick** | **PASSED** — `artifacts/verify/20260324_190103` (`verification_report.md`, `summary.json`; `latest_pointer.json` aligned) |

---

## 9. Related docs

- [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) — Workflow 5
- [WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md](WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md)
- [TEST_CLASSIFICATION.md](../governance/TEST_CLASSIFICATION.md) — seam vs transport-mock tests
- [PLAYBACK_ENTRY_POINTS.md](PLAYBACK_ENTRY_POINTS.md) — transport ownership (recording/timeline)

---

## 10. Implementation lock (slice 1)

### 10.1 Locked scope — **Slice 1 IN**

- **C1:** Active project propagation — `TranscribeViewModel` and `RecordingViewModel` sync `SelectedProjectId` / `ProjectId` from `IContextManager.ActiveProjectId` on activation and on `ProjectChangedEvent` (pattern aligned with `EffectsMixerViewModel` / Pass 02). `TranscribeViewModel` implements `IPanelLifecycle` for `OnActivatedAsync` / `RefreshAsync`.
- **C4:** Honest validation — `TranscribeAsync` / `LoadTranscriptionsAsync` set `ErrorMessage` + warning toast when `SelectedAudioId` is missing or whitespace; `RecordingViewModel.PlayRecordedAsync` uses `BackendPlaybackBaseUrl.Resolve(BackendClientConfig)` for backend id playback (Pass 04-aligned URL policy).

### 10.2 Slice 1 — **OUT / deferred**

- **C2** — Auto-fill `SelectedAudioId` from Library/Recording (orchestration). *(Delivered in **slice 2**, §10.4 — `AssetAddedEvent` handoff.)*
- **C3** — Policy for when source audio must appear under `IProjectAudioClient` vs timeline-only.
- **TimelineViewModel** changes beyond what is strictly required for C4 in slice 1 (none planned).
- Backup/restore, search, shell rewrite, BackendClient extraction, broad timeline redesign, E2E.

### 10.3 Policy note (transcribe without project)

Transcription **may** still be requested with `SelectedProjectId` null if the user clears the field after sync; slice 1 does **not** block API calls on null project (backend accepts optional project scope). Future slice may add stricter product policy.

### Expansion guard

Any work that expands to “full media asset management” or “replace Library” is **out of scope** unless Workflow 5 is re-baselined in this doc.

### 10.4 Implementation lock — **Slice 2 IN (C2 only)**

- **C2:** Prefill `SelectedAudioId` in `TranscribeViewModel` when the field is **null, empty, or whitespace**, using **`AssetAddedEvent`** from **`recording-panel`** (after successful upload in [`RecordingViewModel.StopRecordingAsync`](../../src/VoiceStudio.App/ViewModels/RecordingViewModel.cs)) and **`import-workflow`** (after successful upload in [`ImportWorkflowService.ImportAudioFileAsync`](../../src/VoiceStudio.App/Services/ImportWorkflowService.cs)). Ignore other `sourcePanelId` values. Require `assetType == "audio"` and non-empty `AssetId`. **Do not** overwrite a non-empty user-entered `SelectedAudioId`.
- **Lifecycle:** Unsubscribe `AssetAddedEvent` and `ProjectChangedEvent` in **`OnDeactivatedAsync`** and in **`Dispose`** (override on `TranscribeViewModel`) so subscriptions do not leak.

### 10.5 Slice 2 — handoff map (code-truth)

| Source | When | Payload | Transcribe action |
|--------|------|---------|-------------------|
| Recording upload OK | After `UploadAudioFileAsync` succeeds | `AssetAddedEvent("recording-panel", uploadResult.Id, "audio", path)` | Prefill `SelectedAudioId` if empty |
| Single-file import OK | After `UploadLibraryAssetAsync` in import workflow | `AssetAddedEvent("import-workflow", playbackId, "audio", filePath)` | Prefill `SelectedAudioId` if empty |
| Batch import | `LibraryUseCase.ImportFilesAsync` | No `AssetAddedEvent` | **Out of slice 2** |
| Upload failure | Recording catch path | No event published | No handoff |

### 10.6 Slice 2 — **OUT**

- **C3** — project-audio vs library vs timeline persistence policy, `IProjectAudioClient` placement, timeline ownership (**slice 3** — requires product decisions; see §11).
- Batch library import handoff without a new event contract.
- Shell / `PanelHost` / new global orchestration layer.

### 10.7 Implementation lock — **Slice 3 IN (C3 / Option B only)**

Canonical decisions: [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md) (frozen **Option B**, **C3-OptB** matrix row).

**IN**

- Honest **library vs overlay vs project-scope** semantics via resource-backed copy, toasts, and/or bindable `AudioPersistenceSemanticsHint` on `TranscribeViewModel` (see policy §8).
- Seam tests for C3 branches; **no** new `SaveAudioToProjectAsync` from record/import/transcribe.

**OUT** (same as policy §5)

- No new `SaveAudioToProjectAsync` from record, import, or transcribe flows.
- No batch `ImportFilesAsync` event contract change.
- No timeline clip model rewrite; no shell/library redesign.

---

## 11. After Pass 05 — deferred persistence and follow-up lanes

This pass delivered **three bounded slices** (C1+C4, C2, **C3 Option B**). End-to-end “import/transcribe → project audio persistence” (Workflow 5 **Option A**) is **still** not claimed here.

- **C3 Option B (semantics / messaging):** **Complete** (slice 3). Canonical policy: [PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md](PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md). Closure proof: §8.2; Quick verify `artifacts/verify/20260324_190103` (seam filter **19 passed** — cite **separately** from Quick verify).
- **Option C (record-only bridge):** **Not** part of the slice 1–3 matrix. Implement **only** via the **separate** follow-up doc — [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md) (§8 execution record; **does not** reopen **C3-OptB**).
- **Option A (transcribe/import persistence):** **Deferred** — requires a **new** bounded doc + matrix after Option C closure policy in the follow-up doc.
- **Batch library import:** [`LibraryUseCase.ImportFilesAsync`](../../src/VoiceStudio.App/UseCases/LibraryUseCase.cs) still does **not** publish `AssetAddedEvent` unless a **separate** scoped change is approved.
- **Next workflow lanes:** See [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md) — **Workflow 6** (backup/restore coherence), **Option A** planning, or other ranked backlog.

**Registry:** Policy doc listed in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

---

## Changelog

| Date | Note |
|------|------|
| 2026-03-24 | Scope frozen: as-is map, defects, provisional matrix, out-of-scope, proof expectations; implementation deferred. |
| 2026-03-24 | **Slice 1 closed:** C1+C4; seam tests 11 passed; verify.ps1 -Quick `artifacts/verify/20260324_173141`. |
| 2026-03-24 | **Slice 2 scoped:** §10.4–§10.6 C2-only lock, handoff map, §11 C3 deferral; §5 C2 row, §7.1, §8.1. |
| 2026-03-24 | **Slice 2 closed:** C2; seam tests **17 passed**; verify.ps1 -Quick `artifacts/verify/20260324_181021`. |
| 2026-03-24 | **Governance sync:** top **Status** line unified; §11 expanded + link to `PASS_05_C3_PROJECT_AUDIO_PERSISTENCE_POLICY.md`; `CANONICAL_REGISTRY` + Workflow 5 wiring row + `STATE` active task reconciled to slice 2 proof `20260324_181021`. |
| 2026-03-24 | **Slice 3 closed:** C3 Option B — §10.7 lock, policy Option B frozen, `AudioPersistenceSemanticsHint` + `Transcribe.C3.*` resources; seam **19 passed**; Quick verify `20260324_190103`. |
| 2026-03-24 | **Doc truth sync:** §7 title, §4 D1/D3, §8 leftovers, §11 post–Option-B narrative (no “future slice 3” prefreeze). |
| 2026-03-25 | §11 — **Option C** pointer to separate follow-up doc (`WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md`); **Option A** still deferred. |
