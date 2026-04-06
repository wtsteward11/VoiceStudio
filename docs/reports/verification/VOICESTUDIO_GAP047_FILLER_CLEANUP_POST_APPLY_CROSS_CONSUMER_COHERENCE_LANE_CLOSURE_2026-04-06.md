# VoiceStudio GAP-047 Post-Apply Cross-Consumer Coherence Lane Closure — 2026-04-06

**Lane:** GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-POST-APPLY-CROSS-CONSUMER-COHERENCE-01 — After **successful Apply** (inline edit / filler-cleaned draft), Timeline subtitle overlay **quiet-refetches** from backend when **loaded transcription id** and **active project** match; draft/failure/cancel paths do **not** publish the coherence event.  
**Execution row:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md) (**Closed**); [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) (**Closed**)  
**Product:** **GAP-047** and **GAP-045** remain **Open** (broader product scope unchanged).

## 1) Scope summary

- **Transcribe:** `RegenerateSegmentAudioAsync` gains `requestTimelineSubtitleCoherence` (default **false**). **Apply** and **retry-apply** pass **true**; toolbar regen path unchanged (default **false**).
- **Publish:** `PublishTimelineCoherenceAfterSegmentApplySuccess` → `NavigateToEvent` with `action = coherentReloadAfterSegmentApply`, `transcriptionId`, `projectId` (non-empty project required).
- **Timeline:** `OnNavigateToTimeline` handles `coherentReloadAfterSegmentApply` — `LoadTranscriptSegmentsAsync(..., quietNotifications: true)` only when `LoadedSubtitleTranscriptionId` and `SelectedProject.Id` match payload; otherwise **no-op** (fail-closed).
- **Proof tests:** `TranscribeViewModelInlineEditTests` (apply success/failure/cancel coherence counts); `TimelineViewModelGap045CrossConsumerTests` (match/mismatch/no-overlay); `TranscribeViewModelSeamTests.RehydrateAfterAppliedCleanup_UsesAuthoritativeText_NotDraftState`.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| `dotnet test` — filters for new GAP-047 tests | PASS — **7** tests |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3108** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed (**2** deselected) |
| `python scripts/validate_xaml_resources.py` | PASS — 0 missing VSQ.\* references |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_134939/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260406-135457** at first post-matrix run; **20260406-135727** after final governance sync) |
| OnlyStage `UI Self-Test` | PASS — `artifacts/verify/20260406_135509/` |
| OnlyStage `Icon-Launch Smoke` | PASS — `artifacts/verify/20260406_135520/` |
| OnlyStage `Failure-Path Smoke` | PASS — `artifacts/verify/20260406_135529/` |
| OnlyStage `Runtime-Missing Failure Smoke` | PASS — `artifacts/verify/20260406_135547/` |

## 3) Proof artifacts (code + docs)

- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs` — `requestTimelineSubtitleCoherence`, `PublishTimelineCoherenceAfterSegmentApplySuccess`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` — `coherentReloadAfterSegmentApply`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs`
- `docs/design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md`

## 4) Honest limits

- Lane is **Transcribe ↔ Timeline subtitle overlay** only; no new backend routes or coordinator protocol changes.
- **Regenerate-only** (non-apply) path does not publish post-apply coherence by design.

## 5) Closure

**GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-POST-APPLY-CROSS-CONSUMER-COHERENCE-01:** **Closed** 2026-04-06 with proof-backed acceptance per execution row.

**GAP-047 / GAP-045:** product rows **Open** until future lanes close broader tracker scope.
