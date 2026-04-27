# GAP-008 Slice 41 — MainWindow Help → Keyboard Shortcuts menu item wiring shell (bounded)

**Status:** Accepted (Tasks 233–240)  
**Date:** 2026-04-27  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 41”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 41** moving **Help → Keyboard Shortcuts** **menu item** wiring (**`Click`** handler + **`MainWindowKeyboardShortcutRegistrationDependencies`** “open shortcuts” callback) out of **`MainWindow`** into **`MainWindowKeyboardShortcutsMenuItemShellBridge`**; **`MainWindowKeyboardShortcutsShellBridge`** (**Slice 21**) **still owns** **`RunKeyboardShortcutsMenuFlowAsync`** (dialogs / ADR-047); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 20** deferred **`KeyboardShortcutsMenuItem_Click`** as menu/tool glue. Today **`MainWindow`** still declares **`private async void KeyboardShortcutsMenuItem_Click`** and invokes it from the keyboard-registration shortcut group. **Slice 41** extracts **only** that **wiring** so **`MainWindow`** has **no** named handler method for this item; **no** change to **`ContentDialog`** construction or shortcut sheet UX (**Slice 21** remains authoritative).

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowKeyboardShortcutsMenuItemShellBridge`** — ctor captures **`MainWindowKeyboardShortcutsShellBridge`** + **`Func<XamlRoot?>`** + **`Func<KeyboardCustomizationViewModel>`** + **`Func<IToastNotificationService?>`**; **`OnKeyboardShortcutsMenuItemClick`** (async void for **`RoutedEventHandler`**) and/or **`RunFlowAsync`** delegates to **`_keyboardShortcutsShellBridge.RunKeyboardShortcutsMenuFlowAsync`** | **RHVoice** / `engines/audio/rhvoice/` |
| **`MainWindow`** — **`readonly`** field **`_keyboardShortcutsMenuItemShellBridge`**; ctor **`new`** **immediately after** **`_keyboardShortcutsShellBridge = new ...`** / **before** **`_helpAboutShellBridge = new ...`**; **`_keyboardShortcutsMenuItem.Click +=`** bridge handler; **`MainWindowKeyboardShortcutRegistrationDependencies`** keyboard-shortcut callback invokes **`_keyboardShortcutsMenuItemShellBridge.OnKeyboardShortcutsMenuItemClick(_keyboardShortcutsMenuItem, new RoutedEventArgs())`** when menu item non-null; **delete** **`KeyboardShortcutsMenuItem_Click`** | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence |
| | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn / matrix theater |
| | **Tasks 103 / 113 / … / 240** — optional runtime appendix; **not** spine gates |
| | **`MainWindowKeyboardShortcutsShellBridge`** (**Slice 21**) — **no** edits to **`RunKeyboardShortcutsMenuFlowAsync`** body / dialog layout |
| | **`MainWindowKeyboardShortcutRegistrationShellBridge`** / **`MainWindowKeyboardShortcutKeyDispatchShellBridge`** (**Slices 36–38**) — **no** registration table or **`KeyDown`** merge |
| | **`MainWindowWindowActivatedLoggingShellBridge`** (**Slice 40**) / **`MainWindowSmokeStartupModeShellBridge`** (**Slice 39**) — **no** merge |
| | **Obsolete `SwitchToPanel`** **removal** — **OUT** |
| | **Other menu items** (`CustomizeToolbar`, `ManageWorkspaces`, …) — **OUT** |

## One bridge class name

**`MainWindowKeyboardShortcutsMenuItemShellBridge`**

## Dependency map (Tasks 233–234)

| Bucket | Content |
|--------|---------|
| **MainWindow** | **Delete** **`private async void KeyboardShortcutsMenuItem_Click`**. **`_keyboardShortcutsMenuItem.Click +=`** → **`_keyboardShortcutsMenuItemShellBridge.OnKeyboardShortcutsMenuItemClick`**. **`MainWindowKeyboardShortcutRegistrationDependencies`** last delegate (keyboard “show shortcuts” group): **replace** inline **`KeyboardShortcutsMenuItem_Click(...)`** with **`_keyboardShortcutsMenuItemShellBridge.OnKeyboardShortcutsMenuItemClick(_keyboardShortcutsMenuItem, new RoutedEventArgs())`** when **`_keyboardShortcutsMenuItem != null`**. **Field** **`_keyboardShortcutsMenuItemShellBridge`** **after** **`_keyboardShortcutsShellBridge`**. **Ctor:** **`_keyboardShortcutsMenuItemShellBridge = new MainWindowKeyboardShortcutsMenuItemShellBridge(_keyboardShortcutsShellBridge, () => this.Content?.XamlRoot, () => AppServices.GetRequiredService<KeyboardCustomizationViewModel>(), () => ServiceProvider.GetToastNotificationService())`** **after** **`MainWindowKeyboardShortcutsShellBridge Created`** checkpoint / **before** **`_helpAboutShellBridge = new ...`**. **`_keyboardShortcutsMenuItem`** creation + **`MainWindowMenuBarShellWire.KeyboardShortcutsMenuItem`** assignment **unchanged** (still **`MainWindow`**). |
| **Consumers** | **`MainWindowKeyboardShortcutsShellBridge`** — sole dialog/flow implementation. **`KeyboardCustomizationViewModel`** — from **`AppServices`**. **`IToastNotificationService`** — error toast path inside Slice 21 bridge. |
| **Async / ADR-047** | **`OnKeyboardShortcutsMenuItemClick`** remains **`async void`** (WinUI **`RoutedEventHandler`**); **`ConfigureAwait(true)`** on **`RunKeyboardShortcutsMenuFlowAsync`** chain (**same** as prior **`MainWindow`** handler). **No** ctor **`async`**. |
| **Side effects** | **UI dialogs** only when user opens shortcuts (unchanged semantics). |
| **Overlaps** | **Slice 21** — **all** **`ContentDialog`** / **`KeyboardShortcutsView`** / **`KeyboardCustomizationView`**. **Slice 41** — **menu item + shortcut entry** wiring only. **Slices 36–38** — **unchanged** surfaces. |
| **Deferred** | **Obsolete `SwitchToPanel`**; **other** Help/View menu handlers not in this slice; **attach** **`Click`** in a different order — **not** required unless file-pin tests fail. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 21** dialog implementation or **Slice 36** bulk registration logic.

## Alternatives not Slice 41

- **Move `RunKeyboardShortcutsMenuFlowAsync` into this bridge** — **rejected**; duplicates **Slice 21**.
- **Charter `SwitchToPanel` removal`** — **rejected** (blast radius; **Slice 24** pins).

## Acceptance

- **`MainWindow`**: **no** **`KeyboardShortcutsMenuItem_Click`** symbol; **`_keyboardShortcutsMenuItemShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice41Tests` + `MainWindowKeyboardShortcutsMenuItemShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line-2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 237–238)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (2026-04-27) |
| `dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice41Tests\|FullyQualifiedName~MainWindowKeyboardShortcutsMenuItemShellBridgeTests"` | **9 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **311/311** Passed; **`listedTestCount`** **311**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_213036.trx`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → **`.buildlogs/verification/last_run.json`** |

## Changelog

- 2026-04-27: **Tasks 237–238** — Verification table + green fixture + reconciliation + STATE proof; spine **311/311**.
- 2026-04-27: **Tasks 233–234** — **Accepted** charter + dependency map; bridge **`MainWindowKeyboardShortcutsMenuItemShellBridge`**.
