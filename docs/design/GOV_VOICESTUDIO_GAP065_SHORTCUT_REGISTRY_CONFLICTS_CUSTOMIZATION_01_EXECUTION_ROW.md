# GOV-VOICESTUDIO-GAP065-SHORTCUT-REGISTRY-CONFLICTS-CUSTOMIZATION-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP065-SHORTCUT-REGISTRY-CONFLICTS-CUSTOMIZATION-01 |
| **GAP** | GAP-065 (Shortcut registry authority, context-aware conflicts, customization UI, startup rehydration) |
| **Status** | **Closed** |
| **Phase** | Bounded execution row — UI + services seam |
| **Role** | UI Engineer + Core Platform (keyboard seam) |

## §1 Objective (frozen)

Deliver **shortcut registry authority**, **context-aware conflict detection**, **user customization UI**, and **startup rehydration** by closing dead-code/broken-path gaps in `KeyboardShortcutService` and adding a minimal customization View/ViewModel layer.

## §2 Hard IN

- Fix all five broken paths: (1) call `InitializeAsync()` from `MainWindow` content `Loaded` after theme init; (2) customization UI wired to `SetCustomShortcutAsync` / reset APIs; (3) `CheckForConflict` / `HasConflict` only flag **same chord + same `ShortcutContext`**; (4) panel registrations and transport use **`TryRegisterShortcut`** (with logging on failure; intentional overrides use `allowOverwrite: true` where required); (5) **`ConflictDetected`** subscribed in customization ViewModel with inline UI surface.
- Minimal customization UI: browse, search, rebind (chord capture), per-row reset, reset all (with confirm), conflict badge on row.
- Persistence path: `%AppData%\VoiceStudio\shortcuts.json` via existing service APIs.

## §3 Hard OUT

- Backend shortcut sync beyond existing `KeyboardShortcutsClient` usage; command system rewrite; mouse/gesture customization; full settings redesign; `CommandDescriptor.DefaultHotkey` string parser; workspace-definition shortcut strings.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Binding state, persistence, conflict detection | `KeyboardShortcutService` |
| UI state (search, edit row, chord capture) | `KeyboardCustomizationViewModel` |
| Startup rehydration call site | `MainWindow` content **`Loaded`** (after `IUnifiedThemeService.InitializeAsync`, ADR-047) |
| Transport override registration | `TransportShortcutCoordinator` via `TryRegisterShortcut` |

## §5 Acceptance criteria

- [x] `KeyboardShortcutService.InitializeAsync()` invoked from **`MainWindow` Loaded** only (not constructor); try/catch + Debug log on failure.
- [x] `CheckForConflict` / `HasConflict` are **context-aware**; `IUnifiedKeyboardService.CheckForConflict` signature updated accordingly.
- [x] Panel code-behinds + `TransportShortcutCoordinator` use **`TryRegisterShortcut`**; failures logged; intentional overrides use **`allowOverwrite: true`** where specified.
- [x] `KeyboardCustomizationView` + `KeyboardCustomizationViewModel` registered in DI; entry point from **Keyboard Shortcuts** flow (e.g. “Customize…” secondary dialog).
- [x] `ConflictDetected` subscribed in ViewModel; inline conflict indication in View.
- [x] MSTests: context-aware conflicts, persistence round-trip, ViewModel behaviors, Loaded-vs-constructor seam scan for shortcut init.
- [x] Build + App.Tests + `verify.ps1 -Quick` GREEN; closure report, tracker, STATE, registry, AutomationIds.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~KeyboardCustomization"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~KeyboardShortcut"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register (summary)

| Risk | Mitigation |
|------|------------|
| Chord capture swallows input | `PreviewKeyDown`, cancel on Escape / focus loss |
| Panel + global same physical key | Prefer `ShortcutContext.Panel` for panel-registered chords that overlap globals |

## §8 Rollback order

1. `MainWindow` Loaded shortcut init + menu entry  
2. New View/ViewModel + DI  
3. `KeyboardShortcutService` conflict API + `TryRegisterShortcut` handler overload  
4. Panel + transport registration edits  
5. Tests + governance artifacts  
