# VoiceStudio GAP-027 Recording → Library → Timeline Lane Closure — 2026-04-02

**Lane:** GOV-VOICESTUDIO-GAP027-RECORDING-LIBRARY-TIMELINE-01 — operator-driven handoff: recording upload publishes `AssetAddedEvent` with **`PanelIds.Recording`**; Library reloads and focuses the new asset; explicit **Add to Timeline** publishes `AddToTimelineEvent`; timeline remains insertion authority (GAP-025 semantics).  
**Execution row:** [GOV_VOICESTUDIO_GAP027_RECORDING_LIBRARY_TIMELINE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP027_RECORDING_LIBRARY_TIMELINE_01_EXECUTION_ROW.md)  
**Tracker:** **GAP-027** **Closed** — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).  
**Product:** **GAP-045** / **GAP-047** remain **Open** per tracker (this lane is hero-path wiring only).

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — full matrix below executed on a developer machine with normal repo/toolchain access.

## 1) Scope summary

- **`RecordingViewModel`:** `AssetAddedEvent` `SourcePanelId` uses **`PanelIds.Recording`** (canonical panel id).
- **`LibraryViewModel`:** Pending focus of `AssetId` after recording-origin `AssetAddedEvent`; applied after successful `LoadAssetsAsync` / reload; **`AddSelectedAssetToTimelineCommand`** / **`AddAssetToTimelineCommand`** publishing **`AddToTimelineEvent`** (playback URL fallback via `BackendClientConfig` + `/api/audio/file/{playbackId}` when path empty).
- **`LibraryView.xaml` / code-behind:** Context menu **Add to Timeline**; AutomationId `LibraryView_Menu_AddToTimeline` (registry + `AutomationIds.Library.MenuAddToTimeline`).
- **`TranscribeViewModel`:** `IsRecordingDerivedAssetSource` accepts `PanelIds.Recording` and legacy `recording-panel` for recording → transcribe prefill continuity.
- **`TimelineViewModel`:** Idempotent **`AddClipToTrack`** when same **audio id** and same resolved **start** on track — no duplicate clip + info path.
- **Tests:** `RecordingViewModelSeamTests`, `LibraryViewModelSeamTests`, `WorkflowCoherenceAdvancedTests.AddToTimelineEvent_DuplicateSameAudioSameStart_DoesNotInsertSecondClip`.
- **No** new FastAPI routes or shared-schema changes.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in other files) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3014** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260402_070703/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260402-071153**) |

## 3) Proof artifacts (code)

- `docs/design/GOV_VOICESTUDIO_GAP027_RECORDING_LIBRARY_TIMELINE_01_EXECUTION_ROW.md`
- `src/VoiceStudio.App/ViewModels/RecordingViewModel.cs`
- `src/VoiceStudio.App/ViewModels/LibraryViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/LibraryView.xaml`, `LibraryView.xaml.cs`
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs`
- `src/VoiceStudio.App.Tests/ViewModels/RecordingViewModelSeamTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/LibraryViewModelSeamTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/WorkflowCoherenceAdvancedTests.cs`
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — `LibraryView_Menu_AddToTimeline`

## 4) Honest limits

- **In proof:** Repo build, MSTest, CI pytest, Quick verify, and `run_verification.py` with **completion_guard** — not a full live WinUI manual certification of every operator gesture.
- **Still Open:** **GAP-045** / **GAP-047** — core3 Library DnD closed under **GAP-032** (2026-04-02); full “all panels” sweep remains tracker-deferred — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).

## 5) Closure

**GOV-VOICESTUDIO-GAP027-RECORDING-LIBRARY-TIMELINE-01:** **Closed** 2026-04-02 with proof-backed acceptance per execution row.

**Next:** See [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md) for the next **Open** hero-path or **GAP-045** bounded slice.
