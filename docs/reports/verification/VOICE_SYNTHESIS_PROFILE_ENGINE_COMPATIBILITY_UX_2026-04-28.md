# Voice Synthesis profile/engine compatibility UX (2026-04-28)

## Scope

Client-only UX: optional **engine allow-list** encoded on existing `VoiceProfile.Tags` using a single deterministic convention. **No** new backend fields, **no** shared OpenAPI or `shared/` schema edits. Extends `CanSynthesize` so synthesis is blocked only when compatibility is **known** and **incompatible**. Does **not** clear `LastSynthesized*`, recent results, or playback diagnostics when compatibility becomes incompatible.

## Tag rule (deterministic)

- Scan profile tags for the **first** entry whose prefix is exactly **`vs:engines:`** (ordinal, case-sensitive prefix).
- Payload: **`vs:engines:engineA,engineB`** — split on `,`, trim segments; engine id match uses **`StringComparer.OrdinalIgnoreCase`**.
- **No** matching tag, **empty** payload after parse, or tag `vs:engines:` with no ids → **`IsProfileEngineCompatibilityKnown == false`** — no compatibility hard-block (legacy behavior preserved).
- **Single-engine mode** (`UseMultiEngineEnsemble == false`): compatible iff `SelectedEngine` is non-empty and in the allow-list when known.
- **Ensemble mode** (`UseMultiEngineEnsemble == true`): when **`SelectedEngines`** is non-empty, compatible iff **every** selected engine is in the allow-list. When ensemble is on but **`SelectedEngines`** is empty, compatibility is treated as **unknown** (no new silent fallback; does not add a hard-block).

## Files changed

- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` — `ProfileEngineCompatibilityStatus`, `TryParseEnginesAllowList`, `RefreshProfileEngineCompatibility`, `CompatibleProfilesForSelectedEngine`, `SelectFirstCompatibleProfileCommand`, extended `CanSynthesize`, `SelectedEngines.CollectionChanged` subscription.
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` — summary `TextBlock`, optional **Use first compatible profile** button, incompatible-only `InfoBar`, `InverseBooleanToVisibilityConverter` resource for stream toggle; new grid row; `Grid.RowSpan` **11** on overlays.
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — **`#region Profile engine compatibility`** with **10** scenarios.
- `docs/developer/AUTOMATION_ID_REGISTRY.md` — `VoiceSynthesisView_ProfileEngineCompatibilitySummary`, `VoiceSynthesisView_SelectFirstCompatibleProfileButton`, `VoiceSynthesisView_ProfileEngineCompatibilityInfoBar`.

## Behavior

- **`ProfileEngineCompatibilityMessage`**: always includes current engine label and profile name; unknown state explains that profile metadata does not restrict engines.
- **`IsProfileEngineCompatibilityInfoBarOpen`**: open only when known and incompatible (separate `AutomationId` and severity from consent `InfoBar`).
- **`SelectFirstCompatibleProfileCommand`**: selects the first profile in `Profiles` order that satisfies `ProfileMatchesCurrentEngineSelection`; `CanExecute` when `HasCompatibleProfilesForSelectedEngine`.

## Test results

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — **0 errors** (warnings may exist outside touched scope).
- `dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis"` — **154 passed** (**22** skipped UI/E2E patterns).
- `python scripts/run_verification.py` — **Overall: PASS** (`.buildlogs/verification/last_run.json`).
- `.\scripts\verify.ps1 -Quick` — **exit 0** — `artifacts/verify/20260428_042020/verification_report.md` (local run used `VOICESTUDIO_ALLOW_REPO_RUNTIME=1` when `installer/runtime` is non-empty).

## Limitations

- Convention is **opt-in** via tags; profiles without the tag behave as before.
- Only the **first** `vs:engines:` tag is interpreted; duplicate or conflicting tags are not merged.

## Non-claims

- **Not** GAP-008; **not** Slice 46; **not** any new `MainWindow*ShellBridge`.
- **Not** RHVoice; **not** `ENGINE_PARITY_MATRIX` edits.
- **Not** a **runtime FULL PASS** or human in-app attestation.
- **Not** backend or shared-contract changes for engine allow-lists.
