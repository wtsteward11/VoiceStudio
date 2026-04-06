# GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-RANGE-APPLY-PARITY-01 — Range / multi-segment filler cleanup parity (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-06) — bounded slice; product **GAP-047** remains **Open**.
- **Product scope:** **GAP-047** — **Open** overall; **GAP-045** — **Open**; this lane proves **contiguous range** filler cleanup + Apply matches single-segment authority and post-apply coherence contracts.
- **Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md) **Closed**; [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) **Closed**; [GOV_VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_01_EXECUTION_ROW.md) **Closed** (range apply mechanics).
- **Closure:** [VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_LANE_CLOSURE_2026-04-06.md)

## 0.1 Lane allowlist and tree hygiene (pre-code)

**In scope for this lane’s commit (explicit pathspec only):**

- `docs/design/GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_01_EXECUTION_ROW.md`
- `docs/reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_LANE_CLOSURE_2026-04-06.md`
- `docs/design/PROFESSIONAL_GAP_TRACKER.md` (GAP-047 line only, if touched)
- `docs/governance/CANONICAL_REGISTRY.md` (rows for this lane only, if touched)
- `.cursor/STATE.md` (ACTIVE WINDOW / proof index, if touched; `git add -f` if needed)
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelInlineEditTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGap045CrossConsumerTests.cs`
- `src/VoiceStudio.App.Tests/ViewModels/TranscribeViewModelSeamTests.cs`
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs` / `TimelineViewModel.cs` **only if** tests prove a gap

**Explicitly excluded from this lane (do not stage):** unrelated modified/untracked files (e.g. GAP-045 design rows, startup docs, `BackendProcessManager`, `TranscriptionExportFormatter`, view code-behind, `scripts/run_verification.py`, `tools/scripts/`, audit markdown) until a separate bounded changeset.

## 1. Problem statement

Single-segment filler cleanup + Apply and post-apply Timeline coherence are proven. **Range / multi-segment** flows share the same `ApplyEditedSegmentAsync` chokepoint but are the usual place for **duplicate events**, **stale overlay text**, and **draft-only leakage**. This lane locks parity: one successful range Apply → one coherence signal → one ownership-gated quiet refetch; failure/cancel/draft-only → none.

## 2. Frozen architecture decisions

1. Range Apply remains under the same **Apply** authority as single-segment (`ApplyEditedSegmentAsync` → `RegenerateSegmentAudioAsync` with `requestTimelineSubtitleCoherence: true` on success path only).
2. **Same** post-apply event contract as single-segment: `NavigateToEvent` with `action = coherentReloadAfterSegmentApply`, parameters `transcriptionId`, `projectId`. No range-specific action string.
3. **Exactly one** `coherentReloadAfterSegmentApply` publication per successful operator Apply (including range); **zero** on failure or cancel or draft-only range cleanup.
4. Timeline handler remains **fail-closed** (loaded subtitle id + selected project must match).
5. No new backend routes; no `TranscriptSegmentRegenerationCoordinator` protocol expansion for filler/range.

## 3. Acceptance contract (all required)

- [x] Successful filler-cleanup **range Apply** publishes exactly one `coherentReloadAfterSegmentApply`.
- [x] Failed range Apply publishes none.
- [x] Cancel after range draft cleanup publishes none.
- [x] Range Apply updates Timeline overlay to authoritative merged/range backend shape when ownership matches (no stale pre-apply overlay text after one quiet refetch).
- [x] One successful post-apply signal → **one** additional `GetTranscriptionAsync` on Timeline (no duplicate quiet refetch from a single event).
- [x] Rehydrate after successful range-style authoritative list response replaces stale multi-segment local row (`TranscribeViewModelSeamTests`).
- [x] Draft-only range filler cleanup does not publish coherence and does not mutate committed segment text (`RangeApply_DoesNotLeakDraftOnlyStateAcrossConsumers`).
- [x] Parity test names + closure matrix + governance sync.

## 4. Hard OUT

- New filler catalog or NLP work; batch transcript-wide cleanup.
- Startup/cold-launch work; broad Timeline architecture beyond existing subtitle coherence handler.
- Coordinator rewrite or new transport surface unless a hard defect proves unavoidable.

## 5. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted filters: `TranscribeViewModelInlineEditTests`, `TimelineViewModelGap045CrossConsumerTests`, `TranscribeViewModelSeamTests` (range parity cases)
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage: `UI Self-Test`, `Icon-Launch Smoke`, `Failure-Path Smoke`, `Runtime-Missing Failure Smoke`

## 6. Rollback

Revert tests, execution row, closure report, and governance deltas for this lane only; restore prior `TranscribeViewModel` / `TimelineViewModel` only if changed.

## 7. Changelog

- **2026-04-06:** Row frozen (Open) — range Apply parity contract and hygiene allowlist.
- **2026-04-06:** Lane **closed** — tests + matrix + governance sync per [VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_RANGE_APPLY_PARITY_LANE_CLOSURE_2026-04-06.md).
