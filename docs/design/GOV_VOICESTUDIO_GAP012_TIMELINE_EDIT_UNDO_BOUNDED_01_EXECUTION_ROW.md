# GOV-VOICESTUDIO-GAP012-TIMELINE-EDIT-UNDO-BOUNDED-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP012-TIMELINE-EDIT-UNDO-BOUNDED-01 |
| **GAP** | GAP-012 (bounded slice: timeline trim / split / fade only) |
| **Status** | **Closed** (2026-04-04) |
| **Phase** | Successor plan Phase 1 (extracted prerequisite before GAP-040 / GAP-038) |
| **Role** | UI Engineer + Core Platform |
| **Dependency** | GAP-037 (bounded waveform MVP), GAP-017 persistence |
| **Created** | 2026-04-04 |

## §1 Objective (frozen)

Establish **one user-visible undo authority** for **bounded** timeline edit operations (trim start/end, split at playhead, set fade) such that **Undo** restores **persisted project clips** and **re-imports** the backend mix graph — no desync between UI track clips and project store.

## §2 Hard IN

- **`UndoRedoService` + `TimelineTrackClipsCoherenceUndoAction`:** full-track clip snapshots **before** and **after** each bounded edit; **Undo/Redo** applies target snapshot via `IBackendClient` (`DeleteClipAsync` / `UpdateClipAsync` / `CreateClipAsync`) then `ITimelineUseCase.ImportProjectTimelineAsync`.
- **Wiring:** `TimelineViewModel` registers coherence undo after successful split/trim/fade paths (post-persistence + re-import).
- **Tests:** MSTest on `TimelineTrackClipsCoherenceUndoAction` (split-like undo deletes new id + updates original).
- **Verification:** Matrix in §6 GREEN before closure.

## §3 Hard OUT

- Calling `POST /api/timeline/undo` for these edits’ undo path (backend in-memory `_undo_stack` is **not** the UX authority for this slice; it may still accrue entries from timeline API calls — see §7).
- Multi-track atomic undo, ripple edits, move-clip drag undo, **delete-selected** persistence redo (still `DeleteClipsAction` UI-first; **GAP-040**).
- GAP-007 PanelHost / shell redesign; GAP-038 GPU rendering; full non-destructive model (GAP-040).

## §4 Authority map (this lane)

| Concern | Owner |
|---------|--------|
| **User Undo/Redo for trim/split/fade** | `UndoRedoService` + `TimelineTrackClipsCoherenceUndoAction` |
| **Project clip truth** | `IBackendClient` project clip routes |
| **Mix graph after restore** | `ITimelineUseCase.ImportProjectTimelineAsync` |
| **Backend timeline global undo stack** | `timeline.py` — **secondary** for this slice (not invoked on Undo) |

## §5 Acceptance criteria

- [x] After trim/split/fade, **Undo** restores prior clip set and timing on the **selected track** and triggers **import-from-project**.
- [x] **Redo** re-applies the post-edit snapshot (same mechanism).
- [x] Unit test proves **split-like** undo issues **DeleteClip** for removed id + **UpdateClip** for restored geometry.
- [x] Closure report + tracker + registry + STATE synced.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TimelineTrackClipsCoherenceUndoActionTests"
python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_timeline_mixdown.py -q
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| **Dual stack confusion** | Document: UX undo = client coherence action; timeline route mutations may still push backend `_undo_stack` — GAP-040 may align or clear. |
| **STA / blocking** | Undo uses sync-over-async (`GetAwaiter().GetResult()`), same pattern as `TranscriptClipAudioReplaceUndoAction`. |
| **Large tracks** | Full-track snapshot each op; acceptable for MVP bounded lane. |

## §8 Rollback order

1. `TimelineViewModel` registration calls  
2. `TimelineTrackClipsCoherenceUndoAction`  
3. This execution row / closure (governance only)
