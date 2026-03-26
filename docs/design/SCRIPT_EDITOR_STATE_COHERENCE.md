# Script Editor State Coherence

## Strategy (Selective)

The Script Editor uses **two intentional state-coherence models**:

### Model A — Reload + Rebind (full script payload changes)

Used by `UpdateScriptAsync` and `GenerateSegmentAsync`. After persist:

1. Capture `selectedScriptId` and `selectedSegmentId` before reload.
2. Call `LoadScriptsAsync` (reload from server).
3. Rebind `SelectedScript = Scripts.FirstOrDefault(s => s.Id == selectedScriptId)`.
4. Rebind `SelectedSegment = SelectedScript?.Segments.FirstOrDefault(s => s.Id == selectedSegmentId)`.

**Rationale:** Full script updates (name, description, segments) can change structure. Reload ensures `Scripts` reflects server truth and selection points at live collection instances.

### Model B — Local Mutation (targeted changes)

Used by `AddSegmentAsync`, `RemoveSegmentAsync`, `CreateScriptAsync`, `DeleteScriptAsync`. After persist:

- Mutate local `SelectedScript`, `SelectedScript.Segments`, or `Scripts` in place.
- **AddSegment:** Rebind `SelectedSegment` by Id after `UpdateFrom` (segments are replaced with new instances).
- **RemoveSegment:** Set `SelectedSegment = null` when the removed segment was selected.

**Rationale:** Add/remove/create/delete are targeted operations. Local mutation keeps responsiveness; selection coherence is maintained by explicit rebind.

## Guarantees

- **Update/Generate:** `SelectedScript` is in `Scripts`; `SelectedSegment` is in `SelectedScript.Segments`.
- **Add/Remove:** `SelectedScript` remains in `Scripts`; `SelectedSegment` is null or in `SelectedScript.Segments` after remove.

## Reference

- Reload+rebind: `ScriptEditorViewModel.UpdateScriptAsync`, `ScriptEditorViewModel.GenerateSegmentAsync`
- Local mutation: `ScriptEditorViewModel.AddSegmentAsync`, `ScriptEditorViewModel.RemoveSegmentAsync`, `CreateScriptAsync`, `DeleteScriptAsync`
- Tests: `UpdateScriptAsync_AfterReload_SelectedScriptAndSegmentAreInLiveCollection`, `GenerateSegmentAsync_AfterReload_SelectedScriptAndSegmentAreInLiveCollection`, `AddSegment_AfterAdd_SelectedScriptAndSegmentCoherent`, `RemoveSegment_WhenRemovingOtherSegment_SelectedSegmentRemainsCoherent`, `RemoveSegment_WhenRemovingSelectedSegment_ClearsSelectedSegment`
