# GOV-VOICESTUDIO-SESSION-AUTOSAVE-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-SESSION-AUTOSAVE-01`  
**Status:** Closed (2026-03-29)  
**Tracker:** GAP-020 — **Closed** (session autosave + explicit crash recovery)  
**Closure:** [VOICESTUDIO_SESSION_AUTOSAVE_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_SESSION_AUTOSAVE_LANE_CLOSURE_2026-03-29.md)  

## Frozen objective

Deliver **project-scoped** dirty detection, **debounced + failsafe** autosave through the **canonical shell save seam** (`UnifiedProjectSaveHandler` / `IProjectSaveHandler`), **settings-backed** intervals (`SettingsData.General.AutoSave`, `AutoSaveInterval`), and **explicit** crash-recovery UX (no silent overwrite of manual save).

## Binary acceptance (lane)

| Slice | Acceptance |
|-------|------------|
| 1 — Authority + contract | Execution row names save / autosave / recovery owners; precedence order written; hard IN/OUT frozen; split-brain risks documented. |
| 2 — Autosave pipeline | Dirty transitions are deterministic; autosave runs only when dirty + AutoSave enabled; uses same handler as manual save; settings interval honored (seconds); no duplicate save authority. |
| 3 — Recovery UX | `CrashRecoveryService` resolvable from `ServiceProvider` / deferred init; pending recovery does not auto-apply; user Restore vs Discard is explicit; clean shutdown clears crash marker. |
| 4 — Verification | App tests for dirty/autosave/recovery precedence; `dotnet build` / `dotnet test` / `pytest tests/ci` / `verify.ps1 -Quick` / `run_verification.py` PASS; closure report; STATE + registry + gap tracker updated. |

## Precedence order (frozen)

1. **Manual save** (`SaveProjectAsync` from menu/command) is the authoritative user intent.
2. **Autosave** persists the same payload through `IProjectSaveHandler` and is a recoverable candidate only.
3. **Restore** after crash is an **explicit user decision** — never silent replacement of the last manual save.
4. No implicit overwrite: declining recovery **discards recovery artifacts only**, not normal project files.

## Hard IN

- Project-scoped dirty state (`IProjectSessionDirtyState`) and high-signal hooks (timeline `Tracks`, mixer edits).
- `SessionAutosaveOrchestrator`: debounce + periodic failsafe while dirty; reads settings via `ISettingsService.LoadSettingsAsync`.
- `ProjectWorkflowCoordinator.TryAutosaveProjectAsync` — same `_saveHandler` as manual save; **no error toast spam** (log-only failure path).
- `CrashRecoveryService`: pending recovery queue + `PendingRecoveryDetermined` for UI; `MarkCleanShutdown` on exit.
- Session metadata (`session.json`) alignment with active project id/name after successful save.

## Hard OUT

- GAP-029 export/effects authority redesign; PanelHost GAP-007; new persistence roots; plugin/metering/waveform/collaboration scope.

## Authority map (save / autosave / recovery)

| Concern | Owner | Notes |
|--------|--------|--------|
| Canonical project persist (tracks snap + mixer + backend `UpdateProjectAsync`) | `UnifiedProjectSaveHandler` | Single seam for manual + autosave |
| Workflow orchestration (startup gate, user toasts) | `ProjectWorkflowCoordinator` | Manual save + `TryAutosaveProjectAsync` |
| Dirty flag | `ProjectSessionDirtyState` | Suppressed during load/open list refresh |
| Autosave timing | `SessionAutosaveOrchestrator` | MainWindow-owned lifecycle after panels ready |
| Recovery file + marker | `CrashRecoveryService` | `%LocalAppData%/VoiceStudio/Recovery/` |
| Backend project files | `project_store_service` / `track_store` (existing env roots) | No new path assumptions |

## Split-brain risks

- **FileOperationsHandler** JSON path vs shell save: autosave does **not** add a new path; remains unified handler only.
- **Mixer inline saves**: still persist mixer state immediately; additionally **mark project dirty** so unified autosave can run for timeline coherence.

## Environment note

`VOICESTUDIO_PROJECTS_DIR` vs `VOICESTUDIO_PROJECTS_PATH` are **not** assumed equivalent; autosave does not introduce new coupling to disk layout beyond existing handler.
