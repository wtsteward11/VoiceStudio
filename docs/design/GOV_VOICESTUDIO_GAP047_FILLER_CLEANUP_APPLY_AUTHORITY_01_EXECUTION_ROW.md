# GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-APPLY-AUTHORITY-01 — Transcribe filler cleanup apply authority (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-06) — bounded slice; product **GAP-047** remains **Open**.
- **Product scope:** **GAP-047** — **Open** overall; **GAP-045** — **Open**; this lane hardens **authority** only (no new filler catalog, no new detection).
- **Depends on:** [GOV_VOICESTUDIO_TRANSCRIBE_FILLER_CLEANUP_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIBE_FILLER_CLEANUP_01_EXECUTION_ROW.md) **Closed**; [GOV_VOICESTUDIO_FILLER_CLEANUP_REVIEW_CONTROLS_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_FILLER_CLEANUP_REVIEW_CONTROLS_01_EXECUTION_ROW.md) **Closed**.
- **Closure:** [VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_LANE_CLOSURE_2026-04-06.md)

## 1. Problem statement

Review controls and **Remove fillers** intentionally mutate **draft** only. **Canonical** transcript text, clip regen, and backend transcript persistence must occur **only** through the existing explicit **Apply** path (`ApplyEditedSegmentAsync` → intent → `TranscriptSegmentRegenerationCoordinator`). Without documented invariants and seam tests, a future refactor could accidentally route filler cleanup into persistence or regen, causing silent authoritative mutation or double-apply drift.

## 2. Frozen architecture decisions

1. **Draft authority:** `TryRemoveFillersFromEditingDraft`, toggle changes, and `FillerRemovalPreviewText` affect **`EditingSegmentDraftText`** (and session history for draft cleanup) only.
2. **Apply authority:** `ApplyEditedSegmentAsync` is the **sole** VM entry that starts segment regeneration for inline edit (replacement text from current draft, including post-filler draft).
3. **View wiring:** Flyout **Remove fillers** button calls only `TryRemoveFillersFromEditingDraft`; **Apply** calls only `ApplyEditedSegmentAsync` (see `TranscribeView.xaml.cs`).
4. **Cancel / flyout close:** `CancelSegmentEdit` clears draft + filler review state; **committed** segment text on `SelectedTranscription` must remain unchanged until a successful Apply.
5. **No new backend routes** for this lane; validity and persistence remain existing transcription/regen contracts.

## 3. Acceptance contract (all required)

- [x] Removing fillers from the draft does **not** invoke `ITranscriptRegenerationClient.StartRegenerateSegmentAsync` (or equivalent apply/regen entry).
- [x] Changing filler toggles / relying on preview alone does **not** invoke regen.
- [x] After draft filler removal, **committed** segment text on `SelectedTranscription` remains the pre-apply original until Apply succeeds.
- [x] Cancel after filler review/removal leaves canonical segment text unchanged.
- [x] `RemoveFillersFromDraft_ThenApply_SendsCleanedReplacementText` class of behavior preserved: Apply sends **one** regen with cleaned replacement text when operator applies.
- [x] Seam tests in `TranscribeViewModelInlineEditTests` (and references in execution row).
- [x] Closure matrix + governance sync (STATE / tracker / registry / proof index).

## 4. Hard OUT

- NLP/ML filler detection, new catalogs, or batch transcript-wide cleanup.
- New `/api/*` routes or new job types for filler-only apply.
- Timeline architecture refactor or startup/cold-launch scope.
- Persisted per-user filler prefs (deferred per review-controls row).

## 5. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted `dotnet test` — filter `TranscribeViewModelInlineEdit` + filler/apply
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage: `UI Self-Test`, `Icon-Launch Smoke`, `Failure-Path Smoke`, `Runtime-Missing Failure Smoke`

## 6. Rollback

Revert lane-specific comments/tests, execution row, closure report, and governance deltas for this lane only.

## 7. Changelog

- **2026-04-06:** Row frozen + lane **closed** — apply authority invariants, `TranscribeViewModel` contract XML docs, `TranscribeViewModelInlineEditTests` seam proofs; matrix + governance sync per [VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_LANE_CLOSURE_2026-04-06.md).
