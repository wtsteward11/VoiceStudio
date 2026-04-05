# GOV-VOICESTUDIO-GAP037-WAVEFORM-EDITING-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP037-WAVEFORM-EDITING-01 |
| **GAP** | GAP-037 |
| **Status** | **Closed** (2026-04-03) |
| **Phase** | 4 (Waveform / timeline edit MVP) |
| **Role** | UI Engineer + Core Platform |
| **Effort** | 64h (tracker); **this lane is bounded MVP** (trim / split / fade / contract / persistence mirror) |
| **Dependency** | GAP-017 (persistence), GAP-031 (mixdown/export authority) |
| **Created** | 2026-04-02 |

## §1 Objective (frozen)

Deliver a **non-destructive** waveform edit **MVP**: trim-in, trim-out, split-at-playhead, fade-in, fade-out, with **correct export/mixdown** semantics, **aligned C# ↔ FastAPI contracts**, **persistence mirror** for project clips, **tests + closure-grade verification**. Transcript/session **transcript-linkage** behavior must not be regressed (treat transcript IDs as **out of lane** unless already stable).

## §2 Hard IN

- **Contract:** `TimelineUseCase` payloads match `backend/api/routes/timeline.py` (delete `id`, trim `new_start`/`new_end`, move `new_start_time`/`new_track_id`, split `split_position`, split response `clip_before`/`clip_after`, add clip flat `AddClipRequest`).
- **Backend timeline:** Trim advances `source_start` when trimming from the **start**; split remains deterministic with `source_start` on the right-hand clip; **fade** applied in `_render_timeline_audio` (linear ramps, clamped to clip length).
- **Persistence:** Project track clip JSON may carry `source_start_seconds`, `fade_in_seconds`, `fade_out_seconds`; `import-from-project` maps them into timeline `Clip`.
- **UI:** Timeline panel commands route through `ITimelineUseCase` + persistence (`IBackendClient.UpdateClipAsync` / `CreateClipAsync`) — no view-only graph that **only** mutates local collections for these ops.
- **Undo:** Backend timeline `POST /api/timeline/undo` remains valid for ops that use timeline routes; local `UndoRedoService` may register actions where already used for paste/duplicate (no new silent fire-and-forget).
- **Tests:** Pytest timeline routes (trim/split/fade/mixdown), C# `TimelineUseCase` payload-shape tests, ViewModel/command tests as feasible without WinUI composition.
- **Verification:** Matrix in §6 GREEN before closure.

## §3 Hard OUT

- Ripple edit suite, spectral editing, elastic time-stretch, automation lanes.
- Full DAW-style crossfade editor UI.
- GAP-007 PanelHost / shell redesign; GAP-067 notification center scope.
- Replacing SQLite project authority or new ADR without Overseer approval.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| **Persistence** (tracks/clips JSON in `project_tracks`) | `backend/api/routes/tracks.py` + `TrackStore` |
| **Mixdown / export graph** | `backend/api/routes/timeline.py` (`import-from-project`, `_render_timeline_audio`, `export`) |
| **C# seam** | `ITimelineUseCase` / `TimelineUseCase` |
| **UI commands** | `TimelineViewModel` + `TimelineView.xaml.cs` context menu |
| **Single-flight / transport** | `IBackendClient` (existing pipeline) |

## §5 Acceptance criteria

- [x] Trim from start updates **both** timeline `start_time` and `source_start`; trim from end updates `end_time` only; export audio content matches trimmed boundaries (pytest).
- [x] Split at interior time returns `clip_before` / `clip_after`; **C# deserializes** both; second clip has correct `source_start`.
- [x] Fade-in / fade-out seconds honored in render (audible ramp; unit test with synthetic clip or amplitude bounds).
- [x] `TimelineUseCase` JSON bodies use **snake_case names expected by FastAPI** (verified in MSTest).
- [x] After edit, **project** clip row reflects timing/metadata needed for re-import; export-after-save remains correct.
- [x] Closure report + STATE + tracker + registry synced; `run_verification.py` completion_guard PASS.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_timeline_mixdown.py -q
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| **Dual-model drift** | Always mirror persistence after timeline op OR re-import project after persistence-only change; tests for import round-trip. |
| **Split / identity corruption** | Preserve left clip id; right clip new id; mirror with `UpdateClip` + `CreateClip`. |
| **Transcript linkage** | Do not rename clip IDs except split right-hand new id; document in tests. |
| **Transcript agent / Cursor JSONL** | Do not embed transcript content into execution row; link transcript path only if required by Overseer. |

## §8 Rollback order

1. `TimelineViewModel` / context menu commands  
2. `TimelineUseCase` / DTO changes  
3. Timeline route fade/trim semantics  
4. `tracks.py` optional clip fields  
5. Governance rows for GAP-037  

Keep **GAP-031** mixdown and **GAP-034** notifications intact.
