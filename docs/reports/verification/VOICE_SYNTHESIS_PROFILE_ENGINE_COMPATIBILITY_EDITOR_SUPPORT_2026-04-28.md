# Voice Synthesis — Profile engine compatibility editor support (2026-04-28)

## Scope

Close the **VOICE_SYNTHESIS_PROFILE_ENGINE_COMPATIBILITY_EDITOR_SUPPORT** lane: persist `vs:engines:` compatibility edits from the Profiles detail panel (not only via Edit Profile dialog), unit-test the shared tag helper and `ProfilesViewModel`, register AutomationIds, and verify without claiming GAP-008, backend contract changes, or full runtime PASS.

## Behavior

- **`VoiceProfileEngineCompatibilityTags`** remains the single parser/writer for optional profile tag **`vs:engines:id1,id2`** (ordinal prefix `vs:engines:`).
- **`ProfilesViewModel.SaveCompatibleEnginesCommand`** calls **`UpdateProfileAsync(..., tagsText: null)`** so base tags come from the current profile and **`ReplaceEnginesTag`** merges **`_profileCompatibleEngineIds`**.
- **`ProfilesView`** exposes **Save** plus existing Add / Remove / Clear controls with stable AutomationIds (see registry).

## Files touched (implementation)

- `src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs` — `SaveCompatibleEnginesAsync` + can-execute.
- `src/VoiceStudio.App/Views/Panels/ProfilesView.xaml` / `.xaml.cs` — Compatible engines section + Save control.
- `src/VoiceStudio.App/Core/Models/VoiceProfileEngineCompatibilityTags.cs` — (helper; prior refactor `616e5df9` on `main`).
- `src/VoiceStudio.App.Tests/Models/VoiceProfileEngineCompatibilityTagsTests.cs` — helper unit tests.
- `src/VoiceStudio.App.Tests/ViewModels/ProfilesViewModelTests.cs` — region **Profile engine compatibility editor**; `CreateEngineEditorViewModel` no longer overwrites caller `UpdateAsync` mock setup.
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — Compatible engines AutomationIds for **ProfilesView**.

## Tests

| Area | Filter / notes |
|------|----------------|
| Helper | `FullyQualifiedName~VoiceProfileEngineCompatibilityTagsTests` — **8** tests |
| Profiles VM editor | `FullyQualifiedName~ProfilesViewModelTests.ProfileEngineCompatibility` — **12** tests |
| Combined lane check | `FullyQualifiedName~VoiceSynthesis\|FullyQualifiedName~ProfilesViewModelTests.ProfileEngineCompatibility\|FullyQualifiedName~VoiceProfileEngineCompatibilityTagsTests` — **174** passed, **22** skipped (E2E/UI gating) |

`VoiceSynthesisViewModel` **Profile engine compatibility** tests (e.g. `ProfileEngineCompatibility_KnownCompatible_AllowsSynthesize`, `ProfileEngineCompatibility_UnknownTag_DoesNotBlockSynthesize`) were **not duplicated**; existing coverage retained under the combined filter.

## Build and verification

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (warnings pre-existing in solution tests).
- `python scripts/run_verification.py` — **Overall: PASS** → `.buildlogs/verification/last_run.json`.
- `.\scripts\verify.ps1 -Quick` — **exit 0** — report: `artifacts/verify/20260428_110257/verification_report.md`.

## Explicit non-claims

- **Not** GAP-008 / **not** a new `MainWindow*ShellBridge` / **not** Slice 46+ charter work.
- **Not** RHVoice or `ENGINE_PARITY_MATRIX` updates.
- **Not** a shared OpenAPI / backend contract change for profiles (still `List<string>` tags on existing profile update path).
- **Not** a claim of operator-attested in-app runtime synthesis or **runtime FULL PASS**.
- **Not** full `verify.ps1` (non-Quick); this lane used **`verify.ps1 -Quick`** only.

## Limitations

- Manual UI click-through of Save in the live app is **not** part of this automated proof batch.
