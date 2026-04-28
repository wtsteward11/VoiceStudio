# Voice Synthesis recent results management (2026-04-28)

## Scope

Add **remove one** and **clear all** for the Voice Synthesis **recent generated audio** mini-list (in-memory + existing `IPanelStatePersistable` serialization). **No** disk deletion, **no** backend calls. Active synthesis output (`LastSynthesizedAudioId` / `LastSynthesizedAudioUrl`, workflow, playback diagnostics) is **not** cleared unless the user explicitly **Use**s another history entry.

## Files changed

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `RemoveRecentResultCommand`, `ClearRecentResultsCommand`, `RemoveRecentResult` / `ClearRecentResults`, `NotifyRecentSynthesisResultsChanged`.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` — header **Clear recent**; per-row **Remove** beside **Use**; automation IDs.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — **12** management tests (remove/clear behavior, persistence key, `CanExecute`, active output preservation).
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — `VoiceSynthesisView_ClearRecentResultsButton`, `VoiceSynthesisView_RemoveRecentResultButton`.

## Behavior

- **Remove:** Removes a single list entry by reference; no-op for null or an object not in the collection; clears `SelectedRecentResult` if it was the removed row.
- **Clear:** Clears the collection and selection; does **not** clear last synthesized IDs/URLs or playback error state.
- **Persistence:** Unchanged contract — `GetCurrentState` still omits `VoiceSynthesis_RecentResults` when the list is empty; partial list serializes after mutations.

## Test results

- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"` — **144 passed** (**22** skipped UI/E2E patterns).
- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (warnings may exist outside touched scope).
- `python scripts/run_verification.py` — **Overall: PASS** (`.buildlogs/verification/last_run.json`).
- `.\scripts\verify.ps1 -Quick` — **exit 0** — `artifacts/verify/20260428_034057/verification_report.md`.

## Limitations

- **Remove/Clear** only affect the **history list**; they do not delete files or revoke server-side artifacts.
- UI uses **Use** to make a history row the active result; clearing history does not remove the last successful synthesis from the “generated audio” affordances.

## Non-claims

- **Not** GAP-008; **not** Slice 46; **not** any new `MainWindow*ShellBridge`.
- **Not** RHVoice; **not** `ENGINE_PARITY_MATRIX` edits.
- **Not** a **runtime FULL PASS** or human in-app attestation.
- **Not** file deletion: remove/clear are list-only operations.
