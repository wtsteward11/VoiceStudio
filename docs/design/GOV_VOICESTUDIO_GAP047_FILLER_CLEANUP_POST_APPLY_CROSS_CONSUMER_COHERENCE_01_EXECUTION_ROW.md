# GOV-VOICESTUDIO-GAP047-FILLER-CLEANUP-POST-APPLY-CROSS-CONSUMER-COHERENCE-01 — Post-apply transcript coherence (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-06) — bounded slice; product **GAP-047** remains **Open**.
- **Product scope:** **GAP-047** — **Open** overall; **GAP-045** — **Open**; this lane extends **cross-consumer** coherence to **successful Apply** (including filler-cleanup Apply), without new filler NLP/catalog work.
- **Depends on:** [GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP047_FILLER_CLEANUP_APPLY_AUTHORITY_01_EXECUTION_ROW.md) **Closed**; [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) **Closed** (rehydrate → Timeline pattern).
- **Closure:** [VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_LANE_CLOSURE_2026-04-06.md)

## 1. Problem statement

After **successful Apply** (inline edit / filler-cleaned draft → coordinator → persisted transcript), the Timeline subtitle overlay can retain **stale segment text** until the operator repeats **Send to Timeline** or another full load. **Draft-only** filler cleanup, preview/toggles, **failure**, and **cancel** must not mutate Timeline (or other) consumers. Rehydrate continues to resolve from **backend list** authority; this lane adds a **bounded post-apply** signal so Timeline can **quiet-refetch** when overlay **ownership** (project + loaded transcription id) still matches.

## 2. Frozen architecture decisions

1. **Apply remains sole mutation authority** (prior lane); **successful Apply** (including retry of an apply job) is the **only** trigger that publishes the post-apply coherence event from Transcribe.
2. **Coupling surface:** `NavigateToEvent` with `action = coherentReloadAfterSegmentApply`, parameters `transcriptionId`, `projectId` (both required non-empty from publisher). No new backend routes; coordinator contracts unchanged.
3. **Timeline activation:** Timeline **quiet-refetches** via `LoadTranscriptSegmentsAsync(..., quietNotifications: true)` **only if**:
   - `LoadedSubtitleTranscriptionId` is non-null and equals `transcriptionId`, and
   - `SelectedProject` is non-null and its `Id` equals `projectId`.
4. **Fail-closed:** If the loaded subtitle id or active project **does not** match the payload, Timeline **does not** refetch and **does not** clear the overlay (no speculative cross-project writes).
5. **No overlay:** If no subtitle overlay is loaded (`LoadedSubtitleTranscriptionId` null), post-apply event is a **no-op** on Timeline (unlike cold `coherentReloadAfterRehydrate` which may load when Transcribe selects a row).
6. **Regenerate-only toolbar path:** `RegenerateSegmentAudioAsync` without **apply** intent does **not** publish this event (`requestTimelineSubtitleCoherence` default **false**).

## 3. Acceptance contract (all required)

- [x] After successful filler cleanup **Apply**, Transcribe publishes **exactly one** `coherentReloadAfterSegmentApply` for the active transcription + project (test-backed).
- [x] Apply **failure** does not publish the event; cross-consumer state unchanged from that path.
- [x] **Cancel** after draft filler cleanup does not publish the event.
- [x] Timeline **quiet-refetch** when ids align updates overlay text from backend; **mismatch** leaves prior overlay and does not issue a mismatched fetch.
- [x] **No loaded overlay** → post-apply publish does not call `GetTranscriptionAsync` on Timeline.
- [x] Seam: after list **rehydrate**, selected transcription reflects **backend list** text, not stale draft-only state (`TranscribeViewModelSeamTests`).
- [x] Closure matrix + governance sync (STATE / tracker / registry / proof index).

## 4. Hard OUT

- New filler NLP/catalog or batch transcript cleanup.
- New `/api/*` routes or coordinator protocol expansion.
- Startup/cold-launch architecture changes.
- Broad panel event bus beyond Transcribe → Timeline subtitle coherence.

## 5. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- Targeted `dotnet test` filters: `TranscribeViewModelInlineEditTests`, `TimelineViewModelGap045CrossConsumerTests`, `TranscribeViewModelSeamTests` (post-apply / rehydrate cases)
- Full `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `python scripts/validate_xaml_resources.py`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS
- Sequential OnlyStage: `UI Self-Test`, `Icon-Launch Smoke`, `Failure-Path Smoke`, `Runtime-Missing Failure Smoke`

## 6. Rollback

Revert `PublishTimelineCoherenceAfterSegmentApplySuccess`, `RegenerateSegmentAudioAsync` flag, Timeline `coherentReloadAfterSegmentApply` branch, tests, execution row, closure report, and governance deltas for this lane only.

## 7. Changelog

- **2026-04-06:** Row frozen (Open) — post-apply cross-consumer coherence contract.
- **2026-04-06:** Lane **closed** — implementation + tests + matrix + governance sync per [VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_LANE_CLOSURE_2026-04-06.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_POST_APPLY_CROSS_CONSUMER_COHERENCE_LANE_CLOSURE_2026-04-06.md).
