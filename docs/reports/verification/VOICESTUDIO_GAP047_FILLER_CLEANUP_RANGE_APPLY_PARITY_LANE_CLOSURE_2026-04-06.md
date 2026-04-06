# VoiceStudio GAP-047 Range Apply Parity Lane Closure — 2026-04-06

**Lane:** GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-RANGE-APPLY-PARITY-01 — **Contiguous range / multi-segment** filler cleanup + Apply preserves the same authority and post-apply coherence contract as single-segment Apply (one `coherentReloadAfterSegmentApply` per successful Apply; zero on failure/cancel/draft-only; Timeline quiet-refetch ownership-gated).  
**Execution row:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) (**Closed**); [GOV_VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_01_EXECUTION_ROW.md) (**Closed**)  
**Product:** **GAP-047** and **GAP-045** remain **Open**.

## 1) Scope summary

- **Runtime delta:** none — existing `ApplyEditedSegmentAsync` / `RegenerateSegmentAudioAsync` / `coherentReloadAfterSegmentApply` path already covers range; this lane adds **proof tests** only.
- **Transcribe proofs:** `TranscribeViewModelInlineEditTests` — range filler cleanup success (one event), failure (none), cancel after range draft (none), draft-only no leakage (committed segment text + zero events).
- **Timeline proofs:** `TimelineViewModelGap045CrossConsumerTests` — multi-segment overlay quiet-refetch updates merged authoritative text; mismatch fail-closed with exactly one `GetTranscriptionAsync` total.
- **Seam proof:** `TranscribeViewModelSeamTests.RangeApply_Rehydrate_UsesAuthoritativeBackendTruth` — list reload replaces stale multi-segment local row with backend merged shape.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (via test compile; 0 errors) |
| `dotnet test` — filter `RangeApply_`, `CancelAfterRangeDraftCleanup` | PASS — **7** tests |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3115** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed (**2** deselected) |
| `python scripts/validate_xaml_resources.py` | PASS — 0 missing VSQ.\* references |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_142017/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260406-142846** after final governance sync) |
| OnlyStage `UI Self-Test` | PASS — `artifacts/verify/20260406_142550/` |
| OnlyStage `Icon-Launch Smoke` | PASS — `artifacts/verify/20260406_142557/` |
| OnlyStage `Failure-Path Smoke` | PASS — `artifacts/verify/20260406_142606/` |
| OnlyStage `Runtime-Missing Failure Smoke` | PASS — `artifacts/verify/20260406_142624/` |

## 3) Proof artifacts (code + docs)

- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs` — range parity coherence + draft-only invariants
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs` — range overlay refetch + mismatch
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs` — range rehydrate authority
- `docs/design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md`

## 4) Honest limits

- No coordinator or backend route changes; no new navigate `action` string.
- UI Self-Test / Icon-Launch stages completed in **stub/fast** mode on this runner (harness reported PASSED with minimal wall time).

## 5) Closure

**GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-RANGE-APPLY-PARITY-01:** **Closed** 2026-04-06 with proof-backed acceptance per execution row.

**GAP-047 / GAP-045:** product rows **Open** until future lanes close broader tracker scope.
