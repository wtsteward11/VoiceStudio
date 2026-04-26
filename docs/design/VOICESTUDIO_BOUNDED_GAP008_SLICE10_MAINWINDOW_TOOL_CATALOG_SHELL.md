# GAP-008 Slice 10 — MainWindow tool catalog shell glue (bounded)

**Status:** Accepted (Tasks 339–348; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 9](VOICESTUDIO_BOUNDED_GAP008_SLICE09_MAINWINDOW_TOOLBAR_COMMAND_ROUTING_SHELL.md) (toolbar routing; **not** engine “Bounded Slice 9”)

## First seam (exact)

**Shell-only delegation** for opening the **Tool Catalog** dialog from the shell, then applying **`OpenPanelByIdAsync`** + **`PanelHost`** chrome when the user confirms:

- **Keyboard:** **`nav.toolcatalog`** (Ctrl+Shift+T) — **`RegisterKeyboardShortcuts`** lambda forwards to **`ShowToolCatalogAsync()`** (same entry as any future menu path).
- **Entry:** **`MainWindow.ShowToolCatalogAsync`** — thin forward to **`MainWindowToolCatalogShellBridge.RunShowAsync()`**.
- **Orchestration:** Bridge resolves **`XamlRoot`** via injected factory, invokes **`IToolCatalogShellLauncher.ShowAsync`** (production: **`ToolCatalogShellLauncher`** → **`ToolCatalogDialog`**). On **Primary** + selection, calls injected **`Func<string, PanelRegion?, Task<bool>>`** (production: forwards to **`MainWindow`** → **`_navShellBridge.OpenPanelByIdAsync`**). On success, invokes injected **`Action<PanelRegion, string, string?>`** to set **`PanelHost`** title/icon (production: **`FindNameOnContent`** + **`SetPanelHostMeta`**).
- **Failure:** **`IToolCatalogShellDiagnostics`** (default → **`ErrorLogger`**) + **`IToastNotificationService.ShowError`** when the bridge or launcher throws — **no** silent swallow (replaces prior **`Debug.WriteLine`**-only path for exceptions).
- **Re-entrancy:** Multiple overlapping catalog opens are **allowed** (same as pre-slice behavior); no single-flight mutex in this slice.

**Types:** [`MainWindowToolCatalogShellBridge`](../../src/VoiceStudio.App/Services/MainWindowToolCatalogShellBridge.cs); [`IToolCatalogShellLauncher`](../../src/VoiceStudio.App/Services/IToolCatalogShellLauncher.cs); [`ToolCatalogShellLauncher`](../../src/VoiceStudio.App/Services/ToolCatalogShellLauncher.cs); [`IToolCatalogShellDiagnostics`](../../src/VoiceStudio.App/Services/IToolCatalogShellDiagnostics.cs); [`ToolCatalogShellErrorDiagnostics`](../../src/VoiceStudio.App/Services/ToolCatalogShellErrorDiagnostics.cs).

## In scope (explicit symbol list)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`ShowToolCatalogAsync`** | Single **`MainWindow`** entry; forwards to **`_toolCatalogShellBridge.RunShowAsync()`** |
| **`nav.toolcatalog`** / Ctrl+Shift+T | Lambda → **`ShowToolCatalogAsync()`** (same bridge path) |
| **`MainWindowToolCatalogShellBridge`** | XamlRoot factory + launcher + open-panel + chrome delegates + diagnostics + toast on failure |
| **`IToolCatalogShellLauncher`** / **`ToolCatalogShellLauncher`** | Test seam; production wraps **`ToolCatalogDialog`** |
| **`ToolCatalogShellChoice`** | DTO crossing launcher → bridge (panel id, effective region, display name, icon) |

**Inventory (grep):** **`ShowToolCatalogAsync`** / **`nav.toolcatalog`** — **`MainWindow.xaml.cs`** only (no menu duplicate at charter time). **`ToolCatalogDialog`** — implementation unchanged except consumed by launcher.

## Explicitly NOT Slice 10 (deferred)

| Cluster | Deferred to | Notes |
| ------- | ----------- | ----- |
| **`MainWindowCommandPaletteShellBridge`** / command palette | [Slice 8](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md) | **Zero** references in Slice 10 bridge source (`Gap008Slice10Tests` creep gate) |
| **`MainWindowSearchOverlayShellBridge`** / global search | [Slice 6](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md) | Same creep gate |
| **`MainWindowToolbarCustomizationShellBridge`** | [Slice 7](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md) | Same |
| **`MainWindowToolbarCommandShellBridge`** / toolbar import | [Slice 9](VOICESTUDIO_BOUNDED_GAP008_SLICE09_MAINWINDOW_TOOLBAR_COMMAND_ROUTING_SHELL.md) | Same |
| **Project workflow / recent-project mutation / Loaded tail / nav-bridge internals** | Prior slices | Bridge calls **`OpenPanelByIdAsync`** port only — does not open **`MainWindowNavigationShellBridge`** |
| **`engines/audio/rhvoice/`** | **Frozen** | **Zero** edits |

## Naming trap

**Engine parity “Bounded Slice 9”** (XTTS playback, `PROOF_SLICE9_*`) is **unrelated** to **GAP-008 Slice 9** (toolbar command routing). This brief is **GAP-008 Slice 10** only.

## Dependency / blast-radius map (Task 340)

| Responsibility | Current owner (pre-slice) | Target after Slice 10 | Risk | Tests |
| -------------- | ------------------------- | ---------------------- | ---- | ----- |
| Keyboard Ctrl+Shift+T | **`MainWindow`** lambda → inline **`ShowToolCatalogAsync`** | Lambda → **`ShowToolCatalogAsync`** → bridge | L | **`Gap008Slice10Tests`** text pins |
| Dialog + selection | **`ToolCatalogDialog`** inline in **`MainWindow`** | **`ToolCatalogShellLauncher`** | M | Moq **`IToolCatalogShellLauncher`** |
| Panel open | **`OpenPanelByIdAsync`** → **`_navShellBridge`** | Injected **`Func<string, PanelRegion?, Task<bool>>`** from **`MainWindow`** | M | Assert mock invoked with expected ids |
| **`PanelHost`** title/icon | **`FindNameOnContent`** + property sets in **`MainWindow`** | Injected **`Action<PanelRegion, string, string?>`** | M | Assert apply action invoked when open returns true |
| **`XamlRoot`** | **`this.Content.XamlRoot`** | **`Func<XamlRoot?>`** — null → explicit toast + return (no dialog) | M | Test null root path |
| Error on failure | **`Debug.WriteLine`** + toast | **`IToolCatalogShellDiagnostics`** + **`ShowError`** | **H** (policy) | Launcher throws → diagnostics + toast |
| Coupling to other bridges | None desired | **None** (creep identifiers) | M | **`Gap008Slice10Tests`** on bridge file |

**Stop rule (pre-extraction):** Isolation holds: catalog flow needs only **XamlRoot**, **dialog**, **open panel func**, **chrome action**, **toast** — no palette/search/toolbar types in the new bridge file.

## Narrow-seam rule

**`MainWindowToolCatalogShellBridge`** owns **tool catalog open + confirm → open panel + chrome** from shell only — not command palette, not search overlay, not toolbar customization, not toolbar command routing, not general navigation implementation. A second unrelated responsibility requires a **new bounded brief + tests**.

## Optional composition: “not wired” open-panel / chrome ports

**`MainWindow`** calls **`WireToolCatalogHandlers`** once after constructing the bridge (same pattern as **`WireImportAudioHandler`** on the toolbar command bridge). If **`RunShowAsync`** runs before **`WireToolCatalogHandlers`**, or the launcher returns a selection while **`WireToolCatalogHandlers`** was never called, the bridge throws **`InvalidOperationException`** with an explicit **“Tool catalog shell is not wired”** message (fail-fast; no silent fallback). Tests cover this path.

## RHVoice

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice remains **frozen** / **operator-gated**.

## Verification (fill after green)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowToolCatalogShellBridgeTests|FullyQualifiedName~Gap008Slice10Tests|FullyQualifiedName~MainWindowToolbarCommandShellBridgeTests|FullyQualifiedName~Gap008Slice9Tests|FullyQualifiedName~ToolbarViewModelTests|FullyQualifiedName~MainWindowCommandPaletteShellBridgeTests|FullyQualifiedName~Gap008Slice8Tests|FullyQualifiedName~MainWindowToolbarCustomizationShellBridgeTests|FullyQualifiedName~Gap008Slice7Tests|FullyQualifiedName~MainWindowSearchOverlayShellBridgeTests|FullyQualifiedName~Gap008Slice6Tests|FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~Gap008Slice5Tests" -v q
python scripts\run_verification.py
```

**Results (2026-04-25):**

- **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`:** **0 Error(s)** (warnings only; pre-existing in solution).
- **`dotnet test` (widened filter above):** **Passed: 71**, Failed: 0 — extends Slice 9 batch (**60**) with **`MainWindowToolCatalogShellBridgeTests`** (**8**) + **`Gap008Slice10Tests`** (**3**).
- **`python scripts/run_verification.py`:** **Overall: PASS** — `.buildlogs/verification/last_run.json`.

**Verify bar:** unchanged unless anchored to **`verify.ps1`** / intentional proof batch.

## Changelog

- **2026-04-25 (Tasks 339–348):** Bounded brief + dependency map; **`MainWindowToolCatalogShellBridge`** + launcher + diagnostics; **`MainWindow`** wiring; **`MainWindowToolCatalogShellBridgeTests`** + **`Gap008Slice10Tests`**; widened regression filter; **MAINWINDOW** / **CANONICAL_REGISTRY** / **STATE** sync; Slice 11 planning pointer; anti-sprawl subsection in decomposition plan.
