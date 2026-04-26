# GAP-008 Slice 8 — MainWindow command palette shell glue (bounded)

**Status:** Accepted (Tasks 319–328; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 7](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md)

## First seam (exact)

**Shell-only delegation** for opening the **command palette** from the shell:

- **Keyboard:** **`nav.commandpalette`** (Ctrl+P) — **`RegisterKeyboardShortcuts`** lambda forwards to **`ShowCommandPalette()`** (same entry as any future menu path).
- **Entry:** **`MainWindow.ShowCommandPalette`** — thin forward to **`MainWindowCommandPaletteShellBridge.Show()`**.
- **Orchestration:** Bridge resolves **`IPanelRegistry`** and **`ThemeManager`** via injected factories (production: **`ServiceProvider.GetPanelRegistry()`**, **`new ThemeManager()`**), invokes injectable **`ICommandPaletteShellLauncher`** (production: **`CommandPaletteShellLauncher`** → **`CommandPaletteService.Show()`**).
- **Failure:** Empty **`catch`** removed — **`ICommandPaletteShellDiagnostics`** (default → **`ErrorLogger.LogError`**) + **`IToastNotificationService.ShowError`** when launcher throws (no silent swallow).

**Types:** [`MainWindowCommandPaletteShellBridge`](../../src/VoiceStudio.App/Services/MainWindowCommandPaletteShellBridge.cs); [`ICommandPaletteShellLauncher`](../../src/VoiceStudio.App/Services/ICommandPaletteShellLauncher.cs); [`CommandPaletteShellLauncher`](../../src/VoiceStudio.App/Services/CommandPaletteShellLauncher.cs); [`ICommandPaletteShellDiagnostics`](../../src/VoiceStudio.App/Services/ICommandPaletteShellDiagnostics.cs); [`CommandPaletteShellErrorDiagnostics`](../../src/VoiceStudio.App/Services/CommandPaletteShellErrorDiagnostics.cs).

## In scope (explicit symbol list)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`ShowCommandPalette`** | Single **`MainWindow`** entry; forwards to **`_commandPaletteShellBridge.Show()`** |
| **`nav.commandpalette`** / Ctrl+P registration | Lambda → **`ShowCommandPalette()`** (same bridge path) |
| **`MainWindowCommandPaletteShellBridge`** | Registry/theme factories + launcher + logged + toasts on failure |
| **`ICommandPaletteShellLauncher`** / **`CommandPaletteShellLauncher`** | Test seam; production wraps **`CommandPaletteService`** |

**Inventory (grep):** **`ShowCommandPalette`** — **`MainWindow.xaml.cs`** only. **`CommandPalette`** / **`CommandPaletteService`** / **`CommandPaletteWindow`** — implementation unchanged. **`nav.commandpalette`** — **`MainWindow`** keyboard registration. **`view.commandPalette`** / **`tools.commandPalette`** — **`CommandIds`** + **`KeyboardShortcutService`** (`tools.commandPalette` uses Ctrl+Shift+P elsewhere); **not** rewired in this slice unless already calling **`ShowCommandPalette`** (none found). **Menu:** no separate “Command Palette” menu item calling **`ShowCommandPalette`** in this batch — Ctrl+P remains the primary shell path.

## Explicitly NOT Slice 8 (deferred)

| Cluster | Deferred to | Notes |
| ------- | ----------- | ----- |
| **`MainWindowSearchOverlayShellBridge`** / global search | [Slice 6](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md) | **Zero** references in Slice 8 bridge source (`Gap008Slice8Tests`) |
| **`MainWindowToolbarCustomizationShellBridge`** / customize toolbar | [Slice 7](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md) | Same creep gate |
| **Project / recent / import / transport / nav-bridge internals** | Prior slices | No expansion |
| **`ShowToolCatalogAsync`** / Ctrl+Shift+T | [Slice 10](VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md) (**landed**) | Bounded brief + bridge; not in palette bridge |
| **`CommandPaletteService`** / **`CommandPaletteWindow`** / VM internals | Unchanged | Brain stays in service + views |
| **`engines/audio/rhvoice/`** | **Frozen** (Task 328) | **Zero** edits |

## Dependency / blast-radius map (Task 320)

| Responsibility | Current owner | Target after Slice 8 | Risk | Tests |
| -------------- | ------------- | ---------------------- | ---- | ----- |
| Open palette (Ctrl+P) | **`MainWindow.ShowCommandPalette`** + inline **`CommandPaletteService`** | Thin → **`MainWindowCommandPaletteShellBridge`** | M (window lifetime in real **`Show`**) | Moq **`ICommandPaletteShellLauncher`** |
| Palette implementation | **`CommandPaletteService`** + window | **Unchanged** | — | None required this slice |
| Error on open failure | Empty **`catch`** | **`ErrorLogger`** + **`ShowError`** | **H** (policy) | Launcher throws → assert toast + verify **`Show`** invoked |
| Coupling to search/toolbar | None desired | **None** (grep gate) | M | **`Gap008Slice8Tests`** forbidden strings |

## Slice 9 (superseded planning note — authoritative: toolbar routing)

**Chartered and landed:** [Slice 9 — Toolbar command routing shell](VOICESTUDIO_BOUNDED_GAP008_SLICE09_MAINWINDOW_TOOLBAR_COMMAND_ROUTING_SHELL.md) (**Tasks 329–338**). Prior text here that named **Slice 9 = tool catalog** is **obsolete**.

**Slice 10 (landed, Tasks 339–348):** **Tool catalog** — [VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md](VOICESTUDIO_BOUNDED_GAP008_SLICE10_MAINWINDOW_TOOL_CATALOG_SHELL.md); see [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) § Slice 11 planning.

## RHVoice (Task 328)

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice remains **frozen** / **operator-gated**; does not reorder GAP-008.

## Narrow-seam rule (Task 327)

**`MainWindowCommandPaletteShellBridge`** owns **command palette open from shell** only — not toolbar customization, not search overlay, not tool catalog, not **`OpenPanelByIdAsync`**. A second unrelated responsibility requires a **new bounded brief + tests**; no drive-by additions.

## Follow-up: UI-host coverage (testing debt) — search overlay (Task 323)

Real **`FrameworkElement.Visibility`** collapse for **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** is covered by **unit tests + text pins** only. A dedicated **`[UITestMethod]`** was **not** retained — the WinUI test host **crashed** in this repo’s default vstest configuration (see [Slice 7 brief § Verification](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md) Task 313 note). **Done** for this debt means: stable WinUI UI test process, a single STA harness test, or an integration checklist — tracked in [Slice 6 brief § Follow-up](VOICESTUDIO_BOUNDED_GAP008_SLICE06_MAINWINDOW_SEARCH_OVERLAY_SHELL.md) and **MAINWINDOW_DECOMPOSITION_PLAN** testing-debt bullet.

## Verification (Task 324 — fill after green)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowCommandPaletteShellBridgeTests|FullyQualifiedName~Gap008Slice8Tests|FullyQualifiedName~MainWindowToolbarCustomizationShellBridgeTests|FullyQualifiedName~Gap008Slice7Tests|FullyQualifiedName~MainWindowSearchOverlayShellBridgeTests|FullyQualifiedName~Gap008Slice6Tests|FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~Gap008Slice5Tests" -v q
python scripts\run_verification.py
```

**Results (2026-04-25):**

- **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`:** **0 Error(s)** (warnings only; pre-existing in test project).
- **`dotnet test` (filter above):** **Passed: 49**, Failed: 0 — includes **`MainWindowCommandPaletteShellBridgeTests`** + **`Gap008Slice8Tests`**, Slice 7 toolbar tests + **`Gap008Slice7Tests`**, Slice 6 search overlay tests + **`Gap008Slice6Tests`**, **`SearchOverlayCoordinatorTests`**, **`Gap008Slice5Tests`**.
- **`python scripts/run_verification.py`:** **Overall: PASS** — `.buildlogs/verification/last_run.json`.

**Verify bar:** unchanged unless anchored to **`verify.ps1`** / intentional proof batch.

## Changelog

- **2026-04-25 (Tasks 339–348 follow-on):** Slice 10 tool catalog **landed** — deferred-row and §Slice 10 pointer updated to bounded brief.
- **2026-04-25 (Tasks 329–338 follow-on):** **`ICommandPaletteShellDiagnostics`** + default **`CommandPaletteShellErrorDiagnostics`**; palette failure path observable in tests; Slice 9 doc pointer updated (toolbar routing landed; tool catalog → Slice 10 planning, now landed).
- **2026-04-25:** Tasks 319–328 — brief + **`MainWindowCommandPaletteShellBridge`** + launcher seam + tests + **`MainWindow`** wiring + Slice 6 / MAINWINDOW testing-debt notes + docs/STATE/registry + Slice 9 planning + narrow-seam one-liners.
