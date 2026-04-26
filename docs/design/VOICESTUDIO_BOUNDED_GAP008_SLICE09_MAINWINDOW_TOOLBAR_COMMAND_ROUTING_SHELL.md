# GAP-008 Slice 9 — MainWindow toolbar command routing shell (bounded)

**Status:** Accepted (Tasks 329–338; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 8](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md)

**Supersedes prior planning:** Slice 9 was briefly documented as **tool catalog** (`ShowToolCatalogAsync`) in Slice 8 / MAINWINDOW planning text — **this brief is authoritative**: Slice 9 is **toolbar import shell routing** only. Tool catalog → **Slice 10** (**landed** — [Slice 10 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md); see § Slice 10).

## First seam (exact)

**Shell-only wiring** so toolbar **import audio** does not use **`App.MainWindowInstance`**:

- **`IToolbarShellImportFromToolbar`** — DI-facing port; implemented by **`MainWindowToolbarCommandShellBridge`**.
- **`ToolbarViewModel.ExecuteToolbarActionAsync`** — for **`import_audio`**, calls **`_toolbarShellImport.RequestImportAudio()`** (no `Action` callback from [`CustomizableToolbar`](../../src/VoiceStudio.App/Controls/CustomizableToolbar.xaml.cs)).
- **`MainWindow`** ctor — resolves **`MainWindowToolbarCommandShellBridge`**, calls **`WireImportAudioHandler(ImportAudioFile)`** once after shell composition.
- **Fallback path** when **`ToolbarViewModel`** is null in **`CustomizableToolbar`**: unchanged (keyboard shortcut service for `play` / `pause` / `stop` / `record` / `undo` / `redo` only); **no** `import_audio` in fallback map — **contract:** import requires VM + wired shell (fail-closed if user could reach import without wire: prevented by normal app init ordering).

**Types:** [`MainWindowToolbarCommandShellBridge`](../../src/VoiceStudio.App/Services/MainWindowToolbarCommandShellBridge.cs); [`IToolbarShellImportFromToolbar`](../../src/VoiceStudio.App/Services/IToolbarShellImportFromToolbar.cs).

## In scope (explicit symbol / itemId list)

| Symbol / `itemId` | Role |
| ----------------- | ---- |
| **`import_audio`** | Routed via **`ToolbarViewModel`** → **`IToolbarShellImportFromToolbar.RequestImportAudio()`** |
| **`MainWindowToolbarCommandShellBridge.WireImportAudioHandler`** | **`MainWindow`** registers **`ImportAudioFile`** delegate |
| **`MainWindowToolbarCommandShellBridge.RequestImportAudio`** | Invokes wired handler; throws **`InvalidOperationException`** if not wired |
| **`ToolbarViewModel`** ctor | Accepts **`IToolbarShellImportFromToolbar`** |
| **`ExecuteToolbarActionAsync(string itemId)`** | **`import_audio`** branch uses port only (callback parameter removed) |
| **`CustomizableToolbar.HandleToolbarButtonClick`** | Calls **`ExecuteToolbarActionAsync(itemId)`** without **`App.MainWindowInstance`** |

**Not expanded this slice:** `play`, `pause`, `stop`, `record`, `undo`, `redo`, `loop` remain in **`ToolbarViewModel`** + command registry (no move to bridge).

## Explicitly NOT Slice 9 (deferred)

| Cluster | Deferred to | Notes |
| ------- | ----------- | ----- |
| **`MainWindowToolbarCustomizationShellBridge`** | [Slice 7](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md) | **Zero** in Slice 9 bridge (`Gap008Slice9Tests`) |
| **`MainWindowCommandPaletteShellBridge`** / palette | [Slice 8](VOICESTUDIO_BOUNDED_GAP008_SLICE08_MAINWINDOW_COMMAND_PALETTE_SHELL.md) | Same creep gate |
| **Search overlay** | [Slice 6](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md) | Same |
| **`ShowToolCatalogAsync`** / **`nav.toolcatalog`** | [Slice 10](VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md) (**landed**) | Prior “Slice 9 = tool catalog” docs **superseded** |
| **Library / menu** `ImportAudioFile` call sites | Unchanged | Not toolbar routing |
| **`engines/audio/rhvoice/`** | **Frozen** (Task 338) | **Zero** edits |

## Dependency / blast-radius map (Task 330)

| Responsibility | Current owner (pre–Slice-9) | Target after Slice 9 | Risk | Tests |
| -------------- | --------------------------- | ---------------------- | ---- | ----- |
| Toolbar import → `MainWindow` | **`App.MainWindowInstance`** + callback | **`IToolbarShellImportFromToolbar`** + **`WireImportAudioHandler`** | H (static coupling removal) | Moq port + **`ToolbarViewModelTests`** |
| Toolbar play/stop/undo… | **`ToolbarViewModel`** + registry | **Unchanged** | L | Existing VM tests |
| VM null fallback in control | **`CustomizableToolbar`** + keyboard | **Unchanged** (no import in fallback) | M | Documented contract |
| DI registration | **`AppServices`** | **`MainWindowToolbarCommandShellBridge`** + VM ctor | M | App boot implicit |

## Unknown toolbar `itemId` policy

**Locked:** Unregistered command IDs remain **silent no-op** in **`ToolbarViewModel`** (existing `IsRegistered` guard). Slice 9 does **not** add toast/log for unknown ids.

## Slice 8 follow-on (Task 333) — palette failure diagnostics

**Observable logging:** [`MainWindowCommandPaletteShellBridge`](../../src/VoiceStudio.App/Services/MainWindowCommandPaletteShellBridge.cs) uses injectable **`ICommandPaletteShellDiagnostics`** (default [`CommandPaletteShellErrorDiagnostics`](../../src/VoiceStudio.App/Services/CommandPaletteShellErrorDiagnostics.cs) → **`ErrorLogger`**). Tests verify diagnostics + toast + null-toast.

## Slice 10 — tool catalog shell (Tasks 339–348; landed)

**Authoritative brief:** [VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md](VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md) — **`MainWindowToolCatalogShellBridge`**, launcher, diagnostics, **`WireToolCatalogHandlers`**, tests **`MainWindowToolCatalogShellBridgeTests`** + **`Gap008Slice10Tests`**.

**Next (Slice 11):** **Welcome / startup** (`MainWindow_Activated`, `_welcomeDialogShown`) — **planning only** until chartered ([MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)).

**No code** in Tasks 329–338 for Slice 10 (Slice 10 ships in **Tasks 339–348**).

## RHVoice (Task 338)

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice does not reorder GAP-008.

## Narrow-seam rule (Task 337)

**`MainWindowToolbarCommandShellBridge`** owns **toolbar import audio routing from shell** only — not customization, not palette, not search overlay, not tool catalog, not command registry internals.

## Verification (Task 334 — fill after green)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowToolbarCommandShellBridgeTests|FullyQualifiedName~Gap008Slice9Tests|FullyQualifiedName~ToolbarViewModelTests|FullyQualifiedName~MainWindowCommandPaletteShellBridgeTests|FullyQualifiedName~Gap008Slice8Tests|FullyQualifiedName~MainWindowToolbarCustomizationShellBridgeTests|FullyQualifiedName~Gap008Slice7Tests|FullyQualifiedName~MainWindowSearchOverlayShellBridgeTests|FullyQualifiedName~Gap008Slice6Tests|FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~Gap008Slice5Tests" -v q
python scripts\run_verification.py
```

**Results (2026-04-25):**

- **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`:** **0 Error(s)** (warnings only; pre-existing in test project).
- **`dotnet test` (filter above):** **Passed: 60**, Failed: 0 — includes Slice 9 bridge + **`Gap008Slice9Tests`** (incl. toolbar **`MainWindowInstance`** creep guard) + **`ToolbarViewModelTests`**, Slice 8 palette tests + **`Gap008Slice8Tests`**, and Slice 5–7 regression tests in filter.
- **`python scripts/run_verification.py`:** **Overall: PASS** — `.buildlogs/verification/last_run.json`.

**Verify bar:** unchanged unless anchored to **`verify.ps1`** / intentional proof batch.

## Changelog

- **2026-04-25 (Tasks 339–348 follow-on):** Slice 10 tool catalog **landed** — pointer updates in this brief; Slice 11 welcome/startup = planning only.
- **2026-04-25:** Tasks 329–338 — brief + **`MainWindowToolbarCommandShellBridge`** + **`IToolbarShellImportFromToolbar`** + **`ToolbarViewModel`** / **`CustomizableToolbar`** / **`MainWindow`** / **`AppServices`** + tests + Slice 8 **`ICommandPaletteShellDiagnostics`** + docs/STATE/registry + Slice 10 planning + narrow-seam row.
