# Workspace restore / EffectsMixer panel load failure — product hotfix proof

**Working title:** `WORKSPACE_RESTORE_EFFECTS_MIXER_FAILURE` (not GAP-008; no new `MainWindow*ShellBridge`).

## Symptom

- Workspace restore could fail to load **EffectsMixer** (and other panels) with a generic **TargetInvocationException** message in the panel error UI.
- Multiple **Partial Restore Failed** / **Restore Failed** toasts (startup + profile-changed restore).
- Toast action button mislabeled **View Details** when the action was **reset workspace**.

## Root cause (code)

1. **PanelRegistry** always assigned **ViewModelFactory** output to `DataContext` when `ViewModelType` was set. **EffectsMixerView** constructs **EffectsMixerViewModel** in its constructor and sets `DataContext` first; replacing it with a second VM from the factory caused a conflict/failure (surfaced as **TargetInvocationException**).
2. **PanelHost** showed `ex.Message` without unwrapping inner exceptions.
3. **ToastNotificationService** hardcoded **View Details** for any `ShowError` with an action.
4. **MainWindow** restore paths could fire **duplicate** toasts with the same **(title, message)** in a short window.

## Fix summary

| Area | Change |
|------|--------|
| `PanelRegistry.CreatePanel` | If `DataContext` is already non-null (view-owned VM), **skip** factory assignment. |
| `ExceptionDiagnostics` + `PanelHost` | Unwrap **TargetInvocationException** / single-inner **AggregateException**; user string `Failed to create/load panel '{id}': {RootType}: {Message}`; append full exception to `%LocalAppData%\VoiceStudio\crashes\panel_load_failure_diag.txt`. |
| `IToastNotificationService` / `ToastNotificationService` | Optional `actionButtonLabel` on `ShowError`; default **View Details** when action non-null and label null. |
| `MainWindow.Workspaces` | Restore toasts use **Reset to Studio**; **5s** dedupe on identical **(title, message)** via `WorkspaceRestoreFailureToastSuppressor`. |

## Files touched (see commit)

- `src/VoiceStudio.App/Services/PanelRegistry.cs`
- `src/VoiceStudio.App/Diagnostics/ExceptionDiagnostics.cs`
- `src/VoiceStudio.App/Services/WorkspaceRestoreFailureToastSuppressor.cs`
- `src/VoiceStudio.App/Controls/PanelHost.xaml.cs`
- `src/VoiceStudio.App/Services/ToastNotificationService.cs`
- `src/VoiceStudio.App/MainWindow.Workspaces.cs`
- `src/VoiceStudio.App.Tests/Diagnostics/ExceptionDiagnosticsTests.cs`
- `src/VoiceStudio.App.Tests/Services/WorkspaceRestoreFailureToastSuppressorTests.cs`
- `src/VoiceStudio.App.Tests/Services/*ShellBridge*Tests.cs` (Moq `ShowError` arity)
- `src/VoiceStudio.App.Tests/Services/SearchOverlayCoordinatorTests.cs`, `ProjectWorkflowCoordinatorTests.cs` (interface stub)

## Automated tests

- `ExceptionDiagnosticsTests` — unwrap + format strings.
- `WorkspaceRestoreFailureToastSuppressorTests` — dedupe within window; distinct message not suppressed.
- Shell bridge tests updated for new `ShowError` optional parameter.

**Note:** Full `CreatePanel` for WinUI `UserControl` in MSTest requires an app host; **EffectsMixer** path is covered by product code + this doc, not a headless `CreatePanel` test.

## Verification commands (this session)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — 0 errors.
- `python scripts/run_verification.py` — **PASS**; `.buildlogs/verification/last_run.json`.
- `.\scripts\verify.ps1 -Quick` — **PASS**; `artifacts/verify/20260427_181658/verification_report.md`.

## Manual verification (operator)

- Backend up; launch app; trigger workspace that includes EffectsMixer; confirm panel loads or overlay shows **root** exception type/message (not only TargetInvocationException).
- At most one duplicate-suppressed **Partial Restore** toast for same **(title, message)** within ~5s.
- Toast button reads **Reset to Studio** (not **View Details**) for restore actions.
- Optional: open `%LocalAppData%\VoiceStudio\crashes\panel_load_failure_diag.txt` after a forced failure — full exception present.

## Verdict

**Fix landed in repo** (diagnostics, toast labels, dedupe, **EffectsMixer** `DataContext` preservation). **Runtime / in-app** operator attestation is **separate** from this doc; product runtime truth for synthesis remains **PARTIAL** per [VOICESTUDIO_RUNTIME_TRUTH_FOLLOWUP_WINUI_PIPER_2026-04-27.md](VOICESTUDIO_RUNTIME_TRUTH_FOLLOWUP_WINUI_PIPER_2026-04-27.md) until a human session completes the Voice Synthesis UI path.

**Not** GAP-008 Slice 46. **Not** a claim of full `verify.ps1` FULL without machine-local run.

## Manual verification after push

| Field | Value |
|------|--------|
| **Commit SHA** | `eda493ce9487b72cc43bd97e1f7d113ecd95d834` |
| **Operator / date** | **Cursor agent session (no human WinUI operator)** — **2026-04-27** (agent log; in-app attestation not performed) |
| **Backend health** | `GET http://127.0.0.1:8000/api/health` → **200**; `engines_ready` **true**; `version_info.git_commit` **`b0a1b793`** (Python backend process; does not match C# `HEAD` **eda493ce** — expected: hotfix is WinUI-only; backend ref independent) |
| **App launch (hotfix build)** | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors**, 5 pre-existing nullable warnings. Expected EXE: `src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe` (path verified on disk). **App not launched in this session** (no human to operate UI / capture PID, title, screenshot). |
| **EffectsMixer result** | **N/A (deferred)** — requires human to open Right panel / restore workspace and confirm load or root exception in overlay. |
| **Duplicate toast result** | **N/A (deferred)** — requires human to trigger restore-failure paths within ~5s. |
| **Reset to Studio button result** | **N/A (deferred)** — requires human to observe restore-failure toast. |
| **panel_load_failure_diag.txt** | **N/A (deferred)** — not inspected (no new failure forced in this session). |
| **DataContext spot-check (Library, Voice Synthesis, Profiles)** | **N/A (deferred)** — requires in-app navigation. |
| **Verdict** | **DEFERRED** — **automation preflight only** (repo clean except allowed dirty files, build green, backend healthy). **Not** **PASS** / **PARTIAL** / **FAIL** for the in-app checklist until a human runs Phases 4–5. |

## Related

- GAP-008: remains **strategically frozen** for new `MainWindow*ShellBridge` (see `.cursor/STATE.md`).
