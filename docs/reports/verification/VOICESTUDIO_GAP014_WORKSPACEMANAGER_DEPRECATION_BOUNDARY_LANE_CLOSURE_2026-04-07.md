# GAP-014 — WorkspaceManager deprecation / workspace authority boundary lane closure

**Lane:** `GOV_VOICESTUDIO_GAP014_WORKSPACEMANAGER_DEPRECATION_BOUNDARY_01`  
**Tracker:** GAP-014 **Closed** — single runtime workspace authority (`PanelStateService` + `MainWindow` orchestrator); parallel `IWorkspaceService`/`ILayoutService` DI removed; deprecated `Features/Workspaces/WorkspaceManager` type removed  
**Date:** 2026-04-07

## 1. Scope delivered

- **`AppServices`:** Removed `AddSingleton<ILayoutService, LayoutService>()` and `AddSingleton<IWorkspaceService>(...)`; removed `GetLayoutService` / `TryGetLayoutService` / `GetWorkspaceService` / `TryGetWorkspaceService` so no second runtime owner can be resolved from the shell container.
- **Deleted:** `src/VoiceStudio.App/Features/Workspaces/WorkspaceManager.cs` (deprecated legacy type; **not** `WorkspaceManagerDialog`, which remains and delegates to `PanelStateService`).
- **Quarantine (source retained):** `WorkspaceService.cs` / `LayoutService.cs` headers document **not DI-registered** after GAP-014; retained for `DefaultPresets` / tests.
- **Comments:** `IUnifiedWorkspaceService.cs`, `LockOrderValidator.cs` aligned with GAP-014 narrative.
- **Tests:** `WorkspaceAuthoritySeamTests` (**4**) — source contract for DI registration, static accessors, deleted file path, `PanelStateService` : `IUnifiedWorkspaceService`.

## 2. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings only) |
| Targeted MSTest (workspace slice) | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` with filter matching `WorkspaceAuthoritySeamTests`, `PanelStateServiceTests`, `PanelRestoreEndToEndTests`, `WorkspaceRoundTripTests`, `WorkspaceDefinitionTests`, `WorkspaceConfigurationTests` (GAP-014 plan §8) | **61** PASS |
| GAP-013 regression | `dotnet test ... --filter "FullyQualifiedName~PanelHost"` | **7** PASS |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS (**2** deselected) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_232856/` |
| Ledger / guard | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` `timestamp_short` **20260406-233651** (**completion_guard** PASS, post-commit) |

## 3. Proof pointers

- Quick verify folder: `artifacts/verify/20260406_232856/`
- Verification JSON: `.buildlogs/verification/last_run.json` — **`timestamp_short` `20260406-233651`** (post-commit `run_verification.py`, **completion_guard** PASS); mirrored in **`.cursor/STATE.md`** **Latest verify artifact**.

## 4. Rollback

Revert the GAP-014 commit(s). Restores `WorkspaceManager.cs` and DI registrations only if rollback is required; prefer forward fix over partial restore.
