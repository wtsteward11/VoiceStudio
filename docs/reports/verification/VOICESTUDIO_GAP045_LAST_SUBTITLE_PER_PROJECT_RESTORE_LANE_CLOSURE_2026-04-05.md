# VOICESTUDIO — GAP-045 last subtitle per-project restore — Lane closure (2026-04-05)

**Execution row:** [GOV_VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_01_EXECUTION_ROW.md)  
**Product tracker:** [GAP-045](../../design/PROFESSIONAL_GAP_TRACKER.md) — **Open** (bounded lane closed only)

## 1. Goal

Persist and restore the Timeline subtitle overlay selection per project so that reopen/rehydrate re-selects the same backend-authoritative transcription when valid, while preserving project-switch isolation.

## 2. Verification matrix (evidence)

| Step | Command / artifact | Result |
|------|-------------------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| Targeted App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~LastSubtitleRestore|FullyQualifiedName~TimelineViewModelGap045"` | **11** passed |
| App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3093** passed / **274** skipped |
| Py CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS |
| Quick | `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260405_211745/` |
| Rolling | `python scripts/run_verification.py` | PASS → `.buildlogs/verification/last_run.json` **20260405-212821** (**completion_guard** PASS) |
| UI Self-Test | `.\scripts\verify.ps1 -OnlyStage "UI Self-Test" -SkipBuild` | PASS → `artifacts/verify/20260405_212256/` |
| Icon-Launch Smoke | `.\scripts\verify.ps1 -OnlyStage "Icon-Launch Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_212308/` |
| Failure-Path Smoke | `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_212322/` |
| Runtime-Missing Failure Smoke | `.\scripts\verify.ps1 -OnlyStage "Runtime-Missing Failure Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_212344/` |

**Note:** Run OnlyStage smokes sequentially to avoid harness artifact contention.

## 3. Code touchpoints

- `src/VoiceStudio.Core/Models/Project.cs` — `LastSubtitleTranscriptionId` persisted field (null omitted in JSON)
- `src/VoiceStudio.Core/Services/IProjectRepository.cs` — persist/read contract for last subtitle id
- `src/VoiceStudio.App/Services/JsonProjectRepository.cs` — local JSON storage for `LastSubtitleTranscriptionId`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` — persist on `LoadTranscriptSegmentsAsync`; explicit-clear-only persisted wipe; cold-session coherent reload load path
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs` — rehydrate reads stored id when in-memory selection is absent; restore-specific operator diagnostic
- `src/VoiceStudio.App/Views/Panels/TimelineView.xaml.cs` — inject `IProjectRepository`
- `src/VoiceStudio.App/Views/Panels/TranscribeView.xaml.cs` — inject `IProjectRepository`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs` — write/no-write persistence seam tests
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelLastSubtitleRestoreTests.cs` — restore/no-override/fail-closed/null-safe seam tests

## 4. Startup / cold-launch posture

Startup harness remains green in this lane’s matrix. Operator manual cold-launch checklist remains represented in [VOICESTUDIO_COLD_LAUNCH_FIVE_RUN_EVIDENCE_2026-04-05.md](VOICESTUDIO_COLD_LAUNCH_FIVE_RUN_EVIDENCE_2026-04-05.md) with explicit manual table section.

## 5. Tracker

**GAP-045** remains **Open** for broader product scope; this bounded restore lane is **Closed**.
