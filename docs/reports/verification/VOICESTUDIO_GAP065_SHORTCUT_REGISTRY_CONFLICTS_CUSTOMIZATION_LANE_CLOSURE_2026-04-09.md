# GAP-065 — Shortcut registry, conflicts, and customization lane closure

**Lane:** `GOV-VOICESTUDIO-GAP065-SHORTCUT-REGISTRY-CONFLICTS-CUSTOMIZATION-01`  
**Tracker:** GAP-065 **Closed**  
**Date:** 2026-04-09  

## 1. Scope delivered

- **Startup rehydration:** `MainWindow` content `Loaded` calls `await _keyboardShortcutService.InitializeAsync()` after theme init (ADR-047; try/catch + Debug log).
- **Context-aware conflicts:** `CheckForConflict` / `HasConflict` require same chord **and** same `ShortcutContext`; `IUnifiedKeyboardService` aligned; `TryRegisterShortcut` / `SetCustomShortcutAsync` use correct context.
- **Persistence JSON:** `ShortcutImport` uses `[JsonPropertyName("key")]` / `[JsonPropertyName("modifiers")]` so export/import round-trips (fixes `VirtualKey.None` on load).
- **Registration:** Panel code-behinds (Timeline, Assistant, WorkflowAutomation, EmbeddingExplorer) and `TransportShortcutCoordinator` use `TryRegisterShortcut` (intentional `playback.stop` override: `allowOverwrite: true`); handler registration overload where needed.
- **Customization UI:** `KeyboardCustomizationView` + `KeyboardCustomizationViewModel`; DI `AddTransient<KeyboardCustomizationViewModel>`; **Keyboard Shortcuts** flow: Primary **Customize…** opens customization dialog; `ConflictDetected` → inline row badge.
- **AutomationIds:** `KeyboardCustomization_SearchBox`, `KeyboardCustomization_ShortcutList`, `KeyboardCustomization_ResetAllButton` (see `AUTOMATION_ID_REGISTRY.md`).

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| Full App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3223** PASS / **274** skipped |
| GAP-065 targeted | `KeyboardShortcutServiceGap065Tests` + `KeyboardCustomizationViewModelTests` | **9** PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260409_182507/` |
| Rolling harness | `python scripts/run_verification.py` (inherited from Quick) | `.buildlogs/verification/last_run.json` **20260409-183025** (gate/ledger PASS; **completion_guard** skipped in Quick per harness) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260409_182507/`
- Verification JSON: `.buildlogs/verification/last_run.json` (`timestamp_short`: **20260409-183025**)
- Execution row (Closed): `docs/design/GOV_VOICESTUDIO_GAP065_SHORTCUT_REGISTRY_CONFLICTS_CUSTOMIZATION_01_EXECUTION_ROW.md`

## 4. Rollback

Revert the GAP-065 commit(s). Restores prior shortcut init, conflict semantics, and removes customization UI surface.
