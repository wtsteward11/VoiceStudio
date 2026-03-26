# Workflow Coherence Pass 02 — Project → Timeline → Effects/Mixer

**Purpose:** Bounded product-facing pass for the project → timeline → effects/mixer workflow.  
**Date:** 2026-03-23  
**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md), [WORKFLOW_COHERENCE_PASS_01_PROFILE_SYNTHESIS_TIMELINE.md](WORKFLOW_COHERENCE_PASS_01_PROFILE_SYNTHESIS_TIMELINE.md)

---

## 1. Participating ViewModels/Services

| Component | Role |
|-----------|------|
| ProjectsViewModel | Project CRUD; project selection; publishes or sets project context |
| TimelineViewModel | SelectedProject; OnSelectedProjectChanged loads tracks via LoadTracksForProject; uses ITimelineTrackService, ITimelineClipService |
| EffectsMixerViewModel | SelectedProjectId; OnSelectedProjectIdChanged loads effect chains, mixer state, presets via LoadProjectSelectionDataAsync |
| IProjectsClient | Project CRUD |
| ITimelineTrackService | GetTracksAsync, CreateTrackAsync |
| ITimelineClipService | CreateClipAsync, etc. |
| IEffectChainClient | GetEffectChainsAsync, etc. |
| IMixerStateClient | GetMixerStateAsync, GetMixerPresetsAsync, etc. |
| IContextManager | ActiveProjectId, ActiveProjectName; SetActiveProject publishes ProjectChangedEvent |
| IEventAggregator | ProjectChangedEvent (PanelEvents.cs) |

---

## 2. Current Behavior

**Post–Pass 02 (2026-03-24):** Timeline `OnSelectedProjectChanged` calls `IContextManager.SetActiveProject`; EffectsMixer subscribes to `ProjectChangedEvent` and syncs on activate. Paragraphs below describe the **pre-pass** baseline for comparison; see §9–§10 for the historical map.

**Project open/create (historical baseline text):**
- ProjectsViewModel: User opens/creates project; selection flows to app state.
- IContextManager: SetCurrentProject / app state update publishes project change.
- TimelineViewModel: SelectedProject bound to Projects or context; OnSelectedProjectChanged fires LoadTracksForProject, LoadProjectAudioAsync; clears Tracks when null.

**Timeline population:**
- LoadTracksForProject gets tracks via ITimelineTrackService.GetTracksAsync; populates Tracks; creates default track if none.
- Clips load with tracks (backend returns tracks with clips).

**Effects/mixer on project switch:**
- EffectsMixerViewModel: Has SelectedProjectId; OnSelectedProjectIdChanged triggers LoadProjectSelectionDataAsync (effect chains, mixer state, presets).
- **Gap:** EffectsMixerViewModel.SelectedProjectId may not be automatically synced with TimelineViewModel.SelectedProject or IContextManager.CurrentProjectId when user switches project in Projects or Timeline panel.
- Possible stale mixer state when project changes in one panel but EffectsMixerViewModel has not received the update.

---

## 3. Target Behavior

1. **Project selection propagates to Timeline** — When user selects/opens a project (Projects panel or elsewhere), TimelineViewModel loads tracks and project audio.
2. **Project selection propagates to Effects/Mixer** — When project changes, EffectsMixerViewModel loads effect chains, mixer state, and presets for the new project.
3. **No stale state** — Timeline and EffectsMixer both reflect the currently selected project; no display of previous project's data.

---

## 4. Change Matrix (Pass 02 Frozen Scope)

| Change ID | Target behavior | Primary owner | Supporting | Event/state | Tests | Proof |
|-----------|-----------------|---------------|------------|-------------|-------|-------|
| C1 | Project activation propagates to Timeline | TimelineViewModel | TimelineProjectHandlers | SelectedProject → LoadTracksForProject | Verify existing | Unit |
| C2 | Project activation propagates to EffectsMixer | EffectsMixerViewModel | IContextManager, IEventAggregator | ProjectChangedEvent subscription | OnProjectChanged → SelectedProjectId | Unit |
| C3 | Timeline selection syncs to context (optional) | TimelineViewModel | ProjectStore, IContextManager | SetCurrentProject / ProjectChangedEvent | Audit | Verify |
| C4 | EffectsMixer syncs from IContextManager on activate | EffectsMixerViewModel | IContextManager | OnActivatedAsync | SelectedProjectId = ActiveProjectId | Unit |
| C5 | Staleness guard on async load | EffectsMixerViewModel | CancellationToken | LoadProjectSelectionDataAsync | SelectedProjectId != projectId return | Verify existing |
| C6 | Clear stale state on project switch | EffectsMixerViewModel, TimelineViewModel | — | Reset EffectChains, MixerState, etc. | Clear collections when project null | Unit |
| C7 | Workflow-step-specific failure messages | TimelineViewModel, EffectsMixerViewModel | ToastNotificationService | Project/timeline/mixer messages | Unit | — |

---

## 5. Tests/Proof Required

- **Manual:** (1) Open project → verify timeline populates with tracks. (2) Switch project → verify timeline clears/repopulates; verify EffectsMixer shows new project's state or clears.
- **Unit:** EffectsMixerViewModel loads mixer state when SelectedProjectId changes.
- **Integration:** Project switch → timeline and mixer both update.
- **verify.ps1 -Quick** after any code change; record artifact path.

---

## 6. Out-of-Scope (Strict — Pass 02 Lock)

- No BackendClient extraction.
- No project persistence/API redesign.
- No broad mixer feature additions.
- No generic timeline architecture rewrite.
- No visual theming.
- No Search, Script editor, Record, Backup workflows.
- No project model/API redesign.
- No transport/client extraction.
- No broad refactor of project selection UI.
- No "while I'm here" cleanup unrelated to this workflow.

---

## 7. User-Visible Wins

- Open project → timeline and mixer both show correct project data.
- Switch project → no stale mixer state from previous project.
- Consistent behavior across Projects, Timeline, EffectsMixer panels.

---

## 8. Likely Failure Modes

- **Event ordering:** Project change event fires before panels are ready.
- **Race conditions:** Fast project switching; async loads complete for old project after switch.
- **Missing event:** ProjectSelectedEvent or equivalent may not exist or may not reach EffectsMixerViewModel.
- **Different project sources:** Timeline uses SelectedProject (Project object); EffectsMixer uses SelectedProjectId (string); may need sync.

---

## 9. As-Is Workflow Map (Pre-Implementation Baseline — Historical)

*Captured before C3–C7 implementation; retained for audit trail. Post-fix behavior is described in §13.*

### Entry points

- **TimelineProjectHandlers.TimelineProjectOpenHandler.OpenProjectByIdAsync** — Sets `vm.SelectedProject` on TimelineViewModel (TimelineProjectHandlers.cs:72, 82).
- **TimelineProjectHandlers.TimelineProjectCreateHandler.CreateNewAsync** — Delegates to TimelineViewModel.CreateProjectCommand.
- **TimelineViewModel** — User selects project from ComboBox; `SelectedProject` setter; CreateProjectAsync sets SelectedProject (line 1081); LoadProjectByIdAsync / FollowSelection sets SelectedProject (1054, 1062).
- **Projects panel** — If it exists, may open projects via coordinator; not yet traced.

### Ownership handoffs

- **Project open:** TimelineProjectOpenHandler → TimelineViewModel.SelectedProject (direct VM reference).
- **Project create:** TimelineViewModel.CreateProjectAsync → IProjectsClient.CreateProjectAsync → SelectedProject = project.
- **Timeline → context:** TimelineViewModel.OnSelectedProjectChanged does NOT call IContextManager.SetActiveProject. Context is never updated from Timeline selection.
- **Context → EffectsMixer:** None. EffectsMixerViewModel has no subscription to ProjectChangedEvent or IContextManager.

### Event flow

- **ProjectChangedEvent:** Published by ContextManager.SetActiveProject (ContextManager.cs:295) and when AppState.Project.CurrentProjectId changes (ContextManager.cs:119). No ViewModels subscribe.
- **ProjectStore:** StoreIntegration syncs ProjectStore.CurrentProject to AppState. ProjectStore.SetCurrentProject exists but has no callers in ViewModel layer. ProjectStore.CurrentProject is set internally in LoadProjectAsync (line 185, 258).

### Project activation path

1. User opens project via TimelineProjectHandlers or selects in Timeline ComboBox.
2. TimelineViewModel.SelectedProject is set.
3. OnSelectedProjectChanged fires LoadTracksForProject, LoadProjectAudioAsync.
4. IContextManager.SetActiveProject is never called — global context (ActiveProjectId) stays stale.
5. ProjectChangedEvent never fires from Timeline actions.

### Timeline activation path

- OnSelectedProjectChanged clears Tracks when null; loads tracks via ITimelineTrackService.GetTracksAsync when project set.
- LoadProjectAudioAsync loads project audio files.
- RecentProjectsService.AddRecentProjectAsync called (best-effort).

### Effects/mixer synchronization path

- EffectsMixerViewModel.SelectedProjectId: never set by any external source. Starts null.
- OnSelectedProjectIdChanged triggers LoadProjectSelectionDataAsync when value is non-null.
- No ProjectChangedEvent subscription; no IContextManager read in OnActivatedAsync.
- EffectsMixerView creates ViewModel with no project context; no ComboBox to select project in EffectsMixer UI.

### Context loss points

- TimelineViewModel.SelectedProject changes do not propagate to IContextManager or ProjectChangedEvent.
- EffectsMixerViewModel.SelectedProjectId has no source; user cannot select project in EffectsMixer panel.
- ProjectStore and TimelineViewModel.Projects are separate; no sync between them.

### Panel-order dependencies

- If user opens project in Timeline first, EffectsMixer never learns. If user could select project in EffectsMixer (no UI exists), Timeline would not know.
- Both panels operate independently with no shared project context.

---

## 10. Current Defects / Coherence Gaps (Pre-Implementation — Historical)

*Table below described gaps before Pass 02 implementation. Resolved items are mapped in §13 (C1–C7). Remaining: duplicate project sources (Timeline vs ProjectStore); optional C3 full ProjectStore sync.*

| Defect | Affected files/classes | User symptom | Root cause | Priority |
|--------|------------------------|--------------|------------|----------|
| EffectsMixerViewModel.SelectedProjectId never synced | EffectsMixerViewModel | Mixer/effects show nothing or wrong project; user cannot use mixer for active project | No ProjectChangedEvent subscription; no IContextManager sync; no UI to select project | P0 (addressed) |
| ProjectChangedEvent has no subscribers | All panels | Project change in one panel never reaches others | No ViewModel subscribes to ProjectChangedEvent | P0 (addressed for mixer) |
| TimelineViewModel.SelectedProject does not sync to context | TimelineViewModel | IContextManager.ActiveProjectId stale when user selects project in Timeline | OnSelectedProjectChanged does not call IContextManager.SetActiveProject | P0 (addressed) |
| Stale mixer state on project switch | EffectsMixerViewModel | Previous project's effect chains/mixer state shown after switch | SelectedProjectId never updates; no clear on project null | P1 (addressed) |
| EffectsMixer OnActivatedAsync no-op | EffectsMixerViewModel | Activating EffectsMixer panel does not sync project from context | OnActivatedAsync returns Task.CompletedTask; no IContextManager read | P1 (addressed) |
| Duplicate project sources | TimelineViewModel, ProjectStore | Two sources of truth can drift | Timeline has own Projects; ProjectStore separate; no sync | P2 (leftover) |

---

## 11. Implementation Checklist

- [x] Audit project selection flow: where does SelectedProject/SelectedProjectId originate?
- [x] Confirm IContextManager or ProjectSelectedEvent reaches EffectsMixerViewModel.
- [x] If gap: add subscription to project change; call LoadProjectSelectionDataAsync.
- [x] Verify staleness guards (SelectedProjectId != projectId) in EffectsMixerViewModel.
- [x] Run verify.ps1 -Quick to completion; record **authoritative** artifact path (must match `latest_pointer.json`).

---

## 12. Execution Record — Files, Commands, Artifacts

**Files changed (implementation):**
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` — `IContextManager` via `AppServices.TryGetContextManager`; `OnSelectedProjectChanged` calls `SetActiveProject`.
- `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs` — `IEventAggregator` / `IContextManager` via `AppServices.TryGet`; `ProjectChangedEvent` subscription in `OnActivatedAsync`; `OnProjectChanged` sets `SelectedProjectId`; `OnActivatedAsync` syncs from `ActiveProjectId`; `OnSelectedProjectIdChanged` clears collections when null; `Dispose` unsubscribes; `LoadProjectSelectionDataAsync` toast on non-cancel failure.
- `src/VoiceStudio.App.Tests/ViewModels/EffectsMixerViewModelSeamTests.cs` — `SelectedProjectId_SetToNull_ClearsStaleState`.

**Docs:** This pass doc; [WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md](WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md) (proof honesty for incomplete runs).

**Commands (closure):**
- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TimelineViewModel|FullyQualifiedName~EffectsMixerViewModel"`
- `.\scripts\verify.ps1 -Quick` (must finish; confirm `verification_report.md` and `latest_pointer.json`)

**Authoritative verify artifact:** `artifacts/verify/20260324_012252` (PASSED; `latest_pointer.json` aligned).

**Superseded (incomplete — no `verification_report.md`):** `artifacts/verify/20260323_144107`, `20260324_012037`, `20260324_012215`.

---

## 13. Closure Record — Change Matrix C1–C7 vs Implementation

| ID | Target | Delivered | Test / proof |
|----|--------|-----------|--------------|
| C1 | Project activation → Timeline | `LoadTracksForProject` / `LoadProjectAudioAsync` still on `SelectedProject`; handlers set `SelectedProject` | Existing `TimelineViewModel` tests; manual open/create |
| C2 | Project activation → EffectsMixer | `ProjectChangedEvent` → `SelectedProjectId` | Subscription + handler in `EffectsMixerViewModel` |
| C3 | Timeline → context | `SetActiveProject` in `OnSelectedProjectChanged` | Audit; no `ProjectStore.SetCurrentProject` (optional leftover) |
| C4 | EffectsMixer on activate | `OnActivatedAsync` sets `SelectedProjectId` from `ActiveProjectId` | Code path; seam test env may lack `IContextManager` registration |
| C5 | Staleness guard | `LoadProjectSelectionDataAsync` checks `SelectedProjectId != projectId` | Pre-existing |
| C6 | Clear stale state | Null `SelectedProjectId` clears chains, presets, sends, returns, subgroups, `MixerState`, `Master` | `SelectedProjectId_SetToNull_ClearsStaleState` |
| C7 | Workflow-specific failures | Toast title `"Effects/Mixer sync failed"` on load failure | Manual / log; timeline still uses existing `LoadTracksForProject` error surfacing |

**User-visible outcomes:** Open/switch project in Timeline updates global project context; Effects/Mixer loads or clears for that project; failed mixer load shows step-specific toast.

**Intentionally out-of-scope:** Per §6; plus **Channels** collection not cleared on project null (meter UI may retain channel shells until next meter load); **SelectedAudioId** independent of project; **ProjectStore** vs **Timeline.Projects** duplication.

---

## 14. Test Coverage vs Main Chain (Honest)

| Scenario | Covered by |
|----------|------------|
| Project change updates context (`SetActiveProject`) | Code review + app integration; no isolated unit with real `ContextManager` in test host |
| Effects/mixer context updates on `SelectedProjectId` | Async load path + `SelectedProjectId_SetToNull_ClearsStaleState` |
| Stale mixer/effects cleared on null project | `SelectedProjectId_SetToNull_ClearsStaleState` |
| Sync failure → workflow-specific toast | Implementation only; no toast mock test |
| No prior project chains/presets after clear | Asserted in seam test |

**Gap (acceptable for this pass):** `ProjectChangedEvent` handler setting `SelectedProjectId` without full `AppServices` `IContextManager` in `TestAppServicesHelper` — add in a future pass if regression risk warrants it.
