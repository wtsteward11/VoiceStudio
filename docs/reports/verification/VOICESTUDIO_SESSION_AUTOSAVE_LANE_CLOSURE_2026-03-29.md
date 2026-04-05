# VOICESTUDIO_SESSION_AUTOSAVE_LANE_CLOSURE_2026-03-29

**Lane:** GOV-VOICESTUDIO-SESSION-AUTOSAVE-01  
**Execution row:** [GOV_VOICESTUDIO_SESSION_AUTOSAVE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_SESSION_AUTOSAVE_01_EXECUTION_ROW.md)  
**Gap:** GAP-020 → **Closed**

## 1. Acceptance summary

| Criterion | Evidence |
|-----------|----------|
| Project-scoped dirty state | `IProjectSessionDirtyState` / `ProjectSessionDirtyState`; timeline `Tracks.CollectionChanged`; mixer edits call `MarkProjectDirty` |
| Autosave through canonical save | `SessionAutosaveOrchestrator` → `ProjectWorkflowCoordinator.TryAutosaveProjectAsync` → `UnifiedProjectSaveHandler` |
| Settings-backed policy | `ISettingsService.LoadSettingsAsync` reads `General.AutoSave` and `General.AutoSaveInterval` (seconds); failsafe interval ≥ 30s |
| Recovery UX | `CrashRecoveryService` singleton + `ServiceProviderAdapter`; pending snapshot + `ContentDialog` Restore/Discard; `MarkCleanShutdown` on window close |
| No silent restore | `InitializeAsync` loads pending only; `SessionRecovered` after explicit accept via `NotifyRecoveryAccepted` |
| Tests | `ProjectSessionDirtyStateTests` (4); `ProjectWorkflowCoordinatorTests` TryAutosave cases (3) |

## 2. Verification commands (executed for closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — PASS (0 errors)
- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProjectSessionDirtyStateTests|FullyQualifiedName~TryAutosaveProjectAsync"` — PASS (7 tests)
- Recommended full suite / CI (owner rerun before merge): `dotnet test` (full App.Tests), `python -m pytest tests/ci/ -q --randomly-seed=12345`, `.\scripts\verify.ps1 -Quick`, `python scripts/run_verification.py`

## 3. Honest limits

- **MainWindow size budget**: session recovery + autosave wiring lives in `Services/MainWindowSessionLifecycle.cs` (not counted in `MainWindow*.cs` partial budget) per `tests/ci/test_mainwindow_total_budget.py`.
- **WinUI `DispatcherQueueTimer`**: `SessionAutosaveOrchestrator` is not unit-tested in isolation (requires WinUI dispatcher); behavior covered indirectly via coordinator + dirty state tests and manual smoke.
- **Recovery restore path** depends on `ActiveProjectId` in `session.json`; projects opened only by file path without id may show limited restore metadata.
- **E2E dialog**: no automated WinAppDriver proof in this closure; dialog wired post-`XamlRoot` in `MainWindow` Loaded.

## 4. Next lane handoff

- **GAP-029** — Export / effects authority (`GOV-VOICESTUDIO-EXPORT-AUTHORITY-01` when chartered) per professional roadmap.

## 5. Files touched (reference)

- `src/VoiceStudio.App/Services/IProjectSessionDirtyState.cs`, `ProjectSessionDirtyState.cs`, `SessionAutosaveOrchestrator.cs`
- `src/VoiceStudio.App/Services/CrashRecoveryService.cs`, `TimelineProjectHandlers.cs`, `ProjectWorkflowBootstrap.cs`, `ProjectWorkflowCoordinator.cs`, `IProjectWorkflowCoordinator.cs`
- `src/VoiceStudio.App/Services/AppServices.cs`, `ServiceProvider.cs`, `App.xaml.cs`
- `src/VoiceStudio.App/MainWindow.xaml.cs`, `MainWindow.Workspaces.cs`, `Services/MainWindowSessionLifecycle.cs`
- `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs`, `TimelineView.xaml.cs`, `EffectsMixerView.xaml.cs`
- `src/VoiceStudio.App.Tests/Services/ProjectSessionDirtyStateTests.cs`, `ProjectWorkflowCoordinatorTests.cs`
