# VOICE_SYNTHESIS_COMPATIBLE_PROFILE_PICKER_UX — 2026-04-28

**Type:** Report (bounded product lane)  
**Status:** Landed (local commit; see git)  
**Scope:** WinUI Voice Synthesis panel — profile ComboBox filters by `vs:engines:` compatibility; optional “compatible only” strict mode.

## Scope

- **In scope:** `VoiceSynthesisViewModel` picker state (`ProfilePickerProfiles`, `ShowCompatibleProfilesOnly`, counts, `ProfilePickerSummary`, `HasProfilePickerMatches`); `VoiceSynthesisView.xaml` — summary line + `ToggleSwitch`; `ProfilePickerProfiles` as `ComboBox.ItemsSource`; `Profiles.CollectionChanged` → recompute; selection alignment when the current profile is excluded by the active filter; headless unit tests; AutomationIds in registry.
- **Out of scope:** GAP-008 / `MainWindow*ShellBridge`; RHVoice; `ENGINE_PARITY_MATRIX.md`; backend API or shared schema changes; Profiles panel editor (separate prior lane); runtime / in-app human verification; claiming **full** `verify.ps1` (non-Quick) or end-to-end **FULL PASS**.

## Behavior semantics

| Mode | Picker includes |
|------|-----------------|
| **Default** (`ShowCompatibleProfilesOnly` = false) | Known-**compatible** + **unrestricted/unknown** (no valid `vs:engines:` tag, or ensemble selection ambiguous). **Excludes** known-**incompatible** tags. |
| **Compatible only** (true) | Only profiles with a **matching** `vs:engines:` allow-list for the current engine (or ensemble) selection. **Unrestricted** profiles are **hidden**. |

- If the current selection is not in the filtered list and `Profiles` is non-empty, selection moves to the **first** item in `ProfilePickerProfiles`, or is cleared if the filtered list is empty.
- If `Profiles` is **empty** (e.g. unit tests with orphan `SelectedProfile`), **alignment is skipped** so headless tests that do not populate `Profiles` still validate compatibility messaging.

## Changed files (summary)

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — picker state, `RebuildProfilePickerListsAndCounts`, `AlignSelectedProfileWithPickerFilter`, `Profiles.CollectionChanged`.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` — bindings + toggle + summary.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — picker tests; `SelectFirstCompatible` test adjusted.
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — new AutomationIds.

## Tests

- `dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"` → **159 passed**, 22 skipped (WinUI integration tests).

## Verification

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (pre-existing warnings elsewhere).
- `python scripts/run_verification.py` — **Overall: PASS** (`.buildlogs/verification/last_run.json`).
- `.\scripts\verify.ps1 -Quick` — **PASS** — report `artifacts/verify/20260428_112648/verification_report.md`.

## Explicit non-claims

- No operator-attested in-app synthesis/playback for this lane.
- No change to engine manifests, backend routes, or `shared/` contracts.
- Quick verification only; full `verify.ps1` not asserted here.

## Related

- Prior compatibility UX: `VOICE_SYNTHESIS_PROFILE_ENGINE_COMPATIBILITY_UX_2026-04-28.md`
- Editor support: `VOICE_SYNTHESIS_PROFILE_ENGINE_COMPATIBILITY_EDITOR_SUPPORT_2026-04-28.md`
