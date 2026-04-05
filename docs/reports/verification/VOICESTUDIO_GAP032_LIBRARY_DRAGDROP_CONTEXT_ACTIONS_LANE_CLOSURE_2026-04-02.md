# VoiceStudio GAP-032 Library drag/drop & context actions (core3) — 2026-04-02

**Lane:** **GOV-VOICESTUDIO-GAP032 library drag/drop / context actions** — Library → **Timeline**, **Voice Synthesis**, **Voice Cloning Wizard** via `IDragDropService` + WinUI `AllowDrop` / `DragOver` / `Drop`; context **Add to timeline** uses `AddAssetToTimelineCommand` (no toast-only placeholder).  
**Execution row:** [GOV_VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_01_EXECUTION_ROW.md)  
**Tracker:** **GAP-032** **Closed** — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).  
**Product:** **GAP-045** / **GAP-047** remain **Open** per tracker (this lane is hero-path wiring only).

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — full matrix below executed on a developer machine with normal repo/toolchain access.

## 1) Scope summary

- **`LibraryViewModel`:** `BuildCrossPanelDragPayload` — playback id, URL/`FilePath` fallback, metadata (`AssetType`, `FilePath`, `DurationSeconds`, `LibraryAssetId`).
- **`LibraryView.xaml.cs`:** drag payload from VM; file menu **Add to timeline** invokes `AddAssetToTimelineCommand` when the asset is in the loaded list.
- **`TimelineView`:** root drop surface; library audio drops → **`AddToTimelineEvent`** via **`IEventAggregator`** (not direct `SelectedTrack.Clips.Add`); voice-profile drops → **`ProfileSelectedEvent`**; benign handling for timeline-internal payloads where applicable.
- **`VoiceSynthesisView`:** accepts profile / voice-profile library drops → **`ProfileSelectedEvent`** with **`ImmediateUse`** where aligned with existing synthesis predicates.
- **`VoiceCloningWizardView`:** reference drops → **`CloneReferenceSelectedEvent`** only when a **local `FilePath` exists**; otherwise fail-closed feedback + **failed** drop result.
- **Tests:** `LibraryViewModelSeamTests.BuildCrossPanelDragPayload_*`, `DragDropAndWorkspaceTests.CanDrop_VoiceProfileLibraryAsset_MatchesSynthesisStylePredicate`.
- **No** new FastAPI routes or shared-schema migrations (per execution row hard OUT).

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in test/other projects) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3016** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260402_181517/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260402-182200**; prior same-session cap **20260402-182040**) |

## 3) Proof artifacts (code + docs)

- `docs/design/GOV_VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_01_EXECUTION_ROW.md`
- `src/VoiceStudio.App/ViewModels/LibraryViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/LibraryView.xaml.cs`
- `src/VoiceStudio.App/Views/Panels/TimelineView.xaml`, `TimelineView.xaml.cs`
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml`, `VoiceSynthesisView.xaml.cs`
- `src/VoiceStudio.App/Views/Panels/VoiceCloningWizardView.xaml`, `VoiceCloningWizardView.xaml.cs`
- `src/VoiceStudio.App.Tests/ViewModels/LibraryViewModelSeamTests.cs`
- `src/VoiceStudio.App.Tests/Services/DragDropAndWorkspaceTests.cs`
- `docs/design/GOV_VOICESTUDIO_GAP032_LIBRARY_DRAGDROP_CONTEXT_ACTIONS_01_EXECUTION_ROW.md` (design context; execution row §0 **Closed**)

## 4) Honest limits

- **In proof:** Repo build, full App.Tests, CI pytest slice, Quick verify, and `run_verification.py` with **completion_guard** — not a full manual WinUI certification of every drag gesture on every DPI/theme combination.
- **Still Open:** Full “all panels” Library DnD and **GAP-045** / **GAP-047** product rows — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).

## 5) Closure

**GOV-VOICESTUDIO-GAP032-LIBRARY-DRAGDROP-CONTEXT-ACTIONS-01:** **Closed** 2026-04-02 with proof-backed acceptance per execution row.

**Next:** See [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md) for the next **Open** hero-path row (e.g. **GAP-030**) or a **GAP-045** bounded slice.
