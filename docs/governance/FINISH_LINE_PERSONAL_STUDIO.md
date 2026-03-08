# Finish Line: Personal Studio (Workspace) — Acceptance Criteria

Feature-freeze document. Defines acceptance criteria and manual verification for the customizable workspace (Personal Studio) so the system is stable and provable.

---

## 1. Workspaces CRUD / Import / Export

| ID | Criterion | Verification |
|----|-----------|--------------|
| W1 | Create workspace: user can create a new named profile from the workspace selector or Manage dialog. | **Manual:** Open app → Workspace selector or Manage Workspaces → Create → enter name (e.g. `Test1`) → confirm. **Expect:** New profile appears in list; switching to it loads empty or default layout. **CI:** `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "PanelStateService"` includes create/rename/export/import tests. |
| W2 | Rename workspace: user can rename a profile (except `studio`). Case-only rename (e.g. `test` → `Test`) succeeds. | **Manual:** Manage Workspaces → select non-studio profile → Rename → new name. **Expect:** List shows new name; file on disk is `{newName}.json`. **CI:** `RenameWorkspace_ToExistingName_FailsDeterministically`, `RenameWorkspace_SameName_ReturnsTrueNoOp`, `RenameWorkspace_UpdatesProfileListAndRemovesOldName` pass. |
| W3 | Delete workspace: user can remove a profile (except `studio`). | **Manual:** Manage Workspaces → select profile → Delete → confirm. **Expect:** Profile removed from list; `{name}.json` removed from workspace profiles directory. |
| W4 | Export workspace: exports current profile layout (regions, panels, splitter ratios, collapsed state, PinnedPanelIds) as JSON. | **Manual:** Manage Workspaces → select profile → Export. **Expect:** JSON string (e.g. copied to clipboard or shown). Contains `profileName`, `regions`, `pinnedPanelIds`. **CI:** `PinnedPanelIds_SurviveExportImport` passes. |
| W5 | Import workspace: pasted JSON creates a new profile (name suffixed if conflict, e.g. `"Name (Imported)"`). Pins and layout round-trip. | **Manual:** Export a profile with pins → Import → paste. **Expect:** New profile appears; switch to it → pins and panel layout match. **CI:** `PinnedPanelIds_SurviveExportImport`, `PinnedPanelIds_AreWorkspaceScoped` pass. |
| W6 | Profile name validation: invalid names (empty, path separators, reserved device names, length &gt; 64) are rejected. | **CI:** `CreateWorkspace_RejectsInvalidNames` (≥10 invalid), `CreateWorkspace_AcceptsValidNames` (≥3 valid). Run: `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "CreateWorkspace"`. |

---

## 2. Tool Catalog: Search / Filter / Pin / Open to Region

| ID | Criterion | Verification |
|----|-----------|--------------|
| TC1 | Tool Catalog opens from toolbar or shortcut; lists panels from unified registry (GetAllDescriptors). | **Manual:** Click tool-catalog entry → dialog opens with panel list. **CI:** `test_tool_catalog_uses_registry`, `test_tool_catalog_uses_panel_descriptor`, `test_main_window_references_tool_catalog` in `tests/ci/test_tool_catalog_contract.py`. |
| TC2 | Search filters the list by panel name/description. | **Manual:** Open Tool Catalog → type in search box → list narrows. **CI:** `test_tool_catalog_has_region_chooser` (and filters) assert presence of `_categoryFilter`, `_maturityFilter` in ToolCatalogDialog.cs. |
| TC3 | Category filter (ComboBox) narrows by MenuCategory. | **Manual:** Select category → only panels in that category shown. **CI:** `test_tool_catalog_has_category_filter` passes. |
| TC4 | Maturity filter (ComboBox) narrows by maturity (Stable/Beta/Experimental). | **Manual:** Select maturity → list filtered. **CI:** `test_tool_catalog_has_maturity_filter` passes. |
| TC5 | Region chooser: user selects Left/Center/Right/Bottom; open uses that region, not default. | **Manual:** Select e.g. Right → Open a panel → panel opens in right host. **CI:** `test_tool_catalog_has_region_chooser`, `test_tool_catalog_region_flows_to_open` (MainWindow uses `dialog.SelectedRegion ?? desc.DefaultRegion` and `OpenPanelByIdAsync(desc.PanelId, region)`). |
| TC6 | Pin/unpin: context menu on list item pins that item (right-clicked item, not selection). Pinned panels sort to top; state is workspace-scoped. | **Manual:** Right-click a panel → Pin → reopen catalog → panel at top; switch workspace → different pins. **CI:** `test_tool_catalog_has_pin_support`, `test_tool_catalog_has_region_chooser` (IndexFromContainer/OriginalSource). `PinnedPanelIds_AreWorkspaceScoped` in PanelStateServiceTests. |
| TC7 | Open action: only opens via OpenPanelByIdAsync → PanelHost.LoadPanelAsync (no direct `new View()`). | **CI:** `test_main_window_uses_selected_region`; no `OpenPanelById` (sync) calls; panel IDs from registry. |

---

## 3. Docking, Resize, Collapse Persistence

| ID | Criterion | Verification |
|----|-----------|--------------|
| D1 | Dragging a panel to another region (docking) updates layout; panel state migrates so the same panelId keeps state in the new region. | **Manual:** Open panel in Left → drag to Right → close app → reopen → panel is in Right with same state. **CI:** `MigratePanelState_MovesStateAcrossRegions` in PanelStateServiceTests. |
| D2 | Splitter ratios (column/row star values) persist per workspace and restore on load. | **Manual:** Resize columns/rows → switch workspace and back, or restart → ratios restored. **Code:** SaveWorkspaceLayout writes WidthRatio/HeightRatio; RestoreSplitterRatios reads them in MainWindow.Workspaces.cs. |
| D3 | Region collapsed state persists; restore applies IsCollapsed to PanelHost. | **Manual:** Collapse a region → switch workspace and back or restart → region stays collapsed. **Code:** SavePanelHostRegion calls SaveRegionCollapsedState; restore applies to host. |
| D4 | Layout save is debounced (e.g. 2s) so rapid resize does not thrash disk. | **Code:** MainWindow uses Debouncer for SaveWorkspaceLayout (e.g. 2000 ms). No automated test required. |

---

## 4. Restore Failure Recovery (Toast + Reset)

| ID | Criterion | Verification |
|----|-----------|--------------|
| R1 | If restore restores zero regions or layout has regions but all fail: show toast with message and "Reset to Studio?" action. | **Manual:** Corrupt workspace JSON or remove a panel type from registry → start app. **Expect:** Toast with text like "Workspace restore failed — reset to Studio?"; action switches to `studio` and restores default panels. **Code:** InitializePanelsAsync in MainWindow.Workspaces.cs. |
| R2 | If restore partially fails (some regions fail): show toast listing failed panel IDs (up to 5) and region, e.g. "Failed to restore: 'Timeline' (Center), 'Profiles' (Left). Reset to Studio?" | **Manual:** Use a layout that references a panel ID that no longer exists. **Expect:** Toast shows failed panel IDs and regions. **Code:** FormatRestoreFailureMessage(failedItems) with (region, panelId). **CI:** Workspace tests; no dedicated pytest for message format. |
| R3 | Reset action switches to `studio` profile and restores its layout (default panels). | **Manual:** Trigger restore failure → click reset. **Expect:** Current profile becomes studio; Left=Profiles, Center=Timeline, Right=EffectsMixer, Bottom=Macro (or doc default). |

---

## 5. Manual: Workspace Thrash Test

Run this procedure on a clean profile (or a copy of `studio`) to validate stability. If any step fails, fix the bug; do not weaken the checklist.

1. **Start**  
   - Close VoiceStudio if running.  
   - Build: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`  
   - Launch app.  
   - **Expect:** App opens; default workspace (e.g. Studio) loaded; no crash.

2. **Create workspace**  
   - Open workspace selector or Manage Workspaces → Create.  
   - Name: `Thrash1` → confirm.  
   - **Expect:** `Thrash1` appears; switching to it shows empty or default layout.

3. **Tool Catalog open to region**  
   - Open Tool Catalog.  
   - Set region to Right.  
   - Open "Voice Profiles" (or any panel).  
   - **Expect:** Panel opens in the right host only.

4. **Pin and sort**  
   - In Tool Catalog, right-click "Timeline" → Pin.  
   - Right-click "Effects Mixer" → Pin.  
   - Close and reopen Tool Catalog.  
   - **Expect:** Timeline and Effects Mixer appear at top of list.

5. **Resize and collapse**  
   - Drag column splitters to change widths.  
   - Collapse bottom region (if visible).  
   - **Expect:** Layout updates; no crash.

6. **Switch workspace**  
   - Switch to `studio` then back to `Thrash1`.  
   - **Expect:** Layout and pins for `Thrash1` restored (right host panel, pins, ratios, collapsed state).

7. **Export / Import**  
   - Manage Workspaces → select `Thrash1` → Export (copy JSON).  
   - Import → paste.  
   - **Expect:** New profile e.g. `Thrash1 (Imported)`; switch to it → same layout and pins as `Thrash1`.

8. **Rename**  
   - Manage Workspaces → select `Thrash1 (Imported)` → Rename to `Thrash2`.  
   - **Expect:** List shows `Thrash2`; file on disk is `Thrash2.json`.

9. **Restore failure (optional)**  
   - Manually edit a workspace JSON to reference a non-existent panel ID; save.  
   - Restart app and switch to that profile (or make it default).  
   - **Expect:** Toast with failed panel IDs and "Reset to Studio?"; reset recovers to studio layout.

10. **Exit and re-open**  
    - Close app.  
    - Reopen.  
    - **Expect:** Last active profile restored; layout and pins match last save.

---

## 6. Build Determinism Rule

- **Rule:** The app must be closed before building. A locked running executable (or loaded DLLs) can cause the build to fail or produce undefined behavior (e.g. copy/lock errors on Windows).
- **Enforcement:** If build fails with file-in-use or access-denied on `VoiceStudio.App.exe` or under `.buildlogs`, close VoiceStudio and retry.
- **Optional helper:** Run `scripts/dev/stop_voicestudio.ps1` before build to terminate any running VoiceStudio process.

---

## 7. Verification Commands (Summary)

```powershell
# Build
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64

# C# tests (workspace + panel state)
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64

# CI (Tool Catalog contract, panel registry, etc.)
python -m pytest tests/ci/ -q --randomly-seed=12345
```

All of the above must pass before declaring the Personal Studio finish line complete.
