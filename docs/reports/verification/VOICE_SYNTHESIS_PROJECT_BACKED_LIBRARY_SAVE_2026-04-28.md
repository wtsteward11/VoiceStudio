# Voice Synthesis Project-Backed Library Save — Verification Report

**Date:** 2026-04-28  
**Scope:** Durable library upload and optional project association for generated synthesis output when a **local file path** is available; typed outcomes (`GeneratedAudioSaveKind`); preserve **API-only** `AssetAddedEvent` path.

## Scope

- Extend `GeneratedAudioLibraryService` with `ILibraryClient`, `IContextManager`, `IProjectAudioClient`, optional `IErrorLoggingService`.
- Add `GeneratedAudioSaveKind` and expanded `GeneratedAudioSaveResult`.
- Minimal `VoiceSynthesisViewModel` status/toast handling per outcome kind.
- Unit tests for service + extended Voice Synthesis library-output tests.
- DI registration via factory after `IContextManager`.

## Explicit non-scope

- GAP-008 / **Slice 46+** / any new `MainWindow*ShellBridge` code.
- RHVoice, `ENGINE_PARITY_MATRIX` edits, backend OpenAPI or shared schema changes.
- Runtime **FULL PASS** or human in-app attestation.
- Pushing the implementation commit to `origin` (per lane plan); prior bundle commit `ce1f0832` was pushed under git guard.

## Files changed (implementation)

- `src/VoiceStudio.App/Services/IGeneratedAudioLibraryService.cs` — `GeneratedAudioSaveKind`, expanded `GeneratedAudioSaveResult`, legacy 2-arg ctor.
- `src/VoiceStudio.App/Services/GeneratedAudioLibraryService.cs` — local file upload, project save, event-only path.
- `src/VoiceStudio.App/Services/AppServices.cs` — `IGeneratedAudioLibraryService` factory registration after `IContextManager`.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — status + toast by `SaveKind`.
- `src/VoiceStudio.App.Tests/Services/GeneratedAudioLibraryServiceTests.cs` — new.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — default mock + EventNotified/ProjectBacked cases.

## Persistence path

1. **Local file** (`AudioReference` or `AudioId` resolving to an existing file, fully qualified or `file://`, not directory): `UploadLibraryAssetAsync` → `AssetAddedEvent` with playback id + file path; `SetCurrentPlayable` / `SetActiveAsset`; if `ActiveProjectId` set → `SaveAudioToProjectAsync` → **`ProjectBacked`**; if no project → **`LibraryBacked`**; if project save throws → **`LibraryBacked`** with message (upload already succeeded).
2. **API / remote reference only:** `AssetAddedEvent` with audio id; **`EventNotified`** (no upload, no project API).

## Local file rules

Align with `VoiceSynthesisViewModel` path resolution: reject `/api/...`, `http(s)://`, non–fully-qualified paths, directories, missing files.

## API-only / event-notified

When no eligible local file: publish event if `AudioId` or `AudioReference` provides a non-empty primary id. Message documents that project-backed durability needs a local generated file.

## Active project

Uses `IContextManager.ActiveProjectId` only; no new project selection UI.

## Saved status semantics (UI)

| SaveKind        | `GeneratedAudioSaveStatus` (typical) |
|-----------------|----------------------------------------|
| ProjectBacked   | Saved to project library               |
| LibraryBacked   | Saved to library (optional message)    |
| EventNotified   | Library notified (or full `Message`)   |
| Failed          | Save failed — …                        |

`Success` is true for the three non-failed kinds. Recent-row **Saved** marker follows existing behavior on `Success`.

## Tests

- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GeneratedAudioLibraryServiceTests|FullyQualifiedName~VoiceSynthesisViewModelTests&FullyQualifiedName~LibraryOutput"` → **25 passed**.

## Verification commands

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (warnings may exist elsewhere).
- `python scripts/run_verification.py` — **Overall: PASS** → `.buildlogs/verification/last_run.json`
- `.\scripts\verify.ps1 -Quick` — **PASS** → `artifacts/verify/20260428_122756/verification_report.md`

## Limitations

- API-only synthesis URLs cannot be uploaded without a local file; user sees **`EventNotified`** with explanatory message.
- Project save failure after successful upload yields **`LibraryBacked`** with partial message, not **`ProjectBacked`**.

## Non-claims

- Not GAP-008; not Slice 46; not `MainWindow*ShellBridge`; not RHVoice; not runtime FULL PASS; no ENGINE_PARITY_MATRIX mutation.
