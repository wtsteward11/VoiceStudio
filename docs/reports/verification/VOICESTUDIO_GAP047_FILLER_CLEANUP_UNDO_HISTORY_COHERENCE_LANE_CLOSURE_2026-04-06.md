# VoiceStudio GAP-047 Undo / History Coherence Lane Closure — 2026-04-06

**Lane:** GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-UNDO-HISTORY-COHERENCE-01 — After filler-cleanup Apply, **Undo/Redo** restores **authoritative transcript** snapshots (not only clip audio + linkage) and publishes **one** `coherentReloadAfterSegmentApply` per Undo/Redo when transcript payloads are active (same ownership-gated Timeline contract as Apply).  
**Execution row:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md) (**Closed**)  
**Product:** **GAP-047** and **GAP-045** remain **Open**.

## 1) Scope summary

- **Runtime:** `TranscriptSegmentRegenerationCoordinator` captures pre/post `TranscriptTextUndoPayload` when replacement text persists successfully; `TranscriptClipAudioReplaceUndoAction` calls `UpdateTranscriptionTextAsync`, syncs the live `TranscriptionResponse` when provided, and publishes `NavigateToEvent` with `coherentReloadAfterSegmentApply`.
- **New type:** `TranscriptTextUndoPayload.cs` — segment + text snapshot helper.
- **Transcribe proofs:** `TranscribeViewModelInlineEditTests` — single/range undo text restore, undo coherence count = 1, draft/cancel history + undo stack hygiene, Apply→Undo→list rehydrate.
- **Timeline proofs:** `TimelineViewModelGap045CrossConsumerTests` — simulated apply then undo refetch chain; two coherence events → three `GetTranscriptionAsync` calls (no duplicate suppression regression).
- **Seam:** `TranscribeViewModelSeamTests.ApplyUndoRehydrate_UsesAuthoritativeBackendTruth` — list authority after local drift (paired with inline coordinator integration).
- **Service tests:** `TranscriptSegmentRegenerationCoordinatorTests.TryExecuteAsync_WithReplacementText_UndoRestoresTranscriptSnapshotAndCoherenceNavigate`; `TranscriptClipAudioReplaceUndoActionTests.Undo_WithTranscriptPayload_CallsTranscriptionClient_PublishesCoherenceNavigate`.
- **Harness:** `TranscribeViewModelInlineEditTests.InstallHarness` / `InstallRetryHarness` register `UndoRedoService`, coordinator `ITranscriptionClient` persistence mock, `IEventAggregator`, and `UpdateClipAsync` for **audio-old** restore.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (via test rebuild; 0 errors) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3125** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed (**2** deselected) |
| `python scripts/validate_xaml_resources.py` | PASS — 0 missing VSQ.\* references |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_151745/verification_report.md` (**completion_guard** skipped in Quick) |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260406-152616**) |
| OnlyStage `UI Self-Test` | PASS — `artifacts/verify/20260406_152328/` (stub/fast) |
| OnlyStage `Icon-Launch Smoke` | PASS — `artifacts/verify/20260406_152335/` |
| OnlyStage `Failure-Path Smoke` | PASS — `artifacts/verify/20260406_152343/` |
| OnlyStage `Runtime-Missing Failure Smoke` | PASS — `artifacts/verify/20260406_152402/` |

## 3) Proof artifacts (code + docs)

- `src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs`
- `src/VoiceStudio.App/Services/UndoableActions/TranscriptClipAudioReplaceUndoAction.cs`
- `src/VoiceStudio.App/Services/UndoableActions/TranscriptTextUndoPayload.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptSegmentRegenerationCoordinatorTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptClipAudioReplaceUndoActionTests.cs`
- `docs/design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md`

## 4) Honest limits

- Transcript undo payloads are registered only when **persistence returns success** (`persistenceMessage == null`); persist-failure-after-clip-apply remains a known sharp edge (pre-existing coordinator behavior).
- UI Self-Test / Icon-Launch stages ran in **stub/fast** mode on this runner (`Real UI: False` in harness output).

## 5) Closure

**GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-UNDO-HISTORY-COHERENCE-01:** **Closed** 2026-04-06 with proof-backed acceptance per execution row.

**GAP-047 / GAP-045:** product rows **Open** until future lanes close broader tracker scope.
