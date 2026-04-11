# GOV-VOICESTUDIO-GAP052-ENGINE-BENCHMARKING-MOS-SIDEBYSIDE-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP052-ENGINE-BENCHMARKING-MOS-SIDEBYSIDE-01 |
| **GAP** | GAP-052 (Engine benchmarking UI — MOS / side-by-side) |
| **Status** | **Closed** |
| **Phase** | Bounded execution row — `QualityBenchmarkView` / `QualityBenchmarkViewModel` |
| **Role** | UI Engineer |

## §1 Objective (frozen)

Extend the **existing** canonical benchmarking surface (`PanelIds.QualityBenchmark`) with a **bounded side-by-side comparison** workflow: same input text and profile across **≥2** selected engines, per-slot **audio playback**, **objective metrics** from `VoiceSynthesisResponse.QualityMetrics`, **subjective 1–5 score** per slot, and **preference winner** selection. Retain the existing **automated** `RunBenchmarkAsync` path unchanged.

## §2 Hard IN

- **Canonical panel**: [`QualityBenchmarkView`](../../src/VoiceStudio.App/Views/Panels/QualityBenchmarkView.xaml) — no new `PanelIds` entry.
- **Engine enumeration**: [`IEnginesClient.GetEnginesAsync`](../../src/VoiceStudio.App/Services/IEnginesClient.cs).
- **Per-engine synthesis (playback-capable)**: [`IVoiceSynthesisService.SynthesizeVoiceAsync`](../../src/VoiceStudio.App/Services/IVoiceSynthesisService.cs) with `VoiceSynthesisRequest` (same `Text`, `ProfileId`, `Engine` per slot).
- **Playback**: [`IAudioPlayerService.PlayBackendAudioIdAsync`](../../src/VoiceStudio.App/Core/Services/IAudioPlayerService.cs) + [`BackendClientConfig.BaseUrl`](../../src/VoiceStudio.App/Core/Services/BackendClientConfig.cs).
- **Automated benchmark**: [`IQualityControlClient.RunBenchmarkAsync`](../../src/VoiceStudio.App/Core/Services/IQualityControlClient.cs) — **retained**; existing checkboxes + results list unchanged.
- **Session-light persistence**: [`UnpackagedSettingsHelper`](../../src/VoiceStudio.App/Helpers/UnpackagedSettingsHelper.cs) for test text + selected comparison engine ids (JSON).
- **Failure honesty**: per-slot success/failure; failed slots show error; successful slots unchanged.
- **Tests**: extend [`QualityBenchmarkViewModelSeamTests`](../../src/VoiceStudio.App.Tests/ViewModels/QualityBenchmarkViewModelSeamTests.cs); add [`Gap052Tests`](../../src/VoiceStudio.App.Tests/Views/Gap052Tests.cs) source seam scans.
- **AutomationIds**: `QualityBenchmarkView_RunComparisonButton`, `QualityBenchmarkView_ComparisonSlots`.
- **Governance**: closure report, tracker, STATE, CANONICAL_REGISTRY, openmemory.

## §3 Hard OUT

- No new panel ID or duplicate benchmarking authority surface.
- No backend `/api/quality/benchmark` contract change required for closure (comparison uses synthesis seam).
- No experiment framework, analytics platform, automatic engine ranking, or bulk reporting.
- No shell redesign; no new background job orchestration beyond existing synthesis calls.

## §4 Authority map

| Concern | Owner |
|--------|--------|
| Engine list | `IEnginesClient` |
| Side-by-side synthesis | `IVoiceSynthesisService` |
| Playback URL | `BackendClientConfig.BaseUrl` |
| Automated batch benchmark | `IQualityControlClient.RunBenchmarkAsync` |
| Session prefs | `UnpackagedSettingsHelper` |
| UI | `QualityBenchmarkView` / `QualityBenchmarkViewModel` |

## §5 Acceptance criteria

- [x] Operator can select **≥2** engines from API-driven list and run **Run comparison** with same profile + test text.
- [x] Comparison slots appear **side-by-side** (horizontal layout); each slot shows metrics (when success), play, subjective score, prefer.
- [x] Failed engine shows explicit error; other slots remain usable.
- [x] `Run benchmark` (legacy) still works.
- [x] `Gap052Tests` (8) + extended `QualityBenchmarkViewModelSeamTests` + full `App.Tests` + `verify.ps1 -Quick` PASS.
- [x] Closure report + tracker + STATE + CANONICAL_REGISTRY + openmemory + execution row **Closed**.

## §6 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap052Tests|FullyQualifiedName~QualityBenchmarkViewModelSeamTests"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
```

## §7 Risk register

| Risk | Mitigation |
|------|------------|
| Large engine list | Cap visible selection / require explicit multi-select (≥2) before run |
| XAML `DataTemplate` command to parent VM | `x:Name` on root `UserControl` + `ElementName` binding |

## §8 Rollback

Revert VM + XAML + tests + docs; no migration.
