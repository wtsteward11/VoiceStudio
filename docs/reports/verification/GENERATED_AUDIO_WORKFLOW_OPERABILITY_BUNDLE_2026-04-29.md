# Generated-audio workflow operability bundle (2026-04-29)

## Scope

Voice Synthesis panel only: generated audio → library → timeline UX (status, evidence, failures, persistence). No backend timeline durability contract changes. No GAP-008 / no new `MainWindow*ShellBridge` / no RHVoice / no `ENGINE_PARITY_MATRIX.md`.

## Files changed

| Area | Path |
|------|------|
| ViewModel | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` |
| View | `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` |
| Automation constant | `src/VoiceStudio.App/Constants/AutomationIds.cs` |
| Tests | `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` |
| Registry | `docs/developer/AUTOMATION_ID_REGISTRY.md` |
| UI fixture | `tests/ui/fixtures/automation_ids.py` |

## User-facing behavior

- **Summary:** `SynthesisResultSummary` now includes non-empty library/timeline status lines and a compact **Track / clip** line when placement ids are known.
- **Timeline success:** Status text includes placement basis plus track/clip short ids and start seconds when the service returns all three.
- **Copy evidence:** **Copy evidence** button runs `CopyWorkflowEvidenceCommand` — copies a short multi-line block (audio id, reference, library line with save kind + asset id when saved, timeline line with track/clip/placement when added). Uses WinUI `Clipboard` (desktop session).
- **Failures:** Library save failures map known messages (e.g. missing audio id, empty upload) to short actionable strings; timeline **Unavailable** / **PlacementUnavailable** / **Failed** use distinct `FormatTimelineFailureStatus` copy.
- **Stale state:** `ResetLastSynthesisOutput` and post-success synthesis path clear `LastTimeline*` / `LastLibrarySaveKind`; timeline add failures clear timeline placement fields; new synthesis clears prior timeline placement on the active result row.
- **Recent results:** Rows persist `LibraryAssetId`, `LibrarySaveKind`, `TimelineClipId`, `TimelineTrackId`, `TimelinePlacementStartSeconds`; restore flows copy library + timeline evidence back onto the VM.

## Tests added / updated

- `TimelineOutput_Success_CallsServiceAndMarksAdded` — asserts `LastTimelineClipId`, `LastTimelineTrackId`, `LastTimelinePlacementStartSeconds`.
- `TimelineOutput_NewSynthesisClears_ClipAndTrackAndPlacement`
- `TimelineOutput_RestoreRecentResult_RestoresClipAndTrackId`
- `TimelineOutput_PlacementUnavailable_SurfacesActionableMessage`
- `TimelineOutput_PlacementUnavailable_DoesNotMarkAdded` — assertion updated to **placement** keyword.
- `WorkflowEvidence_CanCopyWorkflowEvidence_FalseBeforeResult` / `TrueAfterResult`
- `WorkflowEvidence_SummaryReflectsLibraryAndTimelineAfterCommands` (avoids headless `Clipboard.SetContent`).

**Filter used:** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesisViewModelTests"` — **159 passed**.

## Verification artifacts

| Command | Result |
|---------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **Succeeded** (pre-existing nullable warnings in other projects). |
| `python scripts/run_verification.py` | **Overall PASS** — `.buildlogs/verification/last_run.json` |
| `.\scripts\verify.ps1 -Quick` | **PASS** — report `artifacts/verify/20260429_025341/verification_report.md` |

## Non-claims

- **Not** runtime FULL PASS for end-to-end generated-audio operator workflow.
- **Not** operator WinUI proof; this is a **product operability** bundle only.
- **Not** GAP-008, **not** Slice 46, **not** new `MainWindow*ShellBridge`.
- **Not** RHVoice.
- **Not** `ENGINE_PARITY_MATRIX.md`.
