# GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-UNDO-HISTORY-COHERENCE-01 — Undo / history coherence after filler cleanup apply (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-06) — bounded slice; product **GAP-047** remains **Open** until full product closure criteria are met.
- **Product scope:** **GAP-047** — **Open** overall; this lane proves **Apply → Undo → (optional rehydrate)** preserves authoritative transcript truth and cross-consumer coherence for single-segment and range filler-cleanup flows.
- **Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md) **Closed**; [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) **Closed**.
- **Closure:** [VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_LANE_CLOSURE_2026-04-06.md)

## 0.1 Lane allowlist and tree hygiene (pre-code)

**In scope for this lane’s commit (explicit pathspec only):**

- `docs/design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_01_EXECUTION_ROW.md`
- `docs/reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_UNDO_HISTORY_COHERENCE_LANE_CLOSURE_*.md`
- `docs/design/PROFESSIONAL_GAP_TRACKER.md` (GAP-047 line only, if touched)
- `docs/governance/CANONICAL_REGISTRY.md` (rows for this lane only, if touched)
- `.cursor/STATE.md` (ACTIVE WINDOW / proof index, if touched; `git add -f` if needed)
- `src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs`
- `src/VoiceStudio.App/Services/UndoableActions/TranscriptClipAudioReplaceUndoAction.cs`
- `src/VoiceStudio.App/Services/UndoableActions/TranscriptTextUndoPayload.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptSegmentRegenerationCoordinatorTests.cs`
- `src/VoiceStudio.App.Tests/Services/TranscriptClipAudioReplaceUndoActionTests.cs`

**Explicitly excluded from this lane (do not stage):** unrelated modified/untracked files (e.g. GAP-045-only rows, startup docs, `BackendProcessManager`, `TranscriptionExportFormatter`, `scripts/run_verification.py`, `tools/scripts/`, audit markdown) until a separate bounded changeset.

## 1. Problem statement

Post-apply cross-consumer coherence is proven. **Undo** after filler-cleanup Apply currently restores clip audio and linkage but **not** persisted transcript text, so canonical truth and Timeline overlay can diverge. This lane locks **snapshot-based** transcript restore on Undo/Redo and **one** ownership-gated coherence signal per Undo/Redo, matching the apply-path contract.

## 2. Frozen architecture decisions

1. Undo remains an authoritative post-apply reversal; it restores **pre-apply** transcript snapshots from the coordinator boundary, not UI draft buffers.
2. Redo re-applies **post-apply** transcript snapshots and clip semantics consistent with the registered undo action.
3. Cross-consumer refresh after Undo/Redo reuses **`NavigateToEvent`** with `action = coherentReloadAfterSegmentApply` (`transcriptionId`, `projectId`). No new side-channel contract.
4. **Exactly one** coherence publication per Undo and per Redo when transcript undo payload is active (same handler as apply).
5. History semantics: draft-only filler cleanup stays non-committed; cancel/failure paths do not register coordinator undo for successful apply.
6. No new backend routes unless tests prove representational insufficiency (not expected).

## 3. Acceptance contract (all required)

- [x] Successful single-segment filler cleanup Apply → Undo restores canonical segment text in-memory and via `UpdateTranscriptionTextAsync` (pre snapshot).
- [x] Successful range Apply → Undo restores full range segment texts.
- [x] Undo/Redo each publish **one** `coherentReloadAfterSegmentApply` when transcript payload is present; Timeline performs **one** additional quiet refetch per event when ownership matches.
- [x] Draft-only filler cleanup does not create a committed apply history entry (`SingleSegmentApply` / `MultiSegmentRangeApply` with regeneration semantics).
- [x] Cancel after draft cleanup does not leave an undoable coordinator mutation on the stack.
- [x] Rehydrate after Undo reflects authoritative list/get response (`TranscribeViewModelSeamTests` + `TranscribeViewModelInlineEditTests`).
- [x] Coordinator + undo action unit tests cover transcript snapshot registration and persistence calls on Undo/Redo.
- [x] Closure matrix + governance sync (tracker, registry, STATE proof index).

## 4. Hard OUT

- Umbrella “finish GAP-047”; startup; GAP-045 unrelated work; analyzer-only cleanup.
- Coordinator rewrite beyond snapshot wiring; new transport unless defect-proven.

## 5. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted filters: `TranscribeViewModelInlineEditTests`, `TimelineViewModelGap045CrossConsumerTests`, `TranscribeViewModelSeamTests`, `TranscriptSegmentRegenerationCoordinatorTests`, `TranscriptClipAudioReplaceUndoActionTests`
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage: `UI Self-Test`, `Icon-Launch Smoke`, `Failure-Path Smoke`, `Runtime-Missing Failure Smoke`

## 6. Rollback

Revert lane-scoped files only; re-open row status if rollback after closure; re-run Quick verify and `run_verification.py`.

## 7. Changelog

- **2026-04-06:** Row frozen (Open) — undo/history coherence contract and hygiene allowlist.
- **2026-04-06:** Lane **closed** — tests + runtime snapshot undo + matrix + governance sync per closure report.
