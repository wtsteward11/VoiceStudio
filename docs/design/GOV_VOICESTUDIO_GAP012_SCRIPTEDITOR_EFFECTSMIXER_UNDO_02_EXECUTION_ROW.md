# GOV-VOICESTUDIO-GAP012-SCRIPTEDITOR-EFFECTSMIXER-UNDO-02

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP012-SCRIPTEDITOR-EFFECTSMIXER-UNDO-02 |
| **GAP** | GAP-012 (bounded slice: ScriptEditor + EffectsMixer undo remainder) |
| **Status** | **Closed** (2026-04-09) |
| **Phase** | Successor to GOV-VOICESTUDIO-GAP012-TIMELINE-EDIT-UNDO-BOUNDED-01 (Closed 2026-04-04) |
| **Role** | UI Engineer |
| **Dependency** | GAP-012 slice 01 (timeline coherence undo) — **Closed** |
| **Created** | 2026-04-08 |

## §1 Objective (frozen)

Finish **user-visible undo/redo** for **ScriptEditor** and **EffectsMixer** for the bounded mutations below, using **`UndoRedoService` + `IUndoableAction`** with **persistence parity** where the backend is the authority (script update, effect chain update).

## §2 Hard IN

- **ScriptEditor**
  - **`UpdateScriptUndoAction`:** snapshot **before** / **after** `ScriptUpdateRequest`; **Undo/Redo** calls `IScriptEditorClient.UpdateScriptAsync` then reload callback to resync UI.
  - **Delete selected scripts:** `CompositeUndoAction` over per-script `DeleteScriptAction` instances (same persistence path as single delete); update confirmation copy (no “cannot be undone” once undo exists).
- **EffectsMixer**
  - **Bypass:** `ToggleBypassUndoAction` for `IsEffectChainBypassed` (client session flag for GAP-039 apply/preview — **not** persisted on effect chain API).
  - **Effect enable:** `ToggleEffectEnabledUndoAction` after user toggles enable checkbox; persists via `UpdateEffectChainAsync`.
  - **Save effect chain:** `UpdateEffectChainSnapshotUndoAction` capturing **full `EffectChain` clone** before vs after successful `UpdateEffectChainAsync`.
- **Tests:** MSTest for new undo actions + targeted ViewModel/seam coverage as listed in §6.
- **Verification:** Matrix in §6 **GREEN** before closure.

## §3 Hard OUT

- Global shell undo bus; cross-panel unified undo stack.
- Timeline edits; `GenerateSegmentAsync` / synthesis undo.
- Mixer volume/pan/mute/solo, sends/returns/subgroups, presets, reset — **separate lanes**.
- Continuous gesture coalescing for sliders.
- Refactor of `UndoRedoService` beyond additive `CompositeUndoAction`.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| **Undo stack** | `UndoRedoService` (singleton) |
| **Script persistence** | `IScriptEditorClient.UpdateScriptAsync` / `DeleteScriptAsync` |
| **Effect chain persistence** | `IEffectChainClient.UpdateEffectChainAsync` |
| **Redo invalidation** | `RegisterAction` clears redo stack |
| **Dirty project** | `IProjectSessionDirtyState.MarkProjectDirty` on persisted undo/redo where applicable |

## §5 Undo authority (design pass)

### ScriptEditor

- **Undoable:** rename / description / segment edits delivered through **`UpdateScriptAsync`** — **before** snapshot taken from current `ScriptItem` + segment list immediately before the successful `UpdateScriptAsync` call; **after** snapshot equals the persisted request payload.
- **Not undoable (this lane):** `GenerateSegmentAsync` (audio + multi-field side effects).
- **Delete selected:** same API as single delete (`DeleteScriptAsync` per id); **one** composite undo restores in **reverse deletion order** using each `DeleteScriptAction`.

### EffectsMixer

- **Bypass:** intent per toggle of `IsEffectChainBypassed`; no backend field — undo only flips VM state (suppress flag during load/undo).
- **Effect enable + save:** toggle persists chain; undo restores prior `Enabled` and re-calls `UpdateEffectChainAsync`.
- **Save chain:** one undo unit per successful save; snapshots are **deep clones** of `EffectChain` (effects + parameters).

## §6 Acceptance criteria

- [x] `UpdateScriptAsync` registers `UpdateScriptUndoAction` after successful persist; undo/redo restores prior/next server state and reloads list.
- [x] `DeleteSelectedScriptsAsync` registers `CompositeUndoAction` wrapping `DeleteScriptAction` entries; confirmation text updated.
- [x] Bypass toggle registers `ToggleBypassUndoAction` (no fake undo when suppressed).
- [x] Effect enable (checkbox) registers `ToggleEffectEnabledUndoAction` + persists.
- [x] `SaveEffectChainAsync` registers `UpdateEffectChainSnapshotUndoAction` after success.
- [x] MSTest coverage for new actions (`ScriptEditorUndoActionTests`, `EffectChainUndoActionTests`).
- [x] Closure report + tracker + STATE + registry synced — **no governance lag**.

## §7 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ScriptEditorUndoActionTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EffectChainUndoActionTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §8 Risk register

| Risk | Mitigation |
|------|------------|
| Sync-over-async in `IUndoableAction` | Same pattern as `TimelineTrackClipsCoherenceUndoAction` |
| Effect checkbox re-entrancy | `_suppressEffectEnableUndo` during programmatic fix |
| Composite partial failure | Log each step; prefer throw on first failure for coherence |

## §9 Rollback order

1. ViewModel + View wiring  
2. Undo action types  
3. Execution row / closure (governance only)  

## Changelog

- **2026-04-08:** Row frozen (Open) — implementation in progress.
- **2026-04-09:** **Closed** — [VOICESTUDIO_GAP012_SCRIPTEDITOR_EFFECTSMIXER_UNDO_LANE_CLOSURE_2026-04-09.md](../reports/verification/VOICESTUDIO_GAP012_SCRIPTEDITOR_EFFECTSMIXER_UNDO_LANE_CLOSURE_2026-04-09.md); tracker **GAP-012** **Closed**; App.Tests **3210** / skipped **274**; Quick `artifacts/verify/20260409_064018/`; rolling `20260409-064607`.
