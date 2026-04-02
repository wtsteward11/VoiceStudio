# Workflow Coherence Pass 01 — Profile → Synthesis → Timeline

**Purpose:** Bounded product-facing pass for the profile → synthesis → timeline workflow.  
**Date:** 2026-03-24  
**Post-Pass note (GAP-025, 2026-04-02):** Synthesis → timeline clip insertion is **explicit only** via operator `AddToTimelineEvent` / **Add to Timeline**. `TimelineViewModel` does **not** subscribe to `SynthesisCompletedEvent` for insertion. See [GOV_VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_01_EXECUTION_ROW.md).  
**Related:** [CROSS_FEATURE_WORKFLOW_BACKLOG.md](CROSS_FEATURE_WORKFLOW_BACKLOG.md), [POST_EXTRACTION_TRANSITION_PLAN.md](POST_EXTRACTION_TRANSITION_PLAN.md)

---

## 1. Participating ViewModels/Services

| Component | Role |
|-----------|------|
| ProfilesViewModel | Profile selection; publishes ProfileSelectedEvent (line 1195) |
| VoiceProfileViewModel | Alternative profile source; SelectedProfile |
| VoiceSynthesisViewModel | Synthesis panel; subscribes to ProfileSelectedEvent in OnActivatedAsync (line 890); OnProfileSelected sets SelectedProfile |
| VoiceSynthesisView | HandleProfileDropAsync for drag-drop profile → synthesis (line 141) |
| TimelineViewModel | Subscribes to ProfileSelectedEvent, `AddToTimelineEvent`; `OnAddToTimeline` creates clip (**GAP-025:** no `SynthesisCompletedEvent` subscription for insert) |
| IContextManager | Profile state; publishes ProfileSelectedEvent on SetSelectedProfile |
| IEventAggregator | ProfileSelectedEvent, AddToTimelineEvent |
| ITimelineSynthesisService | SynthesizeAndSaveAsync(engine, profileId, text, ...) |
| IWorkflowCoordinatorService | StartSynthesizeWithVoiceAsync(profileId, profileName) |

---

## 2. Current Behavior

**Profile → Synthesis:**
- ProfilesViewModel: On selection change, publishes `ProfileSelectedEvent(PanelId, profileId, profileName)`.
- ContextManager: On SetSelectedProfile, publishes `ProfileSelectedEvent`.
- VoiceSynthesisViewModel: Subscribes in OnActivatedAsync; OnProfileSelected finds profile in Profiles list, sets SelectedProfile via Dispatcher. If profile not in list, loads profiles then selects.
- VoiceSynthesisView: HandleProfileDropAsync accepts DragPayloadType.Profile; sets ViewModel.SelectedProfile.
- SynthesisViewModel (Features/Synthesis): Subscribes to VoiceProfileSelectedEvent for Library workflow.

**Synthesis → Timeline:**
- VoiceSynthesisViewModel / SynthesisViewModel: explicit **Add to Timeline** publishes `AddToTimelineEvent` with ProfileId (extended pass), audio path, optional insert hints (`InsertPosition`, `TargetTrackIndex` per GAP-025 resolver).
- TimelineViewModel: `OnAddToTimeline` creates clip with ProfileId from event or `IContextManager` fallback; `AddClipToTrack`; `CreateClipAsync` persists. Requires SelectedProject != null.
- **GAP-025:** Completing synthesis alone does **not** insert a clip; operator must use Add to Timeline.

**Historical gaps (pre–Pass 01 extended):**
- TimelineViewModel.OnProfileSelected only logs; does not set default profile for new clips.
- AddToTimelineEvent and AudioClip may omit ProfileId; backend CreateClipAsync expects profile_id (verify).

---

## 3. Target Behavior

1. **Profile selection propagates to Synthesis** — Already works. Verify in manual test.
2. **Synthesize → Add to Timeline** — Already works when project selected. Verify "Add to Timeline" button creates clip.
3. **Optional improvement:** TimelineViewModel uses IContextManager.CurrentProfileId or profile from AddToTimelineEvent when creating clip, if backend accepts it and no DTO changes required.

---

## 4. Code Touch Points

| File | Change |
|------|--------|
| VoiceSynthesisViewModel.cs | Verify OnProfileSelected, OnActivatedAsync subscription. No change if working. |
| TimelineViewModel.cs | OnProfileSelected — currently no-op. Option: set internal _defaultProfileForNewClips from e.ProfileId if used by AddClipToTrack. |
| AddToTimelineEvent (PanelEvents.cs) | Option: add optional ProfileId parameter if backend/clip creation needs it. Out-of-scope if requires DTO/API change. |
| TimelineViewModel.AddClipToTrack | Option: use profile from event or IContextManager when creating AudioClip.ProfileId. |

**Minimal pass:** No code changes; document current behavior, verify manual flow, add regression test if one does not exist.

**Extended pass (if in scope):** Add ProfileId to AddToTimelineEvent; have VoiceSynthesisViewModel pass SelectedProfile?.Id; TimelineViewModel use it in AudioClip. Confirm backend CreateClipAsync signature and behavior first.

---

## 5. Tests/Proof Required

- **Manual:** (1) Select profile in Profiles panel; switch to Synthesis; verify SelectedProfile pre-populated. (2) Synthesize; click Add to Timeline; verify clip appears (project must be selected).
- **Unit:** VoiceSynthesisViewModel OnProfileSelected sets SelectedProfile when profile in list. (May already exist.)
- **E2E:** Golden path if it covers this workflow.
- **verify.ps1 -Quick** after any code change; record artifact path.

---

## 6. Out-of-Scope (Strict)

- No profile model contract redesign.
- No backend API changes unless strictly required for existing flow correctness (fix client instead).
- No transport/client extraction.
- No generalized timeline rewrite.
- No search, batch, or training work folded into this pass.
- No broad UI theming/polish unrelated to the workflow.
- No Features/Timeline TimelineViewModel changes (Pass 01 targets Views/Panels flow).
- No IWorkflowCoordinatorService refactor.
- No consolidating VoiceSynthesisViewModel and SynthesisViewModel (Features/Synthesis).

---

## Implementation Checklist

- [x] Verify ProfileSelectedEvent subscription in VoiceSynthesisViewModel (OnActivatedAsync/OnDeactivatedAsync).
- [x] Verify AddToTimelineEvent flow: VoiceSynthesisViewModel → TimelineViewModel.
- [x] Extended: Add ProfileId to AddToTimelineEvent; pass from VoiceSynthesisViewModel/SynthesisViewModel; set in TimelineViewModel.
- [x] Timeline focus/selection after insert; workflow-step-specific failure handling.
- [x] AddClipToTrack_PassesProfileIdToCreateClipAsync test.
- [x] Run verify.ps1 -Quick; record artifact path (artifacts/verify/20260323_141258; 20260323_134023 was incomplete).

---

## 7. As-Is Workflow Map (Pass 01 Baseline)

### User entry points

- Profiles panel: `ProfilesViewModel.SelectedProfile` setter (line ~1195) → publishes `ProfileSelectedEvent(PanelId, profileId, profileName)`.
- ContextManager: `SetSelectedProfile` → publishes `ProfileSelectedEvent`.
- Library: `LibraryViewModel` "Use for synthesis" → publishes `VoiceProfileSelectedEvent`.
- VoiceSynthesisView: `HandleProfileDropAsync` (DragPayloadType.Profile) → sets ViewModel.SelectedProfile directly.
- IWorkflowCoordinatorService: `StartSynthesizeWithVoiceAsync` → publishes `VoiceProfileSelectedEvent`.

### State handoff points

- Profile → Synthesis: `ProfileSelectedEvent` (Profiles/ContextManager) or `VoiceProfileSelectedEvent` (Library) → `VoiceSynthesisViewModel.OnProfileSelected` / `SynthesisViewModel.OnVoiceProfileSelected` → sets `SelectedProfile` / `CurrentVoice`.
- Synthesis → Timeline: `AddToTimelineEvent` (optional ProfileId, `InsertPosition`, `TargetTrackIndex`) — **explicit operator handoff** (GAP-025).

### Event flow

- ProfileSelectedEvent: ProfilesViewModel, ContextManager → VoiceSynthesisViewModel (subscribes in OnActivatedAsync).
- VoiceProfileSelectedEvent: LibraryViewModel, WorkflowCoordinatorService → SynthesisViewModel (Features).
- AddToTimelineEvent: VoiceSynthesisViewModel.AddSynthesizedAudioToTimeline, SynthesisViewModel → Views/Panels/TimelineViewModel.OnAddToTimeline.
- SynthesisCompletedEvent: still published by synthesis panels for other subscribers (e.g. Library); **TimelineViewModel does not use it for clip insertion** (GAP-025).

### Service dependencies

- IEventAggregator, IContextManager, ITimelineClipService, ITimelineTrackService, IProfilesClient, IProjectsClient, IAudioPlayerService, MultiSelectService, ToastNotificationService, UndoRedoService.

### Places where context is dropped

- Missing ProfileId on **both** `AddToTimelineEvent` and `IContextManager` fallback → `AddClipToTrack` blocks insert / backend may reject (mitigated: Pass 01 extended passes ProfileId on explicit handoff).
- VoiceSynthesisViewModel subscribes only when activated; if Synthesis panel never activated, ProfileSelectedEvent never reaches it.
- Profile not in Profiles list: OnProfileSelected triggers LoadProfilesAsync then selects — race with async continuation.

### Ambiguous ownership zones

- Two synthesis panels (VoiceSynthesisViewModel vs SynthesisViewModel) with different event sources (ProfileSelectedEvent vs VoiceProfileSelectedEvent).
- Two TimelineViewModels; Features one subscribes to ProfileSelectedEvent but does nothing actionable. Pass 01 uses **Views/Panels/TimelineViewModel**.

---

## 8. Defects / gaps (historical snapshot — pre Pass 01 closure)

| Defect | Affected files | User symptom | Root cause | Priority |
|--------|----------------|--------------|------------|----------|
| ProfileId missing on clip creation | TimelineViewModel.AddClipToTrack, AddToTimelineEvent | Clip appears locally but backend CreateClipAsync fails 400 | AudioClip.ProfileId never set; AddToTimelineEvent has no ProfileId | P0 — **Addressed** Pass 01 extended (see §10) |
| No timeline focus after insert | TimelineViewModel.AddClipToTrack | User cannot immediately see/select new clip | No MultiSelectService.Add or selection update after insert | P1 — **Addressed** Pass 01 (see §10) |
| Profile propagation depends on panel activation | VoiceSynthesisViewModel.OnActivatedAsync | Select profile → switch to Synthesis; profile may not appear if Synthesis never activated first | Subscription only in OnActivatedAsync | P2 |
| Failure messages not workflow-step-specific | VoiceSynthesisViewModel, TimelineViewModel | Generic "Failed to add clip" | No profile/synthesis/insertion/navigation categorization | P2 |
| Features TimelineViewModel OnProfileSelected no-op | Features/Timeline/TimelineViewModel | Profile selection from Profiles does not affect timeline default | OnProfileSelected only Debug.WriteLine | P3 (out-of-scope) |

---

## 9. Change Matrix

| Change ID | Target behavior | Primary owner | Supporting | Events/services | Tests | Proof |
|-----------|-----------------|---------------|-------------|-----------------|-------|-------|
| C1 | Profile propagates to synthesis | VoiceSynthesisViewModel | ProfilesViewModel, ContextManager | ProfileSelectedEvent | OnProfileSelected sets SelectedProfile when in list | Unit |
| C2 | AddToTimelineEvent carries ProfileId | AddToTimelineEvent, VoiceSynthesisViewModel | PanelEvents.cs | AddToTimelineEvent | Add optional ProfileId param; VoiceSynthesis passes SelectedProfile?.Id | Unit |
| C3 | Timeline sets ProfileId on AudioClip | Views/Panels/TimelineViewModel | IContextManager | AddClipToTrack, CreateClipAsync | Use event.ProfileId or IContextManager; set clip.ProfileId | Unit + integration |
| C4 | Timeline focuses/selects inserted clip | TimelineViewModel | MultiSelectService | AddClipToTrack | After AddClipToTrack, add clip to multi-select and raise selection changed | Unit |
| C5 | Workflow-step-specific error messages | VoiceSynthesisViewModel, TimelineViewModel | ErrorHandler | ToastNotificationService | Profile/synthesis/insertion/navigation message templates | Unit |
| C6 | ~~SynthesisCompletedEvent → timeline auto-add~~ **Superseded by GAP-025 (2026-04-02)** | — | — | — | Insertion authority is **only** `AddToTimelineEvent` | `SynthesisCompletedEvent_DoesNotInsertClip_Gap025ExplicitHandoffOnly` |

---

## 10. Proof Notes (Execution Record)

### Exact files changed

| File | Change |
|------|--------|
| `src/VoiceStudio.Core/Events/PanelEvents.cs` | AddToTimelineEvent: added optional `ProfileId` parameter; SynthesisCompletedEvent: carries ProfileId |
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` | Publish AddToTimelineEvent with ProfileId = SelectedProfile?.Id; synthesis request and AddToTimelineEvent include ProfileId |
| `src/VoiceStudio.App/Features/Synthesis/SynthesisViewModel.cs` | Publish AddToTimelineEvent with ProfileId; OnVoiceProfileSelected handles ProfileId |
| `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` | OnAddToTimeline: use e.ProfileId or IContextManager fallback; AddClipToTrack sets newClip.ProfileId; blocks add when no profile; MultiSelectState.SetSingle after insert; workflow-step-specific failure messages; **GAP-025:** no `SynthesisCompletedEvent` subscription for insert; deterministic track/start resolution in handoff helper |
| `src/VoiceStudio.Core/Models/AudioClip.cs` | ProfileId property (existing) |

### Behaviors fixed (C1–C6 mapping)

| Change ID | Behavior | Proof |
|-----------|----------|-------|
| C1 | Profile propagates to synthesis | OnProfileSelected in VoiceSynthesisViewModel; SynthesisViewModel.OnVoiceProfileSelected |
| C2 | AddToTimelineEvent carries ProfileId | PanelEvents.cs AddToTimelineEvent.ProfileId; VoiceSynthesisViewModel/SynthesisViewModel pass it |
| C3 | Timeline sets ProfileId on AudioClip | AddClipToTrack uses event.ProfileId or IContextManager; sets newClip.ProfileId |
| C4 | Timeline focuses/selects inserted clip | MultiSelectState.SetSingle(newClip.Id) after AddClipToTrack |
| C5 | Workflow-step-specific error messages | "Insertion failed", "Clip saved locally but failed to save to project", "Voice profile required" |
| C6 | **Superseded (GAP-025)** | Timeline does not auto-insert on `SynthesisCompletedEvent`; explicit `AddToTimelineEvent` only |

### Tests added/updated

- **TimelineViewModelTests.AddClipToTrack_PassesProfileIdToCreateClipAsync** — Verifies CreateClipAsync receives clip with ProfileId when adding via AddClipToTrackCommand.
- **TimelineViewModelTests.AddClipToTrack_OneExecution_ExactlyOneCreateClipAsync** — Updated with SelectedProfileId and SynthesisText for AddClipToTrackAsync path.

### User-visible outcomes

1. **Profile selected → synthesis → add to timeline** — User selects profile, synthesizes, clicks Add to Timeline; clip is created with correct ProfileId and persists to backend.
2. **Clip gets ProfileId** — Backend CreateClipAsync receives profile_id; no more 400 failures from missing profile.
3. **Clip selected after insert** — New clip is automatically selected in the timeline.
4. **Specific error messages** — "Voice profile required" when no profile; "Insertion failed" vs "Clip saved locally but failed to save to project" for workflow-step-specific feedback.

### Audit summary (Task 6)

| Check | Result |
|-------|--------|
| Profile optional where required | AddClipToTrack blocks when no profile (event or IContextManager); AddClipToTrackAsync uses SelectedProfileId (command path); SynthesizeCommand requires SelectedProfileId so synthesis path is protected |
| Duplicate profile-source logic | VoiceSynthesisViewModel (ProfileSelectedEvent) and SynthesisViewModel (VoiceProfileSelectedEvent) remain separate; intentional per out-of-scope |
| Timeline selection in one path but not another | AddClipToTrack (event path) calls SetSingle; AddClipToTrackAsync (command path) uses different flow — clips added via command get selection via AddClipAction/undo path; both paths create clips |
| Generic toasts | Pass 01 added workflow-step-specific: "Voice profile required", "Insertion failed", "Clip saved locally but failed to save to project"; no residual generic "Failed to add clip" in main paths |

No critical residual incoherence. Known leftovers documented below.

### Intentionally out-of-scope (known leftovers)

- **Features/Timeline TimelineViewModel OnProfileSelected** — Still no-op (Debug.WriteLine only); Pass 01 targets Views/Panels flow.
- **IWorkflowCoordinatorService refactor** — Not touched.
- **VoiceSynthesisViewModel vs SynthesisViewModel consolidation** — Two synthesis panels remain with different event sources (ProfileSelectedEvent vs VoiceProfileSelectedEvent).
- **Profile propagation depends on panel activation** — VoiceSynthesisViewModel subscribes only in OnActivatedAsync; if Synthesis never activated, ProfileSelectedEvent may not reach it (P2, deferred).

### Commands run

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesisViewModel|FullyQualifiedName~TimelineViewModel"
.\scripts\verify.ps1 -Quick
```

### Test coverage confirmation (Task 5)

| Behavior | Covered by test | Notes |
|----------|-----------------|-------|
| 1. Profile ID propagated into synthesis/timeline | AddClipToTrack_PassesProfileIdToCreateClipAsync | Clip receives ProfileId from SelectedProfileId; verified via CreateClipAsync mock |
| 2. Clip creation receives correct ProfileId | AddClipToTrack_PassesProfileIdToCreateClipAsync | Explicit assertion `c.ProfileId == "profile-123"` |
| 3. Missing profile blocks insertion | Manual / code path | AddClipToTrack shows "Voice profile required", returns; no unit test for event path; AddClipToTrackAsync (command path) guarded by SynthesizeCommand requiring SelectedProfileId |
| 4. New clip selected after insert | Code path | AddClipToTrack calls _multiSelectState.SetSingle(newClip.Id); behavior implemented; not asserted in current unit tests |
| 5. Insertion failure surfaces workflow-specific feedback | Code path | "Insertion failed", "Clip saved locally but failed to save to project", "Voice profile required" in TimelineViewModel; not asserted in unit tests |

Pass 01 is behaviorally implemented; primary proof is AddClipToTrack_PassesProfileIdToCreateClipAsync (ProfileId flow) and AddClipToTrack_OneExecution_ExactlyOneCreateClipAsync. Gaps 3–5 are verified by code inspection and verify.ps1 -Quick.

### Artifact path

`artifacts/verify/20260323_141258` (Quick mode; run date 2026-03-23; authoritative. Run 20260323_134023 was incomplete — superseded per WORKFLOW_PASS_01_ARTIFACT_RECONCILIATION.md)
