# GOV-VOICESTUDIO-TRANSCRIPT-TRUTH-RECONCILIATION-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_01`  
**Status:** **Closed** — 2026-03-31; bounded follow-on under product **GAP-045** (Option B transcript-truth policy).  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md) — product row remains **Open**; this lane is **Closed**.  
**Depends on:** [GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md) (**Closed**), [GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md) (**Closed**)

## Policy: Option B (frozen)

- **Explicit persisted stale state** on the timeline clip after regeneration removes linkage.
- **Operator-triggered canonical refresh** only — no auto re-transcription.
- **Fail-closed** if clip is not `StaleAfterClipRegeneration`, audio ids mismatch, or multiple stale clips share the same `audioId` in the Transcribe panel hint path.

## Frozen objective

Restore **deterministic transcript truth** after clip-audio regeneration: one in-app refresh path re-transcribes the clip’s current `AudioId`, then **`UpsertLinksForTranscription`** rebuilds `Project.ClipTranscriptLinks` for that clip; autosave/recovery sees persisted `AudioClip.TranscriptTruth`.

## Seam contracts (stale → refresh → current)

| Seam | Ownership | Behavior |
|------|-----------|----------|
| `TranscriptSegmentRegenerationCoordinator` | App | On success: remove links, apply new audio, set `TranscriptTruth = StaleAfterClipRegeneration`, publish `ClipAudioArtifactReplacedEvent` + `TranscriptTruthStateChangedEvent` (stale). |
| `TranscriptClipAudioReplaceUndoAction` | App | Undo: restore links + prior audio, set `Current`; Redo: new audio + remove links, set `StaleAfterClipRegeneration`; publish truth events + clip replaced. |
| `TranscriptTruthRefreshCoordinator` / `ITranscriptTruthRefreshCoordinator` | App | Entry: only `StaleAfterClipRegeneration`. Sets `RefreshInProgress`, calls **`ITranscriptionClient.TranscribeAudioAsync`** (existing `/api/transcribe` contract), verifies returned `AudioId` matches clip; `RemoveLinksByClipId` then `UpsertLinksForTranscription`; sets `Current`; publishes `TranscriptionCompletedEvent` + `TranscriptTruthStateChangedEvent`. |
| Backend | FastAPI | **No new route required** for this lane; canonical transcription remains existing transcribe service + routes. |
| `ClipTranscriptLinkageService` | App | Deterministic rebuild per [GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md) overlap rules. |
| `TranscribeViewModel` | App | Subscribes to `TranscriptTruthStateChangedEvent`; InfoBar + **Refresh transcript linkage** command when active project + `SelectedAudioId` resolve a **single** stale clip. |
| `TimelineViewModel` | App | Subscribes to `TranscriptTruthStateChangedEvent`; operator toasts by state (warning / info / success). |

## Hard IN

- [x] Persisted `TranscriptTruthState` on `AudioClip` (project JSON; `JsonProjectRepository` round-trip).
- [x] One canonical refresh path: `TranscriptTruthRefreshCoordinator`.
- [x] Operator messaging: stale, refresh-in-progress, refreshed/current, failed refresh (toasts + Transcribe InfoBar).

## Hard OUT

- Multi-segment editor, waveform editing, subtitle overhaul, batch regen, broad panel polish.

## Binary acceptance

- [x] After regeneration: clip `TranscriptTruth` is `StaleAfterClipRegeneration`; truth event published; persistence test passes.
- [x] Refresh: fail-closed when not stale; success clears stale, rebuilds linkage, `MarkProjectDirty("transcript_truth_refresh")`.
- [x] Undo/redo restores `TranscriptTruth` consistently with linkage + audio.
- [x] Transcribe + Timeline surfaces wired (InfoBar + command + event toasts).
- [x] MSTest: coordinator + regen success + JSON round-trip; closure report + tracker + registry + STATE proof.

## Proof

- [VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_LANE_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_LANE_CLOSURE_2026-03-31.md)

## Rollback

Revert `TranscriptTruthState` / `AudioClip.TranscriptTruth`, coordinator + DI, Transcribe/Timeline subscriptions, panel event, undo/regen truth edits; keep regenerate-segment lane intact.
