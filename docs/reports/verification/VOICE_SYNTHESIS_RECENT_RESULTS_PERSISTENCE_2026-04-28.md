# Voice Synthesis recent results persistence (2026-04-28)

## Scope

Persist the **last 5** voice synthesis recent results (newest-first) across panel deactivate/reactivate via existing **`IPanelStatePersistable`** / `PanelStateData.CustomData`, key **`VoiceSynthesis_RecentResults`**, storing a **JSON string** of DTOs (avoids `JsonElement` nesting issues on round-trip through `PanelStateService`).

## Files changed

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `CustomKeyRecentResults`, nested `RecentResultPersistDto`, `GetCurrentState` / `RestoreStateAsync` + `RestoreRecentResultsFromCustomData`; extended `CoerceCustomStateString` for `JsonElement`.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — **10** persistence-focused tests + helpers (`BuildRecentResultsPanelState`, `BuildRecentResultsPanelStateRawJson`).

## Behavior

- **Save:** When `RecentSynthesisResults.Count > 0`, serializes up to **5** entries to JSON and sets `CustomData["VoiceSynthesis_RecentResults"]`.
- **Restore:** Deserializes; skips rows with neither `AudioId` nor `AudioReference`; caps at **5**; tolerates missing/malformed JSON (logs `Debug`, no throw); `CreatedAtUtc` stored as ISO 8601 UTC, restored to local time or `DateTime.Now` if invalid.
- **Not persisted:** Playback error fields, loading flags, `StatusMessage` (DTO excludes them).

## Test results

- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"` — **132 passed** (includes persistence region + prior Voice Synthesis tests; **22** skipped UI/E2E patterns).
- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors**.
- `python scripts/run_verification.py` — **Overall: PASS** (`.buildlogs/verification/last_run.json`).
- `.\scripts\verify.ps1 -Quick` — **exit 0** — `artifacts/verify/20260428_024458/verification_report.md`.

## Limitations

- In-process **collection** semantics: persistence follows **panel state** save/restore (same pipeline as other GAP-050 custom keys). No separate on-disk schema file; DTO shape is **not** versioned in a shared schema—future changes need migration if the JSON contract must stay backward-compatible.

## Non-claims

- **Not** GAP-008; **not** Slice 46; **not** `MainWindow*ShellBridge`.
- **Not** RHVoice; **not** `ENGINE_PARITY_MATRIX` edits.
- **Not** a **runtime FULL PASS** or human in-app attestation.
