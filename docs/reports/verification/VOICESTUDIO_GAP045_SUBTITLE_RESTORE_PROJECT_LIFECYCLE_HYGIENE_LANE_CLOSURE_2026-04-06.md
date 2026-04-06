# VOICESTUDIO — GAP-045 subtitle restore project lifecycle hygiene — Lane closure (2026-04-06)

**Execution row:** [GOV_VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP045_SUBTITLE_RESTORE_PROJECT_LIFECYCLE_HYGIENE_01_EXECUTION_ROW.md)  
**Product tracker:** [GAP-045](../../design/PROFESSIONAL_GAP_TRACKER.md) — **Open** (bounded lane closed only)  
**Git:** `16443070ca9deeab47384255bf133d740995405e` (workspace at closure authoring)

## 1. Goal

Harden **New / Open / Save As** and **project identity** transitions so `LastSubtitleTranscriptionId` and Transcribe in-memory selection do not cross-contaminate across projects; keep backend list as restore validity authority.

## 2. Verification matrix (evidence)

| Step | Command / artifact | Result |
|------|---------------------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable warnings in unrelated files) |
| Targeted | `dotnet test ... --filter "FullyQualifiedName~FileOperationsHandlerTests\|LastSubtitleRestore\|JsonProjectRepositoryTests"` | PASS (**39** tests) |
| App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3097** passed / **274** skipped |
| Py CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS (173 / 101 / 0 missing) |
| Quick | `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260406_001747/` |
| Rolling | `python scripts/run_verification.py` | PASS → `.buildlogs/verification/last_run.json` **20260406-002616** (**completion_guard** PASS) (post-governance sync reroll) |
| UI Self-Test | `.\scripts\verify.ps1 -OnlyStage "UI Self-Test" ...` | PASS → `artifacts/verify/20260406_002301/` |
| Icon-Launch Smoke | `.\scripts\verify.ps1 -OnlyStage "Icon-Launch Smoke" ...` | PASS → `artifacts/verify/20260406_002311/` |
| Failure-Path Smoke | `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke" ...` | PASS → `artifacts/verify/20260406_002319/` |
| Runtime-Missing Failure Smoke | `.\scripts\verify.ps1 -OnlyStage "Runtime-Missing Failure Smoke" ...` | PASS → `artifacts/verify/20260406_002336/` |

## 3. Code touchpoints

- `src/VoiceStudio.App/Commands/FileOperationsHandler.cs` — `CreateShellProjectForNewIdentity`; New / Save As clear `LastSubtitleTranscriptionId`; Open authoritative load documented
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs` — `OnSelectedProjectIdChanged` clears transcript list/selection before rehydrate
- `src/VoiceStudio.App.Tests/Commands/FileOperationsHandlerTests.cs` — New + Save As lifecycle tests
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelLastSubtitleRestoreTests.cs` — repository restore vs prior in-memory selection
- `src/VoiceStudio.App.Tests/Services/JsonProjectRepositoryTests.cs` — `SaveLastSubtitleTranscriptionIdAsync` round-trip + clear (`using System.Threading` for `CancellationToken`)

**Timeline:** Existing `TimelineViewModelGap045CrossConsumerTests.SelectedProjectChanged_DifferentProject_ClearsSubtitleOverlay` already covers project-switch subtitle overlay hygiene for this seam; no VM code change required in this lane.

## 4. Tracker

**GAP-045** remains **Open** for any further transcript / edit-apply scope; this lane is **Closed**.
