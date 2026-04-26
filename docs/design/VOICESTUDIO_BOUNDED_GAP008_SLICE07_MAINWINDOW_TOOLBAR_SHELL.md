# GAP-008 Slice 7 — MainWindow toolbar customization shell glue (bounded)

**Status:** Accepted (Tasks 309–318; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 6](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md)

## First seam (exact)

**Shell-only delegation** for opening **toolbar customization** from the shell:

- **Menu:** **`_customizeToolbarMenuItem`** remains created on **`MainWindow`**; **`CustomizeToolbarMenuItem_Click`** stays the code-behind entry point and **forwards** to **`MainWindowToolbarCustomizationShellBridge.ShowCustomizationDialogAsync()`**.
- **Dialog:** **`ToolbarCustomizationDialog`** construction, **`XamlRoot`** assignment from **`MainWindow.Content?.XamlRoot`**, **`ShowAsync`**, and **failure toast** (`TryGetToastNotificationService` + **`ShowError`**) live in the bridge (via injectable **`IToolbarCustomizationDialogLauncher`** for tests).

**Types:** [`MainWindowToolbarCustomizationShellBridge`](../../src/VoiceStudio.App/Services/MainWindowToolbarCustomizationShellBridge.cs); [`IToolbarCustomizationDialogLauncher`](../../src/VoiceStudio.App/Services/IToolbarCustomizationDialogLauncher.cs); [`ToolbarCustomizationDialogLauncher`](../../src/VoiceStudio.App/Services/ToolbarCustomizationDialogLauncher.cs).

## In scope (explicit symbol list)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`CustomizeToolbarMenuItem_Click`** | Thin forward → bridge **`ShowCustomizationDialogAsync`** |
| **`MainWindowToolbarCustomizationShellBridge`** | Owns try/catch, launcher call, toast on failure |
| **`_customizeToolbarMenuItem`** field + ctor wiring + **`Click +=`** | Stays on **`MainWindow`** (composition) |
| **`MainWindow.Menu.cs`** | Continues to add **`_customizeToolbarMenuItem`** to Tools menu (no refactor) |

## Explicitly NOT Slice 7 (deferred)

| Cluster | Deferred to | Notes |
| ------- | ----------- | ----- |
| **`ShowCommandPalette`** / Ctrl+P (`nav.commandpalette`) | **[Slice 8](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md)** — landed | **`MainWindowCommandPaletteShellBridge`**; empty **`catch`** removed |
| **`ShowToolCatalogAsync`** / Ctrl+Shift+T (`nav.toolcatalog`) | **Slice 8+** (separate brief after palette) | Uses **`OpenPanelByIdAsync`**; higher blast radius |
| **Global search overlay** | [Slice 6](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md) | **`_searchOverlayShellBridge`** must not appear in Slice 7 bridge source |
| **Project / recent / import / transport** | Prior slices / future | No expansion |
| **`CustomizableToolbar.HandleToolbarButtonClick`** / **`App.MainWindowInstance`** import path | Future bounded slice if extracted | Not required for customization launcher |
| **`engines/audio/rhvoice/`** | Frozen | **Task 318** |

## Dependency / blast-radius map (Task 310)

| Responsibility | Current owner | Target after Slice 7 | Risk | Tests |
| ---------------- | ------------- | ---------------------- | ---- | ----- |
| Customize toolbar menu → dialog | **`MainWindow.CustomizeToolbarMenuItem_Click`** | Thin handler → **`MainWindowToolbarCustomizationShellBridge`** | M (XamlRoot, async) | Moq **`IToolbarCustomizationDialogLauncher`** + toast on throw |
| Command palette | **`MainWindow.ShowCommandPalette`** | **[Slice 8](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md)** **`MainWindowCommandPaletteShellBridge`** | — | See Slice 8 tests |
| Tool catalog | **`MainWindow.ShowToolCatalogAsync`** | **Unchanged** | L/M | None this slice |
| Toolbar button dispatch | **`CustomizableToolbar`** + VM / keyboard service | **Unchanged** | H if touched | None this slice |
| Search overlay | **`MainWindowSearchOverlayShellBridge`** | **Unchanged** | — | **Zero** references in Slice 7 bridge file |

**Search-overlay coupling:** **none** — grep gate in **`Gap008Slice7Tests`**.

## Slice 8 (Task 316 — landed)

**Implemented:** [Slice 8 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md) — **`MainWindowCommandPaletteShellBridge`**, **`nav.commandpalette`** → **`ShowCommandPalette`** → bridge; explicit **`ErrorLogger`** + toast on failure. **Tool catalog** (`ShowToolCatalogAsync`) → **Slice 9+** planning in MAINWINDOW plan + Slice 8 brief.

## RHVoice (Task 318)

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice remains **frozen** / **operator-gated**; does not reorder GAP-008.

## Narrow-seam rule (Task 317)

**`MainWindowToolbarCustomizationShellBridge`** owns **toolbar customization dialog launch from shell** only — not command palette, not tool catalog, not search overlay, not **`CustomizableToolbar`** internals. New shell behavior → new bounded brief or explicit amendment.

## Verification (Task 314 — fill after green)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowToolbarCustomizationShellBridgeTests|FullyQualifiedName~Gap008Slice7Tests|FullyQualifiedName~MainWindowSearchOverlayShellBridgeTests|FullyQualifiedName~Gap008Slice6Tests|FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~Gap008Slice5Tests" -v q
python scripts\run_verification.py
```

**Results (2026-04-25):**

- **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`:** **0 Error(s)** (warnings only).
- **`dotnet test` (filter above):** **Passed: 43**, Failed: 0 — includes Slice 7 bridge + **`Gap008Slice7Tests`**, Slice 6 search overlay tests + **`Gap008Slice6Tests`**, **`SearchOverlayCoordinatorTests`**, **`Gap008Slice5Tests`**, and **Task 313** additions (`TryCollapseGlobalSearchOverlayIfFrameworkElement` unit tests + text pin).
- **`python scripts/run_verification.py`:** **Overall: PASS** — `.buildlogs/verification/last_run.json`.

**Task 313 note:** A dedicated **`[UITestMethod]`** for **`FrameworkElement.Visibility`** was **not** retained — the WinUI test host **crashed** in this repo’s vstest configuration; collapse semantics are covered by **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** unit tests + **`Gap008Slice6Tests`** source pin (`is not FrameworkElement`).

**Verify bar:** unchanged.

## Changelog

- **2026-04-25:** Tasks 309–318 — brief + **`MainWindowToolbarCustomizationShellBridge`** + tests + **`MainWindow`** wiring + Slice 6 regression (`object?` collapse + UITest) + docs/STATE/registry after green.
