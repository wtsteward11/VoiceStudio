# GOV-VOICESTUDIO-GAP047-PERSIST-FAILURE-AFTER-CLIP-APPLY-RECOVERY-01 — Atomic recovery when transcript persistence fails after clip apply (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-06) — bounded slice; product **GAP-047** remains **Open** until full product closure criteria are met.
- **Product scope:** **GAP-047** — when `UpdateClipAsync` succeeds but `UpdateTranscriptionTextAsync` fails, the coordinator must not leave a half-committed operator state.
- **Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md) **Closed**.
- **Closure:** [VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_LANE_CLOSURE_2026-04-06.md)

## 0.1 Lane allowlist and tree hygiene (pre-code)

**In scope for this lane’s commit (explicit pathspec only):**

- `docs/design/GOV_VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_01_EXECUTION_ROW.md`
- `docs/reports/verification/VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_LANE_CLOSURE_*.md`
- `docs/design/PROFESSIONAL_GAP_TRACKER.md` (GAP-047 line only, if touched)
- `docs/governance/CANONICAL_REGISTRY.md` (rows for this lane only, if touched)
- `.cursor/STATE.md` (ACTIVE WINDOW / proof index, if touched; `git add -f` if needed)
- `src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptSegmentRegenerationCoordinatorTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs`

**Explicitly excluded:** unrelated modified files (startup, `BackendProcessManager`, other GAP rows, `scripts/run_verification.py`, etc.) until a separate bounded changeset.

## 1. Problem statement

After successful clip/audio `UpdateClipAsync`, transcript persistence may throw. Today the coordinator still mutates in-memory clip, removes linkage, publishes success-path events, and registers a partial undo action. That is **silent corruption risk**. This lane implements **Option A — atomic failure**: compensate by restoring the pre-apply clip on the backend, skip mutations/events/undo, return a deterministic error.

## 2. Frozen architecture decision

**Option A (atomic failure):** If transcript persistence fails after clip apply, **rollback clip audio** via `UpdateClipAsync` with saved `prevAudioId` / `prevUrl` / `prevDur` (best-effort). Do not publish `ClipAudioArtifactReplacedEvent` / `TranscriptTruthStateChangedEvent` for success semantics; do not register undo; report `apply_failed` job progress. If compensation also fails, append to the operator message without masking the original persistence error.

## 3. Acceptance contract (all required)

- [x] Transcript persistence failure after clip apply triggers clip compensation and returns a message containing `transcript persistence failed` (or equivalent user-facing text from coordinator).
- [x] No `RemoveLinksByClipId`, in-memory clip mutation, success events, or undo registration on that path.
- [x] `UpdateClipAsync` invoked twice when compensation succeeds (forward new audio, then restore old).
- [x] No `coherentReloadAfterSegmentApply` from Transcribe VM on persist failure (existing `err != null` gate preserved).
- [x] Undo stack unchanged after failed apply (no registered action).
- [x] Single-segment and range (`rangeEndInclusiveIndex`) share the same failure contract.
- [x] Double failure (persist + compensation) surfaces both failures in the returned message.
- [x] Closure matrix + governance sync.

## 4. Hard OUT

- New backend routes; changes to `TranscriptClipAudioReplaceUndoAction` or `TimelineViewModel` coherence handler (unless tests prove defect).
- Umbrella “finish GAP-047”; GAP-045 unrelated work; startup-only fixes mixed into this lane.

## 5. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted filters: `TranscriptSegmentRegenerationCoordinatorTests`, `TranscribeViewModelInlineEditTests`, `TimelineViewModelGap045CrossConsumerTests`, `TranscribeViewModelSeamTests`
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage smokes

## 6. Rollback

Revert lane-scoped files only; re-open row if rollback after closure; re-run Quick verify and `run_verification.py`.

## 7. Changelog

- **2026-04-06:** Lane **closed** — atomic persist-failure recovery contract frozen; `TranscriptSegmentRegenerationCoordinator` compensation path; tests + closure matrix + governance sync per [closure report](../reports/verification/VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_LANE_CLOSURE_2026-04-06.md).
