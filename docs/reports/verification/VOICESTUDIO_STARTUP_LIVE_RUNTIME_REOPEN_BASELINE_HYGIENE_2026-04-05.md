# Startup live runtime reopen — baseline hygiene (2026-04-05)

**Closure:** [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md](VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md)

## Clean rebuild

- `dotnet clean VoiceStudio.sln -c Debug -p:Platform=x64`
- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors** (warnings pre-existing in repo).

## `selectedTextWithDiagnostics` (Gemini / XAML clue)

- After rebuild, search: `src/VoiceStudio.App/obj/**/*.cs` → **no matches** for `selectedTextWithDiagnostics`.
- Treat prior clue as **not reproduced** in current generated output; no generator change required for this lane.

## Local crash/log hygiene

- Operator step (not run in agent): archive then clear `%LOCALAPPDATA%\VoiceStudio\crashes` and `%LOCALAPPDATA%\VoiceStudio\logs` before manual icon-launch capture if needed.
