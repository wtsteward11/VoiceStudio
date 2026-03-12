# Timeline Hardening and Next Tasks Plan — 2026-03-11

**Purpose:** Ruthless mentor assessment response. Comprehensive plan covering verification of completed work, honest status of partial work, and the next set of tasks in correct order.

**Audience:** Senior software architect, engineering lead.

---

## 1. Verification of Completed Work

### 1.1 Task 9.4 — Enhancement Preview/Apply Ownership

**Status:** ✅ **DONE**

**Evidence:** `src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs` lines 1689–1694

```csharp
// Task 9.4: Preview/Apply ownership — KEEP in ViewModel.
// PreviewEnhancedAudioAsync: UI state (IsPlayingEnhanced) + player orchestration (_audioPlayer.PlayFileAsync).
// StopEnhancedPreview: Player stop + command refresh. No backend, no domain policy.
// ApplyEnhancedAudioAsync: Reload profiles + clear state + toast. Backend does not yet support reference_audio_url
// updates; when it does, apply logic should move to IProfileEnhancementService.ApplyAsync.
```

**Assessment:** Honest stopgap. Ownership is documented. Apply will migrate when backend supports it.

---

### 1.2 Task 11.1 — Verification Closure

**Status:** ✅ **DONE**

**Evidence:** `docs/reports/verification/VERIFICATION_CLOSURE_2026-03-11.md`

- Commands executed with exact results
- Gate status recorded (all PASS)
- Artifact locations documented
- No dangling "still running" status

---

### 1.3 P2 Profiles Bypass Migration

**Status:** ✅ **DONE** (16/16)

**Evidence:** `docs/reports/verification/REQUEST_COORDINATION_AUDIT_2026-03-11.md`

All 16 ViewModels now use `IProfilesClient` for `GetProfilesAsync`. TimelineViewModel is among them (line 40 of audit).

---

### 1.4 Degraded-Mode Tests in CI

**Status:** ✅ **DONE**

**Evidence:** `.github/workflows/ci.yml` line 129

```yaml
- name: 429 degraded mode gate
  run: dotnet test ... --filter "TestCategory=DegradedMode|FullyQualifiedName~RateLimitToastDedupeTests"
```

---

## 2. Honest Status: Task 10.1 — Timeline Hardening

### 2.1 What Was Done (First-Stage Hardening)

| Improvement | Location | Status |
|-------------|----------|--------|
| IDialogService injection | TimelineViewModel constructor | ✅ |
| DeleteProjectAsync uses _dialogService.ShowConfirmationAsync | TimelineViewModel | ✅ |
| DeleteClipAsync (single clip) uses _dialogService | TimelineViewModel | ✅ |
| DeleteSelectedClipsAsync uses _dialogService | TimelineViewModel | ✅ |
| GetProfilesAsync migrated to IProfilesClient | TimelineViewModel | ✅ |
| IProjectsClient for GetProjectsAsync | TimelineViewModel | ✅ |

### 2.2 What Remains (Not Hardened)

#### 2.2.1 Service Locator in TimelineView Callbacks

**File:** `src/VoiceStudio.App/Views/Panels/TimelineView.xaml.cs`

| Line | Context | Issue |
|------|---------|-------|
| 652 | Paste undo callback | `ServiceProvider.GetBackendClient()` |
| 664 | Paste redo callback | `ServiceProvider.GetBackendClient()` |
| 679 | Paste save to backend | `ServiceProvider.GetBackendClient()` |
| 734 | Duplicate undo callback | `ServiceProvider.GetBackendClient()` |
| 746 | Duplicate redo callback | `ServiceProvider.GetBackendClient()` |
| 761 | Duplicate save to backend | `ServiceProvider.GetBackendClient()` |

**Impact:** View owns transport logic in undo/redo paths. Violates panel hardening pattern.

#### 2.2.2 Direct IBackendClient Usage in TimelineViewModel

**File:** `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs`

| Method / Endpoint | Line(s) | Workflow Family |
|-------------------|---------|-----------------|
| GetTranscriptionAsync | 292 | Transcription loading |
| CreateClipAsync | 688, 1430, 1465 | Clip CRUD |
| DeleteClipAsync | 773, 1883 | Clip CRUD |
| SynthesizeVoiceAsync | 976 | Synthesis |
| SaveAudioToProjectAsync | 998, 1465 | Project audio |
| GetTracksAsync | 1233 | Track lifecycle |
| CreateTrackAsync | 1244, 1300 | Track lifecycle |
| ListProjectAudioAsync | 1567 | Playback data |
| GetProjectAudioAsync | 1609 | Playback data |
| GetWaveformDataAsync | 1528, 1659, 1705 | Visualization |
| GetSpectrogramDataAsync | 1669 | Visualization |

**Count:** 18+ direct `_backendClient` calls across 6+ workflow families.

**Assessment:** Timeline is a god-panel in transport terms. One major workflow family must be extracted before claiming "hardened."

---

## 3. Next Set of Tasks (Recommended Order)

### Task 1: Finish Timeline Hardening Properly

**Priority:** Highest. Stopping halfway is how structural debt survives.

#### 1A. Eliminate Service-Locator / Raw Backend Callbacks in TimelineView

**Scope:** `TimelineView.xaml.cs` paste/duplicate undo/redo paths.

**Actions:**
1. Move `PasteClipAsync` and `DuplicateClipAsync` logic into `TimelineViewModel` (or a dedicated command).
2. Inject `IBackendClient` or `ITimelineClipService` into ViewModel; pass to View only via ViewModel commands.
3. Replace all 6 `ServiceProvider.GetBackendClient()` usages in TimelineView with ViewModel-owned operations.
4. Undo/redo callbacks must invoke ViewModel methods, not service locator.

**Definition of done:** Zero `ServiceProvider.GetBackendClient()` in TimelineView.xaml.cs.

**Validation:** Grep for `GetBackendClient` in TimelineView.xaml.cs → 0 matches.

---

#### 1B. Introduce One Focused Timeline Service Seam

**Scope:** Extract one workflow family from direct `_backendClient` coupling.

**Best first candidate:** `ITimelineClipService`

**Interface (minimal):**
```csharp
public interface ITimelineClipService
{
    Task<AudioClip> CreateClipAsync(string projectId, string trackId, AudioClip clip, CancellationToken ct = default);
    Task DeleteClipAsync(string projectId, string trackId, string clipId, CancellationToken ct = default);
    // UpdateClipAsync if needed for move/resize
}
```

**Implementation:** `TimelineClipService` wraps `IBackendClient` calls. Enables:
- Undo-friendly operations
- Single place for clip persistence policy
- Future: request coordination for clip endpoints if needed

**Note:** `ITimelineGateway` exists (`VoiceStudio.Core.Gateways`) with AddClipAsync, RemoveClipAsync, etc. The panel `Views/Panels/TimelineViewModel` does not use it; it calls `IBackendClient` directly. `Features/Timeline/TimelineViewModel` uses `ITimelineGateway`. The panel may need an adapter (GatewayResult → exceptions, ClipInfo ↔ AudioClip) or a dedicated `ITimelineClipService` that wraps BackendClient's existing clip API. Prefer reusing ITimelineGateway if the API shapes can be bridged without excessive mapping.

**Definition of done:** TimelineViewModel uses `ITimelineClipService` (or equivalent) for CreateClipAsync and DeleteClipAsync. At least one workflow family no longer directly calls `_backendClient`.

---

#### 1C. Remove One Major Class of Direct _backendClient Usage

**Target cluster:** Clip CRUD + undo callbacks.

**Rationale:**
- Central to Timeline behavior
- Already touches dialogs and destructive actions
- Clear architecture seam
- Enables 1A (paste/duplicate) to route through ViewModel

**Definition of done:** Clip create/delete flows go through `ITimelineClipService` (or injected seam). TimelineViewModel has fewer direct `_backendClient` calls.

**Validation:** `grep "_backendClient\.(CreateClipAsync|DeleteClipAsync)" TimelineViewModel.cs` → 0 matches (or reduced to 0 for clip ops).

---

### Task 2: Tighten Dialog-Pattern Baseline

**Priority:** Immediately after Timeline 1A–1C.

**Scope:** `scripts/ci/check_dialog_pattern.py`, `.ci/dialog_pattern_baseline.txt`

**Current baseline:** 1 entry (PresetLibraryViewModel:587 — deferred custom multi-field dialog).

**Actions:**
1. Re-scan ViewModels for ConfirmationDialog/ContentDialog violations.
2. Remove any Timeline-related exceptions that are no longer valid (TimelineViewModel now uses IDialogService).
3. If new violations exist in other panels, either fix or document with removal plan.

**Definition of done:** Baseline shrinks or stays minimal. No invalid exceptions.

**Validation:** `python scripts/ci/check_dialog_pattern.py` → exit 0; baseline file has no stale entries.

---

### Task 3: Refresh Request-Coordination Audit

**Priority:** After Timeline and dialog baseline.

**Scope:** `docs/reports/verification/REQUEST_COORDINATION_AUDIT_2026-03-11.md`

**Current state:** Profiles 16/16 migrated. Projects and engines documented.

**Actions:**
1. Add section for Timeline-supporting endpoints (tracks, clips, project audio, waveform, spectrogram).
2. For each: canonical path, consumers, bypasses, coordination status.
3. Identify any stable shared endpoints that should be coordinated but are not.
4. Update "Next Steps" to reflect Timeline transport extraction.

**Definition of done:** Audit artifact reflects current state and any new bypasses from Timeline work.

---

### Task 4: Promote Degraded-Mode Tests — Verify Stability

**Priority:** After audit refresh.

**Current state:** Degraded-mode gate already in ci.yml (Task 11.2 done).

**Actions:**
1. Run degraded-mode tests locally and in CI; confirm they are stable.
2. If flaky: fix stability first (no promotion of flaky tests).
3. Document in verification report that 429/degraded behavior is enforced.

**Definition of done:** Degraded-mode tests are stable and enforced in the right pipeline.

---

### Task 5: Expand Timeline Bounded-Request Proof

**Priority:** After Timeline transport extraction (Task 1).

**Current proof:** `docs/reports/verification/TIMELINE_BOUNDED_REQUEST_PROOF_2026-03-11.md`

- Covers: Refresh (GetProjectsAsync) + LoadProfiles (GetProfilesAsync).
- Test: `TimelinePanelScenario_RefreshLoadProfiles_BoundedRequestCounts`.

**Gap:** Proof does not cover clip CRUD, track load, synthesis, or visualization flows.

**Actions:**
1. After Task 1 (clip seam), add scenario: open Timeline → load projects → select project → load tracks → perform one clip action (create or delete).
2. Assert bounded request behavior for stable reads and no pointless fan-out.
3. Update proof document with new scenario and expected counts.

**Definition of done:** A Timeline scenario can fail if request behavior regresses. Proof covers at least one representative clip action.

---

### Task 6: Second Strict Mypy Slice

**Priority:** After Timeline and proof work.

**Scope:** Next backend route file per STRICT_MYPY_BURNDOWN_SUBPLAN (e.g. `voice/analysis.py`).

**Rationale:** Python typing is important but not the biggest blast-radius risk. Architecture and proof come first.

---

### Task 7: Second Workflow Consolidation Slice

**Priority:** After mypy slice.

**Scope:** Find next duplicated workflow/bootstrap chunk; extract cleanly.

**Constraint:** One repeated seam at a time. No broad workflow rewrite.

---

### Task 8: First Real Skip-Debt Execution Batch

**Priority:** Last in this block.

**Scope:** Use skip report; remove/fix one category cluster (e.g. flaky or defect-masking).

**Rationale:** Important but does not beat unfinished Timeline architecture and proof.

---

## 4. Execution Order Summary

| Order | Task | Blocker |
|-------|------|---------|
| 1 | Timeline 1A: Kill service locator in TimelineView callbacks | None |
| 2 | Timeline 1B: Introduce ITimelineClipService (or equivalent) | None |
| 3 | Timeline 1C: Remove clip CRUD from direct _backendClient | 1B |
| 4 | Task 2: Tighten dialog-pattern baseline | 1A–1C |
| 5 | Task 3: Refresh request-coordination audit | 2 |
| 6 | Task 4: Verify degraded-mode test stability | None (can parallel) |
| 7 | Task 5: Expand Timeline bounded-request proof | 1A–1C |
| 8 | Task 6: Second mypy slice | 1–5 |
| 9 | Task 7: Second workflow consolidation | 6 |
| 10 | Task 8: First skip-debt batch | 7 |

---

## 5. Hard Truth (Ruthless Assessment)

### What Is Complete

- Profiles lane substantially compressed (IBackendClient removed, IProfilesClient, IProfileEnhancementService).
- Verification closure properly documented.
- Timeline dialog handling and destructive action ownership improved.
- P2 profiles bypass migration complete (16/16).
- Degraded-mode tests in CI.
- Narrow Timeline bounded-request proof (Refresh + LoadProfiles).

### What Is Not Complete

- Timeline is not fully hardened.
- Timeline is still too backend-heavy (18+ direct _backendClient calls).
- TimelineView still uses service locator in paste/duplicate callbacks.
- No dedicated Timeline transport seam exists yet.

### One-Sentence Priority

**Stop calling Timeline hardened and finish the first real Timeline transport extraction.**

---

## 6. Changelog

| Date | Change |
|------|--------|
| 2026-03-11 | Initial plan; verification of 9.4, 11.1, 10.1 partial; next 8 tasks in order |
