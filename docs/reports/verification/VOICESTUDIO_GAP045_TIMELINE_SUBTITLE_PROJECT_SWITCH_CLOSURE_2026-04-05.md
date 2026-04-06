# VOICESTUDIO — GAP-045 timeline subtitle project-switch coherence — Lane closure (2026-04-05)

**Execution row:** [GOV_VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_COHERENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_COHERENCE_01_EXECUTION_ROW.md)  
**Product tracker:** [GAP-045](../../design/PROFESSIONAL_GAP_TRACKER.md) — **Open** (bounded lane closed only)

## 1. Goal

Prevent the Timeline **subtitle overlay** from showing another project’s backend transcript after **SelectedProject** changes or when the shell clears project selection.

## 2. Verification matrix (evidence)

| Step | Command / artifact | Result |
|------|-------------------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (via Quick harness) |
| App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3087** passed / **274** skipped |
| Py CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS |
| Quick | `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260405_190541/` |
| Rolling | `python scripts/run_verification.py` | PASS → `.buildlogs/verification/last_run.json` **20260405-191135** (**completion_guard** PASS) |
| UI Self-Test | `.\scripts\verify.ps1 -OnlyStage "UI Self-Test" -SkipBuild` | PASS → `artifacts/verify/20260405_191157/` |
| Icon-Launch Smoke | `.\scripts\verify.ps1 -OnlyStage "Icon-Launch Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_191214/` |
| Failure-Path Smoke | `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_191246/` |
| Runtime-Missing Failure Smoke | `.\scripts\verify.ps1 -OnlyStage "Runtime-Missing Failure Smoke" -SkipBuild` | PASS → `artifacts/verify/20260405_191312/` |

**Note:** Run OnlyStage smokes **sequentially** if parallel harness invocations contend for artifacts (observed transient failure when overlapping).

## 3. Code touchpoints

- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` — `_subtitleOverlayOwnerProjectId`, `OnSelectedProjectChanged`, `ClearTranscript`, `LoadTranscriptSegmentsAsync`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs` — project switch + null-selection seam tests

## 4. Cold-launch / startup posture

Five-run evidence table: [VOICESTUDIO_COLD_LAUNCH_FIVE_RUN_EVIDENCE_2026-04-05.md](VOICESTUDIO_COLD_LAUNCH_FIVE_RUN_EVIDENCE_2026-04-05.md) (prior wave) + this matrix’s Quick + four OnlyStages above.

## 5. Tracker

**GAP-045** remains **Open** for broader transcript / edit-apply scope; this lane is **Closed**.
